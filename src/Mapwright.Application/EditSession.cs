using Mapwright.Domain;

namespace Mapwright.Application;

public sealed class EditSession : IAsyncDisposable
{
    public static readonly TimeSpan AutosaveCadence = TimeSpan.FromSeconds(30);

    private readonly IProjectRepository _repository;
    private readonly IRenderInvalidationQueue _renderQueue;
    private readonly object _queueGate = new();
    private Task _queueTail = Task.CompletedTask;
    private MapProject _current;
    private SaveQueueState _saveState;
    private int _queuedCommands;
    private long _revisionEventSequence;
    private readonly IConnectionHold? _connectionHold;
    private bool _disposed;

    public EditSession(
        MapProject initial,
        IProjectRepository repository,
        IRenderInvalidationQueue renderQueue)
    {
        _current = initial;
        _repository = repository;
        _renderQueue = renderQueue;
        _saveState = SaveQueueState.InitiallySaved(initial.Revision);
        _connectionHold = (repository as IConnectionHoldingRepository)?.HoldConnection();
    }

    public MapProject Current => Volatile.Read(ref _current);

    /// <summary>Completes when durable storage is open and ready for the first edit.</summary>
    public Task Prepared => _connectionHold?.Prepared ?? Task.CompletedTask;

    public SaveQueueState SaveState => Volatile.Read(ref _saveState);

    public event Action<SaveQueueState>? SaveStateChanged;

    public event Action<RevisionTimelineEvent>? RevisionPublished;

    public Task<EditAcknowledgement> ExecuteAsync(
        IEditCommand command,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(command, prepared: null, cancellationToken);

    /// <summary>
    /// <paramref name="prepared"/> receives the single projected change after
    /// <c>Apply</c> and before the durable commit starts. It may start private,
    /// disposable work only: nothing derived from it may be presented until the
    /// returned acknowledgement matches the command and revision. It is not
    /// invoked for no-op commands, and its failures cannot affect the commit.
    /// </summary>
    public Task<EditAcknowledgement> ExecuteAsync(
        IEditCommand command,
        Action<PreparedEdit>? prepared,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ObjectDisposedException.ThrowIf(_disposed, this);
        Interlocked.Increment(ref _queuedCommands);
        PublishState(new SaveQueueState(
            SaveQueuePhase.Queued,
            Volatile.Read(ref _queuedCommands),
            SaveState.DurableRevision,
            SaveState.SavedAt,
            null));
        return EnqueueAsync(() => ExecuteCoreAsync(command, prepared, cancellationToken), commandOperation: true);
    }

    /// <summary>
    /// Runs representative commands' pure <c>Apply</c> and, when the repository
    /// supports it, payload serialization, then discards every result. Nothing is
    /// committed, published or rendered; it only moves one-time code preparation
    /// ahead of the first real edit. Failures are ignored. Returns elapsed ms.
    /// </summary>
    public Task<double> RehearseAsync(IReadOnlyList<IEditCommand> commands) => Task.Run(() =>
    {
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        var project = Current;
        foreach (var command in commands)
        {
            try
            {
                if (command.Apply(project).IsNoOp) continue;
                (_repository as ICommandRehearsal)?.RehearseSerialization(command);
            }
            catch (Exception exception)
            {
                System.Diagnostics.Debug.WriteLine($"Edit rehearsal skipped {command.GetType().Name}: {exception.Message}");
            }
        }
        return System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
    });

    public Task<SaveAcknowledgement> SaveAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return EnqueueAsync(() => SaveCoreAsync(cancellationToken), commandOperation: false);
    }

    private async Task<EditAcknowledgement> ExecuteCoreAsync(
        IEditCommand command,
        Action<PreparedEdit>? prepared,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var previous = Current;
        var change = command.Apply(previous);
        if (change.IsNoOp)
            return new EditAcknowledgement(command.Id, previous.Revision, DateTimeOffset.UtcNow,
                change.Invalidation);

        if (prepared is not null)
        {
            try { prepared(new PreparedEdit(command.Id, previous.Revision, change)); }
            catch (Exception exception)
            {
                System.Diagnostics.Debug.WriteLine($"Prepared-edit observer failed before commit: {exception}");
            }
        }

        var committed = await _repository.CommitAsync(previous, command, change, cancellationToken)
            .ConfigureAwait(false);
        if (committed.CommandId != command.Id || committed.Revision != change.Project.Revision)
            throw new InvalidDataException("Repository acknowledged a different command or revision.");

        Volatile.Write(ref _current, change.Project);
        _renderQueue.Enqueue(previous.ProjectId, committed.Revision, command.Id, change.Invalidation);
        var publishedAt = DateTimeOffset.UtcNow;
        RevisionPublished?.Invoke(new RevisionTimelineEvent(
            Interlocked.Increment(ref _revisionEventSequence),
            command.GetType().Name,
            command.Id,
            previous.Revision,
            committed.Revision,
            startedAt,
            committed.CommittedAt,
            publishedAt,
            CountTiles(change.Invalidation.Bounds)));
        PublishState(new SaveQueueState(
            SaveQueuePhase.Queued,
            Volatile.Read(ref _queuedCommands),
            committed.Revision,
            committed.CommittedAt,
            null));
        return new EditAcknowledgement(command.Id, committed.Revision, committed.CommittedAt,
            change.Invalidation);
    }

    private async Task<SaveAcknowledgement> SaveCoreAsync(CancellationToken cancellationToken)
    {
        var current = Current;
        PublishState(new SaveQueueState(
            SaveQueuePhase.Saving,
            Volatile.Read(ref _queuedCommands),
            SaveState.DurableRevision,
            SaveState.SavedAt,
            null));
        var durable = await _repository.LoadAsync(current.ProjectId, cancellationToken).ConfigureAwait(false);
        if (durable.Revision != current.Revision)
            throw new RevisionConflictException(current.Revision, durable.Revision);
        var verifiedAt = DateTimeOffset.UtcNow;
        PublishState(new SaveQueueState(SaveQueuePhase.Saved, 0, current.Revision, verifiedAt, null));
        return new SaveAcknowledgement(current.Revision, verifiedAt);
    }

    private Task<T> EnqueueAsync<T>(Func<Task<T>> operation, bool commandOperation)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_queueGate)
        {
            var predecessor = _queueTail;
            _queueTail = RunQueuedAsync(predecessor, operation, completion, commandOperation);
        }
        return completion.Task;
    }

    private async Task RunQueuedAsync<T>(
        Task predecessor,
        Func<Task<T>> operation,
        TaskCompletionSource<T> completion,
        bool commandOperation)
    {
        try
        {
            await predecessor.ConfigureAwait(false);
            var result = await operation().ConfigureAwait(false);
            completion.TrySetResult(result);
        }
        catch (OperationCanceledException exception)
        {
            completion.TrySetCanceled(exception.CancellationToken);
            PublishFailure(exception);
        }
        catch (Exception exception)
        {
            completion.TrySetException(exception);
            PublishFailure(exception);
        }
        finally
        {
            if (commandOperation)
            {
                var remaining = Interlocked.Decrement(ref _queuedCommands);
                var state = SaveState;
                if (state.Phase != SaveQueuePhase.Failed)
                {
                    PublishState(new SaveQueueState(
                        remaining == 0 ? SaveQueuePhase.Saved : SaveQueuePhase.Queued,
                        remaining,
                        state.DurableRevision,
                        remaining == 0 ? state.SavedAt : null,
                        null));
                }
            }
        }
    }

    private void PublishFailure(Exception exception)
    {
        var state = SaveState;
        PublishState(new SaveQueueState(
            SaveQueuePhase.Failed,
            Volatile.Read(ref _queuedCommands),
            state.DurableRevision,
            state.SavedAt,
            exception.Message));
    }

    private void PublishState(SaveQueueState state)
    {
        Volatile.Write(ref _saveState, state);
        SaveStateChanged?.Invoke(state);
    }

    private static int CountTiles(MapBounds bounds)
    {
        const double tileSize = 512;
        var width = Math.Max(0, bounds.Right - bounds.Left);
        var height = Math.Max(0, bounds.Bottom - bounds.Top);
        if (width == 0 && height == 0) return 0;
        return Math.Max(1, (int)Math.Ceiling(width / tileSize)) *
               Math.Max(1, (int)Math.Ceiling(height / tileSize));
    }

    public async ValueTask DisposeAsync()
    {
        Task tail;
        lock (_queueGate)
        {
            if (_disposed) return;
            _disposed = true;
            tail = _queueTail;
        }
        try { await tail.ConfigureAwait(false); }
        finally
        {
            if (_connectionHold is not null) await _connectionHold.DisposeAsync().ConfigureAwait(false);
        }
    }
}

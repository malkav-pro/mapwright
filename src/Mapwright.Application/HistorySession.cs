using System.Collections.Immutable;
using Mapwright.Domain;

namespace Mapwright.Application;

public sealed record HistoryNavigationResult(
    long PreviousRevision,
    long Revision,
    DateTimeOffset CommittedAt,
    bool Reconstructed,
    int TilesRestored,
    bool CompatibilityMismatchRebuilt)
{
    public TileInvalidation? Invalidation { get; init; }
}

public sealed record HistoryReconstructionState(
    bool IsRunning,
    long CurrentRevision,
    long? TargetRevision,
    int CompletedCommands,
    int TotalCommands,
    bool CanCancel);

public sealed class HistorySession : IAsyncDisposable
{
    private readonly IProjectRepository _repository;
    private readonly IRenderInvalidationQueue _renderQueue;
    private readonly SemaphoreSlim _navigationLock = new(1, 1);
    private readonly object _residentGate = new();
    private readonly LinkedList<HistoryResidentDelta> _residentDeltas = [];
    private readonly Dictionary<long, CachedResidentSnapshot> _residentSnapshots = [];
    private MapProject _current;
    private HistoryReconstructionState _reconstructionState;
    private CancellationTokenSource? _activeReconstruction;
    private int _residentTileCount;
    private long _revisionEventSequence;
    private readonly IConnectionHold? _connectionHold;
    private bool _disposed;

    public HistorySession(
        MapProject initial,
        IProjectRepository repository,
        IRenderInvalidationQueue renderQueue)
    {
        _current = initial;
        _repository = repository;
        _connectionHold = (repository as IConnectionHoldingRepository)?.HoldConnection();
        _renderQueue = renderQueue;
        _reconstructionState = new HistoryReconstructionState(false, initial.Revision, null, 0, 0, false);
    }

    public MapProject Current => Volatile.Read(ref _current);

    /// <summary>Completes when durable storage is open and ready for the first navigation.</summary>
    public Task Prepared => _connectionHold?.Prepared ?? Task.CompletedTask;

    public HistoryReconstructionState ReconstructionState => Volatile.Read(ref _reconstructionState);

    public int ResidentTileCount => Volatile.Read(ref _residentTileCount);

    public CancellationToken ReconstructionCancellationToken =>
        Volatile.Read(ref _activeReconstruction)?.Token ?? CancellationToken.None;

    public event Action<RevisionTimelineEvent>? RevisionPublished;

    public bool RememberResidentDelta(HistoryResidentDelta delta)
    {
        delta.Validate();
        if (delta.Tiles.Length > 16) return false;
        lock (_residentGate)
        {
            var currentCompatibility = HistoryCompatibility.For(Current);
            if (delta.Compatibility != currentCompatibility) return false;
            for (var node = _residentDeltas.First; node is not null;)
            {
                var next = node.Next;
                if (node.Value.BeforeRevision == delta.BeforeRevision &&
                    node.Value.AfterRevision == delta.AfterRevision)
                {
                    _residentTileCount -= node.Value.Tiles.Length;
                    _residentDeltas.Remove(node);
                }
                node = next;
            }
            _residentDeltas.AddLast(delta);
            _residentTileCount += delta.Tiles.Length;
            while (_residentTileCount > 16 && _residentDeltas.First is { } oldest)
            {
                _residentTileCount -= oldest.Value.Tiles.Length;
                _residentDeltas.RemoveFirst();
            }
            return _residentDeltas.Contains(delta);
        }
    }

    public async Task<HistoryState> ReadStateAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return await _repository.ReadHistoryStateAsync(Current.ProjectId, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<HistoryWindow> ReadWindowAsync(
        int offset,
        int limit,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return await _repository.ReadHistoryWindowAsync(Current.ProjectId, offset, limit, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<HistoryControlState> ReadControlsAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var state = await _repository.ReadHistoryStateAsync(Current.ProjectId, cancellationToken)
            .ConfigureAwait(false);
        var undoEntry = state.CanUndo
            ? await _repository.ReadHistoryEntryAsync(Current.ProjectId, state.CursorRevision, cancellationToken)
                .ConfigureAwait(false)
            : null;
        var redoEntry = state.CanRedo
            ? await _repository.ReadHistoryEntryAsync(Current.ProjectId, state.CursorRevision + 1, cancellationToken)
                .ConfigureAwait(false)
            : null;
        var undo = undoEntry is null
            ? new HistoryActionAvailability(false, null, null, "No committed action to undo.")
            : new HistoryActionAvailability(true, undoEntry.Label, undoEntry.Revision,
                $"Undo {undoEntry.Label}.");
        var redo = redoEntry is null
            ? new HistoryActionAvailability(false, null, null, "No committed action to redo.")
            : new HistoryActionAvailability(true, redoEntry.Label, redoEntry.Revision,
                $"Redo {redoEntry.Label}.");
        return new HistoryControlState(undo, redo);
    }

    public bool CancelReconstruction()
    {
        var active = Volatile.Read(ref _activeReconstruction);
        if (active is null || active.IsCancellationRequested) return false;
        active.Cancel();
        return true;
    }

    public async Task<HistoryNavigationResult> UndoAsync(CancellationToken cancellationToken = default)
    {
        var state = await ReadStateAsync(cancellationToken).ConfigureAwait(false);
        if (!state.CanUndo) throw new InvalidOperationException("Nothing to undo.");
        return await MoveToRevisionAsync(state.CursorRevision - 1, cancellationToken).ConfigureAwait(false);
    }

    public async Task<HistoryNavigationResult> RedoAsync(CancellationToken cancellationToken = default)
    {
        var state = await ReadStateAsync(cancellationToken).ConfigureAwait(false);
        if (!state.CanRedo) throw new InvalidOperationException("Nothing to redo.");
        return await MoveToRevisionAsync(state.CursorRevision + 1, cancellationToken).ConfigureAwait(false);
    }

    public async Task<HistoryNavigationResult> MoveToRevisionAsync(
        long targetRevision,
        CancellationToken cancellationToken = default)
    {
        return await ReconstructAsync(targetRevision, HistoryCompatibility.For(Current), null,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<HistoryNavigationResult> ReconstructAsync(
        long targetRevision,
        HistoryCompatibility compatibility,
        IProgress<HistoryReconstructionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        compatibility.Validate();
        await _navigationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Volatile.Write(ref _activeReconstruction, linkedCancellation);
        try
        {
            var startedAt = DateTimeOffset.UtcNow;
            var previous = Current;
            var state = await _repository.ReadHistoryStateAsync(previous.ProjectId, linkedCancellation.Token)
                .ConfigureAwait(false);
            if (state.CursorRevision != previous.Revision)
                throw new RevisionConflictException(previous.Revision, state.CursorRevision);
            if (targetRevision == previous.Revision)
                return new HistoryNavigationResult(previous.Revision, previous.Revision,
                    DateTimeOffset.UtcNow, false, 0, false);
            if (targetRevision < state.BaselineRevision || targetRevision > state.LatestRevision)
                throw new ArgumentOutOfRangeException(nameof(targetRevision));

            var resident = FindResidentDelta(previous.Revision, targetRevision, compatibility);
            Volatile.Write(ref _reconstructionState, new HistoryReconstructionState(
                true, previous.Revision, targetRevision, 0, 0, true));
            var reporting = new ProgressRelay(update =>
            {
                Volatile.Write(ref _reconstructionState, new HistoryReconstructionState(
                    true, previous.Revision, targetRevision, update.CompletedCommands,
                    update.TotalCommands, true));
                progress?.Report(update);
            });

            // Resident pixels alone cannot restore the document model. Reuse only a previously
            // reconstructed adjacent model whose durable command identity is still unchanged.
            var previousIdentity = resident is null ? null : await RevisionIdentityAsync(
                previous.ProjectId, previous.Revision, state.BaselineRevision, linkedCancellation.Token)
                .ConfigureAwait(false);
            var targetIdentity = resident is null ? null : await RevisionIdentityAsync(
                previous.ProjectId, targetRevision, state.BaselineRevision, linkedCancellation.Token)
                .ConfigureAwait(false);
            var cached = resident is not null && _residentSnapshots.TryGetValue(targetRevision, out var candidate) &&
                candidate.Identity == targetIdentity && candidate.BaselineRevision == state.BaselineRevision
                    ? candidate
                    : null;
            HistoryReconstructionResult? reconstruction = null;
            MapProject restored;
            if (cached is not null)
            {
                restored = cached.Project;
                reporting.Report(new HistoryReconstructionProgress(0, 0, targetRevision));
            }
            else if (resident is not null && targetRevision == state.BaselineRevision)
            {
                // The baseline has an authoritative snapshot; no command replay or
                // redundant checkpoint deserialization is required for adjacent undo.
                restored = await _repository.LoadResidentRevisionAsync(previous.ProjectId, targetRevision,
                    previous, linkedCancellation.Token).ConfigureAwait(false);
                reporting.Report(new HistoryReconstructionProgress(0, 0, targetRevision));
            }
            else
            {
                // Nothing visible or durable changes if a full reconstruction is cancelled.
                reconstruction = await _repository.ReconstructRevisionAsync(previous.ProjectId, targetRevision,
                    compatibility, reporting, linkedCancellation.Token).ConfigureAwait(false);
                restored = reconstruction.Project;
            }
            linkedCancellation.Token.ThrowIfCancellationRequested();

            // Once the transaction starts it is allowed to finish; publishing follows the durable cursor commit.
            var transition = await _repository.MoveHistoryCursorAsync(previous.ProjectId, previous.Revision,
                targetRevision, CancellationToken.None).ConfigureAwait(false);
            if (resident is not null)
            {
                _residentSnapshots[previous.Revision] = new CachedResidentSnapshot(
                    previous, previousIdentity, state.BaselineRevision);
                _residentSnapshots[targetRevision] = new CachedResidentSnapshot(
                    restored, targetIdentity, state.BaselineRevision);
                if (_residentSnapshots.Count > 2)
                    foreach (var obsolete in _residentSnapshots.Keys
                                 .Where(revision => revision != previous.Revision && revision != targetRevision)
                                 .ToArray())
                        _residentSnapshots.Remove(obsolete);
            }
            Volatile.Write(ref _current, restored);
            var invalidation = ConservativeInvalidation(previous, restored);
            _renderQueue.Enqueue(previous.ProjectId, restored.Revision, transition.TransitionId,
                invalidation);
            var publishedAt = DateTimeOffset.UtcNow;
            RevisionPublished?.Invoke(new RevisionTimelineEvent(
                Interlocked.Increment(ref _revisionEventSequence),
                targetRevision < previous.Revision ? "Undo" : "Redo",
                transition.TransitionId,
                previous.Revision,
                restored.Revision,
                startedAt,
                transition.CommittedAt,
                publishedAt,
                resident?.Tiles.Length ?? 0));
            return new HistoryNavigationResult(previous.Revision, restored.Revision,
                transition.CommittedAt, resident is null, resident?.Tiles.Length ?? 0,
                reconstruction?.CompatibilityMismatchRebuilt ?? false)
            {
                Invalidation = invalidation
            };
        }
        finally
        {
            Volatile.Write(ref _activeReconstruction, null);
            var revision = Current.Revision;
            Volatile.Write(ref _reconstructionState,
                new HistoryReconstructionState(false, revision, null, 0, 0, false));
            _navigationLock.Release();
        }
    }

    private HistoryResidentDelta? FindResidentDelta(
        long currentRevision,
        long targetRevision,
        HistoryCompatibility compatibility)
    {
        lock (_residentGate)
        {
            for (var node = _residentDeltas.Last; node is not null; node = node.Previous)
            {
                var delta = node.Value;
                if (delta.Compatibility != compatibility) continue;
                if ((delta.BeforeRevision == currentRevision && delta.AfterRevision == targetRevision) ||
                    (delta.AfterRevision == currentRevision && delta.BeforeRevision == targetRevision))
                    return delta;
            }
            return null;
        }
    }

    private async Task<CommandId?> RevisionIdentityAsync(ProjectId projectId, long revision,
        long baselineRevision, CancellationToken cancellationToken)
    {
        if (revision == baselineRevision) return null;
        var entry = await _repository.ReadHistoryEntryAsync(projectId, revision, cancellationToken)
            .ConfigureAwait(false);
        return entry?.CommandId ?? throw new InvalidDataException(
            $"History revision {revision} has no durable command identity.");
    }

    private sealed record CachedResidentSnapshot(
        MapProject Project, CommandId? Identity, long BaselineRevision);

    private static TileInvalidation FullInvalidation(MapProject previous, MapProject restored) => new(
        previous.TerrainLayers.Select(layer => layer.Id)
            .Concat(restored.TerrainLayers.Select(layer => layer.Id))
            .ToImmutableHashSet(),
        new MapBounds(0, 0, Math.Max(previous.Width, restored.Width), Math.Max(previous.Height, restored.Height)),
        true,
        true,
        true);

    private static TileInvalidation ConservativeInvalidation(MapProject previous, MapProject restored)
    {
        var full = FullInvalidation(previous, restored);
        if (previous.ProjectId != restored.ProjectId || previous.MapId != restored.MapId ||
            previous.Width != restored.Width || previous.Height != restored.Height ||
            previous.Title != restored.Title || previous.StorageFormatVersion != restored.StorageFormatVersion ||
            previous.ImportedSource != restored.ImportedSource || previous.River != restored.River ||
            previous.TerrainLayers.Length != restored.TerrainLayers.Length)
            return full;

        PaintStroke? changed = null;
        LayerId changedLayer = default;
        foreach (var before in previous.TerrainLayers)
        {
            var after = restored.TerrainLayers.FirstOrDefault(layer => layer.Id == before.Id);
            if (after is null || before.Name != after.Name || before.Role != after.Role ||
                before.Visible != after.Visible || before.Locked != after.Locked ||
                before.Opacity != after.Opacity || before.Coastline != after.Coastline ||
                before.SourceBlobHash != after.SourceBlobHash || before.Solo != after.Solo ||
                before.CoverageSourceBlobHash != after.CoverageSourceBlobHash ||
                !before.ResolvedTextureStrokes.SequenceEqual(after.ResolvedTextureStrokes) ||
                !before.ResolvedLandStrokes.SequenceEqual(after.ResolvedLandStrokes))
                return full;

            var removed = before.Strokes.ExceptBy(after.Strokes.Select(stroke => stroke.Id),
                stroke => stroke.Id).ToArray();
            var added = after.Strokes.ExceptBy(before.Strokes.Select(stroke => stroke.Id),
                stroke => stroke.Id).ToArray();
            if (removed.Length + added.Length == 0)
            {
                if (!before.Strokes.SequenceEqual(after.Strokes)) return full;
                continue;
            }
            if (removed.Length + added.Length != 1 || changed is not null ||
                before.Strokes.Length + added.Length - removed.Length != after.Strokes.Length)
                return full;
            var oldShared = before.Strokes.Where(stroke => stroke.Id != removed.FirstOrDefault()?.Id).ToArray();
            var newShared = after.Strokes.Where(stroke => stroke.Id != added.FirstOrDefault()?.Id).ToArray();
            if (!oldShared.SequenceEqual(newShared)) return full;
            changed = removed.Length == 1 ? removed[0] : added[0];
            changedLayer = before.Id;
        }
        if (changed is null) return full;
        var coverage = changed.Kind == TerrainStrokeKind.Coverage;
        return new TileInvalidation(ImmutableHashSet.Create(changedLayer), changed.Bounds,
            coverage, coverage, true);
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed) return ValueTask.CompletedTask;
        _disposed = true;
        _navigationLock.Dispose();
        return _connectionHold?.DisposeAsync() ?? ValueTask.CompletedTask;
    }

    private sealed class ProgressRelay(Action<HistoryReconstructionProgress> report)
        : IProgress<HistoryReconstructionProgress>
    {
        public void Report(HistoryReconstructionProgress value) => report(value);
    }
}

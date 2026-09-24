using System.Collections.Immutable;
using Mapwright.Domain;

namespace Mapwright.Application;

public sealed record DurableCommit(
    CommandId CommandId,
    long Revision,
    DateTimeOffset CommittedAt);

public sealed record HistoryState(
    ProjectId ProjectId,
    long BaselineRevision,
    long CursorRevision,
    long LatestRevision)
{
    public bool CanUndo => CursorRevision > BaselineRevision;
    public bool CanRedo => CursorRevision < LatestRevision;
}

public sealed record HistoryCursorCommit(
    CommandId TransitionId,
    long PreviousRevision,
    long CursorRevision,
    DateTimeOffset CommittedAt);

public sealed record HistoryEntry(
    long Sequence,
    CommandId CommandId,
    long BaseRevision,
    long Revision,
    string CommandType,
    string Label,
    DateTimeOffset CommittedAt,
    int TileCount,
    long DeltaBytes);

public sealed record HistoryWindow(
    int Offset,
    int TotalCount,
    HistoryState State,
    IReadOnlyList<HistoryEntry> Entries);

public sealed record HistoryActionAvailability(
    bool Enabled,
    string? Label,
    long? Revision,
    string Reason);

public sealed record HistoryControlState(
    HistoryActionAvailability Undo,
    HistoryActionAvailability Redo);

public sealed record RevisionTimelineEvent(
    long Sequence,
    string Action,
    CommandId OperationId,
    long FromRevision,
    long ToRevision,
    DateTimeOffset StartedAt,
    DateTimeOffset DurableAt,
    DateTimeOffset PublishedAt,
    int TileCount);

public sealed record HistoryCompatibility(
    string RendererVersion,
    int RecipeVersion,
    string SourceIdentity)
{
    public static HistoryCompatibility For(MapProject project) => new(
        "history-replay-v1",
        1,
        project.ImportedSource?.SourceSha256 ?? project.ProjectId.Value.ToString("D"));

    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(RendererVersion);
        if (RecipeVersion <= 0) throw new ArgumentOutOfRangeException(nameof(RecipeVersion));
        ArgumentException.ThrowIfNullOrWhiteSpace(SourceIdentity);
    }
}

public sealed record HistoryTileDelta(
    int TileX,
    int TileY,
    ImmutableArray<byte> CompressedBefore,
    ImmutableArray<byte> CompressedAfter)
{
    public long CompressedBytes => CompressedBefore.Length + CompressedAfter.Length;

    public void Validate()
    {
        if (TileX < 0) throw new ArgumentOutOfRangeException(nameof(TileX));
        if (TileY < 0) throw new ArgumentOutOfRangeException(nameof(TileY));
        if (CompressedBefore.IsDefaultOrEmpty)
            throw new ArgumentException("A resident delta requires compressed before bytes.");
        if (CompressedAfter.IsDefaultOrEmpty)
            throw new ArgumentException("A resident delta requires compressed after bytes.");
    }
}

public sealed record HistoryResidentDelta(
    long BeforeRevision,
    long AfterRevision,
    HistoryCompatibility Compatibility,
    ImmutableArray<HistoryTileDelta> Tiles)
{
    public long CompressedBytes => Tiles.Sum(tile => tile.CompressedBytes);

    public void Validate()
    {
        if (AfterRevision != BeforeRevision + 1)
            throw new ArgumentException("Resident deltas must describe one adjacent history step.");
        Compatibility.Validate();
        if (Tiles.IsDefaultOrEmpty) throw new ArgumentException("A resident delta requires tiles.");
        foreach (var tile in Tiles) tile.Validate();
    }
}

public sealed record HistoryReconstructionProgress(
    int CompletedCommands,
    int TotalCommands,
    long TargetRevision);

public sealed record HistoryReconstructionResult(
    MapProject Project,
    int CommandsReplayed,
    bool UsedCheckpoint,
    bool CompatibilityMismatchRebuilt);

public interface IProjectRepository
{
    Task<MapProject> LoadAsync(ProjectId projectId, CancellationToken cancellationToken);

    Task<DurableCommit> CommitAsync(
        MapProject previous,
        IEditCommand command,
        DocumentChange change,
        CancellationToken cancellationToken);

    Task<MapProject> LoadRevisionAsync(
        ProjectId projectId,
        long revision,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("This repository does not provide durable revision history.");

    Task<MapProject> LoadResidentRevisionAsync(
        ProjectId projectId,
        long revision,
        MapProject verifiedCurrent,
        CancellationToken cancellationToken = default) =>
        LoadRevisionAsync(projectId, revision, cancellationToken);

    Task<HistoryState> ReadHistoryStateAsync(
        ProjectId projectId,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("This repository does not provide durable cursor state.");

    Task<HistoryCursorCommit> MoveHistoryCursorAsync(
        ProjectId projectId,
        long expectedCursorRevision,
        long targetRevision,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("This repository does not provide durable cursor transitions.");

    Task<IReadOnlyList<HistoryEntry>> ReadHistoryPageAsync(
        ProjectId projectId,
        int offset,
        int limit,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("This repository does not provide history paging.");

    Task<HistoryReconstructionResult> ReconstructRevisionAsync(
        ProjectId projectId,
        long revision,
        HistoryCompatibility compatibility,
        IProgress<HistoryReconstructionProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("This repository does not provide command reconstruction.");

    Task DeleteHistoryAccelerationAsync(
        ProjectId projectId,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("This repository does not provide disposable cache deletion.");

    Task<HistoryWindow> ReadHistoryWindowAsync(
        ProjectId projectId,
        int offset,
        int limit,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("This repository does not provide virtualized history windows.");

    Task<HistoryEntry?> ReadHistoryEntryAsync(
        ProjectId projectId,
        long revision,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("This repository does not provide revision-labelled actions.");
}

public interface IRenderInvalidationQueue
{
    void Enqueue(ProjectId projectId, long revision, CommandId commandId, TileInvalidation invalidation);
}

public interface IApplicationClock
{
    DateTimeOffset UtcNow { get; }
}

public sealed record EditAcknowledgement(
    CommandId CommandId,
    long Revision,
    DateTimeOffset CommittedAt,
    TileInvalidation Invalidation);

/// <summary>
/// Optional repository support for keeping durable storage open for a session's
/// lifetime. Holding never changes what is durable or when a commit is acknowledged.
/// </summary>
public interface IConnectionHoldingRepository
{
    IConnectionHold HoldConnection();
}

/// <summary>A session's hold on durable storage.</summary>
public interface IConnectionHold : IAsyncDisposable
{
    /// <summary>
    /// Completes when session storage is open and ready for a first edit. It never
    /// fails; a preparation problem is simply paid by the first real operation.
    /// </summary>
    Task Prepared { get; }
}

/// <summary>Optional repository support for discarding a payload serialization.</summary>
public interface ICommandRehearsal
{
    void RehearseSerialization(IEditCommand command);
}

/// <summary>A projected, not yet durable, change. Never an acknowledgement.</summary>
public sealed record PreparedEdit(
    CommandId CommandId,
    long BaseRevision,
    DocumentChange Change);

public sealed record SaveAcknowledgement(long Revision, DateTimeOffset VerifiedAt);

public enum SaveQueuePhase
{
    Saved = 0,
    Queued = 1,
    Saving = 2,
    Failed = 3
}

public sealed record SaveQueueState(
    SaveQueuePhase Phase,
    int QueuedCommands,
    long DurableRevision,
    DateTimeOffset? SavedAt,
    string? Failure)
{
    public static SaveQueueState InitiallySaved(long revision) =>
        new(SaveQueuePhase.Saved, 0, revision, null, null);
}

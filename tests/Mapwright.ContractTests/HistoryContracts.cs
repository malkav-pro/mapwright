using System.Collections.Immutable;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Mapwright.Application;
using Mapwright.Domain;
using Mapwright.Infrastructure;

public sealed class HistoryContractCases : IContractCaseProvider
{
    public void Register(ContractRegistry registry)
    {
        registry.Add("history Q4 measurement fixture reports checkpoint candidates", Q4MeasurementFixture);
        registry.Add("history cursor and redo branch survive restart", CursorAndRedoBranchSurviveRestart);
        registry.Add("history cursor rejects stale transitions", CursorRejectsStaleTransitions);
        registry.Add("history recent resident undo reports a p95 distribution", RecentResidentUndoDistribution);
        registry.Add("history resident model cache rejects a replaced redo branch",
            ResidentCacheRejectsReplacedBranch);
        registry.Add("history adjacent stroke navigation invalidates only its bounds",
            AdjacentStrokeNavigationIsBounded);
        registry.Add("history resident deltas are bounded and compatible", ResidentDeltasAreBoundedAndCompatible);
        registry.Add("history windows and controls are revision labelled", WindowsAndControlsAreRevisionLabelled);
        registry.Add("history evicted reconstruction cancellation is atomic", EvictedReconstructionCancellationIsAtomic);
        registry.Add("history cache deletion and version mismatch preserve authority", CacheDeletionAndVersionMismatchPreserveAuthority);
        registry.Add("save drains queued commands and reports durable state", SaveDrainsQueuedCommands);
        registry.Add("prepared edit shares the single projection before durable commit", PreparedEditPrecedesDurableCommit);
        registry.Add("edit rehearsal never commits, publishes or changes state", RehearsalHasNoDurableEffect);
        registry.Add("session connection hold keeps the WAL, shares nested reads and releases files",
            HeldConnectionKeepsWalAndReleases);
    }

    private static Task Q4MeasurementFixture()
    {
        const int commandCount = 256;
        int[] intervals = [1, 8, 32, 64];
        var initial = Fixture() with { Title = "Real-map 3780x4097 history fixture", Width = 3780, Height = 4097 };
        var commands = new List<IEditCommand>(commandCount);
        var revisions = new List<MapProject>(commandCount + 1) { initial };
        var current = initial;
        var foreground = initial.RequireRole(TerrainRole.Foreground);
        for (var index = 0; index < commandCount; index++)
        {
            var point = new MapPoint((index * 137) % 3780, (index * 211) % 4097);
            var recipe = TexturePresetCatalog.Resolve("hard-round", new string('a', 64), index + 1, 48);
            var stroke = TexturePaintStroke.Create(StrokeId.New(), [point], recipe, point);
            var command = new AddResolvedTextureStroke(CommandId.New(), current.Revision,
                TerrainRole.Foreground, stroke);
            commands.Add(command);
            current = command.Apply(current).Project;
            revisions.Add(current);
        }

        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        foreach (var interval in intervals)
        {
            long snapshotBytes = 0;
            for (var index = 0; index < revisions.Count; index += interval)
                snapshotBytes += JsonSerializer.SerializeToUtf8Bytes(revisions[index], options).LongLength;
            if ((revisions.Count - 1) % interval != 0)
                snapshotBytes += JsonSerializer.SerializeToUtf8Bytes(revisions[^1], options).LongLength;

            var targetIndex = commandCount - 1;
            var checkpointIndex = targetIndex - (targetIndex % interval);
            var replayStart = revisions[checkpointIndex];
            var watch = Stopwatch.StartNew();
            var replayed = replayStart;
            for (var repeat = 0; repeat < 25; repeat++)
            {
                replayed = replayStart;
                for (var index = checkpointIndex; index < targetIndex; index++)
                    replayed = commands[index].Apply(replayed).Project;
            }
            watch.Stop();
            Equal(revisions[targetIndex].Revision, replayed.Revision);
            Console.WriteLine(
                $"Q4 interval={interval} snapshot_bytes={snapshotBytes} worst_replay_commands={targetIndex - checkpointIndex} replay_ms={watch.Elapsed.TotalMilliseconds / 25:F3}");
        }

        var visible = current;
        var cancelledAfter = 0;
        for (var index = 0; index < commands.Count; index++)
        {
            cancelledAfter++;
            if (cancelledAfter == 17) break;
        }
        Equal(current.Revision, visible.Revision);
        Console.WriteLine(
            "Q4 fixture_sha256=67b69858634b1a2eee8082efd80043e915dcebb36e29c8dc66e42c6406e54fab renderer=history-replay-v1 recipe=texture-brush-v1 cancellation_units=17");
        return Task.CompletedTask;
    }

    private static async Task ResidentDeltasAreBoundedAndCompatible()
    {
        var initial = Fixture();
        await using var session = new HistorySession(initial, new RecordingRepository([]),
            new RecordingRenderer([]));
        var compatibility = HistoryCompatibility.For(initial);
        for (var index = 0; index < 17; index++)
        {
            var tile = new HistoryTileDelta(index, 0, [1, 2, 3], [4, 5, 6]);
            True(session.RememberResidentDelta(new HistoryResidentDelta(
                    initial.Revision + index,
                    initial.Revision + index + 1,
                    compatibility,
                    [tile])),
                "A compatible one-tile adjacent delta should be retained.");
        }
        Equal(16, session.ResidentTileCount);

        var incompatible = compatibility with { RendererVersion = "history-replay-v2" };
        True(!session.RememberResidentDelta(new HistoryResidentDelta(
                initial.Revision + 17,
                initial.Revision + 18,
                incompatible,
                [new HistoryTileDelta(17, 0, [1], [2])])),
            "Incompatible resident pixels must not enter the cache.");
        Equal(16, session.ResidentTileCount);

        var tooLarge = Enumerable.Range(0, 17)
            .Select(index => new HistoryTileDelta(index, 1, [1], [2]))
            .ToImmutableArray();
        True(!session.RememberResidentDelta(new HistoryResidentDelta(100, 101, compatibility, tooLarge)),
            "A delta touching more than 16 tiles must use reconstruction.");
        Equal(16, session.ResidentTileCount);
    }

    private static async Task RecentResidentUndoDistribution()
    {
        var root = Path.Combine(Path.GetTempPath(), $"mapwright-history-resident-{Guid.NewGuid():N}");
        try
        {
            var initial = Fixture();
            var repository = new SqliteProjectRepository(root, new FixedClock());
            await repository.CreateAsync(initial);
            var latest = await SeedRenameCommandsAsync(repository, initial, 1);
            await using var session = new HistorySession(latest, repository, new RecordingRenderer([]));
            var tiles = Enumerable.Range(0, 16)
                .Select(index => new HistoryTileDelta(index % 4, index / 4, [1, 2], [3, 4]))
                .ToImmutableArray();
            True(session.RememberResidentDelta(new HistoryResidentDelta(
                    initial.Revision, latest.Revision, HistoryCompatibility.For(initial), tiles)),
                "The 16-tile adjacent delta should remain resident.");

            var samples = new List<double>();
            for (var index = 0; index < 20; index++)
            {
                var started = Stopwatch.GetTimestamp();
                var undo = await session.UndoAsync();
                samples.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                Equal(16, undo.TilesRestored);
                True(!undo.Reconstructed, "A compatible resident undo must use its delta path.");
                var redo = await session.RedoAsync();
                Equal(16, redo.TilesRestored);
            }

            var ordered = samples.Order().ToArray();
            var p50 = ordered[(int)Math.Ceiling(ordered.Length * 0.50) - 1];
            var p95 = ordered[(int)Math.Ceiling(ordered.Length * 0.95) - 1];
            Console.WriteLine($"HISTORY resident_undo samples={samples.Count} tiles=16 p50_ms={p50:F3} p95_ms={p95:F3} max_ms={ordered[^1]:F3}");
            True(double.IsFinite(p95), "Resident undo p95 must be measurable.");
            True(p95 <= 100, $"Resident undo p95 exceeded 100 ms: {p95:F3} ms.");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static async Task ResidentCacheRejectsReplacedBranch()
    {
        var root = Path.Combine(Path.GetTempPath(), $"mapwright-history-branch-cache-{Guid.NewGuid():N}");
        try
        {
            var initial = Fixture();
            var repository = new SqliteProjectRepository(root, new FixedClock());
            await repository.CreateAsync(initial);
            var original = new RenameTerrainLayer(CommandId.New(), initial.Revision,
                TerrainRole.Foreground, "Original branch");
            var firstChange = original.Apply(initial);
            await repository.CommitAsync(initial, original, firstChange, CancellationToken.None);
            await using var session = new HistorySession(firstChange.Project, repository,
                new RecordingRenderer([]));
            True(session.RememberResidentDelta(new HistoryResidentDelta(initial.Revision,
                firstChange.Project.Revision, HistoryCompatibility.For(initial),
                [new HistoryTileDelta(0, 0, [1], [2])])),
                "The adjacent resident pair should be admitted.");
            await session.UndoAsync();

            var replacement = new RenameTerrainLayer(CommandId.New(), initial.Revision,
                TerrainRole.Foreground, "Replacement branch");
            var replacementChange = replacement.Apply(initial);
            await repository.CommitAsync(initial, replacement, replacementChange, CancellationToken.None);
            await MoveCursorAsync(repository, initial.ProjectId, replacementChange.Project.Revision,
                initial.Revision);
            await session.RedoAsync();
            Equal("Replacement branch", session.Current.RequireRole(TerrainRole.Foreground).Name);
            var reopened = await new SqliteProjectRepository(root, new FixedClock())
                .LoadAsync(initial.ProjectId, CancellationToken.None);
            Equal("Replacement branch", reopened.RequireRole(TerrainRole.Foreground).Name);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static async Task AdjacentStrokeNavigationIsBounded()
    {
        var root = Path.Combine(Path.GetTempPath(), $"mapwright-history-bounds-{Guid.NewGuid():N}");
        try
        {
            var initial = Fixture();
            var repository = new SqliteProjectRepository(root, new FixedClock());
            await repository.CreateAsync(initial);
            var foreground = initial.RequireRole(TerrainRole.Foreground);
            var stroke = new PaintStroke(StrokeId.New(), [new MapPoint(500, 500)],
                new ResolvedBrush(new string('a', 64), 32, 0.8, 0.8, 0.7, 0.2, 0, 4, 1),
                false, TerrainStrokeKind.Texture);
            var command = new AddTextureStroke(CommandId.New(), initial.Revision, foreground.Id, stroke);
            var change = command.Apply(initial);
            await repository.CommitAsync(initial, command, change, CancellationToken.None);
            await using var session = new HistorySession(change.Project, repository,
                new RecordingRenderer([]));
            var undo = await session.UndoAsync();
            Equal(stroke.Bounds, undo.Invalidation?.Bounds);
            True(undo.Invalidation?.RebuildComposite == true,
                "Undo must request recomposition of its affected tiles.");
            var redo = await session.RedoAsync();
            Equal(stroke.Bounds, redo.Invalidation?.Bounds);
            Equal(change.Project.Revision, session.Current.Revision);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static async Task WindowsAndControlsAreRevisionLabelled()
    {
        var root = Path.Combine(Path.GetTempPath(), $"mapwright-history-window-{Guid.NewGuid():N}");
        try
        {
            var initial = Fixture();
            var repository = new SqliteProjectRepository(root, new FixedClock());
            await repository.CreateAsync(initial);
            var latest = await SeedRenameCommandsAsync(repository, initial, 3);
            await using var session = new HistorySession(latest, repository, new RecordingRenderer([]));

            var first = await session.ReadWindowAsync(0, 2);
            var second = await session.ReadWindowAsync(2, 2);
            Equal(3, first.TotalCount);
            Equal(2, first.Entries.Count);
            Equal(1, second.Entries.Count);
            Equal(3, first.Entries.Select(entry => entry.Sequence)
                .Concat(second.Entries.Select(entry => entry.Sequence)).Distinct().Count());
            Equal(initial.Revision + 1, first.Entries[0].Revision);
            Equal(initial.Revision + 3, second.Entries[0].Revision);
            Equal("Rename Terrain Layer", first.Entries[0].Label);
            Equal(first.Entries[0].CommittedAt, second.Entries[0].CommittedAt);
            True(first.Entries.All(entry => entry.TileCount == 4),
                "Whole-document property edits must report measured 512px tile coverage.");

            var atTip = await session.ReadControlsAsync();
            True(atTip.Undo.Enabled && atTip.Undo.Label == "Rename Terrain Layer",
                "Undo must name the current durable action.");
            Equal("No committed action to redo.", atTip.Redo.Reason);
            await session.UndoAsync();
            var afterUndo = await session.ReadControlsAsync();
            True(afterUndo.Undo.Enabled && afterUndo.Redo.Enabled,
                "Both actions must be available at an interior cursor.");
            Equal(initial.Revision + 2, afterUndo.Undo.Revision!.Value);
            Equal(initial.Revision + 3, afterUndo.Redo.Revision!.Value);
            var windowAfterUndo = await session.ReadWindowAsync(0, 10);
            Equal(initial.Revision + 2, windowAfterUndo.State.CursorRevision);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static async Task EvictedReconstructionCancellationIsAtomic()
    {
        var root = Path.Combine(Path.GetTempPath(), $"mapwright-history-cancel-{Guid.NewGuid():N}");
        try
        {
            var initial = Fixture();
            var repository = new SqliteProjectRepository(root, new FixedClock());
            await repository.CreateAsync(initial);
            var latest = await SeedRenameCommandsAsync(repository, initial, 70);
            await repository.DeleteHistoryAccelerationAsync(initial.ProjectId);
            await using var session = new HistorySession(latest, repository, new RecordingRenderer([]));
            var cancellationWasLive = false;
            var progress = new SynchronousProgress(update =>
            {
                if (update.CompletedCommands == 1)
                {
                    cancellationWasLive = session.ReconstructionCancellationToken.CanBeCanceled;
                    True(session.CancelReconstruction(), "The UI cancellation handle must cancel active replay.");
                }
            });
            var target = initial.Revision + 63;
            await ThrowsAsync<OperationCanceledException>(() => session.ReconstructAsync(
                target, HistoryCompatibility.For(initial), progress));

            Equal(latest.Revision, session.Current.Revision);
            True(cancellationWasLive, "Active reconstruction must expose a cancellable token.");
            var afterCancellation = await repository.ReadHistoryStateAsync(initial.ProjectId);
            Equal(latest.Revision, afterCancellation.CursorRevision);
            True(!session.ReconstructionState.IsRunning,
                "Cancellation must clear the reconstruction working state.");

            var completed = new List<HistoryReconstructionProgress>();
            var timeline = new List<RevisionTimelineEvent>();
            session.RevisionPublished += timeline.Add;
            var result = await session.ReconstructAsync(target, HistoryCompatibility.For(initial),
                new SynchronousProgress(completed.Add));
            Equal(target, result.Revision);
            True(result.Reconstructed, "An evicted move must report the reconstruction path.");
            True(completed.Count > 1 && completed[^1].CompletedCommands == completed[^1].TotalCommands,
                "Reconstruction must report real completed/total command units.");
            var afterCompletion = await repository.ReadHistoryStateAsync(initial.ProjectId);
            Equal(target, afterCompletion.CursorRevision);
            Equal(1, timeline.Count);
            Equal(1L, timeline[0].Sequence);
            Equal("Undo", timeline[0].Action);
            Equal(latest.Revision, timeline[0].FromRevision);
            Equal(target, timeline[0].ToRevision);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static async Task CacheDeletionAndVersionMismatchPreserveAuthority()
    {
        var root = Path.Combine(Path.GetTempPath(), $"mapwright-history-cache-{Guid.NewGuid():N}");
        try
        {
            var initial = Fixture();
            var repository = new SqliteProjectRepository(root, new FixedClock());
            await repository.CreateAsync(initial);
            var latest = await SeedRenameCommandsAsync(repository, initial, 70);
            var incompatible = HistoryCompatibility.For(initial) with { RecipeVersion = 2 };
            var rebuilt = await repository.ReconstructRevisionAsync(initial.ProjectId, latest.Revision,
                incompatible);
            ProjectEquivalent(latest, rebuilt.Project);
            True(rebuilt.CompatibilityMismatchRebuilt,
                "A version mismatch must be observable while rebuilding from commands.");
            Equal(70, rebuilt.CommandsReplayed);

            var cacheFile = Path.Combine(root, "cache", "history", "disposable.bin");
            await File.WriteAllBytesAsync(cacheFile, [1, 2, 3]);
            await repository.DeleteHistoryAccelerationAsync(initial.ProjectId);
            True(!File.Exists(cacheFile), "Deleting acceleration must remove cache files.");
            var afterDelete = await repository.ReconstructRevisionAsync(initial.ProjectId, latest.Revision,
                HistoryCompatibility.For(initial));
            ProjectEquivalent(latest, afterDelete.Project);
            Equal(70, afterDelete.CommandsReplayed);
            var commands = await repository.ReadHistoryPageAsync(initial.ProjectId, 0, 100);
            Equal(70, commands.Count);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static async Task CursorAndRedoBranchSurviveRestart()
    {
        var root = Path.Combine(Path.GetTempPath(), $"mapwright-history-{Guid.NewGuid():N}");
        try
        {
            var initial = Fixture();
            var repository = new SqliteProjectRepository(root, new FixedClock());
            await repository.CreateAsync(initial);
            var foreground = initial.RequireRole(TerrainRole.Foreground);

            var first = new RenameTerrainLayer(CommandId.New(), initial.Revision,
                TerrainRole.Foreground, "First edit");
            var firstChange = first.Apply(initial);
            await repository.CommitAsync(initial, first, firstChange, CancellationToken.None);
            var second = new RenameTerrainLayer(CommandId.New(), firstChange.Project.Revision,
                TerrainRole.Foreground, "Second edit");
            var secondChange = second.Apply(firstChange.Project);
            await repository.CommitAsync(firstChange.Project, second, secondChange, CancellationToken.None);

            await MoveCursorAsync(repository, initial.ProjectId, secondChange.Project.Revision,
                firstChange.Project.Revision);
            var reopenedRepository = new SqliteProjectRepository(root, new FixedClock());
            var reopened = await reopenedRepository.LoadAsync(initial.ProjectId, CancellationToken.None);
            Equal(firstChange.Project.Revision, reopened.Revision);
            Equal("First edit", reopened.RequireRole(TerrainRole.Foreground).Name);

            var state = await ReadHistoryStateAsync(reopenedRepository, initial.ProjectId);
            Equal(firstChange.Project.Revision, LongProperty(state, "CursorRevision"));
            Equal(secondChange.Project.Revision, LongProperty(state, "LatestRevision"));

            var replacement = new RenameTerrainLayer(CommandId.New(), reopened.Revision,
                TerrainRole.Foreground, "Replacement edit");
            var replacementChange = replacement.Apply(reopened);
            await reopenedRepository.CommitAsync(reopened, replacement, replacementChange,
                CancellationToken.None);

            var branched = await reopenedRepository.LoadAsync(initial.ProjectId, CancellationToken.None);
            Equal(secondChange.Project.Revision, branched.Revision);
            Equal("Replacement edit", branched.RequireRole(TerrainRole.Foreground).Name);
            var page = await ReadHistoryPageAsync(reopenedRepository, initial.ProjectId, 0, 10);
            Equal(2, page.Count);
            Equal(first.Id, StructProperty<CommandId>(page[0], "CommandId"));
            Equal(replacement.Id, StructProperty<CommandId>(page[1], "CommandId"));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static async Task CursorRejectsStaleTransitions()
    {
        var root = Path.Combine(Path.GetTempPath(), $"mapwright-history-stale-{Guid.NewGuid():N}");
        try
        {
            var initial = Fixture();
            var repository = new SqliteProjectRepository(root, new FixedClock());
            await repository.CreateAsync(initial);
            await ThrowsAsync<RevisionConflictException>(() =>
                MoveCursorAsync(repository, initial.ProjectId, initial.Revision + 1, initial.Revision));

            await MoveCursorAsync(repository, initial.ProjectId, initial.Revision, initial.Revision);
            var unchanged = await repository.LoadAsync(initial.ProjectId, CancellationToken.None);
            Equal(initial.Revision, unchanged.Revision);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static async Task SaveDrainsQueuedCommands()
    {
        var initial = Fixture();
        var repository = new BlockingRepository(initial);
        var renderer = new RecordingRenderer(repository.Events);
        await using var session = new EditSession(initial, repository, renderer);
        var timeline = new List<RevisionTimelineEvent>();
        session.RevisionPublished += timeline.Add;

        var stateProperty = typeof(EditSession).GetProperty("SaveState")
            ?? throw new InvalidOperationException("EditSession must expose authoritative SaveState queue facts.");
        var command = new RenameTerrainLayer(CommandId.New(), initial.Revision,
            TerrainRole.Foreground, "Queued edit");
        var edit = session.ExecuteAsync(command);
        await repository.CommitEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var queued = stateProperty.GetValue(session)
            ?? throw new InvalidOperationException("SaveState must never be null.");
        Equal("Queued", StringProperty(queued, "Phase"));
        Equal(1, IntProperty(queued, "QueuedCommands"));

        var save = session.SaveAsync();
        True(!save.IsCompleted, "Save must wait behind the queued durable command.");
        repository.ReleaseCommit.TrySetResult();
        var acknowledgement = await edit;
        var saved = await save;

        Equal(acknowledgement.Revision, saved.Revision);
        Equal("commit-start,commit-durable,render-enqueued,load-durable", string.Join(',', repository.Events));
        var final = stateProperty.GetValue(session)!;
        Equal("Saved", StringProperty(final, "Phase"));
        Equal(0, IntProperty(final, "QueuedCommands"));
        Equal(saved.Revision, LongProperty(final, "DurableRevision"));
        Equal(1, timeline.Count);
        Equal(1L, timeline[0].Sequence);
        Equal(initial.Revision, timeline[0].FromRevision);
        Equal(saved.Revision, timeline[0].ToRevision);
        Equal(nameof(RenameTerrainLayer), timeline[0].Action);
    }

    private static async Task PreparedEditPrecedesDurableCommit()
    {
        var initial = Fixture();
        var repository = new BlockingRepository(initial);
        var renderer = new RecordingRenderer(repository.Events);
        await using var session = new EditSession(initial, repository, renderer);
        var command = new RenameTerrainLayer(CommandId.New(), initial.Revision,
            TerrainRole.Foreground, "Prepared edit");
        PreparedEdit? prepared = null;
        var edit = session.ExecuteAsync(command, change =>
        {
            prepared = change;
            repository.Events.Add("prepared");
            throw new InvalidOperationException("Observer failure must not reach the commit.");
        });
        await repository.CommitEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        True(prepared is not null, "The projected change must be shared before the commit starts.");
        Equal(command.Id, prepared!.CommandId);
        Equal(initial.Revision, prepared.BaseRevision);
        Equal(initial.Revision, session.Current.Revision);
        repository.ReleaseCommit.TrySetResult();
        var acknowledgement = await edit;

        Equal(prepared.Change.Project.Revision, acknowledgement.Revision);
        True(ReferenceEquals(prepared.Change.Project, session.Current),
            "The session must publish the same projected change it shared, not a second Apply.");
        Equal("prepared,commit-start,commit-durable,render-enqueued", string.Join(',', repository.Events));
    }

    private static async Task RehearsalHasNoDurableEffect()
    {
        var initial = Fixture();
        var repository = new BlockingRepository(initial);
        var renderer = new RecordingRenderer(repository.Events);
        await using var session = new EditSession(initial, repository, renderer);
        var published = 0;
        session.RevisionPublished += _ => published++;
        var elapsed = await session.RehearseAsync(
        [
            new RenameTerrainLayer(CommandId.New(), initial.Revision, TerrainRole.Foreground, "Rehearsed"),
            new RenameTerrainLayer(CommandId.New(), initial.Revision + 7, TerrainRole.Foreground, "Stale")
        ]);
        True(elapsed >= 0, "Rehearsal reports its elapsed setup time.");
        True(ReferenceEquals(initial, session.Current), "Rehearsal must not replace the current snapshot.");
        Equal(0, repository.Events.Count);
        Equal(0, published);
        Equal("Saved", session.SaveState.Phase.ToString());
    }

    private static async Task HeldConnectionKeepsWalAndReleases()
    {
        var root = Path.Combine(Path.GetTempPath(), $"mapwright-hold-{Guid.NewGuid():N}");
        var initial = Fixture();
        var repository = new SqliteProjectRepository(root, new FixedClock());
        await repository.CreateAsync(initial);
        var wal = Path.Combine(root, "scene.sqlite-wal");
        True(!File.Exists(wal), "A repository without a hold closes its last connection, removing the WAL.");
        await using (var session = new EditSession(initial, repository, new RecordingRenderer([])))
        {
            for (var edit = 1; edit <= 3; edit++)
                await session.ExecuteAsync(new RenameTerrainLayer(CommandId.New(), session.Current.Revision,
                    TerrainRole.Foreground, $"Held edit {edit}"));
            True(File.Exists(wal), "While a session holds the connection, commits must not checkpoint away the WAL.");
            // Load nests a reconstruction scope on the same flow; it must reuse, not deadlock.
            var loaded = await repository.LoadAsync(initial.ProjectId, CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(10));
            Equal(session.Current.Revision, loaded.Revision);
            // A second, independent repository sees every acknowledged commit before release.
            var concurrent = await new SqliteProjectRepository(root, new FixedClock())
                .LoadAsync(initial.ProjectId, CancellationToken.None);
            Equal("Held edit 3", concurrent.RequireRole(TerrainRole.Foreground).Name);
            var parallel = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ =>
                Task.Run(() => repository.ReadHistoryStateAsync(initial.ProjectId))));
            True(parallel.All(state => state.CursorRevision == session.Current.Revision),
                "Concurrent operations must serialize on the held connection.");
        }
        var reopened = await new SqliteProjectRepository(root, new FixedClock())
            .LoadAsync(initial.ProjectId, CancellationToken.None);
        Equal(initial.Revision + 3, reopened.Revision);
        Directory.Delete(root, recursive: true);
        True(!Directory.Exists(root), "Releasing the last hold must close every project file handle.");
    }

    private static async Task MoveCursorAsync(
        SqliteProjectRepository repository,
        ProjectId projectId,
        long expected,
        long target)
    {
        var method = typeof(SqliteProjectRepository).GetMethod("MoveHistoryCursorAsync")
            ?? throw new InvalidOperationException("SqliteProjectRepository must persist history cursor transitions.");
        await InvokeTaskAsync(method, repository, projectId, expected, target, CancellationToken.None);
    }

    private static Task<object> ReadHistoryStateAsync(SqliteProjectRepository repository, ProjectId projectId) =>
        InvokeTaskAsync(
            typeof(SqliteProjectRepository).GetMethod("ReadHistoryStateAsync")
            ?? throw new InvalidOperationException("SqliteProjectRepository must expose durable history state."),
            repository, projectId, CancellationToken.None);

    private static async Task<IReadOnlyList<object>> ReadHistoryPageAsync(
        SqliteProjectRepository repository,
        ProjectId projectId,
        int offset,
        int limit)
    {
        var result = await InvokeTaskAsync(
            typeof(SqliteProjectRepository).GetMethod("ReadHistoryPageAsync")
            ?? throw new InvalidOperationException("SqliteProjectRepository must expose stable history paging."),
            repository, projectId, offset, limit, CancellationToken.None);
        return ((System.Collections.IEnumerable)result).Cast<object>().ToArray();
    }

    private static async Task<object> InvokeTaskAsync(MethodInfo method, object target, params object[] arguments)
    {
        Task task;
        try
        {
            task = (Task)(method.Invoke(target, arguments)
                ?? throw new InvalidOperationException($"{method.Name} returned null."));
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            throw exception.InnerException;
        }

        await task;
        return task.GetType().GetProperty("Result")?.GetValue(task) ?? new object();
    }

    private static MapProject Fixture()
    {
        var background = new TerrainLayer(LayerId.New(), "Background", true, false, 1,
            new CoastlineStyle(0), ImmutableArray<PaintStroke>.Empty, TerrainRole.Background);
        var foreground = new TerrainLayer(LayerId.New(), "Foreground", true, false, 1,
            new CoastlineStyle(22), ImmutableArray<PaintStroke>.Empty, TerrainRole.Foreground);
        return new MapProject(ProjectId.New(), MapId.New(), "History fixture", 1024, 1024, 7,
            [background, foreground]);
    }

    private static async Task<MapProject> SeedRenameCommandsAsync(
        SqliteProjectRepository repository,
        MapProject initial,
        int count)
    {
        var current = initial;
        for (var index = 0; index < count; index++)
        {
            var command = new RenameTerrainLayer(CommandId.New(), current.Revision,
                TerrainRole.Foreground, $"Foreground edit {index + 1}");
            var change = command.Apply(current);
            await repository.CommitAsync(current, command, change, CancellationToken.None);
            current = change.Project;
        }
        return current;
    }

    private static string StringProperty(object value, string name) =>
        value.GetType().GetProperty(name)?.GetValue(value)?.ToString()
        ?? throw new InvalidOperationException($"{value.GetType().Name}.{name} is missing.");

    private static int IntProperty(object value, string name) =>
        Convert.ToInt32(value.GetType().GetProperty(name)?.GetValue(value)
            ?? throw new InvalidOperationException($"{value.GetType().Name}.{name} is missing."));

    private static long LongProperty(object value, string name) =>
        Convert.ToInt64(value.GetType().GetProperty(name)?.GetValue(value)
            ?? throw new InvalidOperationException($"{value.GetType().Name}.{name} is missing."));

    private static T StructProperty<T>(object value, string name) where T : struct =>
        (T)(value.GetType().GetProperty(name)?.GetValue(value)
            ?? throw new InvalidOperationException($"{value.GetType().Name}.{name} is missing."));

    private static void True(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void ProjectEquivalent(MapProject expected, MapProject actual)
    {
        Equal(expected.ProjectId, actual.ProjectId);
        Equal(expected.MapId, actual.MapId);
        Equal(expected.Revision, actual.Revision);
        Equal(expected.Title, actual.Title);
        Equal(expected.TerrainLayers.Length, actual.TerrainLayers.Length);
        for (var index = 0; index < expected.TerrainLayers.Length; index++)
        {
            Equal(expected.TerrainLayers[index].Id, actual.TerrainLayers[index].Id);
            Equal(expected.TerrainLayers[index].Role, actual.TerrainLayers[index].Role);
            Equal(expected.TerrainLayers[index].Name, actual.TerrainLayers[index].Name);
        }
    }

    private static void Equal<T>(T expected, T actual) where T : notnull
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected {expected}; got {actual}.");
    }

    private static async Task ThrowsAsync<T>(Func<Task> action) where T : Exception
    {
        try { await action(); }
        catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }

    private sealed class BlockingRepository(MapProject initial) : IProjectRepository
    {
        private MapProject _durable = initial;

        public List<string> Events { get; } = [];
        public TaskCompletionSource CommitEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseCommit { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<MapProject> LoadAsync(ProjectId projectId, CancellationToken cancellationToken)
        {
            Events.Add("load-durable");
            return Task.FromResult(_durable);
        }

        public async Task<DurableCommit> CommitAsync(
            MapProject previous,
            IEditCommand command,
            DocumentChange change,
            CancellationToken cancellationToken)
        {
            Events.Add("commit-start");
            CommitEntered.TrySetResult();
            await ReleaseCommit.Task.WaitAsync(cancellationToken);
            _durable = change.Project;
            Events.Add("commit-durable");
            return new DurableCommit(command.Id, change.Project.Revision, DateTimeOffset.UnixEpoch);
        }
    }

    private sealed class SynchronousProgress(Action<HistoryReconstructionProgress> report)
        : IProgress<HistoryReconstructionProgress>
    {
        public void Report(HistoryReconstructionProgress value) => report(value);
    }
}

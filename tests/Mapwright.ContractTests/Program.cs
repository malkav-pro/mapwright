using System.Collections.Immutable;
using Mapwright.Application;
using Mapwright.Domain;
using Mapwright.Infrastructure;

var registry = ContractRegistry.Discover();
if (registry.Cases.Count == 0)
{
    Console.Error.WriteLine("FAIL no contract cases were discovered.");
    return 2;
}

var failures = new List<string>();
var executed = 0;
foreach (var contract in registry.Cases)
{
    try
    {
        await contract.Run();
        executed++;
        Console.WriteLine($"PASS {contract.Name}");
    }
    catch (Exception exception)
    {
        failures.Add($"FAIL {contract.Name}: {exception.Message}");
    }
}

foreach (var failure in failures) Console.Error.WriteLine(failure);
Console.WriteLine($"RESULT {executed} passed, {failures.Count} failed, {registry.Cases.Count} discovered");
return executed > 0 && failures.Count == 0 ? 0 : 1;

public sealed record ContractCase(string Name, Func<Task> Run);

public interface IContractCaseProvider
{
    void Register(ContractRegistry registry);
}

public sealed class ContractRegistry
{
    private readonly Dictionary<string, ContractCase> _cases = new(StringComparer.Ordinal);

    public IReadOnlyList<ContractCase> Cases => _cases.Values.OrderBy(test => test.Name, StringComparer.Ordinal).ToArray();

    public void Add(string name, Func<Task>? run)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (run is null) throw new InvalidOperationException($"Contract case '{name}' has no implementation.");
        if (!_cases.TryAdd(name, new ContractCase(name, run)))
            throw new InvalidOperationException($"Contract case '{name}' was registered more than once.");
    }

    public static ContractRegistry Discover()
    {
        var registry = new ContractRegistry();
        var providers = typeof(ContractRegistry).Assembly.GetTypes()
            .Where(type => !type.IsAbstract && typeof(IContractCaseProvider).IsAssignableFrom(type))
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .ToArray();
        if (providers.Length == 0) throw new InvalidOperationException("No contract providers were discovered.");
        foreach (var providerType in providers)
        {
            var before = registry._cases.Count;
            var provider = Activator.CreateInstance(providerType) as IContractCaseProvider
                ?? throw new InvalidOperationException($"Could not instantiate contract provider {providerType.FullName}.");
            provider.Register(registry);
            if (registry._cases.Count == before)
                throw new InvalidOperationException($"Contract provider {providerType.FullName} registered no cases.");
        }
        return registry;
    }
}

public sealed class BaselineContractCases : IContractCaseProvider
{
    public void Register(ContractRegistry registry)
    {
        registry.Add("two fixed terrain roles are enforced", TwoFixedTerrainRolesAreEnforced);
        registry.Add("paint invalidation includes coastline reach", PaintInvalidationIncludesCoastline);
        registry.Add("river move invalidates old and new Foreground geometry", RiverMoveInvalidatesOldAndNewGeometry);
        registry.Add("projects may omit a river", ProjectsMayOmitRiver);
        registry.Add("revision conflicts are rejected", RevisionConflictsAreRejected);
        registry.Add("durable commit precedes render scheduling and acknowledgement", CommitPrecedesRenderAndAcknowledgement);
        registry.Add("failed commit is neither published nor rendered", FailedCommitIsNotPublished);
        registry.Add("repeated Save preserves the acknowledged revision", RepeatedSaveIsIdempotent);
        registry.Add("SQLite commit reopens at the acknowledged revision", SqliteCommitReopens);
        registry.Add("SQLite accepts one successor for one base revision", SqliteAcceptsOneSuccessor);
    }

    private static Task TwoFixedTerrainRolesAreEnforced()
    {
        var project = Fixture();
        project.ValidateConnectedTerrain();
        Equal(2, project.TerrainLayers.Length);
        Equal(TerrainRole.Background, project.TerrainLayers[0].Role);
        Equal(TerrainRole.Foreground, project.TerrainLayers[1].Role);
        Equal(project.TerrainLayers[1].Id, project.River!.TargetLayerId);
        return Task.CompletedTask;
    }

    private static Task PaintInvalidationIncludesCoastline()
    {
        var project = Fixture();
        var target = project.RequireRole(TerrainRole.Foreground);
        var stroke = new PaintStroke(StrokeId.New(), [new MapPoint(100, 120), new MapPoint(180, 160)],
            Brush(radius: 20), true, TerrainStrokeKind.Coverage);
        var change = new AddPaintStroke(CommandId.New(), project.Revision, target.Id, stroke).Apply(project);
        Equal(project.Revision + 1, change.Project.Revision);
        Equal(1, change.Project.RequireRole(TerrainRole.Foreground).Strokes.Length);
        Equal(new MapBounds(58, 78, 222, 202), change.Invalidation.Bounds);
        True(change.Invalidation.RebuildCoverage && change.Invalidation.RebuildDistance &&
             change.Invalidation.RebuildComposite, "Coverage paint must rebuild the full terrain dependency chain.");
        return Task.CompletedTask;
    }

    private static Task RiverMoveInvalidatesOldAndNewGeometry()
    {
        var project = Fixture();
        var command = new MoveRiverPoint(CommandId.New(), project.Revision, 1, new MapPoint(600, 400));
        var change = command.Apply(project);
        var bounds = change.Invalidation.Bounds;
        True(bounds.Left <= 58 && bounds.Top <= 58 && bounds.Right >= 642 && bounds.Bottom >= 442,
            "River invalidation must include old and new adjacent segments plus effect reach.");
        Equal(TerrainRole.Foreground, change.Project.RequireLayer(change.Project.River!.TargetLayerId).Role);
        return Task.CompletedTask;
    }

    private static Task ProjectsMayOmitRiver()
    {
        var project = Fixture() with { River = null };
        project.ValidateConnectedTerrain();
        Throws<InvalidOperationException>(() =>
            new SetRiverWidth(CommandId.New(), project.Revision, 20).Apply(project));
        return Task.CompletedTask;
    }

    private static Task RevisionConflictsAreRejected()
    {
        var project = Fixture();
        var layer = project.RequireRole(TerrainRole.Background);
        var stroke = TextureStroke(new MapPoint(0, 0), 8);
        Throws<RevisionConflictException>(() =>
            new AddTextureStroke(CommandId.New(), project.Revision - 1, layer.Id, stroke).Apply(project));
        return Task.CompletedTask;
    }

    private static async Task CommitPrecedesRenderAndAcknowledgement()
    {
        var events = new List<string>();
        var project = Fixture();
        var repository = new RecordingRepository(events);
        var renderer = new RecordingRenderer(events);
        await using var session = new EditSession(project, repository, renderer);
        var target = project.RequireRole(TerrainRole.Foreground);
        var command = new AddTextureStroke(CommandId.New(), project.Revision, target.Id,
            TextureStroke(new MapPoint(40, 50), 10));
        var acknowledgement = await session.ExecuteAsync(command);
        events.Add("acknowledged");

        Equal(project.Revision + 1, acknowledgement.Revision);
        Equal(project.Revision + 1, session.Current.Revision);
        Equal("commit-start,commit-durable,render-enqueued,acknowledged", string.Join(',', events));
    }

    private static async Task FailedCommitIsNotPublished()
    {
        var events = new List<string>();
        var project = Fixture();
        var repository = new RecordingRepository(events) { Fail = true };
        var renderer = new RecordingRenderer(events);
        await using var session = new EditSession(project, repository, renderer);
        var target = project.RequireRole(TerrainRole.Foreground);
        var command = new AddTextureStroke(CommandId.New(), project.Revision, target.Id,
            TextureStroke(new MapPoint(40, 50), 10));
        await ThrowsAsync<IOException>(() => session.ExecuteAsync(command));
        Equal(project.Revision, session.Current.Revision);
        Equal("commit-start", string.Join(',', events));
    }

    private static async Task RepeatedSaveIsIdempotent()
    {
        var project = Fixture();
        var repository = new RecordingRepository([]) { Loaded = project };
        await using var session = new EditSession(project, repository, new RecordingRenderer([]));
        var first = await session.SaveAsync();
        var second = await session.SaveAsync();
        Equal(project.Revision, first.Revision);
        Equal(first.Revision, second.Revision);
        Equal(project.Revision, session.Current.Revision);
    }

    private static async Task SqliteCommitReopens()
    {
        var root = Path.Combine(Path.GetTempPath(), $"mapwright-contract-{Guid.NewGuid():N}");
        try
        {
            var project = Fixture();
            var repository = new SqliteProjectRepository(root, new FixedClock());
            await repository.CreateAsync(project);
            var renderer = new RecordingRenderer([]);
            await using (var session = new EditSession(project, repository, renderer))
            {
                var layer = project.RequireRole(TerrainRole.Foreground);
                var command = new AddTextureStroke(CommandId.New(), project.Revision, layer.Id,
                    TextureStroke(new MapPoint(70, 80), 12, new MapPoint(90, 110)));
                var acknowledgement = await session.ExecuteAsync(command);
                Equal(project.Revision + 1, acknowledgement.Revision);
            }

            var reopened = await new SqliteProjectRepository(root).LoadAsync(project.ProjectId,
                CancellationToken.None);
            Equal(project.Revision + 1, reopened.Revision);
            Equal(1, reopened.RequireRole(TerrainRole.Foreground).Strokes.Length);
            Equal(TerrainRole.Background, reopened.TerrainLayers[0].Role);
            Equal(TerrainRole.Foreground, reopened.TerrainLayers[1].Role);
            True(File.Exists(Path.Combine(root, "scene.sqlite")), "The authoritative database is missing.");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static async Task SqliteAcceptsOneSuccessor()
    {
        var root = Path.Combine(Path.GetTempPath(), $"mapwright-conflict-{Guid.NewGuid():N}");
        try
        {
            var project = Fixture();
            var repository = new SqliteProjectRepository(root, new FixedClock());
            await repository.CreateAsync(project);
            var foreground = project.RequireRole(TerrainRole.Foreground);
            var first = new AddTextureStroke(CommandId.New(), project.Revision, foreground.Id,
                TextureStroke(new MapPoint(20, 20), 9));
            var second = new AddTextureStroke(CommandId.New(), project.Revision, foreground.Id,
                TextureStroke(new MapPoint(30, 30), 9));
            await repository.CommitAsync(project, first, first.Apply(project), CancellationToken.None);
            await ThrowsAsync<RevisionConflictException>(() =>
                repository.CommitAsync(project, second, second.Apply(project), CancellationToken.None));
            var reopened = await repository.LoadAsync(project.ProjectId, CancellationToken.None);
            Equal(project.Revision + 1, reopened.Revision);
            Equal(1, reopened.RequireRole(TerrainRole.Foreground).Strokes.Length);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static MapProject Fixture()
    {
        var background = new TerrainLayer(LayerId.New(), "Background", true, false, 1,
            new CoastlineStyle(0), ImmutableArray<PaintStroke>.Empty, TerrainRole.Background);
        var foreground = new TerrainLayer(LayerId.New(), "Foreground", true, false, 1,
            new CoastlineStyle(22), ImmutableArray<PaintStroke>.Empty, TerrainRole.Foreground);
        return new MapProject(ProjectId.New(), MapId.New(), "Fixture", 1024, 1024, 7,
            [background, foreground],
            new River(RiverId.New(), foreground.Id,
                [new MapPoint(100, 100), new MapPoint(240, 180), new MapPoint(300, 320)], 40));
    }

    private static PaintStroke TextureStroke(MapPoint first, double radius, params MapPoint[] remaining) =>
        new(StrokeId.New(), [first, .. remaining], Brush(radius), false, TerrainStrokeKind.Texture);

    private static ResolvedBrush Brush(double radius) => new(
        new string('a', 64), radius, 0.8, 0.7, 0.5, 0.2, 0, 42, 1);

    private static void True(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Equal<T>(T expected, T actual) where T : notnull
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected {expected}; got {actual}.");
    }

    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }

    private static async Task ThrowsAsync<T>(Func<Task> action) where T : Exception
    {
        try { await action(); }
        catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }
}

public sealed class RecordingRepository(List<string> events) : IProjectRepository
{
    public bool Fail { get; init; }
    public MapProject? Loaded { get; init; }

    public Task<MapProject> LoadAsync(ProjectId projectId, CancellationToken cancellationToken) =>
        Task.FromResult(Loaded ?? throw new NotSupportedException());

    public Task<DurableCommit> CommitAsync(MapProject previous, IEditCommand command,
        DocumentChange change, CancellationToken cancellationToken)
    {
        events.Add("commit-start");
        if (Fail) throw new IOException("Injected durable storage failure.");
        events.Add("commit-durable");
        return Task.FromResult(new DurableCommit(command.Id, change.Project.Revision,
            DateTimeOffset.UnixEpoch));
    }
}

public sealed class RecordingRenderer(List<string> events) : IRenderInvalidationQueue
{
    public void Enqueue(ProjectId projectId, long revision, CommandId commandId,
        TileInvalidation invalidation) => events.Add("render-enqueued");
}

public sealed class FixedClock : IApplicationClock
{
    public DateTimeOffset UtcNow => new(2026, 9, 22, 0, 0, 0, TimeSpan.Zero);
}

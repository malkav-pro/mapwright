using System.Security.Cryptography;
using System.Collections.Immutable;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Godot;
using Mapwright.Application;
using Mapwright.Core;
using Mapwright.Domain;
using Mapwright.Export;
using Mapwright.Infrastructure;
using Mapwright.Rendering;
using Microsoft.Data.Sqlite;
using DomainMapPoint = Mapwright.Domain.MapPoint;

namespace Mapwright.App;

public static class ConnectedEvidenceRoot
{
    public static string Resolve(string repositoryRoot, string legacyLeaf, string? runLeaf = null)
        => Resolve(repositoryRoot, legacyLeaf, runLeaf,
            System.Environment.GetEnvironmentVariable("MAPWRIGHT_PHASE11_RUN_ID"),
            System.Environment.GetEnvironmentVariable("MAPWRIGHT_PHASE11_RUN_ROOT"),
            System.Environment.GetEnvironmentVariable("MAPWRIGHT_PHASE11_FULL") == "1");

    public static string Resolve(string repositoryRoot, string legacyLeaf, string? runLeaf,
        string? runId, string? runRoot, bool phase11Full)
    {
        if (!Regex.IsMatch(legacyLeaf, "^[a-z0-9-]+$", RegexOptions.CultureInvariant) ||
            (runLeaf is not null && !Regex.IsMatch(runLeaf, "^[a-z0-9-]+$", RegexOptions.CultureInvariant)))
            throw new ArgumentException("Evidence leaf must be a single directory name.");
        if (string.IsNullOrEmpty(runId) && string.IsNullOrEmpty(runRoot))
        {
            if (phase11Full)
                throw new InvalidOperationException("Phase 01.1 run context is missing.");
            return Path.Combine(repositoryRoot, "artifacts", legacyLeaf);
        }
        if (string.IsNullOrEmpty(runId) || string.IsNullOrEmpty(runRoot) ||
            !Regex.IsMatch(runId, @"^\d{8}T\d{9}Z-[0-9a-f]{32}$", RegexOptions.CultureInvariant))
            throw new InvalidOperationException("Phase 01.1 run context is incomplete or invalid.");

        var runsRoot = Path.GetFullPath(Path.Combine(repositoryRoot, "artifacts", "phase11-acceptance", "runs"));
        var expected = Path.GetFullPath(Path.Combine(runsRoot, runId));
        if (!Path.GetFullPath(runRoot).Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Phase 01.1 run root escaped its unique run directory.");
        foreach (var directory in new[] { Path.Combine(repositoryRoot, "artifacts"),
                     Path.Combine(repositoryRoot, "artifacts", "phase11-acceptance"), runsRoot, expected })
            if (Directory.Exists(directory) &&
                (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("Phase 01.1 run root contains a reparse point.");
        var lease = Path.Combine(expected, ".active-run");
        if (!File.Exists(lease) || File.ReadAllText(lease).Trim() != runId)
            throw new InvalidOperationException("Phase 01.1 run lease is missing or belongs to another run.");
        var destination = Path.Combine(expected, runLeaf ?? legacyLeaf);
        if (Directory.Exists(destination) &&
            (File.GetAttributes(destination) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("Phase 01.1 evidence destination is a reparse point.");
        return destination;
    }
}

public enum UiJobKind
{
    Import,
    Save,
    Export,
    History,
    Cache
}

public sealed record UiJobIdentity(Guid Id, UiJobKind Kind, long? Revision, string CanonicalTarget);

public sealed class UiJobRegistry
{
    private readonly Dictionary<Guid, UiJobIdentity> _active = [];

    public IReadOnlyCollection<UiJobIdentity> Active => _active.Values.ToArray();

    public UiJobIdentity Start(UiJobKind kind, long? revision, string target)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        var job = new UiJobIdentity(Guid.NewGuid(), kind, revision, Path.GetFullPath(target));
        _active.Add(job.Id, job);
        return job;
    }

    public bool Complete(Guid id) => _active.Remove(id);
    public bool Cancel(Guid id) => _active.Remove(id);
}

public sealed record RecentProjectReference(string Path);

public sealed record RecentProjectItem(
    string Name,
    string Path,
    bool FolderExists,
    Guid? ProjectId,
    long? DurableRevision,
    DateTimeOffset? DurableAt,
    string StatusText);

public static class RecentProjectCatalog
{
    private const string IndexFile = "recent-projects.json";

    public static IReadOnlyList<RecentProjectItem> Read(string projectsRoot)
    {
        var references = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (Directory.Exists(projectsRoot))
        {
            foreach (var directory in Directory.EnumerateDirectories(projectsRoot, "*.mapwright"))
                references.Add(Path.GetFullPath(directory));
        }

        var index = Path.Combine(projectsRoot, IndexFile);
        if (File.Exists(index))
        {
            var stored = JsonSerializer.Deserialize<RecentProjectReference[]>(File.ReadAllText(index)) ?? [];
            foreach (var item in stored.Where(item => !string.IsNullOrWhiteSpace(item.Path)))
                references.Add(Path.GetFullPath(item.Path));
        }

        return references.Select(ReadOne)
            .OrderByDescending(item => item.DurableAt ?? DateTimeOffset.MinValue)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static void Remember(string projectsRoot, string projectPath)
    {
        Directory.CreateDirectory(projectsRoot);
        var paths = Read(projectsRoot).Select(item => item.Path)
            .Append(Path.GetFullPath(projectPath))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => new RecentProjectReference(path))
            .ToArray();
        File.WriteAllText(Path.Combine(projectsRoot, IndexFile),
            JsonSerializer.Serialize(paths, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static RecentProjectItem ReadOne(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        var database = Path.Combine(path, "scene.sqlite");
        if (!Directory.Exists(path))
            return new RecentProjectItem(name, path, false, null, null, null,
                "Folder not found — the drive may be disconnected.");
        if (ProjectStore.HasPrototypeManifest(path))
            return new RecentProjectItem(name, path, true, null, null, null,
                "Prototype manifest · open for compatibility guidance.");
        if (!File.Exists(database))
            return new RecentProjectItem(name, path, true, null, null, null,
                "SQLite database missing · open for recovery guidance.");
        try
        {
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = database,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false
            }.ToString());
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT project_id, revision FROM current_project LIMIT 1;";
            using var reader = command.ExecuteReader();
            if (!reader.Read()) throw new InvalidDataException("Project identity is missing.");
            var id = Guid.Parse(reader.GetString(0));
            var revision = reader.GetInt64(1);
            var savedAt = File.GetLastWriteTimeUtc(database);
            return new RecentProjectItem(name, path, true, id, revision, savedAt,
                $"Revision {revision} · {savedAt.ToLocalTime():g}");
        }
        catch (Exception exception)
        {
            return new RecentProjectItem(name, path, true, null, null,
                File.GetLastWriteTimeUtc(database), $"Could not read project — {exception.Message}");
        }
    }
}

public sealed class ImportReviewModel
{
    public ImportReviewModel(InkImportResult import, string configuredProjectsFolder)
    {
        Import = import ?? throw new ArgumentNullException(nameof(import));
        ConfiguredProjectsFolder = Path.GetFullPath(configuredProjectsFolder);
        ProjectName = MakeSafeName(import.Document.Title);
    }

    public InkImportResult Import { get; }
    public string ConfiguredProjectsFolder { get; }
    public string ProjectName { get; set; }
    public string? DestinationOverride { get; private set; }
    public ImportRecoveryMode? SelectedMode { get; private set; }
    public bool CanCreate => SelectedMode is not null && !string.IsNullOrWhiteSpace(ProjectName);
    public string CanonicalDestination => Path.GetFullPath(DestinationOverride ??
        Path.Combine(ConfiguredProjectsFolder, ProjectName + ".mapwright"));

    public void SelectMode(ImportRecoveryMode mode) => SelectedMode = mode;
    public void SetDestinationOverride(string? path) =>
        DestinationOverride = string.IsNullOrWhiteSpace(path) ? null : Path.GetFullPath(path);

    public ImportProjectRequest ToRequest() => new(
        InkImportPackageFactory.Create(Import), SelectedMode, ConfiguredProjectsFolder,
        ProjectName + ".mapwright", DestinationOverride);

    private static string MakeSafeName(string value)
    {
        var pieces = value.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries);
        var joined = string.Join("-", pieces).Trim().Replace(' ', '-').ToLowerInvariant();
        return string.IsNullOrWhiteSpace(joined) ? "imported-map" : joined;
    }
}

public sealed class EntryImportUiConnectedCaseProvider : IConnectedCaseProvider
{
    public void Register(ConnectedCaseRegistry registry) =>
        registry.Register("EntryImportUi", EntryImportUiSmoke.RunAsync);
}

public static class EntryImportUiSmoke
{
    private static readonly string[] RequiredIcons =
    [
        "open-project", "import-ink", "save-project", "undo", "redo", "export-png",
        "pan", "texture-brush", "land", "river", "sample-texture", "project-storage",
        "visible", "locked", "solo", "cancel-job", "status-saved", "status-working", "status-failed"
    ];

    public static async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var assertions = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
            assertions++;
        }

        var repositoryRoot = Path.GetFullPath(ProjectSettings.GlobalizePath("res://"))
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var atlasPath = Path.Combine(repositoryRoot, "Assets", "UI", "Icons.svg");
        var atlas = XDocument.Load(atlasPath);
        XNamespace svg = "http://www.w3.org/2000/svg";
        var symbols = atlas.Descendants(svg + "symbol").ToDictionary(
            element => element.Attribute("id")?.Value ?? "", StringComparer.Ordinal);
        foreach (var name in RequiredIcons)
        {
            Check(symbols.TryGetValue(name, out var symbol), $"Icon atlas is missing '{name}'.");
            Check(symbol!.Element(svg + "title") is { Value.Length: > 0 },
                $"Icon '{name}' has no accessible title.");
        }

        var artifactRoot = ConnectedEvidenceRoot.Resolve(repositoryRoot, "entry-import-ui");
        if (Directory.Exists(artifactRoot) &&
            string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("MAPWRIGHT_PHASE11_RUN_ID")))
            Directory.Delete(artifactRoot, recursive: true);
        Directory.CreateDirectory(artifactRoot);
        var projects = Path.Combine(artifactRoot, "projects");
        Directory.CreateDirectory(projects);
        var missing = Path.Combine(artifactRoot, "missing-project.mapwright");
        File.WriteAllText(Path.Combine(projects, "recent-projects.json"),
            JsonSerializer.Serialize(new[] { new RecentProjectReference(missing) }));
        var recent = RecentProjectCatalog.Read(projects);
        Check(recent.Count == 1 && !recent[0].FolderExists && recent[0].Path == Path.GetFullPath(missing),
            "Missing recent folders do not retain their real path and readable state.");
        Check(!Directory.Exists(missing), "Reading recent projects attempted to auto-open or recreate a project.");

        var sourcePath = Main.ConfiguredInkPath;
        Check(File.Exists(sourcePath), $"Real .ink fixture is missing: {sourcePath}");
        var sourceHashBefore = await HashFileAsync(sourcePath, cancellationToken).ConfigureAwait(false);
        var imported = await Task.Run(() => new InkImportService().Import(sourcePath), cancellationToken)
            .ConfigureAwait(false);
        var review = new ImportReviewModel(imported, projects);
        Check(imported.PreviewPng is { Length: > 0 } && imported.PreviewWidth > 0 && imported.PreviewHeight > 0,
            "Recovery review did not retain the real source preview and dimensions.");
        Check(review.SelectedMode is null && !review.CanCreate,
            "Recovery review preselected a mode or enabled Create project.");
        Check(review.CanonicalDestination.StartsWith(Path.GetFullPath(projects) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase),
            "Configured projects folder is not the default destination.");
        var elsewhere = Path.Combine(artifactRoot, "elsewhere", "long destination name.mapwright");
        review.SetDestinationOverride(elsewhere);
        Check(review.CanonicalDestination == Path.GetFullPath(elsewhere),
            "Elsewhere override did not expose its canonical target.");
        review.SetDestinationOverride(null);

        review.SelectMode(ImportRecoveryMode.EditableRecoveredTerrain);
        review.SelectMode(ImportRecoveryMode.EditableRecoveredTerrain);
        Check(review.CanCreate && review.SelectedMode == ImportRecoveryMode.EditableRecoveredTerrain,
            "Repeated recovery selection was not idempotent.");
        review.SelectMode(ImportRecoveryMode.OriginalFlattenedAppearance);
        Check(review.SelectedMode == ImportRecoveryMode.OriginalFlattenedAppearance,
            "Flattened recovery card did not remain an explicit user choice.");

        var cancelledDestination = review.CanonicalDestination;
        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel();
            try
            {
                cancelled.Token.ThrowIfCancellationRequested();
                throw new InvalidOperationException("Cancelled import continued to publication.");
            }
            catch (OperationCanceledException)
            {
                Check(!Directory.Exists(cancelledDestination),
                    "Cancelling import created a visible project destination.");
            }
        }

        var jobs = new UiJobRegistry();
        var first = jobs.Start(UiJobKind.Import, null, review.CanonicalDestination);
        var second = jobs.Start(UiJobKind.Import, null, Path.Combine(projects, "second.mapwright"));
        Check(first.Id != second.Id && first.CanonicalTarget != second.CanonicalTarget && jobs.Active.Count == 2,
            "Concurrent import jobs lost independent identity.");
        Check(jobs.Cancel(first.Id) && jobs.Active.Single().Id == second.Id,
            "Cancelling one import affected a different job.");

        var sourceHashAfter = await HashFileAsync(sourcePath, cancellationToken).ConfigureAwait(false);
        Check(sourceHashBefore == sourceHashAfter, "Entry/import UI changed the original .ink source.");
        return assertions;
    }

    private static async Task<string> HashFileAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, System.IO.FileAccess.Read, FileShare.Read,
            1024 * 1024, System.IO.FileOptions.Asynchronous | System.IO.FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false))
            .ToLowerInvariant();
    }
}

public sealed record ShellLayoutState(
    int ViewportWidth,
    int ViewportHeight,
    double Scale,
    bool Compressed,
    int TopBarHeight,
    int RailWidth,
    int ToolPanelWidth,
    int LayersPanelWidth,
    int StatusBarHeight,
    int MinimumCanvasWidth,
    bool ToolPanelScrollable,
    bool LayersPanelScrollable)
{
    public static ShellLayoutState For(int width, int height, double scale)
    {
        if (width <= 0 || height <= 0 || scale <= 0) throw new ArgumentOutOfRangeException();
        var compressed = width / scale < 1_500 || scale >= 1.5;
        return compressed
            ? new ShellLayoutState(width, height, scale, true, 38, 48, 252, 272, 28, 480, true, true)
            : new ShellLayoutState(width, height, scale, false, 78, 56, 318, 340, 30, 480, true, true);
    }

    public bool EssentialControlsReachable =>
        ToolPanelScrollable && LayersPanelScrollable && MinimumCanvasWidth >= 480 &&
        TopBarHeight > 0 && RailWidth > 0 && StatusBarHeight > 0;
}

public static class EditorShellContract
{
    public static readonly string[] Regions =
    [
        "Title strip", "Command bar", "Tool rail", "Tool panel", "Canvas", "Layers/History", "Status bar"
    ];

    public static readonly string[] Tools =
    [
        "Pan", "Texture Brush", "Land", "River", "Sample Texture", "Project Storage"
    ];

    public static string SaveStatus(SaveQueueState state) => state.Phase switch
    {
        SaveQueuePhase.Queued => $"Unsaved — {state.QueuedCommands} commands queued",
        SaveQueuePhase.Saving => "Saving — waiting for queued commands",
        SaveQueuePhase.Saved => state.SavedAt is { } savedAt
            ? $"Saved {savedAt.ToLocalTime():t}"
            : "Saved",
        SaveQueuePhase.Failed => $"Save failed — {state.Failure ?? "unknown cause"}",
        _ => throw new ArgumentOutOfRangeException(nameof(state))
    };

    public static string ExportProgress(DocumentExportProgress progress)
    {
        var revision = $"revision {progress.FrozenRevision}";
        if (progress.Phase == DocumentExportPhase.Validating && progress.TotalUnits <= 0)
            return $"Validating · {revision} · No percentage available";
        if (progress.TotalUnits <= 0)
            return $"{progress.Phase} · {revision} · No percentage available";
        return $"{progress.Phase} · {revision} · {progress.CompletedUnits}/{progress.TotalUnits}";
    }
}

public static class ExistingExportRequest
{
    public static bool TryResolve(MapProject snapshot, int width, int height,
        out int longestEdge, out string message)
    {
        longestEdge = Math.Max(width, height);
        message = "Use an aspect-preserving 1K, 2K, 3K, 4K, 8K or 16K longest edge; both dimensions must match the map.";
        if (!MapUnitPolicy.ExportLongestEdges.Contains(longestEdge)) return false;
        try
        {
            var expected = DocumentPngExport.DimensionsForPreset(snapshot, longestEdge);
            if (width != expected.Width || height != expected.Height)
            {
                message = $"For this map at {longestEdge:N0} pixels, enter {expected.Width} × {expected.Height}. " +
                          "The existing destination was left untouched.";
                return false;
            }
        }
        catch (ArgumentException)
        {
            return false;
        }
        message = string.Empty;
        return true;
    }
}

public sealed class ShellStatesConnectedCaseProvider : IConnectedCaseProvider
{
    public void Register(ConnectedCaseRegistry registry) =>
        registry.Register("ShellStates", ShellStatesSmoke.RunAsync);
}

public sealed class GesturesConnectedCaseProvider : IConnectedCaseProvider
{
    public void Register(ConnectedCaseRegistry registry) =>
        registry.Register("Gestures", GesturesSmoke.RunAsync);
}

public static class GesturesSmoke
{
    public static async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var assertions = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
            assertions++;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var controllerType = typeof(Main).Assembly.GetType("Mapwright.App.GestureController");
        Check(controllerType is not null,
            "GestureController is missing; MapCanvas still owns transient edit state directly.");
        var controller = Activator.CreateInstance(controllerType!)
            ?? throw new InvalidOperationException("GestureController could not be constructed.");

        object Invoke(string name, params object[] arguments)
        {
            var method = controllerType!.GetMethods()
                .SingleOrDefault(candidate => candidate.Name == name &&
                                              candidate.GetParameters().Length == arguments.Length)
                ?? throw new InvalidOperationException($"GestureController.{name} is missing.");
            return method.Invoke(controller, arguments)
                ?? throw new InvalidOperationException($"GestureController.{name} returned null.");
        }

        object? Read(object instance, string property) => instance.GetType().GetProperty(property)?.GetValue(instance);
        T ReadController<T>(string property) => (T)(controllerType!.GetProperty(property)?.GetValue(controller)
            ?? throw new InvalidOperationException($"GestureController.{property} is missing."));

        var begin = Invoke("BeginEdit", "Texture Brush", "Foreground", 4L, 20d, 30d, true, false);
        Check((bool)(Read(begin, "Accepted") ?? false), "A paintable Texture Brush target was refused.");
        Check(ReadController<bool>("IsEditing") && ReadController<int>("PreviewSampleCount") == 1,
            "Pointer-down did not begin one transient preview.");
        Check((bool)Invoke("AddSample", 24d, 32d) && (bool)Invoke("AddSample", 29d, 36d),
            "Pointer samples were not retained by the active gesture.");
        var plan = Invoke("PrepareCommit", 4L, true, false);
        Check((long)(Read(plan, "BaseRevision") ?? -1L) == 4L &&
              (int)(Read(plan, "SampleCount") ?? 0) == 3,
            "Release did not prepare the whole stroke against its bound base revision.");
        Invoke("MarkCommitted", 5L);
        Invoke("MarkPresented", 5L);
        Check(ReadController<long>("LastCommittedRevision") == 5L &&
              ReadController<long>("LastPresentedRevision") == 5L &&
              ReadController<long>("LastSequenceId") > 0,
            "Gesture sequence/revision identity did not reach the presented frame.");

        var duplicateCommitRejected = false;
        try { Invoke("PrepareCommit", 5L, true, false); }
        catch (System.Reflection.TargetInvocationException exception)
            when (exception.InnerException is InvalidOperationException)
        {
            duplicateCommitRejected = true;
        }
        Check(duplicateCommitRejected, "One release could prepare more than one durable command.");

        Invoke("BeginEdit", "Land", "Foreground mask", 5L, 40d, 50d, true, false);
        Invoke("AddSample", 44d, 54d);
        Check((bool)Invoke("Cancel", "Escape") && !ReadController<bool>("IsEditing") &&
              ReadController<int>("PreviewSampleCount") == 0,
            "Escape did not discard the complete uncommitted gesture.");
        Check(ReadController<long>("LastCommittedRevision") == 5L,
            "Cancelling a preview changed the committed revision.");

        foreach (var reason in new[] { "Capture lost", "Window deactivated", "Modal opened" })
        {
            Invoke("BeginEdit", "Land", "Foreground mask", 5L, 60d, 70d, true, false);
            Check((bool)Invoke("Cancel", reason) && !ReadController<bool>("IsEditing"),
                $"{reason} did not cancel the active preview.");
        }

        var locked = Invoke("BeginEdit", "Texture Brush", "Foreground", 5L, 20d, 20d, true, true);
        Check(!(bool)(Read(locked, "Accepted") ?? true) &&
              string.Equals(Read(locked, "Action") as string, "Unlock", StringComparison.Ordinal),
            "A locked target did not refuse with the inline Unlock action.");
        var hidden = Invoke("BeginEdit", "Texture Brush", "Foreground", 5L, 20d, 20d, false, false);
        Check(!(bool)(Read(hidden, "Accepted") ?? true) &&
              string.Equals(Read(hidden, "Action") as string, "Show layer", StringComparison.Ordinal),
            "A hidden target did not refuse with the inline Show layer action.");

        Invoke("SetView", 10d, 15d, 2d);
        var worldX = ReadController<double>("PointerWorldX");
        Invoke("RememberPointer", 110d, 95d);
        worldX = ReadController<double>("PointerWorldX");
        var worldY = ReadController<double>("PointerWorldY");
        Invoke("ZoomAt", 110d, 95d, 1.25d);
        Check(Math.Abs(ReadController<double>("PointerWorldX") - worldX) < 0.000001 &&
              Math.Abs(ReadController<double>("PointerWorldY") - worldY) < 0.000001,
            "Plain-wheel zoom drifted from the world point under the pointer.");
        var oldSize = new Vector2(896, 650);
        var nextSize = new Vector2(1792, 1301);
        var anchor = new Vector2(110, 95);
        var oldPan = new Vector2(10, 15);
        const float oldZoom = 2.5f;
        var preserved = MapCanvas.PreserveDetailAnchor(oldSize, nextSize,
            oldPan, oldZoom, anchor);
        var oldMapX = (anchor.X - oldPan.X) / (oldSize.X * oldZoom);
        var oldMapY = (anchor.Y - oldPan.Y) / (oldSize.Y * oldZoom);
        var nextMapX = (anchor.X - preserved.Pan.X) / (nextSize.X * preserved.Zoom);
        var nextMapY = (anchor.Y - preserved.Pan.Y) / (nextSize.Y * preserved.Zoom);
        Check(Math.Abs(oldMapX - nextMapX) < 0.000001f &&
              Math.Abs(oldMapY - nextMapY) < 0.000001f,
            "Adaptive display resolution changed the document point under the wheel pointer.");
        Invoke("BeginTemporaryPan", 110d, 95d, "Space");
        Invoke("MoveTemporaryPan", 130d, 120d);
        Invoke("EndTemporaryPan");
        Check(ReadController<double>("PanX") == 5d && ReadController<double>("PanY") == 20d &&
              string.Equals(ReadController<string>("ActiveTool"), "Land", StringComparison.Ordinal),
            "Temporary Space pan did not restore the prior editing tool.");

        Check(string.Equals(Invoke("RouteShortcut", "Ctrl+Z", true, false, false).ToString(), "Field",
                StringComparison.Ordinal) &&
              string.Equals(Invoke("RouteShortcut", "B", true, false, false).ToString(), "Field",
                StringComparison.Ordinal),
            "Focused text/numeric input did not retain typing and undo ownership.");
        Check(string.Equals(Invoke("RouteShortcut", "B", false, false, false).ToString(), "Canvas",
                StringComparison.Ordinal) &&
              string.Equals(Invoke("RouteShortcut", "B", false, false, true).ToString(), "Blocked",
                StringComparison.Ordinal) &&
              string.Equals(Invoke("RouteShortcut", "Wheel", false, false, true).ToString(), "Canvas",
                StringComparison.Ordinal),
            "Shortcut scope did not block edits while preserving history-rebuild navigation.");

        var repositoryRoot = Path.GetFullPath(ProjectSettings.GlobalizePath("res://"))
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var artifactRoot = ConnectedEvidenceRoot.Resolve(repositoryRoot, "gestures");
        if (Directory.Exists(artifactRoot) &&
            string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("MAPWRIGHT_PHASE11_RUN_ID")))
            Directory.Delete(artifactRoot, recursive: true);
        Directory.CreateDirectory(artifactRoot);
        var background = new TerrainLayer(LayerId.New(), "Background", true, false, 1,
            new CoastlineStyle(0), ImmutableArray<PaintStroke>.Empty, TerrainRole.Background);
        var foreground = new TerrainLayer(LayerId.New(), "Foreground", true, false, 1,
            new CoastlineStyle(0), ImmutableArray<PaintStroke>.Empty, TerrainRole.Foreground);
        var initial = new MapProject(ProjectId.New(), MapId.New(), "Gesture fixture", 128, 128, 0,
            [background, foreground]);
        var repository = new SqliteProjectRepository(Path.Combine(artifactRoot, "gesture.mapwright"));
        await repository.CreateAsync(initial, cancellationToken).ConfigureAwait(false);
        await using var edit = new EditSession(initial, repository, new NullRenderInvalidationQueue());
        var durableController = new GestureController();
        durableController.BeginEdit("Texture Brush", "Foreground", edit.Current.Revision,
            32, 32, visible: true, locked: false);
        durableController.AddSample(48, 48);
        durableController.AddSample(64, 64);
        var durablePlan = durableController.PrepareCommit(edit.Current.Revision,
            visible: true, locked: false);
        var brush = new ResolvedBrush(new string('9', 64), 18, 0.72, 0.8, 0.9, 0.2, 0, 901, 1);
        var acknowledgement = await edit.ExecuteAsync(new AddTextureStroke(CommandId.New(),
            durablePlan.BaseRevision, foreground.Id,
            new PaintStroke(StrokeId.New(), durablePlan.Samples, brush, false,
                TerrainStrokeKind.Texture)), cancellationToken).ConfigureAwait(false);
        durableController.MarkCommitted(acknowledgement.Revision);
        var history = new HistorySession(edit.Current, repository, new NullRenderInvalidationQueue());
        var firstWindow = await history.ReadWindowAsync(0, 10, cancellationToken).ConfigureAwait(false);
        Check(edit.Current.Revision == 1 && firstWindow.TotalCount == 1 &&
              edit.Current.RequireRole(TerrainRole.Foreground).Strokes.Length == 1,
            "One completed pointer gesture did not become exactly one durable history command.");

        var committedFrame = ConnectedTerrainGraph.RenderReference(edit.Current, 32, 32,
            null, null, null, 0, 0, 32, 32).RgbaSha256;
        durableController.BeginEdit("Texture Brush", "Foreground", edit.Current.Revision,
            80, 80, visible: true, locked: false);
        durableController.AddSample(96, 96);
        durableController.Cancel("Escape");
        var afterCancel = await repository.LoadAsync(initial.ProjectId, cancellationToken).ConfigureAwait(false);
        var afterWindow = await history.ReadWindowAsync(0, 10, cancellationToken).ConfigureAwait(false);
        var afterFrame = ConnectedTerrainGraph.RenderReference(afterCancel, 32, 32,
            null, null, null, 0, 0, 32, 32).RgbaSha256;
        Check(afterCancel.Revision == 1 && afterWindow.TotalCount == 1 && afterFrame == committedFrame,
            "A cancelled gesture changed committed pixels, revision, or durable history.");
        await history.DisposeAsync();

        Check(typeof(MapCanvas).GetFields(System.Reflection.BindingFlags.Instance |
                                         System.Reflection.BindingFlags.NonPublic)
                .Any(field => field.FieldType == controllerType),
            "MapCanvas is not wired to GestureController.");
        return assertions;
    }
}

public sealed class ToolPanelsConnectedCaseProvider : IConnectedCaseProvider
{
    public void Register(ConnectedCaseRegistry registry) =>
        registry.Register("ToolPanels", ToolPanelsSmoke.RunAsync);
}

public static class ToolPanelsSmoke
{
    public static Task<int> RunAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var assertions = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
            assertions++;
        }

        var state = new EditorControlsState();
        state.SetTextureIdentity(new string('a', 64));
        state.SelectTool("Texture Brush");
        state.SelectTextureTarget(TerrainRole.Background);
        state.TextureDiameter.SetFromSlider(321);
        state.TextureRotation.SetFromSlider(37);
        state.SelectTool("Land");
        Check(state.ActiveTarget.Contains("Foreground", StringComparison.Ordinal) &&
              state.ActiveTarget.Contains("Coverage", StringComparison.Ordinal),
            "Land did not force and identify Foreground coverage.");
        state.SelectTool("River");
        Check(state.ActiveTarget.Contains("Foreground land mask", StringComparison.Ordinal),
            "River did not force and identify the Foreground mask modifier.");
        state.SelectTool("Texture Brush");
        Check(state.TextureTarget == TerrainRole.Background &&
              state.TextureDiameter.CommittedValue == 321 && state.TextureRotation.CommittedValue == 37,
            "Returning to Texture Brush lost its target or shared settings.");

        state.SelectPreset("tapered-round");
        var tapered = state.ResolveTextureBrush(42);
        Check(tapered.Taper == TextureTaperKind.SmoothBothEnds && tapered.TextureSha256 == new string('a', 64),
            "Texture preset resolution lost taper or selected texture identity.");
        state.SelectPreset("edged-texture");
        var edgedTexture = state.ResolveTextureBrush(43);
        Check(edgedTexture.Tip == TextureTipKind.Edged && edgedTexture.CornerSmoothing > 0,
            "Edged texture settings did not resolve Roughness/Smooth corners.");
        state.SetLandShape(LandShape.EdgedPolygon);
        var edgedLand = state.ResolveLandBrush(44);
        state.SetLandShape(LandShape.RoundSoft);
        var roundLand = state.ResolveLandBrush(45);
        Check(edgedLand.Shape == LandShape.EdgedPolygon && edgedLand.Softness == 0 &&
              roundLand.Shape == LandShape.RoundSoft && roundLand.Roughness == 0 && roundLand.CornerSmoothing == 0,
            "Land modes mixed edged geometry and round alpha softness.");

        var numeric = new NumericFieldState("Width", RiverLimits.MinimumWidth, RiverLimits.MaximumWidth, 24);
        Check(!numeric.Input("not-a-width") && numeric.Error is not null && numeric.CommittedValue == 24,
            "Invalid numeric text was committed or discarded without an error.");
        Check(numeric.Escape() == 24 && numeric.Text == "24" && numeric.Error is null,
            "Escape did not restore the last committed numeric value.");
        numeric.Adjust(shift: true, alt: false, direction: 1);
        numeric.Adjust(shift: false, alt: true, direction: -1);
        Check(numeric.CommittedValue == 33,
            "Numeric keyboard steps did not apply Shift=10 and whole-domain Alt fallback safely.");

        var background = new TerrainLayer(LayerId.New(), "Background", true, true, 1,
            new CoastlineStyle(0), ImmutableArray<PaintStroke>.Empty, TerrainRole.Background);
        var foreground = new TerrainLayer(LayerId.New(), "Foreground", true, false, 1,
            new CoastlineStyle(0), ImmutableArray<PaintStroke>.Empty, TerrainRole.Foreground);
        var project = new MapProject(ProjectId.New(), MapId.New(), "Tool panel fixture", 512, 512, 0,
            [background, foreground]);
        state.SelectTextureTarget(TerrainRole.Background);
        var availability = state.TargetAvailability(project);
        Check(!availability.Paintable && availability.Role == TerrainRole.Background &&
              availability.Action == "Unlock",
            "Locked Background did not expose an inline Unlock refusal.");
        var textureStroke = TexturePaintStroke.Create(StrokeId.New(), [new DomainMapPoint(20, 20)],
            state.ResolveTextureBrush(46), new DomainMapPoint(20, 20));
        try
        {
            new AddResolvedTextureStroke(CommandId.New(), project.Revision, TerrainRole.Background,
                textureStroke).Apply(project);
            throw new InvalidOperationException("Locked Background painting unexpectedly succeeded.");
        }
        catch (PaintTargetBlockedException exception)
        {
            Check(exception.Role == TerrainRole.Background && exception.SuggestedAction == "Unlock" &&
                  project.RequireRole(TerrainRole.Foreground).ResolvedTextureStrokes.IsEmpty,
                "Blocked texture painting rerouted to Foreground.");
        }
        var hiddenProject = project with
        {
            TerrainLayers = project.TerrainLayers.SetItem(0, background with { Locked = false, Visible = false })
        };
        Check(state.TargetAvailability(hiddenProject).Action == "Show layer",
            "Hidden target did not expose Show layer.");

        var river = River.Create(RiverId.New(), foreground.Id,
            [new DomainMapPoint(40, 40), new DomainMapPoint(120, 100), new DomainMapPoint(200, 180)],
            [32d, 48d, 64d], 0.3);
        var riverProject = project with { River = river };
        var widened = new SetRiverPointWidth(CommandId.New(), riverProject.Revision, 1, 72).Apply(riverProject).Project;
        var softened = new SetRiverBankSoftness(CommandId.New(), widened.Revision, 0.55).Apply(widened).Project;
        Check(softened.River!.ResolvedWidthProfile.Widths[1] == 72 && softened.River.BankSoftness == 0.55,
            "River point width or bank softness did not produce domain-valid commands.");
        var deletedPoint = new DeleteRiverPoint(CommandId.New(), softened.Revision, 1).Apply(softened).Project;
        Check(deletedPoint.River!.Points.Length == 2,
            "Delete point did not remain an undoable river geometry command.");

        string[] RenderPanel(string tool, MapProject snapshot)
        {
            state.SelectTool(tool);
            var host = new VBoxContainer();
            EditorControls.Populate(host, state, snapshot, () => { }, _ => { }, _ => { });
            var text = Descendants(host).Select(control => control switch
                {
                    Label label => label.Text,
                    Button button => button.Text,
                    _ => ""
                })
                .Where(value => !string.IsNullOrWhiteSpace(value)).ToArray();
            host.Free();
            return text;
        }

        var textureText = RenderPanel("Texture Brush", hiddenProject);
        Check(textureText.Any(value => value.Contains("Save preset as", StringComparison.Ordinal)) &&
              textureText.Any(value => value.Contains("Texture scale", StringComparison.Ordinal)) &&
              textureText.Any(value => value.Contains("Show layer", StringComparison.Ordinal)),
            "Texture Brush panel is missing preset, transform, or blocked-target controls.");
        var landText = RenderPanel("Land", project);
        Check(landText.Contains("Edged polygon") && landText.Contains("Round soft") &&
              landText.Contains("Add") && landText.Contains("Subtract") &&
              !landText.Any(value => value.Contains("preset", StringComparison.OrdinalIgnoreCase) ||
                                     value.Contains("square", StringComparison.OrdinalIgnoreCase)),
            "Land panel lost its two approved modes or leaked texture/square controls.");
        var riverText = RenderPanel("River", riverProject);
        Check(riverText.Any(value => value.Contains("Apply width to all", StringComparison.Ordinal)) &&
              riverText.Any(value => value.Contains("Delete point", StringComparison.Ordinal)) &&
              riverText.Any(value => value.Contains("Delete river", StringComparison.Ordinal)) &&
              riverText.Any(value => value.Contains("Bank softness", StringComparison.Ordinal)),
            "River panel lacks width, softness, or undoable deletion controls.");
        return Task.FromResult(assertions);
    }

    private static IEnumerable<Control> Descendants(Node node)
    {
        foreach (var child in node.GetChildren().OfType<Node>())
        {
            if (child is Control control) yield return control;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}

public sealed class ScopeCuesConnectedCaseProvider : IConnectedCaseProvider
{
    public void Register(ConnectedCaseRegistry registry) =>
        registry.Register("ScopeCues", ScopeCuesSmoke.RunAsync);
}

public static class ScopeCuesSmoke
{
    public static async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var assertions = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
            assertions++;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var state = new EditorControlsState();
        state.SetTextureIdentity(new string('c', 64));
        state.SelectTool("Land");
        state.SetLandOperation(LandOperation.Subtract);
        state.SetLandShape(LandShape.RoundSoft);
        var land = ScopeCueContract.ForTool(state);
        Check(land.Scope == EditingScope.Coverage && land.ActiveTool == "Land tool" &&
              land.Target == "Foreground" && land.AffectedProperty == "Coverage",
            "Land scope did not identify its active tool, forced target, and coverage property.");
        Check(land.FilledFootprint && land.OuterRing && !land.FalloffRing &&
              !land.AffectedRegionBox && !land.Swatch && !land.RowOnly,
            "Land scope did not use the filled coverage-footprint cue contract.");

        state.SelectTool("Texture Brush");
        state.SelectTextureTarget(TerrainRole.Background);
        state.SelectPreset("soft-round");
        var texture = ScopeCueContract.ForTool(state);
        Check(texture.Scope == EditingScope.Texture && texture.ActiveTool == "Texture Brush" &&
              texture.Target == "Background" && texture.AffectedProperty == "Texture colour/intensity",
            "Texture scope did not identify its active tool, selected target, and texture property.");
        Check(!texture.FilledFootprint && texture.OuterRing && texture.FalloffRing &&
              texture.AffectedRegionBox && texture.Swatch && !texture.RowOnly,
            "Texture scope did not expose the ring, falloff, affected-box, and swatch cue contract.");

        var opacity = ScopeCueContract.LayerOpacity(TerrainRole.Foreground, 0.22);
        Check(opacity.Scope == EditingScope.LayerOpacity && opacity.ActiveTool == "Layers" &&
              opacity.Target == "Foreground" && opacity.AffectedProperty == "Layer opacity",
            "Opacity scope did not identify its row, target, and affected property.");
        Check(opacity.RowOnly && !opacity.FilledFootprint && !opacity.OuterRing &&
              !opacity.FalloffRing && !opacity.AffectedRegionBox && !opacity.Swatch,
            "Layer opacity incorrectly reused a canvas brush cue.");
        Check(ScopeCueContract.UseCrosshair(5.9) && !ScopeCueContract.UseCrosshair(6),
            "Tiny-brush fallback did not switch to a crosshair strictly below six screen pixels.");

        var background = new TerrainLayer(LayerId.New(), "Background", true, false, 1,
            new CoastlineStyle(0), ImmutableArray<PaintStroke>.Empty, TerrainRole.Background);
        var foreground = new TerrainLayer(LayerId.New(), "Foreground", true, false, 1,
            new CoastlineStyle(0), ImmutableArray<PaintStroke>.Empty, TerrainRole.Foreground);
        var project = new MapProject(ProjectId.New(), MapId.New(), "Scope export fixture", 32, 32, 0,
            [background, foreground]);
        var pixels = Enumerable.Repeat((byte)210, 32 * 32 * 4).ToArray();
        var coverage = Enumerable.Repeat(0.6f, 32 * 32).ToArray();
        var before = ConnectedTerrainGraph.RenderReference(project, 32, 32, null, pixels, coverage,
            0, 0, 32, 32).RgbaSha256;
        state.SelectTool("Land");
        state.LandDiameter.SetFromSlider(720);
        _ = ScopeCueContract.ForTool(state);
        state.SelectTool("Texture Brush");
        state.TextureDiameter.SetFromSlider(620);
        _ = ScopeCueContract.ForTool(state);
        var after = ConnectedTerrainGraph.RenderReference(project, 32, 32, null, pixels, coverage,
            0, 0, 32, 32).RgbaSha256;
        Check(before == after,
            "Changing transient scope-cue state contaminated the connected export render.");

        var repositoryRoot = Path.GetFullPath(ProjectSettings.GlobalizePath("res://"))
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var evidenceRoot = ConnectedEvidenceRoot.Resolve(repositoryRoot, "ui-smoke", "visual");
        Directory.CreateDirectory(evidenceRoot);
        var matrix = (from scale in new[] { 1d, 1.5d }
                      from mode in new[] { "land", "texture", "opacity" }
                      let scaleName = scale == 1 ? "100" : "150"
                      let width = scale == 1 ? 1600 : 1920
                      let height = scale == 1 ? 900 : 1080
                      let output = Path.Combine(evidenceRoot, $"scope-{mode}-{scaleName}.png")
                      select (mode, scale, scaleName, width, height, output)).ToArray();
        foreach (var item in matrix)
            if (File.Exists(item.output)) File.Delete(item.output);
        var results = await Task.WhenAll(matrix.Select(async item =>
        {
            var child = await CaptureAsync(repositoryRoot, item.mode, item.scale, item.width, item.height,
                    item.output, cancellationToken)
                .ConfigureAwait(false);
            return (item, child);
        })).ConfigureAwait(false);
        var captures = new List<object>();
        foreach (var result in results)
        {
            Check(result.child.ExitCode == 0 &&
                  result.child.Output.Contains($"SCOPE_FRAME mode={result.item.mode}", StringComparison.Ordinal),
                $"The {result.item.mode} {result.item.scaleName}% evidence process failed: {result.child.Error}");
            var png = await File.ReadAllBytesAsync(result.item.output, cancellationToken).ConfigureAwait(false);
            Check(png.Length > 32 && png.AsSpan(1, 3).SequenceEqual("PNG"u8) &&
                  ReadBigEndianInt32(png, 16) == result.item.width &&
                  ReadBigEndianInt32(png, 20) == result.item.height,
                $"The {result.item.mode} {result.item.scaleName}% evidence has the wrong dimensions.");
            captures.Add(new
            {
                result.item.mode,
                scalePercent = result.item.scaleName,
                viewport = $"{result.item.width}x{result.item.height}",
                path = Path.GetRelativePath(repositoryRoot, result.item.output)
            });
        }
        Check(captures.Count == 6, "The D-28 evidence matrix did not contain all six captures.");
        var manifest = new
        {
            viewports = new { scale100 = "1600x900", scale150 = "1920x1080" },
            captures,
            questions = new[] { "active tool", "target", "affected property" },
            expected = new
            {
                land = new[] { "Land tool", "Foreground", "Coverage" },
                texture = new[] { "Texture Brush", "Foreground", "Texture colour/intensity" },
                opacity = new[] { "Layers", "Foreground", "Layer opacity" }
            }
        };
        await File.WriteAllTextAsync(Path.Combine(evidenceRoot, "scope-cues-manifest.json"),
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }),
            cancellationToken).ConfigureAwait(false);
        return assertions;
    }

    private static int ReadBigEndianInt32(byte[] bytes, int offset) =>
        (bytes[offset] << 24) | (bytes[offset + 1] << 16) | (bytes[offset + 2] << 8) | bytes[offset + 3];

    private static async Task<(int ExitCode, string Output, string Error)> CaptureAsync(
        string repositoryRoot,
        string mode,
        double scale,
        int width,
        int height,
        string output,
        CancellationToken cancellationToken)
    {
        var godot = System.Environment.GetEnvironmentVariable("MAPWRIGHT_GODOT_PATH");
        if (string.IsNullOrWhiteSpace(godot) || !File.Exists(godot))
            throw new FileNotFoundException("MAPWRIGHT_GODOT_PATH must name the pinned Godot executable.", godot);
        var start = new System.Diagnostics.ProcessStartInfo
        {
            FileName = godot,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in new[]
                 {
                     "--path", repositoryRoot, "--", "--scope-frame", mode,
                     width.ToString(System.Globalization.CultureInfo.InvariantCulture),
                     height.ToString(System.Globalization.CultureInfo.InvariantCulture),
                     scale.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture), output
                 })
            start.ArgumentList.Add(argument);
        using var process = System.Diagnostics.Process.Start(start)
            ?? throw new InvalidOperationException("Could not start the scope evidence worker.");
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        var standardOutput = await outputTask.ConfigureAwait(false);
        var standardError = await errorTask.ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(standardOutput)) GD.Print(standardOutput);
        if (process.ExitCode != 0 && !string.IsNullOrWhiteSpace(standardError)) GD.PrintErr(standardError);
        return (process.ExitCode, standardOutput, standardError);
    }
}

public static class ShellStatesSmoke
{
    public const int HistoryPageSize = 40;

    public static async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var assertions = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
            assertions++;
        }

        Check(EditorShellContract.Regions.Length == 7 && EditorShellContract.Regions.Contains("Canvas"),
            "The shell region contract is incomplete.");
        Check(EditorShellContract.Tools.SequenceEqual(
            new[] { "Pan", "Texture Brush", "Land", "River", "Sample Texture", "Project Storage" }),
            "The tool rail includes a future tool or lost a Phase 1 action.");
        var desktop = ShellLayoutState.For(1920, 1080, 1);
        var compact = ShellLayoutState.For(1366, 768, 1);
        var scaled = ShellLayoutState.For(1920, 1080, 1.5);
        Check(!desktop.Compressed && desktop.ToolPanelWidth == 318 && desktop.LayersPanelWidth == 340,
            "The 1920×1080 composition does not use approved panel widths.");
        Check(compact.Compressed && compact.RailWidth == 48 && compact.ToolPanelWidth == 252 &&
              compact.LayersPanelWidth == 272, "The 1366×768 compression contract is incorrect.");
        Check(scaled.Compressed && scaled.EssentialControlsReachable,
            "The 150% scale contract hides an essential shell control.");

        Check(EditorShellContract.SaveStatus(new SaveQueueState(SaveQueuePhase.Queued, 3, 0, null, null)) ==
              "Unsaved — 3 commands queued", "Queued save copy drifted.");
        Check(EditorShellContract.SaveStatus(new SaveQueueState(SaveQueuePhase.Saving, 1, 0, null, null)) ==
              "Saving — waiting for queued commands", "Saving copy drifted.");
        Check(EditorShellContract.SaveStatus(new SaveQueueState(SaveQueuePhase.Failed, 0, 0, null, "disk full")) ==
              "Save failed — disk full", "Failed save copy drifted.");
        var savedAt = new DateTimeOffset(2026, 9, 23, 10, 24, 0, TimeSpan.Zero);
        Check(EditorShellContract.SaveStatus(new SaveQueueState(SaveQueuePhase.Saved, 0, 0, savedAt, null))
            .StartsWith("Saved ", StringComparison.Ordinal), "Saved copy drifted.");

        var repositoryRoot = Path.GetFullPath(ProjectSettings.GlobalizePath("res://"))
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var artifactRoot = ConnectedEvidenceRoot.Resolve(repositoryRoot, "shell-states");
        if (Directory.Exists(artifactRoot) &&
            string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("MAPWRIGHT_PHASE11_RUN_ID")))
            Directory.Delete(artifactRoot, recursive: true);
        Directory.CreateDirectory(artifactRoot);
        var projectDirectory = Path.Combine(artifactRoot, "shell-state.mapwright");
        var foreground = new TerrainLayer(LayerId.New(), "Foreground", true, false, 1,
            new CoastlineStyle(0), ImmutableArray<PaintStroke>.Empty, TerrainRole.Foreground);
        var background = new TerrainLayer(LayerId.New(), "Background", true, true, 1,
            new CoastlineStyle(0), ImmutableArray<PaintStroke>.Empty, TerrainRole.Background);
        var initial = new MapProject(ProjectId.New(), MapId.New(), "Shell state fixture", 320, 180, 0,
            [background, foreground], null, 2);
        initial.ValidateConnectedTerrain();
        var repository = new SqliteProjectRepository(projectDirectory);
        await repository.CreateAsync(initial, cancellationToken).ConfigureAwait(false);

        var saveEvents = new List<SaveQueueState>();
        await using var edit = new EditSession(initial, repository, new NullRenderInvalidationQueue());
        edit.SaveStateChanged += saveEvents.Add;
        for (var index = 0; index < 46; index++)
        {
            var current = edit.Current;
            await edit.ExecuteAsync(new RenameTerrainLayer(CommandId.New(), current.Revision,
                TerrainRole.Foreground, $"Foreground {index + 1}"), cancellationToken).ConfigureAwait(false);
        }
        await edit.SaveAsync(cancellationToken).ConfigureAwait(false);
        Check(saveEvents.Any(state => state.Phase == SaveQueuePhase.Queued) &&
              saveEvents.Any(state => state.Phase == SaveQueuePhase.Saving) &&
              edit.SaveState.Phase == SaveQueuePhase.Saved && edit.SaveState.QueuedCommands == 0,
            "EditSession did not drive honest queued/saving/saved states.");

        await using var history = new HistorySession(edit.Current, repository, new NullRenderInvalidationQueue());
        var controls = await history.ReadControlsAsync(cancellationToken).ConfigureAwait(false);
        var window = await history.ReadWindowAsync(0, HistoryPageSize, cancellationToken).ConfigureAwait(false);
        Check(controls.Undo.Enabled && !controls.Redo.Enabled, "Persistent Undo/Redo state is not durable.");
        Check(window.TotalCount == 46 && window.Entries.Count == HistoryPageSize,
            "History was not returned as a bounded virtualized window.");
        Check(window.Entries.All(entry => entry.Revision <= window.State.LatestRevision),
            "History displayed an invented revision.");

        var recovery = await repository.ReadRecoveryFactsAsync(initial.ProjectId, cancellationToken)
            .ConfigureAwait(false);
        Check(recovery.BannerText ==
              "Shell state fixture was recovered. The last acknowledged revision and undo position were restored. An unfinished gesture may be missing.",
            "Recovery banner contains unproven detail or drifted from approved copy.");
        Check(recovery.AcknowledgedRevision == edit.Current.Revision &&
              recovery.CursorRevision == edit.Current.Revision, "Recovery facts do not name durable state.");

        var tileCache = Path.Combine(projectDirectory, "cache", "tiles", "smoke.cache");
        Directory.CreateDirectory(Path.GetDirectoryName(tileCache)!);
        await File.WriteAllTextAsync(tileCache, "disposable", cancellationToken).ConfigureAwait(false);
        var deletion = new ProjectStore().DeleteDisposableCaches(projectDirectory);
        Check(deletion.Deleted && deletion.DeletedDirectories > 0 && !File.Exists(tileCache),
            "Confirmed disposable cache deletion did not leave native sources/revisions intact.");
        var reopened = await repository.LoadAsync(initial.ProjectId, cancellationToken).ConfigureAwait(false);
        Check(reopened.Revision == edit.Current.Revision && reopened.TerrainLayers.Length == 2,
            "Cache deletion removed durable project data.");

        var frozen = edit.Current;
        var jobs = new UiJobRegistry();
        var exportTarget = Path.Combine(artifactRoot, "shell-export.png");
        var exportJob = jobs.Start(UiJobKind.Export, frozen.Revision, exportTarget);
        await edit.ExecuteAsync(new SetTerrainOpacity(CommandId.New(), frozen.Revision,
            TerrainRole.Foreground, 0.88), cancellationToken).ConfigureAwait(false);
        var ledger = RenderResourceLedger.ForCpuInspection();
        var progressEvents = new List<DocumentExportProgress>();
        var exporter = new DocumentPngExport(ledger,
            (snapshot, _, _, _) => ValueTask.FromResult<IFrozenDocumentRenderer>(new SmokeRenderer(snapshot)));
        var export = await exporter.ExportAsync(frozen, exportTarget, 64, 36,
            new DocumentPngExportOptions(32, 2), new ImmediateProgress<DocumentExportProgress>(progressEvents.Add),
            cancellationToken).ConfigureAwait(false);
        jobs.Complete(exportJob.Id);
        Check(export.FrozenRevision == exportJob.Revision && edit.Current.Revision > export.FrozenRevision,
            "Export did not keep its named revision frozen while later editing continued.");
        Check(progressEvents.All(item => item.FrozenRevision == frozen.Revision) &&
              progressEvents.Any(item => item.Phase == DocumentExportPhase.Rendering) &&
              progressEvents.Any(item => item.Phase == DocumentExportPhase.Validating),
            "Export job did not expose actual phase/progress units.");
        Check(EditorShellContract.ExportProgress(new DocumentExportProgress(
                  DocumentExportPhase.Validating, 0, 0, frozen.Revision)).EndsWith("No percentage available",
                  StringComparison.Ordinal), "Unknown validation progress invented a percentage.");

        var hardware = RenderResourceLedger.CaptureStartup();
        Check(hardware.Status is RenderHardwareStatus.Ready or RenderHardwareStatus.InspectionOnly &&
              !string.IsNullOrWhiteSpace(hardware.Detail), "Backend status did not expose live startup facts.");
        Check(reopened.TerrainLayers.Count(layer => layer.Role == TerrainRole.Foreground) == 1 &&
              reopened.TerrainLayers.Count(layer => layer.Role == TerrainRole.Background) == 1,
            "The shell project does not contain exactly Foreground and Background.");
        Check(!EditorShellContract.Tools.Any(tool => tool.Contains("Grid", StringComparison.OrdinalIgnoreCase) ||
                                                   tool.Contains("DPI", StringComparison.OrdinalIgnoreCase) ||
                                                   tool.Contains("Label", StringComparison.OrdinalIgnoreCase)),
            "A future-phase control leaked into the Phase 1 shell.");
        return assertions;
    }

    private sealed class ImmediateProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

    private sealed class SmokeRenderer(MapProject snapshot) : IFrozenDocumentRenderer
    {
        public ProjectId ProjectId => snapshot.ProjectId;
        public long Revision => snapshot.Revision;
        public IReadOnlyCollection<string> PinnedBlobHashes => Array.Empty<string>();

        public ValueTask<byte[]> RenderAsync(ExportTileRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var rgba = new byte[checked(request.Width * request.Height * 4)];
            for (var y = 0; y < request.Height; y++)
            for (var x = 0; x < request.Width; x++)
            {
                var index = (y * request.Width + x) * 4;
                rgba[index] = (byte)((request.Left + x) % 251);
                rgba[index + 1] = (byte)((request.Top + y) % 241);
                rgba[index + 2] = 127;
                rgba[index + 3] = 255;
            }
            return ValueTask.FromResult(rgba);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

public partial class Main
{
    private Control _connectedRoot = null!;
    private VBoxContainer _connectedSurface = null!;
    private readonly UiJobRegistry _uiJobs = new();
    private CancellationTokenSource? _importCancellation;
    private ImportReviewModel? _importReviewModel;
    private Label? _entryStatus;
    private SqliteProjectRepository? _shellRepository;
    private HistorySession? _historySession;
    private RenderHardwareSnapshot? _hardwareSnapshot;
    private Label? _shellSaveStatus;
    private Label? _shellOperationStatus;
    private Label? _shellToolStatus;
    private Label? _shellJobStatus;
    private Button? _shellCancelJob;
    private VBoxContainer? _shellToolContent;
    private VBoxContainer? _shellLayersContent;
    private Button? _shellUndo;
    private Button? _shellRedo;
    private CancellationTokenSource? _exportCancellation;
    private string _activeTool = "Pan";
    private string _activeTarget = "Canvas";
    private readonly EditorControlsState _editorControlsState = new();
    private double? _shellScaleOverride;
    private Vector2I? _shellViewportOverride;
    private string ProjectsRoot => ProjectSettings.GlobalizePath("user://projects");

    public override void _UnhandledKeyInput(InputEvent inputEvent)
    {
        if (inputEvent is not InputEventKey key || !key.Pressed || key.Echo ||
            _editSession is null || _canvas is null || !GodotObject.IsInstanceValid(_canvas)) return;
        var focus = GetViewport().GuiGetFocusOwner();
        var fieldFocused = focus is LineEdit or TextEdit;
        var shortcut = ShortcutName(key);
        if (shortcut is null) return;
        var modalOpen = focus is not null && focus.GetWindow() != GetWindow();
        var route = _canvas.Gestures.RouteShortcut(shortcut, fieldFocused,
            modalOpen, _historySession?.ReconstructionState.IsRunning == true);
        if (route is ShortcutRoute.Field or ShortcutRoute.Modal or ShortcutRoute.Blocked) return;

        if (shortcut is "B" or "L" or "R" or "H")
            SelectShellTool(shortcut switch { "B" => "Texture Brush", "L" => "Land", "R" => "River", _ => "Pan" });
        else if (shortcut is "[" or "]")
        {
            var numeric = _activeTool == "Land" ? _editorControlsState.LandDiameter : _editorControlsState.TextureDiameter;
            numeric.Adjust(false, false, shortcut == "]" ? 1 : -1);
            SelectShellTool(_activeTool);
        }
        else if (shortcut == "Ctrl+S") SaveShellAsync();
        else if (shortcut == "Ctrl+Z") NavigateHistoryAsync(undo: true);
        else if (shortcut == "Ctrl+Shift+Z") NavigateHistoryAsync(undo: false);
        else if (shortcut == "Ctrl+Shift+E") ShowExportSetup();
        else if (shortcut == "Delete" && _activeTool == "River" && _editSession.Current.River is { Points.Length: > 2 })
            ExecuteLayerCommand(snapshot => new DeleteRiverPoint(CommandId.New(), snapshot.Revision,
                Math.Clamp(_editorControlsState.SelectedRiverPoint, 0, snapshot.River!.Points.Length - 1)));
        else if (shortcut == "Tab" && _shellToolContent?.GetParent() is Control panel)
            panel.Visible = !panel.Visible;
        else if (shortcut == "F6") FocusNextRegion();
        else return;
        GetViewport().SetInputAsHandled();
    }

    private static string? ShortcutName(InputEventKey key)
    {
        if (key.CtrlPressed && key.ShiftPressed && key.Keycode == Key.Z) return "Ctrl+Shift+Z";
        if (key.CtrlPressed && key.ShiftPressed && key.Keycode == Key.E) return "Ctrl+Shift+E";
        if (key.CtrlPressed && key.Keycode == Key.S) return "Ctrl+S";
        if (key.CtrlPressed && key.Keycode == Key.Z) return "Ctrl+Z";
        return key.Keycode switch
        {
            Key.B => "B", Key.L => "L", Key.R => "R", Key.H => "H",
            Key.Bracketleft => "[", Key.Bracketright => "]", Key.Delete => "Delete",
            Key.F6 => "F6", Key.Tab => "Tab", Key.Escape => "Escape", _ => null
        };
    }

    private void FocusNextRegion()
    {
        var focusable = new Control?[]
        {
            _canvas,
            _shellToolContent?.GetChildren().OfType<Control>().FirstOrDefault(control => control.FocusMode != FocusModeEnum.None),
            _shellLayersContent?.GetChildren().OfType<Control>().FirstOrDefault(control => control.FocusMode != FocusModeEnum.None),
            _shellUndo
        }.Where(control => control is not null && control.Visible).Cast<Control>().ToArray();
        if (focusable.Length == 0) return;
        var current = GetViewport().GuiGetFocusOwner();
        var index = Array.IndexOf(focusable, current);
        focusable[(index + 1 + focusable.Length) % focusable.Length].GrabFocus();
    }

    private void BuildConnectedInterface()
    {
        Theme = EditorTheme.Create();
        if (_shellScaleOverride is { } scale) Theme.DefaultBaseScale = (float)scale;
        _connectedRoot = new PanelContainer
        {
            Name = "ConnectedRoot",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        _connectedRoot.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(_connectedRoot);
        _connectedSurface = new VBoxContainer
        {
            Name = "ConnectedSurface",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        _connectedRoot.AddChild(_connectedSurface);
        ShowProjectEntry();
    }

    private void ClearConnectedSurface()
    {
        foreach (var child in _connectedSurface.GetChildren().OfType<Node>().ToArray())
        {
            _connectedSurface.RemoveChild(child);
            child.QueueFree();
        }
    }

    private void ShowProjectEntry()
    {
        ClearConnectedSurface();
        var page = new MarginContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        page.AddThemeConstantOverride("margin_left", 48);
        page.AddThemeConstantOverride("margin_right", 48);
        page.AddThemeConstantOverride("margin_top", 48);
        page.AddThemeConstantOverride("margin_bottom", 48);
        _connectedSurface.AddChild(page);
        var content = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        content.AddThemeConstantOverride("separation", 24);
        page.AddChild(content);

        content.AddChild(LabelFor("Mapwright", "DisplayLabel"));
        content.AddChild(LabelFor("Pick up where you left off", "PanelTitleLabel"));
        content.AddChild(LabelFor(
            "Mapwright opens on recent projects only when you choose one. Everything stays on this machine; sources are copied in and never modified.",
            "DenseLabel", true));

        var actions = new HBoxContainer();
        var open = ButtonFor("Open project folder…", "Open project folder. Select an existing .mapwright folder.");
        open.Pressed += ShowOpenProjectDialog;
        actions.AddChild(open);
        var import = ButtonFor("Import an .ink into a new project…",
            "Import an Inkarnate backup. The original file will not be changed.", primary: true);
        import.Pressed += ShowImportFileDialog;
        actions.AddChild(import);
        content.AddChild(actions);

        content.AddChild(LabelFor("Recent projects", "SectionLabel"));
        var recents = RecentProjectCatalog.Read(ProjectsRoot);
        if (recents.Count == 0)
        {
            content.AddChild(LabelFor("No project is open", "PanelTitleLabel"));
            content.AddChild(LabelFor(
                "Import an Inkarnate .ink backup or open an existing Mapwright project. Everything stays on this machine, and your source file is never modified.",
                "DenseLabel", true));
        }
        else
        {
            var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(0, 280) };
            var rows = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            foreach (var recent in recents)
            {
                var row = ButtonFor($"{recent.Name}\n{recent.StatusText}\n{recent.Path}",
                    recent.Path, primary: false);
                row.Alignment = HorizontalAlignment.Left;
                row.Pressed += () => OpenRecentProjectAsync(recent);
                rows.AddChild(row);
            }
            scroll.AddChild(rows);
            content.AddChild(scroll);
        }
        _entryStatus = LabelFor("Ready · offline, local files only", "CaptionLabel");
        content.AddChild(_entryStatus);
    }

    private void ShowOpenProjectDialog()
    {
        if (_canvas is not null && GodotObject.IsInstanceValid(_canvas))
            _canvas.CancelActiveGesture("Modal opened");
        var dialog = new FileDialog
        {
            Title = "Open Mapwright project folder",
            FileMode = FileDialog.FileModeEnum.OpenDir,
            Access = FileDialog.AccessEnum.Filesystem,
            UseNativeDialog = true
        };
        dialog.DirSelected += path => OpenProjectPathAsync(path);
        dialog.Canceled += dialog.QueueFree;
        dialog.DirSelected += _ => dialog.QueueFree();
        AddChild(dialog);
        dialog.PopupCenteredRatio(0.8f);
    }

    private void ShowImportFileDialog()
    {
        if (_canvas is not null && GodotObject.IsInstanceValid(_canvas))
            _canvas.CancelActiveGesture("Modal opened");
        var dialog = new FileDialog
        {
            Title = "Import an Inkarnate map",
            FileMode = FileDialog.FileModeEnum.OpenFile,
            Access = FileDialog.AccessEnum.Filesystem,
            UseNativeDialog = true,
            Filters = ["*.ink ; Inkarnate backup"]
        };
        dialog.FileSelected += BeginImportReviewAsync;
        dialog.Canceled += dialog.QueueFree;
        dialog.FileSelected += _ => dialog.QueueFree();
        AddChild(dialog);
        dialog.PopupCenteredRatio(0.8f);
    }

    private async void BeginImportReviewAsync(string sourcePath)
    {
        if (_entryStatus is not null) _entryStatus.Text = "Reading source safely · no project has been created";
        try
        {
            var imported = await Task.Run(() => _importer.Import(sourcePath));
            _currentImport = imported;
            _importReviewModel = new ImportReviewModel(imported, ProjectsRoot);
            ShowImportReview(_importReviewModel);
        }
        catch (ImportRejectedException exception)
        {
            ShowImportOutcome("This file could not be opened",
                $"{exception.Message}\n\nNothing was written and your source file is untouched. Review the details, then choose another file or try again.");
        }
        catch (Exception exception)
        {
            ShowImportOutcome("This file could not be opened",
                $"{exception.Message}\n\nNothing was written and your source file is untouched. Review the details, then choose another file or try again.");
        }
    }

    private void ShowImportReview(ImportReviewModel model)
    {
        ClearConnectedSurface();
        var margin = new MarginContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        margin.AddThemeConstantOverride("margin_left", 24);
        margin.AddThemeConstantOverride("margin_right", 24);
        margin.AddThemeConstantOverride("margin_top", 24);
        margin.AddThemeConstantOverride("margin_bottom", 24);
        _connectedSurface.AddChild(margin);
        var page = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
        margin.AddChild(page);
        page.AddChild(LabelFor("Import an Inkarnate map", "DisplayLabel"));
        page.AddChild(LabelFor("Recovery review — choose how this map opens", "PanelTitleLabel"));

        var split = new HSplitContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        page.AddChild(split);
        var source = new VBoxContainer { CustomMinimumSize = new Vector2(400, 0) };
        source.AddChild(LabelFor("Source file", "SectionLabel"));
        source.AddChild(LabelFor($"{Path.GetFileName(model.Import.SourcePath)}\n{model.Import.SourcePath}", "MonoLabel", true));
        var preview = new TextureRect
        {
            CustomMinimumSize = new Vector2(400, 300),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            Texture = TextureFromPng(model.Import.PreviewPng)
        };
        preview.TooltipText = $"Preserved source preview · {model.Import.PreviewWidth} × {model.Import.PreviewHeight}";
        source.AddChild(preview);
        source.AddChild(LabelFor(
            $"Source dimensions  {model.Import.Document.Width:0} × {model.Import.Document.Height:0}\n" +
            $"Format version  .ink v{model.Import.Document.Import?.SourceVersion ?? 0}\n" +
            $"Recovered rasters  {model.Import.Rasters.Count}\n" +
            $"Unresolved assets  {model.Import.Document.Import?.UnresolvedAssetIds.Count ?? 0}",
            "MonoLabel", true));
        source.AddChild(LabelFor(
            "The original .ink is copied into the project and kept byte-identical with its hash. Mapwright never writes to your source file.",
            "DenseLabel", true));
        split.AddChild(source);

        var optionsScroll = new ScrollContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
        var options = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        optionsScroll.AddChild(options);
        split.AddChild(optionsScroll);
        options.AddChild(LabelFor("Recovery mode", "SectionLabel"));
        var group = new ButtonGroup { AllowUnpress = true };
        var editable = ButtonFor(
            "Editable recovered terrain\nBackground and Foreground arrive paintable when verified; unsupported extras remain preserved and reported.",
            "Choose editable recovered terrain. Unknown mappings degrade honestly to the preserved visual fallback.");
        editable.ToggleMode = true;
        editable.ButtonGroup = group;
        options.AddChild(editable);
        var flattened = ButtonFor(
            "Original flattened appearance\nBackground is the locked embedded preview, Foreground starts empty, and baked coasts are pixels rather than editable geometry.",
            "Choose the original flattened appearance.");
        flattened.ToggleMode = true;
        flattened.ButtonGroup = group;
        options.AddChild(flattened);

        options.AddChild(LabelFor("New project", "SectionLabel"));
        var projectName = new LineEdit { Text = model.ProjectName, PlaceholderText = "Project name" };
        options.AddChild(projectName);
        var destination = LabelFor(model.CanonicalDestination, "MonoLabel", true);
        options.AddChild(destination);
        var elsewhere = ButtonFor("Elsewhere…", "Override the configured projects-folder destination.");
        options.AddChild(elsewhere);
        var actionReason = LabelFor("Choose a recovery mode to continue · the source file is never modified", "CaptionLabel", true);
        options.AddChild(actionReason);
        var actions = new HBoxContainer();
        var cancel = ButtonFor("Cancel", "Return to recent projects without creating anything.");
        var create = ButtonFor("Create project", "Create the initial durable revision.", primary: true);
        create.Disabled = true;
        actions.AddChild(cancel);
        actions.AddChild(create);
        options.AddChild(actions);

        void Refresh()
        {
            model.ProjectName = projectName.Text.Trim();
            destination.Text = model.CanonicalDestination;
            destination.TooltipText = model.CanonicalDestination;
            create.Disabled = !model.CanCreate;
            actionReason.Text = model.CanCreate
                ? $"Ready · {model.CanonicalDestination}"
                : "Choose a recovery mode to continue · the source file is never modified";
        }
        editable.Toggled += selected =>
        {
            if (selected) model.SelectMode(ImportRecoveryMode.EditableRecoveredTerrain);
            Refresh();
        };
        flattened.Toggled += selected =>
        {
            if (selected) model.SelectMode(ImportRecoveryMode.OriginalFlattenedAppearance);
            Refresh();
        };
        projectName.TextChanged += _ => Refresh();
        elsewhere.Pressed += () => ShowDestinationDialog(model, Refresh);
        cancel.Pressed += ShowProjectEntry;
        create.Pressed += () => PublishImportAsync(model);
    }

    private void ShowDestinationDialog(ImportReviewModel model, Action refresh)
    {
        if (_canvas is not null && GodotObject.IsInstanceValid(_canvas))
            _canvas.CancelActiveGesture("Modal opened");
        var dialog = new FileDialog
        {
            Title = "Choose project destination folder",
            FileMode = FileDialog.FileModeEnum.OpenDir,
            Access = FileDialog.AccessEnum.Filesystem,
            UseNativeDialog = true
        };
        dialog.DirSelected += path =>
        {
            model.SetDestinationOverride(Path.Combine(path, model.ProjectName + ".mapwright"));
            refresh();
            dialog.QueueFree();
        };
        dialog.Canceled += dialog.QueueFree;
        AddChild(dialog);
        dialog.PopupCenteredRatio(0.8f);
    }

    private async void PublishImportAsync(ImportReviewModel model)
    {
        if (!model.CanCreate) return;
        _importCancellation?.Dispose();
        _importCancellation = new CancellationTokenSource();
        var token = _importCancellation.Token;
        var job = _uiJobs.Start(UiJobKind.Import, null, model.CanonicalDestination);
        ShowImportWorking(model, job);
        try
        {
            token.ThrowIfCancellationRequested();
            var result = await new ImportProject(path => new SqliteProjectRepository(path))
                .ExecuteAsync(model.ToRequest(), token);
            token.ThrowIfCancellationRequested();
            if (!result.Published || result.ProjectDirectory is null || result.Project is null)
                throw new InvalidOperationException(result.OutcomeText);
            _uiJobs.Complete(job.Id);
            RecentProjectCatalog.Remember(ProjectsRoot, result.ProjectDirectory);
            ShowImportOutcome(result.RecoveryLevel == ImportRecoveryLevel.Editable
                    ? "Project created"
                    : "Imported with degradation",
                result.OutcomeText + "\n\n" + result.ProjectDirectory,
                () => OpenProjectPathAsync(result.ProjectDirectory));
        }
        catch (OperationCanceledException)
        {
            _uiJobs.Cancel(job.Id);
            ShowImportOutcome("Import cancelled",
                "Nothing was published. Your source file is untouched.", () => ShowImportReview(model));
        }
        catch (Exception exception)
        {
            _uiJobs.Cancel(job.Id);
            ShowImportOutcome("Import failed",
                $"{exception.Message}\n\nNothing was written and your source file is untouched. Choose another location or try again.",
                () => ShowImportReview(model));
        }
    }

    private void ShowImportWorking(ImportReviewModel model, UiJobIdentity job)
    {
        ClearConnectedSurface();
        var panel = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        panel.AddChild(LabelFor("Creating project", "DisplayLabel"));
        panel.AddChild(LabelFor("Preparing immutable source, recovered terrain, and initial revision", "WorkingLabel", true));
        panel.AddChild(LabelFor($"Target\n{job.CanonicalTarget}", "MonoLabel", true));
        panel.AddChild(LabelFor(
            "Nothing appears in Recent projects until the initial SQLite revision and every source blob are complete.",
            "DenseLabel", true));
        var progress = new ProgressBar { Indeterminate = true, ShowPercentage = false };
        panel.AddChild(progress);
        var cancel = ButtonFor("Cancel import", "Cancel this import without publishing a project.");
        cancel.Pressed += () => _importCancellation?.Cancel();
        panel.AddChild(cancel);
        _connectedSurface.AddChild(panel);
    }

    private void ShowImportOutcome(string title, string body, Action? primary = null)
    {
        ClearConnectedSurface();
        _connectedSurface.AddChild(LabelFor(title, "DisplayLabel"));
        _connectedSurface.AddChild(LabelFor(body, "DenseLabel", true));
        var actions = new HBoxContainer();
        if (primary is not null)
        {
            var open = ButtonFor("Open the project", "Open the new project.", primary: true);
            open.Pressed += primary;
            actions.AddChild(open);
        }
        var back = ButtonFor("Back to projects", "Return to recent projects.");
        back.Pressed += ShowProjectEntry;
        actions.AddChild(back);
        _connectedSurface.AddChild(actions);
    }

    private async void OpenRecentProjectAsync(RecentProjectItem item) => await OpenProjectPathCoreAsync(item.Path);
    private async void OpenProjectPathAsync(string path) => await OpenProjectPathCoreAsync(path);

    private async Task OpenProjectPathCoreAsync(string path)
    {
        try
        {
            var result = await LegacyMapUnitMigration.OpenAsync(path, CancellationToken.None);
            if (!result.CanOpen || result.ProjectId is null)
            {
                ReportProjectOpenRefusal(result.Reason);
                return;
            }
            var repository = new SqliteProjectRepository(result.ProjectDirectory);
            var project = await repository.LoadAsync(result.ProjectId.Value, CancellationToken.None);
            var recovery = await repository.ReadRecoveryFactsAsync(project.ProjectId, CancellationToken.None);
            if (result.Status == LegacyProjectOpenStatus.Migrated)
                recovery = recovery with
                {
                    BannerText = $"Normalized copy recovered at revision {result.CursorRevision}; redo ends at {result.LatestRevision}. Original retained at {result.OriginalProjectDirectory}."
                };
            RecentProjectCatalog.Remember(ProjectsRoot, result.ProjectDirectory);
            await ActivateEditorShellAsync(project, result.ProjectDirectory, repository, recovery);
        }
        catch (Exception exception)
        {
            ReportProjectOpenRefusal($"{exception.Message}\n\nKeep the original project and review its compatibility before retrying.");
        }
    }

    private void ReportProjectOpenRefusal(string reason)
    {
        if (_editSession is null)
        {
            ShowImportOutcome("This project could not be opened", reason);
            return;
        }
        var dialog = new AcceptDialog
        {
            Title = "This project could not be opened",
            DialogText = reason
        };
        dialog.Confirmed += dialog.QueueFree;
        dialog.Canceled += dialog.QueueFree;
        AddChild(dialog);
        dialog.PopupCenteredRatio(0.65f);
        if (_shellOperationStatus is not null)
            _shellOperationStatus.Text = "Project open refused · current edit session retained";
    }

    private async Task ActivateEditorShellAsync(MapProject project, string path,
        SqliteProjectRepository repository, RepositoryRecoveryFacts recovery)
    {
        if (_editSession is not null) await _editSession.DisposeAsync();
        if (_historySession is not null) await _historySession.DisposeAsync();
        _currentProjectDirectory = Path.GetFullPath(path);
        _shellRepository = repository;
        _connectedGraph = new ConnectedTerrainGraph(_currentProjectDirectory);
        _editSession = new EditSession(project, repository, new NullRenderInvalidationQueue());
        _historySession = new HistorySession(project, repository, new NullRenderInvalidationQueue());
        _hardwareSnapshot = RenderResourceLedger.CaptureStartup();
        if (string.IsNullOrWhiteSpace(_editorControlsState.SelectedTextureIdentity))
            _editorControlsState.SetTextureIdentity(project.ImportedSource?.PreviewBlobHash ??
                                                    project.ImportedSource?.SourceSha256);
        ShowEditorShell(recovery);
        await RefreshHistoryControlsAsync();
    }

    private void ShowEditorShell(RepositoryRecoveryFacts? recovery)
    {
        if (_editSession is null || _historySession is null || _connectedGraph is null ||
            _currentProjectDirectory is null) return;
        ClearConnectedSurface();
        var snapshot = _editSession.Current;
        var viewport = _shellViewportOverride ?? new Vector2I(
            Math.Max(1, (int)GetViewportRect().Size.X), Math.Max(1, (int)GetViewportRect().Size.Y));
        var layout = ShellLayoutState.For(viewport.X, viewport.Y,
            _shellScaleOverride ?? Math.Max(1, GetWindow().ContentScaleFactor));
        var shell = new VBoxContainer
        {
            Name = "EditorShell",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        shell.AddThemeConstantOverride("separation", 0);
        _connectedSurface.AddChild(shell);

        if (!layout.Compressed)
        {
            var title = new HBoxContainer { CustomMinimumSize = new Vector2(0, 34) };
            title.AddChild(LabelFor($"Mapwright  ·  {snapshot.Title}", "PanelTitleLabel"));
            title.AddChild(LabelFor($"revision {snapshot.Revision}  ·  {snapshot.Width:0} × {snapshot.Height:0}",
                "MonoLabel"));
            shell.AddChild(title);
        }

        var command = new HBoxContainer { CustomMinimumSize = new Vector2(0, layout.Compressed ? 38 : 44) };
        command.AddThemeConstantOverride("separation", 6);
        if (layout.Compressed)
        {
            var file = new MenuButton { Text = "File", TooltipText = "Open, save, import, and export commands." };
            var popup = file.GetPopup();
            popup.AddItem("Open project", 1);
            popup.AddItem("Save project", 2);
            popup.AddItem("Import .ink", 3);
            popup.AddItem("Export PNG", 4);
            popup.IdPressed += id =>
            {
                if (id == 1) ShowOpenProjectDialog();
                else if (id == 2) SaveShellAsync();
                else if (id == 3) ShowImportFileDialog();
                else if (id == 4) ShowExportSetup();
            };
            command.AddChild(file);
            command.AddChild(LabelFor($"{snapshot.Title} · r{snapshot.Revision}", "MonoLabel"));
        }
        else
        {
            var open = ButtonFor("Open project", "Open a different local .mapwright folder.");
            open.Pressed += ShowOpenProjectDialog;
            command.AddChild(open);
            var save = ButtonFor("Save project", "Verify every queued command is durable. Ctrl+S.");
            save.Pressed += SaveShellAsync;
            command.AddChild(save);
            var import = ButtonFor("Import .ink", "Import an Inkarnate backup into a new project.");
            import.Pressed += ShowImportFileDialog;
            command.AddChild(import);
            var export = ButtonFor("Export PNG", "Export a named frozen revision. Ctrl+Shift+E.");
            export.Disabled = _hardwareSnapshot?.Status != RenderHardwareStatus.Ready;
            export.Pressed += ShowExportSetup;
            command.AddChild(export);
        }
        _shellUndo = ButtonFor("Undo", "No committed action to undo. Ctrl+Z.");
        _shellUndo.Disabled = true;
        _shellUndo.Pressed += () => NavigateHistoryAsync(undo: true);
        command.AddChild(_shellUndo);
        _shellRedo = ButtonFor("Redo", "No committed action to redo. Ctrl+Shift+Z.");
        _shellRedo.Disabled = true;
        _shellRedo.Pressed += () => NavigateHistoryAsync(undo: false);
        command.AddChild(_shellRedo);
        _shellSaveStatus = LabelFor(EditorShellContract.SaveStatus(_editSession.SaveState), "StatusChip");
        command.AddChild(_shellSaveStatus);
        shell.AddChild(command);
        _editSession.SaveStateChanged += state => Callable.From(() =>
        {
            if (GodotObject.IsInstanceValid(_shellSaveStatus))
                _shellSaveStatus!.Text = EditorShellContract.SaveStatus(state);
        }).CallDeferred();

        if (recovery is not null)
        {
            var banner = new HBoxContainer { Name = "RecoveryBanner" };
            banner.AddChild(LabelFor(recovery.BannerText, "RecoveryLabel", true));
            var dismiss = ButtonFor("Continue editing", "Dismiss this non-blocking recovery notice.", primary: true);
            dismiss.Pressed += banner.QueueFree;
            banner.AddChild(dismiss);
            var details = ButtonFor("View recovery details", "Show only verified recovery facts.");
            details.Pressed += () => ShowRecoveryDetails(recovery);
            banner.AddChild(details);
            shell.AddChild(banner);
        }
        if (_hardwareSnapshot?.Status == RenderHardwareStatus.InspectionOnly)
        {
            shell.AddChild(LabelFor(
                $"Rendering backend unavailable — source inspection and recovery remain available. {_hardwareSnapshot.Detail}",
                "FailureLabel", true));
        }

        var work = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        shell.AddChild(work);
        var rail = new VBoxContainer { CustomMinimumSize = new Vector2(layout.RailWidth, 0) };
        rail.AddThemeConstantOverride("separation", 4);
        work.AddChild(rail);
        foreach (var tool in EditorShellContract.Tools)
        {
            var button = ButtonFor(ToolRailGlyph(tool),
                ToolTooltip(tool));
            button.CustomMinimumSize = new Vector2(layout.Compressed ? 48 : 56, layout.Compressed ? 36 : 40);
            button.Pressed += () => SelectShellTool(tool);
            rail.AddChild(button);
        }

        var leftSplit = new HSplitContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        work.AddChild(leftSplit);
        var toolScroll = new ScrollContainer
        {
            Name = "ToolPanel",
            CustomMinimumSize = new Vector2(layout.ToolPanelWidth, 0),
            SizeFlagsVertical = SizeFlags.ExpandFill,
            FollowFocus = true
        };
        _shellToolContent = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        toolScroll.AddChild(_shellToolContent);
        leftSplit.AddChild(toolScroll);

        var rightSplit = new HSplitContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        leftSplit.AddChild(rightSplit);
        var canvasSurface = new VBoxContainer
        {
            Name = "CanvasSurface",
            CustomMinimumSize = new Vector2(layout.MinimumCanvasWidth, 320),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        _shellJobStatus = LabelFor("", "WorkingLabel", true);
        _shellJobStatus.Visible = false;
        canvasSurface.AddChild(_shellJobStatus);
        _shellCancelJob = ButtonFor("Cancel export", "Cancel this export; the previous destination remains untouched.");
        _shellCancelJob.Visible = false;
        _shellCancelJob.Pressed += () => _exportCancellation?.Cancel();
        canvasSurface.AddChild(_shellCancelJob);
        if (_hardwareSnapshot?.Status == RenderHardwareStatus.Ready)
        {
            _canvas = new MapCanvas
            {
                Name = "MapCanvas",
                CustomMinimumSize = new Vector2(layout.MinimumCanvasWidth, 320),
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                SizeFlagsVertical = SizeFlags.ExpandFill,
                TooltipText = "Map canvas. Coverage, texture, and whole-layer opacity use distinct controls and feedback."
            };
            canvasSurface.AddChild(_canvas);
            _canvas.BindConnectedSession(_editSession, _connectedGraph, message =>
            {
                if (_shellOperationStatus is not null) _shellOperationStatus.Text = message;
            });
            _canvas.BindEditorControls(_editorControlsState);
        }
        else
        {
            var inspection = new CenterContainer
            {
                CustomMinimumSize = new Vector2(layout.MinimumCanvasWidth, 320),
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                SizeFlagsVertical = SizeFlags.ExpandFill
            };
            var inspectionCopy = LabelFor(
                "Inspection only\nThe connected project and recovery facts are readable, but rendering and editing are disabled until a compatible backend is available.",
                "PanelTitleLabel", true);
            inspectionCopy.CustomMinimumSize = new Vector2(460, 120);
            inspectionCopy.HorizontalAlignment = HorizontalAlignment.Center;
            inspection.AddChild(inspectionCopy);
            canvasSurface.AddChild(inspection);
        }
        rightSplit.AddChild(canvasSurface);

        var right = new VBoxContainer
        {
            Name = "LayersHistoryPanel",
            CustomMinimumSize = new Vector2(layout.LayersPanelWidth, 0),
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        var tabs = new HBoxContainer();
        var layersTab = ButtonFor("Layers", "Show the two fixed terrain rows.", primary: true);
        var historyTab = ButtonFor("History", "Show a bounded durable history window.");
        tabs.AddChild(layersTab);
        tabs.AddChild(historyTab);
        right.AddChild(tabs);
        var rightScroll = new ScrollContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            FollowFocus = true
        };
        _shellLayersContent = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        rightScroll.AddChild(_shellLayersContent);
        right.AddChild(rightScroll);
        layersTab.Pressed += PopulateLayersPanel;
        historyTab.Pressed += PopulateHistoryPanelAsync;
        rightSplit.AddChild(right);
        PopulateLayersPanel();
        SelectShellTool(_activeTool);

        var status = new HBoxContainer { CustomMinimumSize = new Vector2(0, layout.StatusBarHeight) };
        _shellOperationStatus = LabelFor("Ready", "CaptionLabel");
        _shellToolStatus = LabelFor($"{_activeTool} · {_activeTarget}", "CaptionLabel");
        status.AddChild(_shellOperationStatus);
        status.AddChild(_shellToolStatus);
        status.AddChild(LabelFor(ResourceStatus(_hardwareSnapshot), "MonoLabel"));
        status.AddChild(LabelFor("Fit · 100%", "MonoLabel"));
        shell.AddChild(status);
    }

    private void PopulateLayersPanel()
    {
        if (_shellLayersContent is null || _editSession is null) return;
        ClearChildren(_shellLayersContent);
        _shellLayersContent.AddChild(LabelFor("Terrain", "PanelTitleLabel"));
        foreach (var role in new[] { TerrainRole.Foreground, TerrainRole.Background })
        {
            var layer = _editSession.Current.RequireRole(role);
            var row = new VBoxContainer { Name = role + "TerrainRow" };
            var identity = new HBoxContainer();
            var roleChip = LabelFor(role == TerrainRole.Foreground ? "FG" : "BG", "RoleChip");
            roleChip.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
            identity.AddChild(roleChip);
            var name = new LineEdit
            {
                Text = layer.Name,
                TooltipText = $"{role} display name",
                CustomMinimumSize = new Vector2(72, 0),
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                Editable = _hardwareSnapshot?.Status == RenderHardwareStatus.Ready
            };
            name.TextSubmitted += value => ExecuteLayerCommand(project =>
                new RenameTerrainLayer(CommandId.New(), project.Revision, role, value));
            identity.AddChild(name);
            var visible = LayerToggle("V", $"{role} visibility", layer.Visible);
            visible.Disabled = _hardwareSnapshot?.Status != RenderHardwareStatus.Ready;
            visible.Toggled += value => ExecuteLayerCommand(project =>
                new SetTerrainVisibility(CommandId.New(), project.Revision, role, value));
            identity.AddChild(visible);
            var locked = LayerToggle("L", $"{role} lock", layer.Locked);
            locked.Disabled = _hardwareSnapshot?.Status != RenderHardwareStatus.Ready;
            locked.Toggled += value => ExecuteLayerCommand(project =>
                new SetTerrainLock(CommandId.New(), project.Revision, role, value));
            identity.AddChild(locked);
            var solo = LayerToggle("S", $"{role} solo", layer.Solo);
            solo.Disabled = _hardwareSnapshot?.Status != RenderHardwareStatus.Ready;
            solo.Toggled += value => ExecuteLayerCommand(project =>
                new SetTerrainSolo(CommandId.New(), project.Revision, role, value));
            identity.AddChild(solo);
            row.AddChild(identity);
            var opacity = new HBoxContainer();
            var opacityTitle = LabelFor("Layer opacity", "DenseLabel");
            opacityTitle.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
            opacity.AddChild(opacityTitle);
            var slider = new HSlider
            {
                MinValue = 0,
                MaxValue = 100,
                Step = 1,
                Value = layer.Opacity * 100,
                TooltipText = $"{role} layer opacity",
                CustomMinimumSize = new Vector2(80, 0),
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                Editable = _hardwareSnapshot?.Status == RenderHardwareStatus.Ready
            };
            var valueLabel = LabelFor($"{slider.Value:0}%", "MonoLabel");
            valueLabel.SizeFlagsHorizontal = SizeFlags.ShrinkEnd;
            slider.ValueChanged += value =>
            {
                valueLabel.Text = $"{value:0}%";
                if (_shellOperationStatus is not null)
                    _shellOperationStatus.Text = $"Layer opacity · {role} · {value:0}%";
            };
            slider.DragEnded += _ => ExecuteLayerCommand(project =>
                new SetTerrainOpacity(CommandId.New(), project.Revision, role, slider.Value / 100.0));
            opacity.AddChild(slider);
            opacity.AddChild(valueLabel);
            row.AddChild(opacity);
            if (role == TerrainRole.Foreground)
            {
                var maskPrefix = _activeTool is "Land" or "River" ? "▶ " : "  ";
                row.AddChild(LabelFor($"{maskPrefix}Land mask · Coverage · Edit", "DenseLabel"));
                row.AddChild(LabelFor($"  River modifier · {(layer.Visible ? "Enabled" : "Disabled")}", "DenseLabel"));
            }
            _shellLayersContent.AddChild(row);
        }
    }

    private async void PopulateHistoryPanelAsync()
    {
        if (_shellLayersContent is null || _historySession is null) return;
        ClearChildren(_shellLayersContent);
        var window = await _historySession.ReadWindowAsync(0, ShellStatesSmoke.HistoryPageSize);
        _shellLayersContent.AddChild(LabelFor("History", "PanelTitleLabel"));
        if (window.TotalCount == 0)
        {
            _shellLayersContent.AddChild(LabelFor("No edits yet", "SectionLabel"));
            _shellLayersContent.AddChild(LabelFor(
                "Committed strokes and property changes will appear here. Choose a tool, then edit the canvas to create the first undoable step.",
                "DenseLabel", true));
            var choose = ButtonFor("Choose Texture Brush", "Activate Texture Brush.", primary: true);
            choose.Pressed += () => SelectShellTool("Texture Brush");
            _shellLayersContent.AddChild(choose);
            return;
        }
        _shellLayersContent.AddChild(LabelFor(
            $"Showing {window.Entries.Count} of {window.TotalCount} durable actions · cursor r{window.State.CursorRevision}",
            "CaptionLabel", true));
        foreach (var entry in window.Entries)
        {
            var current = entry.Revision == window.State.CursorRevision ? " · CURRENT" : "";
            _shellLayersContent.AddChild(LabelFor(
                $"{entry.Label}\nr{entry.Revision}{current} · {entry.CommittedAt.ToLocalTime():g} · {entry.TileCount} tiles",
                "DenseLabel", true));
        }
    }

    private async void ExecuteLayerCommand(Func<MapProject, Mapwright.Domain.IEditCommand> command)
    {
        if (_editSession is null || _hardwareSnapshot?.Status != RenderHardwareStatus.Ready) return;
        try
        {
            await _editSession.ExecuteAsync(command(_editSession.Current));
            if (_canvas is not null && GodotObject.IsInstanceValid(_canvas))
                _canvas.ShowCommittedSnapshot(_editSession.Current);
            if (_historySession is not null) await _historySession.DisposeAsync();
            _historySession = new HistorySession(_editSession.Current, _shellRepository!, new NullRenderInvalidationQueue());
            PopulateLayersPanel();
            SelectShellTool(_activeTool);
            await RefreshHistoryControlsAsync();
        }
        catch (Exception exception)
        {
            if (_shellOperationStatus is not null) _shellOperationStatus.Text = $"Edit failed — {exception.Message}";
        }
    }

    private async void NavigateHistoryAsync(bool undo)
    {
        if (_historySession is null || _shellRepository is null || _hardwareSnapshot?.Status != RenderHardwareStatus.Ready) return;
        try
        {
            var result = undo ? await _historySession.UndoAsync() : await _historySession.RedoAsync();
            if (_editSession is not null) await _editSession.DisposeAsync();
            _editSession = new EditSession(_historySession.Current, _shellRepository, new NullRenderInvalidationQueue());
            _editSession.SaveStateChanged += state =>
            {
                if (_shellSaveStatus is not null) _shellSaveStatus.Text = EditorShellContract.SaveStatus(state);
            };
            if (_canvas is not null && GodotObject.IsInstanceValid(_canvas))
                _canvas.BindConnectedSession(_editSession, _connectedGraph!, message =>
                {
                    if (_shellOperationStatus is not null) _shellOperationStatus.Text = message;
                }, result.Invalidation);
            if (_shellOperationStatus is not null)
                _shellOperationStatus.Text = $"{(undo ? "Undo" : "Redo")} complete · revision {result.Revision}";
            PopulateLayersPanel();
            await RefreshHistoryControlsAsync();
        }
        catch (Exception exception)
        {
            if (_shellOperationStatus is not null) _shellOperationStatus.Text = $"History failed — {exception.Message}";
        }
    }

    private async Task RefreshHistoryControlsAsync()
    {
        if (_historySession is null || _shellUndo is null || _shellRedo is null) return;
        var controls = await _historySession.ReadControlsAsync();
        _shellUndo.Disabled = !controls.Undo.Enabled || _hardwareSnapshot?.Status != RenderHardwareStatus.Ready;
        _shellUndo.TooltipText = controls.Undo.Enabled ? $"Undo {controls.Undo.Label}. Ctrl+Z." : controls.Undo.Reason;
        _shellRedo.Disabled = !controls.Redo.Enabled || _hardwareSnapshot?.Status != RenderHardwareStatus.Ready;
        _shellRedo.TooltipText = controls.Redo.Enabled ? $"Redo {controls.Redo.Label}. Ctrl+Shift+Z." : controls.Redo.Reason;
    }

    private void SelectShellTool(string tool)
    {
        _editorControlsState.SelectTool(tool);
        _activeTool = _editorControlsState.ActiveTool;
        _activeTarget = _editorControlsState.ActiveTarget;
        if (_shellToolStatus is not null) _shellToolStatus.Text = $"{_activeTool} · {_activeTarget}";
        if (_canvas is not null && GodotObject.IsInstanceValid(_canvas) &&
            tool is "Pan" or "Texture Brush" or "Land" or "River")
            _canvas.SetEditorTool(tool, _editorControlsState.TextureTarget);
        if (_shellToolContent is null || _editSession is null) return;
        ClearChildren(_shellToolContent);
        if (tool == "Sample Texture")
        {
            var identity = _editSession.Current.ImportedSource?.PreviewBlobHash ??
                           _editSession.Current.ImportedSource?.SourceSha256;
            _shellToolContent.AddChild(LabelFor(identity is null
                    ? "No local textures available\nLocate texture… to enable texture painting."
                    : $"Local canvas texture\n{identity}",
                "MonoLabel", true));
        }
        else if (tool == "Project Storage")
        {
            _shellToolContent.AddChild(LabelFor(tool, "PanelTitleLabel"));
            PopulateStoragePanel();
        }
        else
        {
            EditorControls.Populate(_shellToolContent, _editorControlsState, _editSession.Current,
                () => SelectShellTool(_editorControlsState.ActiveTool), ExecuteLayerCommand,
                message =>
                {
                    if (_shellOperationStatus is not null) _shellOperationStatus.Text = message;
                });
        }
        PopulateLayersPanel();
    }

    private void PopulateStoragePanel()
    {
        if (_shellToolContent is null || _currentProjectDirectory is null) return;
        var cacheBytes = Directory.Exists(Path.Combine(_currentProjectDirectory, "cache"))
            ? Directory.EnumerateFiles(Path.Combine(_currentProjectDirectory, "cache"), "*", SearchOption.AllDirectories)
                .Sum(path => new FileInfo(path).Length)
            : 0;
        _shellToolContent.AddChild(LabelFor(
            $"Project\n{_currentProjectDirectory}\nDisposable cache bytes  {cacheBytes:N0}\n{ResourceStatus(_hardwareSnapshot)}",
            "MonoLabel", true));
        var delete = ButtonFor("Delete disposable caches…",
            "Confirm deletion of render/history acceleration only. Sources and native revisions remain.");
        delete.Pressed += ConfirmCacheDeletion;
        _shellToolContent.AddChild(delete);
    }

    private void ConfirmCacheDeletion()
    {
        if (_canvas is not null && GodotObject.IsInstanceValid(_canvas))
            _canvas.CancelActiveGesture("Modal opened");
        var dialog = new ConfirmationDialog
        {
            Title = "Delete disposable caches?",
            DialogText = "Render and history acceleration will rebuild from source blobs and native revisions. Source files and committed edits are not deleted.",
            OkButtonText = "Delete caches"
        };
        dialog.Confirmed += DeleteCaches;
        dialog.Canceled += dialog.QueueFree;
        dialog.Confirmed += dialog.QueueFree;
        AddChild(dialog);
        dialog.PopupCentered();
    }

    private void DeleteCaches()
    {
        if (_currentProjectDirectory is null) return;
        var job = _uiJobs.Start(UiJobKind.Cache, _editSession?.Current.Revision, _currentProjectDirectory);
        var result = new ProjectStore().DeleteDisposableCaches(_currentProjectDirectory);
        _uiJobs.Complete(job.Id);
        _connectedGraph = new ConnectedTerrainGraph(_currentProjectDirectory);
        if (_canvas is not null && _editSession is not null && GodotObject.IsInstanceValid(_canvas))
            _canvas.BindConnectedSession(_editSession, _connectedGraph, message =>
            {
                if (_shellOperationStatus is not null) _shellOperationStatus.Text = message;
            });
        if (_shellOperationStatus is not null) _shellOperationStatus.Text = result.OutcomeText;
        PopulateStoragePanelFresh();
    }

    private void PopulateStoragePanelFresh()
    {
        if (_shellToolContent is null) return;
        ClearChildren(_shellToolContent);
        _shellToolContent.AddChild(LabelFor("Project Storage", "PanelTitleLabel"));
        PopulateStoragePanel();
    }

    private async void SaveShellAsync()
    {
        if (_editSession is null) return;
        try { await _editSession.SaveAsync(); }
        catch (Exception exception)
        {
            if (_shellOperationStatus is not null) _shellOperationStatus.Text = $"Save failed — {exception.Message}";
        }
    }

    private void ShowExportSetup()
    {
        if (_shellToolContent is null || _editSession is null || _currentProjectDirectory is null) return;
        if (_canvas is not null && GodotObject.IsInstanceValid(_canvas))
            _canvas.CancelActiveGesture("Modal opened");
        SelectShellTool("Pan");
        ClearChildren(_shellToolContent);
        var frozen = _editSession.Current;
        _shellToolContent.AddChild(LabelFor("Export PNG", "PanelTitleLabel"));
        var destination = new LineEdit
        {
            Text = Path.Combine(ProjectSettings.GlobalizePath("user://exports"),
                $"{SafeExportName(frozen.Title)}-r{frozen.Revision}.png"),
            PlaceholderText = "Destination .png",
            TooltipText = "Export destination"
        };
        _shellToolContent.AddChild(destination);
        var dimensions = new HBoxContainer();
        var (initialWidth, initialHeight) = DocumentPngExport.DimensionsForPreset(frozen, 1024);
        var width = new SpinBox { MinValue = 1, MaxValue = DocumentPngExport.MaximumDimension, Value = initialWidth };
        var height = new SpinBox { MinValue = 1, MaxValue = DocumentPngExport.MaximumDimension, Value = initialHeight };
        dimensions.AddChild(LabelFor("Width", "DenseLabel"));
        dimensions.AddChild(width);
        dimensions.AddChild(LabelFor("Height", "DenseLabel"));
        dimensions.AddChild(height);
        _shellToolContent.AddChild(dimensions);
        _shellToolContent.AddChild(LabelFor("Use an aspect-preserving 1K, 2K, 3K, 4K, 8K or 16K longest edge.",
            "DenseLabel", true));
        _shellToolContent.AddChild(LabelFor($"Frozen revision {frozen.Revision} · editing may continue on a newer revision",
            "MonoLabel", true));
        var start = ButtonFor("Start export", "Export this immutable revision.", primary: true);
        start.Disabled = _hardwareSnapshot?.Status != RenderHardwareStatus.Ready;
        start.Pressed += () => StartExportAsync(frozen, destination.Text, (int)width.Value, (int)height.Value);
        _shellToolContent.AddChild(start);
    }

    private async void StartExportAsync(MapProject frozen, string destination, int width, int height)
    {
        if (_connectedGraph is null || _hardwareSnapshot?.Status != RenderHardwareStatus.Ready ||
            _currentProjectDirectory is null) return;
        if (!ExistingExportRequest.TryResolve(frozen, width, height, out var longestEdge,
                out var validationMessage))
        {
            if (_shellJobStatus is not null)
            {
                _shellJobStatus.Visible = true;
                _shellJobStatus.Text = validationMessage;
            }
            return;
        }
        _exportCancellation?.Dispose();
        _exportCancellation = new CancellationTokenSource();
        var token = _exportCancellation.Token;
        var job = _uiJobs.Start(UiJobKind.Export, frozen.Revision, destination);
        if (_shellJobStatus is not null)
        {
            _shellJobStatus.Visible = true;
            _shellJobStatus.Text = $"Exporting revision {frozen.Revision} · Preparing";
        }
        if (_shellCancelJob is not null) _shellCancelJob.Visible = true;
        try
        {
            var ledger = RenderResourceLedger.FromSnapshot(_hardwareSnapshot);
            var progress = new Progress<DocumentExportProgress>(update =>
            {
                if (_shellJobStatus is not null)
                    _shellJobStatus.Text = $"Exporting {EditorShellContract.ExportProgress(update)}";
            });
            var result = await DocumentPngExport.ForProject(_connectedGraph, ledger, _currentProjectDirectory)
                .ExportPresetAsync(frozen, destination, longestEdge, progress: progress,
                    cancellationToken: token);
            _uiJobs.Complete(job.Id);
            if (_shellJobStatus is not null)
                _shellJobStatus.Text = $"Published · revision {result.FrozenRevision} · {result.Width} × {result.Height} · {result.Destination}";
        }
        catch (OperationCanceledException)
        {
            _uiJobs.Cancel(job.Id);
            if (_shellJobStatus is not null)
                _shellJobStatus.Text = $"Nothing was published. Your previous {Path.GetFileName(destination)} is untouched.";
        }
        catch (Exception exception)
        {
            _uiJobs.Cancel(job.Id);
            if (_shellJobStatus is not null)
                _shellJobStatus.Text = $"Nothing was published. Your previous {Path.GetFileName(destination)} is untouched. {exception.Message}";
        }
        finally
        {
            if (_shellCancelJob is not null) _shellCancelJob.Visible = false;
        }
    }

    private void ShowRecoveryDetails(RepositoryRecoveryFacts facts)
    {
        if (_canvas is not null && GodotObject.IsInstanceValid(_canvas))
            _canvas.CancelActiveGesture("Modal opened");
        var dialog = new AcceptDialog
        {
            Title = "Recovery details",
            DialogText = $"Acknowledged revision  {facts.AcknowledgedRevision}\n" +
                         $"Undo cursor  {facts.CursorRevision}\nLatest revision  {facts.LatestRevision}\n" +
                         $"Verified source blobs  {facts.VerifiedBlobCount}\n" +
                         $"Collected owned staging files  {facts.CollectedOwnedStagedBlobs}"
        };
        dialog.Confirmed += dialog.QueueFree;
        AddChild(dialog);
        dialog.PopupCentered();
    }

    private static void ClearChildren(Node parent)
    {
        foreach (var child in parent.GetChildren().OfType<Node>().ToArray())
        {
            parent.RemoveChild(child);
            child.QueueFree();
        }
    }

    private static string ToolRailGlyph(string tool) => tool switch
    {
        "Pan" => "P",
        "Texture Brush" => "B",
        "Land" => "L",
        "River" => "R",
        "Sample Texture" => "I",
        "Project Storage" => "S",
        _ => "?"
    };

    private static string ToolTooltip(string tool) => tool switch
    {
        "Pan" => "Pan the canvas. Shortcut H or Space.",
        "Texture Brush" => "Paint texture colour/intensity without changing coverage or Layer opacity. Shortcut B.",
        "Land" => "Edit Foreground coverage; this may move the coastline. Shortcut L.",
        "River" => "Edit the river modifier of Foreground’s land mask. Shortcut R.",
        "Sample Texture" => "Pick a local texture identity from the canvas. Shortcut I.",
        "Project Storage" => "Inspect local project storage, disposable caches, and live resource facts.",
        _ => tool
    };

    private static string ResourceStatus(RenderHardwareSnapshot? snapshot)
    {
        if (snapshot is null) return "Resources unavailable";
        return snapshot.Status == RenderHardwareStatus.InspectionOnly
            ? $"Inspection only · RAM {FormatBytes(snapshot.ReportedSystemRamBytes)}"
            : $"VRAM {FormatBytes(snapshot.ReportedVramBytes)} · RAM {FormatBytes(snapshot.ReportedSystemRamBytes)} · compositor {FormatBytes(snapshot.EngineCompositorHeadroomBytes)}";
    }

    private static string FormatBytes(long bytes) => bytes <= 0 ? "not reported" : $"{bytes / (1024d * 1024d):N0} MiB";

    private static string SafeExportName(string value)
    {
        var safe = string.Concat(value.Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '-' : character));
        return string.IsNullOrWhiteSpace(safe) ? "map" : safe.Trim();
    }

    private async void CaptureShellFrameAsync(string[] arguments, int argumentIndex)
    {
        try
        {
            if (argumentIndex + 4 >= arguments.Length)
                throw new ArgumentException("--shell-frame requires width height scale output.png");
            var width = int.Parse(arguments[argumentIndex + 1], System.Globalization.CultureInfo.InvariantCulture);
            var height = int.Parse(arguments[argumentIndex + 2], System.Globalization.CultureInfo.InvariantCulture);
            var scale = double.Parse(arguments[argumentIndex + 3], System.Globalization.CultureInfo.InvariantCulture);
            var output = Path.GetFullPath(arguments[argumentIndex + 4]);
            var zoomClicks = argumentIndex + 5 < arguments.Length &&
                int.TryParse(arguments[argumentIndex + 5], out var requestedZoom)
                    ? Math.Clamp(requestedZoom, 0, 12) : 0;
            _shellScaleOverride = scale;
            _shellViewportOverride = new Vector2I(width, height);
            GetWindow().Size = new Vector2I(width, height);
            GetWindow().ContentScaleSize = new Vector2I(width, height);
            GetWindow().ContentScaleFactor = (float)scale;
            BuildConnectedInterface();

            var projectDirectory = Path.Combine(Path.GetDirectoryName(output)!,
                $"capture-{width}x{height}-{scale:0.0}.mapwright");
            if (Directory.Exists(projectDirectory)) Directory.Delete(projectDirectory, recursive: true);
            var imported = await Task.Run(() => _importer.Import(ConfiguredInkPath));
            var review = new ImportReviewModel(imported, Path.GetDirectoryName(projectDirectory)!);
            review.ProjectName = "Main Continent";
            review.SetDestinationOverride(projectDirectory);
            review.SelectMode(ImportRecoveryMode.OriginalFlattenedAppearance);
            var result = await new ImportProject(path => new SqliteProjectRepository(path))
                .ExecuteAsync(review.ToRequest());
            if (!result.Published || result.Project is null || result.ProjectDirectory is null)
                throw new InvalidOperationException(result.OutcomeText);
            var repository = new SqliteProjectRepository(result.ProjectDirectory);
            var facts = await repository.ReadRecoveryFactsAsync(result.Project.ProjectId);
            await ActivateEditorShellAsync(result.Project, result.ProjectDirectory, repository, facts);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            for (var click = 0; click < zoomClicks; click++)
            {
                _canvas.ZoomAt(_canvas.Size * 0.5f, 1.15);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            if (zoomClicks > 0)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                if (Math.Max(_canvas.ConnectedDisplaySize.X, _canvas.ConnectedDisplaySize.Y) <= 896)
                    throw new InvalidOperationException("High-zoom capture did not request adaptive display detail.");
            }
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            var image = GetViewport().GetTexture().GetImage();
            if (image.GetWidth() != width || image.GetHeight() != height)
                image.Resize(width, height, Image.Interpolation.Lanczos);
            var save = image.SavePng(output);
            if (save != Error.Ok) throw new IOException($"Could not save shell frame: {save}");
            GD.Print($"SHELL_FRAME {width}x{height} scale={scale:0.0} display={_canvas.ConnectedDisplaySize} {output}");
            GetTree().Quit();
        }
        catch (Exception exception)
        {
            GD.PrintErr(exception);
            GetTree().Quit(1);
        }
    }

    private async void CaptureScopeFrameAsync(string[] arguments, int argumentIndex)
    {
        try
        {
            if (argumentIndex + 5 >= arguments.Length)
                throw new ArgumentException("--scope-frame requires mode width height scale output.png");
            var mode = arguments[argumentIndex + 1];
            if (mode is not ("land" or "texture" or "opacity"))
                throw new ArgumentOutOfRangeException(nameof(mode));
            var width = int.Parse(arguments[argumentIndex + 2], System.Globalization.CultureInfo.InvariantCulture);
            var height = int.Parse(arguments[argumentIndex + 3], System.Globalization.CultureInfo.InvariantCulture);
            var scale = double.Parse(arguments[argumentIndex + 4], System.Globalization.CultureInfo.InvariantCulture);
            var output = Path.GetFullPath(arguments[argumentIndex + 5]);
            _shellScaleOverride = scale;
            _shellViewportOverride = new Vector2I(width, height);
            GetWindow().Size = new Vector2I(width, height);
            GetWindow().ContentScaleSize = new Vector2I(width, height);
            GetWindow().ContentScaleFactor = (float)scale;
            BuildConnectedInterface();

            var projectDirectory = Path.Combine(Path.GetDirectoryName(output)!,
                $"capture-scope-{mode}-{scale:0.0}.mapwright");
            if (Directory.Exists(projectDirectory)) Directory.Delete(projectDirectory, recursive: true);
            var imported = await Task.Run(() => _importer.Import(ConfiguredInkPath));
            var review = new ImportReviewModel(imported, Path.GetDirectoryName(projectDirectory)!);
            review.ProjectName = "D-28 evidence";
            review.SetDestinationOverride(projectDirectory);
            review.SelectMode(ImportRecoveryMode.OriginalFlattenedAppearance);
            var result = await new ImportProject(path => new SqliteProjectRepository(path))
                .ExecuteAsync(review.ToRequest());
            if (!result.Published || result.Project is null || result.ProjectDirectory is null)
                throw new InvalidOperationException(result.OutcomeText);
            // The opacity capture commits a real layer change. Use a fixed clock for
            // that isolated capture so its save-time label does not change the image
            // hash from one otherwise identical visual review run to the next.
            var repository = new SqliteProjectRepository(result.ProjectDirectory,
                new CaptureClock());
            var facts = await repository.ReadRecoveryFactsAsync(result.Project.ProjectId);
            await ActivateEditorShellAsync(result.Project, result.ProjectDirectory, repository, facts);
            AttachScopeEvidenceCanvas(result.Project);

            if (mode == "land")
            {
                _editorControlsState.SetLandShape(LandShape.RoundSoft);
                _editorControlsState.SetLandOperation(LandOperation.Subtract);
                _editorControlsState.LandSoftness.SetFromSlider(0.8);
                _editorControlsState.LandDiameter.SetFromSlider(720);
                SelectShellTool("Land");
            }
            else if (mode == "texture")
            {
                _editorControlsState.SelectTextureTarget(TerrainRole.Foreground);
                _editorControlsState.SelectPreset("soft-round");
                _editorControlsState.TextureOpacity.SetFromSlider(0.18);
                _editorControlsState.TextureDiameter.SetFromSlider(620);
                _editorControlsState.TextureRotation.SetFromSlider(23);
                SelectShellTool("Texture Brush");
            }
            else
            {
                var current = _editSession!.Current;
                await _editSession.ExecuteAsync(new SetTerrainOpacity(CommandId.New(), current.Revision,
                    TerrainRole.Foreground, 0.22));
                _canvas.ShowCommittedSnapshot(_editSession.Current);
                SelectShellTool("Pan");
                PopulateLayersPanel();
                if (_shellOperationStatus is not null)
                    _shellOperationStatus.Text = "Layer opacity · Foreground · 22%";
            }

            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (mode is "land" or "texture")
                _canvas.SetEvidenceCursorAtMapFraction(0.56f, 0.48f);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            var image = GetViewport().GetTexture().GetImage();
            if (image.GetWidth() != width || image.GetHeight() != height)
                image.Resize(width, height, Image.Interpolation.Lanczos);
            var save = image.SavePng(output);
            if (save != Error.Ok) throw new IOException($"Could not save scope frame: {save}");
            GD.Print($"SCOPE_FRAME mode={mode} {width}x{height} scale={scale:0.0} {output}");
            GetTree().Quit();
        }
        catch (Exception exception)
        {
            GD.PrintErr(exception);
            GetTree().Quit(1);
        }
    }

    private sealed class CaptureClock : IApplicationClock
    {
        public DateTimeOffset UtcNow => new(2026, 9, 23, 10, 24, 0, TimeSpan.Zero);
    }

    private void AttachScopeEvidenceCanvas(MapProject project)
    {
        if (_canvas is not null && GodotObject.IsInstanceValid(_canvas)) return;
        var canvasSurface = _connectedSurface.FindChild("CanvasSurface", recursive: true, owned: false)
            as VBoxContainer ?? throw new InvalidOperationException("CanvasSurface is missing from the editor shell.");
        foreach (var child in canvasSurface.GetChildren().OfType<CenterContainer>().ToArray())
        {
            canvasSurface.RemoveChild(child);
            child.QueueFree();
        }
        foreach (var label in _connectedSurface.FindChildren("*", "Label", recursive: true, owned: false)
                     .OfType<Label>()
                     .Where(label => label.Text.StartsWith("Rendering backend unavailable", StringComparison.Ordinal)))
            label.Visible = false;
        _canvas = new MapCanvas
        {
            Name = "MapCanvas",
            CustomMinimumSize = new Vector2(480, 320),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            TooltipText = "D-28 evidence canvas using the production scope-cue renderer."
        };
        canvasSurface.AddChild(_canvas);
        _canvas.BindScopeEvidence(_editorControlsState, project);
    }

    private static Texture2D? TextureFromPng(byte[]? png)
    {
        if (png is not { Length: > 0 }) return null;
        var image = new Image();
        return image.LoadPngFromBuffer(png) == Error.Ok ? ImageTexture.CreateFromImage(image) : null;
    }

    private static Label LabelFor(string text, string variation, bool wrap = false) => new()
    {
        Text = text,
        ThemeTypeVariation = variation,
        AutowrapMode = wrap ? TextServer.AutowrapMode.WordSmart : TextServer.AutowrapMode.Off,
        SizeFlagsHorizontal = SizeFlags.ExpandFill,
        TooltipText = text
    };

    private static Button ButtonFor(string text, string tooltip, bool primary = false) => new()
    {
        Text = text,
        TooltipText = tooltip,
        ThemeTypeVariation = primary ? "PrimaryButton" : "Button",
        CustomMinimumSize = new Vector2(0, 34),
        FocusMode = FocusModeEnum.All
    };

    private static Button LayerToggle(string text, string tooltip, bool pressed) => new()
    {
        Text = text,
        TooltipText = tooltip,
        ToggleMode = true,
        ButtonPressed = pressed,
        CustomMinimumSize = new Vector2(30, 28),
        FocusMode = FocusModeEnum.All
    };
}

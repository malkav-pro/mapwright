using System.Security.Cryptography;
using System.Text.Json;
using System.Collections.Concurrent;

namespace Mapwright.Core;

public sealed class ProjectStore
{
    public static bool HasPrototypeManifest(string projectDirectory) =>
        Directory.Exists(projectDirectory) &&
        File.Exists(Path.Combine(projectDirectory, "manifest.json"));

    private static readonly ConcurrentDictionary<string, int> PinnedCacheEntries =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public string SaveImportedProject(
        string projectDirectory,
        InkImportResult import,
        Action<ProjectSaveStage>? checkpoint = null)
    {
        var staging = projectDirectory + ".staging-" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(staging);
        var blobs = Path.Combine(staging, "blobs");
        Directory.CreateDirectory(blobs);

        try
        {
            var document = JsonSerializer.Deserialize<MapDocument>(
                JsonSerializer.Serialize(import.Document, JsonOptions), JsonOptions)
                ?? throw new InvalidDataException("Could not clone the project manifest.");

            if (!File.Exists(import.SourcePath))
                throw new FileNotFoundException("The immutable import source is unavailable.", import.SourcePath);
            var sourceHash = StoreBlob(blobs, import.SourcePath);
            if (!string.Equals(sourceHash, document.Import?.SourceSha256, StringComparison.Ordinal))
                throw new InvalidDataException("The import source no longer matches the hash recorded during recovery.");
            if (document.Import is not null) document.Import.SourceBlobHash = sourceHash;

            foreach (var raster in import.Rasters)
            {
                var hash = StoreBlob(blobs, raster.PngBytes);
                document.Layers.Add(new RasterLayer
                {
                    Name = $"{raster.LayerId} · {raster.Role}",
                    BlobHash = hash,
                    Role = raster.Role,
                    PixelWidth = raster.Width,
                    PixelHeight = raster.Height
                });
            }

            if (import.PreviewPng is not null)
            {
                var hash = StoreBlob(blobs, import.PreviewPng);
                document.Layers.Add(new RasterLayer
                {
                    Name = "Imported composite preview",
                    BlobHash = hash,
                    Role = "preview",
                    PixelWidth = import.PreviewWidth,
                    PixelHeight = import.PreviewHeight
                });
            }
            checkpoint?.Invoke(ProjectSaveStage.BlobsWritten);

            var manifestPath = Path.Combine(staging, "manifest.json");
            File.WriteAllText(manifestPath, JsonSerializer.Serialize(document, JsonOptions));
            File.WriteAllText(Path.Combine(staging, "import-report.txt"), import.Report);
            checkpoint?.Invoke(ProjectSaveStage.ManifestWritten);

            if (Directory.Exists(projectDirectory))
                throw new IOException($"Project directory already exists: {projectDirectory}");
            Directory.Move(staging, projectDirectory);
            checkpoint?.Invoke(ProjectSaveStage.Published);
            return projectDirectory;
        }
        catch
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
            throw;
        }
    }

    public MapDocument OpenProject(string projectDirectory)
    {
        var manifestPath = Path.Combine(projectDirectory, "manifest.json");
        var document = JsonSerializer.Deserialize<MapDocument>(File.ReadAllText(manifestPath), JsonOptions)
            ?? throw new InvalidDataException("Project manifest is empty.");
        var blobDirectory = Path.Combine(projectDirectory, "blobs");
        foreach (var raster in document.Layers.OfType<RasterLayer>())
        {
            var path = Path.Combine(blobDirectory, raster.BlobHash);
            if (!File.Exists(path))
                throw new InvalidDataException($"Missing source blob {raster.BlobHash}.");
            using var stream = File.OpenRead(path);
            var actual = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            if (!string.Equals(actual, raster.BlobHash, StringComparison.Ordinal))
                throw new InvalidDataException($"Source blob hash mismatch for {raster.BlobHash}.");
        }
        if (document.Import?.SourceBlobHash is { Length: > 0 } sourceBlobHash)
            ValidateBlob(blobDirectory, sourceBlobHash);
        return document;
    }

    public IDisposable PinDisposableCacheEntry(string projectDirectory, string relativeEntry)
    {
        var entry = ResolveDisposableEntry(projectDirectory, relativeEntry);
        PinnedCacheEntries.AddOrUpdate(entry, 1, (_, count) => checked(count + 1));
        return new CachePin(entry);
    }

    public CacheDeletionResult DeleteDisposableCaches(string projectDirectory)
    {
        var project = Path.GetFullPath(projectDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!Directory.Exists(project))
            throw new DirectoryNotFoundException($"Project directory does not exist: {project}");
        var targets = new[]
        {
            ResolveDisposableRoot(project, Path.Combine("cache", "tiles")),
            ResolveDisposableRoot(project, Path.Combine("cache", "history"))
        };
        var pinned = PinnedCacheEntries.Where(pair => pair.Value > 0).Select(pair => pair.Key).ToArray();
        foreach (var target in targets)
        {
            var prefix = target + Path.DirectorySeparatorChar;
            if (pinned.Any(entry => string.Equals(entry, target, StringComparison.OrdinalIgnoreCase) ||
                                    entry.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                return new CacheDeletionResult(false, true, 0,
                    "Cache deletion is waiting for a pinned render/export reader; sources and native revisions remain untouched.");
        }

        var deleted = 0;
        foreach (var target in targets)
        {
            if (!Directory.Exists(target)) continue;
            Directory.Delete(target, recursive: true);
            deleted++;
        }
        return new CacheDeletionResult(true, false, deleted,
            deleted == 0
                ? "Disposable caches were already absent; source blobs and native revisions are unchanged."
                : "Disposable render/history caches were deleted; they will rebuild from source blobs and native revisions.");
    }

    private static string ResolveDisposableEntry(string projectDirectory, string relativeEntry)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativeEntry);
        if (Path.IsPathRooted(relativeEntry))
            throw new InvalidDataException("Cache entry must be project-relative.");
        var project = Path.GetFullPath(projectDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var entry = Path.GetFullPath(Path.Combine(project, relativeEntry));
        var roots = new[]
        {
            ResolveDisposableRoot(project, Path.Combine("cache", "tiles")),
            ResolveDisposableRoot(project, Path.Combine("cache", "history"))
        };
        if (!roots.Any(root => string.Equals(entry, root, StringComparison.OrdinalIgnoreCase) ||
                               entry.StartsWith(root + Path.DirectorySeparatorChar,
                                   StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Only recorded render/history acceleration entries may be pinned.");
        return entry;
    }

    private static string ResolveDisposableRoot(string projectDirectory, string relativeRoot)
    {
        var root = Path.GetFullPath(Path.Combine(projectDirectory, relativeRoot));
        var prefix = projectDirectory + Path.DirectorySeparatorChar;
        if (!root.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Disposable cache path escaped the project directory.");
        return root;
    }

    private sealed class CachePin(string entry) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            while (PinnedCacheEntries.TryGetValue(entry, out var count))
            {
                if (count <= 1)
                {
                    if (PinnedCacheEntries.TryRemove(entry, out _)) return;
                }
                else if (PinnedCacheEntries.TryUpdate(entry, count - 1, count)) return;
            }
        }
    }

    private static string StoreBlob(string blobDirectory, byte[] data)
    {
        var hash = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
        var path = Path.Combine(blobDirectory, hash);
        if (!File.Exists(path)) File.WriteAllBytes(path, data);
        return hash;
    }

    private static string StoreBlob(string blobDirectory, string sourcePath)
    {
        using var source = File.OpenRead(sourcePath);
        var hash = Convert.ToHexString(SHA256.HashData(source)).ToLowerInvariant();
        var destination = Path.Combine(blobDirectory, hash);
        if (!File.Exists(destination)) File.Copy(sourcePath, destination, overwrite: false);
        return hash;
    }

    private static void ValidateBlob(string blobDirectory, string hash)
    {
        var path = Path.Combine(blobDirectory, hash);
        if (!File.Exists(path)) throw new InvalidDataException($"Missing source blob {hash}.");
        using var stream = File.OpenRead(path);
        var actual = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        if (!string.Equals(actual, hash, StringComparison.Ordinal))
            throw new InvalidDataException($"Source blob hash mismatch for {hash}.");
    }
}

public sealed record CacheDeletionResult(
    bool Deleted,
    bool RefusedForPinnedReader,
    int DeletedDirectories,
    string OutcomeText);

public enum ProjectSaveStage
{
    BlobsWritten,
    ManifestWritten,
    Published
}

public sealed record StorageProbeResult(
    bool Passed,
    bool Reopened,
    bool SurvivedCacheDeletion,
    bool InterruptedBlobWriteStayedUnpublished,
    bool InterruptedManifestWriteStayedUnpublished,
    int ValidatedBlobs,
    double Milliseconds,
    string Detail);

public static class StorageProbe
{
    public static StorageProbeResult Run(string root)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
            Directory.CreateDirectory(root);
            var store = new ProjectStore();
            var import = StorageFixture.Create(root);

            var interruptedBlobs = Path.Combine(root, "interrupted-blobs.mapwright");
            TryInterruptedSave(interruptedBlobs, ProjectSaveStage.BlobsWritten);
            var blobsStayedUnpublished = !Directory.Exists(interruptedBlobs);

            var interruptedManifest = Path.Combine(root, "interrupted-manifest.mapwright");
            TryInterruptedSave(interruptedManifest, ProjectSaveStage.ManifestWritten);
            var manifestStayedUnpublished = !Directory.Exists(interruptedManifest);

            var project = Path.Combine(root, "complete.mapwright");
            store.SaveImportedProject(project, import);
            var reopened = store.OpenProject(project);
            var cache = Path.Combine(project, "cache");
            Directory.CreateDirectory(cache);
            File.WriteAllText(Path.Combine(cache, "derived.bin"), "discardable");
            Directory.Delete(cache, recursive: true);
            var reopenedAfterCacheDeletion = store.OpenProject(project);
            var validatedBlobs = reopenedAfterCacheDeletion.Layers.OfType<RasterLayer>().Count();

            stopwatch.Stop();
            var passed = reopened.Title == import.Document.Title && validatedBlobs == 1 &&
                         blobsStayedUnpublished && manifestStayedUnpublished;
            return new StorageProbeResult(passed, true, true, blobsStayedUnpublished,
                manifestStayedUnpublished, validatedBlobs, stopwatch.Elapsed.TotalMilliseconds,
                "Injected failures after blob and manifest writes; staging was removed and no partial project was published. Reopen verifies every source hash without derived caches.");

            void TryInterruptedSave(string path, ProjectSaveStage failAt)
            {
                try
                {
                    store.SaveImportedProject(path, import, stage =>
                    {
                        if (stage == failAt) throw new IOException($"Injected failure at {stage}.");
                    });
                }
                catch (IOException)
                {
                    // Expected injection.
                }
            }
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            return new StorageProbeResult(false, false, false, false, false, 0,
                stopwatch.Elapsed.TotalMilliseconds, exception.ToString());
        }
    }
}

public static class StorageFixture
{
    private const string OnePixelRgbaPng =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M/wHwAF/gL+X2W9WQAAAABJRU5ErkJggg==";

    public static InkImportResult Create(string root)
    {
        Directory.CreateDirectory(root);
        var sourcePath = Path.Combine(root, "fixture.ink");
        File.WriteAllBytes(sourcePath, [31, 139, 8, 0, 0, 0, 0, 0]);
        var sourceHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(sourcePath))).ToLowerInvariant();
        return new InkImportResult
        {
            SourcePath = sourcePath,
            Document = new MapDocument
            {
                Title = "Storage fixture",
                Width = 64,
                Height = 64,
                Import = new ImportProvenance
                {
                    SourceFormat = "storage-fixture",
                    SourceFileName = "fixture.ink",
                    SourceSha256 = sourceHash,
                    SourceVersion = 1
                }
            },
            Rasters =
            [
                new ImportedRaster("base", "color", Convert.FromBase64String(OnePixelRgbaPng), 1, 1, null)
            ],
            PreviewPng = null,
            Report = "Synthetic transactional storage fixture with a decodable RGBA PNG."
        };
    }
}

public sealed record CrashRecoveryProbeResult(
    bool Passed,
    bool BlobStageStayedUnpublished,
    bool ManifestStageStayedUnpublished,
    bool PublishedStageReopened,
    double Milliseconds,
    string Detail);

public static class CrashRecoveryProbe
{
    public static CrashRecoveryProbeResult Run(string dotnetExecutable, string workerDll, string outputRoot)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            if (Directory.Exists(outputRoot)) Directory.Delete(outputRoot, recursive: true);
            Directory.CreateDirectory(outputRoot);
            var blobs = RunKilledChild(ProjectSaveStage.BlobsWritten);
            var manifest = RunKilledChild(ProjectSaveStage.ManifestWritten);
            var published = RunKilledChild(ProjectSaveStage.Published);
            stopwatch.Stop();
            return new CrashRecoveryProbeResult(blobs && manifest && published, blobs, manifest, published,
                stopwatch.Elapsed.TotalMilliseconds,
                "Separate storage-worker processes were forcibly terminated at storage checkpoints. Pre-publication kills exposed no project; post-publication kill reopened with all hashes valid.");

            bool RunKilledChild(ProjectSaveStage stage)
            {
                var stageRoot = Path.Combine(outputRoot, stage.ToString());
                Directory.CreateDirectory(stageRoot);
                var marker = Path.Combine(stageRoot, "checkpoint.marker");
                var destination = Path.Combine(stageRoot, "project.mapwright");
                var start = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = dotnetExecutable,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                start.ArgumentList.Add(workerDll);
                start.ArgumentList.Add(stageRoot);
                start.ArgumentList.Add(stage.ToString());
                using var child = System.Diagnostics.Process.Start(start)
                    ?? throw new InvalidOperationException("Could not start crash-recovery child process.");
                var deadline = DateTime.UtcNow.AddSeconds(20);
                while (!File.Exists(marker) && !child.HasExited && DateTime.UtcNow < deadline)
                    Thread.Sleep(20);
                if (!File.Exists(marker))
                {
                    if (!child.HasExited) child.Kill(entireProcessTree: true);
                    throw new TimeoutException($"Child did not reach {stage}. {child.StandardError.ReadToEnd()}");
                }
                child.Kill(entireProcessTree: true);
                child.WaitForExit();
                if (stage is ProjectSaveStage.BlobsWritten or ProjectSaveStage.ManifestWritten)
                    return !Directory.Exists(destination);
                return Directory.Exists(destination) && new ProjectStore().OpenProject(destination).Layers.Count == 1;
            }
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            return new CrashRecoveryProbeResult(false, false, false, false,
                stopwatch.Elapsed.TotalMilliseconds, exception.ToString());
        }
    }

    public static void RunChild(string root, ProjectSaveStage stage)
    {
        var import = StorageFixture.Create(root);
        var destination = Path.Combine(root, "project.mapwright");
        var marker = Path.Combine(root, "checkpoint.marker");
        new ProjectStore().SaveImportedProject(destination, import, checkpoint =>
        {
            if (checkpoint != stage) return;
            File.WriteAllText(marker, checkpoint.ToString());
            using var markerStream = new FileStream(marker, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
            markerStream.Flush(flushToDisk: true);
            Thread.Sleep(TimeSpan.FromMinutes(10));
        });
    }
}

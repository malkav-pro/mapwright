using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using Mapwright.App;
using Mapwright.Domain;
using Mapwright.Export;
using Mapwright.Rendering;

namespace Mapwright.Acceptance;

public sealed class GpuTerrainParityCaseProvider : IConnectedCaseProvider
{
    public void Register(ConnectedCaseRegistry registry) =>
        registry.Register("GpuTerrainParity", GpuTerrainParityCase.RunAsync);
}

/// <summary>
/// Independent correctness gate for the production GPU renderer. Every fixture is
/// evaluated by the CPU oracle (<see cref="ConnectedTerrainGraph.EvaluateRegionAtSize"/>)
/// and by the GPU renderer through <see cref="ConnectedTerrainGraph.PrepareGpuSources"/>,
/// and the complete RGBA8 canvases are compared. The gate is ≤1 channel value; exact
/// byte matches are reported separately. Readback is validation-only.
/// </summary>
public static class GpuTerrainParityCase
{
    private sealed record Comparison(string Fixture, int Width, int Height, int Strokes,
        long ExactChannels, long ChannelsOverOne, int MaximumDifference, long ChangedFromBase);

    public static Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var renderer = ConnectedGpuTerrainRenderer.TryCreate(out var reason)
            ?? throw new NotSupportedException($"GPU terrain parity requires the GPU renderer: {reason}");
        var root = Path.Combine(Path.GetTempPath(), $"mapwright-gpu-parity-{Guid.NewGuid():N}");
        var results = new List<Comparison>();
        var generation = 0L;
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "blobs"));
            void Compare(string name, MapProject project, int width, int height)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var graph = new ConnectedTerrainGraph(root);
                var oracle = graph.EvaluateRegionAtSize(project, width, height, 0, 0, width, height).Rgba;
                var baseline = graph.EvaluateRegionAtSize(StripEdits(project), width, height,
                    0, 0, width, height).Rgba;
                var gpuGraph = new ConnectedTerrainGraph(root);
                var sources = gpuGraph.PrepareGpuSources(project, width, height);
                var frame = renderer.Render(project, sources, ++generation);
                var gpu = renderer.ReadbackForValidation(frame.Generation);
                if (gpu.Length != oracle.Length)
                    throw new InvalidDataException($"{name}: GPU readback has {gpu.Length} bytes, expected {oracle.Length}.");
                long exact = 0, over = 0, changed = 0;
                var maximum = 0;
                for (var index = 0; index < oracle.Length; index++)
                {
                    var difference = Math.Abs(gpu[index] - oracle[index]);
                    if (difference == 0) exact++;
                    if (difference > 1) over++;
                    maximum = Math.Max(maximum, difference);
                    if (oracle[index] != baseline[index]) changed++;
                }
                var strokes = project.TerrainLayers.Sum(layer => layer.Strokes.Length +
                    layer.ResolvedTextureStrokes.Length + layer.ResolvedLandStrokes.Length);
                results.Add(new Comparison(name, width, height, strokes, exact, over, maximum, changed));
                GD.Print($"GPU_PARITY {name} {width}x{height} strokes={strokes} exact={exact}/{oracle.Length} " +
                         $"over1={over} max={maximum} changedFromBase={changed}");
                renderer.RetireBefore(frame.Generation);
            }

            // Flattened, prepared-less (stretched preview) document with transparent hidden RGB.
            var preview = Pattern(97, 71, seed: 1, transparentHoles: true);
            var flattened = FlattenedProject(root, preview, 97, 71);
            foreach (var variant in Variants(flattened, seed: 11))
            {
                Compare($"flattened-{variant.Name}", variant.Project, 97, 71);
                Compare($"flattened-{variant.Name}-resampled", variant.Project, 64, 48);
            }

            // Editable (stretched) document: independent Background, Foreground and soft coverage.
            var editable = EditableProject(root, 80, 60, placed: false);
            foreach (var variant in Variants(editable, seed: 23))
            {
                Compare($"editable-{variant.Name}", variant.Project, 80, 60);
                Compare($"editable-{variant.Name}-upsampled", variant.Project, 120, 90);
            }

            // Normalized v3 editable document: placed rasters through map transforms.
            var placed = EditableProject(root, 0, 0, placed: true);
            var placedSize = MapCanvas.ResolveSamplingSize(placed, 896);
            foreach (var variant in Variants(placed, seed: 37))
                Compare($"placed-{variant.Name}", variant.Project, placedSize.X, placedSize.Y);

            var evidencePath = Path.Combine(Path.GetFullPath(ProjectSettings.GlobalizePath("res://")),
                "artifacts", "gpu-terrain-parity.json");
            Directory.CreateDirectory(Path.GetDirectoryName(evidencePath)!);
            File.WriteAllText(evidencePath, JsonSerializer.Serialize(new
            {
                gate = "no RGBA8 channel differs from the CPU oracle by more than 1",
                results,
                passed = results.All(result => result.ChannelsOverOne == 0)
            }, new JsonSerializerOptions { WriteIndented = true }));
            var failures = results.Where(result => result.ChannelsOverOne != 0).ToArray();
            if (failures.Length > 0)
                throw new InvalidDataException("GPU/CPU parity failed: " + string.Join("; ", failures.Select(
                    failure => $"{failure.Fixture} over1={failure.ChannelsOverOne} max={failure.MaximumDifference}")));
            if (results.Any(result => result.ChangedFromBase == 0 && result.Strokes > 0))
                throw new InvalidDataException("A parity fixture with edits did not change any pixel.");
            return Task.FromResult(results.Count * 2);
        }
        finally
        {
            renderer.Dispose();
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }

    private static MapProject StripEdits(MapProject project) => project with
    {
        River = null,
        TerrainLayers = project.TerrainLayers.Select(layer => layer with
        {
            Strokes = ImmutableArray<PaintStroke>.Empty,
            TextureStrokes = ImmutableArray<TexturePaintStroke>.Empty,
            LandStrokes = ImmutableArray<LandStroke>.Empty
        }).ToImmutableArray()
    };

    private static IEnumerable<(string Name, MapProject Project)> Variants(MapProject project, int seed)
    {
        var random = new Random(seed);
        yield return ("base", project);
        var legacy = WithStrokes(project, random, legacy: 6, resolved: 0, land: 0);
        yield return ("legacy", legacy);
        var mixed = WithStrokes(project, random, legacy: 4, resolved: 5, land: 6);
        yield return ("mixed", mixed);
        var withRiver = WithRiver(mixed, random, softness: 0.35);
        yield return ("mixed-soft-river", withRiver);
        yield return ("mixed-hard-river", WithRiver(mixed, random, softness: 0));
        var background = withRiver.RequireRole(TerrainRole.Background);
        var foreground = withRiver.RequireRole(TerrainRole.Foreground);
        yield return ("opacity", withRiver with
        {
            TerrainLayers = [background with { Opacity = 0.63 }, foreground with { Opacity = 0.41 }]
        });
        yield return ("background-hidden", withRiver with
        {
            TerrainLayers = [background with { Visible = false }, foreground]
        });
        yield return ("background-solo", withRiver with
        {
            TerrainLayers = [background with { Solo = true }, foreground]
        });
        yield return ("dense-legacy", WithStrokes(project, random, legacy: 15, resolved: 0, land: 0));
    }

    private static MapProject WithStrokes(MapProject project, Random random, int legacy, int resolved, int land)
    {
        var background = project.RequireRole(TerrainRole.Background);
        var foreground = project.RequireRole(TerrainRole.Foreground);
        var scale = Math.Max(project.Width, project.Height);
        ImmutableArray<MapPoint> Path(int count)
        {
            var builder = ImmutableArray.CreateBuilder<MapPoint>(count);
            var x = random.NextDouble() * project.Width;
            var y = random.NextDouble() * project.Height;
            for (var index = 0; index < count; index++)
            {
                x = Math.Clamp(x + (random.NextDouble() - 0.5) * scale * 0.12, 0, project.Width - 1e-6);
                y = Math.Clamp(y + (random.NextDouble() - 0.5) * scale * 0.12, 0, project.Height - 1e-6);
                builder.Add(new MapPoint(x, y));
            }
            return builder.MoveToImmutable();
        }
        string Hash() => Convert.ToHexString(SHA256.HashData(BitConverter.GetBytes(random.Next())));
        for (var index = 0; index < legacy; index++)
        {
            // Includes exact hard brushes (hardness 1) that exercise the hard edge path.
            var hardness = index % 3 == 0 ? 1 : random.NextDouble() * 0.9;
            var stroke = new PaintStroke(StrokeId.New(), Path(1 + random.Next(40)),
                new ResolvedBrush(Hash().ToLowerInvariant(), scale * (0.01 + random.NextDouble() * 0.08),
                    hardness, 0.3 + random.NextDouble() * 0.7, 0.2 + random.NextDouble() * 0.8,
                    0.2, 0, random.Next(), 1), false, TerrainStrokeKind.Texture);
            if (index % 2 == 0) background = background with { Strokes = background.Strokes.Add(stroke) };
            else foreground = foreground with { Strokes = foreground.Strokes.Add(stroke) };
        }
        var presets = TexturePresetCatalog.Inventory;
        for (var index = 0; index < resolved; index++)
        {
            var preset = presets[index % presets.Length];
            var recipe = ResolvedTextureBrush.FromDiameter(preset.Id, preset.Tip,
                scale * (0.02 + random.NextDouble() * 0.15), preset.Hardness,
                0.4 + random.NextDouble() * 0.6, 0.3 + random.NextDouble() * 0.7, preset.Spacing,
                Hash().ToLowerInvariant(), 0.5 + random.NextDouble() * 2, random.NextDouble() * 360 - 180, 0,
                preset.Roughness, preset.CornerSmoothing, preset.Taper, random.Next());
            var samples = Path(2 + random.Next(30));
            var stroke = TexturePaintStroke.Create(StrokeId.New(), samples, recipe, samples[0]);
            if (index % 2 == 0) foreground = foreground with { TextureStrokes = foreground.ResolvedTextureStrokes.Add(stroke) };
            else background = background with { TextureStrokes = background.ResolvedTextureStrokes.Add(stroke) };
        }
        for (var index = 0; index < land; index++)
        {
            var edged = index % 2 == 0;
            var diameter = scale * (0.03 + random.NextDouble() * 0.15);
            var brush = edged
                ? ResolvedLandBrush.FromDiameter(LandShape.EdgedPolygon, diameter,
                    random.NextDouble(), index % 4 == 0 ? 0 : random.NextDouble(), 0, random.Next())
                : ResolvedLandBrush.FromDiameter(LandShape.RoundSoft, diameter, 0, 0,
                    index % 3 == 1 ? 0 : random.NextDouble(), random.Next());
            var stroke = new LandStroke(StrokeId.New(), Path(1 + random.Next(12)), brush,
                index % 3 == 2 ? LandOperation.Subtract : LandOperation.Add);
            foreground = foreground with { LandStrokes = foreground.ResolvedLandStrokes.Add(stroke) };
        }
        return project with { TerrainLayers = [background, foreground] };
    }

    private static MapProject WithRiver(MapProject project, Random random, double softness)
    {
        var points = Enumerable.Range(0, 4).Select(index => new MapPoint(
            project.Width * (0.1 + 0.27 * index), project.Height * (0.3 + random.NextDouble() * 0.4))).ToArray();
        var scale = Math.Max(project.Width, project.Height);
        var widths = points.Select(_ => Math.Max(1.5, scale * (0.01 + random.NextDouble() * 0.05))).ToArray();
        return project with
        {
            River = River.Create(RiverId.New(), project.RequireRole(TerrainRole.Foreground).Id,
                [.. points], [.. widths], bankSoftness: softness)
        };
    }

    private static MapProject FlattenedProject(string root, byte[] preview, int width, int height)
    {
        var previewHash = WriteBlob(root, preview, width, height);
        var source = new ImportedSourceReference("synthetic.ink", new string('a', 64), new string('a', 64),
            previewHash, width, height, ImportRecoveryMode.OriginalFlattenedAppearance);
        return BaseProject(width, height, source, null, null, null);
    }

    private static MapProject EditableProject(string root, int width, int height, bool placed)
    {
        if (!placed)
        {
            var background = WriteBlob(root, Pattern(width, height, 2, false), width, height);
            var foreground = WriteBlob(root, Pattern(width, height, 3, true), width, height);
            var coverage = WriteBlob(root, Pattern(width, height, 4, true), width, height);
            var preview = WriteBlob(root, Pattern(width, height, 5, false), width, height);
            var source = new ImportedSourceReference("editable.ink", new string('b', 64), new string('b', 64),
                preview, width, height, ImportRecoveryMode.EditableRecoveredTerrain);
            return BaseProject(width, height, source, background, foreground, coverage);
        }

        // 2000 x 1500 scene units -> 1000 x 750 map units, 1024 x 768 editing sampling.
        var geometry = MapUnitPolicy.FromSource(2000, 1500, 1024);
        var previewHash = WriteBlob(root, Pattern(301, 226, 6, false), 301, 226);
        var backgroundHash = WriteBlob(root, Pattern(257, 199, 7, false), 257, 199);
        var foregroundHash = WriteBlob(root, Pattern(213, 170, 8, true), 213, 170);
        var coverageHash = WriteBlob(root, Pattern(131, 97, 9, true), 131, 97);
        ImportedRasterReference Reference(string id, string role, int order, string hash, int w, int h,
            ImportedRasterTransform transform) =>
            new(id, role, order, hash, w, h, null, transform, "straight-srgb", "alpha", "none");
        var references = ImmutableArray.Create(
            Reference("bg", "base", 0, backgroundHash, 257, 199,
                new ImportedRasterTransform(0, 0, geometry.Width / 257, geometry.Height / 199)),
            Reference("fg", "base", 1, foregroundHash, 213, 170,
                new ImportedRasterTransform(37.25, 18.5, 3.9, 3.7)),
            Reference("mask", "mask", 2, coverageHash, 131, 97,
                new ImportedRasterTransform(-12.75, 44.125, 6.6, 6.2)));
        var normalizedSource = new ImportedSourceReference("placed.ink", new string('c', 64), new string('c', 64),
            previewHash, 301, 226, ImportRecoveryMode.EditableRecoveredTerrain,
            RasterReferences: references, SourceSceneWidth: 2000, SourceSceneHeight: 1500,
            SourceToMapScale: geometry.Width / 2000);
        return BaseProject(geometry.Width, geometry.Height, normalizedSource, backgroundHash, foregroundHash,
            coverageHash) with
        {
            StorageFormatVersion = 3,
            EditingPixelWidth = geometry.EditingPixelWidth,
            EditingPixelHeight = geometry.EditingPixelHeight
        };
    }

    private static MapProject BaseProject(double width, double height, ImportedSourceReference source,
        string? background, string? foreground, string? coverage)
    {
        var backgroundLayer = new TerrainLayer(LayerId.New(), "Background", true, false, 1,
            new CoastlineStyle(0), ImmutableArray<PaintStroke>.Empty, TerrainRole.Background, background);
        var foregroundLayer = new TerrainLayer(LayerId.New(), "Foreground", true, false, 1,
            new CoastlineStyle(0), ImmutableArray<PaintStroke>.Empty, TerrainRole.Foreground, foreground,
            CoverageSourceBlobHash: coverage);
        var project = new MapProject(ProjectId.New(), MapId.New(), "GPU parity", width, height, 1,
            [backgroundLayer, foregroundLayer], ImportedSource: source);
        project.ValidateConnectedTerrain();
        return project;
    }

    private static byte[] Pattern(int width, int height, int seed, bool transparentHoles)
    {
        var random = new Random(seed);
        var bytes = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var offset = (y * width + x) * 4;
            bytes[offset] = (byte)((x * 7 + y * 3 + seed * 29) % 256);
            bytes[offset + 1] = (byte)((x * 2 + y * 9 + seed * 13) % 256);
            bytes[offset + 2] = (byte)random.Next(256);
            // Opaque, soft and fully transparent (hidden RGB) alpha regions.
            bytes[offset + 3] = !transparentHoles ? (byte)255
                : (x / 7 + y / 5) % 5 == 0 ? (byte)0
                : (x / 11 + y / 3) % 3 == 0 ? (byte)((x * 13 + y * 5) % 256) : (byte)255;
        }
        return bytes;
    }

    private static string WriteBlob(string root, byte[] rgba, int width, int height)
    {
        var temporary = Path.Combine(root, $"{Guid.NewGuid():N}.png");
        using (var writer = new StreamingPngWriter(temporary, width, height))
        {
            for (var row = 0; row < height; row++)
                writer.WriteRgbaRow(rgba.AsSpan(row * width * 4, width * 4));
            writer.Complete();
        }
        var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(temporary))).ToLowerInvariant();
        var destination = Path.Combine(root, "blobs", hash);
        if (File.Exists(destination)) File.Delete(temporary);
        else File.Move(temporary, destination);
        return hash;
    }
}

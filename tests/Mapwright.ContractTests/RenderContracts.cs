using System.Collections.Immutable;
using System.Reflection;
using Mapwright.Domain;
using Mapwright.Rendering;

public sealed class RenderContractCases : IContractCaseProvider
{
    private const string TextureHash = "7b59b5c6c76d7baf25075e30d6f0f74a9f2478e65d09a7a5cbcead2fb9276d21";

    public void Register(ContractRegistry registry)
    {
        registry.Add("render reference preserves transparent underlay and linear soft alpha",
            TransparentUnderlayAndLinearSoftAlpha);
        registry.Add("render reference applies Land before river subtraction",
            LandPrecedesRiverSubtraction);
        registry.Add("render reference is frozen and document anchored across tiles",
            FrozenRevisionAndTiledAnchoring);
        registry.Add("normalized tile keys invalidate pixel-space renderer caches",
            NormalizedTileKeysInvalidatePixelSpaceCaches);
        registry.Add("placed non-square rasters and 96-unit Land sample identically at 1K and 4K",
            PlacedRasterAndMapBrushAtIndependentSamplings);
        registry.Add("connected dirty tiles preserve texture Land and river pixels across a viewport seam",
            DirtyTilesMatchMixedReference);
        registry.Add("flattened viewport copy preserves opaque base and texture seams",
            FlattenedViewportTextureSeam);
        registry.Add("render dependency bounds accumulate sequential support and asymmetric shadows",
            DependencyBoundsAccumulateAndRemainAsymmetric);
        registry.Add("coast policy keeps generated edges unstyled across bank identities",
            CoastPolicyKeepsGeneratedEdgesUnstyled);
        registry.Add("render resource ledger admits exact caps and rejects one byte over",
            ResourceLedgerEnforcesEveryCap);
    }

    private static Task TransparentUnderlayAndLinearSoftAlpha()
    {
        var project = Fixture();
        var empty = Render(project, 2, 2, null, null, null, 0, 0, 2, 2);
        Pixel(empty, 2, 0, 0, 0, 0, 0, 0);
        var hiddenColour = Solid(2, 2, 220, 80, 60, 0);
        var transparentBackground = Render(project, 2, 2, hiddenColour, null, null,
            0, 0, 2, 2);
        Pixel(transparentBackground, 2, 0, 0, 0, 0, 0, 0);

        var blue = Solid(2, 2, 0, 0, 255, 255);
        var red = Solid(2, 2, 255, 0, 0, 128);
        var coverage = Enumerable.Repeat(1f, 4).ToArray();
        var composite = Render(project, 2, 2, blue, red, coverage, 0, 0, 2, 2);
        Pixel(composite, 2, 0, 0, 188, 0, 187, 255, tolerance: 1);

        Array.Fill(coverage, 0.5f);
        var soft = Render(project, 2, 2, null, red, coverage, 0, 0, 2, 2);
        Pixel(soft, 2, 0, 0, 255, 0, 0, 64, tolerance: 1);
        return Task.CompletedTask;
    }

    private static Task LandPrecedesRiverSubtraction()
    {
        var project = Fixture();
        var foreground = project.RequireRole(TerrainRole.Foreground);
        var land = new LandStroke(StrokeId.New(), [new MapPoint(4, 4)],
            ResolvedLandBrush.FromDiameter(LandShape.RoundSoft, 16, 0, 0, 0, seed: 4),
            LandOperation.Add);
        project = project with
        {
            TerrainLayers = project.TerrainLayers.SetItem(1,
                foreground with { LandStrokes = [land] }),
            River = River.Create(RiverId.New(), foreground.Id,
                [new MapPoint(0, 4), new MapPoint(8, 4)], [2d, 2d], bankSoftness: 0)
        };

        var rendered = Render(project, 8, 8, null, Solid(8, 8, 220, 180, 100, 255),
            new float[64], 0, 0, 8, 8);
        Pixel(rendered, 8, 4, 4, 0, 0, 0, 0);
        Pixel(rendered, 8, 4, 1, 220, 180, 100, 255);
        return Task.CompletedTask;
    }

    private static Task FrozenRevisionAndTiledAnchoring()
    {
        var project = Fixture();
        var background = project.RequireRole(TerrainRole.Background);
        var recipe = TexturePresetCatalog.Resolve("soft-round", TextureHash, seed: 29, diameter: 8)
            with { TextureScale = 1.75, RotationDegrees = 31, Jitter = 0 };
        var stroke = TexturePaintStroke.Create(StrokeId.New(),
            [new MapPoint(2, 4), new MapPoint(4, 4), new MapPoint(6, 4)], recipe,
            new MapPoint(1.25, -2.5));
        project = project with
        {
            Revision = 19,
            TerrainLayers = project.TerrainLayers.SetItem(0,
                background with { TextureStrokes = [stroke] })
        };

        var whole = RenderFrame(project, 8, 8, null, null, null, 0, 0, 8, 8);
        Equal(19L, whole.Revision, "Renderer did not retain the frozen snapshot revision.");
        var currentKey = ConnectedTerrainGraph.CreateTileKey(project, 8, 8, 0, 0, 8, 8);
        var newerKey = ConnectedTerrainGraph.CreateTileKey(project with { Revision = 20 }, 8, 8, 0, 0, 8, 8);
        True(currentKey != newerKey, "A newer mutable revision reused the frozen revision tile key.");
        var stitched = new byte[8 * 8 * 4];
        for (var tileY = 0; tileY < 8; tileY += 4)
        for (var tileX = 0; tileX < 8; tileX += 4)
        {
            var tile = Render(project, 8, 8, null, null, null, tileX, tileY, 4, 4);
            for (var row = 0; row < 4; row++)
                tile.AsSpan(row * 16, 16).CopyTo(stitched.AsSpan(((tileY + row) * 8 + tileX) * 4));
        }

        for (var index = 0; index < whole.Rgba.Length; index++)
            if (Math.Abs(whole.Rgba[index] - stitched[index]) > 1)
                throw new InvalidOperationException(
                    $"Whole/tiled reference differed by more than one channel at byte {index}.");
        True(whole.Rgba.Any(value => value != 0), "Texture fixture rendered no visible pixels.");
        return Task.CompletedTask;
    }

    private static Task NormalizedTileKeysInvalidatePixelSpaceCaches()
    {
        True(ConnectedTerrainGraph.RendererVersion >= 3,
            "Normalized map-space tiles must have a new renderer version; version 2 keys describe pixel-space source sampling.");
        return Task.CompletedTask;
    }

    private static Task PlacedRasterAndMapBrushAtIndependentSamplings()
    {
        var old = Fixture();
        var project = MapProject.CreateNormalizedFromGrid(old.ProjectId, old.MapId,
            "Placed rendering", 4, 3, 1024, old.TerrainLayers);
        var placed = new PlacedRaster(2, 1,
            [220, 40, 30, 255, 220, 40, 30, 255],
            new ImportedRasterTransform(250, 125, 250, 250));
        foreach (var longest in new[] { 1024, 4096 })
        {
            var width = longest;
            var height = longest * 3 / 4;
            int AtX(double mapX) => (int)(mapX / project.Width * width);
            int AtY(double mapY) => (int)(mapY / project.Height * height);
            var inside = ConnectedTerrainGraph.RenderPlacedReference(project, width, height,
                placed, null, null, AtX(300), AtY(200), 1, 1);
            Pixel(inside.Rgba, 1, 0, 0, 220, 40, 30, 255);
            var gap = ConnectedTerrainGraph.RenderPlacedReference(project, width, height,
                placed, null, null, AtX(200), AtY(200), 1, 1);
            Pixel(gap.Rgba, 1, 0, 0, 0, 0, 0, 0);
            var below = ConnectedTerrainGraph.RenderPlacedReference(project, width, height,
                placed, null, null, AtX(300), AtY(400), 1, 1);
            Pixel(below.Rgba, 1, 0, 0, 0, 0, 0, 0);

            var seam = AtX(250);
            var left = seam - 8;
            var top = AtY(200);
            var whole = ConnectedTerrainGraph.RenderPlacedReference(project, width, height,
                placed, null, null, left, top, 16, 2);
            var second = ConnectedTerrainGraph.RenderPlacedReference(project, width, height,
                placed, null, null, seam, top, 8, 2);
            var first = ConnectedTerrainGraph.RenderPlacedReference(project, width, height,
                placed, null, null, left, top, 8, 2);
            for (var row = 0; row < 2; row++)
            {
                True(whole.Rgba.AsSpan(row * 64, 32).SequenceEqual(
                    first.Rgba.AsSpan(row * 32, 32)), "Left tile changed at a seam.");
                True(whole.Rgba.AsSpan(row * 64 + 32, 32).SequenceEqual(
                    second.Rgba.AsSpan(row * 32, 32)), "Right tile changed at a seam.");
            }
        }

        var foreground = project.RequireRole(TerrainRole.Foreground);
        var land = new LandStroke(StrokeId.New(), [new MapPoint(500, 375)],
            ResolvedLandBrush.FromDiameter(LandShape.RoundSoft, 96, 0, 0, 0, 5),
            LandOperation.Add);
        project = project with { TerrainLayers = project.TerrainLayers.SetItem(1,
            foreground with { LandStrokes = [land] }) };
        var fullForeground = new PlacedRaster(1, 1, [25, 180, 65, 255],
            new ImportedRasterTransform(0, 0, 1000, 750));
        foreach (var longest in new[] { 1024, 4096 })
        {
            var left = longest * 4 / 10;
            var count = longest * 2 / 10;
            var row = longest * 3 / 8;
            var pixels = ConnectedTerrainGraph.RenderPlacedReference(project,
                longest, longest * 3 / 4, null, fullForeground, null,
                left, row, count, 1).Rgba;
            var covered = Enumerable.Range(0, count).Count(x => pixels[x * 4 + 3] >= 128);
            True(Math.Abs(covered / (double)longest - 0.096) < 0.003,
                $"The 96-unit brush covered {covered} of {longest} pixels at {longest} output pixels.");
        }

        var high = ConnectedTerrainGraph.RenderPlacedReference(project, 16_384, 12_288,
            null, fullForeground, null, 8192, 6144, 1, 1);
        True(high.Rgba[3] > 0, "A 16K tile request did not admit a map-unit edit.");
        return Task.CompletedTask;
    }

    private static Task DirtyTilesMatchMixedReference()
    {
        const int width = 260;
        const int height = 16;
        var project = Fixture();
        var foreground = project.RequireRole(TerrainRole.Foreground);
        var legacy = new PaintStroke(StrokeId.New(),
            [new MapPoint(7.7, 3.8), new MapPoint(7.9, 4.0)],
            new ResolvedBrush(TextureHash, 0.65, 0.5, 0.8, 0.8, 0.2, 0, 13, 1),
            false, TerrainStrokeKind.Texture);
        var recipe = TexturePresetCatalog.Resolve("soft-round", TextureHash, seed: 15, diameter: 2)
            with { Jitter = 0, TextureScale = 1.5, RotationDegrees = 21 };
        var resolved = TexturePaintStroke.Create(StrokeId.New(),
            [new MapPoint(7.65, 4.0), new MapPoint(7.85, 4.2)], recipe, new MapPoint(1, 1));
        var land = new LandStroke(StrokeId.New(), [new MapPoint(7.7, 4)],
            ResolvedLandBrush.FromDiameter(LandShape.RoundSoft, 2, 0, 0, 0, seed: 8),
            LandOperation.Add);
        project = project with
        {
            TerrainLayers = project.TerrainLayers.SetItem(1, foreground with
            {
                Strokes = [legacy], TextureStrokes = [resolved], LandStrokes = [land]
            }),
            River = River.Create(RiverId.New(), foreground.Id,
                [new MapPoint(7.2, 2), new MapPoint(7.9, 6)], [1d, 1d], 0.5)
        };
        var coverage = Enumerable.Repeat(0.25f, width * height).ToArray();
        var source = Solid(width, height, 180, 130, 85, 255);
        var whole = Render(project, width, height, null, source, coverage, 0, 0, width, height);
        var stitched = new byte[whole.Length];
        var tiles = ConnectedTerrainGraph.TilesForBounds(project, width, height, null);
        Equal(2, tiles.Count, "A 260-pixel viewport did not cross exactly two 256-pixel tiles.");
        foreach (var tile in tiles)
        {
            var pixels = Render(project, width, height, null, source, coverage,
                tile.Left, tile.Top, tile.Width, tile.Height);
            for (var row = 0; row < tile.Height; row++)
                pixels.AsSpan(row * tile.Width * 4, tile.Width * 4).CopyTo(
                    stitched.AsSpan(((tile.Top + row) * width + tile.Left) * 4));
        }
        for (var index = 0; index < whole.Length; index++)
            if (Math.Abs(whole[index] - stitched[index]) > 1)
                throw new InvalidOperationException($"Mixed render seam differs at byte {index}.");
        True(whole.Any(value => value != 0), "Mixed fixture produced no visible output.");
        var dirty = ConnectedTerrainGraph.TilesForBounds(project, width, height,
            new MapBounds(7.97, 3.7, 7.99, 4.3));
        Equal(1, dirty.Count, "Localized edit invalidated a tile beyond its conservative bound.");
        Equal(256, dirty[0].Left, "Localized edit selected the wrong viewport tile.");
        return Task.CompletedTask;
    }

    private static Task FlattenedViewportTextureSeam()
    {
        const int size = 64;
        var project = Fixture();
        var foreground = project.RequireRole(TerrainRole.Foreground);
        var stroke = new PaintStroke(StrokeId.New(), [new MapPoint(4, 4)],
            new ResolvedBrush(TextureHash, 1, 0.6, 0.9, 0.8, 0.2, 0, 31, 1),
            false, TerrainStrokeKind.Texture);
        project = project with { TerrainLayers = project.TerrainLayers.SetItem(1,
            foreground with { Strokes = [stroke] }) };
        var baseRaster = Solid(size, size, 22, 35, 51, 255);
        var whole = Render(project, size, size, baseRaster, null, null, 0, 0, size, size);
        Pixel(whole, size, 0, 0, 22, 35, 51, 255);
        var stitched = new byte[whole.Length];
        for (var tileY = 0; tileY < size; tileY += 32)
        for (var tileX = 0; tileX < size; tileX += 32)
        {
            var tile = Render(project, size, size, baseRaster, null, null,
                tileX, tileY, 32, 32);
            for (var row = 0; row < 32; row++)
                tile.AsSpan(row * 32 * 4, 32 * 4).CopyTo(
                    stitched.AsSpan(((tileY + row) * size + tileX) * 4));
        }
        True(whole.SequenceEqual(stitched), "Opaque flattened texture output changed at a tile seam.");
        True(whole[(32 * size + 32) * 4] != 22,
            "Foreground texture did not affect the centre pixel.");
        return Task.CompletedTask;
    }

    private static Task DependencyBoundsAccumulateAndRemainAsymmetric()
    {
        var output = new MapBounds(100, 100, 120, 120);
        var dependencies = ConnectedTerrainGraph.BackwardDependencyBounds(
            output, [20, 20], shadowOffsetX: 70, shadowOffsetY: -50, shadowRadius: 6);
        Equal(new MapBounds(24, 60, 160, 176), dependencies,
            "Sequential support or offset-shadow dependency bounds were not propagated conservatively.");
        return Task.CompletedTask;
    }

    private static Task CoastPolicyKeepsGeneratedEdgesUnstyled()
    {
        var policyType = typeof(ConnectedTerrainGraph).Assembly.GetType("Mapwright.Rendering.CoastStylePolicy")
            ?? throw new InvalidOperationException(
                "CoastStylePolicy is absent; the generated-edge branch has not been made explicit.");
        var selectedBranch = policyType.GetProperty("SelectedBranch", BindingFlags.Public | BindingFlags.Static)
            ?.GetValue(null)?.ToString();
        Equal("UnstyledGeneratedEdge", selectedBranch ?? "<missing>",
            "The coast decision must select the authorized unstyled generated-edge branch.");
        Equal(false, (bool?)policyType.GetProperty("DistanceFieldActive",
                BindingFlags.Public | BindingFlags.Static)?.GetValue(null) ?? true,
            "An unused distance field must be reported inactive, not passed.");

        var solid = Solid(8, 8, 220, 180, 100, 255);
        var openCoast = new float[64];
        var lakeCut = Enumerable.Repeat(1f, 64).ToArray();
        for (var y = 0; y < 8; y++)
        for (var x = 0; x < 8; x++)
            openCoast[y * 8 + x] = x < 4 ? 1f : 0f;
        for (var y = 3; y <= 4; y++)
        for (var x = 3; x <= 4; x++)
            lakeCut[y * 8 + x] = 0f;

        AssertOnlyBaseColors(Render(Fixture(), 8, 8, null, solid, openCoast, 0, 0, 8, 8),
            "open outer coast");
        AssertOnlyBaseColors(Render(Fixture(), 8, 8, null, solid, lakeCut, 0, 0, 8, 8),
            "lake-like cut");

        var soft = Enumerable.Repeat(0.5f, 64).ToArray();
        var softRendered = Render(Fixture(), 8, 8, null, solid, soft, 0, 0, 8, 8);
        Pixel(softRendered, 8, 3, 3, 220, 180, 100, 128, tolerance: 1);

        var riverProject = Fixture();
        var foreground = riverProject.RequireRole(TerrainRole.Foreground);
        riverProject = riverProject with
        {
            River = River.Create(RiverId.New(), foreground.Id,
                [new MapPoint(1, 4), new MapPoint(7, 4)], [2d, 2d], bankSoftness: 1)
        };
        var river = Render(riverProject, 8, 8, null, solid,
            Enumerable.Repeat(1f, 64).ToArray(), 0, 0, 8, 8);
        AssertOnlyBaseColors(river, "inland river bank and river mouth");

        var whole = Render(Fixture(), 8, 8, null, solid, openCoast, 0, 0, 8, 8);
        var stitched = new byte[whole.Length];
        for (var tileX = 0; tileX < 8; tileX += 4)
        {
            var tile = Render(Fixture(), 8, 8, null, solid, openCoast, tileX, 0, 4, 8);
            for (var row = 0; row < 8; row++)
                tile.AsSpan(row * 16, 16).CopyTo(stitched.AsSpan((row * 8 + tileX) * 4));
        }
        True(whole.SequenceEqual(stitched), "Unstyled coast fixture changed at a tile crossing.");
        return Task.CompletedTask;
    }

    private static Task ResourceLedgerEnforcesEveryCap()
    {
        var ledgerType = typeof(ConnectedTerrainGraph).Assembly.GetType("Mapwright.Rendering.RenderResourceLedger")
            ?? throw new InvalidOperationException(
                "RenderResourceLedger is absent; allocations are not admitted against measured limits.");
        const long gibibyte = 1024L * 1024L * 1024L;
        var ledger = Activator.CreateInstance(ledgerType,
            16L * gibibyte, 32L * gibibyte, 512L * 1024L * 1024L)
            ?? throw new InvalidOperationException("RenderResourceLedger could not be constructed.");
        var fits = ledgerType.GetMethod("Fits", BindingFlags.Public | BindingFlags.Instance)
            ?? throw new InvalidOperationException("RenderResourceLedger.Fits is absent.");
        var gpuCap = (long)(ledgerType.GetProperty("GpuBudgetBytes")?.GetValue(ledger) ?? -1L);
        var cpuCap = (long)(ledgerType.GetProperty("DecodedCpuBudgetBytes")?.GetValue(ledger) ?? -1L);
        var exportCap = (long)(ledgerType.GetProperty("ExportBufferBudgetBytes")?.GetValue(ledger) ?? -1L);
        var processCap = (long)(ledgerType.GetProperty("ProcessBudgetBytes")?.GetValue(ledger) ?? -1L);
        var historyCap = (long)(ledgerType.GetProperty("HistoryAccelerationBudgetBytes")?.GetValue(ledger) ?? -1L);
        Equal(3584L * 1024L * 1024L, gpuCap, "GPU cap did not subtract compositor headroom.");
        Equal(512L * 1024L * 1024L, cpuCap, "Decoded CPU cap changed.");
        Equal(512L * 1024L * 1024L, exportCap, "Export-buffer cap changed.");
        Equal(8L * gibibyte, processCap, "Process cap is not 25% of reported system RAM.");
        Equal(4L * gibibyte, historyCap, "History-acceleration cap changed.");

        bool Fits(long gpu, long cpu, long export, long process, long history) =>
            (bool)(fits.Invoke(ledger, [gpu, cpu, export, process, history]) ?? false);
        True(Fits(gpuCap, cpuCap, exportCap, processCap, historyCap),
            "Exact resource caps were rejected.");
        True(!Fits(gpuCap + 1, cpuCap, exportCap, processCap, historyCap), "GPU cap + 1 was admitted.");
        True(!Fits(gpuCap, cpuCap + 1, exportCap, processCap, historyCap), "CPU cap + 1 was admitted.");
        True(!Fits(gpuCap, cpuCap, exportCap + 1, processCap, historyCap), "Band/export cap + 1 was admitted.");
        True(!Fits(gpuCap, cpuCap, exportCap, processCap + 1, historyCap), "Process cap + 1 was admitted.");
        True(!Fits(gpuCap, cpuCap, exportCap, processCap, historyCap + 1), "History cap + 1 was admitted.");

        var fitBand = ledgerType.GetMethod("FitBandHeight", BindingFlags.Public | BindingFlags.Instance)
            ?? throw new InvalidOperationException("RenderResourceLedger.FitBandHeight is absent.");
        var exactRows = (int)(fitBand.Invoke(ledger, [16384, 8190, 1, 4, 1]) ?? -1);
        Equal(8190, exactRows, "Exact band plus two halo rows should fit 512 MiB.");
        var reducedRows = (int)(fitBand.Invoke(ledger, [16384, 8191, 1, 4, 1]) ?? -1);
        Equal(8190, reducedRows, "One row over the band/halo cap was not reduced before allocation.");
        return Task.CompletedTask;
    }

    private static void AssertOnlyBaseColors(byte[] rgba, string fixture)
    {
        for (var offset = 0; offset < rgba.Length; offset += 4)
        {
            var alpha = rgba[offset + 3];
            if (alpha == 0) continue;
            if (rgba[offset] != 220 || rgba[offset + 1] != 180 || rgba[offset + 2] != 100)
                throw new InvalidOperationException(
                    $"{fixture} introduced decorative bank colour at pixel {offset / 4}.");
        }
    }

    private static ConnectedTerrainFrame RenderFrame(
        MapProject project,
        int canvasWidth,
        int canvasHeight,
        byte[]? background,
        byte[]? foreground,
        float[]? coverage,
        int regionLeft,
        int regionTop,
        int regionWidth,
        int regionHeight)
    {
        var parameterTypes = new[]
        {
            typeof(MapProject), typeof(int), typeof(int), typeof(byte[]), typeof(byte[]),
            typeof(float[]), typeof(int), typeof(int), typeof(int), typeof(int)
        };
        var method = typeof(ConnectedTerrainGraph).GetMethod(
            "RenderReference", BindingFlags.Public | BindingFlags.Static, null, parameterTypes, null)
            ?? throw new InvalidOperationException(
                "ConnectedTerrainGraph.RenderReference is absent; the shared CPU reference evaluator has not been implemented.");
        try
        {
            return (ConnectedTerrainFrame)(method.Invoke(null,
                [project, canvasWidth, canvasHeight, background, foreground, coverage,
                    regionLeft, regionTop, regionWidth, regionHeight])
                ?? throw new InvalidOperationException("RenderReference returned null."));
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            throw exception.InnerException;
        }
    }

    private static byte[] Render(
        MapProject project,
        int canvasWidth,
        int canvasHeight,
        byte[]? background,
        byte[]? foreground,
        float[]? coverage,
        int regionLeft,
        int regionTop,
        int regionWidth,
        int regionHeight) => RenderFrame(project, canvasWidth, canvasHeight, background, foreground,
            coverage, regionLeft, regionTop, regionWidth, regionHeight).Rgba;

    private static MapProject Fixture()
    {
        var background = new TerrainLayer(LayerId.New(), "Background", true, false, 1,
            new CoastlineStyle(0), ImmutableArray<PaintStroke>.Empty, TerrainRole.Background);
        var foreground = new TerrainLayer(LayerId.New(), "Foreground", true, false, 1,
            new CoastlineStyle(0), ImmutableArray<PaintStroke>.Empty, TerrainRole.Foreground);
        return new MapProject(ProjectId.New(), MapId.New(), "Render fixture", 8, 8, 7,
            [background, foreground]);
    }

    private static byte[] Solid(int width, int height, byte red, byte green, byte blue, byte alpha)
    {
        var result = new byte[checked(width * height * 4)];
        for (var offset = 0; offset < result.Length; offset += 4)
        {
            result[offset] = red;
            result[offset + 1] = green;
            result[offset + 2] = blue;
            result[offset + 3] = alpha;
        }
        return result;
    }

    private static void Pixel(byte[] rgba, int width, int x, int y,
        byte red, byte green, byte blue, byte alpha, int tolerance = 0)
    {
        var offset = (y * width + x) * 4;
        Channel(red, rgba[offset], tolerance, "red");
        Channel(green, rgba[offset + 1], tolerance, "green");
        Channel(blue, rgba[offset + 2], tolerance, "blue");
        Channel(alpha, rgba[offset + 3], tolerance, "alpha");
    }

    private static void Channel(byte expected, byte actual, int tolerance, string channel)
    {
        if (Math.Abs(expected - actual) > tolerance)
            throw new InvalidOperationException(
                $"Expected {channel}={expected}±{tolerance}; got {actual}.");
    }

    private static void Equal<T>(T expected, T actual, string message) where T : notnull
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{message} Expected {expected}; got {actual}.");
    }

    private static void True(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

using System.Collections.Immutable;
using System.Text.Json;
using Mapwright.Application;
using Mapwright.Domain;
using Mapwright.Infrastructure;

public sealed class BrushContractCases : IContractCaseProvider
{
    public void Register(ContractRegistry registry)
    {
        registry.Add("brush roles reject hidden paint targets", HiddenPaintTargetIsRejected);
        registry.Add("brush roles expose fixed terrain state commands", FixedTerrainStateCommandsWork);
        registry.Add("brush roles expose solo visibility truth table", SoloVisibilityTruthTableWorks);
        registry.Add("brush roles report no-op state changes", NoOpStateChangesAreExplicit);
        registry.Add("brush roles reject prohibited role and coverage mutations", ProhibitedRoleAndCoverageMutationsAreRejected);
        registry.Add("brush roles reopen snapshots written before solo state", LegacySnapshotDefaultsSoloOff);
        registry.Add("texture recipes persist every resolved replay field", ResolvedTextureRecipeContractExists);
        registry.Add("texture strokes resolve taper in document space", DocumentSpaceTextureStrokeContractExists);
        registry.Add("texture recipes expose deterministic seeded dabs", DeterministicTextureDabContractExists);
        registry.Add("texture strokes reopen with identical resolved recipes", ResolvedTextureStrokeReopens);
        registry.Add("96 map-unit texture geometry survives independent editing sampling", MapUnitTextureGeometry);
    }

    private static Task HiddenPaintTargetIsRejected()
    {
        var project = Fixture(backgroundVisible: false);
        var background = project.RequireRole(TerrainRole.Background);
        var command = new AddTextureStroke(
            CommandId.New(),
            project.Revision,
            background.Id,
            TextureStroke(new MapPoint(32, 48)));

        var blocked = Capture<PaintTargetBlockedException>(() => command.Apply(project),
            "A hidden Background accepted a texture stroke instead of returning a blocked-target reason.");
        Equal(TerrainRole.Background, blocked.Role);
        Equal(PaintBlockReason.Hidden, blocked.Reason);
        Equal("Show layer", blocked.SuggestedAction);
        return Task.CompletedTask;
    }

    private static Task FixedTerrainStateCommandsWork()
    {
        var initial = Fixture();
        var backgroundId = initial.RequireRole(TerrainRole.Background).Id;
        var foregroundId = initial.RequireRole(TerrainRole.Foreground).Id;

        var renamed = new RenameTerrainLayer(CommandId.New(), initial.Revision,
            TerrainRole.Background, "Imported parchment").Apply(initial).Project;
        Equal("Imported parchment", renamed.RequireRole(TerrainRole.Background).Name);

        var hidden = new SetTerrainVisibility(CommandId.New(), renamed.Revision,
            TerrainRole.Foreground, false).Apply(renamed).Project;
        True(!hidden.RequireRole(TerrainRole.Foreground).Visible, "Visibility command did not hide Foreground.");

        var shown = new SetTerrainVisibility(CommandId.New(), hidden.Revision,
            TerrainRole.Foreground, true).Apply(hidden).Project;
        var locked = new SetTerrainLock(CommandId.New(), shown.Revision,
            TerrainRole.Background, true).Apply(shown).Project;
        var blocked = Capture<PaintTargetBlockedException>(() =>
            new AddTextureStroke(CommandId.New(), locked.Revision, backgroundId,
                TextureStroke(new MapPoint(12, 16))).Apply(locked), "Locked Background accepted paint.");
        Equal(PaintBlockReason.Locked, blocked.Reason);
        Equal("Unlock", blocked.SuggestedAction);

        var unlocked = new SetTerrainLock(CommandId.New(), locked.Revision,
            TerrainRole.Background, false).Apply(locked).Project;
        var faded = new SetTerrainOpacity(CommandId.New(), unlocked.Revision,
            TerrainRole.Foreground, 0.35).Apply(unlocked).Project;
        Equal(0.35, faded.RequireRole(TerrainRole.Foreground).Opacity);
        Equal(0, faded.RequireRole(TerrainRole.Foreground).Strokes.Length);
        Equal(backgroundId, faded.RequireRole(TerrainRole.Background).Id);
        Equal(foregroundId, faded.RequireRole(TerrainRole.Foreground).Id);

        Throws<ArgumentOutOfRangeException>(() =>
            new SetTerrainOpacity(CommandId.New(), faded.Revision, TerrainRole.Foreground, double.NaN)
                .Apply(faded), "NaN opacity entered history.");
        Throws<ArgumentOutOfRangeException>(() =>
            new SetTerrainOpacity(CommandId.New(), faded.Revision, TerrainRole.Foreground, 1.01)
                .Apply(faded), "Out-of-range opacity entered history.");
        Throws<RevisionConflictException>(() =>
            new SetTerrainLock(CommandId.New(), faded.Revision - 1, TerrainRole.Foreground, true)
                .Apply(faded), "A stale terrain state command overwrote the current revision.");
        return Task.CompletedTask;
    }

    private static Task SoloVisibilityTruthTableWorks()
    {
        AssertVisibility(Fixture(), background: true, foreground: true);
        AssertVisibility(WithSolo(backgroundSolo: true, foregroundSolo: false), true, false);
        AssertVisibility(WithSolo(backgroundSolo: false, foregroundSolo: true), false, true);
        AssertVisibility(WithSolo(backgroundSolo: true, foregroundSolo: true), true, true);

        var hiddenSolo = WithSolo(backgroundSolo: true, foregroundSolo: false,
            backgroundVisible: false);
        AssertVisibility(hiddenSolo, false, false);
        var lockedSolo = hiddenSolo with
        {
            TerrainLayers = hiddenSolo.TerrainLayers.SetItem(0,
                hiddenSolo.TerrainLayers[0] with { Visible = true, Locked = true })
        };
        AssertVisibility(lockedSolo, true, false);
        return Task.CompletedTask;
    }

    private static async Task NoOpStateChangesAreExplicit()
    {
        var project = Fixture();
        var command = new SetTerrainOpacity(CommandId.New(), project.Revision,
            TerrainRole.Foreground, project.RequireRole(TerrainRole.Foreground).Opacity);
        var change = command.Apply(project);
        True(change.IsNoOp, "Repeated layer state was not reported as an explicit no-op.");
        Equal(project.Revision, change.Project.Revision);

        var events = new List<string>();
        await using var session = new EditSession(project, new RecordingRepository(events),
            new RecordingRenderer(events));
        var acknowledgement = await session.ExecuteAsync(command);
        Equal(project.Revision, acknowledgement.Revision);
        Equal(0, events.Count);
    }

    private static Task ProhibitedRoleAndCoverageMutationsAreRejected()
    {
        var project = Fixture();
        var background = project.RequireRole(TerrainRole.Background);
        var foreground = project.RequireRole(TerrainRole.Foreground);
        Throws<InvalidDataException>(() => (project with
        {
            TerrainLayers = [foreground, background]
        }).ValidateConnectedTerrain(), "Terrain roles were reorderable.");
        Throws<InvalidDataException>(() => (project with
        {
            TerrainLayers = [background]
        }).ValidateConnectedTerrain(), "A terrain role was removable.");
        Throws<InvalidDataException>(() => (project with
        {
            TerrainLayers = [background, background]
        }).ValidateConnectedTerrain(), "A terrain role was duplicable.");
        Throws<InvalidDataException>(() => (project with
        {
            TerrainLayers = project.TerrainLayers.SetItem(1,
                foreground with { Role = (TerrainRole)2 })
        }).ValidateConnectedTerrain(), "A third terrain role was accepted.");

        var coverage = new PaintStroke(StrokeId.New(), [new MapPoint(20, 20)],
            Brush(), true, TerrainStrokeKind.Coverage);
        Throws<InvalidDataException>(() =>
            new AddPaintStroke(CommandId.New(), project.Revision, background.Id, coverage).Apply(project),
            "Background accepted coverage.");

        var textureChange = new AddTextureStroke(CommandId.New(), project.Revision, foreground.Id,
            TextureStroke(new MapPoint(30, 30))).Apply(project);
        True(!textureChange.Invalidation.RebuildCoverage,
            "Texture painting invalidated or changed coverage.");
        return Task.CompletedTask;
    }

    private static Task LegacySnapshotDefaultsSoloOff()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var json = JsonSerializer.Serialize(Fixture(), options)
            .Replace(",\"solo\":false", string.Empty, StringComparison.Ordinal);
        var reopened = JsonSerializer.Deserialize<MapProject>(json, options)
            ?? throw new InvalidOperationException("Legacy project did not deserialize.");
        reopened.ValidateConnectedTerrain();
        True(reopened.TerrainLayers.All(layer => !layer.Solo),
            "A pre-solo snapshot did not migrate to neither-solo semantics.");
        True(reopened.TerrainLayers.All(layer => layer.ResolvedTextureStrokes.IsEmpty),
            "A snapshot written before resolved texture strokes did not migrate to an empty collection.");
        return Task.CompletedTask;
    }

    private static Task ResolvedTextureRecipeContractExists()
    {
        var inventory = TexturePresetCatalog.Inventory;
        Equal(7, inventory.Length);
        True(inventory.Count(preset => preset.Tip == TextureTipKind.Round) >= 3,
            "Hard, soft, and tapered round families are required.");
        True(inventory.Any(preset => preset.Tip == TextureTipKind.Square), "Square/pencil is required.");
        True(inventory.Count(preset => preset.Tip == TextureTipKind.Irregular) >= 2,
            "Multiple irregular/textured examples are required.");
        True(inventory.Any(preset => preset.Tip == TextureTipKind.Edged), "Edged texture is required.");
        True(inventory.Any(preset => preset.Taper == TextureTaperKind.SmoothBothEnds),
            "A tapered preset is required.");

        var recipe = TexturePresetCatalog.Resolve("edged-texture", TextureHash, 73, diameter: 100) with
        {
            Opacity = 0.75,
            Flow = 0.65,
            TextureScale = 2,
            RotationDegrees = -35,
            Jitter = 0.25
        };
        recipe.Validate();
        Equal(50d, recipe.Radius);
        Equal(100d, recipe.Diameter);
        Equal(TextureTipKind.Edged, recipe.Tip);
        Equal(TextureHash, recipe.TextureSha256);
        Equal(1, recipe.TipVersion);
        Equal(1, recipe.TaperVersion);
        Equal(1, recipe.RandomVersion);
        Equal(1, recipe.AlgorithmVersion);

        Throws<ArgumentOutOfRangeException>(() => (recipe with { Radius = 0 }).Validate(),
            "Zero diameter entered history.");
        Throws<ArgumentOutOfRangeException>(() => (recipe with { Radius = 1024.5 }).Validate(),
            "Oversized diameter entered history.");
        Throws<ArgumentOutOfRangeException>(() => (recipe with { Spacing = 0.049 }).Validate(),
            "Unbounded spacing entered history.");
        Throws<ArgumentOutOfRangeException>(() => (recipe with { TextureScale = 16.01 }).Validate(),
            "Unbounded texture scale entered history.");
        Throws<ArgumentOutOfRangeException>(() => (recipe with { RotationDegrees = 181 }).Validate(),
            "Unbounded rotation entered history.");
        Throws<ArgumentOutOfRangeException>(() => (recipe with { Jitter = double.NaN }).Validate(),
            "NaN jitter entered history.");
        Throws<ArgumentException>(() => (recipe with { TextureSha256 = "missing" }).Validate(),
            "Missing texture identity entered history.");
        Throws<InvalidDataException>(() => (recipe with { AlgorithmVersion = 2 }).Validate(),
            "Unknown brush algorithm entered history.");
        return Task.CompletedTask;
    }

    private static Task DocumentSpaceTextureStrokeContractExists()
    {
        var taperedRecipe = TexturePresetCatalog.Resolve("tapered-round", TextureHash, 19, diameter: 80);
        var positions = ImmutableArray.Create(
            new MapPoint(250, 80),
            new MapPoint(300, 80),
            new MapPoint(350, 80),
            new MapPoint(400, 80),
            new MapPoint(450, 80),
            new MapPoint(500, 80));
        var anchor = new MapPoint(17, 23);
        var tapered = TexturePaintStroke.Create(StrokeId.New(), positions, taperedRecipe, anchor);
        Equal(TextureBrushLimits.MinimumTaperScale, tapered.Samples[0].RadiusScale);
        Equal(TextureBrushLimits.MinimumTaperScale, tapered.Samples[^1].RadiusScale);
        Equal(1d, tapered.Samples[2].RadiusScale);
        Equal(1d, tapered.Samples[3].RadiusScale);
        Equal(anchor, tapered.TextureAnchor);
        Equal(new MapPoint(250, 80), tapered.Samples[0].Position);
        True(tapered.Samples[0].Position.X < 256 && tapered.Samples[1].Position.X > 256,
            "Fixture must cross the 256-pixel tile edge in document space.");

        var plainRecipe = TexturePresetCatalog.Resolve("hard-round", TextureHash, 19, diameter: 80);
        var plain = TexturePaintStroke.Create(StrokeId.New(), positions, plainRecipe, anchor);
        True(plain.Samples.All(sample => sample.RadiusScale == 1),
            "A non-tapered preset changed sample radii.");

        var project = Fixture();
        var backgroundChange = new AddResolvedTextureStroke(CommandId.New(), project.Revision,
            TerrainRole.Background, plain).Apply(project);
        var foregroundStroke = plain with { Id = StrokeId.New() };
        var foregroundChange = new AddResolvedTextureStroke(CommandId.New(), backgroundChange.Project.Revision,
            TerrainRole.Foreground, foregroundStroke).Apply(backgroundChange.Project);
        Equal(plain.Recipe,
            foregroundChange.Project.RequireRole(TerrainRole.Background).ResolvedTextureStrokes.Single().Recipe);
        Equal(plain.Recipe,
            foregroundChange.Project.RequireRole(TerrainRole.Foreground).ResolvedTextureStrokes.Single().Recipe);
        Equal(anchor, foregroundChange.Project.RequireRole(TerrainRole.Foreground)
            .ResolvedTextureStrokes.Single().TextureAnchor);
        True(!backgroundChange.Invalidation.RebuildCoverage && !foregroundChange.Invalidation.RebuildCoverage,
            "Texture target changes affected land coverage.");
        True(foregroundChange.Project.TerrainLayers.All(layer => layer.Strokes.IsEmpty),
            "Resolved texture painting changed the coverage/legacy stroke channel.");
        return Task.CompletedTask;
    }

    private static Task DeterministicTextureDabContractExists()
    {
        var recipe = TexturePresetCatalog.Resolve("grain-irregular", TextureHash, 1234) with { Jitter = 0.8 };
        recipe.Validate();
        var first = recipe.ResolveDab(12);
        var replay = recipe.ResolveDab(12);
        Equal(first, replay);
        Equal(recipe.RotationDegrees, first.RotationDegrees);
        var differentSeed = (recipe with { Seed = 1235 }).ResolveDab(12);
        True(first.Offset != differentSeed.Offset, "Changing the persisted seed did not change the dab.");

        var json = JsonSerializer.Serialize(recipe, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var reopened = JsonSerializer.Deserialize<ResolvedTextureBrush>(json,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException("Resolved recipe did not deserialize.");
        Equal(first, reopened.ResolveDab(12));
        return Task.CompletedTask;
    }

    private static async Task ResolvedTextureStrokeReopens()
    {
        var root = Path.Combine(Path.GetTempPath(), $"mapwright-brush-{Guid.NewGuid():N}");
        try
        {
            var project = Fixture();
            var recipe = TexturePresetCatalog.Resolve("chalk-irregular", TextureHash, -912, diameter: 144)
                with { TextureScale = 0.5, RotationDegrees = 47, Jitter = 0.4 };
            recipe.Validate();
            var stroke = TexturePaintStroke.Create(StrokeId.New(),
                [new MapPoint(252, 200), new MapPoint(260, 204), new MapPoint(300, 220)],
                recipe, new MapPoint(0, 0));
            var command = new AddResolvedTextureStroke(CommandId.New(), project.Revision,
                TerrainRole.Foreground, stroke);
            var change = command.Apply(project);
            var repository = new SqliteProjectRepository(root, new FixedClock());
            await repository.CreateAsync(project);
            await repository.CommitAsync(project, command, change, CancellationToken.None);

            var reopened = await repository.LoadAsync(project.ProjectId, CancellationToken.None);
            var persisted = reopened.RequireRole(TerrainRole.Foreground).ResolvedTextureStrokes.Single();
            Equal(stroke.Recipe, persisted.Recipe);
            Equal(stroke.TextureAnchor, persisted.TextureAnchor);
            Equal(stroke.Samples.Length, persisted.Samples.Length);
            for (var index = 0; index < stroke.Samples.Length; index++)
                Equal(stroke.Samples[index], persisted.Samples[index]);
            Equal(stroke.Recipe.ResolveDab(1), persisted.Recipe.ResolveDab(1));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static MapProject Fixture(bool backgroundVisible = true)
    {
        var background = new TerrainLayer(LayerId.New(), "Background", backgroundVisible, false, 1,
            new CoastlineStyle(0), ImmutableArray<PaintStroke>.Empty, TerrainRole.Background);
        var foreground = new TerrainLayer(LayerId.New(), "Foreground", true, false, 1,
            new CoastlineStyle(0), ImmutableArray<PaintStroke>.Empty, TerrainRole.Foreground);
        return new MapProject(ProjectId.New(), MapId.New(), "Brush fixture", 512, 512, 3,
            [background, foreground]);
    }

    private static Task MapUnitTextureGeometry()
    {
        MapBounds? expectedBounds = null;
        foreach (var sampling in new[] { 1024, 4096 })
        {
            var layers = Fixture().TerrainLayers;
            var project = MapProject.CreateNormalizedFromGrid(ProjectId.New(), MapId.New(),
                "96-unit brush", 40, 30, sampling, layers);
            var center = new MapPoint(250, 375);
            var recipe = TexturePresetCatalog.Resolve("hard-round", TextureHash, 901, diameter: 96);
            var stroke = TexturePaintStroke.Create(StrokeId.New(), [center], recipe, center);
            var changed = new AddResolvedTextureStroke(CommandId.New(), project.Revision,
                TerrainRole.Foreground, stroke).Apply(project).Project;
            var saved = changed.RequireRole(TerrainRole.Foreground).ResolvedTextureStrokes.Single();
            Equal(96d, saved.Recipe.Diameter);
            Equal(center, saved.Samples[0].Position);
            Equal(center, saved.TextureAnchor);
            if (expectedBounds is { } bounds) Equal(bounds, saved.Bounds);
            expectedBounds = saved.Bounds;
            var transform = new DocumentRasterTransform(project.Width, project.Height,
                project.EditingPixelWidth, project.EditingPixelHeight);
            var outputCenter = transform.DocumentToOutput(center);
            var outputRadiusX = transform.RoundDocumentLengthToOutput(saved.Recipe.Radius, true);
            True(Math.Abs(outputCenter.X / project.EditingPixelWidth - 0.25) < 1e-12 &&
                 Math.Abs(outputCenter.Y / project.EditingPixelHeight - 0.5) < 1e-12 &&
                 Math.Abs((double)outputRadiusX / project.EditingPixelWidth - 0.048) < 0.001,
                "Editing sampling changed the relative location or size of a 96-unit brush.");
        }
        return Task.CompletedTask;
    }

    private static PaintStroke TextureStroke(MapPoint point) => new(
        StrokeId.New(),
        [point],
        Brush(),
        false,
        TerrainStrokeKind.Texture);

    private static ResolvedBrush Brush() => new(
        new string('a', 64), 12, 0.8, 0.7, 0.5, 0.2, 0, 42, 1);

    private const string TextureHash = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    private static MapProject WithSolo(
        bool backgroundSolo,
        bool foregroundSolo,
        bool backgroundVisible = true,
        bool foregroundVisible = true)
    {
        var project = Fixture(backgroundVisible);
        return project with
        {
            TerrainLayers =
            [
                project.TerrainLayers[0] with { Solo = backgroundSolo },
                project.TerrainLayers[1] with { Solo = foregroundSolo, Visible = foregroundVisible }
            ]
        };
    }

    private static void AssertVisibility(MapProject project, bool background, bool foreground)
    {
        Equal(background, project.IsTerrainEffectivelyVisible(TerrainRole.Background));
        Equal(foreground, project.IsTerrainEffectivelyVisible(TerrainRole.Foreground));
    }

    private static void True(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Equal<T>(T expected, T actual) where T : notnull
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected {expected}; got {actual}.");
    }

    private static void Throws<T>(Action action, string message) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException(message);
    }

    private static T Capture<T>(Action action, string message) where T : Exception
    {
        try { action(); }
        catch (T exception) { return exception; }
        throw new InvalidOperationException(message);
    }
}

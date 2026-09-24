using System.Collections.Immutable;
using Mapwright.Domain;
using Mapwright.Infrastructure;

public sealed class MaskRiverContractCases : IContractCaseProvider
{
    public void Register(ContractRegistry registry)
    {
        registry.Add("land strokes expose resolved footprint and composition contracts",
            ResolvedLandFootprintsAreDistinct);
        registry.Add("land strokes target only Foreground coverage",
            LandStrokesTargetOnlyForegroundCoverage);
        registry.Add("land coverage boundaries and raster ties are explicit",
            LandCoverageBoundariesAndRasterTiesAreExplicit);
        registry.Add("land strokes reopen with identical resolved geometry",
            LandStrokesReopenWithIdenticalGeometry);
        registry.Add("rivers expose editable width softness and modifier contracts",
            EditableRiverModifierContractWorks);
        registry.Add("river point edits union old and new invalidation bounds",
            RiverPointEditsUnionOldAndNewBounds);
        registry.Add("river subtraction remains ordered after later Land painting",
            RiverSubtractionRemainsOrderedAfterLandPainting);
        registry.Add("river dimensions and raster rounding have explicit boundaries",
            RiverDimensionsAndRasterRoundingHaveExplicitBoundaries);
        registry.Add("96 map-unit Land and river support survives sampling and point edits",
            MapUnitLandAndRiverSupport);
    }

    private static Task ResolvedLandFootprintsAreDistinct()
    {
        var sharp = ResolvedLandBrush.FromDiameter(
            LandShape.EdgedPolygon, 100, 0.45, 0, 0, seed: 1847);
        var smooth = sharp with { CornerSmoothing = 1 };
        var round = ResolvedLandBrush.FromDiameter(
            LandShape.RoundSoft, 100, 0, 0, 0.5, seed: 1847);
        sharp.Validate();
        smooth.Validate();
        round.Validate();

        var geometryDiffers = false;
        for (var degree = 0; degree < 360 && !geometryDiffers; degree++)
        {
            var angle = degree * Math.PI / 180;
            for (var radius = 44d; radius <= 50; radius += 0.25)
            {
                var point = new MapPoint(Math.Cos(angle) * radius, Math.Sin(angle) * radius);
                var sharpCoverage = sharp.CoverageAtOffset(point);
                var smoothCoverage = smooth.CoverageAtOffset(point);
                True(sharpCoverage is 0 or 1 && smoothCoverage is 0 or 1,
                    "Edged Smooth changed alpha softness instead of firm corner geometry.");
                geometryDiffers |= sharpCoverage != smoothCoverage;
            }
        }
        True(geometryDiffers, "Edged Smooth did not round the firm polygon footprint.");

        Equal(0.5, round.CoverageAtOffset(new MapPoint(37.5, 0)), 1e-12);
        Equal(1d, round.CoverageAtOffset(new MapPoint(25, 0)));
        Equal(0d, round.CoverageAtOffset(new MapPoint(50.01, 0)));
        Equal(sharp.CoverageAtOffset(new MapPoint(47, 3)),
            sharp.CoverageAtOffset(new MapPoint(47, 3)));

        Throws<InvalidDataException>(() => (sharp with { Softness = 0.1 }).Validate(),
            "Edged polygon accepted alpha Softness.");
        Throws<InvalidDataException>(() => (round with { Roughness = 0.1 }).Validate(),
            "Round soft accepted Edged Roughness.");
        Throws<ArgumentOutOfRangeException>(() => (round with { Radius = 0 }).Validate(),
            "Zero Land diameter entered history.");
        Throws<ArgumentOutOfRangeException>(() => (round with { Radius = 2048.5 }).Validate(),
            "Oversized Land diameter entered history.");
        return Task.CompletedTask;
    }

    private static Task LandStrokesTargetOnlyForegroundCoverage()
    {
        var project = Fixture();
        var texture = TexturePaintStroke.Create(StrokeId.New(), [new MapPoint(250, 128)],
            TexturePresetCatalog.Resolve("hard-round", TextureHash, seed: 9), new MapPoint(0, 0));
        var textured = new AddResolvedTextureStroke(CommandId.New(), project.Revision,
            TerrainRole.Foreground, texture).Apply(project).Project;
        var originalOpacity = textured.RequireRole(TerrainRole.Foreground).Opacity;

        var addStroke = Stroke(LandOperation.Add,
            ResolvedLandBrush.FromDiameter(LandShape.EdgedPolygon, 64, 0.35, 0.4, 0, seed: 77),
            new MapPoint(250, 256), new MapPoint(270, 256));
        var added = new AddLandStroke(CommandId.New(), textured.Revision,
            TerrainRole.Foreground, addStroke).Apply(textured);
        Equal(1, added.Project.RequireRole(TerrainRole.Foreground).ResolvedLandStrokes.Length);
        Equal(0, added.Project.RequireRole(TerrainRole.Background).ResolvedLandStrokes.Length);
        Equal(1, added.Project.RequireRole(TerrainRole.Foreground).ResolvedTextureStrokes.Length);
        Equal(originalOpacity, added.Project.RequireRole(TerrainRole.Foreground).Opacity);
        True(added.Invalidation.RebuildCoverage && added.Invalidation.RebuildDistance &&
             added.Invalidation.RebuildComposite,
            "Land painting did not invalidate the complete coverage dependency chain.");
        True(added.Invalidation.Bounds.Left <= 218 && added.Invalidation.Bounds.Right >= 302,
            "Land invalidation did not conservatively include the swept footprint.");

        var subtractStroke = Stroke(LandOperation.Subtract,
            ResolvedLandBrush.FromDiameter(LandShape.RoundSoft, 32, 0, 0, 0, seed: 31),
            new MapPoint(260, 256));
        var subtracted = new AddLandStroke(CommandId.New(), added.Project.Revision,
            TerrainRole.Foreground, subtractStroke).Apply(added.Project).Project;
        Equal(0d, subtracted.EvaluateForegroundCoverage(new MapPoint(260, 256)));
        Equal(1d, subtracted.EvaluateForegroundCoverage(new MapPoint(230, 256)));

        Throws<InvalidDataException>(() =>
            new AddLandStroke(CommandId.New(), project.Revision, TerrainRole.Background, addStroke)
                .Apply(project), "Background accepted a Land stroke.");
        return Task.CompletedTask;
    }

    private static Task LandCoverageBoundariesAndRasterTiesAreExplicit()
    {
        Equal(0d, CoverageMath.Apply(0, 0, LandOperation.Add));
        Equal(0.5, CoverageMath.Apply(0, 0.5, LandOperation.Add));
        Equal(1d, CoverageMath.Apply(0, 1, LandOperation.Add));
        Equal(0.5, CoverageMath.Apply(1, 0.5, LandOperation.Subtract));
        Equal(0d, CoverageMath.Apply(1, 1, LandOperation.Subtract));
        Throws<ArgumentOutOfRangeException>(() =>
            CoverageMath.Apply(Math.BitDecrement(0d), 0, LandOperation.Add),
            "Coverage one step below zero was accepted.");
        Throws<ArgumentOutOfRangeException>(() =>
            CoverageMath.Apply(0, Math.BitIncrement(1d), LandOperation.Add),
            "Coverage one step above one was accepted.");

        True(DocumentRasterTransform.IsLand(0.5), "The declared 0.5 contour tie was not land.");
        True(!DocumentRasterTransform.IsLand(Math.BitDecrement(0.5)),
            "Coverage below the 0.5 contour tie was classified as land.");
        var transform = new DocumentRasterTransform(512, 512, 1024, 1024);
        Equal(new MapPoint(127.75, 64.25), transform.OutputPixelCenterToDocument(255, 128));
        Equal(0, transform.RoundDocumentLengthToOutput(0.25, horizontal: true));
        Equal(2, transform.RoundDocumentLengthToOutput(0.75, horizontal: true));

        var crossing = new AddLandStroke(CommandId.New(), Fixture().Revision,
            TerrainRole.Foreground,
            Stroke(LandOperation.Add,
                ResolvedLandBrush.FromDiameter(LandShape.RoundSoft, 24, 0, 0, 0.5, seed: 4),
                new MapPoint(250, 300), new MapPoint(262, 300))).Apply(FixtureWithRevision(3)).Project;
        var left = crossing.EvaluateForegroundCoverage(new MapPoint(255.5, 300));
        var right = crossing.EvaluateForegroundCoverage(new MapPoint(256.5, 300));
        Equal(left, right, 1e-12);
        True(left > 0, "A swept Land stroke opened a seam at the 256-pixel tile boundary.");
        return Task.CompletedTask;
    }

    private static async Task LandStrokesReopenWithIdenticalGeometry()
    {
        var root = Path.Combine(Path.GetTempPath(), $"mapwright-land-{Guid.NewGuid():N}");
        try
        {
            var project = Fixture();
            var stroke = Stroke(LandOperation.Add,
                ResolvedLandBrush.FromDiameter(LandShape.EdgedPolygon, 91, 0.72, 0.38, 0, seed: -991),
                new MapPoint(252, 200), new MapPoint(260, 204), new MapPoint(300, 220));
            var command = new AddLandStroke(CommandId.New(), project.Revision,
                TerrainRole.Foreground, stroke);
            var change = command.Apply(project);
            var repository = new SqliteProjectRepository(root, new FixedClock());
            await repository.CreateAsync(project);
            await repository.CommitAsync(project, command, change, CancellationToken.None);

            var reopened = await repository.LoadAsync(project.ProjectId, CancellationToken.None);
            var persisted = reopened.RequireRole(TerrainRole.Foreground).ResolvedLandStrokes.Single();
            Equal(stroke.Id, persisted.Id);
            Equal(stroke.Recipe, persisted.Recipe);
            Equal(stroke.Operation, persisted.Operation);
            Equal(stroke.Samples.Length, persisted.Samples.Length);
            for (var index = 0; index < stroke.Samples.Length; index++)
                Equal(stroke.Samples[index], persisted.Samples[index]);
            Equal(change.Project.EvaluateForegroundCoverage(new MapPoint(260, 204)),
                reopened.EvaluateForegroundCoverage(new MapPoint(260, 204)), 1e-12);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static Task EditableRiverModifierContractWorks()
    {
        var project = AddBroadLand(Fixture());
        var foreground = project.RequireRole(TerrainRole.Foreground);
        var river = River.Create(RiverId.New(), foreground.Id,
            [new MapPoint(220, 256), new MapPoint(256, 256), new MapPoint(292, 256)],
            [20, 40, 24], bankSoftness: 0.5);
        var created = new CreateRiver(CommandId.New(), project.Revision, river).Apply(project);
        Equal(river.Id, created.Project.River!.Id);
        Equal(2, created.Project.River.GeometrySegments().Length);
        Equal(40d, created.Project.River.ResolvedWidthProfile.Widths[1]);
        Equal(0d, created.Project.EvaluateForegroundCoverage(new MapPoint(256, 256)));

        var widened = new SetRiverPointWidth(CommandId.New(), created.Project.Revision, 1, 80)
            .Apply(created.Project);
        Equal(80d, widened.Project.River!.ResolvedWidthProfile.Widths[1]);
        True(widened.Invalidation.Bounds.Top <= 196 && widened.Invalidation.Bounds.Bottom >= 316,
            "Width drag did not invalidate the expanded soft-bank footprint.");

        var uniform = new SetAllRiverWidths(CommandId.New(), widened.Project.Revision, 32)
            .Apply(widened.Project);
        True(uniform.Project.River!.ResolvedWidthProfile.Widths.All(width => width == 32),
            "Apply width to all did not replace the complete width profile.");
        var soft = new SetRiverBankSoftness(CommandId.New(), uniform.Project.Revision, 1)
            .Apply(uniform.Project);
        Equal(1d, soft.Project.River!.BankSoftness);
        var disabled = new SetRiverEnabled(CommandId.New(), soft.Project.Revision, false)
            .Apply(soft.Project);
        True(!disabled.Project.River!.Enabled, "River enable command did not disable the modifier.");
        Equal(1d, disabled.Project.EvaluateForegroundCoverage(new MapPoint(256, 256)));
        return Task.CompletedTask;
    }

    private static Task RiverPointEditsUnionOldAndNewBounds()
    {
        var project = ProjectWithRiver();
        var riverId = project.River!.Id;
        var inserted = new InsertRiverPoint(CommandId.New(), project.Revision, 1,
            new MapPoint(240, 240), 18).Apply(project);
        Equal(4, inserted.Project.River!.Points.Length);
        Equal(4, inserted.Project.River.ResolvedWidthProfile.Widths.Length);

        var moved = new MoveRiverPoint(CommandId.New(), inserted.Project.Revision, 1,
            new MapPoint(420, 380)).Apply(inserted.Project);
        Equal(riverId, moved.Project.River!.Id);
        True(moved.Invalidation.Bounds.Left <= 210 && moved.Invalidation.Bounds.Top <= 210 &&
             moved.Invalidation.Bounds.Right >= 438 && moved.Invalidation.Bounds.Bottom >= 398,
            "Point drag did not union the old and new centreline bounds.");

        var deletedPoint = new DeleteRiverPoint(CommandId.New(), moved.Project.Revision, 1)
            .Apply(moved.Project);
        Equal(3, deletedPoint.Project.River!.Points.Length);
        Equal(riverId, deletedPoint.Project.River.Id);
        var deletedRiver = new DeleteRiver(CommandId.New(), deletedPoint.Project.Revision)
            .Apply(deletedPoint.Project);
        True(deletedRiver.Project.River is null, "Delete river left the modifier attached.");
        True(deletedPoint.Project.River is not null,
            "Delete river mutated the prior immutable snapshot needed for undo.");

        var twoPoint = River.Create(RiverId.New(), project.RequireRole(TerrainRole.Foreground).Id,
            [new MapPoint(0, 0), new MapPoint(20, 20)], [8, 8], 0);
        var twoPointProject = project with { River = twoPoint };
        Throws<InvalidOperationException>(() =>
            new DeleteRiverPoint(CommandId.New(), twoPointProject.Revision, 0).Apply(twoPointProject),
            "River point deletion accepted fewer than two centreline points.");
        Throws<ArgumentOutOfRangeException>(() =>
            new MoveRiverPoint(CommandId.New(), project.Revision, 1,
                new MapPoint(double.NaN, 0)).Apply(project),
            "Non-finite river geometry entered history.");
        return Task.CompletedTask;
    }

    private static async Task RiverSubtractionRemainsOrderedAfterLandPainting()
    {
        var root = Path.Combine(Path.GetTempPath(), $"mapwright-river-{Guid.NewGuid():N}");
        try
        {
            var initial = Fixture();
            var repository = new SqliteProjectRepository(root, new FixedClock());
            await repository.CreateAsync(initial);

            var land = BroadLand(initial.Revision);
            var landChange = land.Apply(initial);
            await repository.CommitAsync(initial, land, landChange, CancellationToken.None);

            var foreground = landChange.Project.RequireRole(TerrainRole.Foreground);
            var river = River.Create(RiverId.New(), foreground.Id,
                [new MapPoint(230, 256), new MapPoint(256, 256), new MapPoint(282, 256)],
                [24, 24, 24], bankSoftness: 0.5);
            var create = new CreateRiver(CommandId.New(), landChange.Project.Revision, river);
            var riverChange = create.Apply(landChange.Project);
            await repository.CommitAsync(landChange.Project, create, riverChange, CancellationToken.None);

            var lateLand = new AddLandStroke(CommandId.New(), riverChange.Project.Revision,
                TerrainRole.Foreground,
                Stroke(LandOperation.Add,
                    ResolvedLandBrush.FromDiameter(LandShape.RoundSoft, 80, 0, 0, 0, seed: 88),
                    new MapPoint(256, 256)));
            var lateChange = lateLand.Apply(riverChange.Project);
            await repository.CommitAsync(riverChange.Project, lateLand, lateChange, CancellationToken.None);

            Equal(0d, lateChange.Project.EvaluateForegroundCoverage(new MapPoint(256, 256)));
            Equal(0d, lateChange.Project.EvaluateForegroundCoverage(new MapPoint(255.5, 256)));
            Equal(0d, lateChange.Project.EvaluateForegroundCoverage(new MapPoint(256.5, 256)));
            var reopened = await repository.LoadAsync(initial.ProjectId, CancellationToken.None);
            Equal(river.Id, reopened.River!.Id);
            Equal(river.BankSoftness, reopened.River.BankSoftness);
            Equal(river.ResolvedWidthProfile.Widths[1], reopened.River.ResolvedWidthProfile.Widths[1]);
            Equal(0d, reopened.EvaluateForegroundCoverage(new MapPoint(256, 256)));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static Task RiverDimensionsAndRasterRoundingHaveExplicitBoundaries()
    {
        var foreground = Fixture().RequireRole(TerrainRole.Foreground);
        River.Create(RiverId.New(), foreground.Id,
            [new MapPoint(0, 0), new MapPoint(10, 0)],
            [RiverLimits.MinimumWidth, RiverLimits.MaximumWidth], RiverLimits.MinimumBankSoftness).Validate();
        River.Create(RiverId.New(), foreground.Id,
            [new MapPoint(0, 0), new MapPoint(10, 0)],
            [32, 32], RiverLimits.MaximumBankSoftness).Validate();
        Throws<ArgumentOutOfRangeException>(() =>
            River.Create(RiverId.New(), foreground.Id,
                [new MapPoint(0, 0), new MapPoint(10, 0)],
                [Math.BitDecrement(RiverLimits.MinimumWidth), 32], 0),
            "River width one step below the minimum entered history.");
        Throws<ArgumentOutOfRangeException>(() =>
            River.Create(RiverId.New(), foreground.Id,
                [new MapPoint(0, 0), new MapPoint(10, 0)],
                [32, Math.BitIncrement(RiverLimits.MaximumWidth)], 0),
            "River width one step above the maximum entered history.");
        Throws<ArgumentOutOfRangeException>(() =>
            River.Create(RiverId.New(), foreground.Id,
                [new MapPoint(0, 0), new MapPoint(10, 0)], [32, 32], Math.BitDecrement(0d)),
            "Bank softness one step below zero entered history.");
        Throws<ArgumentOutOfRangeException>(() =>
            River.Create(RiverId.New(), foreground.Id,
                [new MapPoint(0, 0), new MapPoint(10, 0)], [32, 32], Math.BitIncrement(1d)),
            "Bank softness one step above one entered history.");

        var transform = new DocumentRasterTransform(512, 512, 1024, 1024);
        Equal(24, transform.RoundDocumentLengthToOutput(12.25, horizontal: true));
        Equal(26, transform.RoundDocumentLengthToOutput(12.75, horizontal: true));
        var softRiver = River.Create(RiverId.New(), foreground.Id,
            [new MapPoint(0, 0), new MapPoint(100, 0)], [20, 20], 1);
        Equal(0.5, softRiver.SubtractionAt(new MapPoint(0, 15)), 1e-12);
        return Task.CompletedTask;
    }

    private static MapProject AddBroadLand(MapProject project) => BroadLand(project.Revision).Apply(project).Project;

    private static Task MapUnitLandAndRiverSupport()
    {
        MapBounds? expectedLand = null;
        MapBounds? expectedRiver = null;
        foreach (var sampling in new[] { 1024, 4096 })
        {
            var project = MapProject.CreateNormalizedFromGrid(ProjectId.New(), MapId.New(),
                "Land and river map units", 40, 30, sampling, Fixture().TerrainLayers);
            var land = Stroke(LandOperation.Add,
                ResolvedLandBrush.FromDiameter(LandShape.RoundSoft, 96, 0, 0, 0, seed: 42),
                new MapPoint(250, 375));
            var added = new AddLandStroke(CommandId.New(), project.Revision,
                TerrainRole.Foreground, land).Apply(project).Project;
            var savedLand = added.RequireRole(TerrainRole.Foreground).ResolvedLandStrokes.Single();
            Equal(96d, savedLand.Recipe.Diameter);
            Equal(new MapPoint(250, 375), savedLand.Samples[0]);
            if (expectedLand is { } landBounds) Equal(landBounds, savedLand.Bounds);
            expectedLand = savedLand.Bounds;

            var river = River.Create(RiverId.New(), added.RequireRole(TerrainRole.Foreground).Id,
                [new MapPoint(250, 375), new MapPoint(375, 375)], [96, 96], 0.35);
            var created = new CreateRiver(CommandId.New(), added.Revision, river).Apply(added).Project;
            Equal(96d, created.River!.ResolvedWidthProfile.Widths[0]);
            Equal(0.35d, created.River.BankSoftness);
            Equal(new MapPoint(250, 375), created.River.Points[0]);
            if (expectedRiver is { } riverBounds) Equal(riverBounds, created.River.Bounds(0));
            expectedRiver = created.River.Bounds(0);
            Equal(0d, created.EvaluateForegroundCoverage(new MapPoint(250, 375)));

            var moved = new MoveRiverPoint(CommandId.New(), created.Revision, 1,
                new MapPoint(400, 400)).Apply(created).Project;
            Equal(river.Id, moved.River!.Id);
            Equal(96d, moved.River.ResolvedWidthProfile.Widths[1]);
            Equal(0.35d, moved.River.BankSoftness);
            var transform = new DocumentRasterTransform(project.Width, project.Height,
                project.EditingPixelWidth, project.EditingPixelHeight);
            var center = transform.DocumentToOutput(moved.River.Points[0]);
            True(Math.Abs(center.X / project.EditingPixelWidth - 0.25) < 1e-12 &&
                 Math.Abs(center.Y / project.EditingPixelHeight - 0.5) < 1e-12,
                "Editing sampling changed the relative position of Land or river geometry.");
        }
        return Task.CompletedTask;
    }

    private static AddLandStroke BroadLand(long baseRevision) => new(
        CommandId.New(),
        baseRevision,
        TerrainRole.Foreground,
        Stroke(LandOperation.Add,
            ResolvedLandBrush.FromDiameter(LandShape.RoundSoft, 256, 0, 0, 0, seed: 51),
            new MapPoint(256, 256)));

    private static MapProject ProjectWithRiver()
    {
        var project = AddBroadLand(Fixture());
        var foreground = project.RequireRole(TerrainRole.Foreground);
        var river = River.Create(RiverId.New(), foreground.Id,
            [new MapPoint(220, 220), new MapPoint(256, 256), new MapPoint(292, 220)],
            [24, 24, 24], bankSoftness: 0.5);
        return new CreateRiver(CommandId.New(), project.Revision, river).Apply(project).Project;
    }

    private static MapProject Fixture() => FixtureWithRevision(3);

    private static MapProject FixtureWithRevision(long revision)
    {
        var background = new TerrainLayer(LayerId.New(), "Background", true, false, 1,
            new CoastlineStyle(0), ImmutableArray<PaintStroke>.Empty, TerrainRole.Background);
        var foreground = new TerrainLayer(LayerId.New(), "Foreground", true, false, 0.65,
            new CoastlineStyle(0), ImmutableArray<PaintStroke>.Empty, TerrainRole.Foreground);
        return new MapProject(ProjectId.New(), MapId.New(), "Mask fixture", 512, 512, revision,
            [background, foreground]);
    }

    private static LandStroke Stroke(
        LandOperation operation,
        ResolvedLandBrush recipe,
        params MapPoint[] samples) => new(StrokeId.New(), [.. samples], recipe, operation);

    private const string TextureHash =
        "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc";

    private static void True(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Equal<T>(T expected, T actual) where T : notnull
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected {expected}; got {actual}.");
    }

    private static void Equal(double expected, double actual, double tolerance)
    {
        if (Math.Abs(expected - actual) > tolerance)
            throw new InvalidOperationException($"Expected {expected}; got {actual}.");
    }

    private static void Throws<T>(Action action, string message) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException(message);
    }
}

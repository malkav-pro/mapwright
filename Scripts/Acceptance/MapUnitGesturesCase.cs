using System.Collections.Immutable;
using Godot;
using Mapwright.Application;
using Mapwright.App;
using Mapwright.Domain;

namespace Mapwright.Acceptance;

public sealed class MapUnitGesturesCaseProvider : IConnectedCaseProvider
{
    public void Register(ConnectedCaseRegistry registry) =>
        registry.Register("MapUnitGestures", MapUnitGesturesCase.RunAsync);
}

public static class MapUnitGesturesCase
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

        var controls = new EditorControlsState();
        controls.SetTextureIdentity(new string('c', 64));
        controls.SelectTool("Texture Brush");
        Check(controls.TextureDiameter.CommittedValue == 96 &&
              controls.TextureDiameter.Label.Contains("map units", StringComparison.Ordinal) &&
              ScopeCueContract.ForTool(controls).Readout.Contains("96 map units", StringComparison.Ordinal),
            "Texture diameter and scope readout must identify durable map units.");
        controls.SelectTool("Land");
        Check(controls.LandDiameter.CommittedValue == 96 &&
              controls.LandDiameter.Label.Contains("map units", StringComparison.Ordinal) &&
              ScopeCueContract.ForTool(controls).Readout.Contains("96 map units", StringComparison.Ordinal),
            "Land diameter and scope readout must identify durable map units.");
        controls.SelectTool("River");
        Check(controls.RiverWidth.Label.Contains("map units", StringComparison.Ordinal),
            "River point width must identify durable map units.");
        Check(!controls.TextureDiameter.Input("0") &&
              controls.TextureDiameter.CommittedValue == 96,
            "Invalid map-unit diameter changed the committed brush size.");
        controls.TextureDiameter.Escape();
        var refused = new GestureController();
        Check(!refused.BeginEdit("Texture Brush", "Foreground", 0, 250, 375,
                visible: true, locked: true).Accepted &&
              !refused.BeginEdit("Texture Brush", "Foreground", 0, 250, 375,
                visible: false, locked: false).Accepted,
            "Locked or hidden targets accepted a map-unit gesture.");

        var background = new TerrainLayer(LayerId.New(), "Background", true, false, 1,
            new CoastlineStyle(0), ImmutableArray<PaintStroke>.Empty, TerrainRole.Background);
        var foreground = new TerrainLayer(LayerId.New(), "Foreground", true, false, 1,
            new CoastlineStyle(0), ImmutableArray<PaintStroke>.Empty, TerrainRole.Foreground);
        var mapPath = new[] { new MapPoint(250, 375), new MapPoint(312.5, 437.5),
            new MapPoint(375, 500) };
        var committedTexture = new List<TexturePaintStroke>();
        var committedLand = new List<LandStroke>();
        foreach (var sampling in new[] { 1024, 4096 })
        {
            var project = MapProject.CreateNormalizedFromGrid(ProjectId.New(), MapId.New(),
                "Gesture map units", 40, 30, sampling, [background, foreground]);
            var textureSize = new Vector2(project.EditingPixelWidth, project.EditingPixelHeight);
            foreach (var zoom in new[] { 0.5f, 2f })
            {
                var pan = new Vector2(12, 24);
                var controller = new GestureController();
                foreach (var tool in new[] { "Texture Brush", "Land" })
                {
                    controls.SelectTool(tool);
                    var first = CanvasPoint(mapPath[0], project, textureSize, pan, zoom);
                    var mapped = MapCanvas.CanvasToDocument(first, textureSize, pan, zoom, project);
                    Check(mapped is not null && mapped.Value.DistanceTo(mapPath[0]) < 1e-8,
                        "Pointer down shifted in map space across sampling or zoom.");
                    var begin = controller.BeginEdit(tool, "Foreground", project.Revision,
                        mapped!.Value.X, mapped.Value.Y, visible: true, locked: false);
                    Check(begin.Accepted, "Visible unlocked target refused the map-unit gesture.");
                    foreach (var point in mapPath.Skip(1))
                    {
                        var screen = CanvasPoint(point, project, textureSize, pan, zoom);
                        var sample = MapCanvas.CanvasToDocument(screen, textureSize, pan, zoom, project);
                        Check(sample is not null && sample.Value.DistanceTo(point) < 1e-8,
                            "Pointer sample shifted in map space across sampling or zoom.");
                        controller.AddSample(sample!.Value.X, sample.Value.Y);
                    }
                    var plan = controller.PrepareCommit(project.Revision, visible: true, locked: false);
                    Check(plan.Samples.Length == mapPath.Length &&
                          plan.Samples.Select((point, index) => point.DistanceTo(mapPath[index]) < 1e-8).All(x => x),
                        "Saved gesture samples changed with viewport or editing sampling.");
                    var canvas = new MapCanvas();
                    canvas.BindEditorControls(controls);
                    var command = canvas.BuildGestureCommand(plan, project);
                    canvas.Free();
                    var changed = command.Apply(project).Project;
                    if (tool == "Texture Brush")
                    {
                        var stroke = changed.RequireRole(TerrainRole.Foreground).ResolvedTextureStrokes.Single();
                        Check(stroke.Recipe.Diameter == 96 &&
                              stroke.TextureAnchor.DistanceTo(mapPath[0]) < 1e-8,
                            "Texture diameter or anchor was scaled to viewport pixels.");
                        committedTexture.Add(stroke);
                    }
                    else
                    {
                        var stroke = changed.RequireRole(TerrainRole.Foreground).ResolvedLandStrokes.Single();
                        Check(stroke.Recipe.Diameter == 96 && stroke.Operation == LandOperation.Add,
                            "Land diameter or operation changed with viewport pixels.");
                        committedLand.Add(stroke);
                    }
                    controller.MarkCommitted(changed.Revision);
                    Check(controller.LastCommittedRevision == 1,
                        "One gesture did not acknowledge exactly one command.");
                }
            }
        }
        foreach (var stroke in committedTexture)
            Check(stroke.Bounds == committedTexture[0].Bounds,
                "Texture map-space bounds changed with zoom or sampling.");
        foreach (var stroke in committedLand)
            Check(stroke.Bounds == committedLand[0].Bounds,
                "Land map-space bounds changed with zoom or sampling.");

        var roundedProject = MapProject.CreateNormalizedFromGrid(ProjectId.New(), MapId.New(),
            "Rounded short raster", 40, 30, 1024, [background, foreground]);
        var roundedRadii = MapCanvas.ScopeCueScreenRadii(96, roundedProject,
            new Vector2(1024, 767), 0.5f);
        Check(Math.Abs(roundedRadii.X - 24.576f) < 1e-4 &&
              Math.Abs(roundedRadii.Y - 24.544f) < 1e-4,
            "Cue footprint must convert the map diameter independently on both raster axes.");
        Check(ScopeCueContract.UseCrosshair(5.9) && !ScopeCueContract.UseCrosshair(6),
            "Tiny crosshair selection must remain a screen-pixel threshold.");

        controls.SelectTool("River");
        var riverProject = MapProject.CreateNormalizedFromGrid(ProjectId.New(), MapId.New(),
            "River gesture", 40, 30, 1024, [background, foreground]);
        var onePoint = new GestureCommitPlan(1, 1, "River", "Foreground mask", 0,
            [new MapPoint(250, 375)]);
        var riverCanvas = new MapCanvas();
        riverCanvas.BindEditorControls(controls);
        var onePointRejected = false;
        try { riverCanvas.BuildGestureCommand(onePoint, riverProject); }
        catch (InvalidOperationException) { onePointRejected = true; }
        riverCanvas.Free();
        Check(onePointRejected, "A one-point river gesture must not invent a second map point.");

        var riverPaths = new List<ImmutableArray<MapPoint>>();
        foreach (var sampling in new[] { 1024, 4096 })
        {
            var project = MapProject.CreateNormalizedFromGrid(ProjectId.New(), MapId.New(),
                "River map units", 40, 30, sampling, [background, foreground]);
            var textureSize = new Vector2(project.EditingPixelWidth, project.EditingPixelHeight);
            foreach (var zoom in new[] { 0.5f, 1f, 4f })
            {
                var pan = new Vector2(12, 24);
                var controller = new GestureController();
                var start = CanvasPoint(mapPath[0], project, textureSize, pan, zoom);
                var first = MapCanvas.CanvasToDocument(start, textureSize, pan, zoom, project)!.Value;
                controller.BeginEdit("River", "Foreground mask", project.Revision,
                    first.X, first.Y, visible: true, locked: false);
                var middle = MapCanvas.CanvasToDocument(
                    CanvasPoint(mapPath[1], project, textureSize, pan, zoom),
                    textureSize, pan, zoom, project)!.Value;
                controller.AddSample(middle.X, middle.Y);
                var last = MapCanvas.CanvasToDocument(
                    CanvasPoint(mapPath[2], project, textureSize, pan, zoom),
                    textureSize, pan, zoom, project)!.Value;
                controller.AddFinalSample(last.X, last.Y);
                var plan = controller.PrepareCommit(project.Revision, visible: true, locked: false);
                var canvas = new MapCanvas();
                canvas.BindEditorControls(controls);
                var command = canvas.BuildGestureCommand(plan, project);
                canvas.Free();
                var saved = command.Apply(project).Project;
                var river = saved.River!;
                riverPaths.Add(river.Points);
                Check(river.Points.Length == 3 &&
                      river.Points.Select((point, index) => point.DistanceTo(mapPath[index]) < 1e-8).All(x => x),
                    "River map points changed with viewport zoom or editing sampling.");
                Check(river.ResolvedWidthProfile.Widths.All(width => width == 96) &&
                      river.BankSoftness == controls.RiverBankSoftness.CommittedValue,
                    "River width or bank softness changed with pixel density.");
                controls.SelectedRiverPoint = 1;
                var move = new GestureCommitPlan(2, 2, "River", "Foreground mask", saved.Revision,
                    [river.Points[1], new MapPoint(400, 400)]);
                var editingCanvas = new MapCanvas();
                editingCanvas.BindEditorControls(controls);
                var moved = editingCanvas.BuildGestureCommand(move, saved).Apply(saved).Project;
                editingCanvas.Free();
                Check(moved.River!.Id == river.Id &&
                      moved.River.Points[1] == new MapPoint(400, 400) &&
                      moved.River.ResolvedWidthProfile.Widths.SequenceEqual(river.ResolvedWidthProfile.Widths) &&
                      moved.River.BankSoftness == river.BankSoftness,
                    "River point edit changed identity, widths or bank softness.");
                controller.MarkCommitted(saved.Revision);
                Check(controller.LastCommittedRevision == 1,
                    "A river gesture did not acknowledge exactly one command.");
            }
        }
        foreach (var path in riverPaths)
            Check(path.SequenceEqual(riverPaths[0]),
                "Equivalent river paths retained different samples.");

        var adjacent = new GestureController();
        adjacent.BeginEdit("River", "Foreground mask", 0, 100, 100, true, false);
        Check(!adjacent.AddSample(100.1, 100),
            "Adjacent motion inside 0.25 map units was not deduplicated.");
        adjacent.AddSample(110, 100);
        adjacent.AddFinalSample(110.1, 100);
        var retained = adjacent.PrepareCommit(0, true, false);
        Check(retained.Samples.Length == 2 && retained.Samples[0] == new MapPoint(100, 100) &&
              retained.Samples[^1] == new MapPoint(110.1, 100),
            "Map-unit deduplication lost the first or release point.");
        Check(!adjacent.Cancel("Tool switched while saving"),
            "Ordinary cancellation discarded a gesture awaiting durable acknowledgement.");
        Check(adjacent.AbortPrepared(), "Failed save did not clear its prepared gesture.");
        Check(adjacent.LastCommittedRevision == -1 && !adjacent.IsEditing,
            "Cancelled gesture retained a command acknowledgement.");
        var tappedRiver = new GestureController();
        tappedRiver.BeginEdit("River", "Foreground mask", 0, 250, 375, true, false);
        var tapRejected = false;
        try { tappedRiver.PrepareCommit(0, true, false); }
        catch (InvalidOperationException) { tapRejected = true; }
        Check(tapRejected && tappedRiver.Cancel("One-point river") &&
              tappedRiver.PreviewSampleCount == 0,
            "One-point river left a prepared or committable gesture.");

        var capped = new GestureController();
        capped.BeginEdit("Land", "Foreground mask", 0, 0, 0, true, false);
        for (var index = 1; index <= GestureController.MaximumSamples + 5; index++)
            capped.AddSample((index % 2000) * 0.5, (index / 2000) * 0.5);
        capped.AddFinalSample(999, 10);
        Check(capped.PreviewSampleCount == GestureController.MaximumSamples &&
              capped.PreviewSamples[0] == new MapPoint(0, 0) &&
              capped.PreviewSamples[^1] == new MapPoint(999, 10),
            "Sample cap failed to retain ordered first and latest map points.");
        capped.Cancel("Dense gesture discarded");
        return Task.FromResult(assertions);
    }

    private static Vector2 CanvasPoint(MapPoint point, MapProject project, Vector2 textureSize,
        Vector2 pan, float zoom) => pan + new Vector2(
            (float)(point.X * textureSize.X / project.Width),
            (float)(point.Y * textureSize.Y / project.Height)) * zoom;
}

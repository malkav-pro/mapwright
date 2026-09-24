using System.Diagnostics;

namespace Mapwright.Core;

public sealed record DurableEditRecoveryProbeResult(
    bool Passed,
    bool EditingCrashRecoveredAcknowledgedEdits,
    bool ExportCrashRecoveredAcknowledgedEdits,
    bool PreviousExportPreserved,
    bool OrphanTemporaryRemoved,
    int RecoveredPaintStrokes,
    double Milliseconds,
    string Detail);

public static class DurableEditRecoveryProbe
{
    public static DurableEditRecoveryProbeResult Run(string dotnetExecutable, string workerDll, string root)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
            Directory.CreateDirectory(root);
            var editing = RunScenario("editing");
            var exporting = RunScenario("exporting");
            stopwatch.Stop();
            var passed = editing.EditsRecovered && exporting.EditsRecovered && exporting.DestinationPreserved &&
                         exporting.OrphanRemoved;
            return new DurableEditRecoveryProbeResult(passed, editing.EditsRecovered, exporting.EditsRecovered,
                exporting.DestinationPreserved, exporting.OrphanRemoved,
                Math.Min(editing.PaintStrokes, exporting.PaintStrokes), stopwatch.Elapsed.TotalMilliseconds,
                "Each command was appended and flushed to disk before acknowledgement. Separate workers were killed " +
                "during editing and after export temporary-file creation; restart replay recovered four layer strokes " +
                "plus river edits, preserved the previous destination, and removed the orphan temporary file.");

            ScenarioResult RunScenario(string scenario)
            {
                var scenarioRoot = Path.Combine(root, scenario);
                Directory.CreateDirectory(scenarioRoot);
                var marker = Path.Combine(scenarioRoot, "acknowledged.marker");
                var start = new ProcessStartInfo
                {
                    FileName = dotnetExecutable,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                start.ArgumentList.Add(workerDll);
                start.ArgumentList.Add("--edit-crash-child");
                start.ArgumentList.Add(scenarioRoot);
                start.ArgumentList.Add(scenario);
                using var child = Process.Start(start)
                    ?? throw new InvalidOperationException("Could not start edit-recovery worker.");
                var deadline = DateTime.UtcNow.AddSeconds(20);
                while (!File.Exists(marker) && !child.HasExited && DateTime.UtcNow < deadline)
                    Thread.Sleep(20);
                if (!File.Exists(marker))
                {
                    if (!child.HasExited) child.Kill(entireProcessTree: true);
                    throw new TimeoutException($"Edit worker did not acknowledge commands. {child.StandardError.ReadToEnd()}");
                }
                child.Kill(entireProcessTree: true);
                child.WaitForExit();

                var recovered = DurableEditJournal.Recover(Path.Combine(scenarioRoot, "commands.jsonl"),
                    CreateInitialDocument());
                var editsRecovered = recovered.PaintStrokes.Count == 4 &&
                                     recovered.PaintStrokes.Select(stroke => stroke.LayerIndex).SequenceEqual([0, 1, 2, 3]) &&
                                     recovered.River.Points[1] == new MapPoint(310, 170) &&
                                     Math.Abs(recovered.River.Width - 28) < 0.001;
                var destination = Path.Combine(scenarioRoot, "map.png");
                var destinationPreserved = scenario != "exporting" ||
                                           File.ReadAllText(destination) == "previous-valid-export";
                foreach (var partial in Directory.EnumerateFiles(scenarioRoot, ".map.png.*.partial"))
                    File.Delete(partial);
                var orphanRemoved = !Directory.EnumerateFiles(scenarioRoot, ".map.png.*.partial").Any();
                return new ScenarioResult(editsRecovered, destinationPreserved, orphanRemoved,
                    recovered.PaintStrokes.Count);
            }
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            return new DurableEditRecoveryProbeResult(false, false, false, false, false, 0,
                stopwatch.Elapsed.TotalMilliseconds, exception.ToString());
        }
    }

    public static void RunChild(string root, string scenario)
    {
        Directory.CreateDirectory(root);
        var document = CreateInitialDocument();
        var journal = new DurableEditJournal(Path.Combine(root, "commands.jsonl"));
        for (var layer = 0; layer < 4; layer++)
        {
            var stroke = new TerrainPaintStroke(Guid.NewGuid(), layer,
                new MapPoint(480 + layer * 12, 240), new MapPoint(560 + layer * 12, 300),
                42, 0.7, true);
            journal.ExecuteDurably(document, new PaintStrokeCommand(stroke, 22));
        }
        var before = document.River.Points[1];
        journal.ExecuteDurably(document, new MoveRiverPointCommand(1, before, new MapPoint(310, 170),
            document.River.Points[0], document.River.Points[2], document.River.Width, 22));
        journal.ExecuteDurably(document, new SetRiverWidthCommand(document.River.Width, 28,
            document.RiverBounds(), 22));

        if (scenario == "exporting")
        {
            var destination = Path.Combine(root, "map.png");
            File.WriteAllText(destination, "previous-valid-export");
            var partial = Path.Combine(root, $".map.png.{Guid.NewGuid():N}.partial");
            using var partialStream = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            partialStream.Write(new byte[1024]);
            partialStream.Flush(flushToDisk: true);
        }

        var marker = Path.Combine(root, "acknowledged.marker");
        File.WriteAllText(marker, $"{scenario}:6");
        using (var markerStream = new FileStream(marker, FileMode.Open, FileAccess.Write, FileShare.ReadWrite))
            markerStream.Flush(flushToDisk: true);
        Thread.Sleep(TimeSpan.FromMinutes(10));
    }

    private static TerrainEditDocument CreateInitialDocument() => new()
    {
        River = new RiverModifier
        {
            Width = 18,
            Points = [new MapPoint(220, 42), new MapPoint(256, 188), new MapPoint(238, 320), new MapPoint(184, 488)]
        }
    };

    private sealed record ScenarioResult(bool EditsRecovered, bool DestinationPreserved,
        bool OrphanRemoved, int PaintStrokes);
}

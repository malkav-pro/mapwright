using System.Diagnostics;
using System.Text.Json;

namespace Mapwright.Core;

public readonly record struct MapPoint(double X, double Y);

public readonly record struct MapBounds(double Left, double Top, double Right, double Bottom)
{
    public static MapBounds Around(MapPoint point, double radius) =>
        new(point.X - radius, point.Y - radius, point.X + radius, point.Y + radius);

    public MapBounds Union(MapBounds other) => new(
        Math.Min(Left, other.Left), Math.Min(Top, other.Top),
        Math.Max(Right, other.Right), Math.Max(Bottom, other.Bottom));

    public static MapBounds AroundSegment(MapPoint start, MapPoint end, double radius) => new(
        Math.Min(start.X, end.X) - radius,
        Math.Min(start.Y, end.Y) - radius,
        Math.Max(start.X, end.X) + radius,
        Math.Max(start.Y, end.Y) + radius);

    public bool Contains(MapPoint point) =>
        point.X >= Left && point.X <= Right && point.Y >= Top && point.Y <= Bottom;
}

public sealed class RiverModifier
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public List<MapPoint> Points { get; init; } = [];
    public double Width { get; set; } = 18;
}

public sealed record TerrainPaintStroke(
    Guid Id,
    int LayerIndex,
    MapPoint Start,
    MapPoint End,
    double Radius,
    double Opacity,
    bool Add);

public interface IEditCommand
{
    MapBounds AffectedBounds { get; }
    void Apply(TerrainEditDocument document);
    void Revert(TerrainEditDocument document);
}

public sealed record MoveRiverPointCommand(
    int PointIndex,
    MapPoint Before,
    MapPoint After,
    MapPoint? PreviousPoint,
    MapPoint? NextPoint,
    double RiverWidth,
    double DownstreamEffectRadius = 0) : IEditCommand
{
    public MapBounds AffectedBounds
    {
        get
        {
            var radius = RiverWidth * 0.5 + DownstreamEffectRadius;
            var bounds = MapBounds.Around(Before, radius).Union(MapBounds.Around(After, radius));
            if (PreviousPoint is { } previous)
                bounds = bounds.Union(MapBounds.AroundSegment(previous, Before, radius))
                    .Union(MapBounds.AroundSegment(previous, After, radius));
            if (NextPoint is { } next)
                bounds = bounds.Union(MapBounds.AroundSegment(Before, next, radius))
                    .Union(MapBounds.AroundSegment(After, next, radius));
            return bounds;
        }
    }

    public void Apply(TerrainEditDocument document) => document.River.Points[PointIndex] = After;
    public void Revert(TerrainEditDocument document) => document.River.Points[PointIndex] = Before;
}

public sealed record SetRiverWidthCommand(
    double Before,
    double After,
    MapBounds RiverBounds,
    double DownstreamEffectRadius) : IEditCommand
{
    private double InvalidationRadius => Math.Max(Before, After) * 0.5 + DownstreamEffectRadius;

    public MapBounds AffectedBounds => new(
        RiverBounds.Left - InvalidationRadius, RiverBounds.Top - InvalidationRadius,
        RiverBounds.Right + InvalidationRadius, RiverBounds.Bottom + InvalidationRadius);

    public void Apply(TerrainEditDocument document) => document.River.Width = After;
    public void Revert(TerrainEditDocument document) => document.River.Width = Before;
}

public sealed record PaintStrokeCommand(TerrainPaintStroke Stroke, double DownstreamEffectRadius) : IEditCommand
{
    public MapBounds AffectedBounds => MapBounds.AroundSegment(
        Stroke.Start, Stroke.End, Stroke.Radius + DownstreamEffectRadius);

    public void Apply(TerrainEditDocument document)
    {
        if (Stroke.LayerIndex is < 0 or > 3) throw new ArgumentOutOfRangeException(nameof(Stroke.LayerIndex));
        document.PaintStrokes.Add(Stroke);
    }

    public void Revert(TerrainEditDocument document)
    {
        if (document.PaintStrokes.Count == 0 || document.PaintStrokes[^1].Id != Stroke.Id)
            throw new InvalidOperationException("Paint history is not in reversible order.");
        document.PaintStrokes.RemoveAt(document.PaintStrokes.Count - 1);
    }
}

public sealed class TerrainEditDocument
{
    private readonly List<IEditCommand> _history = [];
    private int _cursor;

    public required RiverModifier River { get; init; }
    public List<TerrainPaintStroke> PaintStrokes { get; } = [];
    public long Revision { get; private set; }
    public int UndoCount => _cursor;
    public int RedoCount => _history.Count - _cursor;

    public void Execute(IEditCommand command)
    {
        if (_cursor < _history.Count) _history.RemoveRange(_cursor, _history.Count - _cursor);
        command.Apply(this);
        _history.Add(command);
        _cursor++;
        Revision++;
    }

    public MapBounds Undo()
    {
        if (_cursor == 0) throw new InvalidOperationException("Nothing to undo.");
        var command = _history[--_cursor];
        command.Revert(this);
        Revision++;
        return command.AffectedBounds;
    }

    public MapBounds Redo()
    {
        if (_cursor >= _history.Count) throw new InvalidOperationException("Nothing to redo.");
        var command = _history[_cursor++];
        command.Apply(this);
        Revision++;
        return command.AffectedBounds;
    }

    public MapBounds RiverBounds()
    {
        var left = River.Points.Min(point => point.X);
        var top = River.Points.Min(point => point.Y);
        var right = River.Points.Max(point => point.X);
        var bottom = River.Points.Max(point => point.Y);
        return new MapBounds(left, top, right, bottom);
    }
}

public sealed record JournalCommandRecord(
    long Sequence,
    string Type,
    TerrainPaintStroke? Stroke = null,
    int PointIndex = 0,
    MapPoint Before = default,
    MapPoint After = default,
    MapPoint? PreviousPoint = null,
    MapPoint? NextPoint = null,
    double BeforeWidth = 0,
    double AfterWidth = 0,
    MapBounds RiverBounds = default,
    double RiverWidth = 0,
    double DownstreamEffectRadius = 0);

public sealed class DurableEditJournal
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _path;
    private long _nextSequence;

    public DurableEditJournal(string path)
    {
        _path = path;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        _nextSequence = ReadRecords(path).LastOrDefault()?.Sequence + 1 ?? 1;
    }

    public long ExecuteDurably(TerrainEditDocument document, IEditCommand command)
    {
        var record = ToRecord(_nextSequence, command);
        AppendAndFlush(record);
        document.Execute(command);
        _nextSequence++;
        return record.Sequence;
    }

    public static TerrainEditDocument Recover(string path, TerrainEditDocument document)
    {
        long expectedSequence = 1;
        foreach (var record in ReadRecords(path))
        {
            if (record.Sequence != expectedSequence)
                throw new InvalidDataException($"Journal sequence gap: expected {expectedSequence}, got {record.Sequence}.");
            document.Execute(ToCommand(record));
            expectedSequence++;
        }
        return document;
    }

    private void AppendAndFlush(JournalCommandRecord record)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(record, JsonOptions);
        using var stream = new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.Read,
            4096, FileOptions.WriteThrough);
        stream.Write(bytes);
        stream.WriteByte((byte)'\n');
        stream.Flush(flushToDisk: true);
    }

    private static List<JournalCommandRecord> ReadRecords(string path)
    {
        var records = new List<JournalCommandRecord>();
        if (!File.Exists(path)) return records;
        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                var record = JsonSerializer.Deserialize<JournalCommandRecord>(line, JsonOptions);
                if (record is not null) records.Add(record);
            }
            catch (JsonException)
            {
                break; // An unacknowledged append may leave one torn tail record.
            }
        }
        return records;
    }

    private static JournalCommandRecord ToRecord(long sequence, IEditCommand command) => command switch
    {
        PaintStrokeCommand paint => new JournalCommandRecord(sequence, "paint", Stroke: paint.Stroke,
            DownstreamEffectRadius: paint.DownstreamEffectRadius),
        MoveRiverPointCommand move => new JournalCommandRecord(sequence, "move-river-point",
            PointIndex: move.PointIndex, Before: move.Before, After: move.After,
            PreviousPoint: move.PreviousPoint, NextPoint: move.NextPoint, RiverWidth: move.RiverWidth,
            DownstreamEffectRadius: move.DownstreamEffectRadius),
        SetRiverWidthCommand width => new JournalCommandRecord(sequence, "set-river-width",
            BeforeWidth: width.Before, AfterWidth: width.After, RiverBounds: width.RiverBounds,
            DownstreamEffectRadius: width.DownstreamEffectRadius),
        _ => throw new NotSupportedException($"Command is not journalled: {command.GetType().Name}")
    };

    private static IEditCommand ToCommand(JournalCommandRecord record) => record.Type switch
    {
        "paint" when record.Stroke is not null => new PaintStrokeCommand(record.Stroke,
            record.DownstreamEffectRadius),
        "move-river-point" => new MoveRiverPointCommand(record.PointIndex, record.Before, record.After,
            record.PreviousPoint, record.NextPoint, record.RiverWidth, record.DownstreamEffectRadius),
        "set-river-width" => new SetRiverWidthCommand(record.BeforeWidth, record.AfterWidth,
            record.RiverBounds, record.DownstreamEffectRadius),
        _ => throw new InvalidDataException($"Unknown journal command type: {record.Type}")
    };
}

public sealed record HistoryProbeResult(
    bool Passed,
    int Commands,
    double MedianUndoMilliseconds,
    double P95UndoMilliseconds,
    bool MoveRestored,
    bool WidthRestored,
    bool NewEditInvalidatedRedo,
    bool AdjacentSegmentsInvalidated,
    bool WidthEffectsInvalidated,
    string Detail);

public static class HistoryProbe
{
    public static HistoryProbeResult Run()
    {
        const int commandCount = 2_000;
        var document = new TerrainEditDocument
        {
            River = new RiverModifier
            {
                Width = 18,
                Points = [new MapPoint(220, 42), new MapPoint(256, 188), new MapPoint(238, 320), new MapPoint(184, 488)]
            }
        };
        var originalPoint = document.River.Points[1];
        var originalWidth = document.River.Width;
        for (var index = 0; index < commandCount; index++)
        {
            if ((index & 1) == 0)
            {
                var before = document.River.Points[1];
                var after = new MapPoint(245 + index % 23, 180 + index % 19);
                document.Execute(CreateMoveCommand(document, 1, before, after));
            }
            else
            {
                var before = document.River.Width;
                var after = 12 + index % 17;
                document.Execute(new SetRiverWidthCommand(before, after, document.RiverBounds(), 22));
            }
        }

        var timings = new List<double>(commandCount);
        for (var index = 0; index < commandCount; index++)
        {
            var started = Stopwatch.GetTimestamp();
            document.Undo();
            timings.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }
        var moveRestored = document.River.Points[1] == originalPoint;
        var widthRestored = Math.Abs(document.River.Width - originalWidth) < 0.0001;
        document.Redo();
        var beforeReplacement = document.River.Points[2];
        var replacement = CreateMoveCommand(document, 2, beforeReplacement,
            new MapPoint(beforeReplacement.X + 24, beforeReplacement.Y - 16));
        document.Execute(replacement);
        var redoInvalidated = document.RedoCount == 0;
        var oldSegmentMidpoint = Midpoint(document.River.Points[1], beforeReplacement);
        var newSegmentMidpoint = Midpoint(document.River.Points[1], document.River.Points[2]);
        var adjacentSegmentsInvalidated = replacement.AffectedBounds.Contains(oldSegmentMidpoint) &&
                                          replacement.AffectedBounds.Contains(newSegmentMidpoint) &&
                                          replacement.AffectedBounds.Contains(Midpoint(beforeReplacement, document.River.Points[3])) &&
                                          replacement.AffectedBounds.Contains(Midpoint(document.River.Points[2], document.River.Points[3]));
        var riverBounds = document.RiverBounds();
        var widthReplacement = new SetRiverWidthCommand(18, 20, riverBounds, 22);
        var expectedWidthReach = 20 * 0.5 + 22;
        var widthEffectsInvalidated = widthReplacement.AffectedBounds.Contains(
            new MapPoint(riverBounds.Left - expectedWidthReach, riverBounds.Top));
        var ordered = timings.Order().ToArray();
        var median = Percentile(ordered, 0.5);
        var p95 = Percentile(ordered, 0.95);
        return new HistoryProbeResult(moveRestored && widthRestored && redoInvalidated && adjacentSegmentsInvalidated &&
                                      widthEffectsInvalidated && p95 <= 100,
            commandCount, median, p95, moveRestored, widthRestored, redoInvalidated, adjacentSegmentsInvalidated,
            widthEffectsInvalidated,
            "Document-owned river point and width commands report affected bounds, including old/new adjacent segments and width changes expanded for downstream effect reach. Undo, redo and redo-branch invalidation are deterministic. This measures resident command state only; evicted replay remains untested.");
    }

    private static MoveRiverPointCommand CreateMoveCommand(TerrainEditDocument document, int index,
        MapPoint before, MapPoint after, double downstreamEffectRadius = 22) => new(
        index, before, after,
        index > 0 ? document.River.Points[index - 1] : null,
        index + 1 < document.River.Points.Count ? document.River.Points[index + 1] : null,
        document.River.Width, downstreamEffectRadius);

    private static MapPoint Midpoint(MapPoint a, MapPoint b) => new((a.X + b.X) * 0.5, (a.Y + b.Y) * 0.5);

    private static double Percentile(double[] ordered, double percentile)
    {
        var index = Math.Clamp((int)Math.Ceiling(ordered.Length * percentile) - 1, 0, ordered.Length - 1);
        return ordered[index];
    }
}

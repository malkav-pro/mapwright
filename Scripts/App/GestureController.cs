using System.Collections.Immutable;
using System.Diagnostics;
using Mapwright.Domain;

namespace Mapwright.App;

public enum ShortcutRoute
{
    None,
    Field,
    Modal,
    Blocked,
    Canvas,
    Application
}

public sealed record GestureBeginResult(bool Accepted, string Message, string? Action);

public sealed record GestureCommitPlan(
    long SequenceId,
    long InputTimestamp,
    string Tool,
    string Target,
    long BaseRevision,
    ImmutableArray<MapPoint> Samples)
{
    public int SampleCount => Samples.Length;
}

public sealed record GesturePresentation(
    long SequenceId,
    long InputTimestamp,
    long BaseRevision,
    long CommittedRevision,
    long PresentedRevision);

public sealed record CanvasView(double PanX, double PanY, double Zoom);

/// <summary>
/// Owns transient editor interaction state. It never mutates a document: release prepares exactly
/// one immutable plan for MapCanvas to submit through EditSession, while every interruption simply
/// discards the preview.
/// </summary>
public sealed class GestureController
{
    public const int MaximumSamples = 8_192;
    private const double MinimumSampleDistance = 0.25;

    private readonly List<MapPoint> _samples = [];
    private long _nextSequenceId;
    private long _activeSequenceId;
    private long _activeInputTimestamp;
    private long _activeBaseRevision;
    private string _activeTarget = "Foreground";
    private bool _prepared;
    private bool _temporaryPan;
    private double _lastPanPointerX;
    private double _lastPanPointerY;
    private double _pointerX;
    private double _pointerY;

    public string ActiveTool { get; private set; } = "Texture Brush";
    public bool IsEditing { get; private set; }
    public bool IsTemporaryPanning => _temporaryPan;
    public int PreviewSampleCount => _samples.Count;
    public IReadOnlyList<MapPoint> PreviewSamples => _samples;
    public double PanX { get; private set; }
    public double PanY { get; private set; }
    public double Zoom { get; private set; } = 1;
    public double PointerWorldX => (_pointerX - PanX) / Zoom;
    public double PointerWorldY => (_pointerY - PanY) / Zoom;
    public long LastSequenceId { get; private set; }
    public long LastCommittedRevision { get; private set; } = -1;
    public long LastPresentedRevision { get; private set; } = -1;
    public GesturePresentation? LastPresentation { get; private set; }

    public GestureBeginResult BeginEdit(
        string tool,
        string target,
        long baseRevision,
        double x,
        double y,
        bool visible,
        bool locked)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tool);
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        ValidatePoint(x, y);
        if (locked)
            return new GestureBeginResult(false, $"{target} is locked. Unlock {target} to paint it.", "Unlock");
        if (!visible)
            return new GestureBeginResult(false, $"{target} is hidden. Show {target} to paint it.", "Show layer");
        if (IsEditing || _prepared)
            throw new InvalidOperationException("A gesture is already active or awaiting acknowledgement.");

        ActiveTool = tool;
        _activeTarget = target;
        _activeBaseRevision = baseRevision;
        _activeSequenceId = Interlocked.Increment(ref _nextSequenceId);
        _activeInputTimestamp = Stopwatch.GetTimestamp();
        _samples.Clear();
        _samples.Add(new MapPoint(x, y));
        IsEditing = true;
        return new GestureBeginResult(true, $"{tool} preview on {target}", null);
    }

    public bool AddSample(double x, double y)
    {
        if (!IsEditing) return false;
        ValidatePoint(x, y);
        var next = new MapPoint(x, y);
        if (_samples.Count > 0 && _samples[^1].DistanceTo(next) < MinimumSampleDistance) return false;
        if (_samples.Count >= MaximumSamples)
        {
            _samples[^1] = next;
            return true;
        }
        _samples.Add(next);
        return true;
    }

    /// <summary>Keep the release position even when its distance from the previous
    /// motion event is below the map-unit deduplication threshold.</summary>
    public bool AddFinalSample(double x, double y)
    {
        if (!IsEditing) return false;
        ValidatePoint(x, y);
        var next = new MapPoint(x, y);
        if (_samples[^1] == next) return false;
        if (_samples.Count == 1 && _samples[0] != next)
        {
            _samples.Add(next);
            return true;
        }
        if (_samples.Count >= MaximumSamples ||
            _samples[^1].DistanceTo(next) < MinimumSampleDistance)
            _samples[^1] = next;
        else
            _samples.Add(next);
        return true;
    }

    public GestureCommitPlan PrepareCommit(long currentRevision, bool visible, bool locked)
    {
        if (!IsEditing || _samples.Count == 0)
            throw new InvalidOperationException("No edit gesture is available to commit.");
        if (currentRevision != _activeBaseRevision)
            throw new RevisionConflictException(_activeBaseRevision, currentRevision);
        if (locked) throw new InvalidOperationException($"{_activeTarget} is locked. Unlock {_activeTarget} to paint it.");
        if (!visible) throw new InvalidOperationException($"{_activeTarget} is hidden. Show {_activeTarget} to paint it.");
        if (ActiveTool == "River" && _samples.Count < 2)
            throw new InvalidOperationException("A river requires two distinct map points.");

        IsEditing = false;
        _prepared = true;
        return new GestureCommitPlan(_activeSequenceId, _activeInputTimestamp, ActiveTool,
            _activeTarget, _activeBaseRevision, _samples.ToImmutableArray());
    }

    public GesturePresentation MarkCommitted(long revision)
    {
        if (!_prepared) throw new InvalidOperationException("No prepared gesture is awaiting acknowledgement.");
        if (revision <= _activeBaseRevision)
            throw new InvalidOperationException("A gesture acknowledgement must advance the revision.");
        _prepared = false;
        _samples.Clear();
        LastSequenceId = _activeSequenceId;
        LastCommittedRevision = revision;
        LastPresentation = new GesturePresentation(
            _activeSequenceId, _activeInputTimestamp, _activeBaseRevision, revision, -1);
        return LastPresentation;
    }

    public GesturePresentation MarkPresented(long revision)
    {
        if (LastPresentation is null || LastPresentation.CommittedRevision != revision)
            throw new InvalidOperationException("The presented revision does not match the acknowledged gesture.");
        LastPresentedRevision = revision;
        LastPresentation = LastPresentation with { PresentedRevision = revision };
        return LastPresentation;
    }

    public bool Cancel(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (!IsEditing)
        {
            if (_temporaryPan)
            {
                _temporaryPan = false;
                return true;
            }
            return false;
        }
        IsEditing = false;
        _samples.Clear();
        return true;
    }

    public bool AbortPrepared()
    {
        if (!_prepared) return false;
        _prepared = false;
        _samples.Clear();
        return true;
    }

    public CanvasView SetView(double panX, double panY, double zoom)
    {
        if (!double.IsFinite(panX) || !double.IsFinite(panY) || !double.IsFinite(zoom) || zoom <= 0)
            throw new ArgumentOutOfRangeException(nameof(zoom));
        PanX = panX;
        PanY = panY;
        Zoom = Math.Clamp(zoom, 0.01, 16);
        return View();
    }

    public CanvasView RememberPointer(double x, double y)
    {
        ValidatePoint(x, y);
        _pointerX = x;
        _pointerY = y;
        return View();
    }

    public CanvasView ZoomAt(double x, double y, double factor)
    {
        ValidatePoint(x, y);
        if (!double.IsFinite(factor) || factor <= 0) throw new ArgumentOutOfRangeException(nameof(factor));
        RememberPointer(x, y);
        var worldX = PointerWorldX;
        var worldY = PointerWorldY;
        Zoom = Math.Clamp(Zoom * factor, 0.01, 16);
        PanX = x - worldX * Zoom;
        PanY = y - worldY * Zoom;
        return View();
    }

    public bool BeginTemporaryPan(double x, double y, string source)
    {
        ValidatePoint(x, y);
        if (source is not ("Space" or "Middle"))
            throw new ArgumentOutOfRangeException(nameof(source));
        if (IsEditing) Cancel("Temporary pan");
        _temporaryPan = true;
        _lastPanPointerX = x;
        _lastPanPointerY = y;
        RememberPointer(x, y);
        return true;
    }

    public CanvasView MoveTemporaryPan(double x, double y)
    {
        ValidatePoint(x, y);
        if (!_temporaryPan) return RememberPointer(x, y);
        PanX += x - _lastPanPointerX;
        PanY += y - _lastPanPointerY;
        _lastPanPointerX = x;
        _lastPanPointerY = y;
        return RememberPointer(x, y);
    }

    public bool EndTemporaryPan()
    {
        var wasPanning = _temporaryPan;
        _temporaryPan = false;
        return wasPanning;
    }

    public ShortcutRoute RouteShortcut(
        string shortcut,
        bool fieldFocused,
        bool modalOpen,
        bool historyRebuilding)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shortcut);
        if (modalOpen) return ShortcutRoute.Modal;
        if (fieldFocused && (shortcut is "Ctrl+Z" or "B" or "L" or "R" or "H" or "[" or "]"))
            return ShortcutRoute.Field;
        if (historyRebuilding)
            return shortcut is "Wheel" or "Space" or "Middle" ? ShortcutRoute.Canvas : ShortcutRoute.Blocked;
        if (shortcut is "B" or "L" or "R" or "H" or "[" or "]" or "Escape" or "Wheel" or
            "Space" or "Middle" or "Alt" or "Delete" or "Tab") return ShortcutRoute.Canvas;
        if (shortcut is "Ctrl+S" or "Ctrl+Z" or "Ctrl+Shift+Z" or "Ctrl+Shift+E" or "F6")
            return ShortcutRoute.Application;
        return ShortcutRoute.None;
    }

    private CanvasView View() => new(PanX, PanY, Zoom);

    private static void ValidatePoint(double x, double y)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y))
            throw new ArgumentOutOfRangeException(nameof(x), "Gesture coordinates must be finite.");
    }
}

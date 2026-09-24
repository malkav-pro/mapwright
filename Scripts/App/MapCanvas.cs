using Godot;
using Mapwright.Application;
using Mapwright.Domain;
using Mapwright.Rendering;
using System.Diagnostics;
using System.Collections.Immutable;

namespace Mapwright.App;

public readonly record struct ConnectedCanvasUpdate(
    long Revision, long Generation, int DirtyTiles,
    double ReferenceRenderMilliseconds, double TextureUploadMilliseconds)
{
    public string AccelerationMode { get; init; } = "none";
    public ConnectedTerrainGraph.ViewportSourceStages? SourceStages { get; init; }
}

public partial class MapCanvas : Control
{
    private const int ConnectedPreviewMaxDimension = 896;
    private const int ConnectedDetailMaxDimension = 1792;
    private Texture2D? _mapTexture;
    private readonly Dictionary<(int X, int Y), ImageTexture> _mapTiles = [];
    private Vector2I _tileCanvasSize;
    private ConnectedGpuTerrainRenderer? _gpuTerrain;
    private string? _gpuTerrainUnavailable;
    private Rid _gpuTerrainTexture;
    private Vector2I _gpuTerrainSize;
    private ProjectId? _gpuTerrainProject;
    private int _connectedDisplayLimit = ConnectedPreviewMaxDimension;
    private bool _detailRefreshQueued;
    private Vector2 _detailAnchor;
    private ProjectId? _tileProjectId;
    private string? _tileSourceHash;
    private GlobalGpuBrushSurface? _brushSurface;
    private Rid _gpuTexture;
    private Vector2I _gpuTextureSize;
    private Rid _terrainTracerTexture;
    private Vector2I _terrainTracerSize;
    private Guid _terrainTracerEpoch;
    private long _terrainTracerRevision = -1;
    private long _terrainTracerGeneration;
    private Vector2 _pan;
    private float _zoom = 0.1f;
    private bool _panning;
    private bool _painting;
    private readonly Queue<long> _pendingVisibleDabs = new();
    private readonly List<double> _latencies = [];
    private readonly List<double> _frameTimes = [];
    private double _benchmarkSecondsRemaining;
    private double _sampleAccumulator;
    private long _lastFrameTimestamp;
    private int _benchmarkDabs;
    private int _maximumPendingSubmissions;
    private Action<BrushViewportProbeResult>? _benchmarkComplete;
    private EditSession? _editSession;
    private ConnectedTerrainGraph? _connectedGraph;
    private Action<string>? _connectedStatus;
    private readonly GestureController _gestureController = new();
    private bool _committingStroke;
    private bool _spacePan;
    private long? _pendingPresentedRevision;
    private long? _pendingPresentedGeneration;
    private long _mapTextureRevision = -1;
    private long _mapTextureGeneration;
    public long LastDrawnRevision { get; private set; } = -1;
    public long LastDrawnGeneration { get; private set; }
    public long LastDrawnTimestamp { get; private set; }
    private string _editorTool = "Texture Brush";
    private TerrainRole _textureTarget = TerrainRole.Foreground;
    private EditorControlsState? _editorControls;
    private MapProject? _scopeEvidenceProject;
    private LandOperation _landOperationAtGestureStart = LandOperation.Add;
    private Vector2 _scopeCursor;
    private bool _scopeCursorVisible;

    public GestureController Gestures => _gestureController;
    public Vector2I ConnectedDisplaySize => _gpuTerrainTexture.IsValid ? _gpuTerrainSize : _tileCanvasSize;

    /// <summary>Why the GPU terrain renderer is not in use, or null when it is.</summary>
    public string? GpuTerrainUnavailableReason => _gpuTerrainUnavailable;

    /// <summary>Starts an explicitly opt-in GPU feasibility generation.</summary>
    public Guid BeginGpuTerrainTracer()
    {
        if (System.Environment.GetEnvironmentVariable("MAPWRIGHT_GPU_TERRAIN_TRACER") != "1")
            throw new InvalidOperationException("The internal GPU terrain tracer is disabled.");
        _terrainTracerEpoch = Guid.NewGuid();
        _terrainTracerTexture = default;
        _terrainTracerRevision = -1;
        _terrainTracerGeneration = 0;
        return _terrainTracerEpoch;
    }

    public void PresentGpuTerrainTracer(Guid epoch, GpuTraceSubmission submission,
        Vector2I dimensions)
    {
        if (epoch == Guid.Empty || epoch != _terrainTracerEpoch ||
            submission.Revision < 0 || submission.Generation <= _terrainTracerGeneration ||
            !submission.DisplayTexture.IsValid || dimensions.X <= 0 || dimensions.Y <= 0)
            throw new InvalidOperationException("GPU tracer publication has stale or invalid identity.");
        _terrainTracerTexture = submission.DisplayTexture;
        _terrainTracerSize = dimensions;
        _terrainTracerRevision = submission.Revision;
        _terrainTracerGeneration = submission.Generation;
        FitToView();
        QueueRedraw();
    }

    public void EndGpuTerrainTracer(Guid epoch)
    {
        if (epoch != _terrainTracerEpoch) return;
        _terrainTracerEpoch = Guid.Empty;
        _terrainTracerTexture = default;
        _terrainTracerRevision = -1;
        _terrainTracerGeneration = 0;
        QueueRedraw();
    }

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;
        FocusMode = FocusModeEnum.All;
        ClipContents = true;
        SetProcessUnhandledInput(true);
        Resized += OnCanvasResized;
        RenderingServer.FramePostDraw += OnFramePostDraw;
        // Compile the GPU terrain kernels with the application, not inside the first edit.
        ConnectedGpuTerrainRenderer.PrepareKernels();
    }

    public override void _ExitTree()
    {
        RenderingServer.FramePostDraw -= OnFramePostDraw;
        Resized -= OnCanvasResized;
        _brushSurface?.Dispose();
        _terrainTracerEpoch = Guid.Empty;
        _terrainTracerTexture = default;
        ClearTileTextures();
        ClearGpuTerrain();
        _gpuTerrain?.Dispose();
        _gpuTerrain = null;
    }

    public void EnableGpuBrush(Action ready)
    {
        _gpuTextureSize = new Vector2I(1024, 1024);
        _brushSurface = new GlobalGpuBrushSurface(_gpuTextureSize.X, _gpuTextureSize.Y,
            rid =>
            {
                _gpuTexture = rid;
                FitToView();
                QueueRedraw();
                ready();
            },
            timestamp =>
            {
                _pendingVisibleDabs.Enqueue(timestamp);
                _maximumPendingSubmissions = Math.Max(_maximumPendingSubmissions, _pendingVisibleDabs.Count);
                QueueRedraw();
            });
    }

    public void StartAutomatedBrushProbe(double seconds, Action<BrushViewportProbeResult> complete)
    {
        _latencies.Clear();
        _frameTimes.Clear();
        _pendingVisibleDabs.Clear();
        _benchmarkDabs = 0;
        _maximumPendingSubmissions = 0;
        _sampleAccumulator = 0;
        _lastFrameTimestamp = Stopwatch.GetTimestamp();
        _benchmarkSecondsRemaining = seconds;
        _benchmarkComplete = complete;
        SetProcess(true);
    }

    public override void _Process(double delta)
    {
        if (_gestureController.IsEditing && !Input.IsMouseButtonPressed(MouseButton.Left))
            CancelActiveGesture("Capture lost");
        if (_gestureController.IsTemporaryPanning &&
            !Input.IsMouseButtonPressed(MouseButton.Left) &&
            !Input.IsMouseButtonPressed(MouseButton.Middle))
        {
            _gestureController.EndTemporaryPan();
            _panning = false;
        }
        if (_benchmarkComplete is null || _brushSurface is null || !_gpuTexture.IsValid) return;
        var frameTimestamp = Stopwatch.GetTimestamp();
        _frameTimes.Add(Stopwatch.GetElapsedTime(_lastFrameTimestamp, frameTimestamp).TotalMilliseconds);
        _lastFrameTimestamp = frameTimestamp;
        _benchmarkSecondsRemaining -= delta;
        _sampleAccumulator += delta * 120.0;
        while (_benchmarkSecondsRemaining > 0 && _sampleAccumulator >= 1.0)
        {
            var phase = _benchmarkDabs * 0.071f;
            var center = new Vector2(90 + (_benchmarkDabs * 19) % 844,
                512 + MathF.Sin(phase) * 310);
            _brushSurface.PaintDab(center, 128, 0.32f, add: true, Stopwatch.GetTimestamp());
            _benchmarkDabs++;
            _sampleAccumulator -= 1.0;
        }
        _maximumPendingSubmissions = Math.Max(_maximumPendingSubmissions, _pendingVisibleDabs.Count);
        if (_benchmarkSecondsRemaining > 0 || _pendingVisibleDabs.Count > 0) return;

        var result = new BrushViewportProbeResult(
            _latencies.Count > 0,
            false,
            _benchmarkDabs,
            Percentile(_latencies, 0.5),
            Percentile(_latencies, 0.95),
            Percentile(_frameTimes, 0.5),
            Percentile(_frameTimes, 0.95),
            _frameTimes.Count(value => value > 33.3),
            _frameTimes.Count == 0 ? 0 : _frameTimes.Max(),
            _maximumPendingSubmissions,
            "Fed synthetic samples at 120 Hz without waiting for the preceding sample. Frame intervals use monotonic timestamps rather than Godot's smoothed delta. Submission timing ends at the next FramePostDraw event after the render-thread callback; it does not correlate a particular dab with presentation, so the precise input-to-visible gate is not established. No CPU readback is used.");
        var complete = _benchmarkComplete;
        _benchmarkComplete = null;
        SetProcess(false);
        complete(result);
    }

    public void SetPng(byte[] pngBytes)
        => SetPngCore(pngBytes, -1);

    public long SetPngForRevision(byte[] pngBytes, long revision)
    {
        if (revision < 0) throw new ArgumentOutOfRangeException(nameof(revision));
        SetPngCore(pngBytes, revision);
        return _mapTextureGeneration;
    }

    private void SetPngCore(byte[] pngBytes, long revision)
    {
        ClearTileTextures();
        ClearGpuTerrain();
        var image = new Image();
        var error = image.LoadPngFromBuffer(pngBytes);
        if (error != Error.Ok) throw new InvalidDataException($"PNG decode failed: {error}");
        _mapTexture = ImageTexture.CreateFromImage(image);
        _mapTextureRevision = revision;
        _mapTextureGeneration++;
        _pendingPresentedRevision = null;
        _pendingPresentedGeneration = null;
        FitToView();
        QueueRedraw();
    }

    public void BindConnectedSession(
        EditSession session,
        ConnectedTerrainGraph graph,
        Action<string> status,
        TileInvalidation? invalidation = null)
    {
        _editSession = session;
        _connectedStatus = status;
        if (!ReferenceEquals(_connectedGraph, graph)) BindConnectedGraph(graph);
        ShowCommittedChange(session.Current, invalidation);
        _gestureController.SetView(_pan.X, _pan.Y, _zoom);
        _ = session.RehearseAsync(RehearsalCommands(session.Current));
        // Binding can precede the shell's first layout pass. Fit once more after
        // container sizing so a large imported map does not remain anchored to
        // the initial minimum-size canvas.
        CallDeferred(MethodName.FitToView);
    }

    public void BindConnectedGraph(ConnectedTerrainGraph graph)
    {
        _connectedGraph = graph ?? throw new ArgumentNullException(nameof(graph));
        _connectedDisplayLimit = ConnectedPreviewMaxDimension;
        _detailRefreshQueued = false;
        ClearTileTextures();
        // A new graph owns new source caches; derived GPU inputs from the previous
        // graph must not survive into it.
        ClearGpuTerrain();
        _gpuTerrain?.Dispose();
        _gpuTerrain = null;
    }

    public void ShowCommittedSnapshot(MapProject snapshot) => ShowCommittedChange(snapshot, null);

    public ConnectedCanvasUpdate ShowCommittedChange(MapProject snapshot, TileInvalidation? invalidation)
    {
        if (_connectedGraph is null) throw new InvalidOperationException("No connected render graph is bound.");
        var source = snapshot.ImportedSource
            ?? throw new InvalidDataException("Connected canvas requires an imported source.");
        var size = ResolveSamplingSize(snapshot, _connectedDisplayLimit);
        if (TryShowOnGpu(snapshot, size) is { } gpuUpdate) return gpuUpdate;
        var preserveView = _mapTiles.Count > 0 && _tileProjectId == snapshot.ProjectId &&
            _tileSourceHash == source.PreviewBlobHash && _tileCanvasSize != size;
        var previousSize = _tileCanvasSize;
        var rebuild = _mapTiles.Count == 0 || _tileProjectId != snapshot.ProjectId ||
            _tileSourceHash != source.PreviewBlobHash || _tileCanvasSize != size;
        var regions = ConnectedTerrainGraph.TilesForBounds(snapshot, size.X, size.Y,
            rebuild ? null : invalidation?.Bounds);
        var render = Stopwatch.StartNew();
        _connectedGraph.PrimeViewportInputs(snapshot, size.X, size.Y);
        var frames = new ConnectedTerrainFrame[regions.Count];
        Parallel.For(0, regions.Count, index =>
        {
            var region = regions[index];
            frames[index] = _connectedGraph.EvaluateRegionAtSize(snapshot, size.X, size.Y,
                region.Left, region.Top, region.Width, region.Height);
        });
        render.Stop();
        var renderMilliseconds = render.Elapsed.TotalMilliseconds;
        if (rebuild)
        {
            ClearTileTextures();
            ClearGpuTerrain();
            _tileProjectId = snapshot.ProjectId;
            _tileSourceHash = source.PreviewBlobHash;
            _tileCanvasSize = size;
        }
        var uploadMilliseconds = 0d;
        for (var index = 0; index < regions.Count; index++)
        {
            var region = regions[index];
            var frame = frames[index];
            var upload = Stopwatch.StartNew();
            using var image = Image.CreateFromData(frame.Width, frame.Height, false, Image.Format.Rgba8, frame.Rgba);
            var key = (region.Left, region.Top);
            if (_mapTiles.TryGetValue(key, out var texture)) texture.Update(image);
            else _mapTiles.Add(key, ImageTexture.CreateFromImage(image));
            upload.Stop();
            uploadMilliseconds += upload.Elapsed.TotalMilliseconds;
        }
        _mapTexture = null;
        _mapTextureRevision = snapshot.Revision;
        _mapTextureGeneration++;
        _pendingPresentedRevision = null;
        _pendingPresentedGeneration = null;
        if (rebuild)
        {
            if (preserveView)
            {
                var view = PreserveDetailAnchor(
                    new Vector2(previousSize.X, previousSize.Y),
                    new Vector2(size.X, size.Y), _pan, _zoom, _detailAnchor);
                _pan = view.Pan;
                _zoom = view.Zoom;
                _gestureController.SetView(_pan.X, _pan.Y, _zoom);
            }
            else FitToView();
        }
        if (_gestureController.LastCommittedRevision == snapshot.Revision)
        {
            _pendingPresentedRevision = snapshot.Revision;
            _pendingPresentedGeneration = _mapTextureGeneration;
        }
        QueueRedraw();
        return new ConnectedCanvasUpdate(snapshot.Revision, _mapTextureGeneration,
            regions.Count, renderMilliseconds, uploadMilliseconds)
        {
            AccelerationMode = _connectedGraph.LastViewportAccelerationMode,
            SourceStages = _connectedGraph.LastViewportSourceStages
        };
    }

    /// <summary>
    /// Evaluates the whole visible canvas on the GPU from verified sources and the
    /// acknowledged snapshot. Returns null (CPU reference path) when no RenderingDevice
    /// exists, the fp64 kernels are unavailable, or recording fails.
    /// </summary>
    private ConnectedCanvasUpdate? TryShowOnGpu(MapProject snapshot, Vector2I size)
    {
        if (_gpuTerrainUnavailable is not null || _connectedGraph is null) return null;
        if (_gpuTerrain is null)
        {
            _gpuTerrain = ConnectedGpuTerrainRenderer.TryCreate(out var reason);
            if (_gpuTerrain is null)
            {
                _gpuTerrainUnavailable = reason;
                GD.Print($"GPU terrain renderer unavailable; using CPU reference renderer: {reason}");
                return null;
            }
        }
        GpuTerrainGeneration generation;
        var render = Stopwatch.StartNew();
        double sourceMilliseconds;
        try
        {
            var sources = _connectedGraph.PrepareGpuSources(snapshot, size.X, size.Y);
            sourceMilliseconds = render.Elapsed.TotalMilliseconds;
            generation = _gpuTerrain.Render(snapshot, sources, _mapTextureGeneration + 1);
        }
        catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException)
        {
            _gpuTerrainUnavailable = exception.Message;
            GD.PrintErr($"GPU terrain rendering failed; using CPU reference renderer: {exception}");
            ClearGpuTerrain();
            _gpuTerrain.Dispose();
            _gpuTerrain = null;
            return null;
        }
        render.Stop();

        var previousSize = _gpuTerrainTexture.IsValid ? _gpuTerrainSize : _tileCanvasSize;
        var sameProject = (_gpuTerrainTexture.IsValid ? _gpuTerrainProject : _tileProjectId) == snapshot.ProjectId;
        var hadView = _gpuTerrainTexture.IsValid || _mapTiles.Count > 0;
        var sizeChanged = !hadView || !sameProject || previousSize != size;
        ClearTileTextures();
        _mapTexture = null;
        _gpuTerrainTexture = generation.DisplayTexture;
        _gpuTerrainSize = size;
        _gpuTerrainProject = snapshot.ProjectId;
        _mapTextureRevision = snapshot.Revision;
        _mapTextureGeneration = generation.Generation;
        _pendingPresentedRevision = null;
        _pendingPresentedGeneration = null;
        if (sizeChanged)
        {
            if (hadView && sameProject && previousSize != Vector2I.Zero)
            {
                var view = PreserveDetailAnchor(new Vector2(previousSize.X, previousSize.Y),
                    new Vector2(size.X, size.Y), _pan, _zoom, _detailAnchor);
                _pan = view.Pan;
                _zoom = view.Zoom;
                _gestureController.SetView(_pan.X, _pan.Y, _zoom);
            }
            else FitToView();
        }
        if (_gestureController.LastCommittedRevision == snapshot.Revision)
        {
            _pendingPresentedRevision = snapshot.Revision;
            _pendingPresentedGeneration = _mapTextureGeneration;
        }
        QueueRedraw();
        return new ConnectedCanvasUpdate(snapshot.Revision, _mapTextureGeneration, 1,
            render.Elapsed.TotalMilliseconds, generation.InputMilliseconds)
        {
            AccelerationMode = generation.InputsRebuilt
                ? ConnectedGpuTerrainRenderer.AccelerationMode + "-rebuilt-inputs"
                : ConnectedGpuTerrainRenderer.AccelerationMode,
            SourceStages = _connectedGraph.LastViewportSourceStages with
            {
                AccumulatorMilliseconds = generation.RecordMilliseconds,
                ResampleMilliseconds = sourceMilliseconds
            }
        };
    }

    /// <summary>Validation-only readback of the displayed GPU generation.</summary>
    public byte[]? ReadDisplayedGpuTerrainForValidation() =>
        _gpuTerrain is null || !_gpuTerrainTexture.IsValid
            ? null
            : _gpuTerrain.ReadbackForValidation(_mapTextureGeneration);

    private void ClearGpuTerrain()
    {
        _gpuTerrainTexture = default;
        _gpuTerrainSize = Vector2I.Zero;
        _gpuTerrainProject = null;
    }

    internal static Vector2I ResolveSamplingSize(MapProject snapshot, int displayLimit)
    {
        if (displayLimit <= 0) throw new ArgumentOutOfRangeException(nameof(displayLimit));
        var source = snapshot.ImportedSource
            ?? throw new InvalidDataException("Connected canvas requires an imported source.");
        var editingWidth = snapshot.StorageFormatVersion == 3
            ? snapshot.EditingPixelWidth : source.PreviewWidth;
        var editingHeight = snapshot.StorageFormatVersion == 3
            ? snapshot.EditingPixelHeight : source.PreviewHeight;
        var previewScale = Math.Min(1d, displayLimit /
            (double)Math.Max(editingWidth, editingHeight));
        return new Vector2I(
            Math.Max(1, (int)Math.Round(editingWidth * previewScale, MidpointRounding.ToEven)),
            Math.Max(1, (int)Math.Round(editingHeight * previewScale, MidpointRounding.ToEven)));
    }

    private void ClearTileTextures()
    {
        foreach (var texture in _mapTiles.Values) texture.Dispose();
        _mapTiles.Clear();
        _tileCanvasSize = Vector2I.Zero;
        _tileProjectId = null;
        _tileSourceHash = null;
    }

    public void FitToView()
    {
        var textureSize = CurrentTextureSize();
        if (textureSize == Vector2.Zero || Size.X <= 0 || Size.Y <= 0) return;
        var fit = Math.Min(Size.X / textureSize.X, Size.Y / textureSize.Y);
        _zoom = Math.Clamp(fit * 0.94f, 0.01f, 8f);
        var drawnSize = textureSize * _zoom;
        _pan = (Size - drawnSize) * 0.5f;
        _gestureController.SetView(_pan.X, _pan.Y, _zoom);
        QueueRedraw();
    }

    internal static (Vector2 Pan, float Zoom) PreserveDetailAnchor(
        Vector2 previousSize, Vector2 nextSize, Vector2 pan, float zoom, Vector2 anchor)
    {
        if (previousSize.X <= 0 || previousSize.Y <= 0 ||
            nextSize.X <= 0 || nextSize.Y <= 0 || zoom <= 0)
            throw new ArgumentOutOfRangeException(nameof(previousSize));
        var mapFraction = new Vector2(
            (anchor.X - pan.X) / (previousSize.X * zoom),
            (anchor.Y - pan.Y) / (previousSize.Y * zoom));
        var nextZoom = zoom * previousSize.X / nextSize.X;
        var nextPan = anchor - new Vector2(
            mapFraction.X * nextSize.X * nextZoom,
            mapFraction.Y * nextSize.Y * nextZoom);
        return (nextPan, nextZoom);
    }

    private void QueueAdaptiveDetail(Vector2 anchor)
    {
        if (_editSession?.Current.ImportedSource is not { } source ||
            _connectedGraph is null || (_mapTiles.Count == 0 && !_gpuTerrainTexture.IsValid)) return;
        var currentMax = Math.Max(ConnectedDisplaySize.X, ConnectedDisplaySize.Y);
        var sourceMax = _editSession.Current.StorageFormatVersion == 3
            ? Math.Max(_editSession.Current.EditingPixelWidth, _editSession.Current.EditingPixelHeight)
            : Math.Max(source.PreviewWidth, source.PreviewHeight);
        var screenMax = currentMax * _zoom;
        var desired = _connectedDisplayLimit;
        if (screenMax > ConnectedPreviewMaxDimension * 1.4f &&
            sourceMax > ConnectedPreviewMaxDimension)
            desired = Math.Min(ConnectedDetailMaxDimension, sourceMax);
        else if (screenMax < ConnectedPreviewMaxDimension * 1.1f)
            desired = ConnectedPreviewMaxDimension;
        if (desired == _connectedDisplayLimit)
        {
            if (_detailRefreshQueued) _detailAnchor = anchor;
            return;
        }
        _connectedDisplayLimit = desired;
        _detailAnchor = anchor;
        if (_detailRefreshQueued) return;
        _detailRefreshQueued = true;
        CallDeferred(MethodName.RefreshAdaptiveDetail);
    }

    public void RefreshAdaptiveDetail()
    {
        _detailRefreshQueued = false;
        if (_editSession is null || _connectedGraph is null) return;
        _connectedStatus?.Invoke("Refining canvas detail…");
        try
        {
            ShowCommittedChange(_editSession.Current, null);
            _connectedStatus?.Invoke("Canvas detail ready");
        }
        catch (Exception exception)
        {
            _connectedDisplayLimit = Math.Max(ConnectedDisplaySize.X, ConnectedDisplaySize.Y) >
                ConnectedPreviewMaxDimension ? ConnectedDetailMaxDimension : ConnectedPreviewMaxDimension;
            _connectedStatus?.Invoke($"Canvas detail unavailable — existing view retained: {exception.Message}");
        }
    }

    private void OnCanvasResized()
    {
        if (CurrentTextureSize() != Vector2.Zero) FitToView();
        QueueAdaptiveDetail(Size * 0.5f);
    }

    public void SetEditorTool(string tool, TerrainRole textureTarget = TerrainRole.Foreground)
    {
        if (tool is not ("Pan" or "Texture Brush" or "Land" or "River"))
            throw new ArgumentOutOfRangeException(nameof(tool));
        if (_gestureController.IsEditing) CancelActiveGesture("Tool changed");
        _editorTool = tool;
        _textureTarget = textureTarget;
    }

    public void BindEditorControls(EditorControlsState controls)
    {
        _editorControls = controls ?? throw new ArgumentNullException(nameof(controls));
        SetEditorTool(controls.ActiveTool is "Pan" or "Texture Brush" or "Land" or "River"
            ? controls.ActiveTool
            : "Pan", controls.TextureTarget);
    }

    public void BindScopeEvidence(EditorControlsState controls, MapProject project)
    {
        _editorControls = controls ?? throw new ArgumentNullException(nameof(controls));
        _scopeEvidenceProject = project ?? throw new ArgumentNullException(nameof(project));
        SetEditorTool(controls.ActiveTool is "Pan" or "Texture Brush" or "Land" or "River"
            ? controls.ActiveTool
            : "Pan", controls.TextureTarget);
        CallDeferred(MethodName.FitToView);
        QueueRedraw();
    }

    public void SetEvidenceCursor(Vector2 canvasPosition)
    {
        _scopeCursor = canvasPosition;
        _scopeCursorVisible = true;
        _gestureController.RememberPointer(canvasPosition.X, canvasPosition.Y);
        QueueRedraw();
    }

    public void SetEvidenceCursorAtMapFraction(float x, float y)
    {
        if (!float.IsFinite(x) || !float.IsFinite(y) || x is < 0 or > 1 || y is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(x));
        var size = CurrentTextureSize();
        if (size == Vector2.Zero) throw new InvalidOperationException("No map is visible for a scope cue.");
        SetEvidenceCursor(_pan + new Vector2(size.X * x, size.Y * y) * _zoom);
    }

    public bool CancelActiveGesture(string reason)
    {
        var cancelled = _gestureController.Cancel(reason);
        if (!cancelled) return false;
        _painting = false;
        _panning = false;
        _connectedStatus?.Invoke($"Gesture cancelled — {reason}; no command was created");
        QueueRedraw();
        return true;
    }

    public override void _Notification(int what)
    {
        if (what == NotificationWMWindowFocusOut) CancelActiveGesture("Window deactivated");
    }

    public override void _GuiInput(InputEvent inputEvent)
    {
        switch (inputEvent)
        {
            case InputEventKey key when key.Keycode == Key.Space:
                _spacePan = key.Pressed;
                AcceptEvent();
                break;
            case InputEventKey key when key.Keycode == Key.Escape && key.Pressed:
                if (CancelActiveGesture("Escape")) AcceptEvent();
                break;
            case InputEventMouseButton button when button.ButtonIndex == MouseButton.Middle:
                _panning = button.Pressed;
                if (button.Pressed)
                    _gestureController.BeginTemporaryPan(button.Position.X, button.Position.Y, "Middle");
                else
                    _gestureController.EndTemporaryPan();
                AcceptEvent();
                break;
            case InputEventMouseButton button when button.ButtonIndex == MouseButton.Left && _spacePan:
                _panning = button.Pressed;
                if (button.Pressed)
                    _gestureController.BeginTemporaryPan(button.Position.X, button.Position.Y, "Space");
                else
                    _gestureController.EndTemporaryPan();
                AcceptEvent();
                break;
            case InputEventMouseButton button when button.ButtonIndex == MouseButton.Left && _editSession is not null:
                if (_committingStroke) break;
                _painting = button.Pressed;
                if (button.Pressed)
                {
                    GrabFocus();
                    if (_editorTool == "Pan")
                    {
                        _panning = true;
                        _gestureController.BeginTemporaryPan(button.Position.X, button.Position.Y, "Space");
                    }
                    else
                    {
                        var project = _editSession.Current;
                        var targetRole = _editorTool == "Texture Brush" ? _textureTarget : TerrainRole.Foreground;
                        var target = project.RequireRole(targetRole);
                        var point = CanvasToDocument(button.Position);
                        if (point is not null)
                        {
                            var targetName = _editorTool == "Texture Brush"
                                ? targetRole.ToString()
                                : "Foreground mask";
                            if (_editorTool == "Land" && _editorControls is not null)
                            {
                                _landOperationAtGestureStart = Input.IsKeyPressed(Key.Alt)
                                    ? (_editorControls.LandOperation == LandOperation.Add
                                        ? LandOperation.Subtract
                                        : LandOperation.Add)
                                    : _editorControls.LandOperation;
                            }
                            var result = _gestureController.BeginEdit(_editorTool, targetName,
                                project.Revision, point.Value.X, point.Value.Y, target.Visible, target.Locked);
                            if (!result.Accepted)
                            {
                                _painting = false;
                                _connectedStatus?.Invoke($"{result.Message} · {result.Action}");
                            }
                        }
                    }
                }
                else if (_gestureController.IsTemporaryPanning)
                {
                    _panning = false;
                    _gestureController.EndTemporaryPan();
                }
                else if (_gestureController.IsEditing)
                {
                    var project = _editSession.Current;
                    var targetRole = _editorTool == "Texture Brush" ? _textureTarget : TerrainRole.Foreground;
                    var target = project.RequireRole(targetRole);
                    try
                    {
                        var released = CanvasToDocument(button.Position);
                        if (released is { } end)
                            _gestureController.AddFinalSample(end.X, end.Y);
                        var plan = _gestureController.PrepareCommit(project.Revision, target.Visible, target.Locked);
                        CommitConnectedStrokeAsync(plan);
                    }
                    catch (Exception exception)
                    {
                        CancelActiveGesture("Commit validation failed");
                        _connectedStatus?.Invoke(exception.Message);
                    }
                }
                AcceptEvent();
                break;
            case InputEventMouseButton button when button.ButtonIndex == MouseButton.Left && _brushSurface is not null:
                _painting = button.Pressed;
                if (button.Pressed) PaintAt(button.Position);
                AcceptEvent();
                break;
            case InputEventMouseMotion motion when _painting && _brushSurface is not null:
                PaintAt(motion.Position, motion.Pressure);
                AcceptEvent();
                break;
            case InputEventMouseMotion motion when _gestureController.IsEditing && _editSession is not null:
                SetEvidenceCursor(motion.Position);
                AddConnectedSample(motion.Position);
                AcceptEvent();
                break;
            case InputEventMouseMotion motion when _panning:
                SetEvidenceCursor(motion.Position);
                ApplyView(_gestureController.MoveTemporaryPan(motion.Position.X, motion.Position.Y));
                QueueRedraw();
                AcceptEvent();
                break;
            case InputEventMouseMotion motion:
                SetEvidenceCursor(motion.Position);
                break;
            case InputEventMouseButton wheel when wheel.Pressed &&
                wheel.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown:
            {
                ZoomAt(wheel.Position, wheel.ButtonIndex == MouseButton.WheelUp ? 1.15 : 1 / 1.15);
                AcceptEvent();
                break;
            }
        }
    }

    public void ZoomAt(Vector2 canvasPosition, double factor)
    {
        ApplyView(_gestureController.ZoomAt(canvasPosition.X, canvasPosition.Y, factor));
        QueueAdaptiveDetail(canvasPosition);
        QueueRedraw();
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), new Color("10171a"));
        var textureSize = CurrentTextureSize();
        if (textureSize == Vector2.Zero)
        {
            DrawString(ThemeDB.FallbackFont, new Vector2(28, 42), "Import an .ink backup to begin",
                HorizontalAlignment.Left, -1, 18, new Color("94a3a8"));
            return;
        }

        var destination = new Rect2(_pan, textureSize * _zoom);
        if (_terrainTracerTexture.IsValid)
        {
            RenderingServer.CanvasItemAddTextureRect(GetCanvasItem(), destination,
                _terrainTracerTexture, false, Colors.White, false);
            LastDrawnRevision = _terrainTracerRevision;
            LastDrawnGeneration = _terrainTracerGeneration;
            LastDrawnTimestamp = Stopwatch.GetTimestamp();
        }
        else if (_gpuTerrainTexture.IsValid)
        {
            RenderingServer.CanvasItemAddTextureRect(GetCanvasItem(), destination,
                _gpuTerrainTexture, false, Colors.White, false);
            LastDrawnRevision = _mapTextureRevision;
            LastDrawnGeneration = _mapTextureGeneration;
            LastDrawnTimestamp = Stopwatch.GetTimestamp();
        }
        else if (_gpuTexture.IsValid)
            RenderingServer.CanvasItemAddTextureRect(GetCanvasItem(), destination, _gpuTexture, false, Colors.White, false);
        else if (_mapTexture is not null)
        {
            DrawTextureRect(_mapTexture, destination, tile: false);
            LastDrawnRevision = _mapTextureRevision;
            LastDrawnGeneration = _mapTextureGeneration;
            LastDrawnTimestamp = Stopwatch.GetTimestamp();
        }
        else if (_mapTiles.Count > 0)
        {
            foreach (var ((x, y), tile) in _mapTiles)
                DrawTextureRect(tile, new Rect2(_pan + new Vector2(x, y) * _zoom,
                    new Vector2(tile.GetWidth(), tile.GetHeight()) * _zoom), tile: false);
            LastDrawnRevision = _mapTextureRevision;
            LastDrawnGeneration = _mapTextureGeneration;
            LastDrawnTimestamp = Stopwatch.GetTimestamp();
        }
        else if (_scopeEvidenceProject is not null)
        {
            DrawRect(destination, new Color("26343a"));
            DrawCircle(destination.GetCenter(), Math.Min(destination.Size.X, destination.Size.Y) * 0.34f,
                new Color("819c6b"));
            DrawCircle(destination.GetCenter() + new Vector2(destination.Size.X * 0.08f, -destination.Size.Y * 0.04f),
                Math.Min(destination.Size.X, destination.Size.Y) * 0.22f, new Color("a9bd86"));
        }
        DrawRect(destination, new Color("d9b86c"), filled: false, width: 1f);
        DrawEditingScopeCue(textureSize);
    }

    private void DrawEditingScopeCue(Vector2 textureSize)
    {
        if (!_scopeCursorVisible || _editorControls is null ||
            _editorTool is not ("Land" or "Texture Brush")) return;
        var descriptor = ScopeCueContract.ForTool(_editorControls);
        var project = _editSession?.Current ?? _scopeEvidenceProject;
        if (project is null) return;
        var centre = _scopeCursor;
        if (_gestureController.PreviewSampleCount > 0)
        {
            var latest = _gestureController.PreviewSamples[^1];
            centre = _pan + new Vector2(
                (float)(latest.X / project.Width * textureSize.X),
                (float)(latest.Y / project.Height * textureSize.Y)) * _zoom;
        }
        var diameter = descriptor.Scope == EditingScope.Coverage
            ? _editorControls.LandDiameter.CommittedValue
            : _editorControls.TextureDiameter.CommittedValue;
        var radii = ScopeCueScreenRadii(diameter, project, textureSize, _zoom);
        var radius = Math.Max(0.5f, radii.X);
        var outline = new Color("f4f6fb");
        var accent = new Color("93a6ff");
        if (ScopeCueContract.UseCrosshair(Math.Max(radii.X, radii.Y) * 2))
        {
            DrawLine(centre - new Vector2(5, 0), centre + new Vector2(5, 0), outline, 2);
            DrawLine(centre - new Vector2(0, 5), centre + new Vector2(0, 5), outline, 2);
        }
        else if (descriptor.Scope == EditingScope.Coverage)
        {
            DrawColoredPolygon(CueEllipsePoints(centre, radii),
                new Color(accent.R, accent.G, accent.B, 0.22f));
            DrawCueEllipse(centre, radii, outline, 2f);
            DrawCircle(centre, 3, outline);
        }
        else
        {
            DrawCueEllipse(centre, radii, outline, 2f);
            DrawCueEllipse(centre, radii * (float)_editorControls.TextureHardness.CommittedValue,
                accent, 1.5f, dashed: true);
            DrawDashedRectangle(new Rect2(centre - radii, radii * 2), accent);
            DrawCircle(centre, 2.5f, outline);
            var swatch = TextureSwatch(_editorControls.SelectedTextureIdentity,
                (float)_editorControls.TextureOpacity.CommittedValue);
            DrawRect(new Rect2(centre + new Vector2(radius + 10, -12), new Vector2(24, 24)), swatch);
            DrawRect(new Rect2(centre + new Vector2(radius + 10, -12), new Vector2(24, 24)), outline,
                filled: false, width: 1);
        }
        DrawCueReadout(centre, radius, descriptor.Readout);
    }

    private void DrawCueReadout(Vector2 centre, float radius, string text)
    {
        const int fontSize = 12;
        const int lineHeight = 17;
        var font = ThemeDB.FallbackFont;
        var widthLimit = Math.Max(120f, Size.X - 24f);
        var lines = new List<string>();
        var line = "";
        foreach (var part in text.Split(" · ", StringSplitOptions.None))
        {
            var candidate = line.Length == 0 ? part : line + " · " + part;
            if (line.Length > 0 &&
                font.GetStringSize(candidate, HorizontalAlignment.Left, -1, fontSize).X > widthLimit)
            {
                lines.Add(line);
                line = part;
            }
            else
            {
                line = candidate;
            }
        }
        if (line.Length > 0) lines.Add(line);
        if (lines.Count == 0) return;
        var width = lines.Max(value => font.GetStringSize(value, HorizontalAlignment.Left, -1, fontSize).X);
        var position = centre + new Vector2(radius + 10, radius + 24);
        position.X = Math.Clamp(position.X, 8, Math.Max(8, Size.X - width - 20));
        position.Y = Math.Clamp(position.Y, 24,
            Math.Max(24, Size.Y - (lines.Count - 1) * lineHeight - 12));
        var box = new Rect2(position - new Vector2(6, 17),
            new Vector2(width + 12, lines.Count * lineHeight + 10));
        DrawRect(box, new Color(0.05f, 0.06f, 0.09f, 0.94f));
        DrawRect(box, new Color("5c6fe0"), filled: false, width: 1);
        foreach (var value in lines)
        {
            DrawString(font, position, value, HorizontalAlignment.Left, -1, fontSize,
                new Color("ecedf3"));
            position.Y += lineHeight;
        }
    }

    internal static Vector2 ScopeCueScreenRadii(double diameter, MapProject project,
        Vector2 textureSize, float zoom)
    {
        if (!double.IsFinite(diameter) || diameter <= 0 ||
            !float.IsFinite(zoom) || zoom <= 0 ||
            textureSize.X <= 0 || textureSize.Y <= 0)
            throw new ArgumentOutOfRangeException(nameof(diameter));
        return new Vector2(
            (float)(diameter * textureSize.X * zoom / project.Width / 2),
            (float)(diameter * textureSize.Y * zoom / project.Height / 2));
    }

    private static Vector2[] CueEllipsePoints(Vector2 centre, Vector2 radii)
    {
        const int segments = 64;
        var points = new Vector2[segments];
        for (var index = 0; index < segments; index++)
        {
            var angle = Math.Tau * index / segments;
            points[index] = centre + new Vector2((float)(Math.Cos(angle) * radii.X),
                (float)(Math.Sin(angle) * radii.Y));
        }
        return points;
    }

    private void DrawCueEllipse(Vector2 centre, Vector2 radii, Color colour,
        float width, bool dashed = false)
    {
        var points = CueEllipsePoints(centre, radii);
        for (var index = 0; index < points.Length; index++)
        {
            if (dashed && index % 4 >= 2) continue;
            DrawLine(points[index], points[(index + 1) % points.Length], colour, width, true);
        }
    }

    private void DrawDashedRectangle(Rect2 rectangle, Color colour)
    {
        DrawDashedLine(rectangle.Position, new Vector2(rectangle.End.X, rectangle.Position.Y), colour);
        DrawDashedLine(new Vector2(rectangle.End.X, rectangle.Position.Y), rectangle.End, colour);
        DrawDashedLine(rectangle.End, new Vector2(rectangle.Position.X, rectangle.End.Y), colour);
        DrawDashedLine(new Vector2(rectangle.Position.X, rectangle.End.Y), rectangle.Position, colour);
    }

    private void DrawDashedLine(Vector2 from, Vector2 to, Color colour)
    {
        var length = from.DistanceTo(to);
        if (length <= 0) return;
        var direction = (to - from) / length;
        for (var offset = 0f; offset < length; offset += 8)
            DrawLine(from + direction * offset, from + direction * Math.Min(length, offset + 4), colour, 1);
    }

    private static Color TextureSwatch(string identity, float opacity)
    {
        if (identity.Length < 6) return new Color(0.72f, 0.72f, 0.72f, opacity);
        var red = Convert.ToByte(identity[..2], 16) / 255f;
        var green = Convert.ToByte(identity.Substring(2, 2), 16) / 255f;
        var blue = Convert.ToByte(identity.Substring(4, 2), 16) / 255f;
        var pale = new Color((red + 1) * 0.5f, (green + 1) * 0.5f, (blue + 1) * 0.5f, opacity);
        return pale;
    }

    private Vector2 CurrentTextureSize() => _terrainTracerTexture.IsValid
        ? new Vector2(_terrainTracerSize.X, _terrainTracerSize.Y)
        : _gpuTerrainTexture.IsValid
        ? new Vector2(_gpuTerrainSize.X, _gpuTerrainSize.Y)
        : _gpuTexture.IsValid
        ? new Vector2(_gpuTextureSize.X, _gpuTextureSize.Y)
        : _mapTiles.Count > 0
            ? new Vector2(_tileCanvasSize.X, _tileCanvasSize.Y)
        : _mapTexture is not null
            ? new Vector2(_mapTexture.GetWidth(), _mapTexture.GetHeight())
            : _scopeEvidenceProject is null
                ? Vector2.Zero
                : new Vector2((float)_scopeEvidenceProject.Width, (float)_scopeEvidenceProject.Height);

    private void PaintAt(Vector2 canvasPosition, float pressure = 1f)
    {
        if (_brushSurface is null) return;
        var mapPosition = (canvasPosition - _pan) / _zoom;
        if (mapPosition.X < 0 || mapPosition.Y < 0 ||
            mapPosition.X >= _gpuTextureSize.X || mapPosition.Y >= _gpuTextureSize.Y) return;
        var effectivePressure = pressure > 0 ? Math.Clamp(pressure, 0.05f, 1f) : 1f;
        _brushSurface.PaintDab(mapPosition, 128 * MathF.Sqrt(effectivePressure),
            0.32f * effectivePressure, add: true, Stopwatch.GetTimestamp());
    }

    private void AddConnectedSample(Vector2 canvasPosition)
    {
        var point = CanvasToDocument(canvasPosition);
        if (point is null) return;
        _gestureController.AddSample(point.Value.X, point.Value.Y);
        QueueRedraw();
    }

    private async void CommitConnectedStrokeAsync(GestureCommitPlan plan)
    {
        if (_editSession is null || _connectedGraph is null || plan.SampleCount == 0) return;
        _committingStroke = true;
        _painting = false;
        try
        {
            var project = _editSession.Current;
            var command = BuildGestureCommand(plan, project);
            _connectedStatus?.Invoke("Saving — waiting for queued commands");
            var acknowledgement = await _editSession.ExecuteAsync(command);
            _gestureController.MarkCommitted(acknowledgement.Revision);
            ShowCommittedChange(_editSession.Current, acknowledgement.Invalidation);
            _connectedStatus?.Invoke($"Saved revision {acknowledgement.Revision}");
        }
        catch (Exception exception)
        {
            _gestureController.AbortPrepared();
            GD.PrintErr(exception);
            _connectedStatus?.Invoke($"Save failed — {exception.Message}");
        }
        finally
        {
            _committingStroke = false;
            QueueRedraw();
        }
    }

    /// <summary>
    /// Discarded, never-committed stroke commands for the current tools, so the first
    /// real stroke does not pay one-time Apply/serialization code preparation.
    /// </summary>
    internal IReadOnlyList<IEditCommand> RehearsalCommands(MapProject project)
    {
        var samples = ImmutableArray.Create(
            new MapPoint(project.Width * 0.5, project.Height * 0.5),
            new MapPoint(project.Width * 0.5 + 1, project.Height * 0.5));
        var commands = new List<IEditCommand>();
        foreach (var tool in _editorControls is null ? ["Texture Brush"] : new[] { "Texture Brush", "Land" })
        {
            try
            {
                commands.Add(BuildGestureCommand(new GestureCommitPlan(0, Stopwatch.GetTimestamp(), tool,
                    string.Empty, project.Revision, samples), project));
            }
            catch (Exception exception) when (exception is InvalidOperationException or InvalidDataException)
            {
                // A tool without a usable selection is simply not rehearsed.
            }
        }
        // River tool and panel edits share these command paths.
        try
        {
            if (project.River is { } river)
            {
                commands.Add(new MoveRiverPoint(CommandId.New(), project.Revision, 0, samples[0]));
                commands.Add(new SetRiverPointWidth(CommandId.New(), project.Revision, 0,
                    Math.Min(RiverLimits.MaximumWidth, river.ResolvedWidthProfile.Widths[0] + 1)));
            }
            else
            {
                commands.Add(new CreateRiver(CommandId.New(), project.Revision, River.Create(RiverId.New(),
                    project.RequireRole(TerrainRole.Foreground).Id, samples, [8d, 8d], 0.35)));
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or InvalidDataException
                                              or ArgumentException)
        {
            // A river that cannot be edited is simply not rehearsed.
        }
        return commands;
    }

    internal IEditCommand BuildGestureCommand(GestureCommitPlan plan, MapProject project)
    {
        if (plan.Samples.IsDefaultOrEmpty || plan.Samples.Any(point =>
                !double.IsFinite(point.X) || !double.IsFinite(point.Y) ||
                point.X < 0 || point.Y < 0 ||
                point.X >= project.Width || point.Y >= project.Height))
            throw new InvalidOperationException("Gesture samples must stay within finite map bounds.");
        if (_editorControls is null)
        {
            var target = project.RequireRole(TerrainRole.Foreground);
            var brush = new ResolvedBrush(new string('b', 64), 96, 0.72, 0.78, 0.8, 0.2, 0, 101, 1);
            return new AddTextureStroke(CommandId.New(), plan.BaseRevision, target.Id,
                new PaintStroke(StrokeId.New(), plan.Samples, brush, false, TerrainStrokeKind.Texture));
        }

        return plan.Tool switch
        {
            "Texture Brush" => new AddResolvedTextureStroke(CommandId.New(), plan.BaseRevision,
                _editorControls.TextureTarget,
                TexturePaintStroke.Create(StrokeId.New(), plan.Samples,
                    _editorControls.ResolveTextureBrush(unchecked((int)plan.SequenceId)), plan.Samples[0])),
            "Land" => new AddLandStroke(CommandId.New(), plan.BaseRevision, TerrainRole.Foreground,
                new LandStroke(StrokeId.New(), plan.Samples,
                    _editorControls.ResolveLandBrush(unchecked((int)plan.SequenceId)),
                    _landOperationAtGestureStart)),
            "River" => BuildRiverCommand(plan, project),
            _ => throw new InvalidOperationException($"{plan.Tool} does not create an edit command.")
        };
    }

    private IEditCommand BuildRiverCommand(GestureCommitPlan plan, MapProject project)
    {
        if (_editorControls is null) throw new InvalidOperationException("River controls are not bound.");
        if (project.River is not null)
        {
            var index = Math.Clamp(_editorControls.SelectedRiverPoint, 0, project.River.Points.Length - 1);
            return new MoveRiverPoint(CommandId.New(), plan.BaseRevision, index, plan.Samples[^1]);
        }
        var points = plan.Samples;
        if (points.Length < 2)
            throw new InvalidOperationException("A river requires two distinct map points.");
        var width = _editorControls.RiverWidth.CommittedValue;
        var river = River.Create(RiverId.New(), project.RequireRole(TerrainRole.Foreground).Id,
            points, Enumerable.Repeat(width, points.Length).ToImmutableArray(),
            _editorControls.RiverBankSoftness.CommittedValue);
        return new CreateRiver(CommandId.New(), plan.BaseRevision, river);
    }

    private MapPoint? CanvasToDocument(Vector2 canvasPosition)
    {
        if (_editSession is null) return null;
        var textureSize = CurrentTextureSize();
        return CanvasToDocument(canvasPosition, textureSize, _pan, _zoom, _editSession.Current);
    }

    internal static MapPoint? CanvasToDocument(Vector2 canvasPosition, Vector2 textureSize,
        Vector2 pan, float zoom, MapProject project)
    {
        if (!float.IsFinite(canvasPosition.X) || !float.IsFinite(canvasPosition.Y) ||
            !float.IsFinite(pan.X) || !float.IsFinite(pan.Y) ||
            !float.IsFinite(zoom) || zoom <= 0 ||
            textureSize.X <= 0 || textureSize.Y <= 0) return null;
        var imageX = ((double)canvasPosition.X - pan.X) / zoom;
        var imageY = ((double)canvasPosition.Y - pan.Y) / zoom;
        if (imageX < 0 || imageY < 0 || imageX >= textureSize.X || imageY >= textureSize.Y)
            return null;
        return new MapPoint(
            imageX * project.Width / textureSize.X,
            imageY * project.Height / textureSize.Y);
    }

    private void ApplyView(CanvasView view)
    {
        _pan = new Vector2((float)view.PanX, (float)view.PanY);
        _zoom = (float)view.Zoom;
    }

    private void OnFramePostDraw()
    {
        if (_pendingPresentedRevision is { } revision &&
            _pendingPresentedGeneration is { } generation &&
            LastDrawnRevision == revision && LastDrawnGeneration == generation)
        {
            _gestureController.MarkPresented(revision);
            _pendingPresentedRevision = null;
            _pendingPresentedGeneration = null;
        }
        // The frame that drew this generation has been submitted; older generations
        // can no longer be referenced by a queued canvas draw.
        if (_gpuTerrainTexture.IsValid && LastDrawnGeneration == _mapTextureGeneration)
            _gpuTerrain?.RetireBefore(LastDrawnGeneration);
        while (_pendingVisibleDabs.TryDequeue(out var timestamp))
            _latencies.Add(Stopwatch.GetElapsedTime(timestamp).TotalMilliseconds);
    }

    private static double Percentile(List<double> values, double percentile)
    {
        if (values.Count == 0) return 0;
        var ordered = values.Order().ToArray();
        var index = Math.Clamp((int)Math.Ceiling(percentile * ordered.Length) - 1, 0, ordered.Length - 1);
        return ordered[index];
    }
}

public sealed record BrushViewportProbeResult(
    bool Completed,
    bool LatencyGateEstablished,
    int Dabs,
    double MedianSubmissionToFrameEventMilliseconds,
    double P95SubmissionToFrameEventMilliseconds,
    double MedianFrameMilliseconds,
    double P95FrameMilliseconds,
    int FramesOver33Milliseconds,
    double MaximumFrameMilliseconds,
    int MaximumPendingSubmissions,
    string Detail);

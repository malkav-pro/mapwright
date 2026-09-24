using Godot;
using Mapwright.Domain;

namespace Mapwright.App;

public sealed class NumericFieldState
{
    public NumericFieldState(string label, double minimum, double maximum, double value,
        double step = 1, bool allowsTenths = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        if (!double.IsFinite(minimum) || !double.IsFinite(maximum) || minimum > maximum ||
            !double.IsFinite(value) || value < minimum || value > maximum || step <= 0)
            throw new ArgumentOutOfRangeException(nameof(value));
        Label = label;
        Minimum = minimum;
        Maximum = maximum;
        Step = step;
        AllowsTenths = allowsTenths;
        CommittedValue = value;
        Text = Format(value);
    }

    public string Label { get; }
    public double Minimum { get; }
    public double Maximum { get; }
    public double Step { get; }
    public bool AllowsTenths { get; }
    public double CommittedValue { get; private set; }
    public string Text { get; private set; }
    public string? Error { get; private set; }
    public bool IsValid => Error is null;

    public bool Input(string text)
    {
        Text = text;
        if (!double.TryParse(text, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var value) ||
            !double.IsFinite(value) || value < Minimum || value > Maximum)
        {
            Error = $"Enter {Minimum:0.###}–{Maximum:0.###}.";
            return false;
        }
        Error = null;
        return true;
    }

    public bool Commit()
    {
        if (!Input(Text)) return false;
        CommittedValue = double.Parse(Text, System.Globalization.CultureInfo.InvariantCulture);
        Text = Format(CommittedValue);
        return true;
    }

    public double SetFromSlider(double value)
    {
        if (!double.IsFinite(value) || value < Minimum || value > Maximum)
            throw new ArgumentOutOfRangeException(nameof(value));
        CommittedValue = value;
        Text = Format(value);
        Error = null;
        return value;
    }

    public double Adjust(bool shift, bool alt, int direction)
    {
        if (direction is not (-1 or 1)) throw new ArgumentOutOfRangeException(nameof(direction));
        var increment = shift ? 10 : alt && AllowsTenths ? 0.1 : Step;
        return SetFromSlider(Math.Clamp(CommittedValue + (increment * direction), Minimum, Maximum));
    }

    public double Escape()
    {
        Text = Format(CommittedValue);
        Error = null;
        return CommittedValue;
    }

    private static string Format(double value) => value.ToString("0.###",
        System.Globalization.CultureInfo.InvariantCulture);
}

public sealed class EditorControlsState
{
    private string _selectedTextureIdentity = "";

    public EditorControlsState()
    {
        TextureDiameter = new NumericFieldState("Diameter (map units)", TextureBrushLimits.MinimumDiameter,
            TextureBrushLimits.MaximumDiameter, 96);
        TextureHardness = new NumericFieldState("Hardness", 0, 1, 0.72, 0.1, true);
        TextureOpacity = new NumericFieldState("Brush opacity", 0, 1, 0.86, 0.1, true);
        TextureFlow = new NumericFieldState("Flow", 0, 1, 0.8, 0.1, true);
        TextureSpacing = new NumericFieldState("Spacing", TextureBrushLimits.MinimumSpacing,
            TextureBrushLimits.MaximumSpacing, 0.2, 0.1, true);
        TextureScale = new NumericFieldState("Texture scale", TextureBrushLimits.MinimumTextureScale,
            TextureBrushLimits.MaximumTextureScale, 1, 0.1, true);
        TextureRotation = new NumericFieldState("Rotation", TextureBrushLimits.MinimumRotationDegrees,
            TextureBrushLimits.MaximumRotationDegrees, 0);
        TextureJitter = new NumericFieldState("Jitter", 0, 1, 0, 0.1, true);
        TextureRoughness = new NumericFieldState("Roughness", 0, 1, 0.4, 0.1, true);
        TextureSmooth = new NumericFieldState("Smooth corners", 0, 1, 0.5, 0.1, true);
        LandDiameter = new NumericFieldState("Diameter (map units)", LandBrushLimits.MinimumDiameter,
            LandBrushLimits.MaximumDiameter, 96);
        LandRoughness = new NumericFieldState("Roughness", 0, 1, 0.4, 0.1, true);
        LandSmooth = new NumericFieldState("Smooth corners", 0, 1, 0.5, 0.1, true);
        LandSoftness = new NumericFieldState("Softness", 0, 1, 0.35, 0.1, true);
        RiverWidth = new NumericFieldState("Point width (map units)", RiverLimits.MinimumWidth,
            RiverLimits.MaximumWidth, 96);
        RiverBankSoftness = new NumericFieldState("Bank softness", RiverLimits.MinimumBankSoftness,
            RiverLimits.MaximumBankSoftness, 0.35, 0.1, true);
    }

    public string ActiveTool { get; private set; } = "Pan";
    public TerrainRole TextureTarget { get; private set; } = TerrainRole.Foreground;
    public string ActiveTarget => ActiveTool switch
    {
        "Land" => "Foreground’s land mask · Coverage",
        "River" => "Foreground land mask · River modifier",
        "Texture Brush" => $"{TextureTarget} · Texture colour/intensity",
        "Sample Texture" => "Canvas texture identity",
        "Project Storage" => "Native project and disposable caches",
        _ => "Canvas"
    };

    public string SelectedPresetId { get; private set; } = "hard-round";
    public TextureTipKind TextureTip { get; private set; } = TextureTipKind.Round;
    public TextureTaperKind TextureTaper { get; private set; } = TextureTaperKind.None;
    public bool TextureMissing { get; private set; }
    public string SelectedTextureIdentity => _selectedTextureIdentity;
    public LandShape LandShape { get; private set; } = LandShape.EdgedPolygon;
    public LandOperation LandOperation { get; private set; } = LandOperation.Add;
    public int SelectedRiverPoint { get; set; }

    public NumericFieldState TextureDiameter { get; }
    public NumericFieldState TextureHardness { get; }
    public NumericFieldState TextureOpacity { get; }
    public NumericFieldState TextureFlow { get; }
    public NumericFieldState TextureSpacing { get; }
    public NumericFieldState TextureScale { get; }
    public NumericFieldState TextureRotation { get; }
    public NumericFieldState TextureJitter { get; }
    public NumericFieldState TextureRoughness { get; }
    public NumericFieldState TextureSmooth { get; }
    public NumericFieldState LandDiameter { get; }
    public NumericFieldState LandRoughness { get; }
    public NumericFieldState LandSmooth { get; }
    public NumericFieldState LandSoftness { get; }
    public NumericFieldState RiverWidth { get; }
    public NumericFieldState RiverBankSoftness { get; }

    public void SelectTool(string tool)
    {
        if (!EditorShellContract.Tools.Contains(tool, StringComparer.Ordinal))
            throw new ArgumentOutOfRangeException(nameof(tool));
        ActiveTool = tool;
    }

    public void SelectTextureTarget(TerrainRole role)
    {
        if (!Enum.IsDefined(role)) throw new ArgumentOutOfRangeException(nameof(role));
        TextureTarget = role;
    }

    public void SelectPreset(string id)
    {
        var preset = TexturePresetCatalog.Inventory.Single(item => item.Id == id);
        SelectedPresetId = preset.Id;
        TextureTip = preset.Tip;
        TextureTaper = preset.Taper;
        TextureHardness.SetFromSlider(preset.Hardness);
        TextureSpacing.SetFromSlider(preset.Spacing);
        TextureRoughness.SetFromSlider(preset.Roughness);
        TextureSmooth.SetFromSlider(preset.CornerSmoothing);
    }

    public void SelectTextureTip(TextureTipKind tip) => TextureTip = tip;
    public void SetLandShape(LandShape shape) => LandShape = shape;
    public void SetLandOperation(LandOperation operation) => LandOperation = operation;

    public void SetTextureIdentity(string? sha256, bool missing = false)
    {
        _selectedTextureIdentity = sha256 ?? "";
        TextureMissing = missing;
    }

    public ResolvedTextureBrush ResolveTextureBrush(int seed)
    {
        if (_selectedTextureIdentity.Length != 64 || _selectedTextureIdentity.Any(character => !Uri.IsHexDigit(character)))
            throw new InvalidOperationException("No usable local texture is selected.");
        return ResolvedTextureBrush.FromDiameter(SelectedPresetId, TextureTip,
            TextureDiameter.CommittedValue, TextureHardness.CommittedValue,
            TextureOpacity.CommittedValue, TextureFlow.CommittedValue,
            TextureSpacing.CommittedValue, _selectedTextureIdentity,
            TextureScale.CommittedValue, TextureRotation.CommittedValue,
            TextureJitter.CommittedValue,
            TextureTip == TextureTipKind.Edged ? TextureRoughness.CommittedValue : 0,
            TextureTip == TextureTipKind.Edged ? TextureSmooth.CommittedValue : 0,
            TextureTaper, seed);
    }

    public ResolvedLandBrush ResolveLandBrush(int seed) => ResolvedLandBrush.FromDiameter(
        LandShape, LandDiameter.CommittedValue,
        LandShape == LandShape.EdgedPolygon ? LandRoughness.CommittedValue : 0,
        LandShape == LandShape.EdgedPolygon ? LandSmooth.CommittedValue : 0,
        LandShape == LandShape.RoundSoft ? LandSoftness.CommittedValue : 0, seed);

    public TargetAvailability TargetAvailability(MapProject project)
    {
        var role = ActiveTool == "Texture Brush" ? TextureTarget : TerrainRole.Foreground;
        var layer = project.RequireRole(role);
        if (layer.Locked) return new TargetAvailability(false,
            $"{role} is locked. Unlock {role} to paint it.", "Unlock", role);
        if (!project.IsTerrainEffectivelyVisible(role)) return new TargetAvailability(false,
            $"{role} is hidden. Show {role} to paint it.", "Show layer", role);
        return new TargetAvailability(true, $"{role} is paintable.", null, role);
    }
}

public sealed record TargetAvailability(bool Paintable, string Message, string? Action, TerrainRole Role);

public enum EditingScope
{
    Coverage,
    Texture,
    LayerOpacity
}

public sealed record ScopeCueDescriptor(
    EditingScope Scope,
    string ActiveTool,
    string Target,
    string AffectedProperty,
    string Readout,
    bool FilledFootprint,
    bool OuterRing,
    bool FalloffRing,
    bool AffectedRegionBox,
    bool Swatch,
    bool RowOnly);

public static class ScopeCueContract
{
    public static bool UseCrosshair(double screenDiameter) =>
        double.IsFinite(screenDiameter) && screenDiameter < 6;

    public static ScopeCueDescriptor ForTool(EditorControlsState state)
    {
        if (state.ActiveTool == "Land")
        {
            var mode = state.LandShape == LandShape.EdgedPolygon ? "Edged polygon" : "Round soft";
            return new ScopeCueDescriptor(EditingScope.Coverage, "Land tool", "Foreground",
                "Coverage", $"Coverage · Land tool · {state.LandOperation} · Foreground · ⌀ {state.LandDiameter.CommittedValue:0.#} map units · {mode}",
                true, true, false, false, false, false);
        }
        if (state.ActiveTool == "Texture Brush")
        {
            var identity = string.IsNullOrWhiteSpace(state.SelectedTextureIdentity)
                ? "No texture"
                : state.SelectedPresetId + " · " + state.SelectedTextureIdentity[..Math.Min(8, state.SelectedTextureIdentity.Length)];
            return new ScopeCueDescriptor(EditingScope.Texture, "Texture Brush", state.TextureTarget.ToString(),
                "Texture colour/intensity",
                $"Texture colour/intensity · Texture Brush · {state.TextureTarget} · ⌀ {state.TextureDiameter.CommittedValue:0.#} map units · hardness {state.TextureHardness.CommittedValue:P0} · rotation {state.TextureRotation.CommittedValue:0.#}° · {identity}",
                false, true, true, true, true, false);
        }
        throw new InvalidOperationException("The active tool has no D-28 brush scope cue.");
    }

    public static ScopeCueDescriptor LayerOpacity(TerrainRole role, double opacity)
    {
        if (!double.IsFinite(opacity) || opacity is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(opacity));
        return new ScopeCueDescriptor(EditingScope.LayerOpacity, "Layers", role.ToString(),
            "Layer opacity", $"Layer opacity · {role} · {opacity:P0}",
            false, false, false, false, false, true);
    }
}

public static class EditorControls
{
    public static void Populate(
        VBoxContainer host,
        EditorControlsState state,
        MapProject project,
        Action refresh,
        Action<Func<MapProject, IEditCommand>> execute,
        Action<string> status)
    {
        ArgumentNullException.ThrowIfNull(host);
        host.AddChild(Label(state.ActiveTool, "PanelTitleLabel"));
        switch (state.ActiveTool)
        {
            case "Texture Brush": BuildTexture(host, state, project, refresh, execute, status); break;
            case "Land": BuildLand(host, state, project, refresh, execute, status); break;
            case "River": BuildRiver(host, state, project, refresh, execute, status); break;
            default: host.AddChild(Label(MainToolCopy(state.ActiveTool), "DenseLabel", true)); break;
        }
    }

    private static void BuildTexture(VBoxContainer host, EditorControlsState state, MapProject project,
        Action refresh, Action<Func<MapProject, IEditCommand>> execute, Action<string> status)
    {
        host.AddChild(Label("Painting on " + state.TextureTarget +
            " · texture painting does not change land coverage.", "SectionLabel", true));
        var targets = new HBoxContainer();
        foreach (var role in new[] { TerrainRole.Background, TerrainRole.Foreground })
        {
            var button = Button(role.ToString(), $"Paint texture on {role}.");
            button.ToggleMode = true;
            button.ButtonPressed = state.TextureTarget == role;
            button.Pressed += () => { state.SelectTextureTarget(role); refresh(); };
            targets.AddChild(button);
        }
        host.AddChild(targets);
        AddTargetBlock(host, state, project, execute, refresh);

        host.AddChild(Label("Brush presets · tip and stroke preview", "SectionLabel"));
        foreach (var preset in TexturePresetCatalog.Inventory)
        {
            var tapered = preset.Taper == TextureTaperKind.SmoothBothEnds ? " · tapered" : "";
            var button = Button($"{preset.Id} · {preset.Tip} tip ▸ stroke preview{tapered}",
                $"Select {preset.Id} resolved preset.");
            button.Pressed += () => { state.SelectPreset(preset.Id); refresh(); };
            host.AddChild(button);
        }
        host.AddChild(Button("Save preset as…", "Save these resolved settings as a local preset."));
        var tips = new HBoxContainer();
        foreach (var tip in new[] { TextureTipKind.Round, TextureTipKind.Edged })
        {
            var button = Button(tip.ToString(), $"Use {tip} texture tip.");
            button.Pressed += () => { state.SelectTextureTip(tip); refresh(); };
            tips.AddChild(button);
        }
        host.AddChild(tips);
        AddNumeric(host, state.TextureDiameter, _ => refresh());
        AddNumeric(host, state.TextureHardness, _ => refresh());
        AddNumeric(host, state.TextureOpacity, _ => refresh());
        AddNumeric(host, state.TextureFlow, _ => refresh());
        AddNumeric(host, state.TextureSpacing, _ => refresh());
        if (state.TextureTip == TextureTipKind.Edged)
        {
            AddNumeric(host, state.TextureRoughness, _ => refresh());
            AddNumeric(host, state.TextureSmooth, _ => refresh());
        }

        host.AddChild(Label("Local texture", "SectionLabel"));
        if (string.IsNullOrWhiteSpace(state.SelectedTextureIdentity))
        {
            host.AddChild(Label("No local textures available\nLocate a texture file to make it available in this project. Painting stays disabled until a usable texture is selected.",
                "FailureLabel", true));
            host.AddChild(Button("Locate texture…", "Locate a local texture file."));
        }
        else
        {
            var marker = state.TextureMissing ? "MISSING · marked placeholder" : "Selected texture";
            host.AddChild(Label($"{marker}\n{state.SelectedTextureIdentity}", "MonoLabel", true));
            if (state.TextureMissing)
                host.AddChild(Label("Strokes keep their recorded identity until it is found.", "FailureLabel", true));
            host.AddChild(Button("Locate…", "Locate the recorded texture identity."));
            host.AddChild(Button("Choose another texture", "Choose another local texture."));
        }
        AddNumeric(host, state.TextureScale, _ => refresh());
        AddNumeric(host, state.TextureRotation, _ => refresh());
        AddNumeric(host, state.TextureJitter, _ => refresh());
        if (state.TextureTaper == TextureTaperKind.SmoothBothEnds)
            host.AddChild(Label("Tapered · narrows automatically at both stroke ends", "DenseLabel", true));
    }

    private static void BuildLand(VBoxContainer host, EditorControlsState state, MapProject project,
        Action refresh, Action<Func<MapProject, IEditCommand>> execute, Action<string> status)
    {
        host.AddChild(Label("Fixed target · Foreground’s land mask", "SectionLabel", true));
        host.AddChild(Label("Coverage changes coastline and reveals Background. It does not paint texture and is not Layer opacity.",
            "DenseLabel", true));
        AddTargetBlock(host, state, project, execute, refresh);
        var modes = new HBoxContainer();
        foreach (var shape in new[] { LandShape.EdgedPolygon, LandShape.RoundSoft })
        {
            var text = shape == LandShape.EdgedPolygon ? "Edged polygon" : "Round soft";
            var button = Button(text, $"Use {text} Land footprint.");
            button.Pressed += () => { state.SetLandShape(shape); refresh(); };
            modes.AddChild(button);
        }
        host.AddChild(modes);
        var operations = new HBoxContainer();
        foreach (var operation in new[] { LandOperation.Add, LandOperation.Subtract })
        {
            var button = Button(operation.ToString(), $"{operation} Foreground coverage. Hold Alt to invert while drawing.");
            button.Pressed += () => { state.SetLandOperation(operation); refresh(); };
            operations.AddChild(button);
        }
        host.AddChild(operations);
        host.AddChild(Label("Hold Alt while drawing to invert Add/Subtract.", "CaptionLabel", true));
        AddNumeric(host, state.LandDiameter, _ => refresh());
        if (state.LandShape == LandShape.EdgedPolygon)
        {
            AddNumeric(host, state.LandRoughness, _ => refresh());
            AddNumeric(host, state.LandSmooth, _ => refresh());
        }
        else AddNumeric(host, state.LandSoftness, _ => refresh());
    }

    private static void BuildRiver(VBoxContainer host, EditorControlsState state, MapProject project,
        Action refresh, Action<Func<MapProject, IEditCommand>> execute, Action<string> status)
    {
        host.AddChild(Label("Modifier of Foreground land mask", "SectionLabel", true));
        AddTargetBlock(host, state, project, execute, refresh);
        if (project.River is null)
        {
            host.AddChild(Label("Draw a centreline to create the first editable river.", "DenseLabel", true));
            AddNumeric(host, state.RiverWidth, _ => refresh());
            AddNumeric(host, state.RiverBankSoftness, _ => refresh());
            return;
        }
        var river = project.River;
        host.AddChild(Label($"Centreline · {river.Points.Length} points · selected point {state.SelectedRiverPoint + 1}\nWidth handles use the stored per-point profile.",
            "MonoLabel", true));
        var enabled = new CheckButton { Text = "River enabled", ButtonPressed = river.Enabled };
        enabled.Toggled += value => execute(snapshot =>
            new SetRiverEnabled(CommandId.New(), snapshot.Revision, value));
        host.AddChild(enabled);
        AddNumeric(host, state.RiverWidth, value => execute(snapshot =>
            new SetRiverPointWidth(CommandId.New(), snapshot.Revision,
                Math.Clamp(state.SelectedRiverPoint, 0, snapshot.River!.Points.Length - 1), value)));
        AddNumeric(host, state.RiverBankSoftness, value => execute(snapshot =>
            new SetRiverBankSoftness(CommandId.New(), snapshot.Revision, value)));
        var apply = Button("Apply width to all", "Apply the selected width to every centreline point.");
        apply.Pressed += () => execute(snapshot =>
            new SetAllRiverWidths(CommandId.New(), snapshot.Revision, state.RiverWidth.CommittedValue));
        host.AddChild(apply);
        var deletePoint = Button("Delete point", "Delete the selected point as one undoable command.");
        deletePoint.Disabled = river.Points.Length <= 2;
        deletePoint.Pressed += () => execute(snapshot => new DeleteRiverPoint(CommandId.New(), snapshot.Revision,
            Math.Clamp(state.SelectedRiverPoint, 0, snapshot.River!.Points.Length - 1)));
        host.AddChild(deletePoint);
        var deleteRiver = Button("Delete river", "Delete this river modifier as one undoable command.");
        deleteRiver.Pressed += () =>
        {
            execute(snapshot => new DeleteRiver(CommandId.New(), snapshot.Revision));
            status("River deleted — Undo available");
        };
        host.AddChild(deleteRiver);
    }

    private static void AddTargetBlock(VBoxContainer host, EditorControlsState state, MapProject project,
        Action<Func<MapProject, IEditCommand>> execute, Action refresh)
    {
        var availability = state.TargetAvailability(project);
        if (availability.Paintable) return;
        host.AddChild(Label(availability.Message, "FailureLabel", true));
        var action = Button(availability.Action!, availability.Message);
        action.Pressed += () =>
        {
            if (availability.Action == "Unlock")
                execute(snapshot => new SetTerrainLock(CommandId.New(), snapshot.Revision,
                    availability.Role, false));
            else
                execute(snapshot => new SetTerrainVisibility(CommandId.New(), snapshot.Revision,
                    availability.Role, true));
            refresh();
        };
        host.AddChild(action);
    }

    private static void AddNumeric(VBoxContainer host, NumericFieldState state, Action<double> committed)
    {
        var row = new HBoxContainer();
        row.AddChild(Label(state.Label, "DenseLabel"));
        var slider = new HSlider
        {
            MinValue = state.Minimum,
            MaxValue = state.Maximum,
            Step = state.AllowsTenths ? 0.1 : state.Step,
            Value = state.CommittedValue,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            TooltipText = $"{state.Label}: {state.Minimum:0.###}–{state.Maximum:0.###}"
        };
        var field = new LineEdit
        {
            Text = state.Text,
            CustomMinimumSize = new Vector2(76, 26),
            TooltipText = slider.TooltipText
        };
        var error = Label("", "FailureLabel", true);
        error.Visible = false;
        slider.ValueChanged += value => field.Text = state.SetFromSlider(value).ToString("0.###",
            System.Globalization.CultureInfo.InvariantCulture);
        slider.DragEnded += changed => { if (changed) committed(state.CommittedValue); };
        field.TextChanged += text =>
        {
            state.Input(text);
            error.Text = state.Error ?? "";
            error.Visible = state.Error is not null;
        };
        field.TextSubmitted += _ =>
        {
            if (state.Commit())
            {
                field.Text = state.Text;
                error.Visible = false;
                committed(state.CommittedValue);
            }
        };
        field.GuiInput += input =>
        {
            if (input is not InputEventKey key || !key.Pressed) return;
            if (key.Keycode == Key.Escape)
            {
                field.Text = state.Escape().ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
                error.Visible = false;
                field.AcceptEvent();
            }
            else if (key.Keycode is Key.Up or Key.Down)
            {
                var value = state.Adjust(key.ShiftPressed, key.AltPressed,
                    key.Keycode == Key.Up ? 1 : -1);
                slider.Value = value;
                field.Text = state.Text;
                committed(value);
                field.AcceptEvent();
            }
        };
        row.AddChild(slider);
        row.AddChild(field);
        host.AddChild(row);
        host.AddChild(error);
    }

    private static Label Label(string text, string variation, bool wrap = false) => new()
    {
        Text = text,
        ThemeTypeVariation = variation,
        AutowrapMode = wrap ? TextServer.AutowrapMode.WordSmart : TextServer.AutowrapMode.Off,
        SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        TooltipText = text
    };

    private static Button Button(string text, string tooltip) => new()
    {
        Text = text,
        TooltipText = tooltip,
        CustomMinimumSize = new Vector2(Math.Clamp(text.Length * 7 + 20, 64, 160), 30),
        ClipText = true,
        TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
        FocusMode = Control.FocusModeEnum.All
    };

    private static string MainToolCopy(string tool) => tool switch
    {
        "Pan" => "Space+drag or middle drag temporarily pans. Plain wheel zooms about the pointer.",
        "Sample Texture" => "Pick a local texture identity from the canvas.",
        "Project Storage" => "Inspect local project storage and disposable caches.",
        _ => tool
    };
}

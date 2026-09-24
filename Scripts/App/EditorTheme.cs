using Godot;

namespace Mapwright.App;

/// <summary>
/// Offline Phase 1 visual system. Product surfaces use only these semantic tokens;
/// map colours remain document content and never acquire status meaning here.
/// </summary>
public static class EditorTheme
{
    public const string SansPath = "res://Assets/UI/Fonts/IBMPlexSans-Variable.ttf";
    public const string MonoPath = "res://Assets/UI/Fonts/IBMPlexMono-Variable.ttf";
    public const string DisplayPath = "res://Assets/UI/Fonts/Spectral-SemiBold.ttf";

    public const int CaptionSize = 11;
    public const int DenseSize = 12;
    public const int BodySize = 13;
    public const int DisplaySize = 24;

    public static readonly Color AppGround = new("0d0f16");
    public static readonly Color AppBar = new("12141d");
    public static readonly Color Panel = new("161923");
    public static readonly Color PanelHeader = new("1d2130");
    public static readonly Color Control = new("252a3a");
    public static readonly Color Hairline = new("2f3447");
    public static readonly Color ControlBorder = new("3b4159");
    public static readonly Color PrimaryText = new("ecedf3");
    public static readonly Color SecondaryText = new("adb3c6");
    public static readonly Color MutedText = new("8990a6");
    public static readonly Color Accent = new("5c6fe0");
    public static readonly Color SelectedFill = new("23294a");
    public static readonly Color SelectedText = new("c6cfff");
    public static readonly Color FocusRing = new("93a6ff");
    public static readonly Color Durable = new("5fae7f");
    public static readonly Color Pending = new("d9a23f");
    public static readonly Color Failure = new("e2685a");
    public static readonly Color Working = new("9d7be0");

    public static Theme Create(float uiScale = 1f)
    {
        if (!float.IsFinite(uiScale) || uiScale <= 0f)
            throw new ArgumentOutOfRangeException(nameof(uiScale));

        var sans = LoadFont(SansPath);
        var mono = LoadFont(MonoPath);
        var display = LoadFont(DisplayPath);
        var regular = Vary(sans, 400);
        var semibold = Vary(sans, 600);

        var theme = new Theme
        {
            DefaultBaseScale = uiScale,
            DefaultFont = regular,
            DefaultFontSize = BodySize
        };

        SetType(theme, "Label", regular, BodySize, PrimaryText);
        SetType(theme, "Button", semibold, DenseSize, PrimaryText);
        SetType(theme, "LineEdit", mono, DenseSize, PrimaryText);
        SetType(theme, "SpinBox", mono, DenseSize, PrimaryText);
        SetType(theme, "OptionButton", semibold, DenseSize, PrimaryText);
        SetType(theme, "CheckButton", regular, DenseSize, PrimaryText);
        SetType(theme, "TabBar", semibold, DenseSize, PrimaryText);
        SetType(theme, "Tree", regular, DenseSize, PrimaryText);
        SetType(theme, "ItemList", regular, DenseSize, PrimaryText);
        SetType(theme, "TextEdit", mono, DenseSize, PrimaryText);

        theme.SetTypeVariation("CaptionLabel", "Label");
        SetType(theme, "CaptionLabel", regular, CaptionSize, MutedText);
        theme.SetTypeVariation("SectionLabel", "Label");
        SetType(theme, "SectionLabel", semibold, CaptionSize, SecondaryText);
        theme.SetTypeVariation("DenseLabel", "Label");
        SetType(theme, "DenseLabel", regular, DenseSize, PrimaryText);
        theme.SetTypeVariation("MonoLabel", "Label");
        SetType(theme, "MonoLabel", mono, DenseSize, PrimaryText);
        theme.SetTypeVariation("DisplayLabel", "Label");
        SetType(theme, "DisplayLabel", display, DisplaySize, PrimaryText);
        theme.SetTypeVariation("PanelTitleLabel", "Label");
        SetType(theme, "PanelTitleLabel", semibold, BodySize, PrimaryText);
        theme.SetTypeVariation("DurableLabel", "Label");
        SetType(theme, "DurableLabel", semibold, CaptionSize, Durable);
        theme.SetTypeVariation("PendingLabel", "Label");
        SetType(theme, "PendingLabel", semibold, CaptionSize, Pending);
        theme.SetTypeVariation("WorkingLabel", "Label");
        SetType(theme, "WorkingLabel", semibold, CaptionSize, Working);
        theme.SetTypeVariation("FailureLabel", "Label");
        SetType(theme, "FailureLabel", semibold, CaptionSize, Failure);

        ConfigureButtons(theme);
        ConfigureFields(theme);
        ConfigurePanels(theme);
        ConfigureLists(theme);
        ConfigureProgress(theme);
        return theme;
    }

    public static StyleBoxFlat Box(
        Color background,
        Color border,
        int borderWidth = 1,
        int radius = 3,
        int horizontalPadding = 8,
        int verticalPadding = 4)
    {
        return new StyleBoxFlat
        {
            BgColor = background,
            BorderColor = border,
            BorderWidthLeft = borderWidth,
            BorderWidthTop = borderWidth,
            BorderWidthRight = borderWidth,
            BorderWidthBottom = borderWidth,
            CornerRadiusTopLeft = radius,
            CornerRadiusTopRight = radius,
            CornerRadiusBottomLeft = radius,
            CornerRadiusBottomRight = radius,
            ContentMarginLeft = horizontalPadding,
            ContentMarginRight = horizontalPadding,
            ContentMarginTop = verticalPadding,
            ContentMarginBottom = verticalPadding
        };
    }

    private static FontFile LoadFont(string path)
    {
        // Load repository-owned font bytes directly so startup is deterministic even in
        // headless builds that do not perform an editor import scan first.
        var direct = new FontFile();
        var result = direct.LoadDynamicFont(ProjectSettings.GlobalizePath(path));
        if (result != Error.Ok)
            throw new FileNotFoundException($"Required offline font could not be loaded ({result}): {path}");
        return direct;
    }

    private static FontVariation Vary(Font font, double weight)
    {
        var coordinates = new Godot.Collections.Dictionary
        {
            ["wght"] = weight
        };
        return new FontVariation { BaseFont = font, VariationOpentype = coordinates };
    }

    private static void SetType(Theme theme, string type, Font font, int size, Color colour)
    {
        theme.SetFont("font", type, font);
        theme.SetFontSize("font_size", type, size);
        theme.SetColor("font_color", type, colour);
    }

    private static void ConfigureButtons(Theme theme)
    {
        theme.SetStylebox("normal", "Button", Box(Control, ControlBorder));
        theme.SetStylebox("hover", "Button", Box(PanelHeader, Accent));
        theme.SetStylebox("pressed", "Button", Box(SelectedFill, Accent, 2));
        theme.SetStylebox("focus", "Button", FocusBox());
        theme.SetStylebox("disabled", "Button", Box(Panel, Hairline));
        theme.SetColor("font_disabled_color", "Button", MutedText);
        theme.SetColor("font_pressed_color", "Button", SelectedText);
        theme.SetColor("font_hover_color", "Button", PrimaryText);
        theme.SetConstant("outline_size", "Button", 0);

        theme.SetTypeVariation("PrimaryButton", "Button");
        theme.SetStylebox("normal", "PrimaryButton", Box(Accent, Accent, 1));
        theme.SetStylebox("hover", "PrimaryButton", Box(new Color("6d80ed"), FocusRing, 1));
        theme.SetStylebox("pressed", "PrimaryButton", Box(new Color("4659c7"), FocusRing, 2));
        theme.SetStylebox("focus", "PrimaryButton", FocusBox());
        theme.SetStylebox("disabled", "PrimaryButton", Box(PanelHeader, Hairline));

        theme.SetTypeVariation("ToolButton", "Button");
        theme.SetStylebox("normal", "ToolButton", Box(AppBar, AppBar, 0));
        theme.SetStylebox("hover", "ToolButton", Box(PanelHeader, ControlBorder));
        theme.SetStylebox("pressed", "ToolButton", Box(SelectedFill, Accent, 2));
        theme.SetStylebox("focus", "ToolButton", FocusBox(40));
        theme.SetConstant("icon_max_width", "ToolButton", 20);
    }

    private static void ConfigureFields(Theme theme)
    {
        theme.SetStylebox("normal", "LineEdit", Box(AppGround, ControlBorder));
        theme.SetStylebox("focus", "LineEdit", FocusBox());
        theme.SetStylebox("read_only", "LineEdit", Box(Panel, Hairline));
        theme.SetColor("font_uneditable_color", "LineEdit", MutedText);
        theme.SetColor("caret_color", "LineEdit", FocusRing);
        theme.SetColor("selection_color", "LineEdit", SelectedFill);

        theme.SetStylebox("normal", "TextEdit", Box(AppGround, ControlBorder, 1, 3, 8, 8));
        theme.SetStylebox("focus", "TextEdit", FocusBox());
        theme.SetStylebox("read_only", "TextEdit", Box(AppGround, Hairline, 1, 3, 8, 8));

        theme.SetStylebox("slider", "HSlider", Box(Hairline, Hairline, 0, 2, 0, 0));
        theme.SetStylebox("grabber_area", "HSlider", Box(Accent, Accent, 0, 2, 0, 0));
        theme.SetStylebox("grabber_area_highlight", "HSlider", Box(FocusRing, FocusRing, 0, 2, 0, 0));
    }

    private static void ConfigurePanels(Theme theme)
    {
        theme.SetStylebox("panel", "Panel", Box(Panel, Hairline, 1, 5, 0, 0));
        theme.SetStylebox("panel", "PanelContainer", Box(Panel, Hairline, 1, 5, 8, 8));
        theme.SetStylebox("panel", "PopupPanel", Box(Panel, ControlBorder, 1, 5, 16, 16));
        theme.SetStylebox("panel", "TooltipPanel", Box(PanelHeader, FocusRing, 1, 3, 8, 4));
        theme.SetColor("font_color", "TooltipLabel", PrimaryText);
        theme.SetFontSize("font_size", "TooltipLabel", CaptionSize);
        theme.SetConstant("separation", "VBoxContainer", 8);
        theme.SetConstant("separation", "HBoxContainer", 8);
        theme.SetConstant("separation", "GridContainer", 8);
        theme.SetConstant("separation", "VSplitContainer", 4);
        theme.SetConstant("separation", "HSplitContainer", 4);
    }

    private static void ConfigureLists(Theme theme)
    {
        theme.SetStylebox("panel", "ItemList", Box(AppGround, Hairline, 1, 3, 0, 0));
        theme.SetStylebox("focus", "ItemList", FocusBox());
        theme.SetStylebox("selected", "ItemList", Box(SelectedFill, Accent, 2, 1, 4, 2));
        theme.SetStylebox("selected_focus", "ItemList", Box(SelectedFill, FocusRing, 2, 1, 4, 2));
        theme.SetStylebox("panel", "Tree", Box(AppGround, Hairline, 1, 3, 0, 0));
        theme.SetStylebox("focus", "Tree", FocusBox());
        theme.SetStylebox("selected", "Tree", Box(SelectedFill, Accent, 2, 1, 4, 2));
    }

    private static void ConfigureProgress(Theme theme)
    {
        theme.SetStylebox("background", "ProgressBar", Box(AppGround, Hairline, 1, 2, 0, 0));
        theme.SetStylebox("fill", "ProgressBar", Box(Working, Working, 0, 2, 0, 0));
        theme.SetColor("font_color", "ProgressBar", PrimaryText);
        theme.SetFontSize("font_size", "ProgressBar", CaptionSize);
    }

    private static StyleBoxFlat FocusBox(int minimumHeight = 26)
    {
        var focus = Box(new Color(0, 0, 0, 0), FocusRing, 2, 5, 8, 4);
        focus.ExpandMarginLeft = 2;
        focus.ExpandMarginTop = 2;
        focus.ExpandMarginRight = 2;
        focus.ExpandMarginBottom = 2;
        focus.ContentMarginTop = Math.Max(focus.ContentMarginTop, minimumHeight / 2f - 8f);
        focus.ContentMarginBottom = Math.Max(focus.ContentMarginBottom, minimumHeight / 2f - 8f);
        return focus;
    }
}

public sealed class ThemeSmokeConnectedCaseProvider : IConnectedCaseProvider
{
    public void Register(ConnectedCaseRegistry registry) =>
        registry.Register("ThemeSmoke", ThemeSmoke.RunAsync);
}

public static class ThemeSmoke
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

        foreach (var path in new[] { EditorTheme.SansPath, EditorTheme.MonoPath, EditorTheme.DisplayPath })
        {
            var local = ProjectSettings.GlobalizePath(path);
            Check(File.Exists(local), $"Offline font is missing: {path}");
            Check(new FileInfo(local).Length > 100_000, $"Offline font is unexpectedly small: {path}");
            var direct = new FontFile();
            Check(direct.LoadDynamicFont(local) == Error.Ok, $"Offline font did not load: {path}");
        }

        var theme = EditorTheme.Create();
        Check(theme.DefaultFont is not null, "Theme has no default offline font.");
        Check(theme.DefaultFontSize == EditorTheme.BodySize, "Theme body size is not 13px.");
        Check(new[] { EditorTheme.CaptionSize, EditorTheme.DenseSize, EditorTheme.BodySize, EditorTheme.DisplaySize }
                .Min() >= 11,
            "A theme text role dropped below the 11px minimum.");
        Check(theme.HasStylebox("normal", "Button") && theme.HasStylebox("disabled", "Button"),
            "Button normal/disabled states are incomplete.");
        Check(theme.HasStylebox("pressed", "ToolButton") && theme.HasStylebox("focus", "ToolButton"),
            "Tool selected/focus states are incomplete.");
        Check(theme.GetStylebox("focus", "Button") is StyleBoxFlat focus &&
              focus.BorderWidthLeft == 2 && focus.ExpandMarginLeft == 2,
            "Button focus is not a separated 2px ring.");
        Check(theme.GetColor("font_disabled_color", "Button") == EditorTheme.MutedText,
            "Disabled button text is not semantically visible.");
        Check(theme.GetStylebox("fill", "ProgressBar") is StyleBoxFlat working &&
              working.BgColor == EditorTheme.Working,
            "Running work does not use the distinct violet state.");
        Check(EditorTheme.Durable != EditorTheme.Working && EditorTheme.Accent != EditorTheme.Working,
            "Saved, selected and working state colours are not distinct.");
        Check(File.ReadAllText(ProjectSettings.GlobalizePath("res://Assets/UI/Fonts/OFL.txt"))
                .Contains("SIL OPEN FONT LICENSE Version 1.1", StringComparison.Ordinal),
            "Vendored fonts do not carry the required OFL license.");
        return Task.FromResult(assertions);
    }
}

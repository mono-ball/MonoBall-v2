namespace MonoBall.Core.Diagnostics.ImGui;

using System.Numerics;
using Hexa.NET.ImGui;
using MonoBall.Core.Diagnostics.UI;

/// <summary>
/// Provides theming and styling configuration for ImGui debug panels.
/// </summary>
public static class ImGuiTheme
{
    // Pokéball Theme Colors - Derived from DebugColors
    private static readonly Vector4 PokeballRed = DebugColors.Accent; // Pokéball red
    private static readonly Vector4 PikachuYellow = DebugColors.Highlight; // Pikachu yellow
    private static readonly Vector4 WaterBlue = DebugColors.Info; // Water type blue
    private static readonly Vector4 GrassGreen = DebugColors.Success; // Grass type green
    private static readonly Vector4 FireOrange = DebugColors.Blocking; // Fire type orange

    /// <summary>
    /// Applies the default debug theme to ImGui (Pokéball theme).
    /// </summary>
    public static void ApplyDefaultTheme()
    {
        ApplyPokeballTheme();
    }

    /// <summary>
    /// Applies the Pokéball-themed dark theme to ImGui.
    /// Based on Pokémon type colors for intuitive status indication.
    /// </summary>
    public static void ApplyPokeballTheme()
    {
        var style = ImGui.GetStyle();

        // Window
        style.WindowRounding = 4.0f;
        style.WindowBorderSize = 1.0f;
        style.WindowPadding = new Vector2(8, 8);
        style.WindowTitleAlign = new Vector2(0.5f, 0.5f);

        // Frame
        style.FrameRounding = 2.0f;
        style.FrameBorderSize = 0.0f;
        style.FramePadding = new Vector2(4, 2);

        // Items
        style.ItemSpacing = new Vector2(8, 4);
        style.ItemInnerSpacing = new Vector2(4, 4);
        style.IndentSpacing = 20.0f;

        // Scrollbar
        style.ScrollbarSize = 12.0f;
        style.ScrollbarRounding = 2.0f;

        // Grab
        style.GrabMinSize = 8.0f;
        style.GrabRounding = 2.0f;

        // Tab
        style.TabRounding = 2.0f;

        // Colors - Pokéball dark theme
        var colors = style.Colors;

        // Text
        colors[(int)ImGuiCol.Text] = DebugColors.TextPrimary;
        colors[(int)ImGuiCol.TextDisabled] = DebugColors.TextDim;

        // Backgrounds
        colors[(int)ImGuiCol.WindowBg] = DebugColors.BackgroundPrimary;
        colors[(int)ImGuiCol.ChildBg] = new Vector4(0.00f, 0.00f, 0.00f, 0.00f);
        colors[(int)ImGuiCol.PopupBg] = DebugColors.BackgroundElevated;
        colors[(int)ImGuiCol.Border] = new Vector4(0.30f, 0.30f, 0.35f, 0.50f);
        colors[(int)ImGuiCol.BorderShadow] = new Vector4(0.00f, 0.00f, 0.00f, 0.00f);

        // Frame
        colors[(int)ImGuiCol.FrameBg] = new Vector4(0.20f, 0.20f, 0.22f, 0.54f);
        colors[(int)ImGuiCol.FrameBgHovered] = new Vector4(0.30f, 0.30f, 0.35f, 0.40f);
        colors[(int)ImGuiCol.FrameBgActive] = new Vector4(
            PokeballRed.X * 0.5f,
            PokeballRed.Y * 0.5f,
            PokeballRed.Z * 0.5f,
            0.67f
        );

        // Title
        colors[(int)ImGuiCol.TitleBg] = DebugColors.BackgroundSecondary;
        colors[(int)ImGuiCol.TitleBgActive] = new Vector4(
            PokeballRed.X * 0.4f,
            PokeballRed.Y * 0.3f,
            PokeballRed.Z * 0.3f,
            1.00f
        );
        colors[(int)ImGuiCol.TitleBgCollapsed] = new Vector4(0.00f, 0.00f, 0.00f, 0.51f);
        colors[(int)ImGuiCol.MenuBarBg] = DebugColors.BackgroundSecondary;

        // Scrollbar
        colors[(int)ImGuiCol.ScrollbarBg] = new Vector4(0.02f, 0.02f, 0.02f, 0.53f);
        colors[(int)ImGuiCol.ScrollbarGrab] = new Vector4(0.31f, 0.31f, 0.31f, 1.00f);
        colors[(int)ImGuiCol.ScrollbarGrabHovered] = new Vector4(0.41f, 0.41f, 0.41f, 1.00f);
        colors[(int)ImGuiCol.ScrollbarGrabActive] = new Vector4(
            PokeballRed.X,
            PokeballRed.Y,
            PokeballRed.Z,
            0.80f
        );

        // Checkmark and Slider - Pikachu Yellow accent
        colors[(int)ImGuiCol.CheckMark] = PikachuYellow;
        colors[(int)ImGuiCol.SliderGrab] = PikachuYellow;
        colors[(int)ImGuiCol.SliderGrabActive] = new Vector4(
            PikachuYellow.X,
            PikachuYellow.Y * 0.9f,
            PikachuYellow.Z,
            1.00f
        );

        // Buttons - Pokéball red accents
        colors[(int)ImGuiCol.Button] = new Vector4(0.25f, 0.25f, 0.28f, 1.00f);
        colors[(int)ImGuiCol.ButtonHovered] = new Vector4(
            PokeballRed.X * 0.6f,
            PokeballRed.Y * 0.4f,
            PokeballRed.Z * 0.4f,
            1.00f
        );
        colors[(int)ImGuiCol.ButtonActive] = PokeballRed;

        // Header - Pokéball accents
        colors[(int)ImGuiCol.Header] = new Vector4(0.25f, 0.25f, 0.28f, 1.00f);
        colors[(int)ImGuiCol.HeaderHovered] = new Vector4(
            PokeballRed.X * 0.5f,
            PokeballRed.Y * 0.3f,
            PokeballRed.Z * 0.3f,
            0.80f
        );
        colors[(int)ImGuiCol.HeaderActive] = PokeballRed;

        // Separator
        colors[(int)ImGuiCol.Separator] = new Vector4(0.30f, 0.30f, 0.35f, 0.50f);
        colors[(int)ImGuiCol.SeparatorHovered] = PokeballRed with { W = 0.78f };
        colors[(int)ImGuiCol.SeparatorActive] = PokeballRed;

        // Resize grip
        colors[(int)ImGuiCol.ResizeGrip] = new Vector4(0.25f, 0.25f, 0.28f, 0.20f);
        colors[(int)ImGuiCol.ResizeGripHovered] = PokeballRed with { W = 0.67f };
        colors[(int)ImGuiCol.ResizeGripActive] = PokeballRed with { W = 0.95f };

        // Tabs - Pokéball theme
        colors[(int)ImGuiCol.Tab] = new Vector4(0.18f, 0.18f, 0.20f, 0.86f);
        colors[(int)ImGuiCol.TabHovered] = new Vector4(
            PokeballRed.X * 0.7f,
            PokeballRed.Y * 0.5f,
            PokeballRed.Z * 0.5f,
            0.80f
        );
        colors[(int)ImGuiCol.TabSelected] = new Vector4(
            PokeballRed.X * 0.5f,
            PokeballRed.Y * 0.3f,
            PokeballRed.Z * 0.3f,
            1.00f
        );
        colors[(int)ImGuiCol.TabDimmed] = new Vector4(0.14f, 0.14f, 0.16f, 0.97f);
        colors[(int)ImGuiCol.TabDimmedSelected] = new Vector4(0.20f, 0.20f, 0.24f, 1.00f);

        // Plots - Type-based colors
        colors[(int)ImGuiCol.PlotLines] = WaterBlue;
        colors[(int)ImGuiCol.PlotLinesHovered] = FireOrange;
        colors[(int)ImGuiCol.PlotHistogram] = GrassGreen;
        colors[(int)ImGuiCol.PlotHistogramHovered] = new Vector4(
            GrassGreen.X * 1.2f,
            GrassGreen.Y * 1.2f,
            GrassGreen.Z * 1.2f,
            1.00f
        );

        // Table
        colors[(int)ImGuiCol.TableHeaderBg] = DebugColors.BackgroundElevated;
        colors[(int)ImGuiCol.TableBorderStrong] = new Vector4(0.31f, 0.31f, 0.35f, 1.00f);
        colors[(int)ImGuiCol.TableBorderLight] = new Vector4(0.23f, 0.23f, 0.25f, 1.00f);
        colors[(int)ImGuiCol.TableRowBg] = new Vector4(0.00f, 0.00f, 0.00f, 0.00f);
        colors[(int)ImGuiCol.TableRowBgAlt] = new Vector4(1.00f, 1.00f, 1.00f, 0.04f);

        // Selection and Navigation - Pikachu yellow accent
        colors[(int)ImGuiCol.TextSelectedBg] = PikachuYellow with
        {
            W = 0.35f,
        };
        colors[(int)ImGuiCol.DragDropTarget] = PikachuYellow with { W = 0.90f };
        colors[(int)ImGuiCol.NavHighlight] = PikachuYellow;
        colors[(int)ImGuiCol.NavWindowingHighlight] = new Vector4(1.00f, 1.00f, 1.00f, 0.70f);
        colors[(int)ImGuiCol.NavWindowingDimBg] = new Vector4(0.80f, 0.80f, 0.80f, 0.20f);
        colors[(int)ImGuiCol.ModalWindowDimBg] = new Vector4(0.00f, 0.00f, 0.00f, 0.50f);
    }

    /// <summary>
    /// Applies a light theme to ImGui.
    /// </summary>
    public static void ApplyLightTheme()
    {
        ImGui.StyleColorsLight();

        var style = ImGui.GetStyle();
        style.WindowRounding = 4.0f;
        style.FrameRounding = 2.0f;
        style.ScrollbarRounding = 2.0f;
        style.GrabRounding = 2.0f;
        style.TabRounding = 2.0f;
    }

    /// <summary>
    /// Applies ImGui's classic dark theme.
    /// </summary>
    public static void ApplyClassicTheme()
    {
        ImGui.StyleColorsDark();
    }
}

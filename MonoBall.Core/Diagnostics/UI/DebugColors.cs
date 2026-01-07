namespace MonoBall.Core.Diagnostics.UI;

using System.Numerics;

/// <summary>
/// Centralized color constants for debug panels using the Pokéball theme.
/// Colors are inspired by Pokémon types for intuitive status indication.
/// </summary>
public static class DebugColors
{
    // ═══════════════════════════════════════════════════════════════════════════
    // Background Colors
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>Primary background color (dark charcoal).</summary>
    public static readonly Vector4 BackgroundPrimary = new(26 / 255f, 26 / 255f, 29 / 255f, 0.94f);

    /// <summary>Secondary background color (darker).</summary>
    public static readonly Vector4 BackgroundSecondary = new(20 / 255f, 20 / 255f, 23 / 255f, 1f);

    /// <summary>Elevated background color (lighter for popups/modals).</summary>
    public static readonly Vector4 BackgroundElevated = new(38 / 255f, 38 / 255f, 42 / 255f, 1f);

    // ═══════════════════════════════════════════════════════════════════════════
    // Text Colors
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>Primary text color (light gray).</summary>
    public static readonly Vector4 TextPrimary = new(232 / 255f, 232 / 255f, 232 / 255f, 1f);

    /// <summary>Secondary text color (medium gray).</summary>
    public static readonly Vector4 TextSecondary = new(180 / 255f, 180 / 255f, 185 / 255f, 1f);

    /// <summary>Dim text color (disabled/muted).</summary>
    public static readonly Vector4 TextDim = new(120 / 255f, 120 / 255f, 128 / 255f, 1f);

    /// <summary>Value highlight color (cyan tint).</summary>
    public static readonly Vector4 TextValue = new(0.7f, 0.9f, 1f, 1f);

    // ═══════════════════════════════════════════════════════════════════════════
    // Status Colors - Pokémon Type Inspired
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>Success/healthy status (Grass type green).</summary>
    public static readonly Vector4 Success = new(120 / 255f, 200 / 255f, 80 / 255f, 1f);

    /// <summary>Warning status (Pikachu yellow).</summary>
    public static readonly Vector4 Warning = new(255 / 255f, 203 / 255f, 5 / 255f, 1f);

    /// <summary>Error status (Pokéball red).</summary>
    public static readonly Vector4 Error = new(238 / 255f, 21 / 255f, 21 / 255f, 1f);

    /// <summary>Info/neutral status (Water type blue).</summary>
    public static readonly Vector4 Info = new(104 / 255f, 144 / 255f, 240 / 255f, 1f);

    /// <summary>Blocking/active status (Fire type orange).</summary>
    public static readonly Vector4 Blocking = new(240 / 255f, 128 / 255f, 48 / 255f, 1f);

    // ═══════════════════════════════════════════════════════════════════════════
    // Accent Colors
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>Primary highlight color (Pikachu yellow).</summary>
    public static readonly Vector4 Highlight = new(255 / 255f, 203 / 255f, 5 / 255f, 1f);

    /// <summary>Accent color (Pokéball red).</summary>
    public static readonly Vector4 Accent = new(238 / 255f, 21 / 255f, 21 / 255f, 1f);

    /// <summary>Active/selected item color.</summary>
    public static readonly Vector4 Active = new(0.4f, 1f, 0.4f, 1f);

    /// <summary>Paused state color.</summary>
    public static readonly Vector4 Paused = new(1f, 0.9f, 0.4f, 1f);

    /// <summary>Inactive/disabled state color.</summary>
    public static readonly Vector4 Inactive = new(0.5f, 0.5f, 0.5f, 1f);

    // ═══════════════════════════════════════════════════════════════════════════
    // Log Level Colors
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>Verbose log level (dim gray).</summary>
    public static readonly Vector4 LogVerbose = TextDim;

    /// <summary>Debug log level (Water blue).</summary>
    public static readonly Vector4 LogDebug = new(0.4f, 0.8f, 1f, 1f);

    /// <summary>Info log level (light gray).</summary>
    public static readonly Vector4 LogInfo = TextPrimary;

    /// <summary>Warning log level (Pikachu yellow).</summary>
    public static readonly Vector4 LogWarning = Warning;

    /// <summary>Error log level (Pokéball red).</summary>
    public static readonly Vector4 LogError = Error;

    /// <summary>Fatal log level (bright red).</summary>
    public static readonly Vector4 LogFatal = new(1f, 0.2f, 0.2f, 1f);

    // ═══════════════════════════════════════════════════════════════════════════
    // Performance Colors
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>Fast timing color (green).</summary>
    public static readonly Vector4 TimingFast = Success;

    /// <summary>Medium timing color (yellow).</summary>
    public static readonly Vector4 TimingMedium = Warning;

    /// <summary>Slow timing color (red).</summary>
    public static readonly Vector4 TimingSlow = Error;
}

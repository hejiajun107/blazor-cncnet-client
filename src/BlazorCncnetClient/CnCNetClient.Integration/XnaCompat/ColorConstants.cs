using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace CnCNetClient.Integration.XnaCompat;

/// <summary>
/// Named color constants mapped from XNA <c>Microsoft.Xna.Framework.Color</c> to
/// ImageSharp <c>Rgba32</c>. Centralized one-time replacement for all XNA named colors
/// used in the original CnCNet client codebase.
/// </summary>
public static class ColorConstants
{
    // ── IRC chat colors (from CnCNetManager.cs, 15 entries) ──

    public static readonly Rgba32 Default = default;       // index 0/1, set at runtime
    public static readonly Rgba32 LightBlue = new(173, 216, 230, 255);
    public static readonly Rgba32 ForestGreen = new(34, 139, 34, 255);
    public static readonly Rgba32 DarkRed = new(180, 0, 0, 255);
    public static readonly Rgba32 Red = new(255, 0, 0, 255);
    public static readonly Rgba32 MediumPurple = new(147, 112, 219, 255);
    public static readonly Rgba32 Orange = new(255, 165, 0, 255);
    public static readonly Rgba32 Yellow = new(255, 255, 0, 255);
    public static readonly Rgba32 LimeGreen = new(50, 205, 50, 255);
    public static readonly Rgba32 Turquoise = new(64, 224, 208, 255);
    public static readonly Rgba32 LightSkyBlue = new(135, 206, 250, 255);
    public static readonly Rgba32 RoyalBlue = new(65, 105, 225, 255);
    public static readonly Rgba32 DeepPink = new(255, 20, 147, 255);
    public static readonly Rgba32 LightGray = new(211, 211, 211, 255);
    public static readonly Rgba32 Gray = new(128, 128, 128, 255);

    // ── Common named colors used across the codebase ──

    public static readonly Rgba32 White = new(255, 255, 255, 255);
    public static readonly Rgba32 Black = new(0, 0, 0, 255);
    public static readonly Rgba32 Green = new(0, 128, 0, 255);
    public static readonly Rgba32 Blue = new(0, 0, 255, 255);
    public static readonly Rgba32 Purple = new(128, 0, 128, 255);
    public static readonly Rgba32 Pink = new(255, 192, 203, 255);
    public static readonly Rgba32 Cyan = new(0, 255, 255, 255);
    public static readonly Rgba32 Magenta = new(255, 0, 255, 255);
    public static readonly Rgba32 Transparent = new(0, 0, 0, 0);
    public static readonly Rgba32 CornflowerBlue = new(100, 149, 237, 255);
    public static readonly Rgba32 DarkBlue = new(0, 0, 139, 255);
    public static readonly Rgba32 DarkGreen = new(0, 100, 0, 255);
    public static readonly Rgba32 DarkGray = new(64, 64, 64, 255);
    public static readonly Rgba32 Silver = new(192, 192, 192, 255);
}

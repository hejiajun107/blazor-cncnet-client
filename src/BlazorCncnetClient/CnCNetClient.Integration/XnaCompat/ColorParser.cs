using System.Globalization;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace CnCNetClient.Integration.XnaCompat;

/// <summary>
/// Parses color strings (e.g. "R,G,B" or "R,G,B,A") into <see cref="Rgba32"/>.
/// Replaces <c>AssetLoader.GetColorFromString</c> from Rampastring.XNAUI.
/// </summary>
public static class ColorParser
{
    /// <summary>
    /// Creates a color from a string in the form "R,G,B" or "R,G,B,A".
    /// All values must be 0–255.
    /// </summary>
    /// <exception cref="FormatException">Thrown if the string is not a valid color.</exception>
    public static Rgba32 GetColorFromString(string colorString)
    {
        try
        {
            string[] parts = colorString.Split(',');

            byte alpha = 255;
            if (parts.Length == 4)
            {
                alpha = Convert.ToByte(parts[3], CultureInfo.InvariantCulture);
            }

            return new Rgba32(
                Convert.ToByte(parts[0], CultureInfo.InvariantCulture),
                Convert.ToByte(parts[1], CultureInfo.InvariantCulture),
                Convert.ToByte(parts[2], CultureInfo.InvariantCulture),
                alpha);
        }
        catch
        {
            throw new FormatException(
                $"ColorParser.GetColorFromString: Failed to convert '{colorString}' to a valid color!");
        }
    }

    /// <summary>
    /// Creates a color from a string, returning <paramref name="defaultColor"/> on failure.
    /// </summary>
    public static Rgba32 GetColorFromString(string colorString, Rgba32 defaultColor)
    {
        try
        {
            return GetColorFromString(colorString);
        }
        catch
        {
            return defaultColor;
        }
    }
}

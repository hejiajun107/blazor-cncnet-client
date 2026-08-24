#nullable enable
using System;
using System.IO;
using System.Reflection;

using CnCNetClient.Core;

using CnCNetClient.Tools;

using SixLabors.ImageSharp;

namespace CnCNetClient.Domain.Multiplayer.CnCNet
{
    /// <summary>
    /// A <see cref="CnCNetGame"/> that loads its image from a custom icon file, or falls back
    /// to the unknown game icon embedded in the assembly.
    /// </summary>
    internal sealed class CustomCnCNetGame : CnCNetGame
    {
        private static readonly Lazy<Image> lazyFallbackImage = new(() =>
            Image.Load(
                Assembly.GetAssembly(typeof(CustomCnCNetGame))!
                .GetManifestResourceStream("DTAClient.Icons.unknownicon.png")));
        private static Image FallbackImage => lazyFallbackImage.Value;

        private readonly string iconFilename;

        public CustomCnCNetGame(string iconFilename)
        {
            this.iconFilename = iconFilename;
        }

        protected override Image? LoadImage()
        {
            try
            {
                string path = SafePath.CombineFilePath(ProgramConstants.GetBaseResourcePath(), iconFilename);
                if (File.Exists(path))
                    return Image.Load(path);
            }
            catch
            {
                // fall through to fallback image
            }

            return FallbackImage;
        }
    }
}

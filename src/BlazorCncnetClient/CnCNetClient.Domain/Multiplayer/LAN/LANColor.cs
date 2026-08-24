using SixLabors.ImageSharp.PixelFormats;

namespace CnCNetClient.Domain.LAN
{
    public class LANColor
    {
        public LANColor(string name, Rgba32 xnaColor)
        {
            Name = name;
            XNAColor = xnaColor;
        }

        public string Name { get; private set; }
        public Rgba32 XNAColor { get; private set; }
    }
}

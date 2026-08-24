using SixLabors.ImageSharp.PixelFormats;

namespace CnCNetClient.Online
{
    public class IRCColor
    {
        public IRCColor(string name, bool selectable, Rgba32 xnaColor, int ircColorId)
        {
            Name = name;
            Selectable = selectable;
            XnaColor = xnaColor;
            IrcColorId = ircColorId;
        }

        public string Name { get; private set; }
        public bool Selectable { get; private set; }
        public Rgba32 XnaColor { get; private set; }
        public int IrcColorId { get; private set; }
    }
}

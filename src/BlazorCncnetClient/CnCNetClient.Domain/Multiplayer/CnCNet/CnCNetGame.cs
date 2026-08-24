#nullable enable
using System;
using System.Threading;

using SixLabors.ImageSharp;

namespace CnCNetClient.Domain.Multiplayer.CnCNet
{
    /// <summary>
    /// A class for games supported on CnCNet (DTA, TI, TS, RA1/2, etc.)
    /// </summary>
    public abstract class CnCNetGame
    {
        private readonly Lazy<Image?> lazyImage;

        protected CnCNetGame()
        {
            lazyImage = new Lazy<Image?>(LoadImage, LazyThreadSafetyMode.ExecutionAndPublication);
        }

        /// <summary>
        /// The name of the game that is displayed on the user-interface.
        /// </summary>
        public string? UIName { get; set; }

        /// <summary>
        /// The internal name (suffix) of the game.
        /// </summary>
        public string? InternalName { get; set; }

        /// <summary>
        /// The IRC chat channel ID of the game.
        /// </summary>
        public string? ChatChannel { get; set; }

        /// <summary>
        /// The IRC game broadcasting channel ID of the game.
        /// </summary>
        public string? GameBroadcastChannel { get; set; }

        /// <summary>
        /// The executable name of the game's client.
        /// </summary>
        public string? ClientExecutableName { get; set; }

        /// <summary>
        /// Gets the image for this game's icon. Loaded lazily and is thread-safe.
        /// </summary>
        public Image? Image => lazyImage.Value;

        /// <summary>
        /// The location where to read the game's installation path from the registry.
        /// </summary>
        public string? RegistryInstallPath
        {
            get => field;
            set
            {
                string? hive = value?.Split('\\')[0].Trim();
                if (hive is not "HKLM" and not "HKCU")
                    throw new Exception($"Unexpected registry hive. Expected HKLM or HKCU. Got: {hive}");

                field = value;
            }
        }

        private bool supported = true;

        /// <summary>
        /// Determines if the game is properly supported by this client.
        /// Defaults to true.
        /// </summary>
        public bool Supported
        {
            get { return supported; }
            set { supported = value; }
        }

        /// <summary>
        /// If true, the client should always be connected to this game's chat channel.
        /// </summary>
        public bool AlwaysEnabled { get; set; }

        /// <summary>
        /// Loads the image for this game's icon. Thread-safe.
        /// </summary>
        protected abstract Image? LoadImage();
    }
}

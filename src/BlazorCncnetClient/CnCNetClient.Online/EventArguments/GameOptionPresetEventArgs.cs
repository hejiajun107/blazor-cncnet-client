using System;

namespace CnCNetClient.Online.EventArguments
{
    public class GameOptionPresetEventArgs : EventArgs
    {
        public string PresetName { get; }

        public GameOptionPresetEventArgs(string presetName)
        {
            PresetName = presetName;
        }
    }
}

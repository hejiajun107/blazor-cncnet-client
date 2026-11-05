using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

using CnCNetClient.Core;

using Microsoft.JSInterop;

namespace BlazorCncnetClient.Infrastructure
{
    /// <summary>
    /// 界面音效服务（对齐上游 Rampastring <c>EnhancedSoundEffect</c>）：集中解析 Resources 根目录下的
    /// 音效 URL、统一音量（[Audio] ClientVolume），并按 [Audio] Sound*Cooldown 对同类音效做重复播放冷却。
    /// 按钮/复选框/下拉框音效由前端全局事件委托播放，房间/大厅事件音效由页面订阅各服务的
    /// <c>UiSoundRequested</c> 后调用 <see cref="Play(string)"/>。
    /// </summary>
    public sealed class UiSoundService
    {
        public const string Button = "button";
        public const string CheckBox = "checkbox";
        public const string DropDown = "dropdown";
        public const string GameCreated = "gamecreated";
        public const string PrivateMessage = "pm";
        public const string JoinGame = "joingame";
        public const string LeaveGame = "leavegame";
        public const string Message = "message";
        public const string GetReady = "getready";
        public const string Return = "return";

        private static readonly string[] SoundNames =
        [
            Button, CheckBox, DropDown, GameCreated, PrivateMessage,
            JoinGame, LeaveGame, Message, GetReady, Return,
        ];

        /// <summary>扩展名优先级：浏览器/WebView2 优先 mp3、其次 ogg，最后原版 wav。</summary>
        private static readonly string[] Extensions = [".mp3", ".ogg", ".wav"];

        private readonly IJSRuntime _js;
        private readonly GameContentServer _contentServer;
        private readonly Dictionary<string, DateTime> _lastPlayed = new(StringComparer.OrdinalIgnoreCase);
        private bool _initialized;

        public UiSoundService(IJSRuntime js, GameContentServer contentServer)
        {
            _js = js;
            _contentServer = contentServer;
        }

        /// <summary>
        /// 首帧初始化：把 Resources 根目录下实际存在的音效 URL 与音量交给前端（幂等）。
        /// 缺失的音频文件不注册，前端播放时静默跳过。
        /// </summary>
        public async Task InitializeAsync()
        {
            if (_initialized)
                return;
            _initialized = true;

            var urls = new Dictionary<string, string>();
            string resourceRoot = Path.Combine(ProgramConstants.GamePath, "Resources");
            foreach (string name in SoundNames)
            {
                foreach (string extension in Extensions)
                {
                    if (!File.Exists(Path.Combine(resourceRoot, name + extension)))
                        continue;
                    urls[name] = _contentServer.MapUrl("Resources/" + name + extension);
                    break;
                }
            }

            double volume = UserINISettings.Instance.ClientVolume.Value;
            await _js.InvokeVoidAsync("cncnet.initUiSounds", urls, Math.Clamp(volume, 0.0, 1.0));
        }

        /// <summary>播放指定音效（按冷却窗口去重；无文件/音量 0/JS 失败时静默跳过）。</summary>
        public async Task PlayAsync(string name)
        {
            if (string.IsNullOrEmpty(name))
                return;

            double cooldown = CooldownFor(name);
            if (cooldown > 0)
            {
                var now = DateTime.UtcNow;
                if (_lastPlayed.TryGetValue(name, out var last)
                    && (now - last).TotalSeconds < cooldown)
                {
                    return;
                }
                _lastPlayed[name] = now;
            }

            try
            {
                await _js.InvokeVoidAsync("cncnet.playUiSound", name);
            }
            catch
            {
                // JS 上下文未就绪（页面正在释放等）不阻塞事件处理
            }
        }

        /// <summary>事件订阅方使用的即发即忘入口。</summary>
        public void Play(string name) => _ = PlayAsync(name);

        /// <summary>各类音效的重复播放冷却（秒），取值与上游 ClientConfiguration 一致。</summary>
        private static double CooldownFor(string name)
        {
            var configuration = ClientConfiguration.Instance;
            return name switch
            {
                JoinGame => configuration.SoundGameLobbyJoinCooldown,
                LeaveGame => configuration.SoundGameLobbyLeaveCooldown,
                Message => configuration.SoundMessageCooldown,
                PrivateMessage => configuration.SoundPrivateMessageCooldown,
                GetReady => configuration.SoundGameLobbyGetReadyCooldown,
                Return => configuration.SoundGameLobbyReturnCooldown,
                _ => 0.0,
            };
        }
    }
}

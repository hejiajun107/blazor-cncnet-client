using System;
using System.Linq;
using System.Threading.Tasks;
using CnCNetClient.Core;
using CnCNetClient.Core.Extensions;
using CnCNetClient.Core.I18N;
using CnCNetClient.Tools;

namespace BlazorCncnetClient.Infrastructure;

/// <summary>
/// 聊天链接安全打开服务（原版 <c>URLHandler.OpenLink</c> 语义）：
/// 可信域名（<c>ClientConfiguration.TrustedDomains</c> + <c>AlwaysTrustedDomains</c>，含子域名）
/// 直接打开；非可信域名先弹 YesNo 确认框警告，用户确认后才打开。
/// </summary>
public sealed class ChatLinkService
{
    private readonly MessageBoxService _messageBox;
    private readonly ExtrasService _extras;

    public ChatLinkService(MessageBoxService messageBox, ExtrasService extras)
    {
        _messageBox = messageBox;
        _extras = extras;
    }

    /// <summary>按可信域名规则打开聊天链接；非可信域名弹确认框。打开失败弹错误框。</summary>
    public async Task OpenLinkAsync(string url)
    {
        bool isTrusted = false;
        try
        {
            string domain = new Uri(url).Host;
            var config = ClientConfiguration.Instance;
            string[] trusted = config.TrustedDomains
                .Concat(config.AlwaysTrustedDomains)
                .Distinct(StringComparer.InvariantCultureIgnoreCase)
                .ToArray();
            isTrusted = trusted.Contains(domain, StringComparer.InvariantCultureIgnoreCase)
                || trusted.Any(td => domain.EndsWith("." + td, StringComparison.InvariantCultureIgnoreCase));
        }
        catch (Exception ex)
        {
            isTrusted = false;
            Logger.Log($"ChatLinkService: 解析链接 \"{url}\" 出错: {ex}");
        }

        bool proceed;
        if (isTrusted)
        {
            proceed = true;
        }
        else
        {
            string title = UiText.T("Chat.OpenLinkTitle", "Open link confirmation");
            string text = UiText.T("Chat.OpenLinkBody",
                    "You are about to open a link shared in chat.\n\nNote: this link has not been verified and CnCNet is not responsible for its content.\n\nOpen the following link in your browser?\n\n")
                + url;
            proceed = await _messageBox.ShowAsync(title, text, MessageBoxButtons.YesNo, MessageBoxKind.Warn);
        }

        if (!proceed)
            return;

        if (_extras.OpenUrl(url) is { } err)
        {
            await _messageBox.ShowAsync(
                UiText.T("Chat.OpenLinkFailedTitle", "Could not open link"),
                err, MessageBoxButtons.Ok, MessageBoxKind.Error);
        }
    }
}

#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace CnCNetClient.Online.LAN;

/// <summary>
/// LAN 大厅消息去重器（M4.4 D3.1）。
/// 移植自原版 <c>DTAClient.DXGUI.Multiplayer.LANMessageDeduplicator</c>：
/// 为出站消息生成唯一 ID（<c>MID_xxxxxxxx</c>），记录收到的 ID 以过滤重复；
/// ID 过期（默认 60s）后由后台定时器清理，防内存泄漏。线程安全。
/// </summary>
public sealed class LanMessageDeduplicator : IDisposable
{
    private readonly Random random;
    private readonly object lockObject = new();

    private readonly ConcurrentDictionary<string, DateTime> receivedMessageIds = new();

    private readonly double messageIdExpirationSeconds;

    private readonly Timer cleanupTimer;
    private const double CLEANUP_INTERVAL_SECONDS = 30.0;

    private int disposed;

    public LanMessageDeduplicator(int randomSeed, double messageIdExpirationSeconds = 60.0)
    {
        random = new Random(randomSeed);
        this.messageIdExpirationSeconds = messageIdExpirationSeconds;

        int cleanupIntervalMs = (int)(CLEANUP_INTERVAL_SECONDS * 1000);
        cleanupTimer = new Timer(CleanupCallback, null, cleanupIntervalMs, cleanupIntervalMs);
    }

    private void CleanupCallback(object? state)
    {
        if (disposed == 0)
            CleanupExpiredMessageIds();
    }

    private const string Chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";

    /// <summary>生成唯一消息 ID（Random 非线程安全，加锁）。</summary>
    public string GenerateMessageId()
    {
        char[] id = new char[8];
        lock (lockObject)
        {
            for (int i = 0; i < id.Length; i++)
                id[i] = Chars[random.Next(Chars.Length)];
        }
        return "MID_" + new string(id);
    }

    public const int MESSAGE_ID_PREFIX_LENGTH = 4; // "MID_"
    public const int MESSAGE_ID_LENGTH = 12; // "MID_" + 8

    public static bool IsValidMessageId(string value)
        => !string.IsNullOrEmpty(value)
           && value.StartsWith("MID_", StringComparison.Ordinal)
           && value.Length == MESSAGE_ID_LENGTH
           && value[MESSAGE_ID_PREFIX_LENGTH..].All(char.IsLetterOrDigit);

    /// <summary>记录收到的消息 ID；已存在则视为重复。无 ID 视为非重复（兼容旧客户端）。</summary>
    public void AddMessage(string messageId, out bool isDuplicate)
    {
        if (string.IsNullOrEmpty(messageId))
        {
            isDuplicate = false;
            return;
        }

        DateTime expiration = DateTime.UtcNow.AddSeconds(messageIdExpirationSeconds);
        isDuplicate = !receivedMessageIds.TryAdd(messageId, expiration);
    }

    /// <summary>将 payload 前缀消息 ID 后返回。</summary>
    public string WrapMessage(string payload)
        => GenerateMessageId() + payload;

    /// <summary>解包消息：提取开头消息 ID 并查重，返回 payload。</summary>
    public void UnwrapMessage(string wrappedMessage, out string payload, out bool isDuplicate)
    {
        if (!string.IsNullOrEmpty(wrappedMessage) && wrappedMessage.Length >= MESSAGE_ID_LENGTH)
        {
            string potentialId = wrappedMessage[..MESSAGE_ID_LENGTH];
            if (IsValidMessageId(potentialId))
            {
                payload = wrappedMessage[MESSAGE_ID_LENGTH..];
                AddMessage(potentialId, out isDuplicate);
                return;
            }
        }

        payload = wrappedMessage;
        isDuplicate = false;
    }

    private void CleanupExpiredMessageIds()
    {
        if (disposed != 0 || receivedMessageIds.IsEmpty)
            return;

        DateTime now = DateTime.UtcNow;
        foreach (var kvp in receivedMessageIds)
        {
            if (kvp.Value < now)
                _ = receivedMessageIds.TryRemove(kvp.Key, out _);
        }
    }

    public int TrackedMessageCount => receivedMessageIds.Count;

    public void Clear() => receivedMessageIds.Clear();

    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref disposed, 1, 0) == 0)
            cleanupTimer?.Dispose();
        GC.SuppressFinalize(this);
    }
}

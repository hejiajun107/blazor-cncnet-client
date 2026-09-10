using CnCNetClient.Core;
using CnCNetClient.Online;
using Xunit;

namespace CnCNetClient.Tests;

/// <summary>记录 IRC 事件回调的假连接管理器。</summary>
public sealed class FakeConnectionManager : IConnectionManager
{
    public List<string> WelcomeMessages = new();
    public List<string> GenericMessages = new();
    public List<(string channel, string sender, string ident, string message)> ChatMessages = new();
    public List<(string sender, string message)> PrivateMessages = new();
    public List<(string channel, string host, string user, string ident)> UserJoins = new();
    public List<string> UserParts = new();
    public List<string> UserQuits = new();
    public List<(string oldNick, string newNick)> NickChanges = new();
    public List<string> UserLists = new();

    public void OnWelcomeMessageReceived(string message) => WelcomeMessages.Add(message);
    public void OnGenericServerMessageReceived(string message) => GenericMessages.Add(message);
    public void OnChatMessageReceived(string receiver, string senderName, string senderIdent, string message)
        => ChatMessages.Add((receiver, senderName, senderIdent, message));
    public void OnPrivateMessageReceived(string sender, string message)
        => PrivateMessages.Add((sender, message));
    public void OnUserJoinedChannel(string channelName, string hostName, string userName, string ident)
        => UserJoins.Add((channelName, hostName, userName, ident));
    public void OnUserLeftChannel(string channelName, string userName) => UserParts.Add(userName);
    public void OnUserQuitIRC(string userName) => UserQuits.Add(userName);
    public void OnUserNicknameChange(string oldNickname, string newNickname)
        => NickChanges.Add((oldNickname, newNickname));
    public void OnUserListReceived(string channelName, string[] userList) => UserLists.Add(channelName);
    public void OnChannelTopicChanged(string userName, string channelName, string topic) { }
    public void OnChannelTopicReceived(string channelName, string topic) { }
    public void OnWhoReplyReceived(string ident, string hostName, string userName, string extraInfo) { }
    public void OnChannelFull(string channelName) { }
    public void OnTargetChangeTooFast(string channelName, string message) { }
    public void OnChannelInviteOnly(string channelName) { }
    public void OnIncorrectChannelPassword(string channelName) { }
    public void OnCTCPParsed(string channelName, string userName, string message) { }
    public void OnNoticeMessageParsed(string notice, string userName) { }
    public void OnChannelModesChanged(string userName, string channelName, string modeString, List<string> modeParameters) { }
    public void OnUserKicked(string channelName, string userName) { }
    public void OnErrorReceived(string errorMessage) { }
    public void OnNameAlreadyInUse() { }
    public void OnBannedFromChannel(string channelName) { }
    public void OnAttemptedServerChanged(string serverName) { }
    public void OnConnectAttemptFailed() { }
    public void OnConnectionLost(string reason) { }
    public void OnReconnectAttempt() { }
    public void OnDisconnected() { }
    public void OnConnected() { }
    public bool GetDisconnectStatus() => false;
    public void OnServerLatencyTested(int candidateCount, int closerCount) { }
    public void OnAwayMessageReceived(string userName, string reason) { }
}

/// <summary>
/// 1.12 Connection：验证 IRC 消息解析/路由与日志脱敏（不建立真实网络连接）。
/// </summary>
public class ConnectionTests : IDisposable
{
    private readonly TestGameEnvironment _env;
    private readonly FakeConnectionManager _manager;
    private readonly Connection _connection;

    public ConnectionTests()
    {
        _env = new TestGameEnvironment();
        _env.ResetConfigurationInstance();
        _manager = new FakeConnectionManager();
        _connection = new Connection(_manager, new Random(42));
        ProgramConstants.PLAYERNAME = "Tester";
    }

    public void Dispose() => _env.Dispose();

    [Fact]
    public void HandleMessage_WelcomeNumericRoutesToOnWelcomeMessageReceived()
    {
        _connection.HandleMessage(":irc.example.net 001 Tester :Welcome to the Example IRC Network Tester\r\n");

        Assert.Single(_manager.WelcomeMessages);
        Assert.Contains("Welcome to the Example IRC Network Tester", _manager.WelcomeMessages[0]);
    }

    [Fact]
    public void HandleMessage_ChannelMessageRoutesToOnChatMessageReceived()
    {
        _connection.HandleMessage(":Alice!alice@host.example PRIVMSG #cncnet :hello everyone\r\n");

        Assert.Single(_manager.ChatMessages);
        (string channel, string sender, string ident, string message) = _manager.ChatMessages[0];
        Assert.Equal("#cncnet", channel);
        Assert.Equal("Alice", sender);
        Assert.Equal("alice", ident);
        Assert.Equal("hello everyone", message);
    }

    [Fact]
    public void HandleMessage_PrivateMessageToPlayerRoutesToOnPrivateMessageReceived()
    {
        _connection.HandleMessage(":Bob!bob@host.example PRIVMSG Tester :wanna play?\r\n");

        Assert.Single(_manager.PrivateMessages);
        Assert.Equal(("Bob", "wanna play?"), _manager.PrivateMessages[0]);
    }

    [Fact]
    public void HandleMessage_JoinAndPartAndQuitRouteToCorrespondingCallbacks()
    {
        _connection.HandleMessage(":Carol!carol@host.example JOIN #cncnet\r\n");
        _connection.HandleMessage(":Carol!carol@host.example PART #cncnet\r\n");
        _connection.HandleMessage(":Carol!carol@host.example QUIT :bye\r\n");

        Assert.Single(_manager.UserJoins);
        Assert.Equal(("#cncnet", "host.example", "Carol", "carol"), _manager.UserJoins[0]);
        Assert.Contains("Carol", _manager.UserParts);
        Assert.Contains("Carol", _manager.UserQuits);
    }

    [Fact]
    public void HandleMessage_FragmentedLineIsBufferedAndParsedTogether()
    {
        _connection.HandleMessage(":Alice!alice@host.example PRIVMSG #cncnet :part");
        _connection.HandleMessage("ial message\r\n");

        Assert.Single(_manager.ChatMessages);
        Assert.Equal("partial message", _manager.ChatMessages[0].message);
    }

    [Theory]
    [InlineData("PASS secret", "PASS [REDACTED]")]
    [InlineData("JOIN #secretchannel hunter2", "JOIN #secretchannel [REDACTED]")]
    [InlineData("JOIN #open", "JOIN #open")]
    [InlineData("MODE #room +k hunter2", "MODE #room +k [REDACTED]")]
    [InlineData("MODE #room +o Alice", "MODE #room +o Alice")]
    [InlineData("PRIVMSG #cncnet :hello", "PRIVMSG #cncnet :hello")]
    public void SanitizeForLog_RedactsCredentialsOnly(string input, string expected)
    {
        Assert.Equal(expected, Connection.SanitizeForLog(input));
    }
}

using CnCNetClient.Core;
using CnCNetClient.Domain.Multiplayer.CnCNet;
using CnCNetClient.Online.EventArguments;
using CnCNetClient.Core.Extensions;
using CnCNetClient.Integration.Threading;
using CnCNetClient.Integration.XnaCompat;
using CnCNetClient.Tools;
using SixLabors.ImageSharp.PixelFormats;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace CnCNetClient.Online
{
    /// <summary>
    /// Acts as an interface between the CnCNet connection class
    /// and the user-interface's classes.
    /// </summary>
    public class CnCNetManager : IConnectionManager
    {
        // When implementing IConnectionManager functions, pay special attention
        // to thread-safety.
        // The functions in IConnectionManager are usually called from the networking
        // thread, so if they affect anything in the UI or affect data that the
        // UI thread might be reading, use <see cref="IDispatcher.Post"/> to execute a function
        // on the UI thread instead of modifying the data or raising events directly.

        public delegate void UserListDelegate(string channelName, string[] userNames);

        public event EventHandler<ServerMessageEventArgs> WelcomeMessageReceived;
        public event EventHandler<UserAwayEventArgs> AwayMessageReceived;
        public event EventHandler<WhoEventArgs> WhoReplyReceived;
        public event EventHandler<CnCNetPrivateMessageEventArgs> PrivateMessageReceived;
        public event EventHandler<PrivateCTCPEventArgs> PrivateCTCPReceived;
        public event EventHandler<ChannelEventArgs> BannedFromChannel;

        public event EventHandler<AttemptedServerEventArgs> AttemptedServerChanged;
        public event EventHandler ConnectAttemptFailed;
        public event EventHandler<ConnectionLostEventArgs> ConnectionLost;
        public event EventHandler ReconnectAttempt;
        public event EventHandler Disconnected;
        public event EventHandler Connected;

        public event EventHandler<UserEventArgs> UserAdded;
        public event EventHandler<UserEventArgs> UserGameIndexUpdated;
        public event EventHandler<UserNameIndexEventArgs> UserRemoved;
        public event EventHandler MultipleUsersAdded;

        public CnCNetManager(IDispatcher dispatcher, GameCollection gc, CnCNetUserData cncNetUserData, Random random)
        {
            gameCollection = gc;
            this.cncNetUserData = cncNetUserData;
            connection = new Connection(this, random);

            this.dispatcher = dispatcher;

            cDefaultChatColor = ColorParser.GetColorFromString(ClientConfiguration.Instance.DefaultChatColor);

            // IRC color table: XNA Color replaced with Rgba32 constants from ColorConstants
            // (spec §4.2-A). Index 4 keeps the inline (180,0,0,255) value as ColorConstants.DarkRed.
            ircChatColors = new IRCColor[]
            {
                new IRCColor("Default color".L10N("Client:Main:ColorDefault"), false, cDefaultChatColor, 0),
                new IRCColor("Default color #2".L10N("Client:Main:ColorDefault2"), false, cDefaultChatColor, 1),
                new IRCColor("Light Blue".L10N("Client:Main:ColorLightBlue"), true, ColorConstants.LightBlue, 2),
                new IRCColor("Green".L10N("Client:Main:ColorGreen"), true, ColorConstants.ForestGreen, 3),
                new IRCColor("Dark Red".L10N("Client:Main:ColorDarkRed"), true, ColorConstants.DarkRed, 4),
                new IRCColor("Red".L10N("Client:Main:ColorRed"), true, ColorConstants.Red, 5),
                new IRCColor("Purple".L10N("Client:Main:ColorPurple"), true, ColorConstants.MediumPurple, 6),
                new IRCColor("Orange".L10N("Client:Main:ColorOrange"), true, ColorConstants.Orange, 7),
                new IRCColor("Yellow".L10N("Client:Main:ColorYellow"), true, ColorConstants.Yellow, 8),
                new IRCColor("Lime Green".L10N("Client:Main:ColorLimeGreen"), true, ColorConstants.LimeGreen, 9),
                new IRCColor("Turquoise".L10N("Client:Main:ColorTurquoise"), true, ColorConstants.Turquoise, 10),
                new IRCColor("Sky Blue".L10N("Client:Main:ColorSkyBlue"), true, ColorConstants.LightSkyBlue, 11),
                new IRCColor("Blue".L10N("Client:Main:ColorBlue"), true, ColorConstants.RoyalBlue, 12),
                new IRCColor("Pink".L10N("Client:Main:ColorPink"), true, ColorConstants.DeepPink, 13),
                new IRCColor("Metallic".L10N("Client:Main:ColorLightGrayMetallic"), true, ColorConstants.LightGray, 14),
                new IRCColor("Gray".L10N("Client:Main:ColorGray"), false, ColorConstants.Gray, 15)
            };
        }

        public Channel MainChannel { get; private set; }

        private bool connected = false;

        /// <summary>
        /// Gets a value that determines whether the client is
        /// currently connected to CnCNet.
        /// </summary>
        public bool IsConnected
        {
            get { return connected; }
        }

        public bool IsAttemptingConnection
        {
            get { return connection.AttemptingConnection; }
        }

        /// <summary>
        /// The list of all users that we can see on the IRC network.
        /// </summary>
        public List<IRCUser> UserList = new List<IRCUser>();

        private Connection connection;

        private List<Channel> channels = new List<Channel>();

        private GameCollection gameCollection;
        private readonly CnCNetUserData cncNetUserData;

        private Rgba32 cDefaultChatColor;
        private IRCColor[] ircChatColors;

        // Replaces `private WindowManager wm;` from the XNAUI-based original.
        private IDispatcher dispatcher;

        private bool disconnect = false;

        public bool IsCnCNetInitialized()
        {
            return Connection.IsIdSet();
        }

        /// <summary>
        /// Factory method for creating a new channel.
        /// </summary>
        /// <param name="uiName">The user-interface name of the channel.</param>
        /// <param name="channelName">The name of the channel.</param>
        /// <param name="persistent">Determines whether the channel's information
        /// should remain in memory even after a disconnect.</param>
        /// <param name="password">The password for the channel. Use null for none.</param>
        /// <returns>A channel.</returns>
        public Channel CreateChannel(string uiName, string channelName,
            bool persistent, bool isChatChannel, string password)
        {
            return new Channel(uiName, channelName, persistent, isChatChannel, password, connection);
        }

        public void AddChannel(Channel channel)
        {
            if (FindChannel(channel.ChannelName) != null)
                throw new ArgumentException("The channel already exists!".L10N("Client:Main:ChannelExist"), "channel");

            channels.Add(channel);
        }

        public void RemoveChannel(Channel channel)
        {
            if (channel.Persistent)
                throw new ArgumentException("Persistent channels cannot be removed.".L10N("Client:Main:PersistentChannelRemove"), "channel");

            channels.Remove(channel);
        }

        public IRCColor[] GetIRCColors()
        {
            return ircChatColors;
        }

        public void LeaveFromChannel(Channel channel)
        {
            connection.QueueMessage(QueuedMessageType.SYSTEM_MESSAGE, 10, "PART " + channel.ChannelName);

            if (!channel.Persistent)
                channels.Remove(channel);
        }

        public void SetMainChannel(Channel channel)
        {
            MainChannel = channel;
        }

        public void SendCustomMessage(QueuedMessage qm)
        {
            connection.QueueMessage(qm);
        }

        public void SendWhoIsMessage(string nick)
        {
            SendCustomMessage(new QueuedMessage($"WHOIS {nick}", QueuedMessageType.WHOIS_MESSAGE, 0));
        }

        public void OnAttemptedServerChanged(string serverName)
        {
            // Post is necessary for thread-safety; OnAttemptedServerChanged
            // is called by the networking thread, and Post schedules DoAttemptedServerChanged
            // to be executed on the UI thread.
            dispatcher.Post(() => DoAttemptedServerChanged(serverName));
        }

        private void DoAttemptedServerChanged(string serverName)
        {
            MainChannel.AddMessage(new ChatMessage(
                string.Format("Attempting connection to {0}".L10N("Client:Main:AttemptConnectToServer"), serverName)));
            AttemptedServerChanged?.Invoke(this, new AttemptedServerEventArgs(serverName));
        }

        public void OnAwayMessageReceived(string userName, string reason)
        {
            dispatcher.Post(() => DoAwayMessageReceived(userName, reason));
        }

        private void DoAwayMessageReceived(string userName, string reason)
        {
            AwayMessageReceived?.Invoke(this, new UserAwayEventArgs(userName, reason));
        }

        public void OnChannelFull(string channelName)
        {
            dispatcher.Post(() => DoChannelFull(channelName));
        }

        private void DoChannelFull(string channelName)
        {
            var channel = FindChannel(channelName);

            if (channel != null)
                channel.OnChannelFull();
        }

        public void OnTargetChangeTooFast(string channelName, string message)
        {
            dispatcher.Post(() => DoTargetChangeTooFast(channelName, message));
        }

        private void DoTargetChangeTooFast(string channelName, string message)
        {
            var channel = FindChannel(channelName);

            if (channel != null)
                channel.OnTargetChangeTooFast(message);
        }

        public void OnChannelInviteOnly(string channelName)
        {
            dispatcher.Post(() => DoChannelInviteOnly(channelName));
        }

        private void DoChannelInviteOnly(string channelName)
        {
            var channel = FindChannel(channelName);

            if (channel != null)
                channel.OnInviteOnlyOnJoin();
        }

        public void OnChannelModesChanged(string userName, string channelName, string modeString, List<string> modeParameters)
        {
            dispatcher.Post(() => DoChannelModesChanged(userName, channelName, modeString, modeParameters));
        }

        private void DoChannelModesChanged(string userName, string channelName, string modeString, List<string> modeParameters)
        {
            Channel channel = FindChannel(channelName);

            if (channel == null)
                return;

            ApplyChannelModes(channel, modeString, modeParameters);

            channel.OnChannelModesChanged(userName, modeString);
        }

        private void ApplyChannelModes(Channel channel, string modeString, List<string> modeParameters)
        {
            bool addMode = true;
            int parameterCount = 0;
            foreach (char modeChar in modeString)
            {
                if (modeChar == '+')
                    addMode = true;
                else if (modeChar == '-')
                    addMode = false;
                else
                {
                    switch (modeChar)
                    {
                        // Add/remove channel operator status on user.
                        case 'o':
                            if (parameterCount >= modeParameters.Count)
                                break;
                            string parameter = modeParameters[parameterCount++];
                            ChannelUser user = channel.Users.Find(parameter);
                            if (user == null)
                                break;
                            user.IsAdmin = addMode;
                            break;

                        // Add/remove voice status on user.
                        case 'v':
                            if (parameterCount >= modeParameters.Count)
                                break;
                            string vParam = modeParameters[parameterCount++];
                            ChannelUser vUser = channel.Users.Find(vParam);
                            if (vUser == null)
                                break;
                            vUser.IRCUser.HasVoice = addMode;
                            break;
                    }
                }
            }
        }

        public void OnChannelTopicReceived(string channelName, string topic)
        {
            dispatcher.Post(() => DoChannelTopicReceived(channelName, topic));
        }

        private void DoChannelTopicReceived(string channelName, string topic)
        {
            Channel channel = FindChannel(channelName);

            if (channel == null)
                return;

            channel.Topic = topic;
        }

        public void OnChannelTopicChanged(string userName, string channelName, string topic)
        {
            dispatcher.Post(() => DoChannelTopicReceived(channelName, topic));
        }

        public void OnChatMessageReceived(string receiver, string senderName, string ident, string message)
        {
            dispatcher.Post(() => DoChatMessageReceived(receiver, senderName, ident, message));
        }

        private void DoChatMessageReceived(string receiver, string senderName, string ident, string message)
        {
            Channel channel = FindChannel(receiver);

            if (channel == null)
                return;

            if (string.IsNullOrEmpty(message))
                return;

            try
            {
                Rgba32 foreColor;

                // Previously there was an "ACTION" handling, to be compatible with Funky's client, but we don't officially support Funky's client anymore.

                // Color parsing
                if (message.Contains(Convert.ToString((char)03)))
                {
                    if (message.Length < 3)
                    {
                        foreColor = cDefaultChatColor;
                    }
                    else
                    {
                        string colorString = message.Substring(1, 2);
                        message = message.Remove(0, 3);
                        int colorIndex = Conversions.IntFromString(colorString, -1);
                        // Try to parse message color info; if fails, use default color
                        if (colorIndex < ircChatColors.Length && colorIndex > -1)
                            foreColor = ircChatColors[colorIndex].XnaColor;
                        else
                            foreColor = cDefaultChatColor;
                    }
                }
                else
                    foreColor = cDefaultChatColor;

                if (message.Length > 1 && message[message.Length - 1] == '\u001f')
                    message = message.Remove(message.Length - 1);

                ChannelUser user = channel.Users.Find(senderName);
                bool senderIsAdmin = user != null && user.IsAdmin;

                channel.AddMessage(new ChatMessage(senderName, ident, senderIsAdmin, foreColor, DateTime.Now, message.Replace('\r', ' ')));
            }
            catch (Exception ex)
            {
                Logger.Log("Warning: failed to process IRC message (receiver=" + receiver + ", sender=" + senderName + ", ident=" + ident + "): " + ex);

#if DEBUG
                Debugger.Break();
#endif
            }
        }

        public void OnCTCPParsed(string channelName, string userName, string message)
        {
            dispatcher.Post(() => DoCTCPParsed(channelName, userName, message));
        }

        private void DoCTCPParsed(string channelName, string userName, string message)
        {
            Channel channel = FindChannel(channelName);

            // it's possible that we received this CTCP via PRIVMSG, in which case we
            // expect our username instead of a channel as the first parameter
            if (channel == null)
            {
                if (channelName == ProgramConstants.PLAYERNAME)
                {
                    PrivateCTCPEventArgs e = new PrivateCTCPEventArgs(userName, message);

                    PrivateCTCPReceived?.Invoke(this, e);
                }

                return;
            }

            channel.OnCTCPReceived(userName, message);
        }

        public void OnConnectAttemptFailed()
        {
            dispatcher.Post(DoConnectAttemptFailed);
        }

        private void DoConnectAttemptFailed()
        {
            ConnectAttemptFailed?.Invoke(this, EventArgs.Empty);

            MainChannel.AddMessage(new ChatMessage(ColorConstants.Red, "Connecting to CnCNet failed!".L10N("Client:Main:ConnectToCncNetFailed")));
        }

        public void OnConnected()
        {
            dispatcher.Post(DoConnected);
        }

        private void DoConnected()
        {
            connected = true;
            Connected?.Invoke(this, EventArgs.Empty);
            MainChannel.AddMessage(new ChatMessage("Connection to CnCNet established.".L10N("Client:Main:ConnectToCncNetSuccess")));
        }

        /// <summary>
        /// Called when the connection has got cut un-intentionally.
        /// </summary>
        /// <param name="reason"></param>
        public void OnConnectionLost(string reason)
        {
            dispatcher.Post(() => DoConnectionLost(reason));
        }

        private void DoConnectionLost(string reason)
        {
            ConnectionLost?.Invoke(this, new ConnectionLostEventArgs(reason));

            for (int i = 0; i < channels.Count; i++)
            {
                if (!channels[i].Persistent)
                {
                    channels.RemoveAt(i);
                    i--;
                }
                else
                {
                    channels[i].ClearUsers();
                }
            }

            UserList.Clear();

            MainChannel.AddMessage(new ChatMessage(ColorConstants.Red, "Connection to CnCNet has been lost.".L10N("Client:Main:ConnectToCncNetHasLost")));
            connected = false;
        }

        /// <summary>
        /// Disconnects from CnCNet.
        /// </summary>
        public void Disconnect()
        {
            connection.Disconnect();
            disconnect = true;
        }

        /// <summary>
        /// Connects to CnCNet.
        /// </summary>
        public void Connect()
        {
            disconnect = false;
            // 移植版不再由主窗口初始化 MainChannel，这里懒创建默认持久化主频道，
            // 避免 Connect()/OnConnected/断线重连路径访问 MainChannel 时抛 NullReferenceException。
            if (MainChannel is null)
                SetMainChannel(CreateDefaultMainChannel());

            MainChannel.AddMessage(new ChatMessage("Connecting to CnCNet...".L10N("Client:Main:ConnectingToCncNet")));
            connection.ConnectAsync();
        }

        private Channel CreateDefaultMainChannel()
        {
            string mainChannelName = gameCollection.GameList
                .FirstOrDefault(g => g.AlwaysEnabled && !string.IsNullOrEmpty(g.ChatChannel))?.ChatChannel
                ?? "#cncnet";

            var channel = CreateChannel("CnCNet", mainChannelName, persistent: true, isChatChannel: true, password: null);
            AddChannel(channel);
            channel.Join();
            return channel;
        }

        /// <summary>
        /// Called when the connection has been aborted intentionally.
        /// </summary>
        public void OnDisconnected()
        {
            dispatcher.Post(DoDisconnected);
        }

        private void DoDisconnected()
        {
            for (int i = 0; i < channels.Count; i++)
            {
                if (!channels[i].Persistent)
                {
                    channels.RemoveAt(i);
                    i--;
                }
                else
                {
                    channels[i].ClearUsers();
                }
            }

            MainChannel.AddMessage(new ChatMessage("You have disconnected from CnCNet.".L10N("Client:Main:CncNetDisconnected")));
            connected = false;

            UserList.Clear();

            Disconnected?.Invoke(this, EventArgs.Empty);
        }

        public void OnErrorReceived(string errorMessage)
        {
            MainChannel.AddMessage(new ChatMessage(ColorConstants.Red, errorMessage));
        }

        public void OnGenericServerMessageReceived(string message)
        {
            dispatcher.Post(() => DoGenericServerMessageReceived(message));
        }

        private void DoGenericServerMessageReceived(string message)
        {
            MainChannel.AddMessage(new ChatMessage(message));
        }

        public void OnIncorrectChannelPassword(string channelName)
        {
            dispatcher.Post(() => DoIncorrectChannelPassword(channelName));
        }

        private void DoIncorrectChannelPassword(string channelName)
        {
            var channel = FindChannel(channelName);
            if (channel != null)
                channel.OnInvalidJoinPassword();
        }

        public void OnNoticeMessageParsed(string notice, string userName)
        {
            // TODO Parse as private message
        }

        public void OnPrivateMessageReceived(string sender, string message)
        {
            dispatcher.Post(() => DoPrivateMessageReceived(sender, message));
        }

        private void DoPrivateMessageReceived(string sender, string message)
        {
            CnCNetPrivateMessageEventArgs e = new CnCNetPrivateMessageEventArgs(sender, message);

            PrivateMessageReceived?.Invoke(this, e);
        }

        public void OnReconnectAttempt()
        {
            dispatcher.Post(DoReconnectAttempt);
        }

        private void DoReconnectAttempt()
        {
            ReconnectAttempt?.Invoke(this, EventArgs.Empty);

            MainChannel.AddMessage(new ChatMessage("Attempting to reconnect to CnCNet...".L10N("Client:Main:ReconnectingCncNet")));

            connection.ConnectAsync();
        }

        public void OnUserJoinedChannel(string channelName, string host, string userName, string ident)
        {
            dispatcher.Post(() => DoUserJoinedChannel(channelName, host, userName, ident));
        }

        private void DoUserJoinedChannel(string channelName, string host, string userName, string userAddress)
        {
            Channel channel = FindChannel(channelName);

            if (channel == null)
                return;

            bool isAdmin = false;
            string name = userName;

            if (userName.StartsWith("@"))
            {
                isAdmin = true;
                name = userName.Remove(0, 1);
            }

            IRCUser ircUser = null;

            // Check if we already know this user from another channel
            // Avoid LINQ here for performance reasons
            foreach (var user in UserList)
            {
                if (user.Name == name)
                {
                    ircUser = (IRCUser)user.Clone();
                    break;
                }
            }

            // If we don't know the user, create a new one
            if (ircUser == null)
            {
                string identifier = userAddress.Split('@')[0];
                string[] parts = identifier.Split('.');
                ircUser = new IRCUser(name, identifier, host);

                if (parts.Length > 1)
                {
                    ircUser.GameID = gameCollection.GameList.FindIndex(g => g.InternalName.ToUpper() == parts[0].Replace("~", string.Empty));
                }

                AddUserToGlobalUserList(ircUser);
            }

            var channelUser = new ChannelUser(ircUser);
            channelUser.IsAdmin = isAdmin;
            channelUser.IsFriend = cncNetUserData.IsFriend(channelUser.IRCUser.Name);

            ircUser.Channels.Add(channelName);
            channel.OnUserJoined(channelUser);

            //UserJoinedChannel?.Invoke(this, new ChannelUserEventArgs(channelName, userName));
        }

        private void AddUserToGlobalUserList(IRCUser user)
        {
            UserList.Add(user);
            UserList = UserList.OrderBy(u => u.Name).ToList();
            UserAdded?.Invoke(this, new UserEventArgs(user));
        }

        public void OnUserKicked(string channelName, string userName)
        {
            dispatcher.Post(() => DoUserKicked(channelName, userName));
        }

        private void DoUserKicked(string channelName, string userName)
        {
            Channel channel = FindChannel(channelName);

            if (channel == null)
                return;

            channel.OnUserKicked(userName);

            if (userName == ProgramConstants.PLAYERNAME)
            {
                channel.Users.DoForAllUsers(user =>
                {
                    RemoveChannelFromUser(user.IRCUser.Name, channelName);
                });

                if (!channel.Persistent)
                    channels.Remove(channel);

                channel.ClearUsers();
                return;
            }

            RemoveChannelFromUser(userName, channelName);
        }

        public void OnUserLeftChannel(string channelName, string userName)
        {
            dispatcher.Post(() => DoUserLeftChannel(channelName, userName));
        }

        private void DoUserLeftChannel(string channelName, string userName)
        {
            Channel channel = FindChannel(channelName);

            if (channel == null)
                return;

            channel.OnUserLeft(userName);

            if (userName == ProgramConstants.PLAYERNAME)
            {
                channel.Users.DoForAllUsers(user =>
                {
                    RemoveChannelFromUser(user.IRCUser.Name, channelName);
                });

                if (!channel.Persistent)
                    channels.Remove(channel);

                channel.ClearUsers();

                return;
            }

            RemoveChannelFromUser(userName, channelName);
        }

        /// <summary>
        /// Looks up an user in the global user list and removes a channel from the user.
        /// If the user is left with 0 channels (meaning we have no common channel with the user),
        /// the user is removed from the global user list.
        /// </summary>
        /// <param name="userName">The name of the user.</param>
        /// <param name="channelName">The name of the channel.</param>
        public void RemoveChannelFromUser(string userName, string channelName)
        {
            var userIndex = UserList.FindIndex(user => user.Name.ToLower() == userName.ToLower());
            if (userIndex > -1)
            {
                var ircUser = UserList[userIndex];
                ircUser.Channels.Remove(channelName);

                if (ircUser.Channels.Count == 0)
                {
                    UserList.RemoveAt(userIndex);
                    UserRemoved?.Invoke(this, new UserNameIndexEventArgs(userIndex, userName));
                }
            }
        }

        public void OnUserListReceived(string channelName, string[] userList)
        {
            dispatcher.Post(() => DoUserListReceived(channelName, userList));
        }

        private void DoUserListReceived(string channelName, string[] userList)
        {
            Channel channel = FindChannel(channelName);

            if (channel == null)
                return;

            var channelUserList = new List<ChannelUser>();

            foreach (string userName in userList)
            {
                string name = userName;
                bool isAdmin = false;
                bool hasVoice = false;

                if (userName.StartsWith("@"))
                {
                    isAdmin = true;
                    name = userName.Substring(1);
                }
                else if (userName.StartsWith("+"))
                {
                    hasVoice = true;
                    name = userName.Substring(1);
                }

                // Check if we already know the IRC user from another channel
                IRCUser ircUser = UserList.Find(u => u.Name == name);

                // If the user isn't familiar to us already,
                // create a new user instance and add it to the global user list
                if (ircUser == null)
                {
                    ircUser = new IRCUser(name);
                    UserList.Add(ircUser);
                }

                var channelUser = new ChannelUser(ircUser);
                channelUser.IsAdmin = isAdmin;
                channelUser.IsFriend = cncNetUserData.IsFriend(channelUser.IRCUser.Name);
                channelUser.IRCUser.HasVoice = hasVoice;

                channelUserList.Add(channelUser);
            }

            UserList = UserList.OrderBy(u => u.Name).ToList();
            MultipleUsersAdded?.Invoke(this, EventArgs.Empty);

            channel.OnUserListReceived(channelUserList);
        }

        public void OnUserQuitIRC(string userName)
        {
            dispatcher.Post(() => DoUserQuitIRC(userName));
        }

        private void DoUserQuitIRC(string userName)
        {
            new List<Channel>(channels).ForEach(ch => ch.OnUserQuitIRC(userName));

            int userIndex = UserList.FindIndex(user => user.Name == userName);

            if (userIndex > -1)
            {
                UserList.RemoveAt(userIndex);
                UserRemoved?.Invoke(this, new UserNameIndexEventArgs(userIndex, userName));
            }
        }

        public void OnWelcomeMessageReceived(string message)
        {
            dispatcher.Post(() => DoWelcomeMessageReceived(message));
        }


        /// <summary>
        /// Finds a channel with the specified internal name, case-insensitively.
        /// </summary>
        /// <param name="channelName">The internal name of the channel.</param>
        /// <returns>A channel if one matching the name is found, otherwise null.</returns>
        public Channel FindChannel(string channelName)
        {
            channelName = channelName.ToLower();

            foreach (var channel in channels)
            {
                if (channel.ChannelName.ToLower() == channelName)
                    return channel;
            }

            return null;
        }

        private void DoWelcomeMessageReceived(string message)
        {
            channels.ForEach(ch => ch.AddMessage(new ChatMessage(message)));

            WelcomeMessageReceived?.Invoke(this, new ServerMessageEventArgs(message));
        }

        public void OnWhoReplyReceived(string ident, string hostName, string userName, string extraInfo)
        {
            dispatcher.Post(() => DoWhoReplyReceived(ident, hostName, userName, extraInfo));
        }

        private void DoWhoReplyReceived(string ident, string hostName, string userName, string extraInfo)
        {
            WhoReplyReceived?.Invoke(this, new WhoEventArgs(ident, userName, extraInfo));

            string[] eInfoParts = extraInfo.Split(' ');

            int gameIndex = -1;
            if (eInfoParts.Length > 2)
            {
                string gameName = eInfoParts[2];

                gameIndex = gameCollection.GetGameIndexFromInternalName(gameName);

                if (gameIndex == -1)
                    return;
            }

            var user = UserList.Find(u => u.Name == userName);
            if (user != null)
            {
                user.GameID = gameIndex;
                user.Ident = ident;
                user.Hostname = hostName;

                if (gameIndex != -1)
                {
                    channels.ForEach(ch => ch.UpdateGameIndexForUser(userName));
                    UserGameIndexUpdated?.Invoke(this, new UserEventArgs(user));
                }
            }
        }

        public bool GetDisconnectStatus()
        {
            return disconnect;
        }

        public void OnNameAlreadyInUse()
        {
            dispatcher.Post(DoNameAlreadyInUse);
        }

        /// <summary>
        /// Handles situations when the requested name is already in use by another
        /// IRC user. Adds additional underscores to the name or replaces existing
        /// characters with underscores.
        /// </summary>
        private void DoNameAlreadyInUse()
        {
            var charList = ProgramConstants.PLAYERNAME.ToList();
            int maxNameLength = ClientConfiguration.Instance.MaxNameLength;

            if (charList.Count < maxNameLength)
                charList.Add('_');
            else
            {
                int lastNonUnderscoreIndex = charList.FindLastIndex(c => c != '_');

                if (lastNonUnderscoreIndex == -1)
                {
                    MainChannel.AddMessage(new ChatMessage(ColorConstants.White,
                        "Your nickname is invalid or already in use. Please change your nickname in the login screen.".L10N("Client:Main:PickAnotherNickName")));
                    UserINISettings.Instance.SkipConnectDialog.Value = false;
                    Disconnect();
                    return;
                }

                charList[lastNonUnderscoreIndex] = '_';
            }

            var sb = new StringBuilder();
            foreach (char c in charList)
                sb.Append(c);

            MainChannel.AddMessage(new ChatMessage(ColorConstants.White,
                string.Format("Your name is already in use. Retrying with {0}...".L10N("Client:Main:NameInUseRetry"), sb.ToString())));

            ProgramConstants.PLAYERNAME = sb.ToString();
            connection.ChangeNickname();
        }

        public void OnBannedFromChannel(string channelName)
        {
            dispatcher.Post(() => DoBannedFromChannel(channelName));
        }

        private void DoBannedFromChannel(string channelName)
        {
            BannedFromChannel?.Invoke(this, new ChannelEventArgs(channelName));
        }

        public void OnUserNicknameChange(string oldNickname, string newNickname)
            => dispatcher.Post(() => DoUserNicknameChange(oldNickname, newNickname));

        private void DoUserNicknameChange(string oldNickname, string newNickname)
        {
            IRCUser user = UserList.Find(u => u.Name.ToUpper() == oldNickname.ToUpper());
            if (user == null)
            {
                Logger.Log("DoUserNicknameChange: Failed to find user with nickname " + oldNickname);
                return;
            }
            string realOldNickname = user.Name; // To make sure that case matches
            user.Name = newNickname;

            channels.ForEach(ch => ch.OnUserNameChanged(realOldNickname, newNickname));
        }

        public void OnServerLatencyTested(int candidateCount, int closerCount)
        {
            dispatcher.Post(() => DoServerLatencyTested(candidateCount, closerCount));
        }

        private void DoServerLatencyTested(int candidateCount, int closerCount)
        {
            MainChannel.AddMessage(new ChatMessage(
                string.Format(
                    "Lobby servers: {0} available, {1} fast.".L10N("Client:Main:LobbyServerLatencyTestResult"),
                    candidateCount, closerCount)));
        }
    }

    public class UserEventArgs : EventArgs
    {
        public UserEventArgs(IRCUser ircUser)
        {
            User = ircUser;
        }

        public IRCUser User { get; private set; }
    }

    public class IndexEventArgs : EventArgs
    {
        public IndexEventArgs(int index)
        {
            Index = index;
        }

        public int Index { get; private set; }
    }

    public class UserNameChangedEventArgs : EventArgs
    {
        public UserNameChangedEventArgs(string oldUserName, IRCUser user)
        {
            OldUserName = oldUserName;
            User = user;
        }

        public string OldUserName { get; }
        public IRCUser User { get; }
    }
}

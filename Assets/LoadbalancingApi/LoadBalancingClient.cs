﻿// -----------------------------------------------------------------------
// <copyright file="LoadBalancingClient.cs" company="Exit Games GmbH">
//   Loadbalancing Framework for Photon - Copyright (C) 2011 Exit Games GmbH
// </copyright>
// <summary>
//   Provides the operations and a state for games using the
//   Photon LoadBalancing server.
// </summary>
// <author>developer@exitgames.com</author>
// ----------------------------------------------------------------------------

namespace ExitGames.Client.Photon.LoadBalancing
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Diagnostics;

#if UNITY_EDITOR || UNITY_STANDALONE_OSX || UNITY_DASHBOARD_WIDGET || UNITY_STANDALONE_WIN || UNITY_STANDALONE_LINUX || UNITY_STANDALONE || UNITY_WEBPLAYER || UNITY_WII || UNITY_IPHONE || UNITY_ANDROID || UNITY_PS3 || UNITY_XBOX360 || UNITY_NACL  || UNITY_FLASH  || UNITY_BLACKBERRY
    using Hashtable = ExitGames.Client.Photon.Hashtable;
#endif

    #region Enums

    /// <summary>Possible states for a LoadBalancingClient.</summary>
    public enum ClientState
    {
        /// <summary>Peer is created but not used yet.</summary>
        Uninitialized,
        /// <summary>Connecting to master (includes connect, authenticate and joining the lobby)</summary>
        ConnectingToMasterserver,
        /// <summary>Connected to master server.</summary>
        ConnectedToMaster,
        /// <summary>Currently not used.</summary>
        Queued,
        /// <summary>Usually when Authenticated, the client will join a game or the lobby (if AutoJoinLobby is true).</summary>
        Authenticated,
        /// <summary>Connected to master and joined lobby. Display room list and join/create rooms at will.</summary>
        JoinedLobby,
        /// <summary>Transition from master to game server.</summary>
        DisconnectingFromMasterserver,
        /// <summary>Transition to gameserver (client will authenticate and join/create game).</summary>
        ConnectingToGameserver,
        /// <summary>Connected to gameserver (going to auth and join game).</summary>
        ConnectedToGameserver,
        /// <summary>Joining game on gameserver.</summary>
        Joining,
        /// <summary>The client arrived inside a room. CurrentRoom and Players are known. Send events with OpRaiseEvent.</summary>
        Joined,
        /// <summary>Currently not used. Instead of OpLeave, the client disconnects from a server (which also triggers a leave immediately).</summary>
        Leaving,
        /// <summary>Currently not used.</summary>
        Left,
        /// <summary>Transition from gameserver to master (after leaving a room/game).</summary>
        DisconnectingFromGameserver,
        /// <summary>Currently not used.</summary>
        QueuedComingFromGameserver,
        /// <summary>The client disconnects (from any server).</summary>
        Disconnecting,
        /// <summary>The client is no longer connected (to any server). Connect to master to go on.</summary>
        Disconnected,
        /// <summary>Client connects to the NameServer. This process includes low level connecting and setting up encryption. When done, state becomes ConnectedToNameServer.</summary>
        ConnectingToNameServer,
        /// <summary>Client is connected to the NameServer and established enctryption already. You should call OpGetRegions or ConnectToRegionMaster.</summary>
        ConnectedToNameServer,
        /// <summary>Client authenticates itself with the server. On the Photon Cloud this sends the AppId of your game. Used with Master Server and Game Server.</summary>
        Authenticating,
        /// <summary>Clients disconnects (specifically) from the NameServer to reconnect to the master server.</summary>
        DisconnectingFromNameServer
    }

    /// <summary>Ways a room can be created or joined.</summary>
    public enum JoinType
    {
        /// <summary>This client creates a room, gets into it (no need to join) and can set room properties.</summary>
        CreateRoom,
        /// <summary>The room existed already and we join into it (not setting room properties).</summary>
        JoinRoom,
        /// <summary>Done on Master Server and (if successful) followed by a Join on Game Server.</summary>
        JoinRandomRoom,
        /// <summary>Client is either joining or creating a room. On Master- and Game-Server.</summary>
        JoinOrCreateRoom
    }

    /// <summary>Enumaration of causes for Disconnects (used in LoadBalancingClient.DisconnectedCause).</summary>
    /// <remarks>Read the individual descriptions to find out what to do about this type of disconnect.</remarks>
    public enum DisconnectCause
    {
        /// <summary>No error was tracked.</summary>
        None,
        /// <summary>OnStatusChanged: The CCUs count of your Photon Server License is exausted (temporarily).</summary>
        DisconnectByServerUserLimit,
        /// <summary>OnStatusChanged: The server is not available or the address is wrong. Make sure the port is provided and the server is up.</summary>
        ExceptionOnConnect,
        /// <summary>OnStatusChanged: The server disconnected this client. Most likely the server's send buffer is full (receiving too much from other clients).</summary>
        DisconnectByServer,
        /// <summary>OnStatusChanged: This client detected that the server's responses are not received in due time. Maybe you send / receive too much?</summary>
        TimeoutDisconnect,
        /// <summary>OnStatusChanged: Some internal exception caused the socket code to fail. Contact Exit Games.</summary>
        Exception,
        /// <summary>OnOperationResponse: Authenticate in the Photon Cloud with invalid AppId. Update your subscription or contact Exit Games.</summary>
        InvalidAuthentication,
        /// <summary>OnOperationResponse: Authenticate (temporarily) failed when using a Photon Cloud subscription without CCU Burst. Update your subscription.</summary>
        MaxCcuReached,
        /// <summary>OnOperationResponse: Authenticate when the app's Photon Cloud subscription is locked to some (other) region(s). Update your subscription or master server address.</summary>
        InvalidRegion,
        /// <summary>OnOperationResponse: Operation that's (currently) not available for this client (not authorized usually). Only tracked for op Authenticate.</summary>
        OperationNotAllowedInCurrentState,
        /// <summary>OnOperationResponse: Authenticate in the Photon Cloud with invalid client values or custom authentication setup in Cloud Dashboard.</summary>
        CustomAuthenticationFailed,
    }

    #endregion

    /// <summary>
    /// This class implements the Photon LoadBalancing workflow by using a LoadBalancingPeer.
    /// It keeps a state and will automatically execute transitions between the Master and Game Servers.
    /// </summary>
    /// <remarks>
    /// This class (and the Player class) should be extended to implement your own game logic.
    /// You can override CreatePlayer as "factory" method for Players and return your own Player instances.
    /// The State of this class is essential to know when a client is in a lobby (or just on the master)
    /// and when in a game where the actual gameplay should take place.
    /// Extension notes:
    /// An extension of this class should override the methods of the IPhotonPeerListener, as they
    /// are called when the state changes. Call base.method first, then pick the operation or state you
    /// want to react to and put it in a switch-case.
    /// We try to provide demo to each platform where this api can be used, so lookout for those.
    /// </remarks>
    public class LoadBalancingClient : IPhotonPeerListener
    {
        /// <summary>
        /// The client uses a LoadBalancingPeer as API to communicate with the server.
        /// This is public for ease-of-use: Some methods like OpRaiseEvent are not relevant for the connection state and don't need a override.
        /// </summary>
        public LoadBalancingPeer loadBalancingPeer;

        /// <summary>The version of your client. A new version also creates a new "virtual app" to separate players from older client versions.</summary>
        public string AppVersion { get; set; }

        /// <summary>The AppID as assigned from the Photon Cloud. If you host yourself, this is the "regular" Photon Server Application Name (most likely: "LoadBalancing").</summary>
        public string AppId { get; set; }

        /// <summary>A user's authentication values used during connect for Custom Authentication with Photon.</summary>
        /// <remarks>Set this property or pass AuthenticationValues by Connect(..., authValues) for custom authentication.</remarks>
        public AuthenticationValues CustomAuthenticationValues { get; set; }

        /// <summary>True if this client uses a NameServer to get the Master Server address.</summary>
        public bool IsUsingNameServer { get; private set; }

        /// <summary>Name Server Address that this client uses. You can use the default values and usually won't have to set this value.</summary>
        public string NameServerAddress = "ns.exitgamescloud.com:5058";

        /// <summary>The currently used server address (if any). The type of server is define by Server property.</summary>
        public string CurrentServerAddress { get { return this.loadBalancingPeer.ServerAddress; } }

        /// <summary>The master server's address. Defaults to "app-eu.exitgamescloud.com:5055". Can be changed before call of Connect.</summary>
        public string MasterServerAddress { get; internal protected set; }

        /// <summary>The game server's address for a particular room. In use temporarily, as assigned by master.</summary>
        private string GameServerAddress { get; set; }

        /// <summary>Available server (types) for internally used field: server.</summary>
        public enum ServerConnection
        {
            MasterServer,
            GameServer,
            NameServer
        }

        /// <summary>The server this client is currently connected or connecting to.</summary>
        public ServerConnection Server { get; private set; }

        /// <summary>Backing field for property.</summary>
        private ClientState state = ClientState.Uninitialized;

        /// <summary>Current state this client is in. Careful: several states are "transitions" that lead to other states.</summary>
        public ClientState State
        {
            get
            {
                return this.state;
            }

            protected internal set
            {
                this.state = value;
                if (OnStateChangeAction != null) OnStateChangeAction(this.state);
            }
        }

        /// <summary>Returns if this client is currently connected or connecting to some type of server.</summary>
        /// <remarks>This is even true while switching servers. Use IsConnectedAndReady to check only for those states that enable you to send Operations.</remarks>
        public bool IsConnected { get { return this.loadBalancingPeer != null && this.State != ClientState.Uninitialized && this.State != ClientState.Disconnected; } }


        /// <summary>
        /// A refined version of connected which is true only if your connection to the server is ready to accept operations like join, leave, etc.
        /// </summary>
        public bool IsConnectedAndReady
        {
            get
            {
                if (this.loadBalancingPeer == null)
                {
                    return false;
                }

                switch (this.State)
                {
                    case ClientState.Uninitialized:
                    case ClientState.Disconnected:
                    case ClientState.Disconnecting:
                    case ClientState.Authenticating:
                    case ClientState.ConnectingToGameserver:
                    case ClientState.ConnectingToMasterserver:
                    case ClientState.ConnectingToNameServer:
                    case ClientState.Joining:
                    case ClientState.Leaving:
                        return false;   // we are not ready to execute any operations
                }

                return true;
            }
        }

        public Action<ClientState> OnStateChangeAction { get; set; }

        /// <summary>Summarizes (aggregates) the different causes for disconnects of a client.</summary>
        /// <remarks>
        /// A disconnect can be caused by: errors in the network connection or some vital operation failing
        /// (which is considered "high level"). While operations always trigger a call to OnOperationResponse,
        /// connection related changes are treated in OnStatusChanged.
        /// The DisconnectCause is set in either case and summarizes the causes for any disconnect in a single
        /// state value which can be used to display (or debug) the cause for disconnection.
        /// </remarks>
        public DisconnectCause DisconnectedCause { get; protected set; }
        
        /// <summary>The lobby this client currently uses.</summary>
        public TypedLobby CurrentLobby { get; protected internal set; }

        /// \deprecated Use CurrentLobby.Name
        /// <summary>The name of the lobby this client currently uses.</summary>
        [Obsolete("Use CurrentLobby.Name")]
        public string CurrentLobbyName { get { return CurrentLobby.Name; } }

        /// \deprecated Use CurrentLobby.Type
        /// <summary>The type of the lobby this client currently uses. There are "default" and "SQL" typed lobbies. See: LobbyType.</summary>
        [Obsolete("Use CurrentLobby.Type")]
        public LobbyType CurrentLobbyType { get { return CurrentLobby.Type; } }

        /// <summary>Backing field for property.</summary>
        private bool autoJoinLobby = true;

        /// <summary>If your client should join random games, you can skip joining the lobby. Call OpJoinRandomRoom and create a room if that fails.</summary>
        public bool AutoJoinLobby
        {
            get
            {
                return this.autoJoinLobby;
            }

            set
            {
                this.autoJoinLobby = value;
            }
        }

        /// <summary>
        /// Same as client.LocalPlayer.Name
        /// </summary>
        public string PlayerName
        {
            get
            {
                return this.LocalPlayer.Name;
            }

            set
            {
                if (this.LocalPlayer == null)
                {
                    return;
                }

                this.LocalPlayer.Name = value;
            }
        }

        /// <summary>An ID for this user. Sent in OpAuthenticate when you connect. If not set, the PlayerName is applied during connect.</summary>
        /// <remarks>
        /// On connect, if the UserId is null or empty, the client will copy the PlayName to UserId. If PlayerName is not set either 
        /// (before connect), the server applies a temporary ID which stays unknown to this client and other clients.
        /// 
        /// The UserId is what's used in FindFriends and for fetching data for your account (with WebHooks e.g.).
        /// 
        /// By convention, set this ID before you connect, not while being connected. 
        /// There is no error but the ID won't change while being connected.
        /// </remarks>
        public string UserId { get; set; }

        /// <summary>This "list" is populated while being in the lobby of the Master. It contains RoomInfo per roomName (keys).</summary>
        public Dictionary<string, RoomInfo> RoomInfoList = new Dictionary<string, RoomInfo>();

        /// <summary>The current room this client is connected to (null if none available).</summary>
        public Room CurrentRoom;

        private RoomOptions createRoomOptions; // kept for game server call with same parameters

        /// <summary>Private field for LocalPlayer.</summary>
        private Player localPlayer;

        /// <summary>The local player is never null but not valid unless the client is in a room, too. The ID will be -1 outside of rooms.</summary>
        public Player LocalPlayer 
        { 
            get 
            {
                if (localPlayer == null)
                {
                    this.localPlayer = this.CreatePlayer(string.Empty, -1, true, null);
                }
                
                return this.localPlayer;
            }

            set
            {
                this.localPlayer = value;
            }
        }

        /// <summary>Statistic value available on master server: Players on master (looking for games).</summary>
        public int PlayersOnMasterCount { get; set; }

        /// <summary>Statistic value available on master server: Players in rooms (playing).</summary>
        public int PlayersInRoomsCount { get; set; }

        /// <summary>Statistic value available on master server: Rooms currently created.</summary>
        public int RoomsCount { get; set; }

        /// <summary>Internally used to decide if a room must be created or joined on game server.</summary>
        private JoinType lastJoinType;

        /// <summary>Internally used in OpJoin to store the actorNumber this client wants to claim in the room.</summary>
        private int lastJoinActorNumber;
        
        /// <summary>Internally used to trigger OpAuthenticate when encryption was established after a connect.</summary>
        private bool didAuthenticate;

        /// <summary>
        /// List of friends, their online status and the room they are in. Null until initialized by OpFindFriends response.
        /// </summary>
        /// <remarks>
        /// Do not modify this list! It's internally handled by OpFindFriends and meant as read-only.
        /// The value of FriendListAge gives you a hint how old the data is. Don't get this list more often than useful (> 10 seconds).
        /// In best case, keep the list you fetch really short. You could (e.g.) get the full list only once, then request a few updates
        /// only for friends who are online. After a while (e.g. 1 minute), you can get the full list again.
        /// </remarks>
        public List<FriendInfo> FriendList { get; private set; }

        /// <summary>Contains the list of names of friends to look up their state on the server.</summary>
        private string[] friendListRequested;

        /// <summary>
        /// Age of friend list info (in milliseconds). It's 0 until a friend list is fetched.
        /// </summary>
        public int FriendListAge { get { return (this.isFetchingFriendList || friendListTimestamp == 0) ? 0 : Environment.TickCount - friendListTimestamp; } }

        /// <summary>Private timestamp (in ms) of the last friendlist update.</summary>
        private int friendListTimestamp;

        /// <summary>Internal flag to know if the client currently fetches a friend list.</summary>
        private bool isFetchingFriendList;

        /// <summary>Internally used to check if a "Secret" is available to use. Sent by Photon Cloud servers, it simplifies authentication when switching servers.</summary>
        protected bool IsAuthorizeSecretAvailable
        {
            get
            {
                return this.CustomAuthenticationValues != null && !string.IsNullOrEmpty(this.CustomAuthenticationValues.Secret);
            }
        }

        /// <summary>A list of region names for the Photon Cloud. Set by the result of OpGetRegions().</summary>
        /// <remarks>Put a "case OperationCode.GetRegions:" into your OnOperationResponse method to notice when the result is available.</remarks>
        public string[] AvailableRegions { get; private set; }

        /// <summary>A list of region server (IP addresses with port) for the Photon Cloud. Set by the result of OpGetRegions().</summary>
        /// <remarks>Put a "case OperationCode.GetRegions:" into your OnOperationResponse method to notice when the result is available.</remarks>
        public string[] AvailableRegionsServers { get; private set; }

        /// <summary>The cloud region this client connects to. Set by ConnectToRegionMaster(). Not set if you don't use a NameServer!</summary>
        public string CloudRegion { get; private set; }


        public LoadBalancingClient(ConnectionProtocol protocol = ConnectionProtocol.Udp)
        {
            this.MasterServerAddress = "app-eu.exitgamescloud.com:5055";
            this.loadBalancingPeer = new LoadBalancingPeer(this, protocol);
        }

        public LoadBalancingClient(string masterAddress, string appId, string gameVersion, ConnectionProtocol protocol = ConnectionProtocol.Udp)
            : this(protocol)
        {
            this.MasterServerAddress = masterAddress;
            this.AppId = appId;
            this.AppVersion = gameVersion;
        }

        #region Operations and Commands

        /// \deprecated Use one of the Connect overloads instead.
        /// <summary>
        /// Starts the "process" to connect to the master server (initial connect).
        /// This includes connecting, establishing encryption, authentification and joining a lobby (if AutoJoinLobby is true).
        /// </summary>
        /// <param name="appId">Your application's name or ID assigned by Photon Cloud (webpage).</param>
        /// <param name="appVersion">The client's version (clients with differing client appVersions are separated and players don't meet).</param>
        /// <param name="playerName">This player's name.</param>
        /// <returns>If the operation could be send.</returns>
        [Obsolete("Use one of the Connect overloads instead.")]
        public bool ConnectToMaster(string appId, string appVersion, string playerName)
        {
            this.AppId = appId;
            this.AppVersion = appVersion;
            this.PlayerName = playerName;

            return Connect();
        }

        /// \deprecated Use one of the alternative overloads instead.
        /// <summary>
        /// Starts the "process" to connect to the master server (initial connect).
        /// This includes connecting, establishing encryption, authentification and joining a lobby (if AutoJoinLobby is true).
        /// </summary>
        /// <remarks>
        /// To connect to the Photon Cloud, a valid AppId must be provided. This is shown in the Photon Cloud Dashboard.
        /// https://cloud.exitgames.com/dashboard
        /// Connecting to the Photon Cloud might fail due to:
        /// - Network issues (OnStatusChanged() StatusCode.ExceptionOnConnect)
        /// - Region not available (OnOperationResponse() for OpAuthenticate with ReturnCode == ErrorCode.InvalidRegion)
        /// - Subscription CCU limit reached (OnOperationResponse() for OpAuthenticate with ReturnCode == ErrorCode.MaxCcuReached)
        /// More about the connection limitations:
        /// http://doc.exitgames.com/photon-cloud/SubscriptionErrorCases/#cat-references
        /// </remarks>
        [Obsolete("Use the alternative overloads instead.")]
        public bool Connect(string serverAddress, string appId)
        {
            this.MasterServerAddress = serverAddress;
            this.AppId = appId;

            return this.Connect();
        }

        /// <summary>
        /// Starts the "process" to connect to the master server. Relevant connection-values parameters can be set via parameters.
        /// </summary>
        /// <remarks>
        /// The process to connect includes several steps: the actual connecting, establishing encryption, authentification
        /// (of app and optionally the user) and joining a lobby (if AutoJoinLobby is true).
        ///
        /// Instead of providing all these parameters, you can also set the individual properties of a client before calling Connect().
        ///
        /// Users can connect either anonymously or use "Custom Authentication" to verify each individual player's login.
        /// Custom Authentication in Photon uses external services and communities to verify users. While the client provides a user's info,
        /// the service setup is done in the Photon Cloud Dashboard.
        /// The parameter authValues will set this.CustomAuthenticationValues and use them in the connect process.
        ///
        /// To connect to the Photon Cloud, a valid AppId must be provided. This is shown in the Photon Cloud Dashboard.
        /// https://cloud.exitgames.com/dashboard
        /// Connecting to the Photon Cloud might fail due to:
        /// - Network issues (OnStatusChanged() StatusCode.ExceptionOnConnect)
        /// - Region not available (OnOperationResponse() for OpAuthenticate with ReturnCode == ErrorCode.InvalidRegion)
        /// - Subscription CCU limit reached (OnOperationResponse() for OpAuthenticate with ReturnCode == ErrorCode.MaxCcuReached)
        /// More about the connection limitations:
        /// http://doc.exitgames.com/photon-cloud/SubscriptionErrorCases/#cat-references
        /// </remarks>
        /// <param name="serverAddress">Set a master server address instead of using the default. Uses default if null or empty.</param>
        /// <param name="appId">Your application's name or the AppID assigned by Photon Cloud (as listed in Dashboard). Uses default if null or empty.</param>
        /// <param name="appVersion">Can be used to separate users by their client's version (useful to add features without breaking older clients). Uses default if null or empty.</param>
        /// <param name="playerName">Optional name for this player. Provide a unique (!) userID to use the Friend Finding feature. Uses default if null or empty.</param>
        /// <param name="authValues">Set the AuthParameters property to use Custom Authentication (see above). Attempts anonymous auth if null.</param>
        /// <returns>If the operation could be send (can be false for bad server urls).</returns>
        public bool Connect(string serverAddress, string appId, string appVersion, string playerName, AuthenticationValues authValues)
        {
            if (!string.IsNullOrEmpty(serverAddress))
            {
                this.MasterServerAddress = serverAddress;
            }

            if (!string.IsNullOrEmpty(appId))
            {
                this.AppId = appId;
            }

            if (!string.IsNullOrEmpty(appVersion))
            {
                this.AppVersion = appVersion;
            }

            if (!string.IsNullOrEmpty(playerName))
            {
                this.PlayerName = playerName;
            }

            this.CustomAuthenticationValues = authValues;

            return this.Connect();
        }

        /// <summary>
        /// Starts the "process" to connect to the master server. Set relevant connection values by the properties of this instance.
        /// </summary>
        /// <remarks>
        /// The process to connect includes several steps: the actual connecting, establishing encryption, authentification
        /// (of app and optionally the user) and joining a lobby (if AutoJoinLobby is true).
        ///
        /// To connect to the Photon Cloud, a valid AppId must be provided. This is shown in the Photon Cloud Dashboard.
        /// https://cloud.exitgames.com/dashboard
        ///
        /// Users can connect either anonymously or use "Custom Authentication" to verify each individual player's login.
        /// Custom Authentication in Photon uses external services and communities to verify users. While the client provides a user's info,
        /// the service setup is done in the Photon Cloud Dashboard.
        /// The parameter authValues will set this.CustomAuthenticationValues and use them in the connect process.
        ///
        /// Connecting to the Photon Cloud might fail due to:
        /// - Network issues (OnStatusChanged() StatusCode.ExceptionOnConnect)
        /// - Region not available (OnOperationResponse() for OpAuthenticate with ReturnCode == ErrorCode.InvalidRegion)
        /// - Subscription CCU limit reached (OnOperationResponse() for OpAuthenticate with ReturnCode == ErrorCode.MaxCcuReached)
        /// More about the connection limitations:
        /// http://doc.exitgames.com/photon-cloud/SubscriptionErrorCases/#cat-references
        /// </remarks>
        public virtual bool Connect()
        {
            this.DisconnectedCause = DisconnectCause.None;

            if (this.loadBalancingPeer.Connect(this.MasterServerAddress, this.AppId))
            {
                this.State = ClientState.ConnectingToMasterserver;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Connects to the NameServer for Photon Cloud, where a region and server list can be obtained.
        /// </summary>
        /// <see cref="OpGetRegions"/>
        /// <returns>If the workflow was started or failed right away.</returns>
        public bool ConnectToNameServer()
        {
            this.IsUsingNameServer = true;
            this.CloudRegion = null;
            if (!this.loadBalancingPeer.Connect(NameServerAddress, "NameServer"))
            {
                return false;
            }

            this.State = ClientState.ConnectingToNameServer;
            return true;
        }

        /// <summary>
        /// While on the NameServer, this gets you the list of regional servers (short names and their IPs to ping them).
        /// </summary>
        /// <returns>If the operation could be sent. If false, no operation was sent (e.g. while not connected to the NameServer).</returns>
        public bool OpGetRegions()
        {
            if (this.Server != ServerConnection.NameServer)
            {
                return false;
            }

            bool sent = this.loadBalancingPeer.OpGetRegions(this.AppId);
            if (sent)
            {
                this.AvailableRegions = null;
            }

            return sent;
        }

        /// <summary>
        /// Connects you to a specific region's Master Server, using the Name Server to find the IP.
        /// </summary>
        /// <returns>If the operation could be sent. If false, no operation was sent.</returns>
        public bool ConnectToRegionMaster(string region)
        {
            this.IsUsingNameServer = true;

            if (this.State == ClientState.ConnectedToNameServer)
            {
                this.CloudRegion = region;
                return this.loadBalancingPeer.OpAuthenticate(this.AppId, this.AppVersion, (this.UserId ?? this.PlayerName), this.CustomAuthenticationValues, region);
            }

            this.CloudRegion = region;
            if (!this.loadBalancingPeer.Connect(NameServerAddress, "NameServer"))
            {
                return false;
            }

            this.State = ClientState.ConnectingToNameServer;
            return true;
        }

        /// <summary>Disconnects this client from any server and sets this.State if the connection is successfuly closed.</summary>
        public void Disconnect()
        {
            if (this.State != ClientState.Disconnected)
            {
                this.State = ClientState.Disconnecting;
                this.loadBalancingPeer.Disconnect();
                
                // we can set this high-level state if the low-level (connection)state is "disconnected"
                if (this.loadBalancingPeer.PeerState == PeerStateValue.Disconnected || this.loadBalancingPeer.PeerState == PeerStateValue.InitializingApplication)
                {
                    this.State = ClientState.Disconnected;
                }
            }
        }


        /// <summary>
        /// This method excutes dispatches all incoming commands and sends anything in the outgoing queues via 
        /// DispatchIncomingCommands and SendOutgoingCommands.
        /// </summary>
        /// <remarks>
        /// The Photon client libraries are designed to fit easily into a game or application. The application
        /// is in control of the context (thread) in which incoming events and responses are executed and has
        /// full control of the creation of UDP/TCP packages.
        ///
        /// Sending packages and dispatching received messages are two separate tasks. Service combines them
        /// into one method at the cost of control. It calls DispatchIncomingCommands and SendOutgoingCommands.
        ///
        /// Call this method regularly (2..20 times a second).
        ///
        /// This will Dispatch ANY received commands (unless a reliable command in-order is still missing) and 
        /// events AND will send queued outgoing commands. Fewer calls might be more effective if a device 
        /// cannot send many packets per second, as multiple operations might be combined into one package.
        /// </remarks>
        /// <example>
        /// You could replace Service by:
        ///
        ///     while (DispatchIncomingCommands()); //Dispatch until everything is Dispatched...
        ///     SendOutgoingCommands(); //Send a UDP/TCP package with outgoing messages
        /// </example>
        /// <seealso cref="PhotonPeer.DispatchIncomingCommands"/>
        /// <seealso cref="PhotonPeer.SendOutgoingCommands"/>
        public void Service()
        {
            if (this.loadBalancingPeer != null)
            {
                this.loadBalancingPeer.Service();
            }
        }

        /// <summary>
        /// Private Disconnect variant that sets the state, too.
        /// </summary>
        private void DisconnectToReconnect()
        {
            switch (this.Server)
            {
                case ServerConnection.NameServer:
                    this.State = ClientState.DisconnectingFromNameServer;
                    break;
                case ServerConnection.MasterServer:
                    this.State = ClientState.DisconnectingFromMasterserver;
                    break;
                case ServerConnection.GameServer:
                    this.State = ClientState.DisconnectingFromGameserver;
                    break;
            }

            this.loadBalancingPeer.Disconnect();
        }

        /// <summary>
        /// Privately used only.
        /// Starts the "process" to connect to the game server (connect before a game is joined).
        /// </summary>
        private bool ConnectToGameServer()
        {
            if (this.loadBalancingPeer.Connect(this.GameServerAddress, this.AppId))
            {
                this.State = ClientState.ConnectingToGameserver;
                return true;
            }

            // TODO: handle error "cant connect to GS"
            return false;
        }

        /// \deprecated Use alternative with TypedLobby parameter.
        /// <summary>
        /// Joins the lobby on the Master Server, where you get a list of RoomInfos of currently open rooms.
        /// This is an async request which triggers a OnOperationResponse() call.
        /// </summary>
        /// <returns>If the operation could be sent (has to be connected).</returns>
        [Obsolete]
        public bool OpJoinLobby(string name, LobbyType lobbyType)
        {
            TypedLobby lobby = new TypedLobby(name, lobbyType);
            bool sent = this.loadBalancingPeer.OpJoinLobby(lobby);
            if (sent)
            {
                this.CurrentLobby = lobby;
            }

            return sent;
        }

        /// <summary>
        /// Joins the lobby on the Master Server, where you get a list of RoomInfos of currently open rooms.
        /// This is an async request which triggers a OnOperationResponse() call.
        /// </summary>
        /// <param name="lobby">The lobby join to. Use null for default lobby.</param>
        /// <returns>If the operation could be sent (has to be connected).</returns>
        public bool OpJoinLobby(TypedLobby lobby)
        {
            if (lobby == null)
            {
                lobby = TypedLobby.Default;
            }
            bool sent = this.loadBalancingPeer.OpJoinLobby(lobby);
            if (sent)
            {
                this.CurrentLobby = lobby;
            }

            return sent;
        }
        /// <summary>Opposite of joining a lobby. You don't have to explicitly leave a lobby to join another (client can be in one max, at any time).</summary>
        /// <returns>If the operation could be sent (has to be connected).</returns>
        public bool OpLeaveLobby()
        {
            return this.loadBalancingPeer.OpLeaveLobby();
        }

        /// <summary>
        /// Operation to join a random, available room.
        /// This operation fails if all rooms are closed or full.
        /// If successful, the result contains a gameserver address and the name of some room.
        /// </summary>
        /// <remarks>This override sets the state of the client.</remarks>
        /// <param name="expectedCustomRoomProperties">Optional. A room will only be joined, if it matches these custom properties (with string keys).</param>
        /// <param name="expectedMaxPlayers">Filters for a particular maxplayer setting. Use 0 to accept any maxPlayer value.</param>
        /// <returns>If the operation could be sent currently (requires connection).</returns>
        public bool OpJoinRandomRoom(Hashtable expectedCustomRoomProperties, byte expectedMaxPlayers)
        {
            return OpJoinRandomRoom(expectedCustomRoomProperties, expectedMaxPlayers, MatchmakingMode.FillRoom, TypedLobby.Default, null);
        }

        /// <summary>
        /// Operation to join a random, available room.
        /// This operation fails if all rooms are closed or full.
        /// If successful, the result contains a gameserver address and the name of some room.
        /// </summary>
        /// <remarks>This override sets the state of the client.</remarks>
        /// <param name="expectedCustomRoomProperties">Optional. A room will only be joined, if it matches these custom properties (with string keys).</param>
        /// <param name="expectedMaxPlayers">Filters for a particular maxplayer setting. Use 0 to accept any maxPlayer value.</param>
        /// <param name="matchmakingMode">Selects one of the available matchmaking algorithms. See MatchmakingMode enum for options.</param>
        /// <returns>If the operation could be sent currently (requires connection).</returns>
        public bool OpJoinRandomRoom(Hashtable expectedCustomRoomProperties, byte expectedMaxPlayers, MatchmakingMode matchmakingMode)
        {
            return this.OpJoinRandomRoom(expectedCustomRoomProperties, expectedMaxPlayers, matchmakingMode, TypedLobby.Default, null);
        }

        /// \deprecated Use overload with TypedLobby Parameter.
        /// <summary>Operation to join a random, available room.</summary>
        [Obsolete("Use overload with TypedLobby Parameter")]
        public bool OpJoinRandomRoom(Hashtable expectedCustomRoomProperties, byte expectedMaxPlayers, MatchmakingMode matchmakingMode, string lobbyName, LobbyType lobbyType, string sqlLobbyFilter)
        {
            return this.OpJoinRandomRoom(expectedCustomRoomProperties, expectedMaxPlayers, matchmakingMode, new TypedLobby() {Name = lobbyName, Type = lobbyType}, sqlLobbyFilter);
        }

        /// <summary>
        /// Operation to join a random, available room.
        /// This operation fails if all rooms are closed or full.
        /// If successful, the result contains a gameserver address and the name of some room.
        /// </summary>
        /// <remarks>This override sets the state of the client.</remarks>
        /// <param name="expectedCustomRoomProperties">Optional. A room will only be joined, if it matches these custom properties (with string keys).</param>
        /// <param name="expectedMaxPlayers">Filters for a particular maxplayer setting. Use 0 to accept any maxPlayer value.</param>
        /// <param name="matchmakingMode">Selects one of the available matchmaking algorithms. See MatchmakingMode enum for options.</param>
        /// <param name="lobby">The lobby in which to find a room. Use null for default lobby.</param>
        /// <param name="sqlLobbyFilter">Basically the "where" clause of a sql statement. Use null for random game. Examples: "C0 = 1 AND C2 > 50". "C5 = \"Map2\" AND C2 > 10 AND C2 < 20"</param>
        /// <returns>If the operation could be sent currently (requires connection).</returns>
        public bool OpJoinRandomRoom(Hashtable expectedCustomRoomProperties, byte expectedMaxPlayers, MatchmakingMode matchmakingMode, TypedLobby lobby, string sqlLobbyFilter)
        {
            if (lobby == null )
            {
                lobby = TypedLobby.Default;
            }
            this.State = ClientState.Joining;
            this.lastJoinType = JoinType.JoinRandomRoom;
            this.lastJoinActorNumber = 0;
            this.CurrentRoom = CreateRoom(null, new RoomOptions());

            Hashtable playerPropsToSend = null;
            if (this.Server == ServerConnection.GameServer)
            {
                playerPropsToSend = this.LocalPlayer.AllProperties;
            }

            this.CurrentLobby = lobby;
            return this.loadBalancingPeer.OpJoinRandomRoom(expectedCustomRoomProperties, expectedMaxPlayers, playerPropsToSend, matchmakingMode, lobby, sqlLobbyFilter);
        }

        /// <summary>
        /// Joins a room by name and sets this player's properties.
        /// </summary>
        /// <remarks>
        /// Join will try to enter a room by roomName. If the room is full or closed or not existing, this will fail.
        /// 
        /// This overrides the LoadBalancingPeer.OpJoinRoom to keep a state.
        /// </remarks>
        /// <param name="roomName">The name of the room to join. Must be existing already, open and non-full or can't be joined.</param>
        /// <returns>If the operation could be sent (has to be connected).</returns>
        public bool OpJoinRoom(string roomName)
        {
            return this.OpJoinRoom(roomName, 0);
        }

        /// \deprecated We introduced OpJoinOrCreateRoom to make the parameter createIfNotExists obsolete. Use either OpJoinRoom or OpJoinOrCreateRoom.
        /// <summary>
        /// Joins a room by name and sets this player's properties. Optionally, a room can be created if not existing already.
        /// </summary>
        /// <remarks>
        /// Join will try to enter a room by roomName. If the room is full or closed, this will fail.
        /// If the room is not existing, JoinRoom will also fail by default. 
        /// 
        /// You can set createIfNotExists to true to make the server instantly create the room if it doesn't exist.
        /// This makes it easier to get into the same room when several players exchanged a roomName already: 
        /// Any player can try to join or create the room in one step - it doesn't matter who's first.
        /// 
        /// If the room is created due to createIfNotExists, the LocalPlayer.ID will be 1 and LocalPlayer.IsMasterClient
        /// will be true as well.
        /// 
        /// This overrides the LoadBalancingPeer.OpJoinRoom to keep a state.
        /// </remarks>
        /// <param name="roomName">The name of the room to join. Must be existing already, open and non-full or can't be joined.</param>
        /// <param name="createIfNotExists">If true, the server will attempt to create a room.</param>
        /// <returns>If the operation could be sent (has to be connected).</returns>
        [Obsolete("We introduced OpJoinOrCreateRoom to make the parameter createIfNotExists obsolete. Use either OpJoinRoom or OpJoinOrCreateRoom.")]
        public bool OpJoinRoom(string roomName, bool createIfNotExists)
        {
            return OpJoinRoom(roomName, createIfNotExists, 0);
        }

        /// \deprecated We introduced OpJoinOrCreateRoom to make the parameter createIfNotExists obsolete. Use either OpJoinRoom or OpJoinOrCreateRoom.
        /// <summary>
        /// Joins a room by name and sets this player's properties. Optionally, a room can be created if not existing already.
        /// </summary>
        [Obsolete("We introduced OpJoinOrCreateRoom to make the parameter createIfNotExists obsolete. Use either OpJoinRoom or OpJoinOrCreateRoom.")]
        public bool OpJoinRoom(string roomName, bool createIfNotExists, int actorNumber)
        {
            if (createIfNotExists)
            {
                return this.OpJoinOrCreateRoom(roomName, actorNumber, null);
            }
            else
            {
                return this.OpJoinRoom(roomName, actorNumber);
            }
        }

        /// <summary>
        /// Joins a room by name and sets this player's properties. Optionally, a room can be created if not existing already.
        /// </summary>
        /// <remarks>
        /// Join will try to enter a room by roomName. If the room is full or closed, this will fail.
        /// If the room is not existing, JoinRoom will also fail by default.         
        /// This overrides the LoadBalancingPeer.OpJoinRoom to keep a state.
        /// </remarks>
        /// <param name="roomName">The name of the room to join. Must be existing already, open and non-full or can't be joined.</param>
        /// <param name="actorNumber">An actorNumber to claim in room in case the client re-joins a room. Use 0 to not claim an actorNumber.</param>
        /// <returns>If the operation could be sent (has to be connected).</returns>
        public bool OpJoinRoom(string roomName, int actorNumber)
        {
            this.State = ClientState.Joining;
            this.lastJoinType = JoinType.JoinRoom;
            this.lastJoinActorNumber = actorNumber;
            this.createRoomOptions = new RoomOptions();
            this.CurrentRoom = CreateRoom(roomName, this.createRoomOptions);

            Hashtable playerPropsToSend = null;
            if (this.Server == ServerConnection.GameServer)
            {
                playerPropsToSend = this.LocalPlayer.AllProperties;
            }

            bool onGameServer = this.Server == ServerConnection.GameServer;
            return this.loadBalancingPeer.OpJoinRoom(roomName, playerPropsToSend, actorNumber, this.createRoomOptions, false, onGameServer);
        }

        /// <summary>
        /// Joins a room by name or creates new room if room with given name not exists.
        /// </summary>
        /// <remarks>
        /// Join will try to enter a room by roomName. Unlike OpJoinRoom, this will create the room if it doesn't exist.
        /// </remarks>
        /// <param name="roomName">The name of the room to join. Must be existing already, open and non-full or can't be joined.</param>
        /// <param name="actorNumber">An actorNumber to claim in room in case the client re-joins a room. Use 0 to not claim an actorNumber.</param>
        /// <param name="roomOptions">Contains the parameters and properties of the new room. See RoomOptions class for a description of each.</param>
        /// <returns>If the operation could be sent (has to be connected).</returns>
        public bool OpJoinOrCreateRoom(string roomName, int actorNumber, RoomOptions roomOptions)
        {
            if (roomOptions == null)
            {
                roomOptions = new RoomOptions();
            }
            this.State = ClientState.Joining;
            this.lastJoinType = JoinType.JoinOrCreateRoom;
            this.lastJoinActorNumber = actorNumber;
            this.createRoomOptions = roomOptions;
            this.CurrentRoom = CreateRoom(roomName, roomOptions);

            Hashtable playerPropsToSend = null;
            if (this.Server == ServerConnection.GameServer)
            {
                playerPropsToSend = this.LocalPlayer.AllProperties;
            }

            bool onGameServer = this.Server == ServerConnection.GameServer;
            return this.loadBalancingPeer.OpJoinRoom(roomName, playerPropsToSend, actorNumber, roomOptions, true, onGameServer);
        }

        /// \deprecated Use overload with RoomOptions parameter.
        /// <summary>
        /// Creates a new room on the server (or fails when the name is already taken).
        /// </summary>
        [Obsolete("Use overload with RoomOptions parameter.")]
        public bool OpCreateRoom(string roomName, byte maxPlayers, Hashtable customGameProperties, string[] propsListedInLobby)
        {
            return this.OpCreateRoom(roomName, true, true, maxPlayers, customGameProperties, propsListedInLobby, null, LobbyType.Default);
        }

        /// \deprecated Use overload with RoomOptions parameter.
        /// <summary>
        /// Creates a new room on the server (or fails when the name is already taken).
        /// </summary>
        [Obsolete("Use overload with RoomOptions parameter.")]
        public bool OpCreateRoom(string roomName, bool isVisible, bool isOpen, byte maxPlayers, Hashtable customGameProperties, string[] propsListedInLobby)
        {
            return this.OpCreateRoom(roomName, isVisible, isOpen, maxPlayers, customGameProperties, propsListedInLobby, null, LobbyType.Default);
        }

        /// \deprecated Use overload with RoomOptions parameter.
        /// <summary>
        /// Creates a new room on the server (or fails when the name is already taken).
        /// </summary>
        [Obsolete("Use overload with RoomOptions parameter.")]
        public bool OpCreateRoom(string roomName, bool isVisible, bool isOpen, byte maxPlayers, Hashtable customGameProperties, string[] propsListedInLobby, string lobbyName, LobbyType lobbyType)
        {
            return this.OpCreateRoom(roomName, isVisible, isOpen, maxPlayers, customGameProperties, propsListedInLobby, null, LobbyType.Default, 0, 0);
        }

        /// \deprecated Use overload with RoomOptions parameter.
        ///  <summary>
        ///  Creates a new room on the server (or fails when the name is already taken).
        ///  </summary>
        ///  <remarks>
        ///  If you don't want to create a unique room-name, pass null or "" as name and the server will assign a
        ///  roomName (a GUID as string). Room names are unique.
        ///  
        ///  They will be attached to the specified lobby (by name and type). Use null as lobbyName to attach the 
        ///  room to the default lobby.
        ///  Multiple lobbies can help separate players by map or skill or game type. Each room can only be found 
        ///  in one lobby (no matter if defined by name and type or as default).
        /// 
        ///  The response depends on the server the peer is connected to:
        ///  Master will return a Game Server toon connect to.
        ///  Game Server will return the Room's data.
        ///  This is an async request which triggers a OnOperationResponse() call.
        ///  
        ///  This override sets the state of the client.
        ///  </remarks>
        ///  <param name="roomName">The name to create a room with. Must be unique and not in use or can't be created. If null, the server will assign a GUID as name.</param>
        ///  <param name="isVisible">Shows the room in the lobby's room list.</param>
        ///  <param name="isOpen">Keeps players from joining the room (or opens it to everyone).</param>
        ///  <param name="maxPlayers">Max players before room is considered full (but still listed).</param>
        ///  <param name="customGameProperties">Custom properties to apply to the room on creation (use string-typed keys but short ones).</param>
        ///  <param name="propsListedInLobby">Defines the custom room properties that get listed in the lobby. Null defaults to "none", a string[0].</param>
        ///  <param name="lobbyName">Name of the lobby this game gets added to. Default: null, attached to default lobby. Lobbies are unique per lobbyName plus lobbyType, so the same name can be used when several types are existing.</param>
        ///  <param name="lobbyType">Type of the (named)lobby this game gets added to.</param>
        ///  <param name="playerTtl">Defines how long (in milliseconds) a player can be disconnected (thus being inactive) before being removed from a game (for good).</param>
        ///  <param name="roomTtl">Defines how long the room will stay in memory of Photon until it gets cleaned up. Indirectly this also defines how long players can return. Turnbased games get stored/persisted after this timeout.</param>
        /// <returns>If the operation could be sent (has to be connected).</returns>
        [Obsolete("Use overload with RoomOptions parameter.")]
        public bool OpCreateRoom(string roomName, bool isVisible, bool isOpen, byte maxPlayers, Hashtable customGameProperties, string[] propsListedInLobby, string lobbyName, LobbyType lobbyType, int playerTtl, int roomTtl)
        {
            this.State = ClientState.Joining;
            this.lastJoinType = JoinType.CreateRoom;
            this.lastJoinActorNumber = 0;
            this.createRoomOptions = new RoomOptions()
            {
                IsVisible = isVisible,
                IsOpen = isOpen,
                MaxPlayers = maxPlayers,
                PlayerTtl = playerTtl,
                EmptyRoomTtl = roomTtl,
                CleanupCacheOnLeave = true,
                CustomRoomProperties = customGameProperties,
                CustomRoomPropertiesForLobby = propsListedInLobby
            };
            this.CurrentRoom = this.CreateRoom(roomName, this.createRoomOptions);

            Hashtable playerPropsToSend = null;
            if (this.Server == ServerConnection.GameServer)
            {
                playerPropsToSend = this.LocalPlayer.AllProperties;
            }

            this.CurrentLobby = new TypedLobby(lobbyName, lobbyType);

            bool onGameServer = this.Server == ServerConnection.GameServer;
            return this.loadBalancingPeer.OpCreateRoom(roomName, this.createRoomOptions, new TypedLobby(lobbyName, lobbyType), playerPropsToSend, onGameServer);
        }


        /// <summary>
        /// Creates a new room on the server (or fails if the name is already taken).
        /// </summary>
        /// <remarks>
        /// If you don't want to create a unique room-name, pass null or "" as name and the server will assign a
        /// roomName (a GUID as string). Room names are unique.
        /// 
        /// A room will be attached to the specified lobby (by name and type). Use null as lobbyName to attach the 
        /// room to the lobby you are now in. If you are in no lobby, the default lobby is used.
        /// Multiple lobbies can help separate players by map or skill or game type. Each room can only be found 
        /// in one lobby (no matter if defined by name and type or as default).
        /// 
        /// This method returns if the operation can be send at the moment. You must be connected and authenticated 
        /// on the Master Server.
        /// 
        /// The operation-response depends on the server the peer is connected to:
        ///  Master will return a Game Server to connect to.
        ///  Game Server will return the Room's data.
        ///  This is an async request which triggers a OnOperationResponse() call.
        /// 
        /// This override sets the state of the client.
        /// </remarks>
        /// <param name="roomName">The name to create a room with. Must be unique and not in use or can't be created. If null, the server will assign a GUID as name.</param>
        /// <param name="roomOptions">Contains the parameters and properties of the new room. See RoomOptions class for a description of each.</param>
        /// <param name="lobby">The lobby (name and type) in which to create the room. Null uses the current lobby or the default lobby (if not in a lobby).</param>
        /// <returns>If the operation could be sent (has to be connected).</returns>
        public bool OpCreateRoom(string roomName, RoomOptions roomOptions, TypedLobby lobby)
        {
            this.State = ClientState.Joining;
            this.lastJoinType = JoinType.CreateRoom;
            this.lastJoinActorNumber = 0;
            this.createRoomOptions = roomOptions;
            this.CurrentRoom = this.CreateRoom(roomName, roomOptions);

            Hashtable playerPropsToSend = null;
            if (this.Server == ServerConnection.GameServer)
            {
                playerPropsToSend = this.LocalPlayer.AllProperties;
            }

            this.CurrentLobby = lobby;
            bool onGameServer = this.Server == ServerConnection.GameServer;
            return this.loadBalancingPeer.OpCreateRoom(roomName, roomOptions, lobby, playerPropsToSend, onGameServer);
        }

        /// <summary>
        /// Leaves the CurrentRoom and returns to the Master server (back to the lobby).
        /// OpLeaveRoom skips execution when the room is null or the server is not GameServer or the client is disconnecting from GS already.
        /// OpLeaveRoom returns false in those cases and won't change the state, so check return of this method.
        /// </summary>
        /// <remarks>
        /// This method actually is not an operation per se. It sets a state and calls Disconnect().
        /// This is is quicker than calling OpLeave and then disconnect (which also triggers a leave).
        /// </remarks>
        /// <returns>If the current room could be left (impossible while not in a room).</returns>
        public bool OpLeaveRoom()
        {
            return OpLeaveRoom(false);  //TURNBASED
        }

        /// <summary>
        /// Leaves the CurrentRoom and returns to the Master server (back to the lobby).
        /// OpLeaveRoom skips execution when the room is null or the server is not GameServer or the client is disconnecting from GS already.
        /// OpLeaveRoom returns false in those cases and won't change the state, so check return of this method.
        /// </summary>
        /// <param name="willReturnLater"></param>
        /// <remarks>
        /// This method actually is not an operation per se. It sets a state and calls Disconnect().
        /// This is is quicker than calling OpLeave and then disconnect (which also triggers a leave).
        /// </remarks>
        /// <returns>If the current room could be left (impossible while not in a room).</returns>
        public bool OpLeaveRoom(bool willReturnLater)
        {
            if (this.CurrentRoom == null || this.Server != ServerConnection.GameServer || this.State == ClientState.DisconnectingFromGameserver)
            {
                return false;
            }

            if (willReturnLater)
            {
                this.State = ClientState.DisconnectingFromGameserver;
                this.loadBalancingPeer.Disconnect();
            }
            else
            {
                this.State = ClientState.Leaving;
                this.loadBalancingPeer.OpLeaveRoom(false);    //TURNBASED users can leave a room forever or return later
            }

            return true;
        }

        /// <summary>
        /// Request the rooms and online status for a list of friends. All clients should set a unique UserId before connecting. The result is available in this.FriendList.
        /// </summary>
        /// <remarks>
        /// Used on Master Server to find the rooms played by a selected list of users.
        /// The result will be stored in LoadBalancingClient.FriendList, which is null before the first server response.
        ///
        /// Users identify themselves by setting a UserId in the LoadBalancingClient instance.
        /// This will send the ID in OpAuthenticate during connect (to master and game servers).
        /// Note: Changing a player's name doesn't make sense when using a friend list.
        ///
        /// The list of usernames must be fetched from some other source (not provided by Photon).
        ///
        ///
        /// Internal:
        /// The server response includes 2 arrays of info (each index matching a friend from the request):
        /// ParameterCode.FindFriendsResponseOnlineList = bool[] of online states
        /// ParameterCode.FindFriendsResponseRoomIdList = string[] of room names (empty string if not in a room)
        /// </remarks>
        /// <param name="friendsToFind">Array of friend's names (make sure they are unique).</param>
        /// <returns>If the operation could be sent (requires connection).</returns>
        public bool OpFindFriends(string[] friendsToFind)
        {
            if (this.loadBalancingPeer == null)
            {
                return false;
            }

            if (this.isFetchingFriendList || this.Server == ServerConnection.GameServer)
            {
                return false;   // fetching friends currently, so don't do it again (avoid changing the list while fetching friends)
            }

            this.isFetchingFriendList = true;
            this.friendListRequested = friendsToFind;
            return this.loadBalancingPeer.OpFindFriends(friendsToFind);
        }

        /// <summary>
        /// Sets custom properties of a player / actor (only passing on the string-typed custom properties).
        /// Use this only when in state Joined.
        /// </summary>
        /// <param name="actorNr">ID of player to update/set properties for.</param>
        /// <param name="actorProperties">The properties to set for target actor.</param>
        /// <returns>If sending the properties to the server worked (not if the operation was executed successfully).</returns>
        public bool OpSetCustomPropertiesOfActor(int actorNr, Hashtable actorProperties)
        {
            Hashtable customActorProperties = new Hashtable();
            customActorProperties.MergeStringKeys(actorProperties);

            return this.OpSetPropertiesOfActor(actorNr, customActorProperties);
        }

        /// <summary>
        /// This updates the local cache of a player's properties before sending them to the server.
        /// Use this only when in state Joined.
        /// </summary>
        /// <param name="actorNr">ID of player to update/set properties for.</param>
        /// <param name="actorProperties">The properties to set for target actor.</param>
        /// <returns>If sending the properties to the server worked (not if the operation was executed successfully).</returns>
        public bool OpSetPropertiesOfActor(int actorNr, Hashtable actorProperties)
        {
            Player target = this.CurrentRoom.GetPlayer(actorNr);
            if (target != null)
            {
                target.CacheProperties(actorProperties);
            }

            return this.loadBalancingPeer.OpSetPropertiesOfActor(actorNr, actorProperties);
        }


        /// <summary>
        /// Sets only custom game properties (which exclusively use strings as key-type in hash).
        /// </summary>
        /// <param name="gameProperties">The custom roomProperties to udpate or set.</param>
        /// <returns>If sending the properties to the server worked (not if the operation was executed successfully).</returns>
        public bool OpSetCustomPropertiesOfRoom(Hashtable gameProperties)
        {
            return this.OpSetCustomPropertiesOfRoom(gameProperties, false);
        }

        /// <summary>
        /// Sets only custom game properties (which exclusively use strings as key-type in hash).
        /// </summary>
        /// <param name="gameProperties">The roomProperties to udpate or set.</param>
        /// <param name="webForward">If true, this set of properties will be forwarded to your webhooks (if configured).</param>
        /// <returns>If sending the properties to the server worked (not if the operation was executed successfully).</returns>
        public bool OpSetCustomPropertiesOfRoom(Hashtable gameProperties, bool webForward)
        {
            Hashtable customGameProps = new Hashtable();
            customGameProps.MergeStringKeys(gameProperties);

            return this.OpSetPropertiesOfRoom(customGameProps, webForward);
        }

        /// <summary>
        /// This updates the current room's properties before sending them to the server.
        /// Use this only while in state Joined.
        /// </summary>
        /// <param name="gameProperties">The roomProperties to udpate or set.</param>
        /// <returns>If sending the properties to the server worked (not if the operation was executed successfully).</returns>
        protected internal bool OpSetPropertiesOfRoom(Hashtable gameProperties)
        {
            return this.OpSetPropertiesOfRoom(gameProperties, false);
        }

        /// <summary>
        /// This updates the current room's properties before sending them to the server.
        /// Use this only while in state Joined.
        /// </summary>
        /// <param name="gameProperties">The roomProperties to udpate or set.</param>
        /// <param name="webForward">If true, this set of properties will be forwarded to your webhooks (if configured).</param>
        /// <returns>If sending the properties to the server worked (not if the operation was executed successfully).</returns>
        protected internal bool OpSetPropertiesOfRoom(Hashtable gameProperties, bool webForward)
        {
            this.CurrentRoom.CacheProperties(gameProperties);
            return this.loadBalancingPeer.OpSetPropertiesOfRoom(gameProperties, webForward);
        }

        /// <summary>
        /// Send an event with custom code/type and any content to the other players in the same room.
        /// </summary>
        /// <remarks>This override explicitly uses another parameter order to not mix it up with the implementation for Hashtable only.</remarks>
        /// <param name="eventCode">Identifies this type of event (and the content). Your game's event codes can start with 0.</param>
        /// <param name="customEventContent">Any serializable datatype (including Hashtable like the other OpRaiseEvent overloads).</param>
        /// <param name="sendReliable">If this event has to arrive reliably (potentially repeated if it's lost).</param>
        /// <param name="raiseEventOptions">Contains (slightly) less often used options. If you pass null, the default options will be used.</param>
        /// <returns>If operation could be enqueued for sending. Sent when calling: Service or SendOutgoingCommands.</returns>
        public virtual bool OpRaiseEvent(byte eventCode, object customEventContent, bool sendReliable, RaiseEventOptions raiseEventOptions)
        {
            if (this.loadBalancingPeer == null)
            {
                return false;
            }

            return this.loadBalancingPeer.OpRaiseEvent(eventCode, customEventContent, sendReliable, raiseEventOptions);
        }

        /// <summary>
        /// This operation makes Photon call your custom web-service by path/name with the given parameters (converted into JSon).
        /// </summary>
        /// <remarks>
        /// This is a "Photon Turnbased" feature and you have to setup your Photon Cloud Application prior to use.
        /// 
        /// The response by Photon will call OnOperationResponse() with Code: OperationCode.WebRpc.
        /// It's important to understand that the OperationResponse tells you if the WebRPC could be called or not
        /// but the content of the response will contain the values the web-service sent (if any).
        /// If the web-service could not execute the request, it might return another error and a message. This is
        /// inside of the (wrapping) OperationResponse.
        /// The class WebRpcResponse is a helper-class that extracts the most valuable content from the WebRPC 
        /// response.
        /// 
        /// See the Turnbased Feature Overview for a short intro.
        /// http://doc.exitgames.com/en/turnbased/current/getting-started/feature-overview
        /// </remarks>
        /// <example>
        /// It's best to inherit a LoadBalancingClient into your own. Implement something like:
        /// 
        ///     public override void OnOperationResponse(OperationResponse operationResponse)
        ///     {
        ///         base.OnOperationResponse(operationResponse);  // important to call, to keep state up to date
        ///     
        ///         switch (operationResponse.OperationCode) {
        ///         case OperationCode.WebRpc:
        ///         
        ///             if (operationResponse.ReturnCode != 0)
        ///             {
        ///                 DebugReturn(DebugLevel.ERROR, "WebRpc failed. Response: " + operationResponse.ToStringFull());
        ///             }
        ///             else
        ///             {
        ///                 WebRpcResponse webResponse = new WebRpcResponse(operationResponse);
        ///                 this.OnWebRpcResponse(webResponse);
        ///             }
        ///             break;
        ///     
        ///         // more code [...]
        /// </example>
        public bool OpWebRpc(string uriPath, object parameters)
        {
            Dictionary<byte, object> opParameters = new Dictionary<byte, object>();
            opParameters.Add(ParameterCode.UriPath, uriPath);
            opParameters.Add(ParameterCode.WebRpcParameters, parameters);

            return this.loadBalancingPeer.OpCustom(OperationCode.WebRpc, opParameters, true); 
        }

        #endregion

        #region Helpers

        /// <summary>
        /// Privately used to read-out properties coming from the server in events and operation responses (which might be a bit tricky).
        /// </summary>
        private void ReadoutProperties(Hashtable gameProperties, Hashtable actorProperties, int targetActorNr)
        {
            // Debug.LogWarning("ReadoutProperties game=" + gameProperties + " actors(" + actorProperties + ")=" + actorProperties + " " + targetActorNr);
            // read game properties and cache them locally
            if (this.CurrentRoom != null && gameProperties != null)
            {
                this.CurrentRoom.CacheProperties(gameProperties);
            }

            if (actorProperties != null && actorProperties.Count > 0)
            {
                if (targetActorNr > 0)
                {
                    // we have a single entry in the actorProperties with one user's name
                    // targets MUST exist before you set properties
                    Player target = this.CurrentRoom.GetPlayer(targetActorNr);
                    if (target != null)
                    {
                        target.CacheProperties(this.ReadoutPropertiesForActorNr(actorProperties, targetActorNr));
                    }
                }
                else
                {
                    // in this case, we've got a key-value pair per actor (each
                    // value is a hashtable with the actor's properties then)
                    int actorNr;
                    Hashtable props;
                    string newName;
                    Player target;

                    foreach (object key in actorProperties.Keys)
                    {
                        actorNr = (int)key;
                        props = (Hashtable)actorProperties[key];
                        newName = (string)props[ActorProperties.PlayerName];

                        target = this.CurrentRoom.GetPlayer(actorNr);
                        if (target == null)
                        {
                            target = this.CreatePlayer(newName, actorNr, false, props);
                            this.CurrentRoom.StorePlayer(target);
                        }
                        else
                        {
                            target.CacheProperties(props);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Privately used only to read properties for a distinct actor (which might be the hashtable OR a key-pair value IN the actorProperties).
        /// </summary>
        private Hashtable ReadoutPropertiesForActorNr(Hashtable actorProperties, int actorNr)
        {
            if (actorProperties.ContainsKey(actorNr))
            {
                return (Hashtable)actorProperties[actorNr];
            }

            return actorProperties;
        }

        /// <summary>
        /// Internally used to set the LocalPlayer's ID (from -1 to the actual in-room ID).
        /// </summary>
        /// <param name="newID">New actor ID (a.k.a actorNr) assigned when joining a room.</param>
        protected internal void ChangeLocalID(int newID)
        {
            if (this.LocalPlayer == null)
            {
                this.DebugReturn(DebugLevel.WARNING, string.Format("Local actor is null or not in mActors! mLocalActor: {0} mActors==null: {1} newID: {2}", this.LocalPlayer, this.CurrentRoom.Players == null, newID));
            }

            if (this.CurrentRoom == null)
            {
                // change to new actor/player ID and make sure the player does not have a room reference left
                this.LocalPlayer.ChangeLocalID(newID);
                this.LocalPlayer.RoomReference = null;
            }
            else
            {
                // remove old actorId from actor list
                this.CurrentRoom.RemovePlayer(this.LocalPlayer);

                // change to new actor/player ID
                this.LocalPlayer.ChangeLocalID(newID);

                // update the room's list with the new reference
                this.CurrentRoom.StorePlayer(this.LocalPlayer);

                // make this client known to the local player (used to get state and to sync values from within Player)
                this.LocalPlayer.LoadBalancingClient = this;
            }
        }

        /// <summary>
        /// Internally used to clean up local instances of players and room.
        /// </summary>
        private void CleanCachedValues()
        {
            this.ChangeLocalID(-1);
            this.isFetchingFriendList = false;

            // if this is called on the gameserver, we clean the room we were in. on the master, we keep the room to get into it
            if (this.Server == ServerConnection.GameServer || this.State == ClientState.Disconnecting || this.State == ClientState.Uninitialized)
            {
                this.CurrentRoom = null;    // players get cleaned up inside this, too, except LocalPlayer (which we keep)
            }

            // when we leave the master, we clean up the rooms list (which might be updated by the lobby when we join again)
            if (this.Server == ServerConnection.MasterServer || this.State == ClientState.Disconnecting || this.State == ClientState.Uninitialized)
            {
                this.RoomInfoList.Clear();
            }
        }

        /// <summary>
        /// Called internally, when a game was joined or created on the game server.
        /// This reads the response, finds out the local player's actorNumber (a.k.a. Player.ID) and applies properties of the room and players.
        /// </summary>
        /// <param name="operationResponse">Contains the server's response for an operation called by this peer.</param>
        private void GameEnteredOnGameServer(OperationResponse operationResponse)
        {
            if (operationResponse.ReturnCode != 0)
            {
                switch (operationResponse.OperationCode)
                {
                    case OperationCode.CreateGame:
                        this.DebugReturn(DebugLevel.ERROR, "Create failed on GameServer. Changing back to MasterServer. ReturnCode: " + operationResponse.ReturnCode);
                        break;
                    case OperationCode.JoinGame:
                    case OperationCode.JoinRandomGame:
                        this.DebugReturn(DebugLevel.ERROR, "Join failed on GameServer. Changing back to MasterServer.");

                        if (operationResponse.ReturnCode == ErrorCode.GameDoesNotExist)
                        {
                            this.DebugReturn(DebugLevel.INFO, "Most likely the game became empty during the switch to GameServer.");
                        }

                        // TODO: add callback to join failed
                        break;
                }

                this.DisconnectToReconnect();
                return;
            }

            this.State = ClientState.Joined;
            this.CurrentRoom.LoadBalancingClient = this;
            this.CurrentRoom.IsLocalClientInside = true;

            // the local player's actor-properties are not returned in join-result. add this player to the list
            int localActorNr = (int)operationResponse[ParameterCode.ActorNr];
            this.ChangeLocalID(localActorNr);

            Hashtable actorProperties = (Hashtable)operationResponse[ParameterCode.PlayerProperties];
            Hashtable gameProperties = (Hashtable)operationResponse[ParameterCode.GameProperties];
            this.ReadoutProperties(gameProperties, actorProperties, 0);

            //TURNBASED
            int[] actorsInGame = (int[])operationResponse[ParameterCode.ActorList];
            if (actorsInGame != null)
            {
                foreach (int userId in actorsInGame)
                {
                    Player target = this.CurrentRoom.GetPlayer(userId);
                    if (target == null)
                    {
                        Debug.WriteLine("Created player that was missing so far (no property set).");//TODO: decide if this could ever happen. it means the user had no props at all.
                        this.CurrentRoom.StorePlayer(this.CreatePlayer(string.Empty, userId, false, null));
                    }
                }
            }
            switch (operationResponse.OperationCode)
            {
                case OperationCode.CreateGame:
                    // TODO: add callback "game created"
                    break;
                case OperationCode.JoinGame:
                case OperationCode.JoinRandomGame:
                    // TODO: add callback "game joined"
                    break;
            }
        }

        /// <summary>
        /// Factory method to create a player instance - override to get your own player-type with custom features.
        /// </summary>
        /// <param name="actorName">The name of the player to be created. </param>
        /// <param name="actorNumber">The player ID (a.k.a. actorNumber) of the player to be created.</param>
        /// <param name="isLocal">Sets the distinction if the player to be created is your player or if its assigned to someone else.</param>
        /// <param name="actorProperties">The custom properties for this new player</param>
        /// <returns>The newly created player</returns>
        protected internal virtual Player CreatePlayer(string actorName, int actorNumber, bool isLocal, Hashtable actorProperties)
        {
            Player newPlayer = new Player(actorName, actorNumber, isLocal, actorProperties);
            return newPlayer;
        }
        
        /// <summary>Internal "factory" method to create a room-instance.</summary>
        protected internal virtual Room CreateRoom(string roomName, RoomOptions opt)
        {
            if (opt == null)
            {
                opt = new RoomOptions();
            }
            Room r = new Room(roomName, opt);
            return r;
        }

        #endregion

        #region IPhotonPeerListener

        /// <summary>Debug output of low level api (and this client).</summary>
        /// <remarks>This method is not responsible to keep up the state of a LoadBalancingClient. Calling base.DebugReturn on overrides is optional.</remarks>
        public virtual void DebugReturn(DebugLevel level, string message)
        {
            Debug.WriteLine(message);
        }

        /// <summary>
        /// Uses the operationResponse's provided by the server to advance the internal state and call ops as needed.
        /// </summary>
        /// <remarks>This method is essential to update the internal state of a LoadBalancingClient. Overriding methods must call base.OnOperationResponse.</remarks>
        /// <param name="operationResponse">Contains the server's response for an operation called by this peer.</param>
        public virtual void OnOperationResponse(OperationResponse operationResponse)
        {
            // if (operationResponse.ReturnCode != 0) this.DebugReturn(DebugLevel.ERROR, operationResponse.ToStringFull());

            // use the "secret" or "token" whenever we get it. doesn't really matter if it's in AuthResponse.
            if (operationResponse.Parameters.ContainsKey(ParameterCode.Secret))
            {
                if (this.CustomAuthenticationValues == null)
                {
                    this.CustomAuthenticationValues = new AuthenticationValues();
                    //this.DebugReturn(DebugLevel.ERROR, "Server returned secret. Created CustomAuthenticationValues.");
                }

                this.CustomAuthenticationValues.Secret = operationResponse[ParameterCode.Secret] as string;
            }

            switch (operationResponse.OperationCode)
            {
                case OperationCode.FindFriends:
                    if (operationResponse.ReturnCode != 0)
                    {
                        this.DebugReturn(DebugLevel.ERROR, "OpFindFriends failed: " + operationResponse.ToStringFull());
                        this.isFetchingFriendList = false;
                        break;
                    }

                    bool[] onlineList = operationResponse[ParameterCode.FindFriendsResponseOnlineList] as bool[];
                    string[] roomList = operationResponse[ParameterCode.FindFriendsResponseRoomIdList] as string[];

                    this.FriendList = new List<FriendInfo>(this.friendListRequested.Length);
                    for (int index = 0; index < this.friendListRequested.Length; index++)
                    {
                        FriendInfo friend = new FriendInfo();
                        friend.Name = this.friendListRequested[index];
                        friend.Room = roomList[index];
                        friend.IsOnline = onlineList[index];
                        this.FriendList.Insert(index, friend);
                    }

                    this.friendListRequested = null;
                    this.isFetchingFriendList = false;
                    this.friendListTimestamp = Environment.TickCount;
                    if (this.friendListTimestamp == 0)
                    {
                        this.friendListTimestamp = 1;   // makes sure the timestamp is not accidentally 0
                    }
                    break;
                case OperationCode.Authenticate:
                    {
                        if (operationResponse.ReturnCode != 0)
                        {
                            this.DebugReturn(DebugLevel.ERROR, operationResponse.ToStringFull() + " Server: " + this.Server + " Address: " + this.loadBalancingPeer.ServerAddress);

                            switch (operationResponse.ReturnCode)
                            {
                                case ErrorCode.InvalidAuthentication:
                                    this.DisconnectedCause = DisconnectCause.InvalidAuthentication;
                                    break;
                                case ErrorCode.CustomAuthenticationFailed:
                                    this.DisconnectedCause = DisconnectCause.CustomAuthenticationFailed;
                                    break;
                                case ErrorCode.InvalidRegion:
                                    this.DisconnectedCause = DisconnectCause.InvalidRegion;
                                    break;
                                case ErrorCode.MaxCcuReached:
                                    this.DisconnectedCause = DisconnectCause.MaxCcuReached;
                                    break;
                                case ErrorCode.OperationNotAllowedInCurrentState:
                                    this.DisconnectedCause = DisconnectCause.OperationNotAllowedInCurrentState;
                                    break;
                            }
                            this.State = ClientState.Disconnecting;
                            this.Disconnect();
                            break;  // if auth didn't succeed, we disconnect (above) and exit this operation's handling
                        }

                        if (this.Server == ServerConnection.NameServer)
                        {
                            // on the NameServer, authenticate returns the MasterServer address for a region and we hop off to there
                            this.MasterServerAddress = operationResponse[ParameterCode.Address] as string;
                            this.DisconnectToReconnect();
                        }
                        else if (this.Server == ServerConnection.MasterServer)
                        {
                            this.State = ClientState.ConnectedToMaster;

                            if (this.AutoJoinLobby)
                            {
                                this.loadBalancingPeer.OpJoinLobby(this.CurrentLobby);
                            }
                        }
                        else if (this.Server == ServerConnection.GameServer)
                        {
                            this.State = ClientState.ConnectingToGameserver;

                            if (this.lastJoinType == JoinType.JoinRoom || this.lastJoinType == JoinType.JoinRandomRoom) 
                            {
                                // if we just "join" the game, do so. if we "join-or-create", we have to set the createIfNotExists parameter to true
                                this.State = ClientState.Joining;
                                this.OpJoinRoom(this.CurrentRoom.Name, this.lastJoinActorNumber);
                            }
                            else if(this.lastJoinType == JoinType.JoinOrCreateRoom)
                            {
                                this.State = ClientState.Joining;
                                this.OpJoinOrCreateRoom(this.CurrentRoom.Name, this.lastJoinActorNumber, this.createRoomOptions);
                            }
                            else if (this.lastJoinType == JoinType.CreateRoom)
                            {
                                this.State = ClientState.Joining;   // yes, "joining" even though we technically create the game now on the game server
                                this.OpCreateRoom(this.CurrentRoom.Name, this.createRoomOptions, this.CurrentLobby);
                            }
                            break;
                        }
                        break;
                    }

                case OperationCode.GetRegions:
                    this.AvailableRegions = operationResponse[ParameterCode.Region] as string[];
                    this.AvailableRegionsServers = operationResponse[ParameterCode.Address] as string[];
                    break;

                case OperationCode.Leave:
                    //this.CleanCachedValues(); // this is done in status change on "disconnect"
                    this.State = ClientState.DisconnectingFromGameserver;
                    this.loadBalancingPeer.Disconnect();
                    break;
    
                case OperationCode.JoinLobby:
                    this.State = ClientState.JoinedLobby;
                    break;

                case OperationCode.JoinRandomGame:  // this happens only on the master server. on gameserver this is a "regular" join
                case OperationCode.CreateGame:
                case OperationCode.JoinGame:
                    {
                        if (this.Server == ServerConnection.GameServer)
                        {
                            this.GameEnteredOnGameServer(operationResponse);
                        }
                        else
                        {
                            if (operationResponse.ReturnCode == ErrorCode.NoRandomMatchFound)
                            {
                                // this happens only for JoinRandomRoom
                                // TODO: implement callback/reaction when no random game could be found (this is no bug and can simply happen if no games are open)
                                this.state = ClientState.JoinedLobby; // TODO: maybe we have to return to another state here (if we didn't join a lobby)
                                break;
                            }

                            // TODO: handle more error cases

                            if (operationResponse.ReturnCode != 0)
                            {
                                if (this.loadBalancingPeer.DebugOut >= DebugLevel.ERROR)
                                {
                                    this.DebugReturn(DebugLevel.ERROR, string.Format("Getting into game failed, client stays on masterserver: {0}.", operationResponse.ToStringFull()));
                                }

                                this.state = ClientState.JoinedLobby; // TODO: maybe we have to return to another state here (if we didn't join a lobby)
                                break;
                            }

                            this.GameServerAddress = (string)operationResponse[ParameterCode.Address];
                            string gameId = operationResponse[ParameterCode.RoomName] as string;
                            if (!string.IsNullOrEmpty(gameId))
                            {
                                // is only sent by the server's response, if it has not been sent with the client's request before!
                                this.CurrentRoom.Name = gameId;
                            }

                            this.DisconnectToReconnect();
                        }

                        break;
                    }
            }
        }

        /// <summary>
        /// Uses the connection's statusCodes to advance the internal state and call operations as needed.
        /// </summary>
        /// <remarks>This method is essential to update the internal state of a LoadBalancingClient. Overriding methods must call base.OnStatusChanged.</remarks>
        public virtual void OnStatusChanged(StatusCode statusCode)
        {
            switch (statusCode)
            {
                case StatusCode.Connect:
                    if (this.State == ClientState.ConnectingToNameServer)
                    {
                        if (this.loadBalancingPeer.DebugOut >= DebugLevel.ALL)
                        {
                            this.DebugReturn(DebugLevel.ALL, "Connected to nameserver.");
                        }

                        this.Server = ServerConnection.NameServer;
                        if (this.CustomAuthenticationValues != null)
                        {
                            this.CustomAuthenticationValues.Secret = null; // when connecting to NameServer, invalidate the secret (only)
                        }
                    }

                    if (this.State == ClientState.ConnectingToGameserver)
                    {
                        if (this.loadBalancingPeer.DebugOut >= DebugLevel.ALL)
                        {
                            this.DebugReturn(DebugLevel.ALL, "Connected to gameserver.");
                        }

                        this.Server = ServerConnection.GameServer;
                    }

                    if (this.State == ClientState.ConnectingToMasterserver)
                    {
                        if (this.loadBalancingPeer.DebugOut >= DebugLevel.ALL)
                        {
                            this.DebugReturn(DebugLevel.ALL, "Connected to masterserver.");
                        }

                        this.Server = ServerConnection.MasterServer;
                    }

                    this.loadBalancingPeer.EstablishEncryption();   // always enable encryption

                    if (this.IsAuthorizeSecretAvailable)
                    {
                        // if we have a token we don't have to wait for encryption (it is encrypted anyways, so encryption is just optional later on)
                        this.didAuthenticate = this.loadBalancingPeer.OpAuthenticate(this.AppId, this.AppVersion, (this.UserId ?? this.PlayerName), this.CustomAuthenticationValues, this.CloudRegion);
                        if (this.didAuthenticate)
                        {
                            this.State = ClientState.Authenticating;
                        }
                        else
                        {
                            this.DebugReturn(DebugLevel.ERROR, "Error calling OpAuthenticateWithToken! Check log output, CustomAuthenticationValues and if you're connected. State: " + this.State);
                        }
                    }
                    break;

                case StatusCode.EncryptionEstablished:
                    // on nameserver, the "process" is stopped here, so the developer/game can either get regions or authenticate with a specific region
                    if (this.Server == ServerConnection.NameServer)
                    {
                        this.State = ClientState.ConnectedToNameServer;
                    }

                    // on any other server we might now have to authenticate still, so the client can do anything at all
                    if (!this.didAuthenticate && (!this.IsUsingNameServer || this.CloudRegion != null))
                    {
                        // once encryption is availble, the client should send one (secure) authenticate. it includes the AppId (which identifies your app on the Photon Cloud)
                        this.didAuthenticate = this.loadBalancingPeer.OpAuthenticate(this.AppId, this.AppVersion, (this.UserId ?? this.PlayerName), this.CustomAuthenticationValues, this.CloudRegion);
                        if (this.didAuthenticate)
                        {
                            this.State = ClientState.Authenticating;
                        }
                        else
                        {
                            this.DebugReturn(DebugLevel.ERROR, "Error calling OpAuthenticate! Did not work. Check log output, CustomAuthenticationValues and if you're connected. State: " + this.State);
                        }
                    }

                    break;

                case StatusCode.Disconnect:
                    // disconnect due to connection exception is handled below (don't connect to GS or master in that case)

                    this.CleanCachedValues();
                    this.didAuthenticate = false;   // on connect, we know that we didn't

                    if (this.State == ClientState.Disconnecting)
                    {
                        if (this.CustomAuthenticationValues != null)
                        {
                            this.CustomAuthenticationValues.Secret = null; // when leaving the server, invalidate the secret (but not the auth values)
                        }
                        this.State = ClientState.Disconnected;
                    }
                    else if (this.State == ClientState.Uninitialized)
                    {
                        if (this.CustomAuthenticationValues != null)
                        {
                            this.CustomAuthenticationValues.Secret = null; // when leaving the server, invalidate the secret (but not the auth values)
                        }
                        this.State = ClientState.Disconnected;
                    }
                    else if (this.State != ClientState.Disconnected)
                    {
                        if (this.Server == ServerConnection.GameServer || this.Server == ServerConnection.NameServer)
                        {
                            this.Connect();
                        }
                        else if (this.Server == ServerConnection.MasterServer)
                        {
                            this.ConnectToGameServer();
                        }
                    }
                    break;

                case StatusCode.DisconnectByServerUserLimit:
                    this.DebugReturn(DebugLevel.ERROR, "The Photon license's CCU Limit was reached. Server rejected this connection. Wait and re-try.");
                    if (this.CustomAuthenticationValues != null)
                    {
                        this.CustomAuthenticationValues.Secret = null; // when leaving the server, invalidate the secret (but not the auth values)
                    }
                    this.DisconnectedCause = DisconnectCause.DisconnectByServerUserLimit;
                    this.State = ClientState.Disconnected;
                    break;
                case StatusCode.ExceptionOnConnect:
                case StatusCode.SecurityExceptionOnConnect:
                    if (this.CustomAuthenticationValues != null)
                    {
                        this.CustomAuthenticationValues.Secret = null; // when leaving the server, invalidate the secret (but not the auth values)
                    }
                    this.DisconnectedCause = DisconnectCause.ExceptionOnConnect;
                    this.State = ClientState.Disconnected;
                    break;
                case StatusCode.DisconnectByServer:
                    if (this.CustomAuthenticationValues != null)
                    {
                        this.CustomAuthenticationValues.Secret = null; // when leaving the server, invalidate the secret (but not the auth values)
                    }
                    this.DisconnectedCause = DisconnectCause.DisconnectByServer;
                    this.State = ClientState.Disconnected;
                    break;
                case StatusCode.TimeoutDisconnect:
                    if (this.CustomAuthenticationValues != null)
                    {
                        this.CustomAuthenticationValues.Secret = null; // when leaving the server, invalidate the secret (but not the auth values)
                    }
                    this.DisconnectedCause = DisconnectCause.TimeoutDisconnect;
                    this.State = ClientState.Disconnected;
                    break;
                case StatusCode.Exception:
                case StatusCode.ExceptionOnReceive:
                    if (this.CustomAuthenticationValues != null)
                    {
                        this.CustomAuthenticationValues.Secret = null; // when leaving the server, invalidate the secret (but not the auth values)
                    }
                    this.DisconnectedCause = DisconnectCause.Exception;
                    this.State = ClientState.Disconnected;
                    break;
            }
        }

        /// <summary>
        /// Uses the photonEvent's provided by the server to advance the internal state and call ops as needed.
        /// </summary>
        /// <remarks>This method is essential to update the internal state of a LoadBalancingClient. Overriding methods must call base.OnEvent.</remarks>
        public virtual void OnEvent(EventData photonEvent)
        {
            switch (photonEvent.Code)
            {
                case EventCode.GameList:
                case EventCode.GameListUpdate:
                    if (photonEvent.Code == EventCode.GameList)
                    {
                        this.RoomInfoList = new Dictionary<string, RoomInfo>();
                    }

                    Hashtable games = (Hashtable)photonEvent[ParameterCode.GameList];
                    foreach (string gameName in games.Keys)
                    {
                        RoomInfo game = new RoomInfo(gameName, (Hashtable)games[gameName]);
                        if (game.removedFromList)
                        {
                            this.RoomInfoList.Remove(gameName);
                        }
                        else
                        {
                            this.RoomInfoList[gameName] = game;
                        }
                    }
                    break;

                case EventCode.Join:
                    int actorNr = (int)photonEvent[ParameterCode.ActorNr];  // actorNr (a.k.a. playerNumber / ID) of sending player
                    bool isLocal = this.LocalPlayer.ID == actorNr;

                    Hashtable actorProperties = (Hashtable)photonEvent[ParameterCode.PlayerProperties];

                    if (!isLocal)
                    {
                        Player newPlayer = this.CreatePlayer(string.Empty, actorNr, false, actorProperties);
                        this.CurrentRoom.StorePlayer(newPlayer);
                    }
                    else
                    {
                        // in this player's own join event, we get a complete list of players in the room, so check if we know each of the
                        int[] actorsInRoom = (int[])photonEvent[ParameterCode.ActorList];
                        foreach (int actorNrToCheck in actorsInRoom)
                        {
                            if (this.LocalPlayer.ID != actorNrToCheck && !this.CurrentRoom.Players.ContainsKey(actorNrToCheck))
                            {
                                this.CurrentRoom.StorePlayer(this.CreatePlayer(string.Empty, actorNrToCheck, false, null));
                            }
                            else if (this.CurrentRoom.Players.ContainsKey(actorNrToCheck))
                            {
                                Player p = null;
                                if (this.CurrentRoom.Players.TryGetValue(actorNrToCheck, out p))
                                {
                                    p.IsInactive = false;
                                }
                            }
                        }
                    }
                    break;

                case EventCode.Leave:
                    {
                        int actorID = (int) photonEvent[ParameterCode.ActorNr];

                        //TURNBASED
                        bool isInactive = false;
                        if (photonEvent.Parameters.ContainsKey(ParameterCode.IsInactive))
                        {
                            isInactive = (bool)photonEvent.Parameters[ParameterCode.IsInactive];
                        }

                        if (isInactive)
                        {
                            this.CurrentRoom.MarkAsInactive(actorID);
                            //UnityEngine.Debug.Log("leave marked player as inactive "+ actorID);
                        }
                        else
                        {
                            this.CurrentRoom.RemovePlayer(actorID);
                            //UnityEngine.Debug.Log("leave removed player " + actorID);
                        }
                    }
                    break;

                // EventCode.Disconnect was "replaced" by Leave which now has a "inactive" flag.
                //case EventCode.Disconnect:  //TURNBASED
                //    {
                //        int actorID = (int) photonEvent[ParameterCode.ActorNr];
                //        this.CurrentRoom.MarkAsInactive(actorID);
                //    }
                //    break;

                case EventCode.PropertiesChanged:
                    // whenever properties are sent in-room, they can be broadcasted as event (which we handle here)
                    // we get PLAYERproperties if actorNr > 0 or ROOMproperties if actorNumber is not set or 0
                    int targetActorNr = 0;
                    if (photonEvent.Parameters.ContainsKey(ParameterCode.TargetActorNr))
                    {
                        targetActorNr = (int)photonEvent[ParameterCode.TargetActorNr];
                    }
                    Hashtable props = (Hashtable)photonEvent[ParameterCode.Properties];

                    if (targetActorNr > 0)
                    {
                        this.ReadoutProperties(null, props, targetActorNr);
                    }
                    else
                    {
                        this.ReadoutProperties(props, null, 0);
                    }

                    break;

                case EventCode.AppStats:
                    // only the master server sends these in (1 minute) intervals
                    this.PlayersInRoomsCount = (int)photonEvent[ParameterCode.PeerCount];
                    this.RoomsCount = (int)photonEvent[ParameterCode.GameCount];
                    this.PlayersOnMasterCount = (int)photonEvent[ParameterCode.MasterPeerCount];
                    break;
            }
        }

        /// <summary>In Photon 4, "raw messages" will get their own callback method in the interface. Not used yet.</summary>
        public virtual void OnMessage(object message)
        {
            this.DebugReturn(DebugLevel.ALL, string.Format("got OnMessage {0}", message));
        }

        #endregion
    }
}

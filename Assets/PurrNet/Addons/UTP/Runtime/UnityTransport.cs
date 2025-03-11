using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using PurrNet.Logging;
using PurrNet.Transports;
using UnityEngine;

#if UTP_AUTH
using Unity.Services.Authentication;
using Unity.Services.Core;
using PurrNet.Utils;
#endif

#if UTP_RELAY
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
#endif

namespace PurrNet.UTP
{
    [DisallowMultipleComponent]
    public class UnityTransport : GenericTransport, ITransport
    {
        public event OnConnected onConnected;
        public event OnDisconnected onDisconnected;
        public event OnDataReceived onDataReceived;
        public event OnDataSent onDataSent;
        public event OnConnectionState onConnectionState;

        private readonly List<Connection> _connections = new();
        public IReadOnlyList<Connection> connections => _connections;

#if !UTP_TRANSPORT
        public override bool isSupported => false;
#elif !UTP_RELAY
        public override bool isSupported => !_peerToPeer && Application.platform != RuntimePlatform.WebGLPlayer;
#else
        public override bool isSupported => Application.platform != RuntimePlatform.WebGLPlayer;
#endif
        public override ITransport transport => this;

        private ConnectionState _listenerState = ConnectionState.Disconnected;
        private ConnectionState _clientState = ConnectionState.Disconnected;

        public ConnectionState listenerState
        {
            get => _listenerState;
            private set
            {
                if (_listenerState == value) return;

                _listenerState = value;
                onConnectionState?.Invoke(value, true);
            }
        }

        public ConnectionState clientState
        {
            get => _clientState;
            private set
            {
                if (_clientState == value) return;

                _clientState = value;
                onConnectionState?.Invoke(value, false);
            }
        }

        [Header("Relay Settings")]
        [Tooltip("Use a direct P2P connection instead of the Unity Relay Service.")]
        [SerializeField]
        private bool _peerToPeer;

        [Tooltip("Join Code for connecting to a Relay server, must be assigned prior to starting client " +
                 "(obtained via UnityLobbyProvider or UTPTransport.AllocateRelayClientAsync).")]
        [SerializeField]
        private string _relayJoinCode;

        /// <summary>
        ///     The IP Address to connect to.
        /// </summary>
        [Header("Connection Settings")] [SerializeField]
        private string _address = "127.0.0.1";

        /// <summary>
        ///     The port at which to connect.
        /// </summary>
        [SerializeField] private ushort _port = 7777;

        /// <summary>
        ///     Timeout in milliseconds for P2P connection.
        /// </summary>
        [SerializeField] [Tooltip("Timeout in milliseconds for P2P connection.")]
        private int _timeout = 1000;

        [SerializeField] [Header("Misc Settings")]
        private LogLevel _loggerLevel = LogLevel.Error;

        public bool peerToPeer
        {
            get => _peerToPeer;
            set => _peerToPeer = value;
        }

        public string address
        {
            get => _address;
            set => _address = value;
        }

        public ushort port
        {
            get => _port;
            set => _port = value;
        }

        public int timeout
        {
            get => _timeout;
            set => _timeout = value;
        }

        public LogLevel loggerLevel
        {
            get => _loggerLevel;
            set => _loggerLevel = value;
        }

        private UTPServer _server;
        private UTPClient _client;

        /// <summary>
        ///     The Relay Allocation for a server/host which initiates connection to the relay.
        /// </summary>
#if UTP_RELAY
        private Allocation _relayServerAllocation { get; set; }
#else
        private object _relayServerAllocation { get; set; }
#endif

        /// <summary>
        ///     The Relay JoinAllocation for a client who is connecting to a server.
        /// </summary>
#if UTP_RELAY
        private JoinAllocation RelayClientAllocation { get; set; }
#else
        private object RelayClientAllocation { get; set; }
#endif

        private void Awake()
        {
            UTPLog.LoggerLevel = _loggerLevel;
        }

        #region Server

        private void OnRemoteConnected(int connectionId)
        {
            _connections.Add(new Connection(connectionId));
            onConnected?.Invoke(new Connection(connectionId), true);
        }

        private void OnRemoteReceivedData(int connectionId, ArraySegment<byte> message)
        {
            onDataReceived?.Invoke(new Connection(connectionId), new ByteData(message), true);
        }

        private void OnRemoteDisconnected(int connectionId)
        {
            _connections.Remove(new Connection(connectionId));
            onDisconnected?.Invoke(new Connection(connectionId), DisconnectReason.ServerRequest, true);
        }

        protected override void StartServerInternal()
        {
            Listen(_port);
        }

        public void Listen(ushort port)
        {
            if (listenerState is ConnectionState.Connecting or ConnectionState.Connected) return;

            _server = new UTPServer(
                OnRemoteConnected,
                OnRemoteReceivedData,
                OnRemoteDisconnected
            );

            listenerState = ConnectionState.Connecting;

            if (_server.Start(port, _peerToPeer, _relayServerAllocation, _timeout))
            {
                listenerState = ConnectionState.Connected;
            }
            else
            {
                listenerState = ConnectionState.Disconnecting;
                listenerState = ConnectionState.Disconnected;
                _server = null;
            }
        }

        public void StopListening()
        {
            if (_server != null && _server.IsNetworkDriverInitialized())
            {
                listenerState = ConnectionState.Disconnecting;

                Disconnect();

                for (var i = 0; i < _connections.Count; i++)
                {
                    var conn = _connections[i];
                    CloseConnection(conn);
                }

                _connections.Clear();

                _server.Stop();
                listenerState = ConnectionState.Disconnected;
                _server = null;
                _relayServerAllocation = null;
            }
        }

        public void CloseConnection(Connection conn)
        {
            _server?.Disconnect(conn.connectionId);
        }

        public void ReceiveMessages(float delta)
        {
            _server?.Tick();
            _client?.Tick();
        }

        public void SendMessages(float delta) { }

        public void SendToClient(Connection target, ByteData data, Channel method = Channel.ReliableOrdered)
        {
            if (listenerState != ConnectionState.Connected) return;
            if (!target.isValid) return;

            _server.Send(target.connectionId, data.segment, method);

            RaiseDataSent(target, data, true);
        }

        #endregion

        #region Client

        public bool isClientConnected => _client is { isConnected: true };

        private void OnClientConnected(int connId)
        {
            clientState = ConnectionState.Connected;
            onConnected?.Invoke(new Connection(0), false);
        }

        private void OnClientDisconnected(int connId)
        {
            clientState = ConnectionState.Disconnecting;
            clientState = ConnectionState.Disconnected;
            onDisconnected?.Invoke(new Connection(0), DisconnectReason.ClientRequest, false);
        }

        private void OnClientDataReceived(int connId, ArraySegment<byte> data)
        {
            onDataReceived?.Invoke(new Connection(0), new ByteData(data), false);
        }

        protected override void StartClientInternal()
        {
            Connect(_address, _port);
        }

        public async void Connect(string ip, ushort port)
        {
            try
            {
                if (clientState is ConnectionState.Connecting or ConnectionState.Connected) return;

                clientState = ConnectionState.Connecting;

                _client = new UTPClient();
                _client.onConnected += OnClientConnected;
                _client.onDisconnected += OnClientDisconnected;
                _client.onReceivedData += OnClientDataReceived;

                if (!_peerToPeer)
                {
                    if (!_client.Connect(ip, port, _timeout))
                    {
                        clientState = ConnectionState.Disconnecting;
                        clientState = ConnectionState.Disconnected;
                        _client = null;
                    }
                    return;
                }

                if (string.IsNullOrWhiteSpace(_relayJoinCode))
                {
                    PurrLogger.LogError($"Failed to allocate Relay Client, RelayJoinCode was not set.");
                    clientState = ConnectionState.Disconnecting;
                    clientState = ConnectionState.Disconnected;
                    _client = null;
                    return;
                }

                if (await AllocateRelayClientAsync(_relayJoinCode))
                {
                    UTPLog.Info($"Connecting to Relay Server with Join Code: {_relayJoinCode}");

                    if (_client.RelayConnect(RelayClientAllocation))
                    {
                        clientState = ConnectionState.Connected;
                    }
                    else
                    {
                        clientState = ConnectionState.Disconnecting;
                        clientState = ConnectionState.Disconnected;
                        RelayClientAllocation = null;
                        _client = null;
                    }
                }
                else
                {
                    clientState = ConnectionState.Disconnecting;
                    clientState = ConnectionState.Disconnected;
                    _client = null;
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        public void Disconnect()
        {
            if (isClientConnected)
            {
                clientState = ConnectionState.Disconnecting;
                _client.Disconnect();
                clientState = ConnectionState.Disconnected;
                _client = null;

                RelayClientAllocation = null;
            }
        }

        public void SendToServer(ByteData data, Channel method = Channel.ReliableOrdered)
        {
            if (clientState != ConnectionState.Connected)
                return;

            _client.Send(data.segment, method);

            RaiseDataSent(new Connection(0), data, false);
        }

        #endregion

        public void RaiseDataReceived(Connection conn, ByteData data, bool asServer)
        {
            onDataReceived?.Invoke(conn, data, asServer);
        }

        public void RaiseDataSent(Connection conn, ByteData data, bool asServer)
        {
            onDataSent?.Invoke(conn, data, asServer);
        }

        public void Shutdown()
        {
            Disconnect();
            StopListening();

            _connections.Clear();
        }

        private void OnDisable()
        {
            Shutdown();
        }

#pragma warning disable CS1998
        public async Task<bool> InitializeUnityServicesAsync()
        {
#if UTP_AUTH
            try {
                if(UnityServices.State == ServicesInitializationState.Uninitialized) {
                    //Must initialize with different profiles when connecting multiple clients
                    //Same as AuthenticationService.Instance.SwitchProfile
                    var options = new InitializationOptions();
                    if(ApplicationContext.isClone) {
                        options.SetProfile($"{Random.Range(1, 10000)}");
                    }
                    await UnityServices.InitializeAsync(options);
                    UTPLog.Info($"UnityServices Initialized with Profile: {AuthenticationService.Instance.Profile}");
                }

                if(!AuthenticationService.Instance.IsSignedIn) {
                    await AuthenticationService.Instance.SignInAnonymouslyAsync();
                    UTPLog.Info($"UnityServices SignIn: {AuthenticationService.Instance.PlayerId}");
                }
            } catch {
                UTPLog.Error("UnityServices Initialization failed.");
            }

            return UnityServices.State != ServicesInitializationState.Uninitialized && AuthenticationService.Instance.IsSignedIn;
#else
            UTPLog.Error($"Failed to initialize Unity Services: UTP assembly references are missing.");
            return false;
#endif
        }

#if UTP_RELAY
        public void InitializeRelayServer(Allocation serverAllocation) {
#else
        public void InitializeRelayServer(object serverAllocation)
        {
#endif
            _relayServerAllocation = serverAllocation;
        }

        public void InitializeRelayClient(string joinCode)
        {
            _relayJoinCode = joinCode;
        }

        /// <summary>
        ///     Allocates a Relay Server in a given Region. If no valid RegionId is provided, the most optimal Region will be
        ///     automatically used instead.
        /// </summary>
        /// <param name="maxPlayers">The max number of players that may connect to this server.</param>
        /// <param name="regionId">The region to allocate the server in. May be null.</param>
        public async Task<bool> AllocateRelayServerAsync(int maxPlayers, string regionId)
        {
#if UTP_RELAY
            if(!await InitializeUnityServicesAsync()) { return false; }

            //Note: List of regions here https://docs.unity.com/ugs/manual/relay/manual/locations-and-regions
            if(!string.IsNullOrWhiteSpace(regionId)) {
                List<Region> listRegions = await RelayService.Instance.ListRegionsAsync();
                if(listRegions == null || listRegions.Count == 0) {
                    regionId = "";
                    UTPLog.Warning($"Unable to retrieve the list of Relay regions, will use most optimal region instead.");
                } else if(listRegions.Find(x => x.Id == regionId) == null) {
                    regionId = "";
                    UTPLog.Warning($"Invalid Relay Region ID, will use most optimal region instead.");
                }
            }

            _relayServerAllocation = await RelayService.Instance.CreateAllocationAsync(maxPlayers, regionId);
            if(_relayServerAllocation == null) {
                UTPLog.Error($"Unable to allocate Relay Server.");
                return false;
            }

            RelayJoinCode = await RelayService.Instance.GetJoinCodeAsync(_relayServerAllocation.AllocationId);
            if(string.IsNullOrWhiteSpace(RelayJoinCode)) {
                _relayServerAllocation = null;
                UTPLog.Error($"Unable to allocate Relay Server, encountered an error retrieving the Join Code.");
                return false;
            }

            UTPLog.Info($"Relay Server Allocated | Region: {_relayServerAllocation.Region} | Join Code: {RelayJoinCode}");

            return true;
#else
            UTPLog.Error($"Failed to allocate Relay Server: UTP assembly references are missing.");
            return false;
#endif
        }

        /// <summary>
        ///     Retrieves the
        ///     <seealso>
        ///         <cref>JoinAllocation</cref>
        ///     </seealso>
        ///     corresponding to the specified join code.
        /// </summary>
        /// <param name="joinCode">The join code that will be used to retrieve the JoinAllocation.</param>
        public async Task<bool> AllocateRelayClientAsync(string joinCode)
        {
#if UTP_RELAY
            if(!await InitializeUnityServicesAsync()) { return false; }

            RelayClientAllocation = await RelayService.Instance.JoinAllocationAsync(joinCode);
            if(RelayClientAllocation == null) {
                UTPLog.Error($"Failed to allocate Relay Client from Join Code: {joinCode}");
                return false;
            }

            return true;
#else
            UTPLog.Error($"Failed to allocate Relay Client: UTP assembly references are missing.");
            return false;
#endif
        }
#pragma warning restore
    }
}

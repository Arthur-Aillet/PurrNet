using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Epic.OnlineServices;
using Epic.OnlineServices.P2P;
using PurrNet.Logging;
using PurrNet.Transports;
using UnityEngine;
using Channel = PurrNet.Transports.Channel;

namespace PurrNet.EOS
{
    public class EosTransport : GenericTransport, ITransport
    {
        [Header("Server Settings")] [SerializeField]
        private ushort _serverPort = 5003;

        [Header("Client Settings")] [SerializeField]
        private string _address = "127.0.0.1";
        
        
        public ConnectionState clientState => _clientState;
        public ConnectionState listenerState => _listenerState;
        public IReadOnlyList<Connection> connections => _connections;

        public event OnConnected onConnected;
        public event OnDisconnected onDisconnected;
        public event OnDataReceived onDataReceived;
        public event OnDataSent onDataSent;
        public event OnConnectionState onConnectionState;

        private readonly List<Connection> _connections = new();
        private ConnectionState _clientState = ConnectionState.Disconnected;
        private ConnectionState _listenerState = ConnectionState.Disconnected;
        private string _socketName = "purrnet";
        private string _serverId;
        public override bool isSupported => true;
        public override ITransport transport => this;

        [ContextMenu("Force init")]
        private void ForceInit()
        {
            EOSWrapper.Init(NetworkManager.main);
        }
        
        protected override async void StartClientInternal()
        {
            try
            {
                if (!TryGetNetworkManager(NetworkManager.main, out var manager))
                {
                    PurrLogger.LogError($"EOS Transport failed to get the NetworkManager");
                    return;
                }
                
                EOSWrapper.Init(manager);
                await WaitForEOS();
                Connect(_address, _serverPort);
            }
            catch (Exception e)
            {
                PurrLogger.LogException(e);
            }
        }

        protected override async void StartServerInternal()
        {
            try
            {
                if (!TryGetNetworkManager(NetworkManager.main, out var manager))
                {
                    PurrLogger.LogError($"EOS Transport failed to get the NetworkManager");
                    return;
                }
                
                EOSWrapper.Init(manager);
                await WaitForEOS();
                _listenerState = ConnectionState.Connected;
            }
            catch (Exception e)
            {
                PurrLogger.LogException(e);
            }
        }
        
        private async UniTask WaitForEOS()
        {
            while (!EOSWrapper.IsReady)
                await UniTask.Yield();
        }

        public void Connect(string ip, ushort port)
        {
            _serverId = ip;
            var peer = ProductUserId.FromString(ip);

            var socket = new SocketId { SocketName = _socketName };
            var acceptOptions = new AcceptConnectionOptions
            {
                LocalUserId = EOSWrapper.LocalUserId,
                RemoteUserId = peer,
                SocketId = socket
            };

            EOSWrapper.Platform.GetP2PInterface().AcceptConnection(ref acceptOptions);
            _clientState = ConnectionState.Connected;
            onConnectionState?.Invoke(_clientState, false);
            var connection = new Connection(peer.GetHashCode());
            
            if (!_connections.Contains(connection))
                _connections.Add(connection);
            onConnected?.Invoke(connection, false);
            SendPacket(peer, new ByteData(new byte[1] { 0 }));
        }

        public void Listen(ushort port)
        {
            _listenerState = ConnectionState.Connected;
        }

        public void Disconnect()
        {
            _clientState = ConnectionState.Disconnected;
        }

        public void StopListening()
        {
            _listenerState = ConnectionState.Disconnected;
        }

        public void SendMessages(float delta)
        {
            if (_listenerState == ConnectionState.Connected)
                ReceiveMessagesInternal(true);

            if (_clientState == ConnectionState.Connected)
                ReceiveMessagesInternal(false);
        }

        public void CloseConnection(Connection conn)
        {
            _clientState = ConnectionState.Disconnected;
            _listenerState = ConnectionState.Disconnected;
        }

        public void ReceiveMessages(float delta) { }

        private void ReceiveMessagesInternal(bool asServer)
        {
            if (!EOSWrapper.IsReady)
                return;
            
            var p2p = EOSWrapper.Platform.GetP2PInterface();

            var options = new ReceivePacketOptions
            {
                LocalUserId = EOSWrapper.LocalUserId,
                MaxDataSizeBytes = 1024 * 8,
                RequestedChannel = 0
            };

            var buffer = new byte[1024 * 8];
            while (true)
            {
                ProductUserId peer = null;
                var socket = new SocketId { SocketName = _socketName };

                var result = p2p.ReceivePacket(ref options, ref peer, ref socket, out var channel, new ArraySegment<byte>(buffer), out var length);
                if (result != Result.Success)
                    break;

                var data = new byte[length];
                Array.Copy(buffer, data, length);

                var conn = new Connection(peer.GetHashCode());
                if (!_connections.Contains(conn))
                    _connections.Add(conn);

                Debug.Log($"Data received");
                onDataReceived?.Invoke(conn, new ByteData(data, 0, data.Length), asServer);
            }
        }

        public void SendToClient(Connection target, ByteData data, Channel method = Channel.ReliableOrdered)
        {
            var peer = ProductUserId.FromString(target.connectionId.ToString());
            SendPacket(peer, data);
        }

        public void SendToServer(ByteData data, Channel method = Channel.ReliableOrdered)
        {
            var peer = ProductUserId.FromString(_serverId);
            SendPacket(peer, data);
        }

        private void SendPacket(ProductUserId peer, ByteData data)
        {
            if (!EOSWrapper.IsReady)
                return;
            var sendOptions = new SendPacketOptions
            {
                LocalUserId = EOSWrapper.LocalUserId,
                RemoteUserId = peer,
                SocketId = new SocketId { SocketName = _socketName },
                Channel = 0,
                AllowDelayedDelivery = true,
                Data = data.segment,
                Reliability = PacketReliability.ReliableOrdered
            };

            EOSWrapper.Platform.GetP2PInterface().SendPacket(ref sendOptions);
        }

        public void RaiseDataReceived(Connection conn, ByteData data, bool asServer)
        {
            Debug.Log($"Data received");
            onDataReceived?.Invoke(conn, data, asServer);
        }

        public void RaiseDataSent(Connection conn, ByteData data, bool asServer)
        {
            Debug.Log($"Data sent");
            onDataSent?.Invoke(conn, data, asServer);
        }

        public bool shouldServerSendKeepAlive => false;
        public bool shouldClientSendKeepAlive => false;
    }
}

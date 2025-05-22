using System;
using System.Collections.Generic;
using Epic.OnlineServices;
using Epic.OnlineServices.P2P;
using PurrNet.Transports;
using UnityEngine;

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
            EOSWrapper.Init();
        }
        
        protected override void StartClientInternal()
        {
            EOSWrapper.Init();
            Connect(_address, _serverPort);
        }

        protected override void StartServerInternal()
        {
            EOSWrapper.Init();
            _listenerState = ConnectionState.Connected;
        }

        private void Update()
        {
            EOSWrapper.Tick();
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
            onConnected?.Invoke(new Connection(peer.GetHashCode()), false);
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
            EOSWrapper.Tick();
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
                var peer = ProductUserId.FromString("");
                var socket = new SocketId { SocketName = _socketName };
                byte channel;
                uint length;

                var result = p2p.ReceivePacket(ref options, ref peer, ref socket, out channel, new ArraySegment<byte>(buffer), out length);
                if (result != Result.Success)
                    break;

                var data = new byte[length];
                Array.Copy(buffer, data, length);

                var conn = new Connection(peer.GetHashCode());
                if (!_connections.Contains(conn))
                    _connections.Add(conn);

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
            => onDataReceived?.Invoke(conn, data, asServer);

        public void RaiseDataSent(Connection conn, ByteData data, bool asServer)
            => onDataSent?.Invoke(conn, data, asServer);

        public bool shouldServerSendKeepAlive => false;
        public bool shouldClientSendKeepAlive => false;
    }
}

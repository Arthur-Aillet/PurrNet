using System.Collections.Generic;
using PurrNet.Logging;
using PurrNet.Packing;

namespace PurrNet.Modules
{
    public struct IdentityStreamPacket
    {
        public int count;
        public BitPacker data;
    }

    public class IdentityStreamModule : INetworkModule, IPostFixedUpdate
    {
        readonly NetworkManager _manager;
        readonly HierarchyV2 _hierarchy;
        readonly DeltaModule _deltaModule;
        readonly ScenePlayersModule _scenePlayers;
        readonly SceneID _scene;
        private bool _asServer;

        private readonly List<StreamEntry> _identities = new();

        public IdentityStreamModule(NetworkManager manager, HierarchyV2 hierarchy, DeltaModule deltaModule, ScenePlayersModule scenePlayers, SceneID scene)
        {
            _manager = manager;
            _hierarchy = hierarchy;
            _scene = scene;
            _deltaModule = deltaModule;
            _scenePlayers = scenePlayers;
        }

        public void Enable(bool asServer)
        {
            _asServer = asServer;

            var ids = _hierarchy.identities;
            var c = _hierarchy.identities.Count;

            for (var i = 0; i < c; i++)
                OnIdentityAdded(ids[i]);

            _hierarchy.onIdentityAdded += OnIdentityAdded;
            _hierarchy.onIdentityRemoved += OnIdentityRemoved;

            _manager.Subscribe<IdentityStreamPacket>(OnStreamPacket, asServer);
        }

        public void Disable(bool asServer)
        {
            _hierarchy.onIdentityAdded -= OnIdentityAdded;
            _hierarchy.onIdentityRemoved -= OnIdentityRemoved;

            _manager.Unsubscribe<IdentityStreamPacket>(OnStreamPacket, asServer);
        }

        private void OnIdentityAdded(NetworkIdentity identity)
        {
            if (!identity.id.HasValue || identity is not IStreamNetwork stream)
                return;

            var searchResult = BinarySearch(identity.id.Value);
            var value = new StreamEntry
            {
                id = identity.id.Value,
                identity = identity,
                stream = stream
            };
            _identities.Insert(searchResult.index, value);
        }

        private void OnIdentityRemoved(NetworkIdentity identity)
        {
            if (!identity.id.HasValue || identity is not IStreamNetwork)
                return;

            var searchResult = BinarySearch(identity.id.Value);
            if (searchResult.found)
                _identities.RemoveAt(searchResult.index);
            else PurrLogger.LogError($"Identity {identity.name} not found in stream");
        }

        private void OnStreamPacket(PlayerID player, IdentityStreamPacket data, bool asServer)
        {
            throw new System.NotImplementedException();
        }

        public void PostFixedUpdate()
        {
            var mtu = _manager.unreliableChannelMTU;
            var count = _identities.Count;

            if (!_asServer)
            {
                if (_manager.isServer)
                    return;
                WriteForPlayer(default, count, mtu);
            }
            else if (_scenePlayers.TryGetPlayersInScene(_scene, out var players))
            {
                var localPlayer = _manager.localPlayer;
                for (var i = 0; i < players.Count; i++)
                {
                    if (localPlayer == players[i])
                        continue;
                    WriteForPlayer(players[i], count, mtu);
                }
            }
        }

        private void WriteForPlayer(PlayerID player, int count, int mtu)
        {
            using var packer = BitPackerPool.Get();
            var stream = new StreamPacker(packer, _deltaModule);

            for (var i = 0; i < count; i++)
            {
                var identity = _identities[i];
                var before = packer.positionInBits;

                if (player != default && !identity.identity.IsObserver(player))
                    continue;

                stream.SetContext(player, identity.id, _scene);
                identity.stream.StreamWrite(ref stream);

                if (!stream.writtenValues)
                {
                    packer.SetBitPosition(before);
                    continue;
                }

                ++stream.counter;

                if (packer.positionInBits > mtu)
                {
                    packer.SetBitPosition(before);
                    stream.Flush(_manager);
                }
            }

            stream.Flush(_manager);
        }

        struct SearchResult
        {
            public int index;
            public bool found;
        }

        private SearchResult BinarySearch(NetworkID toFind)
        {
            int low = 0;
            int high = _identities.Count - 1;

            while (low <= high)
            {
                int mid = (low + high) / 2;
                if (_identities[mid].id < toFind)
                {
                    low = mid + 1;
                }
                else if (_identities[mid].id > toFind)
                {
                    high = mid - 1;
                }
                else
                {
                    return new SearchResult
                    {
                        index = mid,
                        found = true
                    };
                }
            }

            return new SearchResult
            {
                index = low,
                found = false
            };
        }
    }
}

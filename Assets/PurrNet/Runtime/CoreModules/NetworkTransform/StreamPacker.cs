using PurrNet.Packing;

namespace PurrNet.Modules
{
    internal struct StreamContext : IStableHashable
    {
        public NetworkID id;
        public SceneID scene;

        public uint GetStableHash()
        {
            unchecked
            {
                ulong hash = 2166136261u;
                hash = (hash ^ id.id.value) * 16777619u;
                hash = (hash ^ scene.id.value) * 16777619u;
                hash = (hash ^ id.scope.id.value) * 16777619u;
                return (uint)hash;
            }
        }
    }

    public struct StreamPacker : IPacker
    {
        readonly BitPacker _packer;
        readonly DeltaModule _module;

        private StreamContext _context;
        private PlayerID _target;
        private PackedUInt _cache;

        public int counter;
        public bool writtenValues;

        public StreamPacker(BitPacker packer, DeltaModule module)
        {
            _packer = packer;
            _module = module;
            _context = default;
            _target = default;
            writtenValues = default;
            _cache = default;
            counter = 0;
        }

        internal void SetContext(PlayerID target, NetworkID id, SceneID scene)
        {
            _context.id = id;
            _context.scene = scene;
            _target = target;
            writtenValues = default;
        }

        public void Write<T>(T value)
        {
            writtenValues = _module.Write(_packer, _target, _context, value, ref _cache) || writtenValues;
        }

        public void Read<T>(ref T value)
        {
            _module.Read(_packer, _context, _target, ref value, ref _cache);
        }

        public void Flush(NetworkManager manager)
        {
            _cache = default;
            writtenValues = default;

            if (counter > 0)
            {
                var packet = new IdentityStreamPacket
                {
                    count = counter,
                    data = _packer
                };

                counter = default;

                if (_target == default)
                    manager.SendToServer(packet);
                else manager.Send(_target, packet);

                _packer.ResetPosition();
            }
        }
    }
}

using PurrNet.Packing;

namespace PurrNet.Modules
{
    public interface IStreamNetwork
    {
        void StreamWrite<T>(ref T packer) where T : IPacker;

        void StreamRead<T>(ref T packer) where T : IPacker;
    }

    internal struct StreamEntry
    {
        public NetworkID id;
        public NetworkIdentity identity;
        public IStreamNetwork stream;
    }
}

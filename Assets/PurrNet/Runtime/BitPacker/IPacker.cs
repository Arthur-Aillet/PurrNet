namespace PurrNet.Packing
{
    public interface IPacker
    {
        void Write<T>(T value);
        void Read<T>(ref T value);
    }
}

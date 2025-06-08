using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using PurrNet.Logging;
using PurrNet.Modules;
using PurrNet.Utils;

namespace PurrNet.Packing
{
    public delegate bool DeltaWriteFunc<in T>(BitPacker packer, T oldValue, T newValue);

    public delegate void DeltaReadFunc<T>(BitPacker packer, T oldValue, ref T value);

    public delegate void WriteFunc<in T>(BitPacker packer, T value);

    public delegate void ReadFunc<T>(BitPacker packer, ref T value);

    public static class DeltaPacker<T>
    {
        static DeltaWriteFunc<T> _write;
        static DeltaReadFunc<T> _read;

        public static int GetNecessaryBitsToWrite(in T oldValue, in T newValue)
        {
            if (_write == null)
            {
                PurrLogger.LogError($"No delta writer for type '{typeof(T)}' is registered.");
                return 0;
            }

            using var packer = BitPackerPool.Get();
            if (_write(packer, oldValue, newValue))
                return packer.positionInBits;
            return 0;
        }

        public static void Register(DeltaWriteFunc<T> write, DeltaReadFunc<T> read)
        {
            RegisterWriter(write);
            RegisterReader(read);
        }

        public static void RegisterWriter(DeltaWriteFunc<T> a)
        {
            if (_write != null)
                return;

            DeltaPacker.RegisterWriter(typeof(T), a.Method);
            _write = a;
        }

        public static void RegisterReader(DeltaReadFunc<T> b)
        {
            if (_read != null)
                return;

            DeltaPacker.RegisterReader(typeof(T), b.Method);
            _read = b;
        }

        public static bool Write(BitPacker packer, T oldValue, T newValue)
        {
            try
            {
                if (_write == null)
                {
                    PurrLogger.LogError($"No delta writer for type '{typeof(T)}' is registered.");
                    return false;
                }

                return _write(packer, oldValue, newValue);
            }
            catch (Exception e)
            {
                PurrLogger.LogError($"Failed to delta write value of type '{typeof(T)}'.\n{e.Message}\n{e.StackTrace}");
                return false;
            }
        }

        public static void Read(BitPacker packer, T oldValue, ref T value)
        {
            try
            {
                if (_read == null)
                {
                    PurrLogger.LogError($"No delta reader for type '{typeof(T)}' is registered.");
                    return;
                }

                _read(packer, oldValue, ref value);
            }
            catch (Exception e)
            {
                PurrLogger.LogError($"Failed to delta read value of type '{typeof(T)}'.\n{e.Message}\n{e.StackTrace}");
            }
        }

        public static void Serialize(BitPacker packer, T oldValue, ref T value)
        {
            if (packer.isWriting)
                Write(packer, oldValue, value);
            else Read(packer, oldValue, ref value);
        }
    }

    public static class DeltaPacker
    {
        static readonly Dictionary<Type, MethodInfo> _writeMethods = new Dictionary<Type, MethodInfo>();
        static readonly Dictionary<Type, MethodInfo> _readMethods = new Dictionary<Type, MethodInfo>();

        public static void RegisterWriter(Type type, MethodInfo method)
        {
            _writeMethods.TryAdd(type, method);
        }

        public static void RegisterReader(Type type, MethodInfo method)
        {
            _readMethods.TryAdd(type, method);
        }

        static readonly object[] _args = new object[3];

        public static void Write(BitPacker packer, Type type, object oldValue, object newValue)
        {
            if (!_writeMethods.TryGetValue(type, out var method))
            {
                PurrLogger.LogError($"No delta writer for type '{type}' is registered.");
                return;
            }

            try
            {
                _args[0] = packer;
                _args[1] = oldValue;
                _args[2] = newValue;
                method.Invoke(null, _args);
            }
            catch (Exception e)
            {
                PurrLogger.LogError($"Failed to delta write value of type '{type}'.\n{e.Message}\n{e.StackTrace}");
            }
        }

        public static void Read(BitPacker packer, Type type, object oldValue, ref object newValue)
        {
            if (!_readMethods.TryGetValue(type, out var method))
            {
                PurrLogger.LogError($"No delta reader for type '{type}' is registered.");
                return;
            }

            try
            {
                _args[0] = packer;
                _args[1] = oldValue;
                _args[2] = newValue;
                method.Invoke(null, _args);
                newValue = _args[2];
            }
            catch (Exception e)
            {
                PurrLogger.LogError($"Failed to delta read value of type '{type}'.\n{e.Message}\n{e.StackTrace}");
            }
        }
    }

    public static class Packer<T>
    {
        static WriteFunc<T> _write;
        static ReadFunc<T> _read;

        public static void RegisterWriter(WriteFunc<T> a)
        {
            Packer.RegisterWriter(typeof(T), a.Method);
            _write ??= a;
        }

        public static void RegisterReader(ReadFunc<T> b)
        {
            Packer.RegisterReader(typeof(T), b.Method);
            _read ??= b;
        }

        public static void Write(BitPacker packer, T value)
        {
            try
            {
                if (_write == null)
                {
                    Packer.FallbackWriter(packer, value);
                    return;
                }

                _write(packer, value);
            }
            catch (Exception e)
            {
                PurrLogger.LogError($"Failed to write value of type '{typeof(T)}'.\n{e.Message}\n{e.StackTrace}");
            }
        }

        public static void Read(BitPacker packer, ref T value)
        {
            try
            {
                if (_read == null)
                {
                    Packer.FallbackReader(packer, ref value);
                    return;
                }

                _read(packer, ref value);
            }
            catch (Exception e)
            {
                PurrLogger.LogError($"Failed to read value of type '{typeof(T)}'.\n{e.Message}\n{e.StackTrace}");
            }
        }

        public static T Read(BitPacker packer)
        {
            var value = default(T);
            Read(packer, ref value);
            return value;
        }

        public static void Serialize(BitPacker packer, ref T value)
        {
            if (packer.isWriting)
                Write(packer, value);
            else Read(packer, ref value);
        }
    }

    public static class Packer
    {
        public static T Copy<T>(T value)
        {
            if (!RuntimeHelpers.IsReferenceOrContainsReferences<T>())
                return value;

            using var tmpPacker = BitPackerPool.Get();
            Packer<T>.Write(tmpPacker, value);
            tmpPacker.ResetPositionAndMode(true);
            var copy = default(T);
            Packer<T>.Read(tmpPacker, ref copy);
            return copy;
        }

        [UsedByIL]
        public static bool AreEqual<T>(T a, T b)
        {
            using var packerA = BitPackerPool.Get();
            using var packerB = BitPackerPool.Get();

            Packer<T>.Write(packerA, a);
            Packer<T>.Write(packerB, b);

            if (packerA.positionInBits != packerB.positionInBits)
                return false;

            int bits = packerA.positionInBits;

            packerA.ResetPositionAndMode(true);
            packerB.ResetPositionAndMode(true);

            while (bits >= 64)
            {
                ulong aBits = packerA.ReadBits(64);
                ulong bBits = packerB.ReadBits(64);

                if (aBits != bBits)
                    return false;

                bits -= 64;
            }

            if (bits > 0)
            {
                var remainingBits = (byte)bits;
                ulong aBits = packerA.ReadBits(remainingBits);
                ulong bBits = packerB.ReadBits(remainingBits);
                if (aBits != bBits)
                    return false;
            }

            return true;
        }

        [UsedByIL]
        public static bool AreEqualRef<T>(ref T a, ref T b)
        {
            return AreEqual(a, b);
        }

        static readonly Dictionary<Type, MethodInfo> _writeMethods = new Dictionary<Type, MethodInfo>();
        static readonly Dictionary<Type, MethodInfo> _readMethods = new Dictionary<Type, MethodInfo>();

        public static void RegisterWriter(Type type, MethodInfo method)
        {
            _writeMethods.TryAdd(type, method);
        }

        public static void RegisterReader(Type type, MethodInfo method)
        {
            Hasher.PrepareType(type);
            _readMethods.TryAdd(type, method);
        }

        static readonly object[] _args = new object[2];

        public static void FallbackWriter<T>(BitPacker packer, T value)
        {
            try
            {
                bool hasValue = value != null;
                Packer<bool>.Write(packer, hasValue);

                if (!hasValue) return;

                object obj = value;
                uint typeHash = Hasher.GetStableHashU32(obj.GetType());

                Packer<uint>.Write(packer, typeHash);
                Write(packer, obj);
            }
            catch (Exception e)
            {
                PurrLogger.LogError(
                    $"Failed to write value of type '{typeof(T)}' when using fallback writer.\n{e.Message}\n{e.StackTrace}");
            }
        }

        public static void FallbackReader<T>(BitPacker packer, ref T value)
        {
            try
            {
                bool hasValue = default;
                Packer<bool>.Read(packer, ref hasValue);

                if (!hasValue)
                {
                    value = default;
                    return;
                }

                uint typeHash = default;
                Packer<uint>.Read(packer, ref typeHash);

                var type = Hasher.ResolveType(typeHash);

                object obj = null;
                Read(packer, type, ref obj);

                if (obj is T entity)
                    value = entity;
                else value = default;
            }
            catch (Exception e)
            {
                PurrLogger.LogError(
                    $"Failed to read value of type '{typeof(T)}' when using fallback reader.\n{e.Message}\n{e.StackTrace}");
            }
        }

        public static void Write(BitPacker packer, Type type, object value)
        {
            if (!_writeMethods.TryGetValue(type, out var method))
            {
                PurrLogger.LogError($"No writer for type '{type}' is registered.");
                return;
            }

            try
            {
                _args[0] = packer;
                _args[1] = value;
                method.Invoke(null, _args);
            }
            catch (Exception e)
            {
                PurrLogger.LogError($"Failed to write value of type '{type}'.\n{e.Message}\n{e.StackTrace}");
            }
        }

        [UsedByIL]
        public static void WriteGeneric<T>(BitPacker packer, T value)
        {
            var type = value == null ? typeof(T) : value.GetType();
            Packer<PackedUInt>.Write(packer, Hasher.GetStableHashU32(type));
            Write(packer, type, value);
        }

        [UsedByIL]
        public static void ReadGeneric(BitPacker packer, ref object value)
        {
            PackedUInt hash = default;
            Packer<PackedUInt>.Read(packer, ref hash);
            if (!Hasher.TryGetType(hash, out var type))
                throw new Exception($"Type with hash '{hash}' not found.");

            Read(packer, type, ref value);
        }

        public static void Write(BitPacker packer, object value)
        {
            var type = value.GetType();

            if (!_writeMethods.TryGetValue(type, out var method))
            {
                PurrLogger.LogError($"No writer for type '{type}' is registered.");
                return;
            }

            try
            {
                _args[0] = packer;
                _args[1] = value;
                method.Invoke(null, _args);
            }
            catch (Exception e)
            {
                PurrLogger.LogError($"Failed to write value of type '{type}'.\n{e.Message}\n{e.StackTrace}");
            }
        }

        public static void Read(BitPacker packer, Type type, ref object value)
        {
            if (!_readMethods.TryGetValue(type, out var method))
            {
                PurrLogger.LogError($"No reader for type '{type}' is registered.");
                return;
            }

            try
            {
                _args[0] = packer;
                _args[1] = value;
                method.Invoke(null, _args);
                value = _args[1];
            }
            catch (Exception e)
            {
                PurrLogger.LogError($"Failed to read value of type '{type}'.\n{e.Message}\n{e.StackTrace}");
            }
        }

        public static void Serialize(BitPacker packer, Type type, ref object value)
        {
            if (packer.isWriting)
                Write(packer, value);
            else Read(packer, type, ref value);
        }
    }
}

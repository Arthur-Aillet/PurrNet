using PurrNet.Logging;
using PurrNet.Modules;
using PurrNet.Packing;
using PurrNet.Pooling;
using UnityEngine;

namespace PurrNet
{
    [RegisterNetworkType(typeof(DisposableList<int>))]
    public static class PackNetworkIdentity
    {
        [UsedByIL]
        public static bool PackNetworkIDNullableDelta(this BitPacker stream, NetworkID? old, NetworkID? nid)
        {
            int flagPos = stream.AdvanceBits(1);
            bool wasChanged = default(bool);
            if (stream.HandleNullScenarios(old, nid, ref wasChanged))
            {
                wasChanged = DeltaPacker<PackedULong>.Write(stream, old!.Value.id, nid!.Value.id);
                wasChanged = DeltaPacker<PlayerID>.Write(stream, old.Value.scope, nid.Value.scope) || wasChanged;
            }
            stream.WriteAt(flagPos, wasChanged);
            if (!wasChanged)
            {
                stream.SetBitPosition(flagPos + 1);
            }
            return wasChanged;
        }

        [UsedByIL]
        public static void PackNetworkIDNullableDelta(this BitPacker stream, NetworkID? oldValue, ref NetworkID? value)
        {
            bool value2 = default(bool);
            Packer<bool>.Read(stream, ref value2);
            if (value2)
            {
                if (stream.ReadIsNull(ref value))
                {
                    var oldId = oldValue!.Value.id;
                    var oldScope = oldValue.Value.scope;

                    PackedULong newId = default;
                    PlayerID newScope = default;

                    DeltaPacker<PackedULong>.Read(stream, oldId, ref newId);
                    DeltaPacker<PlayerID>.Read(stream, oldScope, ref newScope);

                    value = new NetworkID(newId, newScope);
                }
            }
            else
            {
                value = oldValue;
            }
        }

        [UsedByIL]
        public static void WriteIdentityConcrete(this BitPacker packer, NetworkIdentity identity)
        {
            WriteIdentity(packer, identity);
        }

        [UsedByIL]
        public static void ReadIdentityConcrete(this BitPacker packer, ref NetworkIdentity identity)
        {
            ReadIdentity(packer, ref identity);
        }

        [UsedByIL]
        public static void WriteGameObject(this BitPacker packer, GameObject go)
        {
            Transform trs = null;
            if (go)
                trs = go.transform;
            WriteTrasform(packer, trs);
        }

        [UsedByIL]
        public static void ReadGameObject(this BitPacker packer, ref GameObject go)
        {
            Transform trs = null;
            ReadTrasform(packer, ref trs);
            go = trs ? trs.gameObject : null;
        }

        static NetworkIdentity GetSpawnedParent(Transform trs)
        {
            if (!trs)
                return null;

            if (trs.TryGetComponent<NetworkIdentity>(out var identity) && identity.isSpawned)
                return identity;

            while (true)
            {
                if (!trs)
                    return null;

                var parent = trs.GetComponentInParent<NetworkIdentity>(true);

                if (!parent)
                    return null;

                if (parent.isSpawned)
                    return parent;

                trs = parent.transform;
            }
        }

        static Transform WalkThePath(Transform parent, DisposableList<int> inversedPath)
        {
            try
            {
                if (inversedPath.list == null || inversedPath.Count == 0)
                    return parent;

                int len = inversedPath.Count;
                for (var i = len - 1; i >= 0; i--)
                {
                    var siblingIndex = inversedPath[i];

                    if (parent.childCount <= siblingIndex)
                    {
                        PurrLogger.LogWarning($"Parent {parent} doesn't have child with index {siblingIndex}");
                        break;
                    }

                    parent = parent.GetChild(siblingIndex);
                }

                return parent;
            }
            catch
            {
                return null;
            }
        }

        [UsedByIL]
        public static void WriteTrasform(this BitPacker packer, Transform trs)
        {
            var parent = GetSpawnedParent(trs);

            if (!parent || !parent.isSpawned || !parent.id.HasValue)
            {
                Packer<bool>.Write(packer, false);
                return;
            }

            Packer<bool>.Write(packer, true);
            using var invPath = HierarchyPool.GetInvPath(parent.transform, trs);

            Packer<SceneID>.Write(packer, parent.sceneId);
            Packer<NetworkID>.Write(packer, parent.id.Value);
            Packer<DisposableList<int>>.Write(packer, invPath);
        }

        [UsedByIL]
        public static void ReadTrasform(this BitPacker packer, ref Transform trs)
        {
            bool hasValue = false;

            Packer<bool>.Read(packer, ref hasValue);

            if (!hasValue)
            {
                trs = null;
                return;
            }

            SceneID sceneId = default;
            NetworkID id = default;
            DisposableList<int> invPath = default;

            Packer<SceneID>.Read(packer, ref sceneId);
            Packer<NetworkID>.Read(packer, ref id);
            Packer<DisposableList<int>>.Read(packer, ref invPath);

            var networkManager = NetworkManager.main;

            if (!networkManager)
            {
                trs = null;
                return;
            }

            if (!networkManager.TryGetModule<HierarchyFactory>(networkManager.isServer, out var module) ||
                !module.TryGetIdentity(sceneId, id, out var result))
            {
                trs = null;
                return;
            }

            var root = result.transform;
            trs = WalkThePath(root, invPath);
            invPath.Dispose();
        }

        [UsedByIL]
        public static void RegisterIdentity<T>() where T : NetworkIdentity
        {
            Packer<T>.RegisterWriter(WriteIdentity);
            Packer<T>.RegisterReader(ReadIdentity);
        }

        [UsedByIL]
        public static void WriteIdentity<T>(this BitPacker packer, T value) where T : NetworkIdentity
        {
            if (value == null || !value.id.HasValue)
            {
                Packer<bool>.Write(packer, false);
                return;
            }

            Packer<bool>.Write(packer, true);
            Packer<NetworkID>.Write(packer, value.id.Value);
            Packer<SceneID>.Write(packer, value.sceneId);
        }

        [UsedByIL]
        public static void ReadIdentity<T>(this BitPacker packer, ref T value) where T : NetworkIdentity
        {
            bool hasValue = false;

            Packer<bool>.Read(packer, ref hasValue);

            if (!hasValue)
            {
                value = null;
                return;
            }

            NetworkID id = default;
            SceneID sceneId = default;

            Packer<NetworkID>.Read(packer, ref id);
            Packer<SceneID>.Read(packer, ref sceneId);

            var networkManager = NetworkManager.main;

            if (!networkManager)
            {
                value = null;
                return;
            }

            if (!networkManager.TryGetModule<HierarchyFactory>(networkManager.isServer, out var module) ||
                !module.TryGetIdentity(sceneId, id, out var result) || result is not T identity)
            {
                value = null;
                return;
            }

            value = identity;
        }
    }
}

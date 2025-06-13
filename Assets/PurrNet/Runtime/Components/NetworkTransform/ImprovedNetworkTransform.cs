using PurrNet.Logging;
using PurrNet.Modules;
using PurrNet.Packing;
using PurrNet.Transports;
using PurrNet.Utils;
using UnityEngine;

namespace PurrNet
{
    public sealed class ImprovedNetworkTransform : NetworkIdentity, ITick
    {
        [Header("What to Sync")]
        [SerializeField, PurrLock]
        private SyncMode _syncPosition = SyncMode.World;

        [SerializeField, PurrLock]
        private SyncMode _syncRotation = SyncMode.World;

        [SerializeField, PurrLock]
        private bool _syncScale = true;

        [SerializeField, PurrLock]
        private bool _syncParent = true;

        [Header("How to Sync")]
        [SerializeField, PurrLock]
        private TransformSyncMode _interpolateSettings = TransformSyncMode.Position | TransformSyncMode.Rotation | TransformSyncMode.Scale;

        [SerializeField, PurrLock, Min(1)]
        private int _minBufferSize = 1;

        [SerializeField, PurrLock, Min(1)]
        private int _maxBufferSize = 2;

        [Header("When to Sync")]
        [SerializeField, PurrLock]
        private bool _ownerAuth = true;

        [SerializeField]
        private InterpolationTiming _interpolationTiming = InterpolationTiming.LateUpdate;

        public bool syncPosition => _syncPosition != SyncMode.No;
        public bool syncRotation => _syncRotation != SyncMode.No;
        public bool syncScale => _syncScale;
        public bool syncParent => _syncParent;
        public bool ownerAuth => _ownerAuth;

        public bool interpolatePosition => _interpolateSettings.HasFlag(TransformSyncMode.Position);
        public bool interpolateRotation => _interpolateSettings.HasFlag(TransformSyncMode.Rotation);
        public bool interpolateScale => _interpolateSettings.HasFlag(TransformSyncMode.Scale);

        public Vector3 position { get; private set; }
        public Quaternion rotation { get; private set; }
        public Vector3 localScale { get; private set; }

        public int ticksBehind
        {
            get
            {
                if (syncPosition) return _position.bufferSize;
                if (syncRotation) return _rotation.bufferSize;
                if (syncScale) return _scale.bufferSize;
                return 0;
            }
        }

        private Transform _trs;
        private DeltaModule _deltaModule;
        private PlayersBroadcaster _broadcaster;

        private Interpolated<Vector3WithParent> _position;
        private Interpolated<QuaternionWithParent> _rotation;
        private Interpolated<ScaleWithParent> _scale;

        private NetworkTransformData _currentData;
        private NetworkTransformData _latestData;
        private NetworkTransformData _lastReadData;
        
        private bool _parentChanged;
        private bool _isIgnoringParentChanges;

        private void Awake()
        {
            _trs = transform;
        }

        protected override void OnEarlySpawn()
        {
            if (!networkManager.TryGetModule<DeltaModule>(isServer, out _deltaModule))
            {
                PurrLogger.LogError("DeltaModule not found for ImprovedNetworkTransform", this);
                return;
            }

            if (!networkManager.TryGetModule<PlayersBroadcaster>(isServer, out _broadcaster))
            {
                PurrLogger.LogError("PlayersBroadcaster not found for ImprovedNetworkTransform", this);
                return;
            }

            _broadcaster.Subscribe<ImprovedTransformUpdate>(OnTransformUpdate);

            float sendDelta = networkManager.tickModule.tickDelta;
            var p = _trs.parent;

            if (syncPosition)
            {
                var currentPos = _syncPosition == SyncMode.World ?
                    new Vector3WithParent(p, false, _trs.position) :
                    new Vector3WithParent(p, true, _trs.localPosition);
                _position = new Interpolated<Vector3WithParent>(
                    interpolatePosition ? Vector3WithParent.Lerp : Vector3WithParent.NoLerp,
                    sendDelta, currentPos, _maxBufferSize, _minBufferSize);
            }

            if (syncRotation)
            {
                var currentRot = _syncRotation == SyncMode.World ?
                    new QuaternionWithParent(p, false, _trs.rotation) :
                    new QuaternionWithParent(p, true, _trs.localRotation);
                _rotation = new Interpolated<QuaternionWithParent>(
                    interpolateRotation ? QuaternionWithParent.Lerp : QuaternionWithParent.NoLerp,
                    sendDelta, currentRot, _maxBufferSize, _minBufferSize);
            }

            if (syncScale)
            {
                var currentScale = new ScaleWithParent(p, _trs.localScale);
                _scale = new Interpolated<ScaleWithParent>(
                    interpolateScale ? ScaleWithParent.Lerp : ScaleWithParent.NoLerp,
                    sendDelta, currentScale, _maxBufferSize, _minBufferSize);
            }

            _currentData = GetCurrentTransformData();
            _latestData = _currentData;
            _lastReadData = _currentData;
        }

        protected override void OnDespawned(bool asServer)
        {
            if (_broadcaster != null)
            {
                _broadcaster.Unsubscribe<ImprovedTransformUpdate>(OnTransformUpdate);
            }
        }

        public void OnTick(float delta)
        {
            _latestData = GetCurrentTransformData();
            
            if (IsControlling(networkManager.localPlayer, isServer))
            {
                GatherState();
                
                SendTransformUpdate();
            }
        }

        private void SendTransformUpdate()
        {
            if (isServer)
            {
                foreach (var observer in observers)
                {
                    if (observer != networkManager.localPlayer)
                    {
                        SendToPlayer(observer);
                    }
                }
            }
            else
            {
                SendToPlayer(PlayerID.Server);
            }
        }

        private void SendToPlayer(PlayerID targetPlayer)
        {
            using var packer = BitPackerPool.Get();
            
            if (DeltaWrite(packer, targetPlayer))
            {
                var transformData = new ImprovedTransformUpdate
                {
                    networkId = id?.id.value ?? 0,
                    data = packer.ToByteData()
                };

                if (targetPlayer == PlayerID.Server)
                    _broadcaster.SendToServer(transformData, Channel.Unreliable);
                else
                    _broadcaster.Send(targetPlayer, transformData, Channel.Unreliable);
            }
            
            _currentData = _latestData;
        }

        private void OnTransformUpdate(PlayerID sender, ImprovedTransformUpdate data, bool asServer)
        {
            if (data.networkId != (id?.id.value ?? 0))
                return;

            using var packer = BitPackerPool.Get();
            packer.WriteBytes(data.data.span);
            packer.ResetPositionAndMode(true);
            
            DeltaRead(packer, sender);
        }

        private bool DeltaWrite(BitPacker packer, PlayerID targetPlayer)
        {
            var key = GetTransformKey();
            return _deltaModule.Write(packer, targetPlayer, key, _currentData);
        }

        private void DeltaRead(BitPacker packer, PlayerID sender)
        {
            var key = GetTransformKey();
            NetworkTransformData newData = default;
            
            _deltaModule.Read(packer, key, sender, ref newData);
            
            if (!newData.Equals(_lastReadData))
            {
                _lastReadData = newData;
                ApplyData(newData);
            }
        }

        private TransformKey GetTransformKey()
        {
            return new TransformKey { networkId = id?.id.value ?? 0 };
        }

        private NetworkTransformData GetCurrentTransformData()
        {
            var pos = _syncPosition switch
            {
                SyncMode.World => _trs.position,
                SyncMode.Local => _trs.localPosition,
                _ => Vector3.zero
            };

            var rot = _syncRotation switch
            {
                SyncMode.World => _trs.rotation,
                SyncMode.Local => _trs.localRotation,
                _ => Quaternion.identity
            };

            var scale = _syncScale ? _trs.localScale : Vector3.one;
            return new NetworkTransformData(pos, rot, scale);
        }

        private void ApplyData(NetworkTransformData data)
        {
            var p = _trs.parent;
            
            if (syncPosition)
                _position.Add(new Vector3WithParent(p, _syncPosition == SyncMode.Local, data.position));

            if (syncRotation)
                _rotation.Add(new QuaternionWithParent(p, _syncRotation == SyncMode.Local, data.rotation));

            if (syncScale)
                _scale.Add(new ScaleWithParent(p, data.scale));
        }

        public void GatherState()
        {
            _currentData = _latestData;
            if (IsController(_ownerAuth))
                TeleportToData(_currentData);
        }

        private void TeleportToData(NetworkTransformData data)
        {
            var p = _trs.parent;

            if (syncPosition)
                _position.Teleport(new Vector3WithParent(p, _syncPosition == SyncMode.Local, data.position));

            if (syncRotation)
                _rotation.Teleport(new QuaternionWithParent(p, _syncRotation == SyncMode.Local, data.rotation));

            if (syncScale)
                _scale.Teleport(new ScaleWithParent(p, data.scale));
        }

        public bool IsControlling(PlayerID player, bool asServer)
        {
            return IsController(player, _ownerAuth, asServer);
        }

        private void Update()
        {
            if (_interpolationTiming == InterpolationTiming.Update)
                UpdateTransform();
        }

        private void LateUpdate()
        {
            if (_interpolationTiming == InterpolationTiming.LateUpdate)
                UpdateTransform();
                
            if (_parentChanged)
            {
                OnTransformParentChangedDelayed();
                _parentChanged = false;
            }
        }

        private void UpdateTransform()
        {
            if (!isSpawned)
                return;

            bool isLocalController = IsController(_ownerAuth);

            if (!isLocalController)
                ApplyLerpedPosition();

            if (isLocalController)
            {
                _latestData = GetCurrentTransformData();
                TeleportToData(_latestData);
            }
        }

        private void ApplyLerpedPosition()
        {
            if (syncPosition)
            {
                var worldPos = _position.Advance(Time.deltaTime).position;
                _trs.position = worldPos;
                position = worldPos;
            }

            if (syncRotation)
            {
                var worldRot = _rotation.Advance(Time.deltaTime).rotation;
                _trs.rotation = worldRot;
                rotation = worldRot;
            }

            if (syncScale)
            {
                var worldScale = _scale.Advance(Time.deltaTime).scale;
                var parentTrs = _trs.parent;
                var ls = parentTrs ? parentTrs.GetLocalScale(worldScale) : worldScale;
                _trs.localScale = ls;
                localScale = ls;
            }
        }

        void OnTransformParentChanged()
        {
            if (!isSpawned)
                return;

            if (_isIgnoringParentChanges)
                return;

            if (!syncParent)
                return;

            _parentChanged = true;
        }

        void OnTransformParentChangedDelayed()
        {
            if (_isIgnoringParentChanges)
                return;

            if (!isSpawned)
                return;

            if (!_trs)
                return;

            if (syncParent)
                HandleParentChanged(_trs.parent);
        }

        private void HandleParentChanged(Transform parent)
        {
            if (networkManager.TryGetModule<HierarchyFactory>(isServer, out var factory) &&
                factory.TryGetHierarchy(sceneId, out var hierarchy))
            {
                hierarchy.OnParentChanged(this, parent);
            }
        }

        public void SetIgnoringParentChanges(bool ignore)
        {
            _isIgnoringParentChanges = ignore;
        }
    }

    public struct ImprovedTransformUpdate : IPackedAuto
    {
        public ulong networkId;
        public ByteData data;
    }

    public struct TransformKey : IStableHashable
    {
        public ulong networkId;

        public uint GetStableHash()
        {
            return (uint)networkId;
        }
    }
}
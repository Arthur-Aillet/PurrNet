using System;
using System.Collections.Generic;
using PurrNet.Logging;
using PurrNet.Transports;
using PurrNet.Utils;
using UnityEngine;

namespace PurrNet
{
    public abstract class ASyncType<TCollection, TChange> : NetworkModule, ITick
    {
        [SerializeField] protected bool _ownerAuth;
        [SerializeField, Min(0)] protected float _sendIntervalInSeconds;
        
        protected List<TChange> _pendingChanges = new();
        protected float _lastSendTime;
        protected bool _isDirty;
        protected bool _wasLastDirty;
        
        /// <summary>
        /// Whether the sync type is owner or server authorized
        /// </summary>
        public bool ownerAuth => _ownerAuth;
        
        /// <summary>
        /// How often to send data
        /// </summary>
        public float sendIntervalInSeconds
        {
            get => _sendIntervalInSeconds;
            set => _sendIntervalInSeconds = value;
        }
        
        protected abstract TCollection GetFullState();
        protected abstract void ApplyFullState(TCollection state);
        protected abstract void ApplyChange(TChange change);
        protected abstract void InvokeChangeEvent(TChange change);
        
        public override void OnInitializeModules()
        {
            base.OnInitializeModules();
            if (!IsController(_ownerAuth)) return;
            
            var state = GetFullState();
            if (isServer)
                SendFullStateToAll(state);
            else 
                SendFullStateToServer(state);
        }
        
        public override void OnObserverAdded(PlayerID player)
        {
            if (player == localPlayer)
                return;
            
            SendFullStateToTarget(player, GetFullState());
        }
        
        public void OnTick(float delta)
        {
            if (!IsController(_ownerAuth))
                return;

            float timeSinceLastSend = Time.time - _lastSendTime;

            if (timeSinceLastSend < _sendIntervalInSeconds)
                return;

            if (_isDirty)
            {
                foreach (var change in _pendingChanges)
                {
                    if (isServer)
                        SendChangeToAll(change);
                    else
                        SendChangeToServer(change);
                }
                
                _pendingChanges.Clear();
                _lastSendTime = Time.time;
                _wasLastDirty = true;
                _isDirty = false;
            }
            else if (_wasLastDirty)
            {
                var state = GetFullState();
                if (isServer)
                    SendFullStateToAll(state);
                else
                    ForceSendReliable(state);
                _wasLastDirty = false;
            }
        }
        
        protected void QueueChange(TChange change)
        {
            _pendingChanges.Add(change);
            _isDirty = true;
        }
        
        protected void ValidateAuthority()
        {
            if (!isSpawned) return;

            bool controller = parent.IsController(_ownerAuth);
            if (!controller)
            {
                PurrLogger.LogError(
                    $"Invalid permissions when modifying '<b>{GetType().Name} {name}</b>' on '{parent.name}'." +
                    $"\nMaybe try enabling owner authority.", parent);
                throw new InvalidOperationException("Invalid permissions");
            }
        }
        
        #region Generic RPCs
        
        [TargetRpc(Channel.ReliableOrdered)]
        private void SendFullStateToTarget<T>(PlayerID player, T state)
        {
            HandleFullState(state);
        }
        
        [ObserversRpc(Channel.ReliableOrdered)]
        private void SendFullStateToAll<T>(T state)
        {
            HandleFullState(state);
        }
        
        [ServerRpc(Channel.ReliableOrdered, requireOwnership: true)]
        private void SendFullStateToServer<T>(T state)
        {
            if (!_ownerAuth) return;
            SendFullStateToOthers(state);
        }
        
        [ObserversRpc(Channel.ReliableOrdered, excludeOwner: true)]
        private void SendFullStateToOthers<T>(T state)
        {
            HandleFullState(state);
        }
        
        private void HandleFullState<T>(T state)
        {
            if (!isHost && state is TCollection collection)
            {
                ApplyFullState(collection);
            }
        }
        
        [ServerRpc(Channel.ReliableOrdered, requireOwnership: true)]
        private void SendChangeToServer<T>(T change)
        {
            if (!_ownerAuth) return;
            SendChangeToOthers(change);
        }
        
        [ObserversRpc(Channel.ReliableOrdered, excludeOwner: true)]
        private void SendChangeToOthers<T>(T change)
        {
            HandleChange(change);
        }
        
        [ObserversRpc(Channel.ReliableOrdered)]
        private void SendChangeToAll<T>(T change)
        {
            HandleChange(change);
        }
        
        private void HandleChange<T>(T change)
        {
            if (!isHost && change is TChange typedChange)
            {
                ApplyChange(typedChange);
                InvokeChangeEvent(typedChange);
            }
        }
        
        [ServerRpc(Channel.ReliableOrdered)]
        private void ForceSendReliable<T>(T state)
        {
            SendFullStateToAll(state);
        }
        
        #endregion
    }
}
using UnityEngine;
using PurrNet.Logging;
using System;
using System.Collections;
using System.Collections.Generic;
using PurrNet.Transports;
using UnityEngine.Scripting;

namespace PurrNet
{
    /// <summary>
    /// The operation which has happened to the list
    /// </summary>
    public enum SyncListOperation
    {
        Added,
        Removed,
        Insert,
        Set,
        Cleared
    }

    /// <summary>
    /// All the data relevant to the change that happened to the list
    /// </summary>
    public readonly struct SyncListChange<T>
    {
        public readonly SyncListOperation operation;
        public readonly T value;
        public readonly int index;

        public SyncListChange(SyncListOperation operation, T value = default, int index = -1)
        {
            this.operation = operation;
            this.value = value;
            this.index = index;
        }

        public override string ToString()
        {
            string valueStr = $"Value: {value} | Operation: {operation} | Index: {index}";
            return valueStr;
        }
    }

    [Serializable]
    public class SyncList<T> : NetworkModule, IList<T>, ITick
    {
        [SerializeField] private bool _ownerAuth;
        [SerializeField, Min(0)] private float _sendIntervalInSeconds;
        [SerializeField] private List<T> _list = new List<T>();
        
        public List<T> list => _list;
        public List<T> ToList() => _list;

        public delegate void SyncListChanged<TYPE>(SyncListChange<TYPE> change);

        /// <summary>
        /// Event that is invoked when the list is changed
        /// </summary>
        public event SyncListChanged<T> onChanged;

        /// <summary>
        /// Whether it is the owner or the server that has the authority to modify the list
        /// </summary>
        public bool ownerAuth => _ownerAuth;
        
        public float sendIntervalInSeconds
        {
            get => _sendIntervalInSeconds;
            set => _sendIntervalInSeconds = value;
        }

        /// <summary>
        /// The amount of entries in the list
        /// </summary>
        public int Count => _list.Count;

        public bool IsReadOnly => false;
        
        private List<SyncListChange<T>> _pendingChanges = new();
        private float _lastSendTime;
        private bool _wasLastDirty;
        private bool _isDirty;

        public SyncList(bool ownerAuth = false)
        {
            _ownerAuth = ownerAuth;
        }

        public SyncList(List<T> defaultValues, bool ownerAuth = false)
        {
            _list = defaultValues;
            _ownerAuth = ownerAuth;
        }

        public T this[int idx]
        {
            get => _list[idx];
            set
            {
                ValidateAuthority();

                var oldValue = _list[idx];

                if (oldValue.Equals(value))
                    return;

                _list[idx] = value;

                var change = new SyncListChange<T>(SyncListOperation.Set, value, idx);
                QueueChange(change);
                InvokeChange(change);

                if (isSpawned)
                {
                    if (isServer)
                        SendSetToAll(idx, value);
                    else
                        SendSetToServer(idx, value);
                }
            }
        }
        
        private void QueueChange(SyncListChange<T> change)
        {
            _pendingChanges.Add(change);
            _isDirty = true;
        }

        public override void OnSpawn()
        {
            if (!IsController(_ownerAuth)) return;

            if (isServer)
                SendInitialStateToAll(_list);
            else SendInitialStateToServer(_list);
        }

        public override void OnObserverAdded(PlayerID player)
        {
            SendInitialToTarget(player, _list);
        }

        [TargetRpc(Channel.ReliableOrdered), Preserve]
        private void SendInitialToTarget(PlayerID player, List<T> items)
        {
            HandleInitialState(items);
        }

        [ObserversRpc(Channel.ReliableOrdered)]
        private void SendInitialStateToAll(List<T> items)
        {
            HandleInitialState(items);
        }

        private void HandleInitialState(List<T> items)
        {
            if (!isHost)
            {
                if (items == null)
                    return;
                _list.Clear();
                _list.AddRange(items);

                var change = new SyncListChange<T>(SyncListOperation.Cleared);
                InvokeChange(change);

                for (int i = 0; i < items.Count; i++)
                {
                    var changeI = new SyncListChange<T>(SyncListOperation.Added, items[i], i);
                    InvokeChange(changeI);
                }
            }
        }

        [ServerRpc(Channel.ReliableOrdered, requireOwnership: true)]
        private void SendInitialStateToServer(List<T> items)
        {
            if (!_ownerAuth) return;
            SendInitialStateToOthers(items);
        }

        [ObserversRpc(Channel.ReliableOrdered, excludeOwner: true)]
        private void SendInitialStateToOthers(List<T> items)
        {
            if (!isServer || isHost)
            {
                _list.Clear();
                _list.AddRange(items);

                var change = new SyncListChange<T>(SyncListOperation.Cleared);
                InvokeChange(change);

                for (int i = 0; i < items.Count; i++)
                {
                    var changeI = new SyncListChange<T>(SyncListOperation.Added, items[i], i);
                    InvokeChange(changeI);
                }
            }
        }

        /// <summary>
        /// adds an item to the list and syncs the change
        /// </summary>
        /// <param name="item">The item you want to add</param>
        public void Add(T item)
        {
            ValidateAuthority();

            _list.Add(item);
            var change = new SyncListChange<T>(SyncListOperation.Added, item, _list.Count - 1);
            QueueChange(change);
            InvokeChange(change);
        }

        /// <summary>
        /// Clears the list and syncs the change
        /// </summary>
        public void Clear()
        {
            ValidateAuthority();

            _list.Clear();
            var change = new SyncListChange<T>(SyncListOperation.Cleared);
            QueueChange(change);
            InvokeChange(change);
        }

        /// <summary>
        /// Inserts an item at a specific index and syncs the change
        /// </summary>
        /// <param name="index">Index to insert the item to</param>
        /// <param name="item">Item to be inserted at the given index</param>
        public void Insert(int index, T item)
        {
            ValidateAuthority();

            _list.Insert(index, item);
            var change = new SyncListChange<T>(SyncListOperation.Insert, item, index);
            QueueChange(change);
            InvokeChange(change);
        }

        /// <summary>
        /// Removes an item from the list and syncs the change
        /// </summary>
        /// <param name="item">Item to be removed</param>
        /// <returns></returns>
        public bool Remove(T item)
        {
            ValidateAuthority();

            int idx = _list.IndexOf(item);
            if (idx < 0) return false;

            _list.RemoveAt(idx);
            var change = new SyncListChange<T>(SyncListOperation.Removed, item, idx);
            QueueChange(change);
            InvokeChange(change);

            return true;
        }

        /// <summary>
        /// Removes an item at a specific index and syncs the change
        /// </summary>
        /// <param name="index">Index of which to remove the entry</param>
        public void RemoveAt(int index)
        {
            ValidateAuthority();

            T item = _list[index];
            _list.RemoveAt(index);
            var change = new SyncListChange<T>(SyncListOperation.Removed, item, index);
            QueueChange(change);
            InvokeChange(change);
        }

        public bool Contains(T item) => _list.Contains(item);
        public void CopyTo(T[] array, int arrayIndex) => _list.CopyTo(array, arrayIndex);
        public IEnumerator<T> GetEnumerator() => _list.GetEnumerator();
        public int IndexOf(T item) => _list.IndexOf(item);
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        private void ValidateAuthority()
        {
            if (!isSpawned) return;

            bool controller = parent.IsController(_ownerAuth);
            if (!controller)
            {
                PurrLogger.LogError(
                    $"Invalid permissions when modifying '<b>SyncList<{typeof(T).Name}> {name}</b>' on '{parent.name}'." +
                    $"\nMaybe try enabling owner authority.", parent);
                throw new InvalidOperationException("Invalid permissions");
            }
        }

        private void InvokeChange(SyncListChange<T> change)
        {
            onChanged?.Invoke(change);
        }

        /// <summary>
        /// Forces the list to be synced again at the given index. Good for when you modify something inside the list
        /// </summary>
        /// <param name="index">Index to set dirty</param>
        public void SetDirty(int index)
        {
            if (!isSpawned) return;

            ValidateAuthority();

            if (index < 0 || index >= _list.Count)
            {
                PurrLogger.LogError($"Invalid index {index} for SetDirty in SyncList. List count: {_list.Count}",
                    parent);
                return;
            }

            var value = _list[index];
            var change = new SyncListChange<T>(SyncListOperation.Set, value, index);
            QueueChange(change);
            InvokeChange(change);
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
                    switch (change.operation)
                    {
                        case SyncListOperation.Added:
                            if (isServer) SendAddToAll(change.value);
                            else SendAddToServer(change.value);
                            break;
                        case SyncListOperation.Removed:
                            if (isServer) SendRemoveToAll(change.value);
                            else SendRemoveToServer(change.value);
                            break;
                        case SyncListOperation.Insert:
                            if (isServer) SendInsertToAll(change.index, change.value);
                            else SendInsertToServer(change.index, change.value);
                            break;
                        case SyncListOperation.Set:
                            if (isServer) SendSetToAll(change.index, change.value);
                            else SendSetToServer(change.index, change.value);
                            break;
                        case SyncListOperation.Cleared:
                            if (isServer) SendClearToAll();
                            else SendClearToServer();
                            break;
                    }
                }

                _pendingChanges.Clear();
                _lastSendTime = Time.time;
                _wasLastDirty = true;
                _isDirty = false;
            }
            else if (_wasLastDirty)
            {
                if(isServer)
                    SendInitialStateToAll(_list);
                else
                    ForceSendReliable();
                _wasLastDirty = false;
            }
        }

        #region RPCs

        [ServerRpc(Channel.ReliableOrdered, requireOwnership: true)]
        private void SendAddToServer(T item)
        {
            if (!_ownerAuth) return;
            SendAddToOthers(item);
        }

        [ObserversRpc(Channel.ReliableOrdered, excludeOwner: true)]
        private void SendAddToOthers(T item)
        {
            if (!isServer || isHost)
            {
                _list.Add(item);
                var change = new SyncListChange<T>(SyncListOperation.Added, item, _list.Count - 1);
                InvokeChange(change);
            }
        }

        [ObserversRpc(Channel.ReliableOrdered)]
        private void SendAddToAll(T item)
        {
            if (!isHost)
            {
                _list.Add(item);
                var change = new SyncListChange<T>(SyncListOperation.Added, item, _list.Count - 1);
                InvokeChange(change);
            }
        }

        [ServerRpc(Channel.ReliableOrdered, requireOwnership: true)]
        private void SendRemoveToServer(T item)
        {
            if (!_ownerAuth) return;
            SendRemoveToOthers(item);
        }

        [ObserversRpc(Channel.ReliableOrdered, excludeOwner: true)]
        private void SendRemoveToOthers(T item)
        {
            if (!isServer || isHost)
            {
                int idx = _list.IndexOf(item);
                if (idx >= 0)
                {
                    _list.RemoveAt(idx);
                    var change = new SyncListChange<T>(SyncListOperation.Removed, item, idx);
                    InvokeChange(change);
                }
            }
        }

        [ObserversRpc(Channel.ReliableOrdered)]
        private void SendRemoveToAll(T item)
        {
            if (!isHost)
            {
                int idx = _list.IndexOf(item);
                if (idx >= 0)
                {
                    _list.RemoveAt(idx);
                    var change = new SyncListChange<T>(SyncListOperation.Removed, item, idx);
                    InvokeChange(change);
                }
            }
        }

        [ServerRpc(Channel.ReliableOrdered, requireOwnership: true)]
        private void SendRemoveAtToServer(int index)
        {
            if (!_ownerAuth) return;
            SendRemoveAtToOthers(index);
        }

        [ObserversRpc(Channel.ReliableOrdered, excludeOwner: true)]
        private void SendRemoveAtToOthers(int index)
        {
            if ((!isServer || isHost) && index < _list.Count)
            {
                T item = _list[index];
                _list.RemoveAt(index);
                var change = new SyncListChange<T>(SyncListOperation.Removed, item, index);
                InvokeChange(change);
            }
        }

        [ObserversRpc(Channel.ReliableOrdered)]
        private void SendRemoveAtToAll(int index)
        {
            if (!isHost && index < _list.Count)
            {
                T item = _list[index];
                _list.RemoveAt(index);
                var change = new SyncListChange<T>(SyncListOperation.Removed, item, index);
                InvokeChange(change);
            }
        }

        [ServerRpc(Channel.ReliableOrdered, requireOwnership: true)]
        private void SendClearToServer()
        {
            if (!_ownerAuth) return;
            SendClearToOthers();
        }

        [ObserversRpc(Channel.ReliableOrdered, excludeOwner: true)]
        private void SendClearToOthers()
        {
            if (!isServer || isHost)
            {
                _list.Clear();
                var change = new SyncListChange<T>(SyncListOperation.Cleared);
                InvokeChange(change);
            }
        }

        [ObserversRpc(Channel.ReliableOrdered)]
        private void SendClearToAll()
        {
            if (!isHost)
            {
                _list.Clear();
                var change = new SyncListChange<T>(SyncListOperation.Cleared);
                InvokeChange(change);
            }
        }

        [ServerRpc(Channel.ReliableOrdered, requireOwnership: true)]
        private void SendSetToServer(int index, T item)
        {
            if (!_ownerAuth) return;
            SendSetToOthers(index, item);
        }

        [ObserversRpc(Channel.ReliableOrdered, excludeOwner: true)]
        private void SendSetToOthers(int index, T item)
        {
            if ((!isServer || isHost) && index < _list.Count)
            {
                _list[index] = item;
                var change = new SyncListChange<T>(SyncListOperation.Set, item, index);
                InvokeChange(change);
            }
        }

        [ObserversRpc(Channel.ReliableOrdered)]
        private void SendSetToAll(int index, T item)
        {
            if (!isHost && index < _list.Count)
            {
                _list[index] = item;
                var change = new SyncListChange<T>(SyncListOperation.Set, item, index);
                InvokeChange(change);
            }
        }

        [ServerRpc(Channel.ReliableOrdered, requireOwnership: true)]
        private void SendInsertToServer(int index, T item)
        {
            if (!_ownerAuth) return;
            SendInsertToOthers(index, item);
        }

        [ObserversRpc(Channel.ReliableOrdered, excludeOwner: true)]
        private void SendInsertToOthers(int index, T item)
        {
            if ((!isServer || isHost) && index <= _list.Count)
            {
                _list.Insert(index, item);
                var change = new SyncListChange<T>(SyncListOperation.Insert, item, index);
                InvokeChange(change);
            }
        }

        [ObserversRpc(Channel.ReliableOrdered)]
        private void SendInsertToAll(int index, T item)
        {
            if (!isHost && index <= _list.Count)
            {
                _list.Insert(index, item);
                var change = new SyncListChange<T>(SyncListOperation.Insert, item, index);
                InvokeChange(change);
            }
        }

        [ServerRpc(Channel.ReliableOrdered, requireOwnership: true)]
        private void SendSetDirtyToServer(int index, T value)
        {
            if (!_ownerAuth) return;
            SendSetDirtyToOthers(index, value);
        }

        [ObserversRpc(Channel.ReliableOrdered, excludeOwner: true)]
        private void SendSetDirtyToOthers(int index, T value)
        {
            if (!isServer || isHost)
            {
                if (index >= 0 && index < _list.Count)
                {
                    _list[index] = value;
                    var change = new SyncListChange<T>(SyncListOperation.Set, value, index);
                    InvokeChange(change);
                }
            }
        }

        [ObserversRpc(Channel.ReliableOrdered)]
        private void SendSetDirtyToAll(int index, T value)
        {
            if (!isHost)
            {
                if (index >= 0 && index < _list.Count)
                {
                    _list[index] = value;
                    var change = new SyncListChange<T>(SyncListOperation.Set, value, index);
                    InvokeChange(change);
                }
            }
        }
        
        [ServerRpc(Channel.ReliableOrdered)]
        private void ForceSendReliable()
        {
            SendInitialStateToAll(_list);
        }

        #endregion
    }
}
using UnityEngine;
using PurrNet.Logging;
using System;
using System.Collections;
using System.Collections.Generic;
using PurrNet.Transports;

namespace PurrNet
{
    public enum SyncArrayOperation
    {
        Set,
        Cleared,
        Resized
    }

    public readonly struct SyncArrayChange<T>
    {
        public readonly SyncArrayOperation operation;
        public readonly T value;
        public readonly int index;

        public SyncArrayChange(SyncArrayOperation operation, T value = default, int index = -1)
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
    public class SyncArray<T> : NetworkModule, IList<T>, ISerializationCallbackReceiver, ITick
    {
        [SerializeField] private bool _ownerAuth;
        [SerializeField] private List<T> _serializedItems = new List<T>();
        [SerializeField] private int _length;
        [SerializeField, Min(0)] private float _sendIntervalInSeconds;
        
        private T[] _array;

        public delegate void SyncArrayChanged<TYPE>(SyncArrayChange<TYPE> change);

        public event SyncArrayChanged<T> onChanged;

        public bool ownerAuth => _ownerAuth;
        
        public float sendIntervalInSeconds
        {
            get => _sendIntervalInSeconds;
            set => _sendIntervalInSeconds = value;
        }
        
        public int Length
        {
            get => _length;
            set
            {
                ValidateAuthority();
                
                if (_length == value)
                    return;
                
                Array.Resize(ref _array, value);
                _length = value;
                
                var change = new SyncArrayChange<T>(SyncArrayOperation.Resized);
                InvokeChange(change);
                
                if (isSpawned)
                {
                    if (isServer)
                        SendResizeToAll(value);
                    else
                        SendResizeToServer(value);
                }
            }
        }

        public int Count => _length;
        public bool IsReadOnly => false;
        private List<SyncArrayChange<T>> _pendingChanges = new();
        private float _lastSendTime;
        private bool _isDirty;
        private bool _wasLastDirty;

        public SyncArray(int length = 0, bool ownerAuth = false)
        {
            _ownerAuth = ownerAuth;
            _length = length;
            _array = new T[length];
            _serializedItems = new List<T>(length);
            for (int i = 0; i < length; i++)
                _serializedItems.Add(default);
        }
        
        public void OnBeforeSerialize()
        {
            _serializedItems.Clear();
            for (int i = 0; i < _length && i < _array.Length; i++)
            {
                _serializedItems.Add(_array[i]);
            }
        }
        
        public void OnAfterDeserialize()
        {
            _array = new T[_length];
            
            for (int i = 0; i < _serializedItems.Count && i < _length; i++)
                _array[i] = _serializedItems[i];
        }

        public T this[int index]
        {
            get
            {
                if (index < 0 || index >= _length)
                    throw new IndexOutOfRangeException();
                    
                return _array[index];
            }
            set
            {
                ValidateAuthority();
                
                if (index < 0 || index >= _length)
                    throw new IndexOutOfRangeException();
                
                bool bothNull = value == null && _array[index] == null;
                bool bothEqual = value != null && value.Equals(_array[index]);
                
                if (bothNull || bothEqual)
                    return;
                
                _array[index] = value;
                
                var change = new SyncArrayChange<T>(SyncArrayOperation.Set, value, index);
                InvokeChange(change);
                
                if (isSpawned)
                {
                    if (isServer)
                        SendSetToAll(index, value);
                    else
                        SendSetToServer(index, value);
                }
            }
        }

        public override void OnInitializeModules()
        {
            base.OnInitializeModules();
            if (!IsController(_ownerAuth)) return;
            
            if (isServer)
                SendInitialSizeToAll(_length);
            else 
                SendInitialSizeToServer(_length);
                
            for (int i = 0; i < _length; i++)
            {
                if (isServer)
                    SendSetToAll(i, _array[i]);
                else
                    SendSetToServer(i, _array[i]);
            }
        }

        public override void OnObserverAdded(PlayerID player)
        {
            SendInitialSizeToTarget(player, _length);
            
            for (int i = 0; i < _length; i++)
            {
                SendSetToTarget(player, i, _array[i]);
            }
        }
        
        private void QueueChange(SyncArrayChange<T> change)
        {
            _pendingChanges.Add(change);
            _isDirty = true;
        }

        [TargetRpc(Channel.ReliableOrdered)]
        private void SendInitialSizeToTarget(PlayerID player, int length)
        {
            HandleInitialSize(length);
        }
        
        [TargetRpc(Channel.ReliableOrdered)]
        private void SendSetToTarget(PlayerID player, int index, T value)
        {
            if (index >= 0 && index < _length)
            {
                _array[index] = value;
                var change = new SyncArrayChange<T>(SyncArrayOperation.Set, value, index);
                //QueueChange(change);
                InvokeChange(change);

            }
        }

        [ObserversRpc(Channel.ReliableOrdered)]
        private void SendInitialSizeToAll(int length)
        {
            HandleInitialSize(length);
        }

        private void HandleInitialSize(int length)
        {
            if (!isHost)
            {
                if (_length != length)
                {
                    Array.Resize(ref _array, length);
                    _length = length;

                    var resizeChange = new SyncArrayChange<T>(SyncArrayOperation.Resized);
                    var clearChange = new SyncArrayChange<T>(SyncArrayOperation.Cleared);
                    QueueChange(resizeChange);
                    InvokeChange(resizeChange);
                    QueueChange(clearChange);
                    InvokeChange(clearChange);
                }
            }
        }

        [ServerRpc(Channel.ReliableOrdered, requireOwnership: true)]
        private void SendInitialSizeToServer(int length)
        {
            if (!_ownerAuth) return;
            SendInitialSizeToOthers(length);
        }

        [ObserversRpc(Channel.ReliableOrdered, excludeOwner: true)]
        private void SendInitialSizeToOthers(int length)
        {
            if (!isServer || isHost)
            {
                if (_length != length)
                {
                    Array.Resize(ref _array, length);
                    _length = length;
                    
                    var resizeChange = new SyncArrayChange<T>(SyncArrayOperation.Resized);
                    var clearChange = new SyncArrayChange<T>(SyncArrayOperation.Cleared);
                    QueueChange(resizeChange);
                    InvokeChange(resizeChange);
                    //QueueChange(clearChange);
                    InvokeChange(clearChange);
                }
            }
        }

        public void Clear()
        {
            ValidateAuthority();
            
            Array.Clear(_array, 0, _length);
            
            var change = new SyncArrayChange<T>(SyncArrayOperation.Cleared);
            QueueChange(change);
            InvokeChange(change);
            
            if (isSpawned)
            {
                if (isServer)
                    SendClearToAll();
                else
                    SendClearToServer();
            }
        }

        public int IndexOf(T item)
        {
            for (int i = 0; i < _length; i++)
            {
                if (EqualityComparer<T>.Default.Equals(_array[i], item))
                    return i;
            }
            return -1;
        }

        public void Insert(int index, T item)
        {
            throw new NotSupportedException("SyncArray does not support insertion. Use Resize and Set operations instead.");
        }

        public void RemoveAt(int index)
        {
            throw new NotSupportedException("SyncArray does not support removal. Use Resize and Set operations instead.");
        }

        public void Add(T item)
        {
            throw new NotSupportedException("SyncArray does not support Add. Use Resize and Set operations instead.");
        }

        public bool Remove(T item)
        {
            throw new NotSupportedException("SyncArray does not support Remove. Use Resize and Set operations instead.");
        }

        public bool Contains(T item)
        {
            return IndexOf(item) != -1;
        }

        public void CopyTo(T[] array, int arrayIndex)
        {
            if (array == null)
                throw new ArgumentNullException(nameof(array));
                
            if (arrayIndex < 0)
                throw new ArgumentOutOfRangeException(nameof(arrayIndex));
                
            if (array.Length - arrayIndex < _length)
                throw new ArgumentException("Destination array is not long enough");
                
            Array.Copy(_array, 0, array, arrayIndex, _length);
        }

        public T[] ToArray()
        {
            T[] result = new T[_length];
            Array.Copy(_array, result, _length);
            return result;
        }

        public IEnumerator<T> GetEnumerator()
        {
            for (int i = 0; i < _length; i++)
            {
                yield return _array[i];
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public void SetDirty(int index)
        {
            if (!isSpawned) return;
            
            ValidateAuthority();
            
            if (index < 0 || index >= _length)
            {
                PurrLogger.LogError($"Invalid index {index} for SetDirty in SyncArray. Array length: {_length}",
                    parent);
                return;
            }
            
            var value = _array[index];
            var change = new SyncArrayChange<T>(SyncArrayOperation.Set, value, index);
            QueueChange(change);
            InvokeChange(change);
            
            if (isServer)
                SendSetDirtyToAll(index, value);
            else
                SendSetDirtyToServer(index, value);
        }

        private void ValidateAuthority()
        {
            if (!isSpawned) return;
            
            bool isController = parent.IsController(_ownerAuth);
            if (!isController)
            {
                PurrLogger.LogError(
                    $"Invalid permissions when modifying '<b>SyncArray<{typeof(T).Name}> {name}</b>' on '{parent.name}'." +
                    $"\nMaybe try enabling owner authority.", parent);
                throw new InvalidOperationException("Invalid permissions");
            }
        }

        private void InvokeChange(SyncArrayChange<T> change)
        {
            onChanged?.Invoke(change);
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
                        case SyncArrayOperation.Set:
                            if (isServer) SendSetToAll(change.index, change.value);
                            else SendSetToServer(change.index, change.value);
                            break;
                        case SyncArrayOperation.Resized:
                            if (isServer) SendResizeToAll(_length);
                            else SendResizeToServer(_length);
                            break;
                        case SyncArrayOperation.Cleared:
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
                    ForceSendReliable_Internal();
                else
                    ForceSendReliable();
                _wasLastDirty = false;
            }
        }

        #region RPCs

        [ServerRpc(Channel.ReliableOrdered, requireOwnership: true)]
        private void SendSetToServer(int index, T value)
        {
            if (!_ownerAuth) return;
            SendSetToOthers(index, value);
        }

        [ObserversRpc(Channel.ReliableOrdered, excludeOwner: true)]
        private void SendSetToOthers(int index, T value)
        {
            if (!isServer || isHost)
            {
                if (index >= 0 && index < _length)
                {
                    _array[index] = value;
                    var change = new SyncArrayChange<T>(SyncArrayOperation.Set, value, index);
                    QueueChange(change);
                    InvokeChange(change);
                }
            }
        }

        [ObserversRpc(Channel.ReliableOrdered)]
        private void SendSetToAll(int index, T value)
        {
            if (!isHost)
            {
                if (index >= 0 && index < _length)
                {
                    _array[index] = value;
                    var change = new SyncArrayChange<T>(SyncArrayOperation.Set, value, index);
                    QueueChange(change);
                    InvokeChange(change);
                }
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
                Array.Clear(_array, 0, _length);
                var change = new SyncArrayChange<T>(SyncArrayOperation.Cleared);
                QueueChange(change);
                InvokeChange(change);
            }
        }

        [ObserversRpc(Channel.ReliableOrdered)]
        private void SendClearToAll()
        {
            if (!isHost)
            {
                Array.Clear(_array, 0, _length);
                var change = new SyncArrayChange<T>(SyncArrayOperation.Cleared);
                QueueChange(change);
                InvokeChange(change);
            }
        }

        [ServerRpc(Channel.ReliableOrdered, requireOwnership: true)]
        private void SendResizeToServer(int newLength)
        {
            if (!_ownerAuth) return;
            SendResizeToOthers(newLength);
        }

        [ObserversRpc(Channel.ReliableOrdered, excludeOwner: true)]
        private void SendResizeToOthers(int newLength)
        {
            if (!isServer || isHost)
            {
                if (_length != newLength)
                {
                    Array.Resize(ref _array, newLength);
                    _length = newLength;
                    var change = new SyncArrayChange<T>(SyncArrayOperation.Resized);
                    QueueChange(change);
                    InvokeChange(change);
                }
            }
        }

        [ObserversRpc(Channel.ReliableOrdered)]
        private void SendResizeToAll(int newLength)
        {
            if (!isHost)
            {
                if (_length != newLength)
                {
                    Array.Resize(ref _array, newLength);
                    _length = newLength;
                    var change = new SyncArrayChange<T>(SyncArrayOperation.Resized);
                    QueueChange(change);
                    InvokeChange(change);
                }
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
                if (index >= 0 && index < _length)
                {
                    _array[index] = value;
                    var change = new SyncArrayChange<T>(SyncArrayOperation.Set, value, index);
                    QueueChange(change);
                    InvokeChange(change);
                }
            }
        }

        [ObserversRpc(Channel.ReliableOrdered)]
        private void SendSetDirtyToAll(int index, T value)
        {
            if (!isHost)
            {
                if (index >= 0 && index < _length)
                {
                    _array[index] = value;
                    var change = new SyncArrayChange<T>(SyncArrayOperation.Set, value, index);
                    QueueChange(change);
                    InvokeChange(change);
                }
            }
        }
        
        [ServerRpc(Channel.ReliableOrdered)]
        private void ForceSendReliable()
        {
            ForceSendReliable_Internal();
        }

        private void ForceSendReliable_Internal()
        {
            SendInitialSizeToAll(_length);
            for (int i = 0; i < _length; i++)
                SendSetToAll(i, _array[i]);
        }

        #endregion
    }
}
using UnityEngine;
using PurrNet.Logging;
using System;
using System.Collections;
using System.Collections.Generic;

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
    public class SyncList<T> : ASyncType<List<T>, SyncListChange<T>>, IList<T>
    {
        [SerializeField] private List<T> _list = new List<T>();

        public List<T> list => _list;
        public List<T> ToList() => _list;

        public delegate void SyncListChanged<TYPE>(SyncListChange<TYPE> change);

        /// <summary>
        /// Event that is invoked when the list is changed
        /// </summary>
        public event SyncListChanged<T> onChanged;

        /// <summary>
        /// The amount of entries in the list
        /// </summary>
        public int Count => _list.Count;

        public bool IsReadOnly => false;

        public SyncList(bool ownerAuth = false)
        {
            _ownerAuth = ownerAuth;
        }

        public SyncList(List<T> defaultValues, bool ownerAuth = false)
        {
            _list = defaultValues;
            _ownerAuth = ownerAuth;
        }

        protected override List<T> GetFullState() => new List<T>(_list);

        protected override void ApplyFullState(List<T> state)
        {
            _list.Clear();
            _list.AddRange(state);

            InvokeChangeEvent(new SyncListChange<T>(SyncListOperation.Cleared));

            for (int i = 0; i < state.Count; i++)
            {
                InvokeChangeEvent(new SyncListChange<T>(SyncListOperation.Added, state[i], i));
            }
        }

        protected override void ApplyChange(SyncListChange<T> change)
        {
            switch (change.operation)
            {
                case SyncListOperation.Added:
                    _list.Add(change.value);
                    break;
                case SyncListOperation.Removed:
                    int idx = _list.IndexOf(change.value);
                    if (idx >= 0) _list.RemoveAt(idx);
                    break;
                case SyncListOperation.Insert:
                    if (change.index <= _list.Count)
                        _list.Insert(change.index, change.value);
                    break;
                case SyncListOperation.Set:
                    if (change.index < _list.Count)
                        _list[change.index] = change.value;
                    break;
                case SyncListOperation.Cleared:
                    _list.Clear();
                    break;
            }
        }

        protected override void InvokeChangeEvent(SyncListChange<T> change)
        {
            onChanged?.Invoke(change);
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
                InvokeChangeEvent(change);
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
            InvokeChangeEvent(change);
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
            InvokeChangeEvent(change);
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
            InvokeChangeEvent(change);
        }

        /// <summary>
        /// Removes an item from the list and syncs the change
        /// </summary>
        /// <param name="item">Item to be removed</param>
        /// <returns>True if the item was removed, false otherwise</returns>
        public bool Remove(T item)
        {
            ValidateAuthority();

            int idx = _list.IndexOf(item);
            if (idx < 0) return false;

            _list.RemoveAt(idx);
            var change = new SyncListChange<T>(SyncListOperation.Removed, item, idx);
            QueueChange(change);
            InvokeChangeEvent(change);

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
            InvokeChangeEvent(change);
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
            InvokeChangeEvent(change);
        }

        public bool Contains(T item) => _list.Contains(item);
        public void CopyTo(T[] array, int arrayIndex) => _list.CopyTo(array, arrayIndex);
        public IEnumerator<T> GetEnumerator() => _list.GetEnumerator();
        public int IndexOf(T item) => _list.IndexOf(item);
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
﻿using ObservableCollections.Internal;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace ObservableCollections
{
    public sealed partial class ObservableDictionary<TKey, TValue>
        : IDictionary<TKey, TValue>, IReadOnlyDictionary<TKey, TValue>, IObservableCollection<KeyValuePair<TKey, TValue>>
        where TKey : notnull
    {
        readonly Dictionary<TKey, TValue> dictionary;
        public readonly object SyncRoot = new object();

        public ObservableDictionary()
        {
            this.dictionary = new Dictionary<TKey, TValue>();
        }

        public ObservableDictionary(IEnumerable<KeyValuePair<TKey, TValue>> collection)
        {
            this.dictionary = new Dictionary<TKey, TValue>(collection);
        }

        public event NotifyCollectionChangedEventHandler<KeyValuePair<TKey, TValue>>? CollectionChanged;

        public TValue this[TKey key]
        {
            get
            {
                lock (SyncRoot)
                {
                    return dictionary[key];
                }
            }
            set
            {
                lock (SyncRoot)
                {
                    if (dictionary.TryGetValue(key, out var oldValue))
                    {
                        dictionary[key] = value;
                        CollectionChanged?.Invoke(NotifyCollectionChangedEventArgs<KeyValuePair<TKey, TValue>>.Replace(
                            new KeyValuePair<TKey, TValue>(key, value),
                            new KeyValuePair<TKey, TValue>(key, oldValue),
                            -1));
                    }
                    else
                    {
                        Add(key, value);
                    }
                }
            }
        }

        // for lock synchronization, hide keys and values.
        ICollection<TKey> IDictionary<TKey, TValue>.Keys
        {
            get
            {
                lock (SyncRoot)
                {
                    return dictionary.Keys;
                }
            }
        }

        ICollection<TValue> IDictionary<TKey, TValue>.Values
        {
            get
            {
                lock (SyncRoot)
                {
                    return dictionary.Values;
                }
            }
        }

        public int Count
        {
            get
            {
                lock (SyncRoot)
                {
                    return dictionary.Count;
                }
            }
        }

        public bool IsReadOnly => false;

        IEnumerable<TKey> IReadOnlyDictionary<TKey, TValue>.Keys
        {
            get
            {
                lock (SyncRoot)
                {
                    return dictionary.Keys;
                }
            }
        }

        IEnumerable<TValue> IReadOnlyDictionary<TKey, TValue>.Values
        {
            get
            {
                lock (SyncRoot)
                {
                    return dictionary.Values;
                }
            }
        }

        public void Add(TKey key, TValue value)
        {
            lock (SyncRoot)
            {
                dictionary.Add(key, value);
                CollectionChanged?.Invoke(NotifyCollectionChangedEventArgs<KeyValuePair<TKey, TValue>>.Add(new KeyValuePair<TKey, TValue>(key, value), -1));
            }
        }

        public void Add(KeyValuePair<TKey, TValue> item)
        {
            Add(item.Key, item.Value);
        }

        public void Clear()
        {
            lock (SyncRoot)
            {
                dictionary.Clear();
                CollectionChanged?.Invoke(NotifyCollectionChangedEventArgs<KeyValuePair<TKey, TValue>>.Reset());
            }
        }

        public bool Contains(KeyValuePair<TKey, TValue> item)
        {
            lock (SyncRoot)
            {
                return ((ICollection<KeyValuePair<TKey, TValue>>)dictionary).Contains(item);
            }
        }

        public bool ContainsKey(TKey key)
        {
            lock (SyncRoot)
            {
                return ((IDictionary<TKey, TValue>)dictionary).ContainsKey(key);
            }
        }

        public void CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex)
        {
            lock (SyncRoot)
            {
                ((ICollection<KeyValuePair<TKey, TValue>>)dictionary).CopyTo(array, arrayIndex);
            }
        }

        public bool Remove(TKey key)
        {
            lock (SyncRoot)
            {
                if (dictionary.Remove(key, out var value))
                {
                    CollectionChanged?.Invoke(NotifyCollectionChangedEventArgs<KeyValuePair<TKey, TValue>>.Remove(new KeyValuePair<TKey, TValue>(key, value), -1));
                    return true;
                }
                return false;
            }
        }

        public bool Remove(KeyValuePair<TKey, TValue> item)
        {
            lock (SyncRoot)
            {
                if (dictionary.TryGetValue(item.Key, out var value))
                {
                    if (EqualityComparer<TValue>.Default.Equals(value, item.Value))
                    {
                        if (dictionary.Remove(item.Key, out var value2))
                        {
                            CollectionChanged?.Invoke(NotifyCollectionChangedEventArgs<KeyValuePair<TKey, TValue>>.Remove(new KeyValuePair<TKey, TValue>(item.Key, value2), -1));
                            return true;
                        }
                    }
                }
                return false;
            }
        }

        public bool TryGetValue(TKey key, [MaybeNullWhen(false)] out TValue value)
        {
            lock (SyncRoot)
            {
                return dictionary.TryGetValue(key, out value);
            }
        }

        public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator()
        {
            return new SynchronizedEnumerator<KeyValuePair<TKey, TValue>>(SyncRoot, dictionary.GetEnumerator());
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}

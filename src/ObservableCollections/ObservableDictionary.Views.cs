﻿using ObservableCollections.Internal;
using System.Collections;
using System.Collections.Specialized;

namespace ObservableCollections
{
    public sealed partial class ObservableDictionary<TKey, TValue>
    {
        public ISynchronizedView<KeyValuePair<TKey, TValue>, TView> CreateView<TView>(Func<KeyValuePair<TKey, TValue>, TView> transform, bool _ = false)
        {
            // reverse is no used.
            return new View<TView>(this, transform);
        }

        public ISynchronizedView<KeyValuePair<TKey, TValue>, TView> CreateSortedView<TView>(Func<KeyValuePair<TKey, TValue>, TView> transform, IComparer<KeyValuePair<TKey, TValue>> comparer)
        {
            return new SortedView<KeyValuePair<TKey, TValue>, TKey, TView>(this, this.SyncRoot, dictionary, x => x.Key, transform, comparer);
        }

        public ISynchronizedView<KeyValuePair<TKey, TValue>, TView> CreateSortedView<TView>(Func<KeyValuePair<TKey, TValue>, TView> transform, IComparer<TView> viewComparer)
        {
            return new SortedViewViewComparer<KeyValuePair<TKey, TValue>, TKey, TView>(this, this.SyncRoot, dictionary, x => x.Key, transform, viewComparer);
        }

        // identity selector is ignored
        ISynchronizedView<KeyValuePair<TKey, TValue>, TView> IObservableCollection<KeyValuePair<TKey, TValue>>.CreateSortedView<TKey1, TView>(Func<KeyValuePair<TKey, TValue>, TKey1> identitySelector, Func<KeyValuePair<TKey, TValue>, TView> transform, IComparer<KeyValuePair<TKey, TValue>> comparer)
        {
            return new SortedView<KeyValuePair<TKey, TValue>, TKey, TView>(this, this.SyncRoot, dictionary, x => x.Key, transform, comparer);
        }

        ISynchronizedView<KeyValuePair<TKey, TValue>, TView> IObservableCollection<KeyValuePair<TKey, TValue>>.CreateSortedView<TKey1, TView>(Func<KeyValuePair<TKey, TValue>, TKey1> identitySelector, Func<KeyValuePair<TKey, TValue>, TView> transform, IComparer<TView> viewComparer)
        {
            return new SortedViewViewComparer<KeyValuePair<TKey, TValue>, TKey, TView>(this, this.SyncRoot, dictionary, x => x.Key, transform, viewComparer);
        }

        class View<TView> : ISynchronizedView<KeyValuePair<TKey, TValue>, TView>
        {
            readonly ObservableDictionary<TKey, TValue> source;
            readonly Func<KeyValuePair<TKey, TValue>, TView> selector;
            ISynchronizedViewFilter<KeyValuePair<TKey, TValue>, TView> filter;
            readonly Dictionary<TKey, (TValue, TView)> dict;

            public View(ObservableDictionary<TKey, TValue> source, Func<KeyValuePair<TKey, TValue>, TView> selector)
            {
                this.source = source;
                this.selector = selector;
                this.filter = SynchronizedViewFilter<KeyValuePair<TKey, TValue>, TView>.Null;
                this.SyncRoot = new object();
                lock (source.SyncRoot)
                {
                    this.dict = source.dictionary.ToDictionary(x => x.Key, x => (x.Value, selector(x)));
                    this.source.CollectionChanged += SourceCollectionChanged;
                }
            }

            public object SyncRoot { get; }
            public event NotifyCollectionChangedEventHandler<KeyValuePair<TKey, TValue>>? RoutingCollectionChanged;
            public event Action<NotifyCollectionChangedAction>? CollectionStateChanged;

            public int Count
            {
                get
                {
                    lock (SyncRoot)
                    {
                        return dict.Count;
                    }
                }
            }

            public void Dispose()
            {
                this.source.CollectionChanged -= SourceCollectionChanged;
            }

            public void AttachFilter(ISynchronizedViewFilter<KeyValuePair<TKey, TValue>, TView> filter)
            {
                lock (SyncRoot)
                {
                    this.filter = filter;
                    foreach (var v in dict)
                    {
                        filter.InvokeOnAttach(new KeyValuePair<TKey, TValue>(v.Key, v.Value.Item1), v.Value.Item2);
                    }
                }
            }

            public void ResetFilter(Action<KeyValuePair<TKey, TValue>, TView>? resetAction)
            {
                lock (SyncRoot)
                {
                    this.filter = SynchronizedViewFilter<KeyValuePair<TKey, TValue>, TView>.Null;
                    if (resetAction != null)
                    {
                        foreach (var v in dict)
                        {
                            resetAction(new KeyValuePair<TKey, TValue>(v.Key, v.Value.Item1), v.Value.Item2);
                        }
                    }
                }
            }

            public INotifyCollectionChangedSynchronizedView<KeyValuePair<TKey, TValue>, TView> WithINotifyCollectionChanged()
            {
                lock (SyncRoot)
                {
                    return new NotifyCollectionChangedSynchronizedView<KeyValuePair<TKey, TValue>, TView>(this);
                }
            }

            public IEnumerator<(KeyValuePair<TKey, TValue>, TView)> GetEnumerator()
            {
                return new SynchronizedViewEnumerator<KeyValuePair<TKey, TValue>, TView>(SyncRoot,
                    dict.Select(x => (new KeyValuePair<TKey, TValue>(x.Key, x.Value.Item1), x.Value.Item2)).GetEnumerator(),
                    filter);
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return GetEnumerator();
            }

            private void SourceCollectionChanged(in NotifyCollectionChangedEventArgs<KeyValuePair<TKey, TValue>> e)
            {
                // ObservableDictionary only provides single item operation and does not use int index.
                lock (SyncRoot)
                {
                    switch (e.Action)
                    {
                        case NotifyCollectionChangedAction.Add:
                            {
                                var v = selector(e.NewItem);
                                dict.Add(e.NewItem.Key, (e.NewItem.Value, v));
                                filter.InvokeOnAdd(new KeyValuePair<TKey, TValue>(e.NewItem.Key, e.NewItem.Value), v);
                            }
                            break;
                        case NotifyCollectionChangedAction.Remove:
                            {
                                if (dict.Remove(e.OldItem.Key, out var v))
                                {
                                    filter.InvokeOnRemove((new KeyValuePair<TKey, TValue>(e.OldItem.Key, v.Item1), v.Item2));
                                }
                            }
                            break;
                        case NotifyCollectionChangedAction.Replace:
                            {
                                if (dict.Remove(e.OldItem.Key, out var oldView))
                                {
                                    filter.InvokeOnRemove((new KeyValuePair<TKey, TValue>(e.OldItem.Key, oldView.Item1), oldView.Item2));
                                }

                                var v = selector(e.NewItem);
                                dict[e.NewItem.Key] = (e.NewItem.Value, v);
                                filter.InvokeOnAdd(new KeyValuePair<TKey, TValue>(e.NewItem.Key, e.NewItem.Value), v);
                            }
                            break;
                        case NotifyCollectionChangedAction.Reset:
                            {
                                if (!filter.IsNullFilter())
                                {
                                    foreach (var item in dict)
                                    {
                                        filter.InvokeOnRemove((new KeyValuePair<TKey, TValue>(item.Key, item.Value.Item1), item.Value.Item2));
                                    }
                                }

                                dict.Clear();
                            }
                            break;
                        case NotifyCollectionChangedAction.Move: // ObservableDictionary have no Move operation.
                        default:
                            break;
                    }

                    RoutingCollectionChanged?.Invoke(e);
                    CollectionStateChanged?.Invoke(e.Action);
                }
            }
        }
    }
}

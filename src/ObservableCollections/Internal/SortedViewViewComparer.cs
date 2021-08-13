﻿using System.Collections;
using System.Collections.Specialized;

namespace ObservableCollections.Internal
{
    internal class SortedViewViewComparer<T, TKey, TView> : ISynchronizedView<T, TView>
            where TKey : notnull
    {
        readonly IObservableCollection<T> source;
        readonly Func<T, TView> transform;
        readonly Func<T, TKey> identitySelector;
        readonly Dictionary<TKey, TView> viewMap; // view-map needs to use in remove.
        readonly SortedDictionary<(TView View, TKey Key), (T Value, TView View)> list;

        ISynchronizedViewFilter<T, TView> filter;

        public event NotifyCollectionChangedEventHandler<T>? RoutingCollectionChanged;
        public event Action<NotifyCollectionChangedAction>? CollectionStateChanged;

        public object SyncRoot { get; } = new object();

        public SortedViewViewComparer(IObservableCollection<T> source, object syncRoot, IEnumerable<T> sourceEnumerable, Func<T, TKey> identitySelector, Func<T, TView> transform, IComparer<TView> comparer)
        {
            this.source = source;
            this.identitySelector = identitySelector;
            this.transform = transform;
            this.filter = SynchronizedViewFilter<T, TView>.Null;
            lock (syncRoot)
            {
                var dict = new SortedDictionary<(TView, TKey), (T, TView)>(new Comparer(comparer));
                this.viewMap = new Dictionary<TKey, TView>();
                foreach (var value in sourceEnumerable)
                {
                    var view = transform(value);
                    var id = identitySelector(value);
                    dict.Add((view, id), (value, view));
                    viewMap.Add(id, view);
                }
                this.list = dict;
                this.source.CollectionChanged += SourceCollectionChanged;
            }
        }

        public int Count
        {
            get
            {
                lock (SyncRoot)
                {
                    return list.Count;
                }
            }
        }

        public void AttachFilter(ISynchronizedViewFilter<T, TView> filter)
        {
            lock (SyncRoot)
            {
                this.filter = filter;
                foreach (var (_, (value, view)) in list)
                {
                    filter.InvokeOnAttach(value, view);
                }
            }
        }

        public void ResetFilter(Action<T, TView>? resetAction)
        {
            lock (SyncRoot)
            {
                this.filter = SynchronizedViewFilter<T, TView>.Null;
                if (resetAction != null)
                {
                    foreach (var (_, (value, view)) in list)
                    {
                        resetAction(value, view);
                    }
                }
            }
        }

        public INotifyCollectionChangedSynchronizedView<T, TView> WithINotifyCollectionChanged()
        {
            lock (SyncRoot)
            {
                return new NotifyCollectionChangedSynchronizedView<T, TView>(this);
            }
        }

        public IEnumerator<(T, TView)> GetEnumerator()
        {
            return new SynchronizedViewEnumerator<T, TView>(SyncRoot, list.Select(x => x.Value).GetEnumerator(), filter);
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public void Dispose()
        {
            this.source.CollectionChanged -= SourceCollectionChanged;
        }

        private void SourceCollectionChanged(in NotifyCollectionChangedEventArgs<T> e)
        {
            lock (SyncRoot)
            {
                switch (e.Action)
                {
                    case NotifyCollectionChangedAction.Add:
                        {
                            // Add, Insert
                            if (e.IsSingleItem)
                            {
                                var value = e.NewItem;
                                var view = transform(value);
                                var id = identitySelector(value);
                                list.Add((view, id), (value, view));
                                viewMap.Add(id, view);
                                filter.InvokeOnAdd(value, view);
                            }
                            else
                            {
                                foreach (var value in e.NewItems)
                                {
                                    var view = transform(value);
                                    var id = identitySelector(value);
                                    list.Add((view, id), (value, view));
                                    viewMap.Add(id, view);
                                    filter.InvokeOnAdd(value, view);
                                }
                            }
                        }
                        break;
                    case NotifyCollectionChangedAction.Remove:
                        {
                            if (e.IsSingleItem)
                            {
                                var value = e.OldItem;
                                var id = identitySelector(value);
                                if (viewMap.Remove(id, out var view))
                                {
                                    list.Remove((view, id), out var v);
                                    filter.InvokeOnRemove(v);
                                }
                            }
                            else
                            {
                                foreach (var value in e.OldItems)
                                {
                                    var id = identitySelector(value);
                                    if (viewMap.Remove(id, out var view))
                                    {
                                        list.Remove((view, id), out var v);
                                        filter.InvokeOnRemove(v);
                                    }
                                }
                            }
                        }
                        break;
                    case NotifyCollectionChangedAction.Replace:
                        // Replace is remove old item and insert new item.
                        {
                            var oldValue = e.OldItem;
                            var oldKey = identitySelector(oldValue);
                            if (viewMap.Remove(oldKey, out var oldView))
                            {
                                list.Remove((oldView, oldKey));
                                filter.InvokeOnRemove(oldValue, oldView);
                            }

                            var value = e.NewItem;
                            var view = transform(value);
                            var id = identitySelector(value);
                            list.Add((view, id), (value, view));
                            viewMap.Add(id, view);

                            filter.InvokeOnAdd(value, view);
                        }
                        break;
                    case NotifyCollectionChangedAction.Move:
                        // Move(index change) does not affect soreted dict.
                        {
                            var value = e.OldItem;
                            var key = identitySelector(value);
                            if (viewMap.TryGetValue(key, out var view))
                            {
                                filter.InvokeOnMove(value, view);
                            }
                        }
                        break;
                    case NotifyCollectionChangedAction.Reset:
                        if (!filter.IsNullFilter())
                        {
                            foreach (var item in list)
                            {
                                filter.InvokeOnRemove(item.Value);
                            }
                        }
                        list.Clear();
                        viewMap.Clear();
                        break;
                    default:
                        break;
                }

                RoutingCollectionChanged?.Invoke(e);
                CollectionStateChanged?.Invoke(e.Action);
            }
        }

        sealed class Comparer : IComparer<(TView view, TKey id)>
        {
            readonly IComparer<TView> comparer;

            public Comparer(IComparer<TView> comparer)
            {
                this.comparer = comparer;
            }

            public int Compare((TView view, TKey id) x, (TView view, TKey id) y)
            {
                var compare = comparer.Compare(x.view, y.view);
                if (compare == 0)
                {
                    compare = Comparer<TKey>.Default.Compare(x.id, y.id);
                }

                return compare;
            }
        }
    }
}
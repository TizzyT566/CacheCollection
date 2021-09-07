using System.Threading;

namespace System.Collections.Generic
{
    public abstract class CacheCollection<T, U>
    {
        private CacheNode<T> _head, _tail;
        private int _lock, _count, _threshold;
        private readonly CacheNode<T>[] _cacheableItems;

        public int Count => _count;
        public int Threshold
        {
            get => _threshold;
            set
            {
                if (value < 1)
                    throw new ArgumentOutOfRangeException(nameof(value), "Threshold must be a value greater than 0");
                _threshold = value;
            }
        }

        public CacheCollection(int threshold, params U[] sources)
        {
            Threshold = threshold;
            _cacheableItems = new CacheNode<T>[sources.Length];
            for (int i = 0; i < sources.Length; i++)
                _cacheableItems[i] = new CacheNode<T>(sources[i], this);
        }

        public CacheNode<T> this[int index]
        {
            get
            {
                try
                {
                    while (Interlocked.CompareExchange(ref _lock, 1, 0) == 1) ;
                    CacheNode<T> cache = _cacheableItems[index].Validate();
                    if (cache != _head)
                    {
                        if (cache._prev != null)
                            cache._prev._next = cache._next;

                        if (cache._next != null)
                            cache._next._prev = cache._prev;
                        cache._next = _head;
                        _head = cache;
                        if (Count == 0)
                            _tail = _head;
                    }
                    return cache;
                }
                catch (Exception)
                {
                    throw;
                }
                finally
                {
                    Interlocked.Exchange(ref _lock, 0);
                }
            }
        }

        public void Trim()
        {
            if (Count > Threshold)
            {
                int count = Count - Threshold;
                for (int i = 0; i < count; i++)
                {
                    CacheNode<T> crnt = _tail;
                    if (crnt._prev != null)
                        crnt._prev._next = null;
                    _tail = crnt._prev;
                    crnt.Invalidate();
                }
            }
        }

        public abstract T Parse(U source);

        public class CacheNode<T>
        {
            private readonly U _source;
            private T _item;
            private int _lock, _marked;
            private CacheCollection<T, U> Parent;

            internal CacheNode<T> _prev, _next;

            internal CacheNode(U source, CacheCollection<T, U> parent)
            {
                _source = source;
                Parent = parent;
            }

            public CacheNode<T> Validate()
            {
                if (Interlocked.CompareExchange(ref _marked, 1, 0) == 1)
                    return this;

                ThreadPool.QueueUserWorkItem(_ =>
                {
                    try
                    {
                        while (Interlocked.CompareExchange(ref _lock, 1, 0) == 1) ;
                        if (_item == null)
                        {
                            _item = Parent.Parse(_source);
                            Interlocked.Increment(ref Parent._count);
                        }
                    }
                    catch (Exception)
                    {
                        throw;
                    }
                    finally
                    {
                        Interlocked.Exchange(ref _lock, 0);
                    }
                });
                return this;
            }

            internal void Invalidate()
            {
                (_item as IDisposable)?.Dispose();
                _item = default;
                Interlocked.Exchange(ref _marked, 0);
                Interlocked.Decrement(ref Parent._count);
            }

            public static implicit operator T(CacheNode<T> cacheRef)
            {
                try
                {
                    while (Interlocked.CompareExchange(ref cacheRef._lock, 1, 0) == 1) ;
                    if (cacheRef._item == null)
                    {
                        cacheRef._item = cacheRef.Parent.Parse(cacheRef._source);
                        Interlocked.Increment(ref cacheRef.Parent._count);
                    }
                    return cacheRef._item;
                }
                catch (Exception)
                {
                    throw;
                }
                finally
                {
                    Interlocked.Exchange(ref cacheRef._lock, 0);
                }
            }
        }
    }
}

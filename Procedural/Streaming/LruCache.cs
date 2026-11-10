using System;
using System.Collections.Generic;
using System.Threading;

namespace AlpineLib.Procedural.Streaming {
    /// <summary>
    /// A bounded least-recently-used cache, thread safe: threads asking for the same key at the same time share one build,
    /// which runs outside the cache lock, and the least recently asked entry goes when the cache is full. An entry evicted
    /// or cleared while its build runs is built again on a later ask. A build that throws is forgotten and the exception
    /// reaches every thread waiting on it; the next ask builds again. Evicted values are dropped, not disposed. A build may
    /// ask the cache for other keys but never for its own key.
    /// </summary>
    public sealed class LruCache<TKey, TValue> {
        private readonly int _capacity;
        private readonly Dictionary<TKey, LinkedListNode<KeyValuePair<TKey, Lazy<TValue>>>> _entries;
        private readonly LinkedList<KeyValuePair<TKey, Lazy<TValue>>> _order = new LinkedList<KeyValuePair<TKey, Lazy<TValue>>>();
        private readonly object _gate = new object();

        /// <summary>A cache holding at most <paramref name="capacity"/> values, keys compared by <paramref name="comparer"/> (default when null).</summary>
        public LruCache(int capacity, IEqualityComparer<TKey> comparer = null) {
            if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "Capacity must be at least one.");

            _capacity = capacity;
            _entries = new Dictionary<TKey, LinkedListNode<KeyValuePair<TKey, Lazy<TValue>>>>(comparer ?? EqualityComparer<TKey>.Default);
        }

        /// <summary>Most entries held at once.</summary>
        public int Capacity => _capacity;

        /// <summary>Entries held now.</summary>
        public int Count {
            get {
                lock (_gate) return _entries.Count;
            }
        }

        /// <summary>
        /// The value for <paramref name="key"/>, built by <paramref name="build"/> when the cache holds none; marks the key
        /// most recently used. A throwing build is forgotten and its exception rethrown.
        /// </summary>
        public TValue GetOrAdd(TKey key, Func<TValue> build) {
            if (key is null) throw new ArgumentNullException(nameof(key));
            if (build == null) throw new ArgumentNullException(nameof(build));

            Lazy<TValue> value;
            lock (_gate) value = TouchOrInsert(key, build);
            try {
                return value.Value;
            } catch {
                lock (_gate) Forget(key, value);
                throw;
            }
        }

        /// <summary>Whether <paramref name="key"/> is held now, without marking it used or waiting on its build.</summary>
        public bool Contains(TKey key) {
            if (key is null) throw new ArgumentNullException(nameof(key));

            lock (_gate) return _entries.ContainsKey(key);
        }

        /// <summary>Forgets every entry; builds already running still finish for the threads waiting on them.</summary>
        public void Clear() {
            lock (_gate) {
                _entries.Clear();
                _order.Clear();
            }
        }

        private Lazy<TValue> TouchOrInsert(TKey key, Func<TValue> build) {
            if (_entries.TryGetValue(key, out LinkedListNode<KeyValuePair<TKey, Lazy<TValue>>> node)) {
                _order.Remove(node);
                _order.AddFirst(node);
                return node.Value.Value;
            }

            var value = new Lazy<TValue>(build, LazyThreadSafetyMode.ExecutionAndPublication);
            _entries[key] = _order.AddFirst(new KeyValuePair<TKey, Lazy<TValue>>(key, value));
            Trim();
            return value;
        }

        // Drops the failed build only while the key still holds it (it may have been evicted and built again since).
        private void Forget(TKey key, Lazy<TValue> failed) {
            if (!_entries.TryGetValue(key, out LinkedListNode<KeyValuePair<TKey, Lazy<TValue>>> node) || !ReferenceEquals(node.Value.Value, failed)) return;

            _order.Remove(node);
            _entries.Remove(key);
        }

        private void Trim() {
            while (_entries.Count > _capacity) {
                LinkedListNode<KeyValuePair<TKey, Lazy<TValue>>> oldest = _order.Last;
                _order.RemoveLast();
                _entries.Remove(oldest.Value.Key);
            }
        }
    }
}

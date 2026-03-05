using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace MurshisoftData.Models;

public class SimpleCache<TKey, TValue>
{
    private readonly Dictionary<TKey, TValue> _cache = new Dictionary<TKey, TValue>();

    public void Add(TKey key, TValue value)
    {
        _cache[key] = value;
    }

    public TValue Get(TKey key)
    {
        return _cache.TryGetValue(key, out var value) ? value : default;
    }
    public bool TryGet(TKey key, out TValue value)
    {
        return _cache.TryGetValue(key, out value);
    }

    public bool ContainsKey(TKey key)
    {
        return _cache.ContainsKey(key);
    }

    public void Remove(TKey key)
    {
        _cache.Remove(key);
    }
    public void Clear()
    {
        _cache.Clear();
    }

}
public class MyCache<TKey, TValue> : IEnumerable<KeyValuePair<TKey, TValue>>, IDisposable
{
    private readonly ConcurrentDictionary<TKey, TtlValue> _dict = new ConcurrentDictionary<TKey, TtlValue>();

    private static SemaphoreSlim _globalStaticLock = new(1);
    public int Count => _dict.Count;
    public void Clear() => _dict.Clear();
    //public void AddOrUpdate(TKey key, TValue value, TimeSpan ttl)
    //{
    //    var ttlValue = new TtlValue(value, ttl);

    //    _dict.AddOrUpdate(key, (k, c) => c, (k, v, c) => c, ttlValue);
    //}
    public bool TryGet(TKey key, out TValue value)
    {
        value = default(TValue);

        if (!_dict.TryGetValue(key, out TtlValue ttlValue))
            return false; //not found
        value = ttlValue.Value;
        return true;
    }
    public bool TryAdd(TKey key, TValue value, TimeSpan ttl)
    {
        if (TryGet(key, out _))
            return false;

        return _dict.TryAdd(key, new TtlValue(value, ttl));
    }

    private TValue GetOrAddCore(TKey key, Func<TValue> valueFactory, TimeSpan ttl)
    {
        bool wasAdded = false; //flag to indicate "add vs get". TODO: wrap in ref type some day to avoid captures/closures
        var ttlValue = _dict.GetOrAdd(
            key,
            (k) =>
            {
                wasAdded = true;
                return new TtlValue(valueFactory(), ttl);
            });

        return ttlValue.Value;
    }
    public TValue GetOrAdd(TKey key, Func<TKey, TValue> valueFactory, TimeSpan ttl)
        => GetOrAddCore(key, () => valueFactory(key), ttl);

    public TValue GetOrAdd<TArg>(TKey key, Func<TKey, TArg, TValue> valueFactory, TimeSpan ttl, TArg factoryArgument)
        => GetOrAddCore(key, () => valueFactory(key, factoryArgument), ttl);
    public TValue GetOrAdd(TKey key, TValue value, TimeSpan ttl)
        => GetOrAddCore(key, () => value, ttl);
    public void Remove(TKey key)
    {
        _dict.TryRemove(key, out _);
    }
    public bool TryRemove(TKey key, out TValue value)
    {
        bool res = _dict.TryRemove(key, out var ttlValue);
        value = res ? ttlValue.Value : default(TValue);
        return res;
    }

    public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator()
    {
        foreach (var kvp in _dict)
        {
            yield return new KeyValuePair<TKey, TValue>(kvp.Key, kvp.Value.Value);
        }
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return this.GetEnumerator();
    }

    private class TtlValue
    {
        public TValue Value { get; private set; }
        private long TickCountWhenToKill;

        public TtlValue(TValue value, TimeSpan ttl)
        {
            Value = value;
        }


    }

    //IDispisable members
    private bool _disposedValue;
    /// <inheritdoc/>
    public void Dispose() => Dispose(true);
    /// <inheritdoc/>
    protected virtual void Dispose(bool disposing)
    {
        if (!_disposedValue)
        {

            _disposedValue = true;
        }
    }
}
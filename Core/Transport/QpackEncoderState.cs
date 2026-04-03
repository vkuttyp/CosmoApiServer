using System.Text;

namespace CosmoApiServer.Core.Transport;

/// <summary>
/// Server-side QPACK dynamic table encoder state. Tracks what the server has inserted
/// so response HEADERS frames can reference dynamic entries instead of repeating literals.
/// Thread-safe: shared across concurrent request streams on the same connection.
/// </summary>
internal sealed class QpackEncoderState
{
    private readonly object _gate = new();
    private readonly List<(string Name, string Value, int Size)> _table = [];
    private int _usedBytes;
    private int _maxCapacity;
    private long _insertCount;

    public long InsertCount { get { lock (_gate) return _insertCount; } }
    public int MaxCapacity { get { lock (_gate) return _maxCapacity; } }

    /// <summary>MaxEntries as defined by RFC 9204 — floor(MaxCapacity / 32).</summary>
    public int MaxEntries { get { lock (_gate) return _maxCapacity > 0 ? _maxCapacity / 32 : 0; } }

    public void SetCapacity(int capacity)
    {
        lock (_gate)
        {
            _maxCapacity = capacity;
            TrimToCapacity();
        }
    }

    /// <summary>
    /// Returns the absolute index and relative (pre-base) index for an exact name+value match,
    /// so the caller can emit an Indexed Field Line with Dynamic Table reference.
    /// </summary>
    public bool TryGetEntry(string name, string value, out long absoluteIndex)
    {
        lock (_gate)
        {
            for (int i = 0; i < _table.Count; i++)
            {
                var e = _table[i];
                if (string.Equals(e.Name, name, StringComparison.Ordinal) &&
                    string.Equals(e.Value, value, StringComparison.Ordinal))
                {
                    absoluteIndex = _insertCount - 1 - i;
                    return true;
                }
            }
        }
        absoluteIndex = -1;
        return false;
    }

    /// <summary>
    /// Returns the absolute index of the most-recently-inserted entry whose name matches,
    /// so the caller can emit a Literal Field Line with Dynamic Name Reference.
    /// </summary>
    public bool TryGetNameEntry(string name, out long absoluteIndex)
    {
        lock (_gate)
        {
            for (int i = 0; i < _table.Count; i++)
            {
                if (string.Equals(_table[i].Name, name, StringComparison.Ordinal))
                {
                    absoluteIndex = _insertCount - 1 - i;
                    return true;
                }
            }
        }
        absoluteIndex = -1;
        return false;
    }

    /// <summary>
    /// Inserts a new entry. Evicts oldest entries as needed to make room.
    /// Returns the absolute index of the inserted entry, or -1 if the entry cannot fit
    /// (capacity is 0 or the entry alone exceeds capacity).
    /// </summary>
    public long Insert(string name, string value)
    {
        int size = EntrySize(name, value);
        lock (_gate)
        {
            if (_maxCapacity <= 0 || size > _maxCapacity)
                return -1;

            while (_usedBytes + size > _maxCapacity && _table.Count > 0)
            {
                var evicted = _table[^1];
                _table.RemoveAt(_table.Count - 1);
                _usedBytes -= evicted.Size;
            }

            if (_usedBytes + size > _maxCapacity)
                return -1;

            _table.Insert(0, (name, value, size));
            _usedBytes += size;
            return _insertCount++;
        }
    }

    /// <summary>
    /// Encodes the Required Insert Count field per RFC 9204 Appendix B.1.
    /// Returns 0 when there are no dynamic references.
    /// </summary>
    public long EncodeRequiredInsertCount(long requiredInsertCount)
    {
        if (requiredInsertCount == 0)
            return 0;

        int maxEntries = MaxEntries;
        if (maxEntries <= 0)
            return 0;

        return (requiredInsertCount % (2 * maxEntries)) + 1;
    }

    private void TrimToCapacity()
    {
        while (_usedBytes > _maxCapacity && _table.Count > 0)
        {
            var evicted = _table[^1];
            _table.RemoveAt(_table.Count - 1);
            _usedBytes -= evicted.Size;
        }
    }

    internal static int EntrySize(string name, string value) =>
        32 + Encoding.UTF8.GetByteCount(name) + Encoding.UTF8.GetByteCount(value);
}

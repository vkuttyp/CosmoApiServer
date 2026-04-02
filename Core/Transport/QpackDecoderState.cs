using System.Text;

namespace CosmoApiServer.Core.Transport;

internal sealed class QpackDecoderState
{
    private readonly object _gate = new();
    private readonly List<QpackDynamicEntry> _entries = [];
    private int _dynamicTableSize;
    private int _maxTableCapacity;
    private int _blockedStreams;
    private long _insertCount;

    public int MaxTableCapacity
    {
        get { lock (_gate) return _maxTableCapacity; }
    }

    public int BlockedStreams
    {
        get { lock (_gate) return _blockedStreams; }
    }

    public long InsertCount
    {
        get { lock (_gate) return _insertCount; }
    }

    public int EntryCount
    {
        get { lock (_gate) return _entries.Count; }
    }

    public IReadOnlyList<(string name, string value)> SnapshotEntries()
    {
        lock (_gate)
            return _entries.Select(x => (x.Name, x.Value)).ToArray();
    }

    public void ApplyPeerSettings(ReadOnlySpan<byte> payload)
    {
        int pos = 0;
        while (pos < payload.Length)
        {
            long settingId = ReadVarInt(payload, ref pos);
            long settingValue = ReadVarInt(payload, ref pos);

            switch (settingId)
            {
                case 0x01:
                    lock (_gate)
                    {
                        _maxTableCapacity = checked((int)settingValue);
                        TrimToCapacity();
                    }
                    break;
                case 0x07:
                    lock (_gate)
                    {
                        _blockedStreams = checked((int)settingValue);
                    }
                    break;
            }
        }
    }

    public void ProcessEncoderInstructions(ReadOnlySpan<byte> payload)
    {
        int pos = 0;
        while (pos < payload.Length)
        {
            byte b = payload[pos];
            if ((b & 0x80) != 0)
            {
                bool isStatic = (b & 0x40) != 0;
                int nameIndex = checked((int)ReadPrefixedInteger(payload, ref pos, 6));
                string name = isStatic
                    ? GetStaticEntry(nameIndex).name
                    : GetDynamicEntryRelative(nameIndex).Name;
                string value = ReadStringLiteral(payload, ref pos, 7, 0x80);
                Insert(name, value);
            }
            else if ((b & 0x40) != 0)
            {
                string name = ReadStringLiteral(payload, ref pos, 5, 0x20);
                string value = ReadStringLiteral(payload, ref pos, 7, 0x80);
                Insert(name, value);
            }
            else if ((b & 0x20) != 0)
            {
                int capacity = checked((int)ReadPrefixedInteger(payload, ref pos, 5));
                lock (_gate)
                {
                    _maxTableCapacity = capacity;
                    TrimToCapacity();
                }
            }
            else
            {
                int index = checked((int)ReadPrefixedInteger(payload, ref pos, 5));
                Duplicate(index);
            }
        }
    }

    private void Duplicate(int index)
    {
        QpackDynamicEntry entry;
        lock (_gate)
        {
            entry = GetDynamicEntryRelative(index);
        }

        Insert(entry.Name, entry.Value);
    }

    private void Insert(string name, string value)
    {
        lock (_gate)
        {
            int entrySize = GetEntrySize(name, value);
            if (_maxTableCapacity <= 0 || entrySize > _maxTableCapacity)
            {
                _entries.Clear();
                _dynamicTableSize = 0;
                return;
            }

            while (_dynamicTableSize + entrySize > _maxTableCapacity && _entries.Count > 0)
            {
                var evicted = _entries[^1];
                _entries.RemoveAt(_entries.Count - 1);
                _dynamicTableSize -= evicted.Size;
            }

            var entry = new QpackDynamicEntry(name, value, entrySize);
            _entries.Insert(0, entry);
            _dynamicTableSize += entrySize;
            _insertCount++;
        }
    }

    private void TrimToCapacity()
    {
        while (_dynamicTableSize > _maxTableCapacity && _entries.Count > 0)
        {
            var evicted = _entries[^1];
            _entries.RemoveAt(_entries.Count - 1);
            _dynamicTableSize -= evicted.Size;
        }
    }

    private QpackDynamicEntry GetDynamicEntryRelative(int index)
    {
        lock (_gate)
        {
            if ((uint)index >= (uint)_entries.Count)
                throw new InvalidOperationException($"Unsupported QPACK dynamic index: {index}");
            return _entries[index];
        }
    }

    private static int GetEntrySize(string name, string value) => 32 + Encoding.UTF8.GetByteCount(name) + Encoding.UTF8.GetByteCount(value);

    internal static (string name, string value) GetStaticEntry(int index)
    {
        if ((uint)index >= (uint)QpackStaticTable.Length)
            throw new InvalidOperationException($"Unsupported QPACK static index: {index}");
        return QpackStaticTable[index];
    }

    private static string ReadStringLiteral(ReadOnlySpan<byte> data, ref int pos, int prefixBits, byte huffmanMask)
    {
        bool huffman = (data[pos] & huffmanMask) != 0;
        int length = checked((int)ReadPrefixedInteger(data, ref pos, prefixBits));
        if (pos + length > data.Length)
            throw new InvalidOperationException("Invalid QPACK string length.");

        var bytes = data.Slice(pos, length);
        pos += length;
        return huffman
            ? HpackDecoder.DecodeHuffmanString(bytes)
            : Encoding.ASCII.GetString(bytes);
    }

    private static long ReadVarInt(ReadOnlySpan<byte> data, ref int pos)
    {
        byte first = data[pos];
        int length = 1 << (first >> 6);
        long value = first & 0x3F;
        pos++;
        for (int i = 1; i < length; i++)
            value = (value << 8) | data[pos++];
        return value;
    }

    private static long ReadPrefixedInteger(ReadOnlySpan<byte> data, ref int pos, int prefixBits)
    {
        int mask = (1 << prefixBits) - 1;
        long value = data[pos++] & mask;
        if (value < mask) return value;

        int shift = 0;
        while (true)
        {
            byte b = data[pos++];
            value += (long)(b & 0x7F) << shift;
            if ((b & 0x80) == 0) break;
            shift += 7;
        }
        return value;
    }

    private sealed record QpackDynamicEntry(string Name, string Value, int Size);

    private static readonly (string name, string value)[] QpackStaticTable =
    [
        (":authority", ""),
        (":path", "/"),
        ("age", "0"),
        ("content-disposition", ""),
        ("content-length", "0"),
        ("cookie", ""),
        ("date", ""),
        ("etag", ""),
        ("if-modified-since", ""),
        ("if-none-match", ""),
        ("last-modified", ""),
        ("link", ""),
        ("location", ""),
        ("referer", ""),
        ("set-cookie", ""),
        (":method", "CONNECT"),
        (":method", "DELETE"),
        (":method", "GET"),
        (":method", "HEAD"),
        (":method", "OPTIONS"),
        (":method", "POST"),
        (":method", "PUT"),
        (":scheme", "http"),
        (":scheme", "https"),
        (":status", "103"),
        (":status", "200"),
        (":status", "304"),
        (":status", "404"),
        (":status", "503"),
        ("accept", "*/*"),
        ("accept", "application/dns-message"),
        ("accept-encoding", "gzip, deflate, br"),
        ("accept-ranges", "bytes"),
        ("access-control-allow-headers", "cache-control"),
        ("access-control-allow-headers", "content-type"),
        ("access-control-allow-origin", "*"),
        ("cache-control", "max-age=0"),
        ("cache-control", "max-age=2592000"),
        ("cache-control", "max-age=604800"),
        ("cache-control", "no-cache"),
        ("cache-control", "no-store"),
        ("cache-control", "public, max-age=31536000"),
        ("content-encoding", "br"),
        ("content-encoding", "gzip"),
        ("content-type", "application/dns-message"),
        ("content-type", "application/javascript"),
        ("content-type", "application/json"),
        ("content-type", "application/x-www-form-urlencoded"),
        ("content-type", "image/gif"),
        ("content-type", "image/jpeg"),
        ("content-type", "image/png"),
        ("content-type", "text/css"),
        ("content-type", "text/html; charset=utf-8"),
        ("content-type", "text/plain"),
        ("content-type", "text/plain;charset=utf-8"),
        ("range", "bytes=0-"),
        ("strict-transport-security", "max-age=31536000"),
        ("strict-transport-security", "max-age=31536000; includesubdomains"),
        ("strict-transport-security", "max-age=31536000; includesubdomains; preload"),
        ("vary", "accept-encoding"),
        ("vary", "origin"),
        ("x-content-type-options", "nosniff"),
        ("x-xss-protection", "1; mode=block"),
        (":status", "100"),
        (":status", "204"),
        (":status", "206"),
        (":status", "302"),
        (":status", "400"),
        (":status", "403"),
        (":status", "421"),
        (":status", "425"),
        (":status", "500"),
        ("accept-language", ""),
        ("access-control-allow-credentials", "FALSE"),
        ("access-control-allow-credentials", "TRUE"),
        ("access-control-allow-headers", "*"),
        ("access-control-allow-methods", "get"),
        ("access-control-allow-methods", "get, post, options"),
        ("access-control-allow-methods", "options"),
        ("access-control-expose-headers", "content-length"),
        ("access-control-request-headers", "content-type"),
        ("access-control-request-method", "get"),
        ("access-control-request-method", "post"),
        ("alt-svc", "clear"),
        ("authorization", ""),
        ("content-security-policy", "script-src 'none'; object-src 'none'; base-uri 'none'"),
        ("early-data", "1"),
        ("expect-ct", ""),
        ("forwarded", ""),
        ("if-range", ""),
        ("origin", ""),
        ("purpose", "prefetch"),
        ("server", ""),
        ("timing-allow-origin", "*"),
        ("upgrade-insecure-requests", "1"),
        ("user-agent", ""),
        ("x-forwarded-for", ""),
        ("x-frame-options", "deny"),
        ("x-frame-options", "sameorigin"),
    ];
}

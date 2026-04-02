using System.Text;

namespace CosmoApiServer.Core.Transport;

internal sealed class QpackDecoderState
{
    private readonly object _gate = new();
    private readonly List<QpackDynamicEntry> _entries = [];
    private byte[] _encoderPending = [];
    private int _dynamicTableSize;
    private int _maxTableCapacity;
    private int _blockedStreams;
    private int _blockedStreamWaiters;
    private long _insertCount;
    private TaskCompletionSource<long> _insertCountChanged = NewInsertCountChangedSource();
    private Action<int>? _insertCountIncrementSink;

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

    public int MaxEntries
    {
        get { lock (_gate) return _maxTableCapacity / 32; }
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

    public (string name, string value) GetDynamicEntryByAbsoluteIndex(long absoluteIndex)
    {
        lock (_gate)
        {
            long newestAbsoluteIndex = _insertCount - 1;
            long offset = newestAbsoluteIndex - absoluteIndex;
            if (offset < 0 || offset >= _entries.Count)
                throw new InvalidOperationException($"Unsupported QPACK dynamic absolute index: {absoluteIndex}");

            var entry = _entries[(int)offset];
            return (entry.Name, entry.Value);
        }
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

    public void SetInsertCountIncrementSink(Action<int> sink)
    {
        _insertCountIncrementSink = sink;
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

    public void AppendEncoderStreamData(ReadOnlySpan<byte> payload)
    {
        lock (_gate)
        {
            if (!payload.IsEmpty)
            {
                var combined = new byte[_encoderPending.Length + payload.Length];
                _encoderPending.CopyTo(combined, 0);
                payload.CopyTo(combined.AsSpan(_encoderPending.Length));
                _encoderPending = combined;
            }

            int consumed = 0;
            while (TryConsumeNextInstruction(_encoderPending.AsSpan(consumed), out int instructionLength))
                consumed += instructionLength;

            if (consumed <= 0)
                return;

            _encoderPending = consumed == _encoderPending.Length
                ? []
                : _encoderPending[consumed..];
        }
    }

    public async Task WaitForInsertCountAsync(long requiredInsertCount, CancellationToken ct)
    {
        bool registeredWaiter = false;
        try
        {
            while (true)
            {
                TaskCompletionSource<long> waiter;
                lock (_gate)
                {
                    if (_insertCount >= requiredInsertCount)
                        break;

                    if (!registeredWaiter)
                    {
                        if (_blockedStreams <= 0)
                            throw new InvalidOperationException("HTTP/3 field section would exceed SETTINGS_QPACK_BLOCKED_STREAMS.");
                        if (_blockedStreamWaiters >= _blockedStreams)
                            throw new InvalidOperationException("HTTP/3 field section would exceed SETTINGS_QPACK_BLOCKED_STREAMS.");

                        _blockedStreamWaiters++;
                        registeredWaiter = true;
                    }

                    waiter = _insertCountChanged;
                }

                await waiter.Task.WaitAsync(ct);
            }
        }
        finally
        {
            if (registeredWaiter)
            {
                lock (_gate)
                {
                    _blockedStreamWaiters--;
                }
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
        TaskCompletionSource<long>? completion = null;
        Action<int>? incrementSink = null;
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
            completion = _insertCountChanged;
            _insertCountChanged = NewInsertCountChangedSource();
            incrementSink = _insertCountIncrementSink;
        }

        completion?.TrySetResult(_insertCount);
        incrementSink?.Invoke(1);
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
        if ((uint)index >= (uint)_entries.Count)
            throw new InvalidOperationException($"Unsupported QPACK dynamic index: {index}");
        return _entries[index];
    }

    private bool TryConsumeNextInstruction(ReadOnlySpan<byte> data, out int consumed)
    {
        consumed = 0;
        if (data.IsEmpty)
            return false;

        int pos = 0;
        byte b = data[pos];
        if ((b & 0x80) != 0)
        {
            bool isStatic = (b & 0x40) != 0;
            if (!TryReadPrefixedInteger(data, ref pos, 6, out long nameIndex))
                return false;
            string name;
            if (isStatic)
            {
                name = GetStaticEntry(checked((int)nameIndex)).name;
            }
            else
            {
                if ((uint)nameIndex >= (uint)_entries.Count)
                    throw new InvalidOperationException($"Unsupported QPACK dynamic index: {nameIndex}");
                name = _entries[(int)nameIndex].Name;
            }

            if (!TryReadStringLiteral(data, ref pos, 7, 0x80, out string value))
                return false;

            Insert(name, value);
        }
        else if ((b & 0x40) != 0)
        {
            if (!TryReadStringLiteral(data, ref pos, 5, 0x20, out string name))
                return false;
            if (!TryReadStringLiteral(data, ref pos, 7, 0x80, out string value))
                return false;

            Insert(name, value);
        }
        else if ((b & 0x20) != 0)
        {
            if (!TryReadPrefixedInteger(data, ref pos, 5, out long capacity))
                return false;
            _maxTableCapacity = checked((int)capacity);
            TrimToCapacity();
        }
        else
        {
            if (!TryReadPrefixedInteger(data, ref pos, 5, out long index))
                return false;
            Duplicate(checked((int)index));
        }

        consumed = pos;
        return true;
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

    private static bool TryReadStringLiteral(ReadOnlySpan<byte> data, ref int pos, int prefixBits, byte huffmanMask, out string value)
    {
        value = string.Empty;
        int originalPos = pos;
        if (pos >= data.Length)
            return false;

        bool huffman = (data[pos] & huffmanMask) != 0;
        if (!TryReadPrefixedInteger(data, ref pos, prefixBits, out long length))
        {
            pos = originalPos;
            return false;
        }

        if (length < 0 || pos + length > data.Length)
        {
            pos = originalPos;
            return false;
        }

        var bytes = data.Slice(pos, checked((int)length));
        pos += (int)length;
        value = huffman
            ? HpackDecoder.DecodeHuffmanString(bytes)
            : Encoding.ASCII.GetString(bytes);
        return true;
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

    private static bool TryReadPrefixedInteger(ReadOnlySpan<byte> data, ref int pos, int prefixBits, out long value)
    {
        value = 0;
        int originalPos = pos;
        if (pos >= data.Length)
            return false;

        int mask = (1 << prefixBits) - 1;
        value = data[pos++] & mask;
        if (value < mask) return true;

        int shift = 0;
        while (true)
        {
            if (pos >= data.Length)
            {
                pos = originalPos;
                value = 0;
                return false;
            }

            byte b = data[pos++];
            value += (long)(b & 0x7F) << shift;
            if ((b & 0x80) == 0) break;
            shift += 7;
        }
        return true;
    }

    private static TaskCompletionSource<long> NewInsertCountChangedSource() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

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

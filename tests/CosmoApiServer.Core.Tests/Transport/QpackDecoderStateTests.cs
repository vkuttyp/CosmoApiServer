using System.Text;
using CosmoApiServer.Core.Transport;

namespace CosmoApiServer.Core.Tests.Transport;

public class QpackDecoderStateTests
{
    [Fact]
    public void ApplyPeerSettings_TracksCapacityAndBlockedStreams()
    {
        var state = new QpackDecoderState();
        using var ms = new MemoryStream();

        WriteVarInt(ms, 0x01);
        WriteVarInt(ms, 512);
        WriteVarInt(ms, 0x07);
        WriteVarInt(ms, 8);

        state.ApplyPeerSettings(ms.ToArray());

        Assert.Equal(512, state.MaxTableCapacity);
        Assert.Equal(8, state.BlockedStreams);
    }

    [Fact]
    public void ProcessEncoderInstructions_InsertsStaticNameReferenceEntry()
    {
        var state = new QpackDecoderState();
        state.ApplyPeerSettings(EncodeSettings((0x01, 512)));

        state.ProcessEncoderInstructions(EncodeInsertWithStaticNameReference(46, "application/json"));

        var entries = state.SnapshotEntries();
        Assert.Single(entries);
        Assert.Equal("content-type", entries[0].name);
        Assert.Equal("application/json", entries[0].value);
        Assert.Equal(1, state.InsertCount);
    }

    [Fact]
    public void ProcessEncoderInstructions_InsertsLiteralNameAndDuplicate()
    {
        var state = new QpackDecoderState();
        state.ApplyPeerSettings(EncodeSettings((0x01, 512)));

        using var ms = new MemoryStream();
        ms.Write(EncodeInsertWithLiteralName("x-tenant", "alpha"));
        ms.Write(EncodeDuplicate(0));

        state.ProcessEncoderInstructions(ms.ToArray());

        var entries = state.SnapshotEntries();
        Assert.Equal(2, entries.Count);
        Assert.Equal(("x-tenant", "alpha"), entries[0]);
        Assert.Equal(("x-tenant", "alpha"), entries[1]);
        Assert.Equal(2, state.InsertCount);
    }

    [Fact]
    public void DecodeFieldSectionForTests_ResolvesDynamicRelativeAndPostBaseReferences()
    {
        var state = new QpackDecoderState();
        state.ApplyPeerSettings(EncodeSettings((0x01, 512)));

        using var encoderStream = new MemoryStream();
        encoderStream.Write(EncodeInsertWithLiteralName("x-one", "v1"));
        encoderStream.Write(EncodeInsertWithLiteralName("x-two", "v2"));
        encoderStream.Write(EncodeInsertWithLiteralName("x-three", "v3"));
        state.ProcessEncoderInstructions(encoderStream.ToArray());

        byte[] fieldSection = EncodeFieldSection(
            encodedRequiredInsertCount: 4,
            signBit: true,
            deltaBase: 1,
            writer =>
            {
                writer.Write(EncodeIndexedDynamicRelative(0));
                writer.Write(EncodeIndexedDynamicPostBase(1));
            });

        var decoded = Http3Connection.DecodeFieldSectionForTests(fieldSection, state);

        Assert.Collection(decoded,
            h => Assert.Equal(("x-one", "v1"), h),
            h => Assert.Equal(("x-three", "v3"), h));
    }

    [Fact]
    public void DecodeFieldSectionForTests_ResolvesDynamicNameReferences()
    {
        var state = new QpackDecoderState();
        state.ApplyPeerSettings(EncodeSettings((0x01, 512)));

        using var encoderStream = new MemoryStream();
        encoderStream.Write(EncodeInsertWithLiteralName("x-one", "v1"));
        encoderStream.Write(EncodeInsertWithLiteralName("x-two", "v2"));
        encoderStream.Write(EncodeInsertWithLiteralName("x-three", "v3"));
        state.ProcessEncoderInstructions(encoderStream.ToArray());

        byte[] fieldSection = EncodeFieldSection(
            encodedRequiredInsertCount: 4,
            signBit: true,
            deltaBase: 1,
            writer =>
            {
                writer.Write(EncodeLiteralWithDynamicNameReference(0, "left"));
                writer.Write(EncodeLiteralWithPostBaseNameReference(1, "right"));
            });

        var decoded = Http3Connection.DecodeFieldSectionForTests(fieldSection, state);

        Assert.Collection(decoded,
            h => Assert.Equal(("x-one", "left"), h),
            h => Assert.Equal(("x-three", "right"), h));
    }

    [Fact]
    public async Task WaitForInsertCountAsync_CompletesAfterIncrementalEncoderData()
    {
        var state = new QpackDecoderState();
        state.ApplyPeerSettings(EncodeSettings((0x01, 512), (0x07, 4)));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        Task waitTask = state.WaitForInsertCountAsync(1, cts.Token);
        Assert.False(waitTask.IsCompleted);

        byte[] instruction = EncodeInsertWithLiteralName("x-live", "ok");
        state.AppendEncoderStreamData(instruction[..2]);
        Assert.False(waitTask.IsCompleted);

        state.AppendEncoderStreamData(instruction[2..]);
        await waitTask;

        var entries = state.SnapshotEntries();
        Assert.Single(entries);
        Assert.Equal(("x-live", "ok"), entries[0]);
    }

    [Fact]
    public async Task WaitForInsertCountAsync_RejectsWhenBlockedStreamsDisabled()
    {
        var state = new QpackDecoderState();
        state.ApplyPeerSettings(EncodeSettings((0x01, 512), (0x07, 0)));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            state.WaitForInsertCountAsync(1, CancellationToken.None));

        Assert.Contains("BLOCKED_STREAMS", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WaitForInsertCountAsync_RejectsWhenBlockedStreamLimitExceeded()
    {
        var state = new QpackDecoderState();
        state.ApplyPeerSettings(EncodeSettings((0x01, 512), (0x07, 1)));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        Task first = state.WaitForInsertCountAsync(1, cts.Token);
        await Task.Delay(20);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            state.WaitForInsertCountAsync(1, CancellationToken.None));

        Assert.Contains("BLOCKED_STREAMS", ex.Message, StringComparison.OrdinalIgnoreCase);

        byte[] instruction = EncodeInsertWithLiteralName("x-open", "ok");
        state.AppendEncoderStreamData(instruction);
        await first;
    }

    private static byte[] EncodeSettings(params (long id, long value)[] settings)
    {
        using var ms = new MemoryStream();
        foreach (var (id, value) in settings)
        {
            WriteVarInt(ms, id);
            WriteVarInt(ms, value);
        }
        return ms.ToArray();
    }

    private static byte[] EncodeInsertWithStaticNameReference(int nameIndex, string value)
    {
        using var ms = new MemoryStream();
        WritePrefixedInteger(ms, 0xC0, 6, nameIndex);
        WriteStringLiteral(ms, value, 7, 0x00);
        return ms.ToArray();
    }

    private static byte[] EncodeInsertWithLiteralName(string name, string value)
    {
        using var ms = new MemoryStream();
        WritePrefixedInteger(ms, 0x40, 5, Encoding.ASCII.GetByteCount(name));
        ms.Write(Encoding.ASCII.GetBytes(name));
        WriteStringLiteral(ms, value, 7, 0x00);
        return ms.ToArray();
    }

    private static byte[] EncodeDuplicate(int index)
    {
        using var ms = new MemoryStream();
        WritePrefixedInteger(ms, 0x00, 5, index);
        return ms.ToArray();
    }

    private static byte[] EncodeFieldSection(long encodedRequiredInsertCount, bool signBit, long deltaBase, Action<MemoryStream> writer)
    {
        using var ms = new MemoryStream();
        WritePrefixedInteger(ms, 0x00, 8, checked((int)encodedRequiredInsertCount));
        WritePrefixedInteger(ms, signBit ? (byte)0x80 : (byte)0x00, 7, checked((int)deltaBase));
        writer(ms);
        return ms.ToArray();
    }

    private static byte[] EncodeIndexedDynamicRelative(int index)
    {
        using var ms = new MemoryStream();
        WritePrefixedInteger(ms, 0x80, 6, index);
        return ms.ToArray();
    }

    private static byte[] EncodeIndexedDynamicPostBase(int index)
    {
        using var ms = new MemoryStream();
        WritePrefixedInteger(ms, 0x10, 4, index);
        return ms.ToArray();
    }

    private static byte[] EncodeLiteralWithDynamicNameReference(int nameIndex, string value)
    {
        using var ms = new MemoryStream();
        WritePrefixedInteger(ms, 0x40, 4, nameIndex);
        WriteStringLiteral(ms, value, 7, 0x00);
        return ms.ToArray();
    }

    private static byte[] EncodeLiteralWithPostBaseNameReference(int nameIndex, string value)
    {
        using var ms = new MemoryStream();
        WritePrefixedInteger(ms, 0x00, 3, nameIndex);
        WriteStringLiteral(ms, value, 7, 0x00);
        return ms.ToArray();
    }

    private static void WriteStringLiteral(Stream stream, string value, int prefixBits, byte prefixPattern)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        WritePrefixedInteger(stream, prefixPattern, prefixBits, bytes.Length);
        stream.Write(bytes, 0, bytes.Length);
    }

    private static void WritePrefixedInteger(Stream stream, byte prefixPattern, int prefixBits, int value)
    {
        int mask = (1 << prefixBits) - 1;
        if (value < mask)
        {
            stream.WriteByte((byte)(prefixPattern | value));
            return;
        }

        stream.WriteByte((byte)(prefixPattern | mask));
        int remaining = value - mask;
        while (remaining >= 128)
        {
            stream.WriteByte((byte)((remaining & 0x7F) | 0x80));
            remaining >>= 7;
        }
        stream.WriteByte((byte)remaining);
    }

    private static void WriteVarInt(Stream stream, long value)
    {
        Span<byte> buffer = stackalloc byte[8];
        int length = EncodeVarInt(value, buffer);
        stream.Write(buffer[..length]);
    }

    private static int EncodeVarInt(long value, Span<byte> destination)
    {
        if (value < 64)
        {
            destination[0] = (byte)value;
            return 1;
        }
        if (value < 16384)
        {
            destination[0] = (byte)(0x40 | ((value >> 8) & 0x3F));
            destination[1] = (byte)(value & 0xFF);
            return 2;
        }
        if (value < 1073741824)
        {
            destination[0] = (byte)(0x80 | ((value >> 24) & 0x3F));
            destination[1] = (byte)((value >> 16) & 0xFF);
            destination[2] = (byte)((value >> 8) & 0xFF);
            destination[3] = (byte)(value & 0xFF);
            return 4;
        }

        destination[0] = (byte)(0xC0 | ((value >> 56) & 0x3F));
        destination[1] = (byte)((value >> 48) & 0xFF);
        destination[2] = (byte)((value >> 40) & 0xFF);
        destination[3] = (byte)((value >> 32) & 0xFF);
        destination[4] = (byte)((value >> 24) & 0xFF);
        destination[5] = (byte)((value >> 16) & 0xFF);
        destination[6] = (byte)((value >> 8) & 0xFF);
        destination[7] = (byte)(value & 0xFF);
        return 8;
    }
}

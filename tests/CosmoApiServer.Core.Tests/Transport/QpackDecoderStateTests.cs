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

using System.Buffers.Binary;
using System.Text;

namespace PhoneLinkDiag.Services;

public static class ObexPacketBuilder
{
    private const byte ObexConnectOpcode = 0x80;
    private const byte ObexPutFinalOpcode = 0x82;
    private const byte HeaderTarget = 0x46;
    private const byte HeaderType = 0x42;
    private const byte HeaderLength4Byte = 0xC3;
    private const byte HeaderEndOfBody = 0x49;

    public static byte[] Connect(Guid? target = null, ushort maxPacketSize = 0xFFFF)
    {
        using var ms = new MemoryStream();
        ms.WriteByte(ObexConnectOpcode);
        ms.Write(stackalloc byte[2]); // length placeholder
        ms.WriteByte(0x10); // OBEX 1.0
        ms.WriteByte(0x00); // flags
        Span<byte> maxPacketBytes = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(maxPacketBytes, maxPacketSize);
        ms.Write(maxPacketBytes);

        if (target is not null)
        {
            WriteTargetHeader(ms, target.Value);
        }

        PatchLength(ms);
        return ms.ToArray();
    }

    public static byte[] PutMessageFinal(string bMessage)
    {
        var body = Encoding.UTF8.GetBytes(bMessage);
        using var ms = new MemoryStream();
        ms.WriteByte(ObexPutFinalOpcode);
        ms.Write(stackalloc byte[2]); // length placeholder
        WriteTypeHeader(ms, "x-bt/message");
        WriteUInt32Header(ms, HeaderLength4Byte, (uint)body.Length);
        WriteByteSequenceHeader(ms, HeaderEndOfBody, body);
        PatchLength(ms);
        return ms.ToArray();
    }

    private static void WriteTargetHeader(Stream ms, Guid target)
    {
        var bytes = target.ToByteArray();
        // OBEX UUID headers are network-order. Guid.ToByteArray uses mixed endian.
        bytes = GuidToNetworkBytes(target);
        WriteByteSequenceHeader(ms, HeaderTarget, bytes);
    }

    private static void WriteTypeHeader(Stream ms, string value)
    {
        var bytes = Encoding.ASCII.GetBytes(value + "\0");
        WriteByteSequenceHeader(ms, HeaderType, bytes);
    }

    private static void WriteByteSequenceHeader(Stream ms, byte headerId, ReadOnlySpan<byte> value)
    {
        ms.WriteByte(headerId);
        Span<byte> length = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(length, checked((ushort)(value.Length + 3)));
        ms.Write(length);
        ms.Write(value);
    }

    private static void WriteUInt32Header(Stream ms, byte headerId, uint value)
    {
        ms.WriteByte(headerId);
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        ms.Write(bytes);
    }

    private static void PatchLength(MemoryStream ms)
    {
        var len = checked((ushort)ms.Length);
        var buffer = ms.GetBuffer();
        buffer[1] = (byte)(len >> 8);
        buffer[2] = (byte)(len & 0xFF);
    }

    private static byte[] GuidToNetworkBytes(Guid guid)
    {
        var text = guid.ToString("N");
        var bytes = new byte[16];
        for (var i = 0; i < 16; i++)
        {
            bytes[i] = Convert.ToByte(text.Substring(i * 2, 2), 16);
        }
        return bytes;
    }
}

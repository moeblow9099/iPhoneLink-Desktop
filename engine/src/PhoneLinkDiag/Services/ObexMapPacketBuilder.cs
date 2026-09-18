using System.Buffers.Binary;
using System.Text;

namespace PhoneLinkDiag.Services;

public static class ObexMapPacketBuilder
{
    public const byte OpConnect = 0x80;
    public const byte OpGetFinal = 0x83;
    public const byte OpSetPath = 0x85;
    public const byte OpPutFinal = 0x82;
    public const byte HeaderName = 0x01;
    public const byte HeaderType = 0x42;
    public const byte HeaderTarget = 0x46;
    public const byte HeaderBody = 0x48;
    public const byte HeaderEndOfBody = 0x49;
    public const byte HeaderWho = 0x4A;
    public const byte HeaderApplicationParameters = 0x4C;
    public const byte HeaderAuthenticateChallenge = 0x4D;
    public const byte HeaderConnectionId = 0xCB;

    public static byte[] Connect(Guid target, ushort maxPacketSize = 0xFFFF, byte[]? appParameters = null)
    {
        using var ms = new MemoryStream();
        ms.WriteByte(OpConnect);
        ms.Write(stackalloc byte[2]);
        ms.WriteByte(0x10);
        ms.WriteByte(0x00);
        WriteUInt16(ms, maxPacketSize);
        WriteByteSequence(ms, HeaderTarget, GuidToNetworkBytes(target));
        if (appParameters is { Length: > 0 })
        {
            WriteByteSequence(ms, HeaderApplicationParameters, appParameters);
        }
        PatchLength(ms);
        return ms.ToArray();
    }

    public static byte[] MapSupportedFeatures(uint features)
    {
        var value = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(value, features);
        return AppParams((0x29, value));
    }

    public static byte[] Get(uint connectionId, string type, string? name = null, byte[]? appParameters = null)
    {
        using var ms = new MemoryStream();
        ms.WriteByte(OpGetFinal);
        ms.Write(stackalloc byte[2]);
        WriteUInt32Header(ms, HeaderConnectionId, connectionId);
        if (!string.IsNullOrWhiteSpace(name))
        {
            WriteUnicodeHeader(ms, HeaderName, name);
        }
        WriteByteSequence(ms, HeaderType, Encoding.ASCII.GetBytes(type + "\0"));
        if (appParameters is { Length: > 0 })
        {
            WriteByteSequence(ms, HeaderApplicationParameters, appParameters);
        }
        PatchLength(ms);
        return ms.ToArray();
    }


    public static byte[] Put(uint connectionId, string type, string? name, byte[] body, byte[]? appParameters = null)
    {
        using var ms = new MemoryStream();
        ms.WriteByte(OpPutFinal);
        ms.Write(stackalloc byte[2]);
        WriteUInt32Header(ms, HeaderConnectionId, connectionId);
        if (!string.IsNullOrWhiteSpace(name))
        {
            WriteUnicodeHeader(ms, HeaderName, name);
        }
        WriteByteSequence(ms, HeaderType, Encoding.ASCII.GetBytes(type + "\0"));
        if (appParameters is { Length: > 0 })
        {
            WriteByteSequence(ms, HeaderApplicationParameters, appParameters);
        }
        WriteByteSequence(ms, HeaderEndOfBody, body);
        PatchLength(ms);
        return ms.ToArray();
    }

    public static byte[] ContinueGet(uint connectionId)
    {
        using var ms = new MemoryStream();
        ms.WriteByte(OpGetFinal);
        ms.Write(stackalloc byte[2]);
        WriteUInt32Header(ms, HeaderConnectionId, connectionId);
        PatchLength(ms);
        return ms.ToArray();
    }

    public static byte[] SetPath(uint connectionId, string? folderName, bool backup = false)
    {
        using var ms = new MemoryStream();
        ms.WriteByte(OpSetPath);
        ms.Write(stackalloc byte[2]);
        ms.WriteByte(backup ? (byte)0x03 : (byte)0x02);
        ms.WriteByte(0x00);
        WriteUInt32Header(ms, HeaderConnectionId, connectionId);
        if (folderName is not null)
        {
            WriteUnicodeHeader(ms, HeaderName, folderName);
        }
        PatchLength(ms);
        return ms.ToArray();
    }

    public static byte[] AppParams(params (byte Tag, byte[] Value)[] parameters)
    {
        using var ms = new MemoryStream();
        foreach (var parameter in parameters)
        {
            ms.WriteByte(parameter.Tag);
            ms.WriteByte(checked((byte)parameter.Value.Length));
            ms.Write(parameter.Value);
        }
        return ms.ToArray();
    }

    public static byte[] UInt16Value(ushort value)
    {
        var bytes = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(bytes, value);
        return bytes;
    }

    private static void WriteUInt16(Stream ms, ushort value)
    {
        Span<byte> bytes = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(bytes, value);
        ms.Write(bytes);
    }

    private static void WriteUInt32Header(Stream ms, byte headerId, uint value)
    {
        ms.WriteByte(headerId);
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        ms.Write(bytes);
    }

    private static void WriteByteSequence(Stream ms, byte headerId, ReadOnlySpan<byte> value)
    {
        ms.WriteByte(headerId);
        WriteUInt16(ms, checked((ushort)(value.Length + 3)));
        ms.Write(value);
    }

    private static void WriteUnicodeHeader(Stream ms, byte headerId, string value)
    {
        var bytes = Encoding.BigEndianUnicode.GetBytes(value + "\0");
        WriteByteSequence(ms, headerId, bytes);
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
        for (var i = 0; i < bytes.Length; i++)
        {
            bytes[i] = Convert.ToByte(text.Substring(i * 2, 2), 16);
        }
        return bytes;
    }
}

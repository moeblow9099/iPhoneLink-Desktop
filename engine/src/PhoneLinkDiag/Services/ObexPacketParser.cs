using System.Buffers.Binary;
using System.Text;

namespace PhoneLinkDiag.Services;

public sealed record ObexDecodedPacket(
    byte Opcode,
    string CodeName,
    ushort PacketLength,
    ushort? MaxPacketSize,
    uint? ConnectionId,
    bool AuthenticationRequested,
    string BodyText,
    IReadOnlyList<string> Headers);

public static class ObexPacketParser
{
    public static ObexDecodedPacket Decode(ReadOnlySpan<byte> packet, bool isConnectResponse = false)
    {
        if (packet.Length == 0)
        {
            return new ObexDecodedPacket(0, "Empty", 0, null, null, false, string.Empty, Array.Empty<string>());
        }

        var opcode = packet[0];
        var length = packet.Length >= 3 ? BinaryPrimitives.ReadUInt16BigEndian(packet.Slice(1, 2)) : (ushort)packet.Length;
        ushort? maxPacket = null;
        var offset = 3;
        if (isConnectResponse && packet.Length >= 7)
        {
            maxPacket = BinaryPrimitives.ReadUInt16BigEndian(packet.Slice(5, 2));
            offset = 7;
        }

        var headers = new List<string>();
        var body = new List<byte>();
        uint? connectionId = null;
        var auth = false;

        while (offset < packet.Length)
        {
            var headerId = packet[offset++];
            var kind = headerId & 0xC0;
            if (kind is 0x00 or 0x40)
            {
                if (offset + 2 > packet.Length)
                {
                    headers.Add($"0x{headerId:X2}: truncated length");
                    break;
                }
                var headerLength = BinaryPrimitives.ReadUInt16BigEndian(packet.Slice(offset, 2));
                offset += 2;
                var valueLength = headerLength - 3;
                if (valueLength < 0 || offset + valueLength > packet.Length)
                {
                    headers.Add($"0x{headerId:X2}: invalid length {headerLength}");
                    break;
                }
                var value = packet.Slice(offset, valueLength).ToArray();
                offset += valueLength;
                headers.Add(DecodeVariableHeader(headerId, value));
                if (headerId is ObexMapPacketBuilder.HeaderBody or ObexMapPacketBuilder.HeaderEndOfBody)
                {
                    body.AddRange(value);
                }
                if (headerId == ObexMapPacketBuilder.HeaderAuthenticateChallenge)
                {
                    auth = true;
                }
            }
            else if (kind == 0x80)
            {
                if (offset >= packet.Length)
                {
                    headers.Add($"0x{headerId:X2}: truncated byte value");
                    break;
                }
                headers.Add($"{HeaderName(headerId)}=0x{packet[offset++]:X2}");
            }
            else
            {
                if (offset + 4 > packet.Length)
                {
                    headers.Add($"0x{headerId:X2}: truncated uint32 value");
                    break;
                }
                var value = BinaryPrimitives.ReadUInt32BigEndian(packet.Slice(offset, 4));
                offset += 4;
                headers.Add($"{HeaderName(headerId)}=0x{value:X8}");
                if (headerId == ObexMapPacketBuilder.HeaderConnectionId)
                {
                    connectionId = value;
                }
            }
        }

        return new ObexDecodedPacket(
            opcode,
            ResponseCodeName(opcode),
            length,
            maxPacket,
            connectionId,
            auth,
            DecodeBody(body.ToArray()),
            headers);
    }

    public static string ToSummary(ObexDecodedPacket packet)
    {
        var parts = new List<string>
        {
            $"{packet.CodeName} (0x{packet.Opcode:X2})",
            $"Length={packet.PacketLength}"
        };
        if (packet.MaxPacketSize is not null) parts.Add($"MaxPacket={packet.MaxPacketSize}");
        if (packet.ConnectionId is not null) parts.Add($"ConnectionId=0x{packet.ConnectionId:X8}");
        if (packet.AuthenticationRequested) parts.Add("AuthenticationRequested=True");
        if (packet.Headers.Count > 0) parts.Add("Headers=[" + string.Join("; ", packet.Headers) + "]");
        if (!string.IsNullOrWhiteSpace(packet.BodyText)) parts.Add("Body=" + Truncate(packet.BodyText, 700));
        return string.Join(" | ", parts);
    }

    public static IReadOnlyList<string> ExtractMessageHandles(string bodyText)
    {
        var handles = new List<string>();
        var marker = "handle=\"";
        var index = 0;
        while ((index = bodyText.IndexOf(marker, index, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            index += marker.Length;
            var end = bodyText.IndexOf('"', index);
            if (end < 0) break;
            var handle = bodyText[index..end];
            if (!string.IsNullOrWhiteSpace(handle)) handles.Add(handle);
            index = end + 1;
        }
        return handles.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string DecodeVariableHeader(byte headerId, byte[] value)
    {
        var name = HeaderName(headerId);
        if (headerId == ObexMapPacketBuilder.HeaderName)
        {
            return $"{name}={TrimNull(Encoding.BigEndianUnicode.GetString(value))}";
        }
        if (headerId == ObexMapPacketBuilder.HeaderType)
        {
            return $"{name}={TrimNull(Encoding.ASCII.GetString(value))}";
        }
        if (headerId is ObexMapPacketBuilder.HeaderBody or ObexMapPacketBuilder.HeaderEndOfBody)
        {
            return $"{name}({value.Length} bytes)";
        }
        return $"{name}={HexUtil.ToHex(value)}";
    }

    private static string DecodeBody(byte[] body)
    {
        if (body.Length == 0) return string.Empty;
        return Encoding.UTF8.GetString(body);
    }

    private static string TrimNull(string value)
    {
        return value.TrimEnd('\0');
    }

    private static string HeaderName(byte headerId)
    {
        return headerId switch
        {
            ObexMapPacketBuilder.HeaderName => "Name",
            ObexMapPacketBuilder.HeaderType => "Type",
            ObexMapPacketBuilder.HeaderTarget => "Target",
            ObexMapPacketBuilder.HeaderBody => "Body",
            ObexMapPacketBuilder.HeaderEndOfBody => "EndOfBody",
            ObexMapPacketBuilder.HeaderWho => "Who",
            ObexMapPacketBuilder.HeaderApplicationParameters => "ApplicationParameters",
            ObexMapPacketBuilder.HeaderAuthenticateChallenge => "AuthenticateChallenge",
            ObexMapPacketBuilder.HeaderConnectionId => "ConnectionId",
            _ => $"Header 0x{headerId:X2}"
        };
    }

    private static string ResponseCodeName(byte code)
    {
        return code switch
        {
            0x80 => "OBEX Connect",
            0x83 => "OBEX Get Final",
            0x85 => "OBEX SetPath",
            0x90 => "Continue",
            0xA0 => "Success",
            0xC0 => "Bad Request",
            0xC1 => "Unauthorized",
            0xC3 => "Forbidden",
            0xC4 => "Not Found",
            0xC5 => "Method Not Allowed",
            0xC6 => "Not Acceptable",
            0xD0 => "Internal Server Error",
            0xD3 => "Service Unavailable",
            _ => $"0x{code:X2}"
        };
    }

    private static string Truncate(string value, int max)
    {
        return value.Length <= max ? value : value[..max] + "...";
    }
}

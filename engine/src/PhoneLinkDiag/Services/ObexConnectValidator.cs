using System.Buffers.Binary;
using PhoneLinkDiag.Models;

namespace PhoneLinkDiag.Services;

public static class ObexConnectValidator
{
    public static ObexValidationResult ValidateMapConnect(byte[] packet, Guid expectedTarget)
    {
        var details = new List<string>();
        var errors = new List<string>();

        if (packet.Length < 7)
        {
            errors.Add("Packet is shorter than the 7-byte OBEX CONNECT fixed header.");
            return Result(details, errors);
        }

        var opcode = packet[0];
        var packetLength = BinaryPrimitives.ReadUInt16BigEndian(packet.AsSpan(1, 2));
        var version = packet[3];
        var flags = packet[4];
        var maxPacket = BinaryPrimitives.ReadUInt16BigEndian(packet.AsSpan(5, 2));

        details.Add($"Opcode=0x{opcode:X2} expected 0x80 CONNECT");
        details.Add($"PacketLength={packetLength} actual bytes={packet.Length}");
        details.Add($"Version=0x{version:X2} expected 0x10 (OBEX 1.0)");
        details.Add($"Flags=0x{flags:X2} expected 0x00");
        details.Add($"MaximumPacketSize={maxPacket} encoded big-endian bytes={packet[5]:X2} {packet[6]:X2}");

        if (opcode != 0x80) errors.Add("Opcode is not OBEX CONNECT (0x80).");
        if (packetLength != packet.Length) errors.Add("Packet length field does not match actual byte count.");
        if (version != 0x10) errors.Add("OBEX version is not 1.0.");
        if (flags != 0x00) errors.Add("OBEX CONNECT flags must be 0x00.");
        if (maxPacket < 255) errors.Add("Maximum packet size is below OBEX minimum practical size.");

        var offset = 7;
        var targetSeen = false;
        var applicationParametersSeen = false;
        var connectionIdSeen = false;
        var expectedTargetBytes = GuidToNetworkBytes(expectedTarget);
        var headerIndex = 0;

        while (offset < packet.Length)
        {
            headerIndex++;
            var headerStart = offset;
            var headerId = packet[offset++];
            var kind = headerId & 0xC0;

            if (kind is 0x00 or 0x40)
            {
                if (offset + 2 > packet.Length)
                {
                    errors.Add($"Header {headerIndex} at byte {headerStart}: missing 2-byte big-endian length.");
                    break;
                }

                var headerLength = BinaryPrimitives.ReadUInt16BigEndian(packet.AsSpan(offset, 2));
                offset += 2;
                var valueLength = headerLength - 3;
                details.Add($"Header {headerIndex}: Id=0x{headerId:X2}; Length={headerLength}; ValueLength={valueLength}; LengthBytes={packet[offset - 2]:X2} {packet[offset - 1]:X2}");

                if (valueLength < 0 || offset + valueLength > packet.Length)
                {
                    errors.Add($"Header {headerIndex} length runs past packet end.");
                    break;
                }

                var value = packet.AsSpan(offset, valueLength).ToArray();
                offset += valueLength;

                if (headerId == ObexMapPacketBuilder.HeaderTarget)
                {
                    targetSeen = true;
                    details.Add($"Target={HexUtil.ToHex(value)} expected={HexUtil.ToHex(expectedTargetBytes)}");
                    if (valueLength != 16) errors.Add("Target header value must be exactly 16 bytes.");
                    if (!value.SequenceEqual(expectedTargetBytes)) errors.Add("Target header does not match MAP MAS OBEX target UUID.");
                    if (headerIndex != 1) errors.Add("Target header should be the first header in this CONNECT request.");
                }
                else if (headerId == ObexMapPacketBuilder.HeaderApplicationParameters)
                {
                    applicationParametersSeen = true;
                    details.Add("ApplicationParameters present; allowed for MAP Connect when carrying MapSupportedFeatures per MAP 6.4.1 C.1.");
                    ValidateMapSupportedFeatures(value, errors, details);
                    if (!targetSeen) errors.Add("Application Parameters must not precede the Target header in this CONNECT request.");
                }
            }
            else if (kind == 0x80)
            {
                if (offset >= packet.Length)
                {
                    errors.Add($"Header {headerIndex} at byte {headerStart}: missing one-byte value.");
                    break;
                }
                details.Add($"Header {headerIndex}: Id=0x{headerId:X2}; ByteValue=0x{packet[offset]:X2}");
                offset++;
            }
            else
            {
                if (offset + 4 > packet.Length)
                {
                    errors.Add($"Header {headerIndex} at byte {headerStart}: missing 4-byte big-endian value.");
                    break;
                }
                var value = BinaryPrimitives.ReadUInt32BigEndian(packet.AsSpan(offset, 4));
                details.Add($"Header {headerIndex}: Id=0x{headerId:X2}; UInt32=0x{value:X8}; Bytes={packet[offset]:X2} {packet[offset + 1]:X2} {packet[offset + 2]:X2} {packet[offset + 3]:X2}");
                if (headerId == ObexMapPacketBuilder.HeaderConnectionId)
                {
                    connectionIdSeen = true;
                }
                offset += 4;
            }
        }

        if (!targetSeen) errors.Add("Target header is missing.");
        details.Add(connectionIdSeen
            ? "Connection ID header is present; initial OBEX CONNECT normally should not include one."
            : "Connection ID header is absent; this is expected for initial OBEX CONNECT.");
        details.Add(applicationParametersSeen
            ? "MAP Application Parameters header is present."
            : "MAP Application Parameters header is absent.");
        if (connectionIdSeen) errors.Add("Connection ID should not be sent in the initial OBEX CONNECT request.");
        if (offset != packet.Length) errors.Add("Header parsing did not end exactly at packet length.");

        return Result(details, errors);
    }

    private static void ValidateMapSupportedFeatures(byte[] value, List<string> errors, List<string> details)
    {
        var offset = 0;
        var found = false;
        while (offset < value.Length)
        {
            if (offset + 2 > value.Length)
            {
                errors.Add("Application Parameters TLV is truncated before tag/length.");
                return;
            }

            var tag = value[offset++];
            var length = value[offset++];
            if (offset + length > value.Length)
            {
                errors.Add($"Application Parameters tag 0x{tag:X2} length exceeds header value.");
                return;
            }

            if (tag == 0x29)
            {
                found = true;
                details.Add($"MapSupportedFeatures tag=0x29 length={length} value={HexUtil.ToHex(value.AsSpan(offset, length).ToArray())}");
                if (length != 4)
                {
                    errors.Add("MapSupportedFeatures Application Parameter must be 4 bytes.");
                }
            }

            offset += length;
        }

        if (!found)
        {
            errors.Add("Application Parameters header is present but does not contain MapSupportedFeatures tag 0x29.");
        }
    }

    public static string DescribeConnectRequest(byte[] packet, Guid expectedTarget)
    {
        var validation = ValidateMapConnect(packet, expectedTarget);
        return validation.Summary + " | " + string.Join("; ", validation.Details);
    }

    private static ObexValidationResult Result(IReadOnlyList<string> details, IReadOnlyList<string> errors)
    {
        var valid = errors.Count == 0;
        var summary = valid
            ? "CONNECT packet validation passed against OBEX/MAP fixed fields and MAP Target header."
            : "CONNECT packet validation failed: " + string.Join("; ", errors);
        return new ObexValidationResult(valid, summary, details, errors);
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

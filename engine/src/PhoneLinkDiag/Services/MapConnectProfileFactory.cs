using PhoneLinkDiag.Models;

namespace PhoneLinkDiag.Services;

public static class MapConnectProfileFactory
{
    private const uint ReadOnlyBrowsingAndInstanceInfo = 0x00000024;

    public static IReadOnlyList<ConnectProfile> CreateProfiles()
    {
        return new[]
        {
            Create(1, "A - MAP Target, MaxPacket 65535", "MAP 6.4.1 Table 6.9: Target header mandatory; OBEX max packet length varies.", 0xFFFF, false, 0),
            Create(2, "B - MAP Target, MaxPacket 8192", "OBEX CONNECT negotiates maximum packet length; 8192 is a legal smaller value.", 8192, false, 0),
            Create(3, "C - MAP Target, MaxPacket 1024", "OBEX CONNECT negotiates maximum packet length; 1024 remains above OBEX 255-byte default/minimum guidance.", 1024, false, 0),
            Create(4, "D - MAP Target + MapSupportedFeatures, MaxPacket 8192", "MAP 6.4.1 C.1 allows Application Parameters MapSupportedFeatures when required by SDP feature bit 19.", 8192, true, ReadOnlyBrowsingAndInstanceInfo),
            Create(5, "E - MAP Target + MapSupportedFeatures, MaxPacket 65535", "Same MAP 6.4.1 C.1 form with the current maximum packet length.", 0xFFFF, true, ReadOnlyBrowsingAndInstanceInfo)
        };
    }

    private static ConnectProfile Create(int attempt, string name, string basis, ushort maxPacket, bool includeFeatures, uint features)
    {
        var appParams = includeFeatures ? ObexMapPacketBuilder.MapSupportedFeatures(features) : null;
        var packet = ObexMapPacketBuilder.Connect(BluetoothServiceIds.ObexMapMasTarget, maxPacket, appParams);
        var validation = ObexConnectValidator.ValidateMapConnect(packet, BluetoothServiceIds.ObexMapMasTarget);
        return new ConnectProfile(attempt, name, basis, maxPacket, includeFeatures, features, packet, validation);
    }
}

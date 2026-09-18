namespace PhoneLinkDiag.Services;

public static class BluetoothServiceIds
{
    public static readonly Guid MapMessageAccessServer = FromShortId(0x1132);
    public static readonly Guid MapMessageNotificationServer = FromShortId(0x1133);
    public static readonly Guid PbapPhoneBookServer = FromShortId(0x112F);
    public static readonly Guid PbapPhoneBookClient = FromShortId(0x1130);
    public static readonly Guid HandsFree = FromShortId(0x111E);
    public static readonly Guid HandsFreeAudioGateway = FromShortId(0x111F);
    public static readonly Guid HeadsetAudioGateway = FromShortId(0x1112);
    public static readonly Guid AdvancedAudioDistributionSource = FromShortId(0x110A);
    public static readonly Guid AdvancedAudioDistributionSink = FromShortId(0x110B);
    public static readonly Guid AvrcpTarget = FromShortId(0x110C);
    public static readonly Guid AvrcpController = FromShortId(0x110E);

    public static readonly Guid ObexMapMasTarget = Guid.Parse("BB582B40-420C-11DB-B0DE-0800200C9A66");
    public static readonly Guid ObexPbapPseTarget = Guid.Parse("796135F0-F0C5-11D8-0966-0800200C9A66");

    public static Guid FromShortId(ushort shortId)
    {
        return Guid.Parse($"0000{shortId:X4}-0000-1000-8000-00805F9B34FB");
    }

    public static string IdentifyProfile(Guid uuid)
    {
        if (uuid == MapMessageAccessServer) return "MAP";
        if (uuid == MapMessageNotificationServer) return "MAP";
        if (uuid == PbapPhoneBookServer) return "PBAP";
        if (uuid == PbapPhoneBookClient) return "PBAP";
        if (uuid == HandsFree) return "HFP";
        if (uuid == HandsFreeAudioGateway) return "HFP";
        if (uuid == AdvancedAudioDistributionSource) return "A2DP";
        if (uuid == AdvancedAudioDistributionSink) return "A2DP";
        if (uuid == AvrcpTarget) return "AVRCP";
        if (uuid == AvrcpController) return "AVRCP";
        return "Unknown";
    }
}

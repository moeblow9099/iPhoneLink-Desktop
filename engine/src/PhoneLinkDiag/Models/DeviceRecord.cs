namespace PhoneLinkDiag.Models;

public sealed record DeviceRecord(
    string Id,
    string Name,
    bool IsPaired,
    bool CanPair,
    string BluetoothConnectionStatus,
    bool? BluetoothDeviceIsPaired,
    string? DeviceClass,
    string? BluetoothAddress,
    IReadOnlyDictionary<string, object?> Properties)
{
    public bool EffectiveIsPaired => IsPaired || BluetoothDeviceIsPaired == true;

    public bool HasPairingMismatch =>
        (!IsPaired && BluetoothDeviceIsPaired == true) ||
        (!IsPaired && string.Equals(BluetoothConnectionStatus, "Connected", StringComparison.OrdinalIgnoreCase));

    public override string ToString()
    {
        var btPaired = BluetoothDeviceIsPaired?.ToString() ?? "Unknown";
        var warning = HasPairingMismatch ? " | MISMATCH" : string.Empty;
        return $"{Name} | DI Paired={IsPaired} | BT Paired={btPaired} | {BluetoothConnectionStatus}{warning}";
    }
}

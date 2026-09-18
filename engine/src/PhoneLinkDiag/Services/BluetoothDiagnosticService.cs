using System.Text.Json;
using PhoneLinkDiag.Models;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Rfcomm;
using Windows.Devices.Enumeration;

namespace PhoneLinkDiag.Services;

public sealed class BluetoothDiagnosticService : IDisposable
{
    private readonly DiagnosticLogger _log;
    private readonly Dictionary<string, DeviceRecord> _devices = new();
    private readonly object _deviceGate = new();
    private DeviceWatcher? _watcher;
    private bool _disposed;

    public event Action? DeviceListChanged;

    public BluetoothDiagnosticService(DiagnosticLogger log)
    {
        _log = log;
    }

    public bool IsScanning
    {
        get
        {
            var watcher = _watcher;
            return watcher is not null &&
                   watcher.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted;
        }
    }

    public IReadOnlyList<DeviceRecord> CurrentDevices
    {
        get
        {
            lock (_deviceGate)
            {
                return _devices.Values.OrderBy(d => d.Name).ThenBy(d => d.Id).ToArray();
            }
        }
    }

    public async Task StartScanAsync(CancellationToken ct)
    {
        ThrowIfDisposed();
        StopScan();
        lock (_deviceGate) _devices.Clear();
        DeviceListChanged?.Invoke();

        if (!await EnsureBluetoothAdapterAsync(ct)) return;

        _log.Info("Bluetooth", "Starting classic Bluetooth device watcher.");
        var selector = BluetoothDevice.GetDeviceSelector();
        _watcher = DeviceInformation.CreateWatcher(selector);
        _watcher.Added += (watcher, info) => { var ignored = UpsertAsync(info, "added", CancellationToken.None); };
        _watcher.Updated += (watcher, update) => { var ignored = HandleUpdateAsync(update); };
        _watcher.Removed += (_, update) =>
        {
            DeviceRecord? removed = null;
            lock (_deviceGate)
            {
                _devices.Remove(update.Id, out removed);
            }
            if (removed is not null)
            {
                _log.Info("Bluetooth", $"Device removed: {removed.Name}");
                DeviceListChanged?.Invoke();
            }
        };
        _watcher.EnumerationCompleted += (_, _) => _log.Info("Bluetooth", "Initial enumeration complete.");
        _watcher.Stopped += (_, _) => _log.Info("Bluetooth", "Device watcher stopped.");
        _watcher.Start();
    }

    public async Task RefreshPairedDevicesAsync(CancellationToken ct)
    {
        ThrowIfDisposed();
        StopScan();

        if (!await EnsureBluetoothAdapterAsync(ct))
        {
            lock (_deviceGate) _devices.Clear();
            DeviceListChanged?.Invoke();
            return;
        }

        _log.Info("Bluetooth", "Refreshing already-paired classic Bluetooth devices.");
        var selector = BluetoothDevice.GetDeviceSelectorFromPairingState(true);
        var devices = await DeviceInformation.FindAllAsync(selector).AsTask(ct);
        var refreshed = new Dictionary<string, DeviceRecord>(StringComparer.OrdinalIgnoreCase);

        if (devices.Count == 0)
        {
            _log.Warn("Bluetooth", "No paired Bluetooth devices returned by Windows.");
        }

        foreach (var info in devices)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var record = await BuildRecordAsync(info, ct);
                refreshed[record.Id] = record;
                LogDeviceRecord(record, "paired refresh");
            }
            catch (Exception ex)
            {
                _log.Error("Bluetooth", $"Device paired refresh failed for {info.Name}: {ex.Message}");
            }
        }

        lock (_deviceGate)
        {
            _devices.Clear();
            foreach (var pair in refreshed) _devices[pair.Key] = pair.Value;
        }

        DeviceListChanged?.Invoke();
        _log.Info("Bluetooth", $"Paired-device refresh complete. Count={refreshed.Count}.");
    }

    public void StopScan()
    {
        if (_watcher is null) return;
        try
        {
            if (_watcher.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted)
            {
                _watcher.Stop();
            }
        }
        catch (Exception ex)
        {
            _log.Warn("Bluetooth", $"Stopping watcher produced warning: {ex.Message}");
        }
        finally
        {
            _watcher = null;
        }
    }

    public async Task<IReadOnlyList<ProfileProbe>> ProbeProfilesAsync(DeviceRecord record, CancellationToken ct)
    {
        ThrowIfDisposed();
        _log.Info("Bluetooth", $"Probing known profiles for {record.Name}.");

        using var device = await OpenBluetoothDeviceAsync(record, ct);
        if (device is null)
        {
            return new[]
            {
                new ProfileProbe("Device", "BluetoothDevice", Guid.Empty, false, "Could not open BluetoothDevice from selected DeviceId.")
            };
        }

        if (!record.EffectiveIsPaired)
        {
            _log.Warn("Bluetooth", $"Device not paired: {record.Name}. Pair it in Windows Settings before profile probing.");
        }

        var probes = new List<(string Name, string Role, Guid Uuid)>
        {
            ("MAP", "Message Access Server / MAS", BluetoothServiceIds.MapMessageAccessServer),
            ("MAP", "Message Notification Server / MNS", BluetoothServiceIds.MapMessageNotificationServer),
            ("PBAP", "Phone Book Server / PSE", BluetoothServiceIds.PbapPhoneBookServer),
            ("PBAP", "Phone Book Client", BluetoothServiceIds.PbapPhoneBookClient),
            ("HFP", "Hands-Free", BluetoothServiceIds.HandsFree),
            ("HFP", "Hands-Free Audio Gateway", BluetoothServiceIds.HandsFreeAudioGateway),
            ("A2DP", "Audio Source", BluetoothServiceIds.AdvancedAudioDistributionSource),
            ("A2DP", "Audio Sink", BluetoothServiceIds.AdvancedAudioDistributionSink),
            ("AVRCP", "Remote Control Target", BluetoothServiceIds.AvrcpTarget),
            ("AVRCP", "Remote Control Controller", BluetoothServiceIds.AvrcpController)
        };

        var results = new List<ProfileProbe>();
        foreach (var probe in probes)
        {
            ct.ThrowIfCancellationRequested();
            results.Add(await ProbeRfcommServiceAsync(device, record, probe.Name, probe.Role, probe.Uuid, ct));
        }

        return results;
    }

    public async Task<RfcommDeviceService?> GetFirstRfcommServiceAsync(
        DeviceRecord record,
        Guid serviceUuid,
        CancellationToken ct,
        BluetoothCacheMode cacheMode = BluetoothCacheMode.Uncached)
    {
        using var device = await OpenBluetoothDeviceAsync(record, ct);
        if (device is null) return null;

        var serviceId = RfcommServiceId.FromUuid(serviceUuid);
        var result = await device.GetRfcommServicesForIdAsync(serviceId, cacheMode).AsTask(ct);
        if (result.Error != BluetoothError.Success)
        {
            LogRfcommError(record, result.Error);
            return null;
        }

        var selected = result.Services.FirstOrDefault();
        foreach (var extra in result.Services.Skip(1))
        {
            extra.Dispose();
        }
        return selected;
    }

    public async Task<IReadOnlyList<ProfileProbe>> ListAllRfcommServicesAsync(DeviceRecord record, CancellationToken ct)
    {
        using var device = await OpenBluetoothDeviceAsync(record, ct);
        if (device is null) return Array.Empty<ProfileProbe>();

        try
        {
            var result = await device.GetRfcommServicesAsync(BluetoothCacheMode.Uncached).AsTask(ct);
            if (result.Error != BluetoothError.Success)
            {
                LogRfcommError(record, result.Error);
                return Array.Empty<ProfileProbe>();
            }

            var services = new List<ProfileProbe>();
            foreach (var service in result.Services)
            {
                using (service)
                {
                    var uuid = service.ServiceId.Uuid;
                    var profile = BluetoothServiceIds.IdentifyProfile(uuid);
                    var role = $"{service.ConnectionServiceName} | {service.ConnectionHostName?.DisplayName}";
                    services.Add(new ProfileProbe(profile, role, uuid, true, "Advertised RFCOMM service."));
                }
            }
            var orderedServices = services
                .OrderBy(service => service.ProfileName)
                .ThenBy(service => service.ServiceUuid)
                .ToArray();

            if (orderedServices.Length == 0)
            {
                _log.Warn("RFCOMM", "No RFCOMM services were exposed by the selected device in the current connection.");
                if (IsIPhone(record) && device.ConnectionStatus == BluetoothConnectionStatus.Connected)
                {
                    _log.Warn("RFCOMM", "iPhone is connected, but no usable RFCOMM profiles were exposed in this query.");
                }
            }

            foreach (var service in orderedServices)
            {
                _log.Info("RFCOMM", $"{service.ProfileName} | UUID={service.ServiceUuid} | {service.ServiceRole}");
            }

            return orderedServices;
        }
        catch (UnauthorizedAccessException ex)
        {
            _log.Warn("RFCOMM", $"Services blocked by Windows permissions: {ex.Message}");
            return Array.Empty<ProfileProbe>();
        }
    }

    public string DumpDeviceProperties(DeviceRecord record)
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "device-dumps");
        Directory.CreateDirectory(folder);
        var safeName = string.Join("_", record.Name.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
        if (string.IsNullOrWhiteSpace(safeName)) safeName = "bluetooth-device";
        var path = Path.Combine(folder, $"{safeName}-{DateTime.Now:yyyyMMdd-HHmmss}.json");

        var payload = new
        {
            record.Name,
            record.Id,
            record.IsPaired,
            record.CanPair,
            record.BluetoothConnectionStatus,
            record.BluetoothDeviceIsPaired,
            record.DeviceClass,
            record.BluetoothAddress,
            Properties = record.Properties
                .OrderBy(p => p.Key)
                .Select(p => new
                {
                    Name = p.Key,
                    Type = p.Value?.GetType().FullName,
                    Value = FormatPropertyValue(p.Value)
                })
        };

        File.WriteAllText(path, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
        _log.Info("Bluetooth", $"Dumped device properties to {path}");
        return path;
    }

    private async Task HandleUpdateAsync(DeviceInformationUpdate update)
    {
        try
        {
            var info = await DeviceInformation.CreateFromIdAsync(update.Id).AsTask();
            await UpsertAsync(info, "updated", CancellationToken.None);
        }
        catch (Exception ex)
        {
            _log.Warn("Bluetooth", $"Device update refresh failed: {ex.Message}");
        }
    }

    private async Task UpsertAsync(DeviceInformation info, string reason, CancellationToken ct)
    {
        try
        {
            var record = await BuildRecordAsync(info, ct);
            lock (_deviceGate) _devices[record.Id] = record;
            LogDeviceRecord(record, reason);
            DeviceListChanged?.Invoke();
        }
        catch (Exception ex)
        {
            _log.Error("Bluetooth", $"Device {reason} failed: {ex.Message}");
        }
    }

    private async Task<DeviceRecord> BuildRecordAsync(DeviceInformation info, CancellationToken ct)
    {
        var properties = new Dictionary<string, object?>();
        foreach (var property in info.Properties)
        {
            properties[property.Key] = property.Value;
        }

        BluetoothDevice? device = null;
        try
        {
            device = await BluetoothDevice.FromIdAsync(info.Id).AsTask(ct);
        }
        catch (Exception ex)
        {
            _log.Warn("Bluetooth", $"BluetoothDevice.FromIdAsync failed for {info.Name}: {ex.Message}");
        }

        using (device)
        {
            var name = string.IsNullOrWhiteSpace(info.Name) ? "Unnamed Bluetooth Device" : info.Name;
            var diPaired = info.Pairing?.IsPaired ?? GetBool(properties, "System.Devices.Aep.IsPaired") ?? false;
            var canPair = info.Pairing?.CanPair ?? GetBool(properties, "System.Devices.Aep.CanPair") ?? false;
            var btPaired = device?.DeviceInformation?.Pairing?.IsPaired;
            var status = device?.ConnectionStatus.ToString() ?? "Unavailable";
            var address = device is null ? GetString(properties, "System.Devices.Aep.DeviceAddress") : FormatBluetoothAddress(device.BluetoothAddress);
            var deviceClass = $"DeviceInformationKind={info.Kind}; InterfaceClassGuid={GetString(properties, "System.Devices.InterfaceClassGuid") ?? "Unknown"}";

            return new DeviceRecord(info.Id, name, diPaired, canPair, status, btPaired, deviceClass, address, properties);
        }
    }

    private async Task<ProfileProbe> ProbeRfcommServiceAsync(BluetoothDevice device, DeviceRecord record, string name, string role, Guid uuid, CancellationToken ct)
    {
        try
        {
            var serviceId = RfcommServiceId.FromUuid(uuid);
            var result = await device.GetRfcommServicesForIdAsync(serviceId, BluetoothCacheMode.Uncached).AsTask(ct);
            if (result.Error != BluetoothError.Success)
            {
                LogRfcommError(record, result.Error);
                return new ProfileProbe(name, role, uuid, false, $"RFCOMM query failed: {result.Error}");
            }

            using var service = result.Services.FirstOrDefault();
            foreach (var extra in result.Services.Skip(1))
            {
                extra.Dispose();
            }
            if (service is null)
            {
                _log.Info("Profile", $"{name} {role}: not found ({uuid}).");
                return new ProfileProbe(name, role, uuid, false, "Not advertised by selected device.");
            }

            var status = $"Found RFCOMM service. ServiceName={service.ConnectionServiceName}";
            _log.Info("Profile", $"{name} {role}: {status}");
            return new ProfileProbe(name, role, uuid, true, status, service.ConnectionServiceName, service.ConnectionHostName?.DisplayName);
        }
        catch (UnauthorizedAccessException ex)
        {
            _log.Warn("Profile", $"Services blocked by Windows permissions: {ex.Message}");
            return new ProfileProbe(name, role, uuid, false, "Blocked by Windows permissions.", Error: ex.ToString());
        }
        catch (Exception ex)
        {
            _log.Error("Profile", $"{name} {role} probe failed: {ex.Message}");
            return new ProfileProbe(name, role, uuid, false, "Probe failed", Error: ex.ToString());
        }
    }

    private async Task<BluetoothDevice?> OpenBluetoothDeviceAsync(DeviceRecord record, CancellationToken ct)
    {
        var device = await BluetoothDevice.FromIdAsync(record.Id).AsTask(ct);
        if (device is null)
        {
            _log.Warn("Bluetooth", "Windows returned no BluetoothDevice. Check pairing/trust state.");
        }

        return device;
    }

    private async Task<bool> EnsureBluetoothAdapterAsync(CancellationToken ct)
    {
        var adapter = await BluetoothAdapter.GetDefaultAsync().AsTask(ct);
        if (adapter is null)
        {
            _log.Error("Bluetooth", "Bluetooth adapter missing or unavailable. Enable Bluetooth or attach an adapter.");
            return false;
        }

        return true;
    }

    private void LogDeviceRecord(DeviceRecord record, string reason)
    {
        _log.Info("Bluetooth", $"Device {reason}: Name={record.Name} | Id={record.Id} | IsPaired={record.IsPaired} | CanPair={record.CanPair} | BT.ConnectionStatus={record.BluetoothConnectionStatus} | BT.Pairing.IsPaired={record.BluetoothDeviceIsPaired?.ToString() ?? "Unknown"} | DeviceClass={record.DeviceClass ?? "Unknown"} | BluetoothAddress={record.BluetoothAddress ?? "Unknown"}");

        foreach (var property in record.Properties.OrderBy(p => p.Key))
        {
            _log.Debug("DeviceProperty", $"{record.Name} | {property.Key} = {FormatPropertyValue(property.Value)}");
        }

        if (record.HasPairingMismatch)
        {
            _log.Warn("Bluetooth", $"Pairing mismatch: Windows Bluetooth shows {record.BluetoothConnectionStatus}/BT paired={record.BluetoothDeviceIsPaired}, but DeviceInformation.IsPaired={record.IsPaired}.");
        }

        if (!record.EffectiveIsPaired)
        {
            _log.Warn("Bluetooth", $"Device not paired: {record.Name}.");
        }

        var present = GetBool(record.Properties, "System.Devices.Aep.IsPresent") ?? GetBool(record.Properties, "System.Devices.IsPresent");
        if (present == true && !record.CanPair && !record.EffectiveIsPaired && record.BluetoothConnectionStatus != "Connected")
        {
            _log.Warn("Bluetooth", $"Device visible but not connectable: {record.Name}.");
        }
    }

    private void LogRfcommError(DeviceRecord record, BluetoothError error)
    {
        if (error == BluetoothError.Success) return;
        var message = error == BluetoothError.DeviceNotConnected
            ? "Device visible but not connectable or not connected."
            : error.ToString();

        _log.Warn("RFCOMM", $"{record.Name}: {message}");
        if (error.ToString().Contains("Access", StringComparison.OrdinalIgnoreCase))
        {
            _log.Warn("RFCOMM", "Services blocked by Windows permissions.");
        }

        if (IsIPhone(record) && record.BluetoothConnectionStatus == "Connected")
        {
            _log.Warn("RFCOMM", "iPhone is connected, but the requested RFCOMM profile is unavailable in the current connection.");
        }
    }

    private static bool IsIPhone(DeviceRecord record)
    {
        return record.Name.Contains("iPhone", StringComparison.OrdinalIgnoreCase);
    }

    private static bool? GetBool(IReadOnlyDictionary<string, object?> properties, string key)
    {
        return properties.TryGetValue(key, out var value) && value is bool boolValue ? boolValue : null;
    }

    private static string? GetString(IReadOnlyDictionary<string, object?> properties, string key)
    {
        return properties.TryGetValue(key, out var value) ? value?.ToString() : null;
    }

    private static string FormatBluetoothAddress(ulong address)
    {
        return string.Join(":", Enumerable.Range(0, 6).Reverse().Select(i => ((address >> (i * 8)) & 0xFF).ToString("X2")));
    }

    private static string FormatPropertyValue(object? value)
    {
        if (value is null) return "<null>";
        if (value is string text) return text;
        if (value is byte[] bytes) return Convert.ToHexString(bytes);
        if (value is System.Collections.IEnumerable items && value is not string)
        {
            var values = new List<string>();
            foreach (var item in items)
            {
                values.Add(item?.ToString() ?? "<null>");
            }
            return string.Join(", ", values);
        }

        return value.ToString() ?? string.Empty;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(BluetoothDiagnosticService));
    }

    public void Dispose()
    {
        if (_disposed) return;
        StopScan();
        _disposed = true;
    }
}

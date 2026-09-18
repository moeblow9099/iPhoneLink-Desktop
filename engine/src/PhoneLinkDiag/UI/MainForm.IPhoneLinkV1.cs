using PhoneLinkDiag.Models;
using PhoneLinkDiag.Services;
using System.Text.Json;

namespace PhoneLinkDiag.UI;

public sealed partial class MainForm
{
    private IPhoneLinkBridgeServer? _bridgeServer;
    private int _roundRobinCursor;
    private readonly Dictionary<string, int> _deviceSendCounts = new(StringComparer.OrdinalIgnoreCase);

    private void InitializeIPhoneLinkV1()
    {
        Text = "iPhoneLink CRM";
        _logger.Info("APP", "iPhoneLink CRM started. Premium CRM shell with SMS, dialer, leads, local bridge. No WhatsApp/consent module in this build.");
        _logger.Info("APP", "Core modes: standalone inbox/reply, local CRM bridge, manual/round-robin sender selection, iPhone HFP dial commands.");
        TryStartCrmBridge();
    }

    private void TryStartCrmBridge()
    {
        try
        {
            _bridgeServer = new IPhoneLinkBridgeServer(_logger);
            _bridgeServer.HealthAsync = () => BridgeOnUiAsync(() => (object)new
            {
                ok = true,
                app = "iPhoneLink CRM",
                bridge = _bridgeServer?.Url,
                mapConnected = _mapSession.ObexConnected,
                rfcommConnected = _mapSession.RfcommConnected,
                inboxCached = _currentInboxMessages.Count,
                authRequired = true,
                tokenFile = _bridgeServer?.TokenFilePath,
                noWhatsApp = true,
                noConsentModule = true
            });
            _bridgeServer.DevicesAsync = () => BridgeOnUiAsync(BridgeDevicesSnapshot);
            _bridgeServer.ContactsAsync = () => BridgeOnUiAsync(() => (object)new
            {
                ok = true,
                count = _currentContacts.Count,
                contacts = _currentContacts.Select(contact => new
                {
                    contact.DeviceId,
                    contact.DeviceName,
                    contact.Name,
                    contact.Phones,
                    contact.Emails,
                    contact.Organization
                }).ToArray()
            });
            _bridgeServer.CallsAsync = () => BridgeOnUiAsync(() => (object)new
            {
                ok = true,
                count = _currentCallHistory.Count,
                calls = _currentCallHistory.ToArray()
            });
            _bridgeServer.MessagesAsync = limit => BridgeOnUiAsync(() => (object)new
            {
                ok = true,
                count = _currentInboxMessages.Take(limit).Count(),
                messages = _currentInboxMessages.Take(limit).Select(ToBridgeMessage).ToArray()
            });
            _bridgeServer.RefreshInboxAsync = limit => BridgeOnUiAsync(async () =>
            {
                SetReadCountForBridge(limit);
                await ReadInboxFastAsync(includeProfileCheck: true, usePagedListing: true);
                return (object)new
                {
                    ok = true,
                    count = _currentInboxMessages.Count,
                    messages = _currentInboxMessages.Take(limit).Select(ToBridgeMessage).ToArray()
                };
            });
            _bridgeServer.SendSmsAsync = (phone, message, deviceMode) => BridgeOnUiAsync(async () => await BridgeSendSmsCoreAsync(phone, message, deviceMode));
            _bridgeServer.CallAsync = (phone, deviceMode) => BridgeOnUiAsync(async () => await BridgeCallCoreAsync(phone, deviceMode));
            _bridgeServer.Start();
            _statusLabel.Text = "CRM bridge running at http://127.0.0.1:8765/ with token protection. Use the local token file for protected API requests.";
        }
        catch (Exception ex)
        {
            _logger.Warn("CRM", $"Could not start local CRM bridge: {ex.Message}");
            _statusLabel.Text = "CRM bridge did not start. Standalone SMS/inbox still works.";
        }
    }

    private Task<T> BridgeOnUiAsync<T>(Func<T> action)
    {
        if (IsDisposed || Disposing || !IsHandleCreated)
            return Task.FromException<T>(new ObjectDisposedException(nameof(MainForm)));

        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            BeginInvoke(new Action(() =>
            {
                try { tcs.TrySetResult(action()); }
                catch (Exception ex) { tcs.TrySetException(ex); }
            }));
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
        {
            tcs.TrySetException(new ObjectDisposedException(nameof(MainForm), ex.Message));
        }
        return tcs.Task;
    }

    private Task<T> BridgeOnUiAsync<T>(Func<Task<T>> action)
    {
        if (IsDisposed || Disposing || !IsHandleCreated)
            return Task.FromException<T>(new ObjectDisposedException(nameof(MainForm)));

        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            BeginInvoke(new Action(async () =>
            {
                try { tcs.TrySetResult(await action()); }
                catch (Exception ex) { tcs.TrySetException(ex); }
            }));
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
        {
            tcs.TrySetException(new ObjectDisposedException(nameof(MainForm), ex.Message));
        }
        return tcs.Task;
    }

    private object BridgeDevicesSnapshot()
    {
        var devices = _bluetooth.CurrentDevices
            .Where(IsLikelyMobilePhone)
            .Select((d, index) => new
            {
                index,
                d.Name,
                d.Id,
                d.BluetoothAddress,
                d.BluetoothConnectionStatus,
                d.EffectiveIsPaired,
                selected = CurrentSelectedDevice()?.Id == d.Id,
                sendCount = _deviceSendCounts.GetValueOrDefault(d.Id)
            })
            .ToArray();
        return new
        {
            ok = true,
            bridge = _bridgeServer?.Url,
            deviceCount = devices.Length,
            devices,
            roundRobinCursor = _roundRobinCursor
        };
    }

    private async Task<object> BridgeSendSmsCoreAsync(string phone, string message, string? deviceMode)
    {
        if (string.IsNullOrWhiteSpace(phone)) return new { ok = false, error = "phone is required" };
        if (string.IsNullOrWhiteSpace(message)) return new { ok = false, error = "message is required" };
        var device = await SelectBridgeDeviceAsync(deviceMode);
        if (device is null) return new { ok = false, error = "No connected mobile phone device available." };

        await _mapWorkflowGate.WaitAsync(_appCts.Token);
        try
        {
            await EnsureMapSessionForDeviceAsync(device);
            var packet = await _mapSession.PushSmsAsync(phone, message, _appCts.Token);
            _deviceSendCounts[device.Id] = _deviceSendCounts.GetValueOrDefault(device.Id) + 1;
            return new
            {
                ok = packet.Success,
                acceptedByIPhone = packet.Success,
                device = device.Name,
                deviceId = device.Id,
                phoneMasked = MaskBridgePhone(phone),
                status = packet.Status,
                sentCountForDevice = _deviceSendCounts[device.Id]
            };
        }
        finally
        {
            _mapWorkflowGate.Release();
        }
    }

    private async Task<object> BridgeCallCoreAsync(string phone, string? deviceMode)
    {
        if (string.IsNullOrWhiteSpace(phone)) return new { ok = false, error = "phone is required" };
        var device = await SelectBridgeDeviceAsync(deviceMode);
        if (device is null) return new { ok = false, error = "No mobile phone device available." };
        if (!_hfpDialer.IsConnected || !string.Equals(_hfpDialer.ConnectedDeviceId, device.Id, StringComparison.OrdinalIgnoreCase))
        {
            await _hfpDialer.ConnectAsync(device, _appCts.Token);
            await _hfpDialer.InitializeAsync(_appCts.Token);
        }
        var response = await _hfpDialer.DialAsync(phone, _appCts.Token);
        if (IsExplicitHfpCallFailure(response))
        {
            _logger.Warn("HFP", $"Bridge dial returned an explicit failure response: {ToDiagnosticText(response)}");
            AddUserNotification("Call Failed", $"{FormatPhoneDisplay(phone)} · {device.Name}", $"bridge-call-fail:{device.Id}:{NormalizePhoneForAction(phone)}");
            return new
            {
                ok = false,
                device = device.Name,
                deviceId = device.Id,
                phoneMasked = MaskBridgePhone(phone),
                status = "Call Failed",
                error = "The phone reported that the call could not be started. See Developer Diagnostics."
            };
        }

        RecordCallStarted(phone, device);
        return new
        {
            ok = true,
            device = device.Name,
            deviceId = device.Id,
            phoneMasked = MaskBridgePhone(phone),
            status = "Call Started"
        };
    }

    private async Task<DeviceRecord?> SelectBridgeDeviceAsync(string? mode)
    {
        if (!_bluetooth.CurrentDevices.Any())
        {
            await _bluetooth.RefreshPairedDevicesAsync(_appCts.Token);
            RefreshDevices();
        }

        var allPhones = _bluetooth.CurrentDevices
            .Where(IsLikelyMobilePhone)
            .OrderBy(d => d.Name)
            .ThenBy(d => d.Id)
            .ToArray();
        var phoneDevices = allPhones.Where(IsConnectedDevice).ToArray();
        if (phoneDevices.Length == 0)
        {
            var selected = CurrentSelectedDevice();
            if (selected is not null && IsLikelyMobilePhone(selected) && IsConnectedDevice(selected)) return selected;
            return null;
        }

        DeviceRecord chosen;
        if (string.Equals(mode, "roundrobin", StringComparison.OrdinalIgnoreCase) || string.Equals(mode, "round-robin", StringComparison.OrdinalIgnoreCase))
        {
            chosen = phoneDevices[_roundRobinCursor % phoneDevices.Length];
            _roundRobinCursor = (_roundRobinCursor + 1) % Math.Max(1, phoneDevices.Length);
        }
        else
        {
            var selected = CurrentSelectedDevice();
            chosen = selected is not null && phoneDevices.Any(device => device.Id.Equals(selected.Id, StringComparison.OrdinalIgnoreCase))
                ? selected
                : phoneDevices[0];
        }

        for (var i = 0; i < _deviceList.Items.Count; i++)
        {
            if (_deviceList.Items[i] is DeviceRecord d && d.Id == chosen.Id)
            {
                _deviceList.SelectedIndex = i;
                break;
            }
        }
        UpdateSelectedDeviceStatus();
        return chosen;
    }

    private async Task EnsureMapSessionForDeviceAsync(DeviceRecord device)
    {
        if (_mapSession.RfcommConnected && !string.Equals(_mapSession.ConnectedDeviceId, device.Id, StringComparison.OrdinalIgnoreCase))
        {
            _logger.Info("MAP", $"Switching MAP session from {_mapSession.ConnectedDeviceId ?? "unknown"} to {device.Id}.");
            await _mapSession.DisconnectAsync();
        }

        if (!_mapSession.RfcommConnected)
        {
            await _mapSession.ConnectMapRfcommAsync(device, _appCts.Token);
        }
        if (!_mapSession.RfcommConnected)
        {
            throw new InvalidOperationException("MAP RFCOMM did not connect to the selected phone. Reconnect that phone and retry.");
        }

        if (!_mapSession.ObexConnected)
        {
            try
            {
                await _mapSession.ObexConnectAsync(_appCts.Token);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.Warn("MAP", $"Primary MAP OBEX connect attempt failed: {ex.Message}. Trying validated connect profiles.");
            }
        }

        if (!_mapSession.ObexConnected)
        {
            await _mapSession.RunConnectProfilesAsync(device, _appCts.Token);
        }

        if (!_mapSession.ObexConnected)
        {
            throw new InvalidOperationException("MAP OBEX did not respond for the selected phone. Reconnect that phone and retry. The HFP calling path is unaffected.");
        }
    }

    private async Task ReconnectMapSessionForDeviceAsync(DeviceRecord device)
    {
        _logger.Info("MAP", $"Forcing a clean MAP session for {device.Name} ({device.Id}) to prevent cross-device session reuse.");
        await _mapSession.DisconnectAsync();
        await _mapSession.ConnectMapRfcommAsync(device, _appCts.Token);
        if (!_mapSession.RfcommConnected)
        {
            throw new InvalidOperationException($"MAP RFCOMM did not reconnect to {device.Name}.");
        }

        try
        {
            await _mapSession.ObexConnectAsync(_appCts.Token);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.Warn("MAP", $"Primary MAP OBEX reconnect failed for {device.Name}: {ex.Message}. Trying validated profiles.");
        }

        if (!_mapSession.ObexConnected)
        {
            await _mapSession.RunConnectProfilesAsync(device, _appCts.Token);
        }

        if (!_mapSession.ObexConnected)
        {
            throw new InvalidOperationException($"MAP OBEX did not reconnect to {device.Name}.");
        }
    }

    private void SetReadCountForBridge(int limit)
    {
        var allowed = new[] { 10, 25, 50, 100, 250 };
        var best = allowed.OrderBy(v => Math.Abs(v - Math.Clamp(limit, 1, 250))).First();
        var text = best.ToString();
        var index = _readCountBox.Items.IndexOf(text);
        if (index >= 0) _readCountBox.SelectedIndex = index;
    }

    private object ToBridgeMessage(InboxMessageView message) => new
    {
        message.Handle,
        message.Folder,
        message.DeviceId,
        message.DeviceName,
        message.Type,
        message.Status,
        From = message.From,
        To = message.To,
        message.Date,
        message.Subject,
        message.Body,
        Preview = message.Body.Length > 160 ? message.Body[..160] + "..." : message.Body
    };

    private static string MaskBridgePhone(string value)
    {
        var digits = new string(value.Where(char.IsDigit).ToArray());
        return digits.Length <= 4 ? value : new string('•', Math.Max(0, digits.Length - 4)) + digits[^4..];
    }
}

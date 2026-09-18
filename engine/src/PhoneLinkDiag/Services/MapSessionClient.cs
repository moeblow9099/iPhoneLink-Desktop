using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using PhoneLinkDiag.Models;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Rfcomm;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;

namespace PhoneLinkDiag.Services;

public sealed class MapSessionClient : IDisposable
{
    private readonly DiagnosticLogger _log;
    private readonly BluetoothDiagnosticService _bluetooth;
    private readonly List<PacketLogEntry> _packetLog = new();
    private readonly object _gate = new();
    private readonly object _stateGate = new();
    private StreamSocket? _socket;
    private DataWriter? _writer;
    private DataReader? _reader;
    private RfcommDeviceService? _service;
    private DeviceRecord? _device;
    private uint? _connectionId;
    private ushort? _remoteMaxPacketSize;
    private System.Threading.Timer? _heartbeatTimer;
    private string _bluetoothState = "Disconnected";
    private string _rfcommState = "Disconnected";
    private string _obexState = "Disconnected";
    private string _mapState = "Disconnected";
    private bool _disposed;

    public event Action<PacketLogEntry>? PacketLogged;
    public event Action? StateChanged;

    public MapSessionClient(DiagnosticLogger log, BluetoothDiagnosticService bluetooth)
    {
        _log = log;
        _bluetooth = bluetooth;
    }

    public IReadOnlyList<PacketLogEntry> PacketLog
    {
        get
        {
            lock (_gate) return _packetLog.ToArray();
        }
    }

    public bool RfcommConnected => _socket is not null;
    public bool ObexConnected => _connectionId is not null;
    public uint? ConnectionId => _connectionId;
    public string? ConnectedDeviceId => _device?.Id;
    public string BluetoothState => _bluetoothState;
    public string RfcommState => _rfcommState;
    public string ObexState => _obexState;
    public string MapState => _mapState;

    public IReadOnlyList<ConnectProfile> ConnectProfiles => MapConnectProfileFactory.CreateProfiles();

    public async Task ConnectMapRfcommAsync(DeviceRecord device, CancellationToken ct, int? attemptNumber = null, string? profileName = null)
    {
        ThrowIfDisposed();
        await DisconnectAsync(attemptNumber, profileName);
        _device = device;
        SetState(device.BluetoothConnectionStatus, "Disconnected", "Disconnected", "Disconnected", "Selected device for MAP RFCOMM connection.", attemptNumber, profileName);
        _log.Info("MAP", $"Connecting MAP RFCOMM for {device.Name}. BluetoothStatus={device.BluetoothConnectionStatus}");

        try
        {
            _service = await _bluetooth.GetFirstRfcommServiceAsync(device, BluetoothServiceIds.MapMessageAccessServer, ct);
            if (_service is null)
            {
                AddStatus("RFCOMM", "Connect MAP", false, "MAP/MAS RFCOMM service not found.", error: "Service lookup returned null.", attemptNumber: attemptNumber, profileName: profileName);
                return;
            }

            _log.Info("RFCOMM", $"MAP ServiceId={_service.ServiceId.Uuid}; ServiceName={_service.ConnectionServiceName}; Host={_service.ConnectionHostName?.DisplayName}");
            _socket = new StreamSocket();
            var sw = Stopwatch.StartNew();
            await WithTimeout(
                _socket.ConnectAsync(
                    _service.ConnectionHostName,
                    _service.ConnectionServiceName,
                    SocketProtectionLevel.BluetoothEncryptionAllowNullAuthentication).AsTask(ct),
                TimeSpan.FromSeconds(20),
                ct);
            sw.Stop();

            _writer = new DataWriter(_socket.OutputStream);
            _reader = new DataReader(_socket.InputStream) { InputStreamOptions = InputStreamOptions.Partial };
            StartHeartbeat();
            SetState(device.BluetoothConnectionStatus, "Connected", "Disconnected", "Disconnected", "RFCOMM socket connected.", attemptNumber, profileName);
            AddStatus("RFCOMM", "Connect MAP", true, $"Socket connected. ServiceId={_service.ServiceId.Uuid}; Channel={_service.ConnectionServiceName}; Elapsed={sw.ElapsedMilliseconds}ms", sw.ElapsedMilliseconds, attemptNumber: attemptNumber, profileName: profileName);
        }
        catch (Exception ex)
        {
            AddStatus("RFCOMM", "Connect MAP", false, BuildFailureStatus("RFCOMM connect failed", ex), error: ex.ToString(), attemptNumber: attemptNumber, profileName: profileName);
            await DisconnectAsync(attemptNumber, profileName);
        }
    }

    public async Task DisconnectAsync(int? attemptNumber = null, string? profileName = null)
    {
        _connectionId = null;
        _remoteMaxPacketSize = null;
        StopHeartbeat();
        _reader?.Dispose();
        _writer?.DetachStream();
        _writer?.Dispose();
        _socket?.Dispose();
        _service?.Dispose();
        _reader = null;
        _writer = null;
        _socket = null;
        _service = null;
        var previousDeviceStatus = _device?.BluetoothConnectionStatus ?? "Unknown";
        _device = null;
        await Task.CompletedTask;
        SetState(previousDeviceStatus, "Disconnected", "Disconnected", "Disconnected", "Session resources released.", attemptNumber, profileName);
        AddStatus("RFCOMM", "Disconnect", true, "Session resources released.", attemptNumber: attemptNumber, profileName: profileName);
    }

    public async Task ObexConnectAsync(CancellationToken ct)
    {
        EnsureSocket();
        var profile = ConnectProfiles[0];
        var response = await SendConnectProfileAsync(profile, ct);
        if (response.Decoded.Opcode == 0xA0)
        {
            _connectionId = response.Decoded.ConnectionId;
            _remoteMaxPacketSize = response.Decoded.MaxPacketSize;
            SetState(_device?.BluetoothConnectionStatus ?? "Unknown", "Connected", _connectionId is null ? "Disconnected" : "Connected", _connectionId is null ? "Disconnected" : "Connected", "OBEX CONNECT succeeded.", profile.AttemptNumber, profile.Name);
            _log.Info("OBEX", $"Connect success. ConnectionId={(_connectionId.HasValue ? $"0x{_connectionId.Value:X8}" : "not returned")}; MaxPacketSize={_remoteMaxPacketSize?.ToString() ?? "unknown"}; AuthRequested={response.Decoded.AuthenticationRequested}");
        }
        else
        {
            _log.Warn("OBEX", $"Connect failed: {response.Decoded.CodeName}");
        }
    }

    public async Task RunConnectProfilesAsync(DeviceRecord device, CancellationToken ct)
    {
        foreach (var profile in ConnectProfiles)
        {
            ct.ThrowIfCancellationRequested();
            AddStatus("OBEX", "CONNECT Profile Attempt Start", true, $"Attempt #{profile.AttemptNumber}: {profile.Name}; {profile.SpecBasis}", attemptNumber: profile.AttemptNumber, profileName: profile.Name);
            await ConnectMapRfcommAsync(device, ct, profile.AttemptNumber, profile.Name);
            if (!RfcommConnected)
            {
                AddStatus("OBEX", "CONNECT Profile Attempt Skipped", false, "RFCOMM did not connect; OBEX CONNECT not sent.", attemptNumber: profile.AttemptNumber, profileName: profile.Name);
                continue;
            }

            await SendConnectProfileAsync(profile, ct);
            if (_connectionId is not null)
            {
                AddStatus("OBEX", "CONNECT Profile Session Kept Alive", true, "OBEX CONNECT succeeded; keeping RFCOMM/OBEX session alive for MAP commands.", attemptNumber: profile.AttemptNumber, profileName: profile.Name);
                break;
            }

            await DisconnectAsync(profile.AttemptNumber, profile.Name);
            await Task.Delay(750, ct);
        }
    }

    private async Task<ObexExchangeResult> SendConnectProfileAsync(ConnectProfile profile, CancellationToken ct)
    {
        AddStatus("OBEX", "OBEX Connect Validation", profile.Validation.IsValid, profile.Validation.Summary + " Details: " + string.Join("; ", profile.Validation.Details), attemptNumber: profile.AttemptNumber, profileName: profile.Name);
            if (!profile.Validation.IsValid)
        {
            var entry = AddPacket("MAP", "OBEX Connect Not Sent", profile.Packet, Array.Empty<byte>(), ObexPacketParser.Decode(Array.Empty<byte>()), 0, false, null, "Validation failed; packet not sent.", null, profile.AttemptNumber, profile.Name);
            return new ObexExchangeResult(entry, ObexPacketParser.Decode(Array.Empty<byte>()));
        }

        var response = await SendObexRequestAsync("OBEX Connect", profile.Packet, true, ct, null, profile.AttemptNumber, profile.Name);
        if (response.Decoded.Opcode == 0xA0)
        {
            _connectionId = response.Decoded.ConnectionId;
            _remoteMaxPacketSize = response.Decoded.MaxPacketSize;
            SetState(_device?.BluetoothConnectionStatus ?? "Unknown", "Connected", _connectionId is null ? "Disconnected" : "Connected", _connectionId is null ? "Disconnected" : "Connected", "OBEX CONNECT succeeded.", profile.AttemptNumber, profile.Name);
        }
        return response;
    }

    public async Task GetMasInstanceInformationAsync(CancellationToken ct)
    {
        var request = ObexMapPacketBuilder.Get(RequireConnectionId(), "x-bt/MASInstanceInformation");
        await SendObexRequestAsync("MAP Get MAS Instance Information", request, false, ct);
    }

    public async Task ListMapFoldersAsync(CancellationToken ct)
    {
        await GetMasInstanceInformationAsync(ct);
        await SetFolderRootAsync(ct);
        await GetFolderListingAsync("Root Folder Listing", ct);
        await SetFolderAsync("telecom", ct);
        await GetFolderListingAsync("telecom Folder Listing", ct);
        await SetFolderAsync("msg", ct);
        await GetFolderListingAsync("telecom/msg Folder Listing", ct);
    }

    public async Task SetMapFolderPathAsync(string folderPath, CancellationToken ct)
    {
        await SetFolderRootAsync(ct);
        foreach (var part in folderPath.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            await SetFolderAsync(part, ct);
        }
    }

    public async Task<IReadOnlyList<string>> ListMessagesAsync(CancellationToken ct)
    {
        var appParams = ObexMapPacketBuilder.AppParams((0x01, ObexMapPacketBuilder.UInt16Value(250)));
        return await ListMessagesVariantAsync("MAP Get Message Listing", appParams, ct);
    }

    public async Task<IReadOnlyList<string>> ListMessagesVariantAsync(string operation, byte[] appParams, CancellationToken ct)
    {
        var startIndex = PacketLog.Count;
        var request = ObexMapPacketBuilder.Get(RequireConnectionId(), "x-bt/MAP-msg-listing", null, appParams);
        var response = await SendObexRequestAsync(operation, request, false, ct);
        var body = CollectBodyFromEntries(startIndex, operation);
        if (string.IsNullOrWhiteSpace(body))
        {
            body = response.Decoded.BodyText;
        }

        var handles = ObexPacketParser.ExtractMessageHandles(body);
        foreach (var handle in handles)
        {
            AddStatus("MAP", "Message Handle", true, $"Discovered message handle {handle}", messageHandle: handle);
        }
        if (handles.Count == 0)
        {
            _log.Warn("MAP", "Message listing returned no message handles or no readable XML body.");
        }
        return handles;
    }

    public async Task<PacketLogEntry> ReadMessageAsync(string handle, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(handle))
        {
            AddStatus("MAP", "Get Message", false, "No selected message handle.");
            return new PacketLogEntry(DateTimeOffset.Now, "MAP", "Get Message", string.Empty, string.Empty, string.Empty, string.Empty, 0, false, "No selected message handle.");
        }

        var appParams = ObexMapPacketBuilder.AppParams((0x0A, new byte[] { 0x01 }), (0x14, new byte[] { 0x00 }));
        var request = ObexMapPacketBuilder.Get(RequireConnectionId(), "x-bt/message", handle, appParams);
        var result = await SendObexRequestAsync($"MAP Get Message {handle}", request, false, ct, handle);
        return result.Entry;
    }


    public async Task<PacketLogEntry> PushSmsAsync(string phoneNumber, string message, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            throw new ArgumentException("Recipient phone number is required.", nameof(phoneNumber));
        }
        if (string.IsNullOrWhiteSpace(message))
        {
            throw new ArgumentException("SMS message body is required.", nameof(message));
        }

        var sanitizedPhone = SanitizePhoneForMap(phoneNumber);
        if (string.IsNullOrWhiteSpace(sanitizedPhone))
        {
            throw new ArgumentException("Recipient phone number is empty after sanitizing.", nameof(phoneNumber));
        }

        await SetMapFolderPathAsync("telecom/msg/outbox", ct);
        var bMessage = BMessageBuilder.BuildSms(sanitizedPhone, message);
        var bodyBytes = System.Text.Encoding.UTF8.GetBytes(bMessage);
        var appParams = ObexMapPacketBuilder.AppParams(
            (0x0B, new byte[] { 0x00 }), // Transparent=false: let the phone keep normal sent-message behavior if supported.
            (0x0C, new byte[] { 0x01 }), // Retry=true.
            (0x14, new byte[] { 0x01 })  // Charset=UTF-8.
        );
        var request = ObexMapPacketBuilder.Put(RequireConnectionId(), "x-bt/message", null, bodyBytes, appParams);
        var result = await SendObexRequestAsync($"MAP Push SMS to {MaskPhoneForLog(sanitizedPhone)}", request, false, ct);
        return result.Entry;
    }

    private static string SanitizePhoneForMap(string value)
    {
        var allowed = value.Where(ch => char.IsDigit(ch) || ch == '+').ToArray();
        return new string(allowed);
    }

    private static string MaskPhoneForLog(string value) => value.Length <= 4 ? value : new string('•', Math.Max(0, value.Length - 4)) + value[^4..];

    public string ExportPacketLog()
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "packet-logs");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, $"map-packet-log-{DateTime.Now:yyyyMMdd-HHmmss}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(PacketLog, new JsonSerializerOptions { WriteIndented = true }));
        _log.Info("MAP", $"Exported packet log to {path}");
        return path;
    }

    public string ExportSessionReport()
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "session-reports");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, $"map-session-report-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
        var lines = new List<string>
        {
            "PhoneLink Clean-Room MAP Session Report",
            $"Generated: {DateTimeOffset.Now:O}",
            $"Device: {_device?.Name ?? "none"}",
            $"DeviceId: {_device?.Id ?? "none"}",
            $"BluetoothStatus: {_device?.BluetoothConnectionStatus ?? "unknown"}",
            $"RFCOMM Connected: {RfcommConnected}",
            $"OBEX Connected: {ObexConnected}",
            $"ConnectionId: {(_connectionId.HasValue ? $"0x{_connectionId.Value:X8}" : "none")}",
            $"RemoteMaxPacketSize: {_remoteMaxPacketSize?.ToString() ?? "unknown"}",
            "",
            "Packet Results:"
        };

        foreach (var entry in PacketLog)
        {
            lines.Add($"{entry.Timestamp:O} | {entry.Layer} | {entry.Operation} | Success={entry.Success} | {entry.Status}");
            if (!string.IsNullOrWhiteSpace(entry.Error)) lines.Add(entry.Error);
        }

        File.WriteAllLines(path, lines);
        _log.Info("MAP", $"Exported session report to {path}");
        return path;
    }

    private async Task GetFolderListingAsync(string operation, CancellationToken ct)
    {
        var appParams = ObexMapPacketBuilder.AppParams((0x01, ObexMapPacketBuilder.UInt16Value(100)));
        var request = ObexMapPacketBuilder.Get(RequireConnectionId(), "x-obex/folder-listing", null, appParams);
        await SendObexRequestAsync(operation, request, false, ct);
    }

    private async Task SetFolderRootAsync(CancellationToken ct)
    {
        var request = ObexMapPacketBuilder.SetPath(RequireConnectionId(), string.Empty);
        await SendObexRequestAsync("MAP Set Folder Root", request, false, ct);
    }

    private async Task SetFolderAsync(string folderName, CancellationToken ct)
    {
        var request = ObexMapPacketBuilder.SetPath(RequireConnectionId(), folderName);
        await SendObexRequestAsync($"MAP Set Folder {folderName}", request, false, ct);
    }

    private string CollectBodyFromEntries(int startIndex, string operationPrefix)
    {
        return string.Concat(PacketLog
            .Skip(Math.Clamp(startIndex, 0, PacketLog.Count))
            .Where(entry => entry.Operation.StartsWith(operationPrefix, StringComparison.Ordinal))
            .Select(entry => entry.ResponseBodyText)
            .Where(text => !string.IsNullOrWhiteSpace(text)));
    }

    private async Task<ObexExchangeResult> SendObexRequestAsync(string operation, byte[] request, bool connectResponse, CancellationToken ct, string? messageHandle = null, int? attemptNumber = null, string? profileName = null)
    {
        EnsureSocket();
        var sw = Stopwatch.StartNew();
        try
        {
            AddStatus("OBEX", operation + " TX Timestamp", true, $"Sending {request.Length} bytes at {DateTimeOffset.Now:O}", attemptNumber: attemptNumber, profileName: profileName);
            for (var i = 0; i < request.Length; i++)
            {
                AddStatus("OBEX", operation + " TX Byte", true, $"Transmitted byte {i + 1}: 0x{request[i]:X2}", attemptNumber: attemptNumber, profileName: profileName);
            }
            _writer!.WriteBytes(request);
            await WithTimeout(_writer.StoreAsync().AsTask(ct), TimeSpan.FromSeconds(10), ct);
            await WithTimeout(_writer.FlushAsync().AsTask(ct), TimeSpan.FromSeconds(10), ct);
            var response = await ReadObexResponseAsync(ct, attemptNumber, profileName);
            sw.Stop();

            var decoded = ObexPacketParser.Decode(response, connectResponse);
            var success = decoded.Opcode is 0xA0 or 0x90;
            var entry = AddPacket("MAP", operation, request, response, decoded, sw.ElapsedMilliseconds, success, messageHandle, attemptNumber: attemptNumber, profileName: profileName);

            var continuationCount = 0;
            while (decoded.Opcode == 0x90 && _connectionId is not null)
            {
                continuationCount++;
                if (continuationCount > 64)
                {
                    AddStatus("MAP", operation + " Continue Guard", false, "Stopped after 64 OBEX Continue requests to prevent an infinite protocol loop.", messageHandle: messageHandle, attemptNumber: attemptNumber, profileName: profileName);
                    break;
                }

                var continuation = ObexMapPacketBuilder.ContinueGet(_connectionId.Value);
                var contSw = Stopwatch.StartNew();
                _writer.WriteBytes(continuation);
                await WithTimeout(_writer.StoreAsync().AsTask(ct), TimeSpan.FromSeconds(10), ct);
                await WithTimeout(_writer.FlushAsync().AsTask(ct), TimeSpan.FromSeconds(10), ct);
                var contResponse = await ReadObexResponseAsync(ct, attemptNumber, profileName);
                contSw.Stop();
                decoded = ObexPacketParser.Decode(contResponse);
                AddPacket("MAP", operation + " Continue", continuation, contResponse, decoded, contSw.ElapsedMilliseconds, decoded.Opcode is 0xA0 or 0x90, messageHandle, attemptNumber: attemptNumber, profileName: profileName);
            }

            return new ObexExchangeResult(entry, decoded);
        }
        catch (Exception ex)
        {
            sw.Stop();
            var status = BuildFailureStatus(operation + " failed", ex);
            var entry = AddPacket("MAP", operation, request, Array.Empty<byte>(), ObexPacketParser.Decode(Array.Empty<byte>()), sw.ElapsedMilliseconds, false, messageHandle, status, ex.ToString(), attemptNumber, profileName);
            CloseAfterFatalError("Fatal protocol error or timeout during " + operation, attemptNumber, profileName);
            return new ObexExchangeResult(entry, ObexPacketParser.Decode(Array.Empty<byte>()));
        }
    }

    private async Task<byte[]> ReadObexResponseAsync(CancellationToken ct, int? attemptNumber = null, string? profileName = null)
    {
        var bytes = new List<byte>();
        ushort? expectedLength = null;
        try
        {
            while (expectedLength is null || bytes.Count < expectedLength.Value)
            {
                var loaded = await WithTimeout(_reader!.LoadAsync(1).AsTask(ct), TimeSpan.FromSeconds(20), ct);
                if (loaded == 0)
                {
                    AddStatus("OBEX", "RX EOF", false, $"No more bytes received. Total={bytes.Count}", attemptNumber: attemptNumber, profileName: profileName);
                    CloseAfterFatalError("Remote endpoint disconnected during receive.", attemptNumber, profileName);
                    break;
                }

                var value = _reader.ReadByte();
                bytes.Add(value);
                AddStatus("OBEX", "RX Byte", true, $"Received byte {bytes.Count}: 0x{value:X2} at {DateTimeOffset.Now:O}", attemptNumber: attemptNumber, profileName: profileName);

                if (bytes.Count == 3)
                {
                    expectedLength = (ushort)((bytes[1] << 8) | bytes[2]);
                    AddStatus("OBEX", "RX Packet Length", true, $"Response packet length field={expectedLength}; bytes=0x{bytes[1]:X2} 0x{bytes[2]:X2}", attemptNumber: attemptNumber, profileName: profileName);
                    if (expectedLength < 3)
                    {
                        AddStatus("OBEX", "RX Packet Length", false, "Invalid OBEX response length below 3.", attemptNumber: attemptNumber, profileName: profileName);
                        break;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            AddStatus("OBEX", "RX Error", false, $"Receive failed after {bytes.Count} byte(s). {BuildFailureStatus("OBEX receive failed", ex)}", error: ex.ToString(), attemptNumber: attemptNumber, profileName: profileName);
            CloseAfterFatalError("Remote endpoint disconnected or receive timed out.", attemptNumber, profileName);
            throw;
        }

        if (expectedLength is null || bytes.Count != expectedLength.Value)
        {
            CloseAfterFatalError("Incomplete OBEX response packet.", attemptNumber, profileName);
            throw new InvalidDataException($"OBEX response was incomplete. Received {bytes.Count} byte(s); expected {(expectedLength?.ToString() ?? "unknown")}.");
        }

        return bytes.ToArray();
    }

    private PacketLogEntry AddPacket(string layer, string operation, byte[] request, byte[] response, ObexDecodedPacket decoded, long elapsedMs, bool success, string? messageHandle = null, string? statusOverride = null, string? error = null, int? attemptNumber = null, string? profileName = null)
    {
        var requestDecoded = operation == "OBEX Connect"
            ? ObexConnectValidator.DescribeConnectRequest(request, BluetoothServiceIds.ObexMapMasTarget)
            : ObexPacketParser.ToSummary(ObexPacketParser.Decode(request));
        var responseDecoded = ObexPacketParser.ToSummary(decoded);
        var status = statusOverride ?? responseDecoded;
        var entry = new PacketLogEntry(
            DateTimeOffset.Now,
            layer,
            operation,
            HexUtil.ToHex(request),
            requestDecoded,
            HexUtil.ToHex(response),
            responseDecoded,
            elapsedMs,
            success,
            status,
            attemptNumber,
            profileName,
            messageHandle,
            error,
            decoded.BodyText);
        AddEntry(entry);
        _log.Info(layer, $"{operation}: Success={success}; Elapsed={elapsedMs}ms; {status}");
        return entry;
    }

    private void AddStatus(string layer, string operation, bool success, string status, long elapsedMs = 0, string? messageHandle = null, string? error = null, int? attemptNumber = null, string? profileName = null)
    {
        var entry = new PacketLogEntry(DateTimeOffset.Now, layer, operation, string.Empty, string.Empty, string.Empty, string.Empty, elapsedMs, success, status, attemptNumber, profileName, messageHandle, error);
        AddEntry(entry);
        _log.Info(layer, $"{operation}: Success={success}; {status}");
    }

    private void AddEntry(PacketLogEntry entry)
    {
        lock (_gate) _packetLog.Add(entry);
        PacketLogged?.Invoke(entry);
    }

    private void SetState(string bluetooth, string rfcomm, string obex, string map, string reason, int? attemptNumber = null, string? profileName = null)
    {
        lock (_stateGate)
        {
            if (_bluetoothState == bluetooth && _rfcommState == rfcomm && _obexState == obex && _mapState == map)
            {
                return;
            }

            _bluetoothState = bluetooth;
            _rfcommState = rfcomm;
            _obexState = obex;
            _mapState = map;
        }

        AddStatus("State", "Session State", true, $"Bluetooth={bluetooth}; RFCOMM={rfcomm}; OBEX={obex}; MAP={map}; Reason={reason}", attemptNumber: attemptNumber, profileName: profileName);
        StateChanged?.Invoke();
    }

    private void StartHeartbeat()
    {
        _heartbeatTimer?.Dispose();
        _heartbeatTimer = new System.Threading.Timer(_ =>
        {
            AddStatus("Heartbeat", "Session Heartbeat", true, $"Bluetooth={BluetoothState}; RFCOMM={RfcommState}; OBEX={ObexState}; MAP={MapState}; Socket={(_socket is null ? "null" : "alive")}; Remote close is detected on next read/write.");
        }, null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));
    }

    private void StopHeartbeat()
    {
        _heartbeatTimer?.Dispose();
        _heartbeatTimer = null;
    }

    private void CloseAfterFatalError(string reason, int? attemptNumber = null, string? profileName = null)
    {
        StopHeartbeat();
        _connectionId = null;
        _remoteMaxPacketSize = null;
        _reader?.Dispose();
        _writer?.DetachStream();
        _writer?.Dispose();
        _socket?.Dispose();
        _service?.Dispose();
        _reader = null;
        _writer = null;
        _socket = null;
        _service = null;
        SetState(_device?.BluetoothConnectionStatus ?? "Unknown", "Disconnected", "Disconnected", "Disconnected", reason, attemptNumber, profileName);
    }

    private uint RequireConnectionId()
    {
        if (_connectionId is null)
        {
            throw new InvalidOperationException("OBEX is not connected or server did not return a Connection ID.");
        }
        return _connectionId.Value;
    }

    private void EnsureSocket()
    {
        if (_socket is null || _writer is null || _reader is null)
        {
            throw new InvalidOperationException("MAP RFCOMM is not connected.");
        }
    }

    private string BuildFailureStatus(string prefix, Exception ex)
    {
        var hresult = ex.HResult == 0 ? "none" : $"0x{ex.HResult:X8}";
        var socketState = _socket is null ? "null" : "created";
        var bluetooth = _device?.BluetoothConnectionStatus ?? "unknown";
        return $"{prefix}; Bluetooth={bluetooth}; RFCOMM={(RfcommConnected ? "connected" : "not connected")}; OBEX={(ObexConnected ? "connected" : "not connected")}; HRESULT={hresult}; Exception={ex.GetType().Name}: {ex.Message}; SocketState={socketState}; DisconnectReason=not reported by StreamSocket";
    }

    private static async Task<T> WithTimeout<T>(Task<T> task, TimeSpan timeout, CancellationToken ct)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var delay = Task.Delay(timeout, timeoutCts.Token);
        var completed = await Task.WhenAny(task, delay);
        if (completed == delay)
        {
            ct.ThrowIfCancellationRequested();
            throw new TimeoutException($"Operation timed out after {timeout.TotalSeconds:0}s.");
        }
        timeoutCts.Cancel();
        return await task;
    }

    private static async Task WithTimeout(Task task, TimeSpan timeout, CancellationToken ct)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var delay = Task.Delay(timeout, timeoutCts.Token);
        var completed = await Task.WhenAny(task, delay);
        if (completed == delay)
        {
            ct.ThrowIfCancellationRequested();
            throw new TimeoutException($"Operation timed out after {timeout.TotalSeconds:0}s.");
        }
        timeoutCts.Cancel();
        await task;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(MapSessionClient));
    }

    public void Dispose()
    {
        if (_disposed) return;
        StopHeartbeat();
        DisconnectAsync().GetAwaiter().GetResult();
        _disposed = true;
    }

    private sealed record ObexExchangeResult(PacketLogEntry Entry, ObexDecodedPacket Decoded);
}

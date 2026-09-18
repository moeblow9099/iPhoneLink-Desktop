using System.Text;
using PhoneLinkDiag.Models;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;

namespace PhoneLinkDiag.Services;

public sealed class HfpDialerClient : IDisposable
{
    private readonly DiagnosticLogger _log;
    private readonly BluetoothDiagnosticService _bluetooth;
    private StreamSocket? _socket;
    private DataWriter? _writer;
    private DataReader? _reader;
    private string? _connectedDeviceId;
    private bool _disposed;

    public HfpDialerClient(DiagnosticLogger log, BluetoothDiagnosticService bluetooth)
    {
        _log = log;
        _bluetooth = bluetooth;
    }

    public bool IsConnected => _socket is not null && _writer is not null && _reader is not null;
    public string? ConnectedDeviceId => _connectedDeviceId;

    public async Task ConnectAsync(DeviceRecord device, CancellationToken ct)
    {
        ThrowIfDisposed();
        await DisconnectAsync();
        _log.Info("HFP", $"Connecting HFP Audio Gateway for {device.Name}.");

        using var service = await FindServiceAsync(device, ct);
        if (service is null)
        {
            throw new InvalidOperationException("HFP RFCOMM service not found for selected device.");
        }

        _socket = new StreamSocket();
        await WithTimeout(_socket.ConnectAsync(
            service.ConnectionHostName,
            service.ConnectionServiceName,
            SocketProtectionLevel.BluetoothEncryptionAllowNullAuthentication).AsTask(ct), TimeSpan.FromSeconds(20), ct);

        _writer = new DataWriter(_socket.OutputStream);
        _reader = new DataReader(_socket.InputStream) { InputStreamOptions = InputStreamOptions.Partial };
        _connectedDeviceId = device.Id;
        _log.Info("HFP", $"HFP RFCOMM connected. ServiceId={service.ServiceId.Uuid}; Channel={service.ConnectionServiceName}");
    }

    private async Task<Windows.Devices.Bluetooth.Rfcomm.RfcommDeviceService?> FindServiceAsync(DeviceRecord device, CancellationToken ct)
    {
        foreach (var cacheMode in new[] { Windows.Devices.Bluetooth.BluetoothCacheMode.Uncached, Windows.Devices.Bluetooth.BluetoothCacheMode.Cached })
        {
            foreach (var serviceId in new[]
                     {
                         BluetoothServiceIds.HandsFreeAudioGateway,
                         BluetoothServiceIds.HandsFree,
                         BluetoothServiceIds.HeadsetAudioGateway
                     })
            {
                ct.ThrowIfCancellationRequested();
                var service = await _bluetooth.GetFirstRfcommServiceAsync(device, serviceId, ct, cacheMode);
                if (service is not null)
                {
                    _log.Info("HFP", $"RFCOMM service found. ServiceId={service.ServiceId.Uuid}; CacheMode={cacheMode}.");
                    return service;
                }
            }
        }

        return null;
    }

    public async Task<IReadOnlyList<string>> InitializeAsync(CancellationToken ct)
    {
        // Keep this sequence aligned with the July 20 HFP build that successfully placed
        // outbound calls from two separate phones. Do not reintroduce ATE0: the validated
        // iPhone path returns ERROR for that optional command even though calling works.
        var responses = new List<string>();
        responses.Add(await SendAtAsync("AT", ct));
        _log.Info("HFP", "AT handshake skipped: ATE0 is not required and this iPhone returns ERROR");
        responses.Add(await SendAtAsync("AT+BRSF=20", ct));
        responses.Add(await SendAtAsync("AT+CIND=?", ct));
        responses.Add(await SendAtAsync("AT+CIND?", ct));
        responses.Add(await SendAtAsync("AT+CMER=3,0,0,1", ct));
        return responses;
    }

    public async Task<string> DialAsync(string phoneNumber, CancellationToken ct)
    {
        var sanitized = SanitizeDialNumber(phoneNumber);
        if (string.IsNullOrWhiteSpace(sanitized))
        {
            throw new ArgumentException("Phone number is empty after sanitizing.", nameof(phoneNumber));
        }

        _log.Info("HFP", $"Dial command requested for {MaskPhone(sanitized)}. Sending ATD command after explicit button click.");
        return await SendAtAsync($"ATD{sanitized};", ct, TimeSpan.FromSeconds(8));
    }

    public async Task<string> HangUpAsync(CancellationToken ct)
    {
        _log.Info("HFP", "Hang up requested. Sending AT+CHUP.");
        return await SendAtAsync("AT+CHUP", ct, TimeSpan.FromSeconds(8));
    }

    public async Task DisconnectAsync()
    {
        _reader?.Dispose();
        _writer?.DetachStream();
        _writer?.Dispose();
        _socket?.Dispose();
        _reader = null;
        _writer = null;
        _socket = null;
        _connectedDeviceId = null;
        await Task.CompletedTask;
    }

    private async Task<string> SendAtAsync(string command, CancellationToken ct, TimeSpan? receiveTimeout = null)
    {
        EnsureConnected();
        var line = command.EndsWith("\r", StringComparison.Ordinal) ? command : command + "\r";
        var bytes = Encoding.ASCII.GetBytes(line);
        _writer!.WriteBytes(bytes);
        await WithTimeout(_writer.StoreAsync().AsTask(ct), TimeSpan.FromSeconds(5), ct);
        await WithTimeout(_writer.FlushAsync().AsTask(ct), TimeSpan.FromSeconds(5), ct);
        _log.Info("HFP", $"TX: {command}");

        var response = await ReadAtResponseAsync(receiveTimeout ?? TimeSpan.FromSeconds(5), ct);
        _log.Info("HFP", $"RX: {response.Replace("\r", "\\r").Replace("\n", "\\n")}");
        return response;
    }

    private async Task<string> ReadAtResponseAsync(TimeSpan timeout, CancellationToken ct)
    {
        EnsureConnected();
        var bytes = new List<byte>();
        var until = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < until)
        {
            var remaining = until - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero) break;
            try
            {
                var loaded = await WithTimeout(_reader!.LoadAsync(1).AsTask(ct), remaining < TimeSpan.FromMilliseconds(500) ? remaining : TimeSpan.FromMilliseconds(500), ct);
                if (loaded == 0) break;
                bytes.Add(_reader.ReadByte());
                var text = Encoding.ASCII.GetString(bytes.ToArray());
                if (text.Contains("OK", StringComparison.OrdinalIgnoreCase)
                    || text.Contains("ERROR", StringComparison.OrdinalIgnoreCase)
                    || text.Contains("NO CARRIER", StringComparison.OrdinalIgnoreCase)
                    || text.Contains("NO ANSWER", StringComparison.OrdinalIgnoreCase)
                    || text.Contains("BUSY", StringComparison.OrdinalIgnoreCase)
                    || text.Contains("+CME ERROR", StringComparison.OrdinalIgnoreCase)
                    || text.Contains("+CMS ERROR", StringComparison.OrdinalIgnoreCase))
                {
                    return text.Trim();
                }
            }
            catch (TimeoutException)
            {
                if (bytes.Count > 0) break;
            }
        }
        return bytes.Count == 0 ? "<no response>" : Encoding.ASCII.GetString(bytes.ToArray()).Trim();
    }

    private void EnsureConnected()
    {
        if (!IsConnected) throw new InvalidOperationException("HFP is not connected.");
    }

    private static string SanitizeDialNumber(string value)
    {
        var allowed = value.Where(ch => char.IsDigit(ch) || ch == '+' || ch == '*' || ch == '#').ToArray();
        return new string(allowed);
    }

    private static string MaskPhone(string value) => value.Length <= 4 ? value : new string('•', Math.Max(0, value.Length - 4)) + value[^4..];

    private static async Task<T> WithTimeout<T>(Task<T> task, TimeSpan timeout, CancellationToken ct)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var delay = Task.Delay(timeout, timeoutCts.Token);
        var completed = await Task.WhenAny(task, delay);
        if (completed == delay)
        {
            ct.ThrowIfCancellationRequested();
            throw new TimeoutException($"Operation timed out after {timeout.TotalSeconds:0.0}s.");
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
            throw new TimeoutException($"Operation timed out after {timeout.TotalSeconds:0.0}s.");
        }
        timeoutCts.Cancel();
        await task;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(HfpDialerClient));
    }

    public void Dispose()
    {
        if (_disposed) return;
        DisconnectAsync().GetAwaiter().GetResult();
        _disposed = true;
    }
}

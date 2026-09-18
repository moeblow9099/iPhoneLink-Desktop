using PhoneLinkDiag.Models;
using Windows.Devices.Bluetooth.Rfcomm;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;

namespace PhoneLinkDiag.Services;

public sealed class ObexRfcommClient
{
    private readonly DiagnosticLogger _log;

    public ObexRfcommClient(DiagnosticLogger log)
    {
        _log = log;
    }

    public async Task<ObexProbeResult> ProbeConnectAsync(RfcommDeviceService service, string targetName, Guid targetGuid, CancellationToken ct)
    {
        try
        {
            _log.Info("OBEX", $"Opening RFCOMM socket for {targetName}: host={service.ConnectionHostName}, service={service.ConnectionServiceName}");

            using var socket = new StreamSocket();
            await socket.ConnectAsync(
                service.ConnectionHostName,
                service.ConnectionServiceName,
                SocketProtectionLevel.BluetoothEncryptionAllowNullAuthentication).AsTask(ct);

            var request = ObexPacketBuilder.Connect(targetGuid);
            _log.Debug("OBEX", $"TX Connect {targetName}: {HexUtil.ToHex(request)}");

            using var writer = new DataWriter(socket.OutputStream);
            writer.WriteBytes(request);
            await writer.StoreAsync().AsTask(ct);
            await writer.FlushAsync().AsTask(ct);

            using var reader = new DataReader(socket.InputStream)
            {
                InputStreamOptions = InputStreamOptions.Partial
            };

            var loaded = await reader.LoadAsync(1024).AsTask(ct);
            var response = new byte[loaded];
            reader.ReadBytes(response);
            var hex = HexUtil.ToHex(response);
            _log.Debug("OBEX", $"RX Connect {targetName}: {hex}");

            var accepted = response.Length > 0 && response[0] == 0xA0;
            var status = accepted
                ? "OBEX Connect accepted. This is required before PushMessage testing."
                : $"OBEX Connect returned non-success opcode 0x{(response.Length > 0 ? response[0] : 0):X2}.";

            return new ObexProbeResult(targetName, true, accepted, hex, status);
        }
        catch (Exception ex)
        {
            _log.Error("OBEX", $"{targetName} probe failed: {ex.Message}");
            return new ObexProbeResult(targetName, false, false, "<none>", "Probe failed", ex.ToString());
        }
    }
}

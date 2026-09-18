using System.Text;
using PhoneLinkDiag.Models;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;

namespace PhoneLinkDiag.Services;

/// <summary>
/// Read-only PBAP client for contacts and call-history phonebooks exposed by a paired phone.
/// No contact writes, deletes, or call actions are performed here.
/// </summary>
public sealed class PbapDataClient
{
    private readonly DiagnosticLogger _log;
    private readonly BluetoothDiagnosticService _bluetooth;

    public PbapDataClient(DiagnosticLogger log, BluetoothDiagnosticService bluetooth)
    {
        _log = log;
        _bluetooth = bluetooth;
    }

    public Task<PbapSyncResult> SyncAsync(DeviceRecord device, int maxEntries, CancellationToken ct)
        => SyncAsync(device, maxEntries, includeContacts: true, includeCalls: true, ct);

    public async Task<PbapSyncResult> SyncAsync(
        DeviceRecord device,
        int maxEntries,
        bool includeContacts,
        bool includeCalls,
        CancellationToken ct)
    {
        maxEntries = Math.Clamp(maxEntries, 1, ushort.MaxValue);
        if (!includeContacts && !includeCalls)
        {
            return new PbapSyncResult(Array.Empty<ContactRecord>(), Array.Empty<CallHistoryRecord>());
        }

        var service = await _bluetooth.GetFirstRfcommServiceAsync(device, BluetoothServiceIds.PbapPhoneBookServer, ct);
        if (service is null)
        {
            throw new InvalidOperationException("PBAP service is not available for the selected phone.");
        }

        using (service)
        using (var socket = new StreamSocket())
        {
            _log.Info("PBAP", $"Connecting read-only PBAP session for {device.Name}. Contacts={includeContacts}; Calls={includeCalls}.");
            await WithTimeout(socket.ConnectAsync(
                service.ConnectionHostName,
                service.ConnectionServiceName,
                SocketProtectionLevel.BluetoothEncryptionAllowNullAuthentication).AsTask(ct), TimeSpan.FromSeconds(20), ct);

            using var writer = new DataWriter(socket.OutputStream);
            using var reader = new DataReader(socket.InputStream) { InputStreamOptions = InputStreamOptions.Partial };

            var connect = ObexMapPacketBuilder.Connect(BluetoothServiceIds.ObexPbapPseTarget);
            var connectResponse = await ExchangeAsync(writer, reader, connect, true, ct);
            if (connectResponse.Decoded.Opcode != 0xA0 || connectResponse.Decoded.ConnectionId is null)
            {
                throw new InvalidOperationException($"PBAP OBEX did not connect for {device.Name} ({connectResponse.Decoded.CodeName}).");
            }

            var connectionId = connectResponse.Decoded.ConnectionId.Value;
            IReadOnlyList<ContactRecord> contacts = Array.Empty<ContactRecord>();
            if (includeContacts)
            {
                var contactsBody = await PullPhonebookAsync(writer, reader, connectionId, "telecom/pb.vcf", maxEntries, ct);
                contacts = ParseContacts(device, contactsBody).Take(maxEntries).ToArray();
            }

            var calls = new List<CallHistoryRecord>();
            if (includeCalls)
            {
                foreach (var phonebook in new[] { "telecom/cch.vcf", "telecom/ich.vcf", "telecom/och.vcf", "telecom/mch.vcf" })
                {
                    try
                    {
                        var body = await PullPhonebookAsync(writer, reader, connectionId, phonebook, maxEntries, ct);
                        calls.AddRange(ParseCalls(device, body, phonebook));
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _log.Warn("PBAP", $"Call-history repository {phonebook} was not available on {device.Name}: {ex.Message}");
                    }
                }
            }

            var contactNameByPhone = BuildContactNameIndex(contacts);
            var uniqueCalls = calls
                .GroupBy(c => $"{c.Type}|{NormalizePhoneKey(c.Phone)}|{c.Timestamp}", StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .Select(call => ResolveCallName(call, contactNameByPhone))
                .OrderByDescending(c => ParseCallTimestamp(c.Timestamp) ?? DateTimeOffset.MinValue)
                .Take(maxEntries)
                .ToArray();

            _log.Info("PBAP", $"Read-only sync complete for {device.Name}: contacts={contacts.Count}, calls={uniqueCalls.Length}.");
            return new PbapSyncResult(contacts, uniqueCalls);
        }
    }

    private async Task<string> PullPhonebookAsync(
        DataWriter writer,
        DataReader reader,
        uint connectionId,
        string objectName,
        int maxEntries,
        CancellationToken ct)
    {
        var appParams = ObexMapPacketBuilder.AppParams(
            (0x04, ObexMapPacketBuilder.UInt16Value((ushort)Math.Min(maxEntries, ushort.MaxValue))), // MaxListCount
            (0x05, ObexMapPacketBuilder.UInt16Value(0)), // ListStartOffset
            (0x06, PbapNameAndCallFilter()), // VERSION, FN, N, TEL, EMAIL, ORG, X-IRMC-CALL-DATETIME
            (0x07, new byte[] { 0x00 })); // vCard 2.1 for broad phone compatibility

        var request = ObexMapPacketBuilder.Get(connectionId, "x-bt/phonebook", objectName, appParams);
        var response = await ExchangeAsync(writer, reader, request, false, ct);
        if (response.Decoded.Opcode is not (0xA0 or 0x90))
        {
            throw new InvalidOperationException($"PBAP GET {objectName} failed ({response.Decoded.CodeName}).");
        }

        var body = new StringBuilder(response.Decoded.BodyText);
        var continuationCount = 0;
        while (response.Decoded.Opcode == 0x90)
        {
            continuationCount++;
            if (continuationCount > 4096)
            {
                throw new InvalidOperationException($"PBAP continuation limit reached while reading {objectName}; refusing to return a silently truncated phonebook.");
            }
            response = await ExchangeAsync(writer, reader, ObexMapPacketBuilder.ContinueGet(connectionId), false, ct);
            body.Append(response.Decoded.BodyText);
            if (response.Decoded.Opcode is not (0xA0 or 0x90))
            {
                throw new InvalidOperationException($"PBAP continuation for {objectName} failed ({response.Decoded.CodeName}).");
            }
        }

        return body.ToString();
    }

    private async Task<ProbeResponse> ExchangeAsync(DataWriter writer, DataReader reader, byte[] request, bool connectResponse, CancellationToken ct)
    {
        writer.WriteBytes(request);
        await WithTimeout(writer.StoreAsync().AsTask(ct), TimeSpan.FromSeconds(10), ct);
        await WithTimeout(writer.FlushAsync().AsTask(ct), TimeSpan.FromSeconds(10), ct);
        var response = await ReadPacketAsync(reader, ct);
        var decoded = ObexPacketParser.Decode(response, connectResponse);
        _log.Debug("PBAP", $"OBEX RX {decoded.CodeName}; bytes={response.Length}.");
        return new ProbeResponse(decoded);
    }

    private static async Task<byte[]> ReadPacketAsync(DataReader reader, CancellationToken ct)
    {
        var bytes = new List<byte>();
        ushort? expectedLength = null;
        while (expectedLength is null || bytes.Count < expectedLength.Value)
        {
            var loaded = await WithTimeout(reader.LoadAsync(1).AsTask(ct), TimeSpan.FromSeconds(20), ct);
            if (loaded == 0) break;
            bytes.Add(reader.ReadByte());
            if (bytes.Count == 3)
            {
                expectedLength = (ushort)((bytes[1] << 8) | bytes[2]);
                if (expectedLength < 3) throw new InvalidDataException("Invalid OBEX packet length.");
            }
        }
        if (bytes.Count == 0) throw new TimeoutException("PBAP OBEX did not respond.");
        if (expectedLength is null || bytes.Count != expectedLength.Value)
        {
            throw new InvalidDataException($"PBAP OBEX response was incomplete. Received {bytes.Count} byte(s); expected {(expectedLength?.ToString() ?? "unknown")}.");
        }
        return bytes.ToArray();
    }

    private static IEnumerable<ContactRecord> ParseContacts(DeviceRecord device, string body)
    {
        foreach (var card in SplitVCards(body))
        {
            var values = ParseVCard(card);
            var phones = values
                .Where(p => p.Key.StartsWith("TEL", StringComparison.OrdinalIgnoreCase))
                .Select(p => DecodeVCardValue(p.Key, p.Value))
                .Where(v => v.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var emails = values
                .Where(p => p.Key.StartsWith("EMAIL", StringComparison.OrdinalIgnoreCase))
                .Select(p => DecodeVCardValue(p.Key, p.Value))
                .Where(v => v.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var fn = FirstPair(values, "FN");
            var n = FirstPair(values, "N");
            var decodedFn = DecodeVCardValue(fn.Key, fn.Value);
            var structuredName = FormatStructuredName(DecodeVCardValue(n.Key, n.Value));
            var orgPair = FirstPair(values, "ORG");
            var org = DecodeVCardValue(orgPair.Key, orgPair.Value).Replace(";", " · ", StringComparison.Ordinal).Trim();
            var name = ChooseBestDisplayName(decodedFn, structuredName, org, phones);
            if (string.IsNullOrWhiteSpace(name)) name = "Unknown contact";

            if (phones.Length == 0 && emails.Length == 0 && string.Equals(name, "Unknown contact", StringComparison.Ordinal)) continue;
            yield return new ContactRecord(device.Id, device.Name, name, phones, emails, org);
        }
    }

    private static IEnumerable<CallHistoryRecord> ParseCalls(DeviceRecord device, string body, string sourcePhonebook)
    {
        foreach (var card in SplitVCards(body))
        {
            var values = ParseVCard(card);
            var fn = FirstPair(values, "FN");
            var n = FirstPair(values, "N");
            var decodedFn = DecodeVCardValue(fn.Key, fn.Value);
            var structuredName = FormatStructuredName(DecodeVCardValue(n.Key, n.Value));
            var phonePair = values.FirstOrDefault(p => p.Key.StartsWith("TEL", StringComparison.OrdinalIgnoreCase));
            var phone = DecodeVCardValue(phonePair.Key, phonePair.Value);
            var name = ChooseBestDisplayName(decodedFn, structuredName, string.Empty, string.IsNullOrWhiteSpace(phone) ? Array.Empty<string>() : new[] { phone });
            var callDate = values.FirstOrDefault(p => p.Key.StartsWith("X-IRMC-CALL-DATETIME", StringComparison.OrdinalIgnoreCase));
            var type = TypeFromPhonebook(sourcePhonebook);
            if (!string.IsNullOrWhiteSpace(callDate.Key))
            {
                var typeMarker = callDate.Key.Split(';').FirstOrDefault(p => p.StartsWith("TYPE=", StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(typeMarker)) type = NormalizeCallType(typeMarker[5..]);
            }
            var timestamp = DecodeVCardValue(callDate.Key, callDate.Value ?? string.Empty);
            if (string.IsNullOrWhiteSpace(phone) && string.IsNullOrWhiteSpace(name)) continue;
            yield return new CallHistoryRecord(device.Id, device.Name, type, name, phone, timestamp);
        }
    }

    private static IEnumerable<string> SplitVCards(string body)
    {
        var normalized = body.Replace("\r\n", "\n").Replace('\r', '\n');
        var start = 0;
        while (true)
        {
            start = normalized.IndexOf("BEGIN:VCARD", start, StringComparison.OrdinalIgnoreCase);
            if (start < 0) yield break;
            var end = normalized.IndexOf("END:VCARD", start, StringComparison.OrdinalIgnoreCase);
            if (end < 0) yield break;
            end += "END:VCARD".Length;
            yield return normalized[start..end];
            start = end;
        }
    }

    private static List<KeyValuePair<string, string>> ParseVCard(string card)
    {
        var unfolded = new List<string>();
        foreach (var rawLine in card.Split('\n'))
        {
            var line = rawLine.TrimEnd();
            if (unfolded.Count > 0 && unfolded[^1].EndsWith('=') && unfolded[^1].Contains("QUOTED-PRINTABLE", StringComparison.OrdinalIgnoreCase))
            {
                unfolded[^1] += line;
            }
            else if ((line.StartsWith(' ') || line.StartsWith('\t')) && unfolded.Count > 0)
            {
                unfolded[^1] += line[1..];
            }
            else
            {
                unfolded.Add(line);
            }
        }

        var values = new List<KeyValuePair<string, string>>();
        foreach (var line in unfolded)
        {
            var colon = line.IndexOf(':');
            if (colon <= 0) continue;
            values.Add(new KeyValuePair<string, string>(line[..colon].Trim(), line[(colon + 1)..].Trim()));
        }
        return values;
    }

    private static KeyValuePair<string, string> FirstPair(IEnumerable<KeyValuePair<string, string>> values, string key)
        => values.FirstOrDefault(p => p.Key.Equals(key, StringComparison.OrdinalIgnoreCase) || p.Key.StartsWith(key + ";", StringComparison.OrdinalIgnoreCase));

    private static string DecodeVCardValue(string? key, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var decoded = value;
        var metadata = key ?? string.Empty;
        try
        {
            if (metadata.Contains("QUOTED-PRINTABLE", StringComparison.OrdinalIgnoreCase))
            {
                decoded = DecodeQuotedPrintable(decoded);
            }
            else if (metadata.Contains("ENCODING=B", StringComparison.OrdinalIgnoreCase) || metadata.Contains("BASE64", StringComparison.OrdinalIgnoreCase))
            {
                decoded = Encoding.UTF8.GetString(Convert.FromBase64String(decoded));
            }
        }
        catch
        {
            // Keep the original field when optional vCard encoding metadata is malformed.
        }
        return CleanVCardValue(decoded);
    }

    private static string DecodeQuotedPrintable(string value)
    {
        value = value.Replace("=\r\n", string.Empty, StringComparison.Ordinal).Replace("=\n", string.Empty, StringComparison.Ordinal);
        using var stream = new MemoryStream();
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == '=' && i + 2 < value.Length && byte.TryParse(value.AsSpan(i + 1, 2), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var hex))
            {
                stream.WriteByte(hex);
                i += 2;
            }
            else
            {
                foreach (var b in Encoding.UTF8.GetBytes(value[i].ToString())) stream.WriteByte(b);
            }
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string FormatStructuredName(string value)
    {
        var parts = value.Split(';');
        if (parts.Length <= 1) return CleanVCardValue(value);
        var family = parts.ElementAtOrDefault(0) ?? string.Empty;
        var given = parts.ElementAtOrDefault(1) ?? string.Empty;
        var additional = parts.ElementAtOrDefault(2) ?? string.Empty;
        var prefix = parts.ElementAtOrDefault(3) ?? string.Empty;
        var suffix = parts.ElementAtOrDefault(4) ?? string.Empty;
        return string.Join(" ", new[] { prefix, given, additional, family, suffix }.Where(part => !string.IsNullOrWhiteSpace(part))).Trim();
    }

    private static string CleanVCardValue(string value)
        => value.Replace("\\n", " ").Replace("\\N", " ").Replace("\\,", ",").Replace("\\;", ";").Trim(';', ' ');

    private static byte[] PbapNameAndCallFilter()
    {
        // PBAP 64-bit vCard filter bits: VERSION(0), FN(1), N(2), TEL(7),
        // EMAIL(8), ORG(16), X-IRMC-CALL-DATETIME(28).
        const ulong filter =
            (1UL << 0) |
            (1UL << 1) |
            (1UL << 2) |
            (1UL << 7) |
            (1UL << 8) |
            (1UL << 16) |
            (1UL << 28);

        var bytes = new byte[8];
        for (var i = 0; i < bytes.Length; i++)
        {
            bytes[7 - i] = (byte)(filter >> (i * 8));
        }
        return bytes;
    }

    private static string ChooseBestDisplayName(
        string formattedName,
        string structuredName,
        string organization,
        IReadOnlyList<string> phones)
    {
        var candidates = new[] { formattedName, structuredName, organization }
            .Select(CleanVCardValue)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();

        foreach (var candidate in candidates)
        {
            if (!LooksLikePhoneIdentifier(candidate, phones))
            {
                return candidate;
            }
        }

        return string.Empty;
    }

    private static bool LooksLikePhoneIdentifier(string value, IReadOnlyList<string> phones)
    {
        var normalized = NormalizePhoneKey(value);
        if (normalized.Length < 3) return false;

        if (phones.Any(phone => NormalizePhoneKey(phone).Equals(normalized, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        var visible = value.Trim();
        var phoneLikeCharacters = visible.All(ch =>
            char.IsDigit(ch) || ch is '+' or '-' or '(' or ')' or ' ' or '.' or '#');
        return phoneLikeCharacters && normalized.Count(char.IsDigit) >= 3;
    }

    private static string NormalizePhoneKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var digits = new string(value.Where(char.IsDigit).ToArray());
        if (digits.Length == 11 && digits.StartsWith("1", StringComparison.Ordinal))
        {
            digits = digits[1..];
        }
        return digits;
    }

    private static Dictionary<string, string> BuildContactNameIndex(IEnumerable<ContactRecord> contacts)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var contact in contacts)
        {
            if (string.IsNullOrWhiteSpace(contact.Name) || contact.Name.Equals("Unknown contact", StringComparison.OrdinalIgnoreCase)) continue;
            foreach (var phone in contact.Phones)
            {
                var key = NormalizePhoneKey(phone);
                if (key.Length > 0 && !result.ContainsKey(key))
                {
                    result[key] = contact.Name;
                }
            }
        }
        return result;
    }

    private static CallHistoryRecord ResolveCallName(CallHistoryRecord call, IReadOnlyDictionary<string, string> contactNameByPhone)
    {
        var key = NormalizePhoneKey(call.Phone);
        if (key.Length == 0) return call;

        var currentNameIsMissing = string.IsNullOrWhiteSpace(call.Name) ||
            call.Name.Equals(call.Phone, StringComparison.OrdinalIgnoreCase) ||
            LooksLikePhoneIdentifier(call.Name, string.IsNullOrWhiteSpace(call.Phone) ? Array.Empty<string>() : new[] { call.Phone });

        return currentNameIsMissing && contactNameByPhone.TryGetValue(key, out var contactName)
            ? call with { Name = contactName }
            : call;
    }

    private static DateTimeOffset? ParseCallTimestamp(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var formats = new[] { "yyyyMMdd'T'HHmmss'Z'", "yyyyMMdd'T'HHmmss", "yyyyMMdd'T'HHmmsszzz", "yyyyMMddHHmmss" };
        if (DateTimeOffset.TryParseExact(value.Trim(), formats, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeLocal, out var exact))
            return exact;
        return DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;
    }

    private static string TypeFromPhonebook(string phonebook)
        => phonebook.Contains("mch", StringComparison.OrdinalIgnoreCase) ? "Missed"
         : phonebook.Contains("ich", StringComparison.OrdinalIgnoreCase) ? "Incoming"
         : phonebook.Contains("och", StringComparison.OrdinalIgnoreCase) ? "Outgoing"
         : "Call";

    private static string NormalizeCallType(string value)
        => value.Equals("MISSED", StringComparison.OrdinalIgnoreCase) ? "Missed"
         : value.Equals("RECEIVED", StringComparison.OrdinalIgnoreCase) ? "Incoming"
         : value.Equals("DIALED", StringComparison.OrdinalIgnoreCase) ? "Outgoing"
         : value;

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

    private sealed record ProbeResponse(ObexDecodedPacket Decoded);
}

using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PhoneLinkDiag.Services;

public sealed class IPhoneLinkBridgeServer : IDisposable
{
    private readonly DiagnosticLogger _log;
    private readonly HttpListener _listener = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private Task? _loopTask;
    private bool _disposed;
    private readonly string _token;

    public string Url { get; }
    public string TokenFilePath { get; }
    public Func<Task<object>>? HealthAsync { get; set; }
    public Func<Task<object>>? DevicesAsync { get; set; }
    public Func<int, Task<object>>? MessagesAsync { get; set; }
    public Func<int, Task<object>>? RefreshInboxAsync { get; set; }
    public Func<Task<object>>? ContactsAsync { get; set; }
    public Func<Task<object>>? CallsAsync { get; set; }
    public Func<string, string, string?, Task<object>>? SendSmsAsync { get; set; }
    public Func<string, string?, Task<object>>? CallAsync { get; set; }

    public IPhoneLinkBridgeServer(DiagnosticLogger log, string url = "http://127.0.0.1:8765/")
    {
        _log = log;
        Url = url.EndsWith('/') ? url : url + "/";
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "iPhoneLinkCRM");
        Directory.CreateDirectory(folder);
        TokenFilePath = Path.Combine(folder, "bridge-token.txt");
        _token = LoadOrCreateToken(TokenFilePath);
    }

    public void Start()
    {
        if (_loopTask is not null) return;
        _listener.Prefixes.Add(Url);
        _listener.Start();
        _loopTask = Task.Run(ListenLoopAsync);
        _log.Info("CRM", $"Local CRM bridge listening on {Url}");
    }

    private async Task ListenLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                var context = await _listener.GetContextAsync();
                _ = Task.Run(() => HandleAsync(context));
            }
            catch (ObjectDisposedException) { break; }
            catch (HttpListenerException) when (_cts.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                _log.Warn("CRM", $"Bridge listener warning: {ex.Message}");
                try
                {
                    await Task.Delay(500, _cts.Token);
                }
                catch (OperationCanceledException) when (_cts.IsCancellationRequested)
                {
                    break;
                }
            }
        }
    }

    private async Task HandleAsync(HttpListenerContext context)
    {
        try
        {
            AddCors(context.Response);
            if (string.Equals(context.Request.HttpMethod, "OPTIONS", StringComparison.OrdinalIgnoreCase))
            {
                await WriteJsonAsync(context, new { ok = true });
                return;
            }

            var path = context.Request.Url?.AbsolutePath.Trim('/').ToLowerInvariant() ?? string.Empty;
            if (path is not ("" or "health") && !IsAuthorized(context.Request))
            {
                context.Response.StatusCode = 401;
                await WriteJsonAsync(context, new { ok = false, error = "Bridge authentication required." });
                return;
            }

            var result = path switch
            {
                "" or "health" => HealthAsync is null ? new { ok = true, service = "iPhoneLink CRM Bridge" } : await HealthAsync(),
                "devices" => DevicesAsync is null ? new { ok = false, error = "Devices handler not configured." } : await DevicesAsync(),
                "messages" => MessagesAsync is null ? new { ok = false, error = "Messages handler not configured." } : await MessagesAsync(ParseInt(context.Request.QueryString["limit"], 250)),
                "refresh-inbox" => RefreshInboxAsync is null ? new { ok = false, error = "Refresh handler not configured." } : await RefreshInboxAsync(ParseInt(context.Request.QueryString["limit"], 250)),
                "contacts" => ContactsAsync is null ? new { ok = false, error = "Contacts handler not configured." } : await ContactsAsync(),
                "calls" => CallsAsync is null ? new { ok = false, error = "Calls handler not configured." } : await CallsAsync(),
                "send-sms" => await HandleSendSmsAsync(context),
                "call" => await HandleCallAsync(context),
                _ => new { ok = false, error = "Unknown endpoint.", endpoints = new[] { "/health", "/devices", "/messages", "/refresh-inbox", "/contacts", "/calls", "/send-sms", "/call" } }
            };

            await WriteJsonAsync(context, result);
        }
        catch (Exception ex)
        {
            _log.Error("CRM", $"Bridge request failed: {ex}");
            context.Response.StatusCode = 500;
            await WriteJsonAsync(context, new { ok = false, error = "Bridge request failed. See Developer Diagnostics." });
        }
    }

    private async Task<object> HandleSendSmsAsync(HttpListenerContext context)
    {
        if (!string.Equals(context.Request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = 405;
            return new { ok = false, error = "Use POST." };
        }
        var payload = await ReadBodyJsonAsync(context.Request);
        var phone = Get(payload, "phone") ?? Get(payload, "to") ?? string.Empty;
        var message = Get(payload, "message") ?? Get(payload, "body") ?? string.Empty;
        var deviceMode = Get(payload, "deviceMode") ?? Get(payload, "device") ?? "selected";
        if (SendSmsAsync is null) return new { ok = false, error = "SMS handler not configured." };
        return await SendSmsAsync(phone, message, deviceMode);
    }

    private async Task<object> HandleCallAsync(HttpListenerContext context)
    {
        if (!string.Equals(context.Request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = 405;
            return new { ok = false, error = "Use POST." };
        }
        var payload = await ReadBodyJsonAsync(context.Request);
        var phone = Get(payload, "phone") ?? Get(payload, "number") ?? string.Empty;
        var deviceMode = Get(payload, "deviceMode") ?? Get(payload, "device") ?? "selected";
        if (CallAsync is null) return new { ok = false, error = "Call handler not configured." };
        return await CallAsync(phone, deviceMode);
    }

    private async Task<Dictionary<string, JsonElement>> ReadBodyJsonAsync(HttpListenerRequest request)
    {
        using var reader = new StreamReader(request.InputStream, request.ContentEncoding ?? Encoding.UTF8);
        var text = await reader.ReadToEndAsync();
        if (string.IsNullOrWhiteSpace(text)) return new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(text, _jsonOptions)
            ?? new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
    }

    private static string? Get(Dictionary<string, JsonElement> payload, string name)
    {
        if (!payload.TryGetValue(name, out var value)) return null;
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => value.ToString()
        };
    }

    private static int ParseInt(string? value, int fallback) => int.TryParse(value, out var parsed) ? Math.Clamp(parsed, 1, 500) : fallback;

    private async Task WriteJsonAsync(HttpListenerContext context, object value)
    {
        AddCors(context.Response);
        context.Response.ContentType = "application/json; charset=utf-8";
        var json = JsonSerializer.Serialize(value, _jsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json);
        context.Response.ContentLength64 = bytes.Length;
        await context.Response.OutputStream.WriteAsync(bytes);
        context.Response.OutputStream.Close();
    }

    private static void AddCors(HttpListenerResponse response)
    {
        response.Headers["Access-Control-Allow-Origin"] = "*";
        response.Headers["Access-Control-Allow-Headers"] = "Content-Type, X-iPhoneLink-Token, Authorization";
        response.Headers["Access-Control-Allow-Methods"] = "GET,POST,OPTIONS";
    }

    private bool IsAuthorized(HttpListenerRequest request)
    {
        var supplied = request.Headers["X-iPhoneLink-Token"];
        if (string.IsNullOrWhiteSpace(supplied))
        {
            var authorization = request.Headers["Authorization"];
            if (!string.IsNullOrWhiteSpace(authorization) && authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                supplied = authorization[7..].Trim();
        }
        if (string.IsNullOrWhiteSpace(supplied)) return false;

        var expectedBytes = Encoding.UTF8.GetBytes(_token);
        var suppliedBytes = Encoding.UTF8.GetBytes(supplied);
        return expectedBytes.Length == suppliedBytes.Length && CryptographicOperations.FixedTimeEquals(expectedBytes, suppliedBytes);
    }

    private static string LoadOrCreateToken(string path)
    {
        if (File.Exists(path))
        {
            var existing = File.ReadAllText(path).Trim();
            if (existing.Length >= 32) return existing;
        }

        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        File.WriteAllText(path, token + Environment.NewLine);
        return token;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts.Cancel();
        if (_listener.IsListening) _listener.Stop();
        _listener.Close();
        _cts.Dispose();
    }
}

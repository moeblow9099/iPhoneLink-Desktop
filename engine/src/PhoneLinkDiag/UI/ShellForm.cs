using System.Text.Json;
using Microsoft.Web.WebView2.WinForms;

namespace PhoneLinkDiag.UI;

public sealed class ShellForm : Form
{
    private readonly WebView2 _web = new();
    private readonly Form _engine;
    private readonly string _token;

    public ShellForm(Form engine, string token)
    {
        _engine = engine;
        _token = token;
        Text = "iPhoneLink";
        Icon = engine.Icon;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(430, 920);
        BackColor = Color.FromArgb(236, 236, 238);
        _web.Dock = DockStyle.Fill;
        Controls.Add(_web);
        Load += OnLoad;
        FormClosed += (_, _) =>
        {
            if (!_engine.IsDisposed) _engine.Close();
        };
    }

    private async void OnLoad(object? sender, EventArgs e)
    {
        var userData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "iPhoneLinkCRM",
            "webview");
        Directory.CreateDirectory(userData);
        var env = await Microsoft.Web.WebView2.Core.CoreWebView2Environment.CreateAsync(null, userData);
        await _web.EnsureCoreWebView2Async(env);
        _web.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
        _web.CoreWebView2.Settings.AreDevToolsEnabled = false;
        _web.CoreWebView2.Settings.IsStatusBarEnabled = false;
        _web.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = false;

        var tokenJson = JsonSerializer.Serialize(_token);
        await _web.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(
            $"window.IPHONELINK_TOKEN={tokenJson};window.IPHONELINK_BRIDGE='http://127.0.0.1:8765';");

        var html = Path.Combine(AppContext.BaseDirectory, "shell", "index.html");
        if (!File.Exists(html))
        {
            MessageBox.Show(this, "iPhoneLink shell is missing from the install folder.", "iPhoneLink", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Close();
            return;
        }
        _web.Source = new Uri(html);
    }
}

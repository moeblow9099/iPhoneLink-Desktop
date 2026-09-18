using PhoneLinkDiag.UI;

namespace PhoneLinkDiag;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        var engine = new MainForm();
        engine.ShowInTaskbar = false;
        engine.WindowState = FormWindowState.Minimized;
        engine.Opacity = 0;
        engine.StartPosition = FormStartPosition.Manual;
        engine.Location = new Point(-32000, -32000);
        engine.Shown += (_, _) => engine.Hide();
        engine.Show();

        Application.Run(new ShellForm(engine, ReadBridgeToken()));
    }

    private static string ReadBridgeToken()
    {
        var tokenPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "iPhoneLinkCRM",
            "bridge-token.txt");

        for (var i = 0; i < 20; i++)
        {
            if (File.Exists(tokenPath))
            {
                var token = File.ReadAllText(tokenPath).Trim();
                if (token.Length >= 32) return token;
            }
            Thread.Sleep(50);
        }

        return File.Exists(tokenPath) ? File.ReadAllText(tokenPath).Trim() : string.Empty;
    }
}

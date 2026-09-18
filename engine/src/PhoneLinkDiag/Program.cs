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

        var tokenPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "iPhoneLinkCRM",
            "bridge-token.txt");
        var token = File.Exists(tokenPath) ? File.ReadAllText(tokenPath).Trim() : string.Empty;

        Application.Run(new ShellForm(engine, token));
    }
}

using PhoneLinkDiag.Models;

namespace PhoneLinkDiag.UI;

public sealed class PacketInspectorForm : Form
{
    public PacketInspectorForm(PacketLogEntry entry)
    {
        Text = "Packet Inspector";
        Width = 1000;
        Height = 720;
        StartPosition = FormStartPosition.CenterParent;

        var box = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            Font = new Font("Consolas", 9),
            Text = BuildText(entry)
        };
        Controls.Add(box);
    }

    private static string BuildText(PacketLogEntry entry)
    {
        return string.Join(Environment.NewLine, new[]
        {
            $"Timestamp: {entry.Timestamp:O}",
            $"Layer: {entry.Layer}",
            $"Operation: {entry.Operation}",
            $"Success: {entry.Success}",
            $"Elapsed: {entry.ElapsedMilliseconds} ms",
            $"Status: {entry.Status}",
            $"MessageHandle: {entry.MessageHandle ?? ""}",
            $"Error: {entry.Error ?? ""}",
            "",
            "Request Hex:",
            entry.RequestHex,
            "",
            "Request Decoded:",
            entry.RequestDecoded,
            "",
            "Response Hex:",
            entry.ResponseHex,
            "",
            "Response Decoded:",
            entry.ResponseDecoded
        });
    }
}

using PhoneLinkDiag.Models;
using PhoneLinkDiag.Services;

namespace PhoneLinkDiag.UI;

public sealed class ConnectProfileComparisonForm : Form
{
    private readonly ComboBox _profiles = new();
    private readonly TextBox _details = new();
    private readonly IReadOnlyList<ConnectProfile> _connectProfiles;

    public ConnectProfileComparisonForm(IReadOnlyList<ConnectProfile> profiles)
    {
        _connectProfiles = profiles;
        Text = "CONNECT Profile Comparison";
        Width = 1000;
        Height = 720;
        StartPosition = FormStartPosition.CenterParent;

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Padding = new Padding(8) };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(root);

        _profiles.DropDownStyle = ComboBoxStyle.DropDownList;
        _profiles.Dock = DockStyle.Top;
        foreach (var profile in profiles)
        {
            _profiles.Items.Add(profile);
        }
        _profiles.DisplayMember = nameof(ConnectProfile.Name);
        _profiles.SelectedIndexChanged += (_, _) => ShowSelectedProfile();
        root.Controls.Add(_profiles, 0, 0);

        _details.Dock = DockStyle.Fill;
        _details.Multiline = true;
        _details.ReadOnly = true;
        _details.ScrollBars = ScrollBars.Both;
        _details.WordWrap = false;
        _details.Font = new Font("Consolas", 9);
        root.Controls.Add(_details, 0, 1);

        if (_profiles.Items.Count > 0)
        {
            _profiles.SelectedIndex = 0;
        }
    }

    private void ShowSelectedProfile()
    {
        if (_profiles.SelectedItem is not ConnectProfile profile) return;
        var decoded = ObexPacketParser.ToSummary(ObexPacketParser.Decode(profile.Packet));
        _details.Text = string.Join(Environment.NewLine, new[]
        {
            $"Current profile: {profile.Name}",
            $"Attempt #: {profile.AttemptNumber}",
            $"Spec basis: {profile.SpecBasis}",
            $"Max packet size: {profile.MaxPacketSize}",
            $"Includes MapSupportedFeatures: {profile.IncludeMapSupportedFeatures}",
            $"MapSupportedFeatures: 0x{profile.MapSupportedFeatures:X8}",
            "",
            "Packet hex:",
            HexUtil.ToHex(profile.Packet),
            "",
            "Decoded fields:",
            decoded,
            "",
            "Validation result:",
            profile.Validation.Summary,
            "",
            "Validation details:",
            string.Join(Environment.NewLine, profile.Validation.Details),
            "",
            "Validation errors:",
            profile.Validation.Errors.Count == 0 ? "None" : string.Join(Environment.NewLine, profile.Validation.Errors)
        });
    }
}

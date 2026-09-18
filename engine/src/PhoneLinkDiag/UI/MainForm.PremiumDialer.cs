using PhoneLinkDiag.Models;

namespace PhoneLinkDiag.UI;

public sealed partial class MainForm
{
    private const string PremiumUiFont = "Segoe UI Variable Display";

    private void ClearDialerPageForRebuild(Panel dialerPage)
    {
        var retained = new HashSet<Control>
        {
            _recentCallList,
            _phoneBox,
            _dialNumberBox,
            _messageBox,
            _dialButton,
            _hangupButton,
            _pushButton,
            _testHfpButton,
            _selectedLeadInfoLabel,
            _sessionStateLabel
        };

        foreach (Control child in dialerPage.Controls.Cast<Control>().ToArray())
        {
            DetachRetainedControls(child, retained);
            dialerPage.Controls.Remove(child);
            child.Dispose();
        }
    }

    private static void DetachRetainedControls(Control parent, HashSet<Control> retained)
    {
        foreach (Control child in parent.Controls.Cast<Control>().ToArray())
        {
            if (retained.Contains(child))
            {
                parent.Controls.Remove(child);
                continue;
            }
            DetachRetainedControls(child, retained);
        }
    }

    private void BuildPremiumDialerPage(Panel dialerPage)
    {
        ClearDialerPageForRebuild(dialerPage);

        var background = _dialerDarkMode ? Color.FromArgb(10, 10, 10) : Color.FromArgb(242, 243, 245);
        var surface = _dialerDarkMode ? Color.FromArgb(20, 20, 20) : Color.White;
        var surfaceAlt = _dialerDarkMode ? Color.FromArgb(30, 30, 30) : Color.FromArgb(248, 248, 249);
        var border = _dialerDarkMode ? Color.FromArgb(54, 54, 54) : Color.FromArgb(218, 220, 224);
        var text = _dialerDarkMode ? Color.FromArgb(245, 245, 247) : Color.FromArgb(22, 22, 24);
        var muted = _dialerDarkMode ? Color.FromArgb(166, 166, 172) : Color.FromArgb(102, 102, 108);
        var keyBackground = _dialerDarkMode ? Color.FromArgb(34, 34, 34) : Color.FromArgb(245, 245, 247);
        var actionBackground = _dialerDarkMode ? Color.FromArgb(245, 245, 247) : Color.FromArgb(24, 24, 26);
        var actionForeground = _dialerDarkMode ? Color.FromArgb(18, 18, 18) : Color.White;
        var green = Color.FromArgb(34, 166, 91);
        var red = Color.FromArgb(215, 63, 63);

        dialerPage.BackColor = background;
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            Padding = new Padding(24),
            BackColor = background
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 27));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 46));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 27));
        dialerPage.Controls.Add(root);

        Panel PanelCard(int padding = 16) => new()
        {
            Dock = DockStyle.Fill,
            BackColor = surface,
            Padding = new Padding(padding),
            Margin = new Padding(8),
            BorderStyle = BorderStyle.FixedSingle
        };

        Label Heading(string value, float size) => new()
        {
            Text = value,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = text,
            Font = new Font(PremiumUiFont, size, FontStyle.Regular)
        };

        Button NeutralButton(string value) 
        {
            var button = new Button
            {
                Text = value,
                Dock = DockStyle.Fill,
                FlatStyle = FlatStyle.Flat,
                BackColor = keyBackground,
                ForeColor = text,
                Font = new Font(PremiumUiFont, 9.5F, FontStyle.Regular),
                Cursor = Cursors.Hand,
                Margin = new Padding(4)
            };
            button.FlatAppearance.BorderColor = border;
            button.FlatAppearance.BorderSize = 1;
            return button;
        }

        var recentCard = PanelCard();
        var recentLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, BackColor = surface };
        recentLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        recentLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        recentLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        recentLayout.Controls.Add(Heading("Recent Calls", 11.5F), 0, 0);
        _recentCallList.Dock = DockStyle.Fill;
        _recentCallList.BorderStyle = BorderStyle.None;
        _recentCallList.BackColor = surface;
        _recentCallList.ForeColor = text;
        _recentCallList.Font = new Font(PremiumUiFont, 9.5F, FontStyle.Regular);
        recentLayout.Controls.Add(_recentCallList, 0, 1);
        var refreshCalls = NeutralButton("Sync Call History");
        refreshCalls.Click += async (_, _) => await SyncPbapDataAsync(includeContacts: false, includeCalls: true, showUserErrors: true);
        recentLayout.Controls.Add(refreshCalls, 0, 2);
        recentCard.Controls.Add(recentLayout);
        root.Controls.Add(recentCard, 0, 0);

        var dialCard = PanelCard(20);
        var dial = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 8, ColumnCount = 1, BackColor = surface };
        dial.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        dial.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        dial.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        dial.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        dial.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
        dial.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        dial.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        dial.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));

        var dialTop = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = surface };
        dialTop.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        dialTop.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));
        dialTop.Controls.Add(Heading("Dialer", 13F), 0, 0);
        var themeButton = NeutralButton(_dialerDarkMode ? "Light Mode" : "Dark Mode");
        themeButton.Click += (_, _) =>
        {
            _dialerDarkMode = !_dialerDarkMode;
            BuildPremiumDialerPage(dialerPage);
            RefreshPbapViews();
        };
        dialTop.Controls.Add(themeButton, 1, 0);
        dial.Controls.Add(dialTop, 0, 0);

        _phoneBox.PlaceholderText = "Enter phone number";
        _phoneBox.Dock = DockStyle.Fill;
        _phoneBox.BorderStyle = BorderStyle.FixedSingle;
        _phoneBox.BackColor = surfaceAlt;
        _phoneBox.ForeColor = text;
        _phoneBox.Font = new Font(PremiumUiFont, 19F, FontStyle.Regular);
        _phoneBox.TextAlign = HorizontalAlignment.Center;
        dial.Controls.Add(_phoneBox, 0, 1);

        _dialNumberBox.PlaceholderText = "Ready to dial";
        _dialNumberBox.Dock = DockStyle.Fill;
        _dialNumberBox.BorderStyle = BorderStyle.None;
        _dialNumberBox.BackColor = surface;
        _dialNumberBox.ForeColor = muted;
        _dialNumberBox.Font = new Font(PremiumUiFont, 10F, FontStyle.Regular);
        _dialNumberBox.TextAlign = HorizontalAlignment.Center;
        dial.Controls.Add(_dialNumberBox, 0, 2);

        var keypad = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 4, Margin = new Padding(18, 8, 18, 8), BackColor = surface };
        for (var row = 0; row < 4; row++) keypad.RowStyles.Add(new RowStyle(SizeType.Percent, 25));
        for (var column = 0; column < 3; column++) keypad.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333F));
        foreach (var key in new[] { "1", "2\nABC", "3\nDEF", "4\nGHI", "5\nJKL", "6\nMNO", "7\nPQRS", "8\nTUV", "9\nWXYZ", "*", "0\n+", "#" })
        {
            var keyButton = NeutralButton(key);
            keyButton.Font = new Font(PremiumUiFont, 13F, FontStyle.Regular);
            keyButton.Margin = new Padding(7);
            keyButton.Click += (_, _) =>
            {
                _phoneBox.Text += key[0];
                _dialNumberBox.Text = NormalizePhoneForAction(_phoneBox.Text);
            };
            keypad.Controls.Add(keyButton);
        }
        dial.Controls.Add(keypad, 0, 3);

        _messageBox.PlaceholderText = "Optional SMS message";
        _messageBox.Multiline = true;
        _messageBox.Dock = DockStyle.Fill;
        _messageBox.BorderStyle = BorderStyle.FixedSingle;
        _messageBox.BackColor = surfaceAlt;
        _messageBox.ForeColor = text;
        _messageBox.Font = new Font(PremiumUiFont, 10F, FontStyle.Regular);
        dial.Controls.Add(_messageBox, 0, 4);

        var actions = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, BackColor = surface };
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.34F));
        ConfigureActionButton(_dialButton, "Call", green, Color.White, border);
        ConfigureActionButton(_hangupButton, "End", red, Color.White, border);
        ConfigureActionButton(_pushButton, "Send SMS", actionBackground, actionForeground, border);
        actions.Controls.Add(_dialButton, 0, 0);
        actions.Controls.Add(_hangupButton, 1, 0);
        actions.Controls.Add(_pushButton, 2, 0);
        dial.Controls.Add(actions, 0, 5);

        _testHfpButton.Text = "Diagnostics: Test HFP";
        _testHfpButton.Dock = DockStyle.Fill;
        _testHfpButton.FlatStyle = FlatStyle.Flat;
        _testHfpButton.BackColor = surfaceAlt;
        _testHfpButton.ForeColor = muted;
        _testHfpButton.Font = new Font(PremiumUiFont, 9F, FontStyle.Regular);
        _testHfpButton.FlatAppearance.BorderColor = border;
        _testHfpButton.FlatAppearance.BorderSize = 1;
        dial.Controls.Add(_testHfpButton, 0, 6);

        var note = new Label
        {
            Text = "HFP call path locked · raw AT/HFP/MAP traffic stays in Diagnostics",
            Dock = DockStyle.Fill,
            ForeColor = muted,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font(PremiumUiFont, 8.5F, FontStyle.Regular)
        };
        dial.Controls.Add(note, 0, 7);
        dialCard.Controls.Add(dial);
        root.Controls.Add(dialCard, 1, 0);

        var infoCard = PanelCard();
        var info = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 7, ColumnCount = 1, BackColor = surface };
        info.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        info.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
        info.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
        info.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        info.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        info.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        info.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        info.Controls.Add(Heading("Phone & Contact Access", 11.5F), 0, 0);
        _selectedLeadInfoLabel.Text = string.IsNullOrWhiteSpace(_selectedLeadInfoLabel.Text) ? "No lead selected." : _selectedLeadInfoLabel.Text;
        _selectedLeadInfoLabel.Dock = DockStyle.Fill;
        _selectedLeadInfoLabel.BackColor = surfaceAlt;
        _selectedLeadInfoLabel.ForeColor = text;
        _selectedLeadInfoLabel.Padding = new Padding(8);
        _selectedLeadInfoLabel.Font = new Font(PremiumUiFont, 9F, FontStyle.Regular);
        info.Controls.Add(_selectedLeadInfoLabel, 0, 1);
        _sessionStateLabel.Dock = DockStyle.Fill;
        _sessionStateLabel.BackColor = surface;
        _sessionStateLabel.ForeColor = muted;
        _sessionStateLabel.Padding = new Padding(6);
        _sessionStateLabel.Font = new Font(PremiumUiFont, 8.8F, FontStyle.Regular);
        info.Controls.Add(_sessionStateLabel, 0, 2);
        var syncContacts = NeutralButton("Sync Contacts");
        syncContacts.Click += async (_, _) =>
        {
            if (!_syncContactsToggle.Checked)
            {
                _syncContactsToggle.Checked = true;
                return;
            }
            await SyncPbapDataAsync(includeContacts: true, includeCalls: false, showUserErrors: true);
        };
        info.Controls.Add(syncContacts, 0, 3);
        var syncHistory = NeutralButton("Sync Calls");
        syncHistory.Click += async (_, _) => await SyncPbapDataAsync(includeContacts: false, includeCalls: true, showUserErrors: true);
        info.Controls.Add(syncHistory, 0, 4);
        info.Controls.Add(new Label
        {
            Text = "Messages, contacts, and call history sync automatically when a phone is Bluetooth-connected. Sync Contacts / Show Messages toggles are not required.",
            Dock = DockStyle.Fill,
            ForeColor = muted,
            Font = new Font(PremiumUiFont, 9F, FontStyle.Regular),
            Padding = new Padding(4),
            TextAlign = ContentAlignment.TopLeft
        }, 0, 5);
        var deviceHint = new Label
        {
            Text = "Each phone keeps separate message, contact, and call-history data. Switching phones creates a clean per-device session.",
            Dock = DockStyle.Fill,
            ForeColor = muted,
            Font = new Font(PremiumUiFont, 8.5F, FontStyle.Regular),
            TextAlign = ContentAlignment.MiddleLeft
        };
        info.Controls.Add(deviceHint, 0, 6);
        infoCard.Controls.Add(info);
        root.Controls.Add(infoCard, 2, 0);

        RefreshPbapViews();
    }

    private void BuildCallHistoryPage(Panel callsPage)
    {
        callsPage.Controls.Clear();
        callsPage.BackColor = Color.FromArgb(242, 243, 245);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Padding = new Padding(16), BackColor = Color.FromArgb(242, 243, 245) };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var top = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
        top.Controls.Add(new Label { Text = "Call History", Dock = DockStyle.Fill, ForeColor = Color.FromArgb(24,24,26), Font = new Font(PremiumUiFont, 13F), TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        _syncCallLogButton.Text = "Sync from Phone";
        _syncCallLogButton.Dock = DockStyle.Fill;
        _syncCallLogButton.FlatStyle = FlatStyle.Flat;
        _syncCallLogButton.BackColor = Color.FromArgb(24, 24, 26);
        _syncCallLogButton.ForeColor = Color.White;
        _syncCallLogButton.FlatAppearance.BorderSize = 0;
        top.Controls.Add(_syncCallLogButton, 1, 0);
        layout.Controls.Add(top, 0, 0);
        ConfigureCallGrid();
        layout.Controls.Add(_callLogGrid, 0, 1);
        callsPage.Controls.Add(layout);
    }

    private void BuildContactsPage(Panel contactsPage)
    {
        contactsPage.Controls.Clear();
        contactsPage.BackColor = Color.FromArgb(242, 243, 245);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Padding = new Padding(16), BackColor = Color.FromArgb(242, 243, 245) };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var top = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1 };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
        top.Controls.Add(new Label { Text = "Contacts", Dock = DockStyle.Fill, ForeColor = Color.FromArgb(24,24,26), Font = new Font(PremiumUiFont, 13F), TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        _syncContactsToggle.Text = "Sync Contacts";
        _syncContactsToggle.Checked = false;
        _syncContactsToggle.Dock = DockStyle.Fill;
        _syncContactsToggle.TextAlign = ContentAlignment.MiddleCenter;
        _syncContactsToggle.Font = new Font(PremiumUiFont, 9F, FontStyle.Regular);
        _syncContactsToggle.ForeColor = Color.FromArgb(64, 64, 68);
        top.Controls.Add(_syncContactsToggle, 1, 0);
        _syncContactsButton.Text = "Sync Now";
        _syncContactsButton.Dock = DockStyle.Fill;
        _syncContactsButton.FlatStyle = FlatStyle.Flat;
        _syncContactsButton.BackColor = Color.FromArgb(24, 24, 26);
        _syncContactsButton.ForeColor = Color.White;
        _syncContactsButton.FlatAppearance.BorderSize = 0;
        top.Controls.Add(_syncContactsButton, 2, 0);
        layout.Controls.Add(top, 0, 0);
        ConfigureContactsGrid();
        layout.Controls.Add(_contactsGrid, 0, 1);
        contactsPage.Controls.Add(layout);
    }

    private static void ConfigureActionButton(Button button, string text, Color background, Color foreground, Color border)
    {
        button.Text = text;
        button.Dock = DockStyle.Fill;
        button.Margin = new Padding(4);
        button.FlatStyle = FlatStyle.Flat;
        button.BackColor = background;
        button.ForeColor = foreground;
        button.Font = new Font(PremiumUiFont, 10F, FontStyle.Regular);
        button.FlatAppearance.BorderColor = border;
        button.FlatAppearance.BorderSize = 0;
    }

    private void ConfigureContactsGrid()
    {
        _contactsGrid.Columns.Clear();
        _contactsGrid.Rows.Clear();
        _contactsGrid.Dock = DockStyle.Fill;
        _contactsGrid.ReadOnly = true;
        _contactsGrid.AllowUserToAddRows = false;
        _contactsGrid.AllowUserToDeleteRows = false;
        _contactsGrid.RowHeadersVisible = false;
        _contactsGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _contactsGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _contactsGrid.BackgroundColor = Color.White;
        _contactsGrid.BorderStyle = BorderStyle.None;
        _contactsGrid.Font = new Font(PremiumUiFont, 9F, FontStyle.Regular);
        _contactsGrid.Columns.Add("Device", "Phone");
        _contactsGrid.Columns.Add("Name", "Name");
        _contactsGrid.Columns.Add("Phones", "Numbers");
        _contactsGrid.Columns.Add("Emails", "Emails");
        _contactsGrid.Columns.Add("Organization", "Organization");
    }

    private void ConfigureCallGrid()
    {
        _callLogGrid.Columns.Clear();
        _callLogGrid.Rows.Clear();
        _callLogGrid.Dock = DockStyle.Fill;
        _callLogGrid.ReadOnly = true;
        _callLogGrid.AllowUserToAddRows = false;
        _callLogGrid.AllowUserToDeleteRows = false;
        _callLogGrid.RowHeadersVisible = false;
        _callLogGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _callLogGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _callLogGrid.BackgroundColor = Color.White;
        _callLogGrid.BorderStyle = BorderStyle.None;
        _callLogGrid.Font = new Font(PremiumUiFont, 9F, FontStyle.Regular);
        _callLogGrid.Columns.Add("Device", "Phone");
        _callLogGrid.Columns.Add("Type", "Type");
        _callLogGrid.Columns.Add("Name", "Name");
        _callLogGrid.Columns.Add("Number", "Number");
        _callLogGrid.Columns.Add("Timestamp", "Time");
    }

    private async Task<bool> SyncPbapDataAsync(
        bool includeContacts = true,
        bool includeCalls = true,
        bool showUserErrors = true,
        string? targetDeviceId = null)
    {
        await _pbapWorkflowGate.WaitAsync(_appCts.Token);
        SetBusy(includeContacts && includeCalls
            ? "Refreshing contacts and call history..."
            : includeContacts
                ? "Refreshing contacts..."
                : "Refreshing call history...");
        try
        {
            await _bluetooth.RefreshPairedDevicesAsync(_appCts.Token);
            RefreshDevices();

            var devices = _bluetooth.CurrentDevices
                .Where(device =>
                    device.EffectiveIsPaired &&
                    (IsLikelyMobilePhone(device) ||
                     (!string.IsNullOrWhiteSpace(targetDeviceId) && string.Equals(device.Id, targetDeviceId, StringComparison.OrdinalIgnoreCase))))
                .Where(device =>
                    string.IsNullOrWhiteSpace(targetDeviceId)
                        ? IsConnectedDevice(device)
                        : string.Equals(device.Id, targetDeviceId, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(IsConnectedDevice)
                .ThenBy(device => device.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(device => device.Id, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (devices.Length == 0)
            {
                if (showUserErrors)
                {
                    MessageBox.Show(this, "No paired mobile phone is currently available. Reconnect the phone and refresh Bluetooth devices.", "No phone available", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                return false;
            }

            var totalContacts = 0;
            var totalCalls = 0;
            var syncedDevices = new List<string>();
            var failedDevices = new List<string>();

            foreach (var device in devices)
            {
                _appCts.Token.ThrowIfCancellationRequested();
                _statusLabel.Text = includeContacts && includeCalls
                    ? $"Syncing contacts and call history from {device.Name}..."
                    : includeContacts
                        ? $"Syncing contacts from {device.Name}..."
                        : $"Syncing call history from {device.Name}...";

                try
                {
                    var result = await _pbapData.SyncAsync(
                        device,
                        ushort.MaxValue,
                        includeContacts,
                        includeCalls,
                        _appCts.Token);

                    if (includeContacts)
                    {
                        _currentContacts.RemoveAll(c => c.DeviceId.Equals(device.Id, StringComparison.OrdinalIgnoreCase));
                        _currentContacts.AddRange(result.Contacts);
                        totalContacts += result.Contacts.Count;

                        // Contact sync is optional, but when the user enables it later,
                        // immediately enrich already-loaded call records for this same phone.
                        // This avoids requiring a second call-history download just to replace
                        // number-only rows with contact names.
                        var existingCalls = _currentCallHistory
                            .Where(call => call.DeviceId.Equals(device.Id, StringComparison.OrdinalIgnoreCase))
                            .ToArray();
                        if (existingCalls.Length > 0)
                        {
                            var renamedCalls = ResolveCallNamesFromCachedContacts(device.Id, existingCalls);
                            _currentCallHistory.RemoveAll(call => call.DeviceId.Equals(device.Id, StringComparison.OrdinalIgnoreCase));
                            _currentCallHistory.AddRange(renamedCalls);
                        }
                    }

                    if (includeCalls)
                    {
                        var resolvedCalls = ResolveCallNamesFromCachedContacts(device.Id, result.Calls);
                        _currentCallHistory.RemoveAll(c => c.DeviceId.Equals(device.Id, StringComparison.OrdinalIgnoreCase));
                        _currentCallHistory.AddRange(resolvedCalls);
                        totalCalls += resolvedCalls.Count;
                    }

                    syncedDevices.Add(device.Name);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.Warn("PBAP", $"{device.Name}: {ex.Message}");
                    failedDevices.Add(device.Name);
                }
            }

            RefreshPbapViews();

            if (syncedDevices.Count > 0)
            {
                var detail = includeContacts && includeCalls
                    ? $"{totalContacts} contacts · {totalCalls} call records"
                    : includeContacts
                        ? $"{totalContacts} contacts"
                        : $"{totalCalls} call records";
                AddUserNotification(
                    includeContacts ? "Phone Data Synced" : "Call History Synced",
                    $"{detail} · {syncedDevices.Count} phone(s)",
                    $"pbap:{includeContacts}:{includeCalls}:{string.Join("|", syncedDevices)}:{totalContacts}:{totalCalls}");
                _statusLabel.Text = $"Synced {detail} from {syncedDevices.Count} phone(s).";
            }

            if (failedDevices.Count > 0 && showUserErrors)
            {
                var item = includeContacts ? "contact/call data" : "call history";
                MessageBox.Show(this, $"{item} was unavailable for: {string.Join(", ", failedDevices)}. Reconnect the affected phone and retry. Raw PBAP/OBEX details are in Developer Diagnostics.", "Partial phone-data sync", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            return syncedDevices.Count > 0 && failedDevices.Count == 0;
        }
        finally
        {
            SetReady();
            _pbapWorkflowGate.Release();
        }
    }

    private IReadOnlyList<CallHistoryRecord> ResolveCallNamesFromCachedContacts(
        string deviceId,
        IReadOnlyList<CallHistoryRecord> calls)
    {
        var nameByPhone = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var contact in _currentContacts.Where(contact => contact.DeviceId.Equals(deviceId, StringComparison.OrdinalIgnoreCase)))
        {
            if (string.IsNullOrWhiteSpace(contact.Name) || contact.Name.Equals("Unknown contact", StringComparison.OrdinalIgnoreCase)) continue;
            foreach (var phone in contact.Phones)
            {
                var key = NormalizePhoneLookup(phone);
                if (key.Length > 0 && !nameByPhone.ContainsKey(key))
                {
                    nameByPhone[key] = contact.Name;
                }
            }
        }

        return calls.Select(call =>
        {
            var key = NormalizePhoneLookup(call.Phone);
            var currentName = call.Name?.Trim() ?? string.Empty;
            var currentNameDigits = NormalizePhoneLookup(currentName);
            var nameMissing = currentName.Length == 0 ||
                              currentName.Equals(call.Phone, StringComparison.OrdinalIgnoreCase) ||
                              (currentNameDigits.Length >= 3 && currentNameDigits == key);
            return nameMissing && key.Length > 0 && nameByPhone.TryGetValue(key, out var contactName)
                ? call with { Name = contactName }
                : call;
        }).ToArray();
    }

    private static string NormalizePhoneLookup(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var digits = new string(value.Where(char.IsDigit).ToArray());
        return digits.Length == 11 && digits.StartsWith("1", StringComparison.Ordinal) ? digits[1..] : digits;
    }

    private void RefreshPbapViews()
    {
        var selectedDeviceId = _preferredSelectedDeviceId ?? CurrentSelectedDevice()?.Id;

        if (_contactsGrid.Columns.Count > 0)
        {
            _contactsGrid.Rows.Clear();
            foreach (var contact in _currentContacts
                .Where(contact => string.IsNullOrWhiteSpace(selectedDeviceId) ||
                                  contact.DeviceId.Equals(selectedDeviceId, StringComparison.OrdinalIgnoreCase))
                .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(c => c.DeviceName, StringComparer.OrdinalIgnoreCase))
            {
                _contactsGrid.Rows.Add(contact.DeviceName, contact.Name, string.Join(" · ", contact.Phones), string.Join(" · ", contact.Emails), contact.Organization);
            }
        }

        var sortedCalls = _currentCallHistory
            .Where(call => string.IsNullOrWhiteSpace(selectedDeviceId) ||
                           call.DeviceId.Equals(selectedDeviceId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(call => ParseCallTimestamp(call.Timestamp) ?? DateTimeOffset.MinValue)
            .ThenBy(call => call.DeviceName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (_callLogGrid.Columns.Count > 0)
        {
            _callLogGrid.Rows.Clear();
            foreach (var call in sortedCalls)
            {
                _callLogGrid.Rows.Add(call.DeviceName, call.Type, call.Name, FormatPhoneDisplay(call.Phone), FormatCallTimestamp(call.Timestamp));
            }
        }

        _recentCallList.Items.Clear();
        foreach (var call in sortedCalls.Take(50))
        {
            var who = string.IsNullOrWhiteSpace(call.Name) ? FormatPhoneDisplay(call.Phone) : call.Name;
            _recentCallList.Items.Add($"{call.Type} · {who} · {FormatCallTimestamp(call.Timestamp)}");
        }
        if (_recentCallList.Items.Count == 0) _recentCallList.Items.Add("No call history synced yet.");
        RefreshDashboardMetrics();
    }

    private static DateTimeOffset? ParseCallTimestamp(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var formats = new[]
        {
            "yyyyMMdd'T'HHmmss'Z'",
            "yyyyMMdd'T'HHmmss",
            "yyyyMMdd'T'HHmmsszzz",
            "yyyyMMddHHmmss",
            "M/d/yyyy h:mm:ss tt",
            "M/d/yyyy h:mm tt"
        };
        if (DateTimeOffset.TryParseExact(value.Trim(), formats, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeLocal, out var exact))
            return exact;
        return DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;
    }

    private static string FormatCallTimestamp(string value)
        => ParseCallTimestamp(value)?.ToLocalTime().ToString("g") ?? value;

    private void RecordCallStarted(string phoneNumber, DeviceRecord device)
    {
        var display = FormatPhoneDisplay(phoneNumber);
        var key = $"call-start:{device.Id}:{NormalizePhoneForAction(phoneNumber)}";
        if (string.Equals(_activeCallNotificationKey, key, StringComparison.OrdinalIgnoreCase)) return;
        _activeCallNotificationKey = key;
        _activeCallDeviceName = device.Name;
        _currentCallHistory.Insert(0, new CallHistoryRecord(device.Id, device.Name, "Outgoing", string.Empty, phoneNumber, DateTimeOffset.Now.ToString("g")));
        RefreshPbapViews();
        AddUserNotification("Call Started", $"{display} · {device.Name}", key);
    }

    private void RecordCallEnded(DeviceRecord? device)
    {
        if (string.IsNullOrWhiteSpace(_activeCallNotificationKey)) return;
        AddUserNotification("Call Ended", ActiveCallDeviceName(device), "call-end:" + _activeCallNotificationKey);
        _activeCallNotificationKey = null;
        _activeCallDeviceName = null;
    }

    private string ActiveCallDeviceName(DeviceRecord? fallback = null)
        => !string.IsNullOrWhiteSpace(_activeCallDeviceName)
            ? _activeCallDeviceName
            : fallback?.Name ?? "Phone";

    private void AddUserNotification(string title, string detail, string dedupeKey)
    {
        title = CleanUserEventText(title);
        detail = SanitizeNotificationDetail(detail);
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(detail)) return;
        var now = DateTimeOffset.UtcNow;
        if (_notificationDedup.Count > 500)
        {
            var cutoff = now - TimeSpan.FromMinutes(10);
            foreach (var staleKey in _notificationDedup
                         .Where(pair => pair.Value < cutoff)
                         .Select(pair => pair.Key)
                         .ToArray())
            {
                _notificationDedup.Remove(staleKey);
            }
        }

        if (_notificationDedup.TryGetValue(dedupeKey, out var previous) && now - previous < TimeSpan.FromSeconds(3)) return;
        _notificationDedup[dedupeKey] = now;

        if (_notificationList.Items.Count == 1 && string.Equals(Convert.ToString(_notificationList.Items[0]), "No user-facing events yet.", StringComparison.Ordinal))
            _notificationList.Items.Clear();
        _notificationList.Items.Insert(0, $"{title} · {detail}");
        while (_notificationList.Items.Count > 100) _notificationList.Items.RemoveAt(_notificationList.Items.Count - 1);
    }

    private static string SanitizeNotificationDetail(string value)
    {
        var cleaned = CleanUserEventText(value);
        if (string.IsNullOrWhiteSpace(cleaned)) return string.Empty;
        var upper = cleaned.ToUpperInvariant();
        return IsRawProtocolTraffic(upper) ? string.Empty : cleaned;
    }

    private static bool IsRawProtocolTraffic(string upper)
    {
        if (string.IsNullOrWhiteSpace(upper)) return false;

        var rawProtocolMarkers = new[]
        {
            "+CIND", "+CIEV", "+BRSF", "+CMER", "+CHLD", "+CLCC", "+CLIP", "+VGS", "+VGM",
            "AT+", "ATD", "ATA", "ATH", "RFCOMM", "OBEX", "HFP", "TX:", "RX:", "0X"
        };

        if (rawProtocolMarkers.Any(upper.Contains)) return true;
        return upper is "OK" or "ERROR" or "ERROR!" or "RING";
    }

    private static bool IsExplicitHfpCallFailure(string response)
    {
        var cleaned = CleanUserEventText(response).ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(cleaned) || cleaned.Contains("<NO RESPONSE>", StringComparison.Ordinal)) return false;

        // Only explicit call-state failures are user-facing. Generic ERROR is intentionally
        // diagnostics-only because optional HFP initialization/probe commands can return ERROR
        // while the validated ATD dial command still succeeds.
        if (cleaned.Contains("NO CARRIER", StringComparison.Ordinal)
            || cleaned.Contains("NO ANSWER", StringComparison.Ordinal)
            || cleaned.Contains("BUSY", StringComparison.Ordinal)
            || cleaned.Contains("+CME ERROR", StringComparison.Ordinal)
            || cleaned.Contains("+CMS ERROR", StringComparison.Ordinal))
        {
            return true;
        }

        return false;
    }

    private static string ToDiagnosticText(string value)
        => CleanUserEventText(value);

    private static string CleanUserEventText(string value)
        => (value ?? string.Empty)
            .Replace("\\r\\n", " ", StringComparison.Ordinal)
            .Replace("\\r", " ", StringComparison.Ordinal)
            .Replace("\\n", " ", StringComparison.Ordinal)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Trim();



}

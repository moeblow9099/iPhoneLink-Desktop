using PhoneLinkDiag.Models;
using PhoneLinkDiag.Services;
using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Windows.Devices.Bluetooth.Rfcomm;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;

namespace PhoneLinkDiag.UI;

public sealed partial class MainForm : Form
{
    private readonly DiagnosticLogger _logger = new();
    private readonly BluetoothDiagnosticService _bluetooth;
    private readonly ObexRfcommClient _obex;
    private readonly MapSessionClient _mapSession;
    private readonly HfpDialerClient _hfpDialer;
    private readonly PbapDataClient _pbapData;
    private readonly CancellationTokenSource _appCts = new();
    private readonly SemaphoreSlim _mapWorkflowGate = new(1, 1);
    private readonly SemaphoreSlim _pbapWorkflowGate = new(1, 1);
    private readonly System.Windows.Forms.Timer _deviceRefreshTimer = new();
    private bool _deviceRefreshInProgress;
    private bool _suppressDeviceSelectionEvents;
    private string? _preferredSelectedDeviceId;
    private readonly HashSet<string> _autoSyncedConnectedDeviceIds = new(StringComparer.OrdinalIgnoreCase);

    private readonly ListBox _deviceList = new();
    private readonly DataGridView _profileGrid = new();
    private readonly DataGridView _messageGrid = new();
    private readonly TextBox _logBox = new();
    private readonly Button _startScanButton = new();
    private readonly Button _refreshPairedButton = new();
    private readonly Button _stopScanButton = new();
    private readonly Button _autoIphoneMapTestButton = new();
    private readonly Button _fullAutoMapTestButton = new();
    private readonly Button _fastAllButton = new();
    private readonly Button _readInboxFastButton = new();
    private readonly Button _loadCachedInboxButton = new();
    private readonly Button _exportShownInboxButton = new();
    private readonly Button _openCacheFolderButton = new();
    private readonly ComboBox _readCountBox = new();
    private readonly TextBox _messageSearchBox = new();
    private readonly Button _capabilityButton = new();
    private readonly Button _listServicesButton = new();
    private readonly Button _dumpPropertiesButton = new();
    private readonly Button _obexButton = new();
    private readonly Button _connectMapButton = new();
    private readonly Button _disconnectMapButton = new();
    private readonly Button _obexConnectButton = new();
    private readonly Button _runConnectProfilesButton = new();
    private readonly Button _compareConnectProfilesButton = new();
    private readonly Button _listMapFoldersButton = new();
    private readonly Button _listMessagesButton = new();
    private readonly Button _readSelectedMessageButton = new();
    private readonly Button _exportPacketLogButton = new();
    private readonly Button _exportSessionReportButton = new();
    private readonly Button _pushButton = new();
    private readonly Button _saveLogButton = new();
    private readonly TextBox _phoneBox = new();
    private readonly TextBox _messageBox = new();
    private readonly TextBox _dialNumberBox = new();
    private readonly Button _testHfpButton = new();
    private readonly Button _dialButton = new();
    private readonly Button _hangupButton = new();
    private readonly Label _statusLabel = new();
    private readonly Label _sessionStateLabel = new();
    private readonly DataGridView _contactsGrid = new();
    private readonly DataGridView _callLogGrid = new();
    private readonly Button _syncContactsButton = new();
    private readonly Button _syncCallLogButton = new();
    private readonly CheckBox _syncContactsToggle = new();
    private readonly ListBox _notificationList = new();
    private readonly ListBox _recentCallList = new();
    private readonly Label _selectedLeadInfoLabel = new();
    private readonly Label _messagesMetricLabel = new();
    private readonly Label _contactsMetricLabel = new();
    private readonly Label _callsMetricLabel = new();
    private readonly Label _phonesMetricLabel = new();
    private readonly List<ContactRecord> _currentContacts = new();
    private readonly List<CallHistoryRecord> _currentCallHistory = new();
    private readonly Dictionary<string, DateTimeOffset> _notificationDedup = new(StringComparer.OrdinalIgnoreCase);
    private bool _dialerDarkMode;
    private string? _activeCallNotificationKey;
    private string? _activeCallDeviceName;
    private string? _lastLoggedSelectedDeviceId;
    private readonly List<InboxMessageView> _currentInboxMessages = new();
    private bool _messageGridConfigured;

    public MainForm()
    {
        _bluetooth = new BluetoothDiagnosticService(_logger);
        _obex = new ObexRfcommClient(_logger);
        _mapSession = new MapSessionClient(_logger, _bluetooth);
        _hfpDialer = new HfpDialerClient(_logger, _bluetooth);
        _pbapData = new PbapDataClient(_logger, _bluetooth);
        Text = "PhoneLink Clean-Room Bluetooth Diagnostics";
        Width = 1180;
        Height = 760;
        MinimumSize = new Size(980, 620);
        StartPosition = FormStartPosition.CenterScreen;

        BuildUi();
        BindEvents();
        InitializeIPhoneLinkV1();
    }

    private void BuildUi()
    {
        Text = "iPhoneLink CRM";
        Width = 1680;
        Height = 940;
        MinimumSize = new Size(1240, 760);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(247, 249, 252);
        Font = new Font("Segoe UI Variable Display", 9F, FontStyle.Regular, GraphicsUnit.Point);

        var navy = Color.FromArgb(4, 35, 73);
        var navy2 = Color.FromArgb(8, 56, 112);
        var blue = Color.FromArgb(17, 88, 203);
        var border = Color.FromArgb(218, 225, 236);
        var soft = Color.FromArgb(250, 252, 255);
        var text = Color.FromArgb(23, 32, 44);
        var muted = Color.FromArgb(91, 103, 118);
        var green = Color.FromArgb(28, 145, 78);
        var red = Color.FromArgb(205, 54, 54);

        Controls.Clear();

        Button Primary(string caption)
        {
            var b = new Button
            {
                Text = caption,
                Height = 38,
                BackColor = blue,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                Font = new Font("Segoe UI Variable Display", 9F, FontStyle.Bold)
            };
            b.FlatAppearance.BorderSize = 0;
            b.FlatAppearance.MouseOverBackColor = navy2;
            return b;
        }

        Button SoftButton(string caption)
        {
            var b = new Button
            {
                Text = caption,
                Height = 34,
                BackColor = Color.White,
                ForeColor = navy,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                Font = new Font("Segoe UI Variable Display", 9F, FontStyle.Regular)
            };
            b.FlatAppearance.BorderColor = border;
            b.FlatAppearance.BorderSize = 1;
            return b;
        }

        Label Title(string caption, float size = 12F) => new()
        {
            Text = caption,
            AutoSize = false,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = text,
            Font = new Font("Segoe UI Variable Display", size, FontStyle.Bold)
        };

        Panel Card(int padding = 12)
        {
            return new Panel
            {
                BackColor = Color.White,
                Padding = new Padding(padding),
                Margin = new Padding(8),
                BorderStyle = BorderStyle.FixedSingle
            };
        }

        DataGridView Grid()
        {
            var g = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                ReadOnly = true,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                RowHeadersVisible = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.None,
                GridColor = border,
                EnableHeadersVisualStyles = false,
                RowTemplate = { Height = 34 },
                ColumnHeadersHeight = 36,
                ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
                {
                    BackColor = Color.FromArgb(242, 246, 252),
                    ForeColor = navy,
                    Font = new Font("Segoe UI Variable Display", 8.6F, FontStyle.Bold),
                    SelectionBackColor = Color.FromArgb(242, 246, 252),
                    SelectionForeColor = navy
                },
                DefaultCellStyle = new DataGridViewCellStyle
                {
                    BackColor = Color.White,
                    ForeColor = text,
                    SelectionBackColor = Color.FromArgb(226, 237, 255),
                    SelectionForeColor = text,
                    Font = new Font("Segoe UI Variable Display", 8.6F, FontStyle.Regular)
                }
            };
            return g;
        }

        var app = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Color.FromArgb(247, 249, 252) };
        app.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 68));
        app.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        Controls.Add(app);

        var rail = new Panel { Dock = DockStyle.Fill, BackColor = navy, Padding = new Padding(0, 12, 0, 12) };
        app.Controls.Add(rail, 0, 0);

        var shell = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = Color.FromArgb(247, 249, 252) };
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        app.Controls.Add(shell, 1, 0);

        var top = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 7, RowCount = 1, BackColor = Color.White, Padding = new Padding(14, 10, 14, 8) };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 260));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 86));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 132));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 132));
        shell.Controls.Add(top, 0, 0);

        var brand = new Label { Text = "iPhoneLink CRM", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = navy, Font = new Font("Segoe UI Variable Display", 16F, FontStyle.Bold) };
        top.Controls.Add(brand, 0, 0);
        var globalSearch = new TextBox { PlaceholderText = "Search leads, phones, emails, messages...", Dock = DockStyle.Fill, Margin = new Padding(8, 4, 8, 4) };
        top.Controls.Add(globalSearch, 1, 0);
        _deviceList.Dock = DockStyle.Fill;
        _deviceList.Height = 36;
        _deviceList.SelectionMode = SelectionMode.One;
        _deviceList.IntegralHeight = false;
        top.Controls.Add(_deviceList, 2, 0);
        _refreshPairedButton.Text = "Refresh Phones";
        _refreshPairedButton.Dock = DockStyle.Fill;
        top.Controls.Add(_refreshPairedButton, 3, 0);
        _readCountBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _readCountBox.Items.Clear();
        _readCountBox.Items.AddRange(new object[] { "10", "25", "50", "100", "250" });
        _readCountBox.SelectedIndex = 4;
        _readCountBox.Dock = DockStyle.Fill;
        top.Controls.Add(_readCountBox, 4, 0);
        _readInboxFastButton.Text = "Sync Messages";
        _readInboxFastButton.Dock = DockStyle.Fill;
        _readInboxFastButton.BackColor = blue;
        _readInboxFastButton.ForeColor = Color.White;
        _readInboxFastButton.FlatStyle = FlatStyle.Flat;
        _readInboxFastButton.FlatAppearance.BorderSize = 0;
        top.Controls.Add(_readInboxFastButton, 5, 0);
        var alertsButton = SoftButton("Notifications");
        alertsButton.Dock = DockStyle.Fill;
        top.Controls.Add(alertsButton, 6, 0);

        var body = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(247, 249, 252), Padding = new Padding(10) };
        shell.Controls.Add(body, 0, 1);

        _statusLabel.Text = "Ready. Select a lead, read inbox, send SMS, or open the dialer.";
        _statusLabel.Dock = DockStyle.Fill;
        _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        _statusLabel.ForeColor = muted;
        _statusLabel.BackColor = Color.White;
        _statusLabel.Padding = new Padding(14, 0, 0, 0);
        shell.Controls.Add(_statusLabel, 0, 2);

        var pages = new List<Panel>();
        Panel NewPage()
        {
            var p = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(247, 249, 252), Visible = false };
            body.Controls.Add(p);
            pages.Add(p);
            return p;
        }

        void ShowPage(Panel page, string name)
        {
            foreach (var p in pages) p.Visible = false;
            page.Visible = true;
            page.BringToFront();
            _statusLabel.Text = $"{name} page active.";
        }

        Button RailButton(string textValue, Panel page, string pageName)
        {
            var b = new Button
            {
                Text = textValue,
                Dock = DockStyle.Top,
                Height = 56,
                FlatStyle = FlatStyle.Flat,
                BackColor = navy,
                ForeColor = Color.White,
                TextAlign = ContentAlignment.MiddleCenter,
                Cursor = Cursors.Hand,
                Font = new Font("Segoe UI Variable Display", 8.5F, FontStyle.Bold)
            };
            b.FlatAppearance.BorderSize = 0;
            b.FlatAppearance.MouseOverBackColor = navy2;
            b.Click += (_, _) => ShowPage(page, pageName);
            return b;
        }

        // Pages
        var dashboardPage = NewPage();
        var leadsPage = NewPage();
        var messagesPage = NewPage();
        var dialerPage = NewPage();
        var callsPage = NewPage();
        var contactsPage = NewPage();
        var emailsPage = NewPage();
        var filesPage = NewPage();
        var devicesPage = NewPage();
        var settingsPage = NewPage();
        var developerPage = NewPage();

        var notificationDrawer = Card(12);
        notificationDrawer.Dock = DockStyle.Right;
        notificationDrawer.Width = 300;
        notificationDrawer.Visible = false;
        notificationDrawer.BringToFront();
        alertsButton.Click += (_, _) =>
        {
            notificationDrawer.Visible = !notificationDrawer.Visible;
            if (notificationDrawer.Visible) notificationDrawer.BringToFront();
        };

        // Dashboard
        var dash = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 2, Padding = new Padding(4) };
        for (var i = 0; i < 4; i++) dash.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        dash.RowStyles.Add(new RowStyle(SizeType.Absolute, 120));
        dash.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        dashboardPage.Controls.Add(dash);
        void AddMetricCard(Label label, string caption)
        {
            var card = Card(16);
            card.Dock = DockStyle.Fill;
            label.Text = $"{caption}\n0";
            label.Dock = DockStyle.Fill;
            label.TextAlign = ContentAlignment.MiddleCenter;
            label.ForeColor = navy;
            label.Font = new Font("Segoe UI Variable Display", 15F, FontStyle.Regular);
            card.Controls.Add(label);
            dash.Controls.Add(card);
        }
        AddMetricCard(_messagesMetricLabel, "Messages Loaded");
        AddMetricCard(_contactsMetricLabel, "Contacts");
        AddMetricCard(_callsMetricLabel, "Call Records");
        AddMetricCard(_phonesMetricLabel, "Connected Phones");
        var dashText = Card(18);
        dashText.Dock = DockStyle.Fill;
        dash.SetColumnSpan(dashText, 4);
        dashText.Controls.Add(new Label
        {
            Text = "Dashboard is the clean command view. Use Leads for CRM work, Dialer for calls/texts, Messages for inbox/replies, and Developer Mode only for diagnostics.",
            Dock = DockStyle.Fill,
            ForeColor = muted,
            Font = new Font("Segoe UI Variable Display", 12F, FontStyle.Regular),
            TextAlign = ContentAlignment.MiddleCenter
        });
        dash.Controls.Add(dashText);

        // Leads page with wider lead list and compact workspace.
        body.Controls.Add(notificationDrawer);
        notificationDrawer.BringToFront();
        var leadMain = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Padding = new Padding(0), BackColor = Color.FromArgb(247, 249, 252) };
        leadMain.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 66));
        leadMain.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34));
        leadsPage.Controls.Add(leadMain);

        var leadsCard = Card(12);
        leadsCard.Dock = DockStyle.Fill;
        var leadsLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1 };
        leadsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        leadsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        leadsLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        leadsCard.Controls.Add(leadsLayout);
        var leadsHeader = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 1 };
        leadsHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        leadsHeader.Controls.Add(Title("Leads", 12F), 0, 0);
        leadsLayout.Controls.Add(leadsHeader, 0, 0);
        var leadSearch = new TextBox { PlaceholderText = "Search company, owner, phone, email, status...", Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 6) };
        leadsLayout.Controls.Add(leadSearch, 0, 1);

        var leadGrid = Grid();
        leadGrid.Columns.Add("Company", "Company");
        leadGrid.Columns.Add("Approval", "Approval");
        leadGrid.Columns.Add("Financials", "Financials");
        leadGrid.Columns.Add("Phone", "Primary Phone");
        leadGrid.Columns.Add("Emails", "Emails");
        leadGrid.Columns.Add("Owner", "Owner");
        leadGrid.Columns.Add("Status", "Status");
        leadGrid.Columns.Add("LastContact", "Last Contact");
        leadGrid.Columns.Add("Assigned", "Device / Rep");
        leadGrid.Columns[0].FillWeight = 160;
        leadGrid.Columns[1].FillWeight = 78;
        leadGrid.Columns[2].FillWeight = 190;
        leadGrid.Columns[3].FillWeight = 105;
        leadGrid.Columns[4].FillWeight = 48;
        leadGrid.Columns[5].FillWeight = 94;
        leadGrid.Columns[6].FillWeight = 78;
        leadGrid.Columns[7].FillWeight = 90;
        leadGrid.Columns[8].FillWeight = 95;
        leadsLayout.Controls.Add(leadGrid, 0, 2);
        leadMain.Controls.Add(leadsCard, 0, 0);

        var workspaceCard = Card(10);
        workspaceCard.Dock = DockStyle.Fill;
        var workspace = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1 };
        workspace.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        workspace.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        workspace.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
        workspaceCard.Controls.Add(workspace);
        var leadTitle = new Label { Text = "Select a lead", Dock = DockStyle.Fill, ForeColor = navy, Font = new Font("Segoe UI Variable Display", 13F, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft };
        workspace.Controls.Add(leadTitle, 0, 0);
        var leadTabs = new TabControl { Dock = DockStyle.Fill, Font = new Font("Segoe UI Variable Display", 8.8F, FontStyle.Regular) };
        workspace.Controls.Add(leadTabs, 0, 1);

        void AddLeadTab(string caption, Control child)
        {
            var page = new TabPage(caption) { BackColor = Color.White, Padding = new Padding(4) };
            child.Dock = DockStyle.Fill;
            page.Controls.Add(child);
            leadTabs.TabPages.Add(page);
        }

        var overviewGrid = Grid();
        overviewGrid.Columns.Add("Field", "Field");
        overviewGrid.Columns.Add("Value", "Value");
        AddLeadTab("Overview", overviewGrid);

        var phonesGrid = Grid();
        phonesGrid.Columns.Add("Type", "Type");
        phonesGrid.Columns.Add("Number", "Number");
        phonesGrid.Columns.Add("Action", "Action");
        AddLeadTab("Phones", phonesGrid);

        var leadMessagesPanel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Padding = new Padding(8) };
        leadMessagesPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        leadMessagesPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        leadMessagesPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        leadMessagesPanel.Controls.Add(new Label { Text = "Matched SMS thread appears here after inbox refresh.", Dock = DockStyle.Fill, ForeColor = muted }, 0, 0);
        var leadMessagePreview = new TextBox { Multiline = true, ReadOnly = true, Dock = DockStyle.Fill, Text = "No thread loaded yet.", BorderStyle = BorderStyle.FixedSingle };
        leadMessagesPanel.Controls.Add(leadMessagePreview, 0, 1);
        var openMessages = SoftButton("Open Messages Page");
        leadMessagesPanel.Controls.Add(openMessages, 0, 2);
        AddLeadTab("Messages", leadMessagesPanel);

        var emailsGrid = Grid();
        emailsGrid.Columns.Add("Type", "Type");
        emailsGrid.Columns.Add("Email", "Email");
        emailsGrid.Columns.Add("Action", "Action");
        AddLeadTab("Emails", emailsGrid);

        var filesList = new ListBox { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, Font = new Font("Segoe UI Variable Display", 9F) };
        AddLeadTab("Files", filesList);

        var historyList = new ListBox { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, Font = new Font("Segoe UI Variable Display", 9F) };

        var notesPanel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Padding = new Padding(8) };
        notesPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        notesPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        var notesBox = new TextBox { Multiline = true, Dock = DockStyle.Fill, PlaceholderText = "Lead notes..." };
        notesPanel.Controls.Add(notesBox, 0, 0);
        var noteButtons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        foreach (var n in new[] { "No Answer", "Left VM", "Interested", "Declined" })
        {
            var b = SoftButton(n);
            b.Width = 88;
            b.Click += (_, _) =>
            {
                var entry = $"{DateTime.Now:g} · {n}";
                if (notesBox.TextLength > 0) notesBox.AppendText(Environment.NewLine);
                notesBox.AppendText(entry);
                historyList.Items.Insert(0, entry);
            };
            noteButtons.Controls.Add(b);
        }
        notesPanel.Controls.Add(noteButtons, 0, 1);
        AddLeadTab("Notes", notesPanel);
        AddLeadTab("History", historyList);

        var quick = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 2, Padding = new Padding(0, 8, 0, 0) };
        for (var i = 0; i < 4; i++) quick.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        quick.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        quick.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        var qSms = Primary("SMS");
        var qCall = SoftButton("Call");
        var qEmail = SoftButton("Email");
        var qFiles = SoftButton("Files");
        var qCopyPhone = SoftButton("Copy Phone");
        var qSave = SoftButton("Sync Contacts");
        var qDialer = SoftButton("Open Dialer");
        var qMessages = SoftButton("Messages");
        quick.Controls.Add(qSms, 0, 0);
        quick.Controls.Add(qCall, 1, 0);
        quick.Controls.Add(qEmail, 2, 0);
        quick.Controls.Add(qFiles, 3, 0);
        quick.Controls.Add(qCopyPhone, 0, 1);
        quick.Controls.Add(qSave, 1, 1);
        quick.Controls.Add(qDialer, 2, 1);
        quick.Controls.Add(qMessages, 3, 1);
        workspace.Controls.Add(quick, 0, 2);
        leadMain.Controls.Add(workspaceCard, 1, 0);

        var notificationLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1 };
        notificationLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        notificationLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        notificationLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        notificationDrawer.Controls.Add(notificationLayout);
        var notifTop = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        notifTop.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        notifTop.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 34));
        notifTop.Controls.Add(Title("Notifications", 11F), 0, 0);
        var closeNotif = SoftButton("X");
        closeNotif.Dock = DockStyle.Fill;
        closeNotif.Click += (_, _) => notificationDrawer.Visible = false;
        notifTop.Controls.Add(closeNotif, 1, 0);
        notificationLayout.Controls.Add(notifTop, 0, 0);
        _notificationList.Dock = DockStyle.Fill;
        _notificationList.BorderStyle = BorderStyle.None;
        _notificationList.BackColor = Color.White;
        _notificationList.ForeColor = text;
        _notificationList.Font = new Font("Segoe UI Variable Display", 9F, FontStyle.Regular);
        _notificationList.Items.Clear();
        _notificationList.Items.Add("No user-facing events yet.");
        notificationLayout.Controls.Add(_notificationList, 0, 1);
        var openAllNotifications = SoftButton("Open Messages");
        openAllNotifications.Click += (_, _) => ShowPage(messagesPage, "Messages");
        notificationLayout.Controls.Add(openAllNotifications, 0, 2);

        // Messages page.
        var msgLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Padding = new Padding(2) };
        msgLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        msgLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        msgLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        messagesPage.Controls.Add(msgLayout);
        var msgTop = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5, RowCount = 1 };
        msgTop.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        msgTop.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        msgTop.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 125));
        msgTop.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 125));
        msgTop.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 125));
        msgTop.Controls.Add(Title("Messages", 12F), 0, 0);
        _messageSearchBox.PlaceholderText = "Search message body, sender, handle...";
        _messageSearchBox.Dock = DockStyle.Fill;
        msgTop.Controls.Add(_messageSearchBox, 1, 0);
        _loadCachedInboxButton.Text = "Load Cache";
        msgTop.Controls.Add(_loadCachedInboxButton, 2, 0);
        _exportShownInboxButton.Text = "Export";
        msgTop.Controls.Add(_exportShownInboxButton, 3, 0);
        _openCacheFolderButton.Text = "Open Cache";
        msgTop.Controls.Add(_openCacheFolderButton, 4, 0);
        msgLayout.Controls.Add(msgTop, 0, 0);
        _messageGrid.Visible = true;
        _messageGrid.Dock = DockStyle.Fill;
        msgLayout.Controls.Add(_messageGrid, 0, 1);
        var msgHint = new Label { Text = "Use Read Inbox from the top bar, then double-click a message row to view the full decoded SMS.", Dock = DockStyle.Fill, ForeColor = muted, TextAlign = ContentAlignment.MiddleLeft };
        msgLayout.Controls.Add(msgHint, 0, 2);

        // Dialer, calls, and contacts use live device data only.
        BuildPremiumDialerPage(dialerPage);
        BuildCallHistoryPage(callsPage);
        BuildContactsPage(contactsPage);

        // Emails page.
        var emailCard = Card(14);
        emailCard.Dock = DockStyle.Fill;
        emailCard.Controls.Add(new Label { Text = "Emails\n\nNo email mailbox is connected. Lead email addresses remain available from synchronized lead/contact data.", Dock = DockStyle.Fill, ForeColor = muted, Font = new Font("Segoe UI Variable Display", 12F) });
        emailsPage.Controls.Add(emailCard);

        // Files page.
        var filesCard = Card(14);
        filesCard.Dock = DockStyle.Fill;
        filesCard.Controls.Add(new Label { Text = "Files\n\nNo files are linked to the selected lead.", Dock = DockStyle.Fill, ForeColor = muted, Font = new Font("Segoe UI Variable Display", 12F) });
        filesPage.Controls.Add(filesCard);

        // Devices page.
        var devicesLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Padding = new Padding(6) };
        devicesLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        devicesLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        devicesLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        devicesPage.Controls.Add(devicesLayout);
        devicesLayout.Controls.Add(Title("Connected Phones", 12F), 0, 0);
        devicesLayout.Controls.Add(new Label { Text = "Select a connected mobile phone from the top device list. Message history uses MAP; contacts and call history use PBAP; calls use the locked HFP dial path.", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = muted }, 0, 1);
        devicesLayout.Controls.Add(new Label { Text = "Messages and call history are read automatically when the connected phone exposes MAP/PBAP. Contact import is optional; turn on Sync Contacts only when you want address-book names and contact records imported.", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = muted, Padding = new Padding(8) }, 0, 2);

        // Settings page.
        var settingsCard = Card(16);
        settingsCard.Dock = DockStyle.Fill;
        settingsCard.Controls.Add(new Label
        {
            Text = "Settings\n\nCRM Bridge: http://127.0.0.1:8765 (token protected)\nBridge token: %LOCALAPPDATA%\\iPhoneLinkCRM\\bridge-token.txt\nStorage: %LOCALAPPDATA%\\PhoneLinkDiag\\message-cache\nDiagnostics: Developer Mode page\n\nNo WhatsApp and no consent/opt-out module in this build.",
            Dock = DockStyle.Fill,
            ForeColor = muted,
            Font = new Font("Segoe UI Variable Display", 12F)
        });
        settingsPage.Controls.Add(settingsCard);

        // Developer page holds the old diagnostics without exposing them in the main CRM workflow.
        var devLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1, Padding = new Padding(4) };
        devLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
        devLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 52));
        devLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 48));
        devLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        developerPage.Controls.Add(devLayout);
        var devButtons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = true };
        _startScanButton.Text = "Start Scan";
        _stopScanButton.Text = "Stop";
        _autoIphoneMapTestButton.Text = "AUTO";
        _fullAutoMapTestButton.Text = "FULL AUTO";
        _fastAllButton.Text = "FAST ALL";
        _capabilityButton.Text = "Capability Check";
        _listServicesButton.Text = "List Services";
        _dumpPropertiesButton.Text = "Dump Device";
        _obexButton.Text = "Probe OBEX";
        _connectMapButton.Text = "Connect MAP";
        _disconnectMapButton.Text = "Disconnect";
        _obexConnectButton.Text = "OBEX Connect";
        _runConnectProfilesButton.Text = "Try MAP Profiles";
        _compareConnectProfilesButton.Text = "Compare";
        _listMapFoldersButton.Text = "List MAP";
        _listMessagesButton.Text = "List Messages";
        _readSelectedMessageButton.Text = "Read Message";
        _exportPacketLogButton.Text = "Export Packets";
        _exportSessionReportButton.Text = "Export Session";
        _saveLogButton.Text = "Save Log";
        foreach (var b in new Button[] { _startScanButton, _stopScanButton, _autoIphoneMapTestButton, _fullAutoMapTestButton, _fastAllButton, _capabilityButton, _listServicesButton, _dumpPropertiesButton, _obexButton, _connectMapButton, _disconnectMapButton, _obexConnectButton, _runConnectProfilesButton, _compareConnectProfilesButton, _listMapFoldersButton, _listMessagesButton, _readSelectedMessageButton, _exportPacketLogButton, _exportSessionReportButton, _saveLogButton })
        {
            b.Width = 128;
            b.Height = 32;
            devButtons.Controls.Add(b);
        }
        devLayout.Controls.Add(devButtons, 0, 0);
        _profileGrid.Dock = DockStyle.Fill;
        _profileGrid.ReadOnly = true;
        _profileGrid.AllowUserToAddRows = false;
        _profileGrid.AllowUserToDeleteRows = false;
        _profileGrid.RowHeadersVisible = false;
        _profileGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _profileGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _profileGrid.BackgroundColor = Color.White;
        _profileGrid.BorderStyle = BorderStyle.None;
        devLayout.Controls.Add(_profileGrid, 0, 1);
        _logBox.Multiline = true;
        _logBox.ScrollBars = ScrollBars.Both;
        _logBox.WordWrap = false;
        _logBox.Visible = true;
        _logBox.Dock = DockStyle.Fill;
        devLayout.Controls.Add(_logBox, 0, 2);
        var devHint = new Label { Text = "Developer diagnostics are preserved here. Normal CRM/dialer work should use Leads, Messages, and Dialer pages.", Dock = DockStyle.Fill, ForeColor = muted, TextAlign = ContentAlignment.MiddleCenter };
        devLayout.Controls.Add(devHint, 0, 3);

        _pushButton.Enabled = true;
        ConfigurePacketGrid();

        void SelectLeadFromRow(int rowIndex)
        {
            if (rowIndex < 0 || rowIndex >= leadGrid.Rows.Count) return;
            var row = leadGrid.Rows[rowIndex];
            var company = Convert.ToString(row.Cells["Company"].Value) ?? string.Empty;
            var approval = Convert.ToString(row.Cells["Approval"].Value) ?? string.Empty;
            var phone = Convert.ToString(row.Cells["Phone"].Value) ?? string.Empty;
            var status = Convert.ToString(row.Cells["Status"].Value) ?? string.Empty;
            var owner = Convert.ToString(row.Cells["Owner"].Value) ?? string.Empty;
            var financials = Convert.ToString(row.Cells["Financials"].Value) ?? string.Empty;
            var emails = Convert.ToString(row.Cells["Emails"].Value) ?? string.Empty;
            var lastContact = Convert.ToString(row.Cells["LastContact"].Value) ?? string.Empty;
            var assigned = Convert.ToString(row.Cells["Assigned"].Value) ?? string.Empty;
            _phoneBox.Text = phone;
            _dialNumberBox.Text = NormalizePhoneForAction(phone);
            _messageBox.Text = $"Hi {owner.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "there"}, following up on {company}'s {approval} approval. What time works for a quick call?";
            leadTitle.Text = company + "  |  " + status;
            _selectedLeadInfoLabel.Text = $"{company}\nOwner: {owner}\nApproval: {approval}\nPrimary: {FormatPhoneDisplay(phone)}";
            overviewGrid.Rows.Clear();
            overviewGrid.Rows.Add("Company", company);
            overviewGrid.Rows.Add("Owner", owner);
            overviewGrid.Rows.Add("Approval", approval);
            overviewGrid.Rows.Add("Financials", financials);
            overviewGrid.Rows.Add("Primary Phone", FormatPhoneDisplay(phone));
            overviewGrid.Rows.Add("Emails", emails);
            overviewGrid.Rows.Add("Status", status);
            overviewGrid.Rows.Add("Last Contact", lastContact);
            overviewGrid.Rows.Add("Assigned Device / Rep", assigned);
            phonesGrid.Rows.Clear();
            if (!string.IsNullOrWhiteSpace(phone)) phonesGrid.Rows.Add("Primary", FormatPhoneDisplay(phone), "Call / SMS / Copy");
            emailsGrid.Rows.Clear();
            foreach (var email in emails.Split(new[] { ';', ',', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (email.Contains('@')) emailsGrid.Rows.Add("Email", email, "Compose / Copy");
            }
            filesList.Items.Clear();
            leadMessagePreview.Text = $"Selected lead thread: {company}\r\nPrimary phone: {FormatPhoneDisplay(phone)}\r\nRefresh inbox to match replies by number.";
            historyList.Items.Clear();
            historyList.Items.Add($"Lead selected: {DateTime.Now:g}");
            historyList.Items.Add($"Status: {status}");
            historyList.Items.Add($"Last contact: {lastContact}");
            _statusLabel.Text = $"Lead selected: {company}. Number and SMS template loaded into the dialer.";
        }

        leadGrid.SelectionChanged += (_, _) =>
        {
            if (leadGrid.CurrentRow != null) SelectLeadFromRow(leadGrid.CurrentRow.Index);
        };
        leadSearch.TextChanged += (_, _) =>
        {
            var q = leadSearch.Text.Trim();
            foreach (DataGridViewRow row in leadGrid.Rows)
            {
                var haystack = string.Join(" ", row.Cells.Cast<DataGridViewCell>().Select(c => Convert.ToString(c.Value)));
                try
                {
                    row.Visible = string.IsNullOrWhiteSpace(q) || haystack.Contains(q, StringComparison.OrdinalIgnoreCase);
                }
                catch
                {
                    // Keep the current row visible if WinForms blocks hiding the active row.
                    row.Visible = true;
                }
            }
        };
        globalSearch.TextChanged += (_, _) => leadSearch.Text = globalSearch.Text;
        qSms.Click += (_, _) => _pushButton.PerformClick();
        qCall.Click += (_, _) => _dialButton.PerformClick();
        qEmail.Click += (_, _) => ShowPage(emailsPage, "Emails");
        qFiles.Click += (_, _) => ShowPage(filesPage, "Files");
        qCopyPhone.Click += (_, _) => { if (!string.IsNullOrWhiteSpace(_phoneBox.Text)) Clipboard.SetText(_phoneBox.Text); };
        qSave.Click += async (_, _) =>
        {
            if (!_syncContactsToggle.Checked)
            {
                _syncContactsToggle.Checked = true;
                return;
            }
            await SyncPbapDataAsync(includeContacts: true, includeCalls: false, showUserErrors: true);
        };
        qDialer.Click += (_, _) => ShowPage(dialerPage, "Dialer");
        qMessages.Click += (_, _) => ShowPage(messagesPage, "Messages");
        openMessages.Click += (_, _) => ShowPage(messagesPage, "Messages");

        rail.Controls.Add(RailButton("DEV", developerPage, "Developer Mode"));
        rail.Controls.Add(RailButton("SET", settingsPage, "Settings"));
        rail.Controls.Add(RailButton("DEVICES", devicesPage, "Devices"));
        rail.Controls.Add(RailButton("FILES", filesPage, "Files"));
        rail.Controls.Add(RailButton("EMAIL", emailsPage, "Emails"));
        rail.Controls.Add(RailButton("CONTACTS", contactsPage, "Contacts"));
        rail.Controls.Add(RailButton("CALLS", callsPage, "Recent Calls"));
        rail.Controls.Add(RailButton("DIAL", dialerPage, "Dialer"));
        rail.Controls.Add(RailButton("MSG", messagesPage, "Messages"));
        rail.Controls.Add(RailButton("LEADS", leadsPage, "Leads"));
        rail.Controls.Add(RailButton("HOME", dashboardPage, "Dashboard"));
        var menu = new Button { Text = "iL", Dock = DockStyle.Top, Height = 54, FlatStyle = FlatStyle.Flat, BackColor = navy, ForeColor = Color.White, Font = new Font("Segoe UI Variable Display", 16F, FontStyle.Bold) };
        menu.FlatAppearance.BorderSize = 0;
        menu.Click += (_, _) => app.ColumnStyles[0].Width = app.ColumnStyles[0].Width < 100 ? 178 : 68;
        rail.Controls.Add(menu);
        rail.Controls.SetChildIndex(menu, 0);

        if (leadGrid.Rows.Count > 0) SelectLeadFromRow(0);
        ShowPage(leadsPage, "Leads");
    }

    private static string NormalizePhoneForAction(string value)
    {
        var digits = new string(value.Where(char.IsDigit).ToArray());
        if (digits.Length == 10) return "+1" + digits;
        if (digits.Length == 11 && digits.StartsWith("1", StringComparison.Ordinal)) return "+" + digits;
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }

    private static string FormatPhoneDisplay(string value)
    {
        var digits = new string(value.Where(char.IsDigit).ToArray());
        if (digits.Length == 10) return $"({digits[..3]}) {digits[3..6]}-{digits[6..]}";
        if (digits.Length == 11 && digits.StartsWith("1", StringComparison.Ordinal)) return $"+1 ({digits[1..4]}) {digits[4..7]}-{digits[7..]}";
        return value;
    }

    private void BindEvents()
    {
        _logger.EntryAdded += entry => SafeUi(() => _logBox.AppendText(entry + Environment.NewLine));
        _bluetooth.DeviceListChanged += () => SafeUi(RefreshDevices);
        _mapSession.PacketLogged += entry => SafeUi(() => AddPacketRow(entry));
        _mapSession.StateChanged += () => SafeUi(UpdateSessionState);

        _startScanButton.Click += async (_, _) => await StartScanAsync();
        _refreshPairedButton.Click += async (_, _) => await RefreshPairedDevicesAsync();
        _stopScanButton.Click += (_, _) => _bluetooth.StopScan();
        _autoIphoneMapTestButton.Click += async (_, _) => await AutoIPhoneMapTestAsync();
        _fullAutoMapTestButton.Click += async (_, _) => await FullAutoMapTestAsync();
        _fastAllButton.Click += async (_, _) => await FastAllTestsAsync();
        _readInboxFastButton.Click += async (_, _) => await ReadInboxFastAsync();
        _loadCachedInboxButton.Click += (_, _) => LoadCachedInbox();
        _exportShownInboxButton.Click += (_, _) => ExportShownInbox();
        _openCacheFolderButton.Click += (_, _) => OpenCacheFolder();
        _messageSearchBox.TextChanged += (_, _) => ShowInboxMessages(_currentInboxMessages);
        _phoneBox.TextChanged += (_, _) => _dialNumberBox.Text = NormalizePhoneForAction(_phoneBox.Text);
        _syncContactsButton.Click += async (_, _) =>
        {
            if (!_syncContactsToggle.Checked)
            {
                _syncContactsToggle.Checked = true;
                return;
            }
            await SyncPbapDataAsync(includeContacts: true, includeCalls: false, showUserErrors: true);
        };
        _syncContactsToggle.CheckedChanged += async (_, _) =>
        {
            if (_syncContactsToggle.Checked)
            {
                await SyncPbapDataAsync(includeContacts: true, includeCalls: false, showUserErrors: true);
            }
        };
        _syncCallLogButton.Click += async (_, _) => await SyncPbapDataAsync(includeContacts: false, includeCalls: true, showUserErrors: true);
        _saveLogButton.Click += (_, _) => SaveLog();
        _capabilityButton.Click += async (_, _) => await RunCapabilityCheckAsync();
        _listServicesButton.Click += async (_, _) => await ListAllRfcommAsync();
        _dumpPropertiesButton.Click += (_, _) => DumpDeviceProperties();
        _obexButton.Click += async (_, _) => await ProbeObexAsync();
        _connectMapButton.Click += async (_, _) => await ConnectMapAsync();
        _disconnectMapButton.Click += async (_, _) => await DisconnectMapAsync();
        _obexConnectButton.Click += async (_, _) => await ObexConnectMapAsync();
        _runConnectProfilesButton.Click += async (_, _) => await RunConnectProfilesAsync();
        _compareConnectProfilesButton.Click += (_, _) => CompareConnectProfiles();
        _listMapFoldersButton.Click += async (_, _) => await ListMapFoldersAsync();
        _listMessagesButton.Click += async (_, _) => await ListMessagesAsync();
        _readSelectedMessageButton.Click += async (_, _) => await ReadSelectedMessageAsync();
        _exportPacketLogButton.Click += (_, _) => ExportPacketLog();
        _exportSessionReportButton.Click += (_, _) => ExportSessionReport();
        _pushButton.Click += async (_, _) => await SendSmsAsync();
        _testHfpButton.Click += async (_, _) => await TestHfpAsync();
        _dialButton.Click += async (_, _) => await DialUsingIPhoneAsync();
        _hangupButton.Click += async (_, _) => await HangUpIPhoneAsync();
        _deviceList.SelectedIndexChanged += async (_, _) => await HandleSelectedDeviceChangedAsync();
        _deviceList.MouseDoubleClick += (_, args) =>
        {
            var index = _deviceList.IndexFromPoint(args.Location);
            if (index >= 0)
            {
                _deviceList.SelectedIndex = index;
            }
            UpdateSelectedDeviceStatus();
        };
        _deviceList.KeyUp += (_, args) =>
        {
            if (args.KeyCode is Keys.Up or Keys.Down or Keys.Home or Keys.End or Keys.PageUp or Keys.PageDown)
            {
                UpdateSelectedDeviceStatus();
            }
        };
        _deviceList.DrawItem += DrawDeviceListItem;
        _profileGrid.CellDoubleClick += (_, args) => OpenPacketInspector(args.RowIndex);
        _messageGrid.CellDoubleClick += (_, args) =>
        {
            if (args.RowIndex < 0 || args.RowIndex >= _messageGrid.Rows.Count) return;
            if (_messageGrid.Rows[args.RowIndex].Tag is InboxMessageView message) ShowMessageDetail(message);
        };
        UpdateSessionState();

        _deviceRefreshTimer.Interval = 10000;
        _deviceRefreshTimer.Tick += async (_, _) => await RefreshConnectedDeviceStateAsync();
        Shown += async (_, _) =>
        {
            await RefreshConnectedDeviceStateAsync();
            if (!IsDisposed) _deviceRefreshTimer.Start();
        };

        FormClosing += (_, _) =>
        {
            _deviceRefreshTimer.Stop();
            _deviceRefreshTimer.Dispose();
            _appCts.Cancel();
            _bridgeServer?.Dispose();
            _bluetooth.Dispose();
            _mapSession.Dispose();
            _hfpDialer.Dispose();
            _appCts.Dispose();
        };
    }


    private async Task RefreshConnectedDeviceStateAsync()
    {
        if (_deviceRefreshInProgress ||
            _appCts.IsCancellationRequested ||
            IsDisposed ||
            UseWaitCursor ||
            _bluetooth.IsScanning)
        {
            return;
        }
        _deviceRefreshInProgress = true;
        try
        {
            await _bluetooth.RefreshPairedDevicesAsync(_appCts.Token);
            RefreshDevices();

            var connectedIds = _bluetooth.CurrentDevices
                .Where(device => IsLikelyMobilePhone(device) && IsConnectedDevice(device))
                .Select(device => device.Id)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            _autoSyncedConnectedDeviceIds.RemoveWhere(id => !connectedIds.Contains(id));
            await AutoSyncConnectedPhonesAsync();
        }
        catch (OperationCanceledException) when (_appCts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.Warn("Bluetooth", $"Automatic device-state refresh failed: {ex.Message}");
        }
        finally
        {
            _deviceRefreshInProgress = false;
        }
    }

    private async Task SendSmsAsync()
    {
        var device = CurrentSelectedDevice() ?? SelectFirstMobileDevice();
        if (device is null)
        {
            MessageBox.Show(this, "Select or refresh a connected mobile phone first.", "No mobile phone selected", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var phone = _phoneBox.Text.Trim();
        var body = _messageBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(phone) || string.IsNullOrWhiteSpace(body))
        {
            MessageBox.Show(this, "Enter a recipient number and SMS message first.", "Missing SMS fields", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        await _mapWorkflowGate.WaitAsync(_appCts.Token);
        SetBusy("Sending SMS through mobile MAP...");
        try
        {
            await EnsureMapSessionForDeviceAsync(device);
            var result = await _mapSession.PushSmsAsync(phone, body, _appCts.Token);
            var ok = result.Success;
            _statusLabel.Text = ok ? "SMS send command accepted. Verify delivery on the phone/recipient." : "SMS send command failed. Review MAP log.";
            MessageBox.Show(this, ok ? "SMS send command was accepted. Check the phone and recipient device." : "SMS send command failed. Review the log.", ok ? "SMS sent/accepted" : "SMS failed", MessageBoxButtons.OK, ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }
        catch (Exception ex)
        {
            _logger.Error("SMS", $"{ex.Message} | {ex}");
            MessageBox.Show(this, "The SMS could not be sent. See Developer Diagnostics for MAP/OBEX details.", "SMS error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetReady();
            _mapWorkflowGate.Release();
        }
    }

    private async Task TestHfpAsync()
    {
        var device = CurrentSelectedDevice() ?? SelectFirstMobileDevice();
        if (device is null)
        {
            MessageBox.Show(this, "Select or refresh a connected mobile phone first.", "No mobile phone selected", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        SetBusy("Testing HFP connection...");
        try
        {
            await _hfpDialer.ConnectAsync(device, _appCts.Token);
            await _hfpDialer.InitializeAsync(_appCts.Token);
            _statusLabel.Text = "HFP test complete. Raw AT responses are available in Developer Diagnostics only.";
        }
        catch (Exception ex)
        {
            _logger.Error("HFP", $"{ex.Message} | {ex}");
            MessageBox.Show(this, ex.Message, "HFP error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetReady();
        }
    }

    private async Task DialUsingIPhoneAsync()
    {
        var device = CurrentSelectedDevice() ?? SelectFirstMobileDevice();
        if (device is null)
        {
            MessageBox.Show(this, "Select or refresh a connected mobile phone first.", "No mobile phone selected", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var number = _dialNumberBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(number))
        {
            MessageBox.Show(this, "Enter a dial number first.", "Missing dial number", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        SetBusy("Dialing through mobile HFP...");
        try
        {
            if (!_hfpDialer.IsConnected || !string.Equals(_hfpDialer.ConnectedDeviceId, device.Id, StringComparison.OrdinalIgnoreCase))
            {
                await _hfpDialer.ConnectAsync(device, _appCts.Token);
                await _hfpDialer.InitializeAsync(_appCts.Token);
            }

            var response = await _hfpDialer.DialAsync(number, _appCts.Token);
            if (IsExplicitHfpCallFailure(response))
            {
                _logger.Warn("HFP", $"Dial command returned an explicit failure response: {ToDiagnosticText(response)}");
                AddUserNotification("Call Failed", $"{FormatPhoneDisplay(number)} · {device.Name}", $"call-fail:{device.Id}:{NormalizePhoneForAction(number)}");
                _statusLabel.Text = $"Call Failed · {FormatPhoneDisplay(number)}";
                MessageBox.Show(this, "The phone reported that the call could not be started. See Developer Diagnostics for the raw HFP response.", "Call failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // A missing/late AT response is not treated as a failure. The validated July 20
            // build proved that the phone can place the call even when probe traffic is noisy.
            RecordCallStarted(number, device);
            _statusLabel.Text = $"Call Started · {FormatPhoneDisplay(number)}";
        }
        catch (Exception ex)
        {
            _logger.Error("HFP", $"{ex.Message} | {ex}");
            AddUserNotification("Call Failed", $"{FormatPhoneDisplay(number)} · {device.Name}", $"call-exception:{device.Id}:{NormalizePhoneForAction(number)}:{ex.GetType().Name}");
            MessageBox.Show(this, "The call could not be started. See Developer Diagnostics for connection details.", "Dial error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetReady();
        }
    }

    private async Task HangUpIPhoneAsync()
    {
        var device = CurrentSelectedDevice();
        var activeDeviceName = ActiveCallDeviceName(device);
        SetBusy("Sending HFP hang-up...");
        try
        {
            var response = await _hfpDialer.HangUpAsync(_appCts.Token);
            if (IsExplicitHfpCallFailure(response))
            {
                _logger.Warn("HFP", $"Hang-up command returned an explicit failure response: {ToDiagnosticText(response)}");
                AddUserNotification("Call End Failed", activeDeviceName, $"call-end-fail:{_hfpDialer.ConnectedDeviceId}:{_activeCallNotificationKey}");
                _statusLabel.Text = $"Call End Failed · {activeDeviceName}";
                MessageBox.Show(this, "The phone did not confirm the hang-up command. See Developer Diagnostics for the raw HFP response.", "Hang-up error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            RecordCallEnded(device);
            _statusLabel.Text = $"Call Ended · {activeDeviceName}";
        }
        catch (Exception ex)
        {
            _logger.Error("HFP", $"{ex.Message} | {ex}");
            AddUserNotification("Call End Failed", activeDeviceName, $"call-end-exception:{_hfpDialer.ConnectedDeviceId}:{ex.GetType().Name}");
            MessageBox.Show(this, "The hang-up command could not be sent. See Developer Diagnostics for connection details.", "Hang-up error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetReady();
        }
    }

    private async Task StartScanAsync()
    {
        _profileGrid.Rows.Clear();
        SetBusy("Starting Bluetooth device scan...");
        try
        {
            await _bluetooth.StartScanAsync(_appCts.Token);
            _statusLabel.Text = "Bluetooth scan is running. Select a mobile phone when it appears.";
        }
        catch (Exception ex)
        {
            _logger.Error("UI", ex.Message);
            _statusLabel.Text = "Bluetooth scan could not start. See Developer Diagnostics.";
        }
        finally
        {
            SetReady();
        }
    }

    private async Task RefreshPairedDevicesAsync()
    {
        _profileGrid.Rows.Clear();
        SetBusy("Refreshing paired Bluetooth devices...");
        try
        {
            await _bluetooth.RefreshPairedDevicesAsync(_appCts.Token);
            RefreshDevices();

            var connectedIds = _bluetooth.CurrentDevices
                .Where(device => IsLikelyMobilePhone(device) && IsConnectedDevice(device))
                .Select(device => device.Id)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            _autoSyncedConnectedDeviceIds.RemoveWhere(id => !connectedIds.Contains(id));
            await AutoSyncConnectedPhonesAsync();

            _statusLabel.Text = "Paired-device refresh and phone auto-sync complete.";
            ShowInboxMessages(_currentInboxMessages);
            RefreshPbapViews();
        }
        catch (Exception ex)
        {
            _logger.Error("UI", ex.Message);
        }
        finally
        {
            SetReady();
        }
    }

    private async Task AutoIPhoneMapTestAsync()
    {
        _profileGrid.Rows.Clear();
        SetBusy("Running AUTO iPHONE MAP TEST...");
        var report = new AutoMapTestReport();

        try
        {
            _logger.Info("AUTO", "AUTO iPHONE MAP TEST started.");
            report.FailurePoint = "Refresh paired devices";
            await _bluetooth.RefreshPairedDevicesAsync(_appCts.Token);
            RefreshDevices();

            report.FailurePoint = "Auto-select iPhone";
            var selected = SelectFirstIPhoneDevice();
            if (selected is null)
            {
                report.DeviceSelected = "none";
                report.ListMapResult = "NOT RUN";
                report.ListMessagesResult = "NOT RUN";
                _logger.Error("AUTO", "No paired device whose name contains iPhone was found.");
                return;
            }

            report.DeviceSelected = $"{selected.Name} | DeviceId={selected.Id} | BluetoothAddress={selected.BluetoothAddress ?? "Unknown"}";
            _logger.Info("AUTO", $"Selected device: {report.DeviceSelected}");

            report.FailurePoint = "RFCOMM connect";
            var beforeRfcomm = _mapSession.PacketLog.Count;
            await _mapSession.ConnectMapRfcommAsync(selected, _appCts.Token);
            report.RfcommResult = BuildStageResult(_mapSession.RfcommConnected, EntriesSince(beforeRfcomm));
            if (!_mapSession.RfcommConnected)
            {
                report.ListMapResult = "NOT RUN";
                report.ListMessagesResult = "NOT RUN";
                return;
            }

            report.FailurePoint = "OBEX Connect";
            var beforeObex = _mapSession.PacketLog.Count;
            await _mapSession.ObexConnectAsync(_appCts.Token);
            report.ObexResult = BuildStageResult(_mapSession.ObexConnected, EntriesSince(beforeObex));
            report.ConnectionId = FormatConnectionId();
            if (!_mapSession.ObexConnected)
            {
                report.ListMapResult = "NOT RUN";
                report.ListMessagesResult = "NOT RUN";
                return;
            }

            report.FailurePoint = "List MAP";
            var beforeListMap = _mapSession.PacketLog.Count;
            await _mapSession.ListMapFoldersAsync(_appCts.Token);
            var listMapEntries = EntriesSince(beforeListMap);
            var listMapSucceeded = DidListMapSucceed(listMapEntries);
            report.ListMapResult = BuildStageResult(listMapSucceeded, listMapEntries);
            if (!listMapSucceeded)
            {
                report.ListMessagesResult = "NOT RUN";
                return;
            }

            report.FailurePoint = "List Messages";
            var beforeMessages = _mapSession.PacketLog.Count;
            var handles = await _mapSession.ListMessagesAsync(_appCts.Token);
            var messageEntries = EntriesSince(beforeMessages);
            var listingAnalysis = AnalyzeMessageListing(messageEntries, handles);
            report.GetMessageListingSucceeded = listingAnalysis.GetMessageListingSucceeded;
            report.IPhoneReturnedXml = listingAnalysis.ReturnedXml;
            report.XmlContainedMessageHandles = listingAnalysis.ContainsHandles;
            report.NoMessagesParsedReason = listingAnalysis.NoHandlesReason;
            report.MessageHandles = handles.ToArray();
            var listMessagesSucceeded = listingAnalysis.GetMessageListingSucceeded;
            report.ListMessagesResult = BuildStageResult(listMessagesSucceeded, messageEntries);
            report.FailurePoint = listMessagesSucceeded ? "NONE" : "List Messages";
        }
        catch (Exception ex)
        {
            var hresult = ex.HResult == 0 ? "none" : $"0x{ex.HResult:X8}";
            _logger.Error("AUTO", $"{report.FailurePoint} failed. HRESULT={hresult}; {ex.GetType().Name}: {ex.Message}");
            if (report.RfcommResult == "NOT RUN") report.RfcommResult = "FAILED before result was available";
            if (report.ObexResult == "NOT RUN" && report.RfcommResult.StartsWith("PASS", StringComparison.Ordinal)) report.ObexResult = "FAILED before result was available";
        }
        finally
        {
            report.ConnectionId = FormatConnectionId();
            var path = ExportAutoIPhoneMapReport(report);
            _statusLabel.Text = $"AUTO iPHONE MAP TEST finished. ZIP report: {path}";
            _logger.Info("AUTO", $"AUTO iPHONE MAP TEST ZIP report exported to {path}");
            SetReady();
        }
    }

    private async Task FullAutoMapTestAsync()
    {
        _profileGrid.Rows.Clear();
        var report = new FullMapSuiteReport { StartedAt = DateTimeOffset.Now };
        var progress = new SuiteProgress(90);
        DeviceRecord? selected = null;

        try
        {
            SetSuiteProgress(progress, "Refresh paired devices");
            await _bluetooth.RefreshPairedDevicesAsync(_appCts.Token);
            RefreshDevices();

            SetSuiteProgress(progress, "Auto-select iPhone");
            selected = SelectFirstIPhoneDevice();
            if (selected is null)
            {
                AddSuiteError(report, "Auto-select iPhone", "No paired device whose name contains iPhone was found.", null);
                report.ExactBlocker = "No paired iPhone was found.";
                return;
            }
            report.Device = new DeviceSummary(selected.Name, selected.Id, selected.BluetoothAddress ?? "Unknown", selected.BluetoothConnectionStatus);
            report.BluetoothConnected = string.Equals(selected.BluetoothConnectionStatus, "Connected", StringComparison.OrdinalIgnoreCase);

            SetSuiteProgress(progress, "Connect MAP RFCOMM");
            var beforeRfcomm = _mapSession.PacketLog.Count;
            await SafeSuiteStep(report, "Connect MAP RFCOMM", () => _mapSession.ConnectMapRfcommAsync(selected, _appCts.Token));
            report.MapRfcommConnected = _mapSession.RfcommConnected;
            report.RfcommResult = BuildStageResult(report.MapRfcommConnected, EntriesSince(beforeRfcomm));

            SetSuiteProgress(progress, "OBEX Connect MAP");
            var beforeObex = _mapSession.PacketLog.Count;
            if (_mapSession.RfcommConnected)
            {
                await SafeSuiteStep(report, "OBEX Connect MAP", () => _mapSession.ObexConnectAsync(_appCts.Token));
            }
            report.MapObexConnected = _mapSession.ObexConnected;
            report.MapConnected = _mapSession.ObexConnected;
            report.ObexResult = BuildStageResult(report.MapObexConnected, EntriesSince(beforeObex));
            report.ConnectionId = FormatConnectionId();

            if (_mapSession.ObexConnected)
            {
                SetSuiteProgress(progress, "Get MAP folder listing");
                var beforeFolders = _mapSession.PacketLog.Count;
                await SafeSuiteStep(report, "Get MAP folder listing", () => _mapSession.ListMapFoldersAsync(_appCts.Token));
                var folderEntries = EntriesSince(beforeFolders);
                report.FolderListingXml = CollectXmlBody(folderEntries);
                report.MapFolderListingWorks = folderEntries.Any(entry => entry.Operation.Contains("Folder Listing", StringComparison.OrdinalIgnoreCase) && entry.Success);
                report.FolderListingResult = BuildStageResult(report.MapFolderListingWorks, folderEntries);

                var folders = BuildFolderTestList(report.FolderListingXml);
                foreach (var folder in folders)
                {
                    if (!_mapSession.ObexConnected)
                    {
                        AddSuiteError(report, $"MAP folder {folder}", "MAP OBEX session closed before this folder test.", null);
                        break;
                    }

                    SetSuiteProgress(progress, $"MAP folder {folder}");
                    var folderResult = await RunFolderSuiteAsync(report, folder);
                    report.FolderResults.Add(folderResult);
                    foreach (var handle in folderResult.MessageHandles)
                    {
                        report.MessageHandles.Add(handle);
                    }
                }

                foreach (var handle in report.MessageHandles.Distinct(StringComparer.OrdinalIgnoreCase).Take(3))
                {
                    if (!_mapSession.ObexConnected) break;
                    SetSuiteProgress(progress, $"Read message {handle}");
                    var beforeRead = _mapSession.PacketLog.Count;
                    await SafeSuiteStep(report, $"Read message {handle}", () => _mapSession.ReadMessageAsync(handle, _appCts.Token));
                    var readEntries = EntriesSince(beforeRead);
                    var bMessage = CollectResponseBody(readEntries);
                    var parsed = ParseBMessage(bMessage);
                    report.ReadMessages.Add(new ReadMessageResult(handle, readEntries.Any(entry => entry.Operation.Contains(handle, StringComparison.OrdinalIgnoreCase) && entry.Success), bMessage, parsed, BuildStageResult(readEntries.Any(entry => entry.Success), readEntries)));
                }
            }
            else
            {
                report.ExactBlocker = "MAP OBEX did not connect, so MAP folder/message tests were not run.";
            }

            if (selected is not null)
            {
                SetSuiteProgress(progress, "PBAP diagnostics");
                report.Pbap = await RunPbapDiagnosticsAsync(selected, report);

                SetSuiteProgress(progress, "HFP diagnostics");
                report.Hfp = await RunHfpDiagnosticsAsync(selected, report);
            }
        }
        finally
        {
            report.CompletedAt = DateTimeOffset.Now;
            ReconcileFullReportFromPacketLog(report, _mapSession.PacketLog);
            FinalizeSuiteConclusion(report);
            ValidateReportConsistency(report, _mapSession.PacketLog);
            var zipPath = ExportFullMapSuiteReport(report);
            var fileSize = new FileInfo(zipPath).Length;
            SetReady();
            _statusLabel.Text = $"FULL AUTO MAP TEST complete. ZIP: {zipPath}";
            ShowZipCompletePopup(zipPath, fileSize);
        }
    }

    private async Task<FolderSuiteResult> RunFolderSuiteAsync(FullMapSuiteReport report, string folder)
    {
        var result = new FolderSuiteResult { Folder = folder };
        var beforeSet = _mapSession.PacketLog.Count;
        await SafeSuiteStep(report, $"SetFolder {folder}", () => _mapSession.SetMapFolderPathAsync(folder, _appCts.Token));
        result.SetFolderResult = BuildStageResult(_mapSession.ObexConnected, EntriesSince(beforeSet));
        if (!_mapSession.ObexConnected)
        {
            result.Blocker = "MAP OBEX session closed while setting folder.";
            return result;
        }

        foreach (var variant in BuildMessageListingVariants(folder))
        {
            var beforeListing = _mapSession.PacketLog.Count;
            IReadOnlyList<string> handles = Array.Empty<string>();
            await SafeSuiteStep(report, variant.Operation, async () =>
            {
                handles = await _mapSession.ListMessagesVariantAsync(variant.Operation, variant.AppParams, _appCts.Token);
            });
            var entries = EntriesSince(beforeListing);
            var xml = CollectXmlBody(entries);
            var success = entries.Any(entry => entry.Operation.StartsWith(variant.Operation, StringComparison.Ordinal) && entry.Success);
            var variantResult = new MessageListingVariantResult
            {
                Name = variant.Name,
                Operation = variant.Operation,
                Success = success,
                ReturnedXml = LooksLikeXml(xml),
                Xml = xml,
                Handles = handles.ToArray(),
                Result = BuildStageResult(success, entries),
                NoHandlesReason = BuildNoHandlesReason(success, xml, handles)
            };
            result.MessageListings.Add(variantResult);
            foreach (var handle in handles)
            {
                if (!result.MessageHandles.Contains(handle, StringComparer.OrdinalIgnoreCase))
                {
                    result.MessageHandles.Add(handle);
                }
            }

            if (!_mapSession.ObexConnected)
            {
                result.Blocker = "MAP OBEX session closed during message listing.";
                break;
            }
        }

        return result;
    }

    private void RefreshDevices()
    {
        var selectedId = _preferredSelectedDeviceId ?? CurrentSelectedDevice()?.Id;
        var selectedIndex = -1;

        try
        {
            _suppressDeviceSelectionEvents = true;
            _deviceList.BeginUpdate();
            _deviceList.Items.Clear();
            foreach (var device in _bluetooth.CurrentDevices)
            {
                _deviceList.Items.Add(device);
            }

            selectedIndex = FindDeviceIndexById(selectedId);
            if (selectedIndex < 0)
            {
                selectedIndex = FindAutoSelectIndex();
            }

            _deviceList.SelectedIndex = selectedIndex;
            if (selectedIndex >= 0 && _deviceList.Items[selectedIndex] is DeviceRecord selected)
            {
                _preferredSelectedDeviceId = selected.Id;
            }
        }
        finally
        {
            _deviceList.EndUpdate();
            _suppressDeviceSelectionEvents = false;
        }

        UpdateSelectedDeviceStatus();
        RefreshDashboardMetrics();
    }

    private void RefreshDashboardMetrics()
    {
        _messagesMetricLabel.Text = $"Messages Loaded\n{_currentInboxMessages.Count}";
        _contactsMetricLabel.Text = $"Contacts\n{_currentContacts.Count}";
        _callsMetricLabel.Text = $"Call Records\n{_currentCallHistory.Count}";
        _phonesMetricLabel.Text = $"Connected Phones\n{_bluetooth.CurrentDevices.Count(d => IsLikelyMobilePhone(d) && IsConnectedDevice(d))}";
    }

    private async Task RunCapabilityCheckAsync()
    {
        var selected = SelectedDeviceOrWarn();
        if (selected is null) return;

        SetBusy("Running capability check...");
        try
        {
            var results = await _bluetooth.ProbeProfilesAsync(selected, _appCts.Token);
            _profileGrid.Rows.Clear();
            foreach (var result in results)
            {
                _profileGrid.Rows.Add(DateTimeOffset.Now.ToString("HH:mm:ss.fff"), "Bluetooth", result.ProfileName, result.Found ? "YES" : "NO", "", result.ServiceUuid, "", $"{result.ServiceRole}: {result.Status}");
            }
            _statusLabel.Text = "Capability check complete. Review MAP/PBAP/HFP/A2DP/AVRCP rows.";
        }
        catch (Exception ex)
        {
            _logger.Error("UI", ex.Message);
        }
        finally
        {
            SetReady();
        }
    }

    private async Task ListAllRfcommAsync()
    {
        var selected = SelectedDeviceOrWarn();
        if (selected is null) return;

        SetBusy("Listing RFCOMM services...");
        try
        {
            var services = await _bluetooth.ListAllRfcommServicesAsync(selected, _appCts.Token);
            _profileGrid.Rows.Clear();
            if (services.Count == 0)
            {
                _logger.Warn("RFCOMM", "No RFCOMM services were exposed by the selected device in the current connection.");
            }
            foreach (var service in services)
            {
                _profileGrid.Rows.Add(DateTimeOffset.Now.ToString("HH:mm:ss.fff"), "RFCOMM", service.ProfileName, service.Found ? "YES" : "NO", "", service.ServiceUuid, "", $"{service.ServiceRole}: {service.Status}");
            }
            _statusLabel.Text = "RFCOMM service list written to log and table.";
        }
        catch (Exception ex)
        {
            _logger.Error("RFCOMM", ex.Message);
        }
        finally
        {
            SetReady();
        }
    }

    private async Task ProbeObexAsync()
    {
        var selected = SelectedDeviceOrWarn();
        if (selected is null) return;

        SetBusy("Probing OBEX targets...");
        try
        {
            await ProbeOneObexAsync(selected, BluetoothServiceIds.MapMessageAccessServer, "MAP/MAS", BluetoothServiceIds.ObexMapMasTarget);
            await ProbeOneObexAsync(selected, BluetoothServiceIds.PbapPhoneBookServer, "PBAP/PSE", BluetoothServiceIds.ObexPbapPseTarget);
            _statusLabel.Text = "OBEX probes complete. Review logs.";
        }
        finally
        {
            SetReady();
        }
    }

    private async Task ProbeOneObexAsync(DeviceRecord selected, Guid serviceUuid, string targetName, Guid obexTarget)
    {
        RfcommDeviceService? service = await _bluetooth.GetFirstRfcommServiceAsync(selected, serviceUuid, _appCts.Token);
        if (service is null)
        {
            _logger.Warn("OBEX", $"{targetName}: RFCOMM service not found; skipping OBEX probe.");
            return;
        }
        using (service)
        {
            var result = await _obex.ProbeConnectAsync(service, targetName, obexTarget, _appCts.Token);
            _logger.Info("OBEX", $"{result.TargetName}: {result.Status}");
        }
    }

    private void DumpDeviceProperties()
    {
        var selected = SelectedDeviceOrWarn();
        if (selected is null) return;

        try
        {
            var path = _bluetooth.DumpDeviceProperties(selected);
            MessageBox.Show(this, $"Saved device property dump:\n{path}", "Device dump saved", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Dump failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task ConnectMapAsync()
    {
        var selected = SelectedDeviceOrWarn();
        if (selected is null) return;

        SetBusy("Connecting MAP RFCOMM...");
        try
        {
            await _mapSession.ConnectMapRfcommAsync(selected, _appCts.Token);
            _statusLabel.Text = "MAP RFCOMM connect attempted. Review packet log.";
            UpdateSessionState();
        }
        finally
        {
            SetReady();
        }
    }

    private async Task DisconnectMapAsync()
    {
        SetBusy("Disconnecting MAP session...");
        try
        {
            await _mapSession.DisconnectAsync();
            _statusLabel.Text = "MAP session disconnected.";
            UpdateSessionState();
        }
        finally
        {
            SetReady();
        }
    }

    private async Task ObexConnectMapAsync()
    {
        SetBusy("Running OBEX Connect...");
        try
        {
            await _mapSession.ObexConnectAsync(_appCts.Token);
            _statusLabel.Text = "OBEX Connect attempted. Review packet log.";
            UpdateSessionState();
        }
        catch (Exception ex)
        {
            _logger.Error("MAP", ex.Message);
        }
        finally
        {
            SetReady();
        }
    }

    private async Task RunConnectProfilesAsync()
    {
        var selected = SelectedDeviceOrWarn();
        if (selected is null) return;

        SetBusy("Running CONNECT profiles...");
        try
        {
            await _mapSession.RunConnectProfilesAsync(selected, _appCts.Token);
            _statusLabel.Text = "CONNECT profile run complete. Export packet log and session report.";
            UpdateSessionState();
        }
        catch (Exception ex)
        {
            _logger.Error("MAP", ex.Message);
        }
        finally
        {
            SetReady();
        }
    }

    private void CompareConnectProfiles()
    {
        using var comparison = new ConnectProfileComparisonForm(_mapSession.ConnectProfiles);
        comparison.ShowDialog(this);
    }

    private async Task ListMapFoldersAsync()
    {
        SetBusy("Listing MAP folders...");
        try
        {
            await _mapSession.ListMapFoldersAsync(_appCts.Token);
            _statusLabel.Text = "MAP folder exploration complete. Review packet log.";
        }
        catch (Exception ex)
        {
            _logger.Error("MAP", ex.Message);
        }
        finally
        {
            SetReady();
        }
    }

    private async Task ListMessagesAsync()
    {
        SetBusy("Listing MAP messages...");
        try
        {
            await _mapSession.ListMessagesAsync(_appCts.Token);
            _statusLabel.Text = "MAP message listing complete. Select a Message Handle row to read.";
        }
        catch (Exception ex)
        {
            _logger.Error("MAP", ex.Message);
        }
        finally
        {
            SetReady();
        }
    }



    private async Task FastAllTestsAsync()
    {
        // Fast path: one MAP session, profile check, paged inbox listing, message reads, cache, and export.
        // It is intentionally read-only and avoids the slower exhaustive FULL AUTO parameter matrix.
        await ReadInboxFastAsync(includeProfileCheck: true, usePagedListing: true);
    }

    private async Task<bool> ReadInboxFastAsync(
        bool includeProfileCheck = true,
        bool usePagedListing = true,
        string? requestedDeviceId = null,
        bool showCompletionPopup = true,
        bool showFailureDialog = true,
        bool exportReport = true,
        int? messageLimit = null)
    {
        await _mapWorkflowGate.WaitAsync(_appCts.Token);
        _profileGrid.Rows.Clear();
        SetBusy("Reading message history in read-only mode...");
        var started = DateTimeOffset.Now;
        var messages = new List<InboxMessageView>();
        string exportPath = string.Empty;
        string selectedDeviceSummary = "none";
        IReadOnlyList<ProfileProbe> advertisedServices = Array.Empty<ProfileProbe>();
        DeviceRecord? selected = null;

        try
        {
            _logger.Info("INBOX", "READ INBOX FAST started. Read-only MAP flow; no delete/mark-read operations.");
            var selectedDeviceId = requestedDeviceId ?? _preferredSelectedDeviceId ?? CurrentSelectedDevice()?.Id;
            await _bluetooth.RefreshPairedDevicesAsync(_appCts.Token);
            RefreshDevices();

            var exactSelectedDevice = _bluetooth.CurrentDevices.FirstOrDefault(device =>
                string.Equals(device.Id, selectedDeviceId, StringComparison.OrdinalIgnoreCase) && IsLikelyMobilePhone(device));

            // An explicit device request must never silently fall back to another phone.
            // This prevents a phone-2 sync attempt from reading phone-1 data when Windows
            // temporarily refreshes or drops the requested DeviceInformation record.
            if (!string.IsNullOrWhiteSpace(requestedDeviceId) && exactSelectedDevice is null)
            {
                throw new InvalidOperationException($"The requested phone is not currently available for MAP sync: {requestedDeviceId}");
            }

            selected = exactSelectedDevice
                ?? CurrentSelectedDevice()
                ?? SelectFirstMobileDevice();
            if (selected is null)
            {
                throw new InvalidOperationException("No paired mobile phone was found.");
            }

            if (string.IsNullOrWhiteSpace(requestedDeviceId))
            {
                _preferredSelectedDeviceId = selected.Id;
            }
            selectedDeviceSummary = $"{selected.Name} | DeviceId={selected.Id} | BluetoothAddress={selected.BluetoothAddress ?? "Unknown"}";
            _logger.Info("INBOX", $"Selected device: {selectedDeviceSummary}");

            if (includeProfileCheck)
            {
                advertisedServices = await _bluetooth.ListAllRfcommServicesAsync(selected, _appCts.Token);
                var mapFound = advertisedServices.Any(service => service.ProfileName.Equals("MAP", StringComparison.OrdinalIgnoreCase) && service.Found);
                var pbapFound = advertisedServices.Any(service => service.ProfileName.Equals("PBAP", StringComparison.OrdinalIgnoreCase) && service.Found);
                var hfpFound = advertisedServices.Any(service => service.ProfileName.Equals("HFP", StringComparison.OrdinalIgnoreCase) && service.Found);
                _logger.Info("FAST ALL", $"Advertised services checked. Count={advertisedServices.Count}. MAP={mapFound}; PBAP={pbapFound}; HFP={hfpFound}");
            }

            await EnsureMapSessionForDeviceAsync(selected);

            var readLimit = Math.Clamp(messageLimit ?? SelectedInboxReadLimit(), 1, 500);
            var inboxHandles = usePagedListing
                ? await ListMessageHandlesPagedAsync("telecom/msg/inbox", readLimit, _appCts.Token)
                : await ListMessageHandlesSingleAsync("telecom/msg/inbox", readLimit, _appCts.Token);

            IReadOnlyList<string> sentHandles;
            try
            {
                sentHandles = usePagedListing
                    ? await ListMessageHandlesPagedAsync("telecom/msg/sent", readLimit, _appCts.Token)
                    : await ListMessageHandlesSingleAsync("telecom/msg/sent", readLimit, _appCts.Token);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.Warn("INBOX", $"Sent-folder listing was unavailable; continuing with inbox history. {ex.Message}");
                sentHandles = Array.Empty<string>();
            }

            var handleFolders = MergeMessageHandles(inboxHandles, sentHandles, readLimit);
            var handles = handleFolders.Select(item => item.Handle).ToArray();

            foreach (var item in handleFolders)
            {
                _appCts.Token.ThrowIfCancellationRequested();
                PacketLogEntry? readResult = null;
                IReadOnlyList<PacketLogEntry> readEntries = Array.Empty<PacketLogEntry>();
                var bMessage = string.Empty;

                for (var attempt = 1; attempt <= 2; attempt++)
                {
                    var beforeAttempt = _mapSession.PacketLog.Count;
                    try
                    {
                        if (!_mapSession.ObexConnected)
                        {
                            await EnsureMapSessionForDeviceAsync(selected);
                            await _mapSession.SetMapFolderPathAsync(item.Folder.Equals("Sent", StringComparison.OrdinalIgnoreCase) ? "telecom/msg/sent" : "telecom/msg/inbox", _appCts.Token);
                        }

                        readResult = await _mapSession.ReadMessageAsync(item.Handle, _appCts.Token);
                        readEntries = EntriesSince(beforeAttempt);
                        bMessage = CollectResponseBody(readEntries);
                        var attemptFailed = readEntries.Any(entry =>
                            entry.Operation.Contains(item.Handle, StringComparison.OrdinalIgnoreCase) &&
                            !entry.Success);
                        if (readResult.Success && !attemptFailed && !string.IsNullOrWhiteSpace(bMessage)) break;
                        _logger.Warn("INBOX", $"Message {item.Handle} read attempt {attempt} failed or was incomplete; reconnecting MAP once before continuing.");
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        readEntries = EntriesSince(beforeAttempt);
                        bMessage = CollectResponseBody(readEntries);
                        _logger.Warn("INBOX", $"Message {item.Handle} read attempt {attempt} raised {ex.GetType().Name}: {ex.Message}");
                    }

                    if (attempt == 1)
                    {
                        try
                        {
                            await ReconnectMapSessionForDeviceAsync(selected);
                            await _mapSession.SetMapFolderPathAsync(item.Folder.Equals("Sent", StringComparison.OrdinalIgnoreCase) ? "telecom/msg/sent" : "telecom/msg/inbox", _appCts.Token);
                        }
                        catch (Exception reconnectEx) when (reconnectEx is not OperationCanceledException)
                        {
                            _logger.Warn("INBOX", $"MAP reconnect for message {item.Handle} failed: {reconnectEx.Message}");
                        }
                    }
                }

                var readFailed = readEntries.Any(entry =>
                    entry.Operation.Contains(item.Handle, StringComparison.OrdinalIgnoreCase) &&
                    !entry.Success);
                if (readResult is null || !readResult.Success || readFailed || string.IsNullOrWhiteSpace(bMessage))
                {
                    _logger.Warn("INBOX", $"Skipping unreadable message handle {item.Handle} after retry. Raw failure details remain in Developer Diagnostics.");
                    continue;
                }

                var parsed = ParseBMessage(bMessage);
                var originator = parsed.GetValueOrDefault("originator_tel", parsed.GetValueOrDefault("tel", string.Empty));
                var recipient = parsed.GetValueOrDefault("recipient_tel", string.Empty);
                var fromValue = parsed.GetValueOrDefault("from", originator);
                var toValue = parsed.GetValueOrDefault("to", recipient);

                var message = new InboxMessageView
                {
                    Handle = item.Handle,
                    Folder = item.Folder,
                    DeviceId = selected.Id,
                    DeviceName = selected.Name,
                    Type = parsed.GetValueOrDefault("type", string.Empty),
                    Status = parsed.GetValueOrDefault("status", string.Empty),
                    From = fromValue,
                    To = toValue,
                    Date = parsed.GetValueOrDefault("date", string.Empty),
                    Subject = parsed.GetValueOrDefault("subject", string.Empty),
                    Body = parsed.GetValueOrDefault("body", string.Empty),
                    RawBMessage = bMessage,
                    Parsed = parsed,
                    Result = BuildStageResult(readEntries.Any(entry => entry.Success), readEntries)
                };
                messages.Add(message);
            }

            var mergedMessages = LoadInboxFromSqlite()
                .Concat(_currentInboxMessages)
                .Concat(messages)
                .GroupBy(message => $"{message.DeviceId}|{message.Folder}|{message.Handle}", StringComparer.OrdinalIgnoreCase)
                .Select(group => group.Last())
                .OrderByDescending(message => ParseMessageTimestamp(message.Date) ?? DateTimeOffset.MinValue)
                .ThenByDescending(message => message.Handle, StringComparer.OrdinalIgnoreCase)
                .ToList();
            ShowInboxMessages(mergedMessages);
            if (exportReport)
            {
                exportPath = ExportReadOnlyInbox(started, selectedDeviceSummary, handles, messages, null, advertisedServices);
            }
            _statusLabel.Text = $"Message history sync complete. New/updated messages: {messages.Count}. Showing {_currentInboxMessages.Count}.";
            if (showCompletionPopup && !string.IsNullOrWhiteSpace(exportPath))
            {
                ShowZipCompletePopup(exportPath, new FileInfo(exportPath).Length);
            }
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error("INBOX", $"{ex.Message} | {ex}");
            if (messages.Count > 0 && exportReport)
            {
                exportPath = ExportReadOnlyInbox(started, selectedDeviceSummary, messages.Select(message => message.Handle).ToArray(), messages, ex, advertisedServices);
                if (showCompletionPopup)
                {
                    ShowZipCompletePopup(exportPath, new FileInfo(exportPath).Length);
                }
            }
            AddUserNotification("Message Sync Failed", selected?.Name ?? "Phone", $"message-sync-fail:{selected?.Id ?? "none"}:{ex.GetType().Name}");
            if (showFailureDialog)
            {
                MessageBox.Show(this, "Message history could not be fully synchronized. Reconnect the selected phone and retry. Raw MAP/OBEX details are available in Developer Diagnostics.", "Message sync failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            return false;
        }
        finally
        {
            SetReady();
            _mapWorkflowGate.Release();
        }
    }

    private async Task<IReadOnlyList<string>> ListMessageHandlesSingleAsync(string folder, int readLimit, CancellationToken ct)
    {
        await _mapSession.SetMapFolderPathAsync(folder, ct);
        return await _mapSession.ListMessagesVariantAsync(
            $"Message Listing Folder={folder} MaxListCount={readLimit}",
            ObexMapPacketBuilder.AppParams(
                (0x01, ObexMapPacketBuilder.UInt16Value((ushort)readLimit)),
                (0x02, ObexMapPacketBuilder.UInt16Value(0)),
                (0x13, new byte[] { 0xFF }),
                (0x10, UInt32Value(0x0000FFFF))),
            ct);
    }

    private async Task<IReadOnlyList<string>> ListMessageHandlesPagedAsync(string folder, int readLimit, CancellationToken ct)
    {
        await _mapSession.SetMapFolderPathAsync(folder, ct);
        var handles = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var requestSize = Math.Min(50, Math.Max(1, readLimit));
        var emptyPages = 0;
        var offset = 0;

        while (offset < readLimit && emptyPages < 2)
        {
            ct.ThrowIfCancellationRequested();
            var count = Math.Min(requestSize, readLimit - handles.Count);
            if (count <= 0) break;

            var page = await _mapSession.ListMessagesVariantAsync(
                $"Message Listing Folder={folder} Offset={offset} Count={count}",
                ObexMapPacketBuilder.AppParams(
                    (0x01, ObexMapPacketBuilder.UInt16Value((ushort)count)),
                    (0x02, ObexMapPacketBuilder.UInt16Value((ushort)offset)),
                    (0x13, new byte[] { 0xFF }),
                    (0x10, UInt32Value(0x0000FFFF))),
                ct);

            var newCount = 0;
            foreach (var handle in page)
            {
                if (seen.Add(handle))
                {
                    handles.Add(handle);
                    newCount++;
                    if (handles.Count >= readLimit) break;
                }
            }

            _logger.Info("MESSAGES", $"Paged listing folder={folder}, offset={offset}, requested={count}, returned={page.Count}, new={newCount}, total={handles.Count}.");
            if (page.Count == 0 || newCount == 0) emptyPages++;
            else emptyPages = 0;

            if (handles.Count >= readLimit) break;
            offset += Math.Max(1, page.Count);
        }

        return handles;
    }

    private static IReadOnlyList<(string Handle, string Folder)> MergeMessageHandles(
        IReadOnlyList<string> inbox,
        IReadOnlyList<string> sent,
        int limit)
    {
        var merged = new List<(string Handle, string Folder)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var max = Math.Max(inbox.Count, sent.Count);
        for (var i = 0; i < max && merged.Count < limit; i++)
        {
            if (i < inbox.Count && seen.Add("Inbox|" + inbox[i])) merged.Add((inbox[i], "Inbox"));
            if (merged.Count >= limit) break;
            if (i < sent.Count && seen.Add("Sent|" + sent[i])) merged.Add((sent[i], "Sent"));
        }
        return merged;
    }

    private static DateTimeOffset? ParseMessageTimestamp(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var formats = new[]
        {
            "yyyyMMdd'T'HHmmss'Z'",
            "yyyyMMdd'T'HHmmss",
            "yyyy-MM-dd'T'HH:mm:ss'Z'",
            "yyyy-MM-dd'T'HH:mm:sszzz",
            "ddd, d MMM yyyy HH:mm:ss zzz",
            "ddd, dd MMM yyyy HH:mm:ss zzz"
        };
        if (DateTimeOffset.TryParseExact(value.Trim(), formats, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeLocal, out var exact))
            return exact;
        return DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;
    }

    private void ConfigurePacketGrid()
    {
        _profileGrid.Columns.Clear();
        _profileGrid.Columns.Add("Timestamp", "Timestamp");
        _profileGrid.Columns.Add("Layer", "Layer");
        _profileGrid.Columns.Add("Operation", "Operation");
        _profileGrid.Columns.Add("Success", "Success");
        _profileGrid.Columns.Add("Elapsed", "Elapsed");
        _profileGrid.Columns.Add("Request", "Request Hex");
        _profileGrid.Columns.Add("Response", "Response Hex");
        _profileGrid.Columns.Add("Decoded", "Decoded / Status");
    }

    private void ConfigureInboxGrid()
    {
        if (_messageGridConfigured) return;
        _messageGridConfigured = true;
        _messageGrid.ReadOnly = true;
        _messageGrid.AllowUserToAddRows = false;
        _messageGrid.AllowUserToDeleteRows = false;
        _messageGrid.MultiSelect = false;
        _messageGrid.RowHeadersVisible = false;
        _messageGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _messageGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _messageGrid.BackgroundColor = Color.White;
        _messageGrid.BorderStyle = BorderStyle.None;
        _messageGrid.GridColor = Color.FromArgb(222, 225, 230);
        _messageGrid.EnableHeadersVisualStyles = false;
        _messageGrid.RowTemplate.Height = 34;
        _messageGrid.ColumnHeadersHeight = 36;
        _messageGrid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Color.FromArgb(242, 243, 245),
            ForeColor = Color.FromArgb(30, 30, 30),
            Font = new Font("Segoe UI Variable Display", 8.6F, FontStyle.Regular),
            SelectionBackColor = Color.FromArgb(242, 243, 245),
            SelectionForeColor = Color.FromArgb(30, 30, 30)
        };
        _messageGrid.DefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Color.White,
            ForeColor = Color.FromArgb(28, 28, 28),
            SelectionBackColor = Color.FromArgb(232, 233, 235),
            SelectionForeColor = Color.FromArgb(20, 20, 20),
            Font = new Font("Segoe UI Variable Display", 8.8F, FontStyle.Regular)
        };
        _messageGrid.Columns.Clear();
        _messageGrid.Columns.Add("Device", "Phone");
        _messageGrid.Columns.Add("Folder", "Folder");
        _messageGrid.Columns.Add("From", "From");
        _messageGrid.Columns.Add("Date", "Date");
        _messageGrid.Columns.Add("Type", "Type");
        _messageGrid.Columns.Add("Status", "Status");
        _messageGrid.Columns.Add("Body", "Body Preview");
        if (_messageGrid.Columns.Count > 0)
        {
            _messageGrid.Columns[0].FillWeight = 70;
            _messageGrid.Columns[1].FillWeight = 50;
            _messageGrid.Columns[2].FillWeight = 95;
            _messageGrid.Columns[3].FillWeight = 90;
            _messageGrid.Columns[4].FillWeight = 55;
            _messageGrid.Columns[5].FillWeight = 55;
            _messageGrid.Columns[6].FillWeight = 260;
        }
    }

    private void ShowInboxMessages(IEnumerable<InboxMessageView> sourceMessages)
    {
        ConfigureInboxGrid();
        _messageGrid.Rows.Clear();
        var filter = _messageSearchBox.Text.Trim();
        var selectedDeviceId = _preferredSelectedDeviceId ?? CurrentSelectedDevice()?.Id;
        var visible = sourceMessages
            .Where(message => string.IsNullOrWhiteSpace(selectedDeviceId) ||
                              message.DeviceId.Equals(selectedDeviceId, StringComparison.OrdinalIgnoreCase))
            .Where(message => string.IsNullOrWhiteSpace(filter)
                || message.From.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || message.To.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || message.Body.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || message.Subject.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || message.Handle.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (!ReferenceEquals(sourceMessages, _currentInboxMessages))
        {
            _currentInboxMessages.Clear();
            _currentInboxMessages.AddRange(sourceMessages);
        }

        foreach (var message in visible)
        {
            AddInboxRow(message);
        }

        _statusLabel.Text = $"Messages loaded. Showing {visible.Count} of {_currentInboxMessages.Count} cached/read messages.";
        RefreshDashboardMetrics();
    }

    private void AddInboxRow(InboxMessageView message)
    {
        var preview = message.Body;
        if (preview.Length > 220) preview = preview[..220] + "...";
        var rowIndex = _messageGrid.Rows.Add(
            message.DeviceName,
            message.Folder,
            string.IsNullOrWhiteSpace(message.From) ? message.Parsed.GetValueOrDefault("tel", string.Empty) : message.From,
            message.Date,
            message.Type,
            message.Status,
            preview);
        _messageGrid.Rows[rowIndex].Tag = message;
    }

    private string ExportReadOnlyInbox(DateTimeOffset started, string selectedDeviceSummary, IReadOnlyList<string> handles, IReadOnlyList<InboxMessageView> messages, Exception? error = null, IReadOnlyList<ProfileProbe>? advertisedServices = null)
    {
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (string.IsNullOrWhiteSpace(desktop)) desktop = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var workingFolder = Path.Combine(Path.GetTempPath(), $"readonly-inbox-{stamp}");
        var messagesFolder = Path.Combine(workingFolder, "messages");
        Directory.CreateDirectory(messagesFolder);
        var jsonOptions = new JsonSerializerOptions { WriteIndented = true };

        var summary = new StringBuilder();
        summary.AppendLine("PhoneLink Clean-Room READ INBOX FAST Report");
        summary.AppendLine($"Started: {started:O}");
        summary.AppendLine($"Completed: {DateTimeOffset.Now:O}");
        summary.AppendLine($"Device: {selectedDeviceSummary}");
        if (advertisedServices is not null && advertisedServices.Count > 0)
        {
            var mapAdvertised = advertisedServices.Any(service => service.ProfileName.Equals("MAP", StringComparison.OrdinalIgnoreCase) && service.Found);
            var pbapAdvertised = advertisedServices.Any(service => service.ProfileName.Equals("PBAP", StringComparison.OrdinalIgnoreCase) && service.Found);
            var hfpAdvertised = advertisedServices.Any(service => service.ProfileName.Equals("HFP", StringComparison.OrdinalIgnoreCase) && service.Found);
            summary.AppendLine($"MAP advertised: {mapAdvertised}");
            summary.AppendLine($"PBAP advertised: {pbapAdvertised}");
            summary.AppendLine($"HFP advertised: {hfpAdvertised}");
        }
        summary.AppendLine($"RFCOMM connected: {_mapSession.RfcommConnected}");
        summary.AppendLine($"OBEX connected: {_mapSession.ObexConnected}");
        summary.AppendLine($"Connection ID: {FormatConnectionId()}");
        summary.AppendLine($"Handles found: {handles.Count}");
        summary.AppendLine($"Messages read: {messages.Count}");
        summary.AppendLine($"Messages with decoded body: {messages.Count(message => !string.IsNullOrWhiteSpace(message.Body))}");
        summary.AppendLine($"SQLite cache: {InboxSqlitePath()}");
        summary.AppendLine($"Error: {(error is null ? "none" : error.GetType().Name + ": " + error.Message)}");
        summary.AppendLine("Safety: read-only; no PushMessage; no delete; no mark-read; no contact writes; no calls.");
        File.WriteAllText(Path.Combine(workingFolder, "summary.txt"), summary.ToString());
        File.WriteAllText(Path.Combine(workingFolder, "inbox-messages.json"), JsonSerializer.Serialize(messages, jsonOptions));
        File.WriteAllLines(Path.Combine(workingFolder, "message-handles.txt"), handles.Count == 0 ? new[] { "No message handles found." } : handles.Distinct(StringComparer.OrdinalIgnoreCase));
        File.WriteAllText(Path.Combine(workingFolder, "packet-log.json"), JsonSerializer.Serialize(_mapSession.PacketLog, jsonOptions));
        File.WriteAllText(Path.Combine(workingFolder, "packet-log.csv"), BuildPacketLogCsv(_mapSession.PacketLog));
        if (advertisedServices is not null && advertisedServices.Count > 0)
        {
            File.WriteAllText(Path.Combine(workingFolder, "advertised-rfcomm-services.json"), JsonSerializer.Serialize(advertisedServices, jsonOptions));
            File.WriteAllLines(Path.Combine(workingFolder, "advertised-rfcomm-services.txt"), advertisedServices.Select(service => $"{service.ProfileName} | Found={service.Found} | UUID={service.ServiceUuid} | {service.Status}"));
        }

        SaveInboxToSqlite(messages);
        File.WriteAllText(Path.Combine(workingFolder, "local-cache-path.txt"), InboxSqlitePath());
        if (File.Exists(InboxSqlitePath()))
        {
            File.Copy(InboxSqlitePath(), Path.Combine(workingFolder, "inbox-cache.sqlite"), true);
        }

        var index = 1;
        foreach (var message in messages)
        {
            var baseName = $"{index:000}-{SafeFileName(message.Handle)}";
            File.WriteAllText(Path.Combine(messagesFolder, baseName + ".bmsg"), message.RawBMessage);
            File.WriteAllText(Path.Combine(messagesFolder, baseName + ".json"), JsonSerializer.Serialize(message, jsonOptions));
            File.WriteAllText(Path.Combine(messagesFolder, baseName + ".txt"), BuildInboxMessageText(message));
            index++;
        }

        var zipPath = Path.Combine(desktop, $"READ-INBOX-FAST-report-{stamp}.zip");
        if (File.Exists(zipPath)) File.Delete(zipPath);
        ZipFile.CreateFromDirectory(workingFolder, zipPath, CompressionLevel.Optimal, false);
        Directory.Delete(workingFolder, true);
        return zipPath;
    }

    private static string BuildInboxMessageText(InboxMessageView message)
    {
        var text = new StringBuilder();
        text.AppendLine($"Device: {message.DeviceName}");
        text.AppendLine($"Device ID: {message.DeviceId}");
        text.AppendLine($"Folder: {message.Folder}");
        text.AppendLine($"Handle: {message.Handle}");
        text.AppendLine($"Type: {message.Type}");
        text.AppendLine($"Status: {message.Status}");
        text.AppendLine($"From: {message.From}");
        text.AppendLine($"To: {message.To}");
        text.AppendLine($"Date: {message.Date}");
        text.AppendLine($"Subject: {message.Subject}");
        text.AppendLine();
        text.AppendLine(message.Body);
        return text.ToString();
    }



    private int SelectedInboxReadLimit()
    {
        if (_readCountBox.SelectedItem is string text && int.TryParse(text, out var value))
        {
            return Math.Clamp(value, 1, 250);
        }
        return 250;
    }

    private static string MessageCacheFolder()
    {
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PhoneLinkDiag", "message-cache");
    }

    private static string InboxSqlitePath()
    {
        return Path.Combine(MessageCacheFolder(), "inbox-cache.sqlite");
    }

    private void OpenCacheFolder()
    {
        var folder = MessageCacheFolder();
        Directory.CreateDirectory(folder);
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = "\"" + folder + "\"",
            UseShellExecute = true
        });
    }

    private void SaveInboxToSqlite(IReadOnlyList<InboxMessageView> messages)
    {
        Directory.CreateDirectory(MessageCacheFolder());
        using var connection = new SqliteConnection($"Data Source={InboxSqlitePath()}");
        connection.Open();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = @"
CREATE TABLE IF NOT EXISTS message_history (
    device_id TEXT NOT NULL,
    device_name TEXT,
    folder TEXT NOT NULL,
    handle TEXT NOT NULL,
    type TEXT,
    status TEXT,
    from_value TEXT,
    to_value TEXT,
    date_value TEXT,
    subject TEXT,
    body TEXT,
    raw_bmessage TEXT,
    parsed_json TEXT,
    updated_at TEXT,
    PRIMARY KEY (device_id, folder, handle)
);";
            command.ExecuteNonQuery();
        }

        using var transaction = connection.BeginTransaction();
        foreach (var message in messages)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
INSERT INTO message_history
(device_id, device_name, folder, handle, type, status, from_value, to_value, date_value, subject, body, raw_bmessage, parsed_json, updated_at)
VALUES ($deviceId, $deviceName, $folder, $handle, $type, $status, $from, $to, $date, $subject, $body, $raw, $parsed, $updated)
ON CONFLICT(device_id, folder, handle) DO UPDATE SET
    device_name=excluded.device_name,
    type=excluded.type,
    status=excluded.status,
    from_value=excluded.from_value,
    to_value=excluded.to_value,
    date_value=excluded.date_value,
    subject=excluded.subject,
    body=excluded.body,
    raw_bmessage=excluded.raw_bmessage,
    parsed_json=excluded.parsed_json,
    updated_at=excluded.updated_at;";
            command.Parameters.AddWithValue("$deviceId", string.IsNullOrWhiteSpace(message.DeviceId) ? "unknown-device" : message.DeviceId);
            command.Parameters.AddWithValue("$deviceName", message.DeviceName);
            command.Parameters.AddWithValue("$folder", string.IsNullOrWhiteSpace(message.Folder) ? "Inbox" : message.Folder);
            command.Parameters.AddWithValue("$handle", message.Handle);
            command.Parameters.AddWithValue("$type", message.Type);
            command.Parameters.AddWithValue("$status", message.Status);
            command.Parameters.AddWithValue("$from", message.From);
            command.Parameters.AddWithValue("$to", message.To);
            command.Parameters.AddWithValue("$date", message.Date);
            command.Parameters.AddWithValue("$subject", message.Subject);
            command.Parameters.AddWithValue("$body", message.Body);
            command.Parameters.AddWithValue("$raw", message.RawBMessage);
            command.Parameters.AddWithValue("$parsed", JsonSerializer.Serialize(message.Parsed));
            command.Parameters.AddWithValue("$updated", DateTimeOffset.Now.ToString("O"));
            command.ExecuteNonQuery();
        }
        transaction.Commit();
        _logger.Info("MESSAGES", $"Saved {messages.Count} messages to per-device SQLite history: {InboxSqlitePath()}");
    }

    private List<InboxMessageView> LoadInboxFromSqlite()
    {
        var messages = new List<InboxMessageView>();
        if (!File.Exists(InboxSqlitePath())) return messages;
        using var connection = new SqliteConnection($"Data Source={InboxSqlitePath()}");
        connection.Open();

        using (var tableCheck = connection.CreateCommand())
        {
            tableCheck.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='message_history'";
            if (tableCheck.ExecuteScalar() is null) return messages;
        }

        using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT device_id,device_name,folder,handle,type,status,from_value,to_value,date_value,subject,body,raw_bmessage,parsed_json
FROM message_history
ORDER BY updated_at DESC
LIMIT 2000";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var parsedJson = reader.IsDBNull(12) ? "{}" : reader.GetString(12);
            Dictionary<string, string> parsed;
            try
            {
                parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(parsedJson)
                    ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
            catch (JsonException ex)
            {
                _logger.Warn("MESSAGES", $"Ignoring malformed parsed_json for cached message row: {ex.Message}");
                parsed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
            messages.Add(new InboxMessageView
            {
                DeviceId = reader.IsDBNull(0) ? string.Empty : reader.GetString(0),
                DeviceName = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                Folder = reader.IsDBNull(2) ? "Inbox" : reader.GetString(2),
                Handle = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                Type = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                Status = reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
                From = reader.IsDBNull(6) ? string.Empty : reader.GetString(6),
                To = reader.IsDBNull(7) ? string.Empty : reader.GetString(7),
                Date = reader.IsDBNull(8) ? string.Empty : reader.GetString(8),
                Subject = reader.IsDBNull(9) ? string.Empty : reader.GetString(9),
                Body = reader.IsDBNull(10) ? string.Empty : reader.GetString(10),
                RawBMessage = reader.IsDBNull(11) ? string.Empty : reader.GetString(11),
                Parsed = parsed,
                Result = "Loaded from per-device SQLite history"
            });
        }
        return messages;
    }

    private void LoadCachedInbox()
    {
        try
        {
            var messages = LoadInboxFromSqlite();
            if (messages.Count == 0)
            {
                MessageBox.Show(this, "No cached message history was found yet. Read messages from a connected phone first.", "No cache", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            _logger.Info("MESSAGES", $"Loaded {messages.Count} cached messages from {InboxSqlitePath()}");
            ShowInboxMessages(messages);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Load cached messages failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ExportShownInbox()
    {
        try
        {
            if (_currentInboxMessages.Count == 0)
            {
                MessageBox.Show(this, "No inbox messages are loaded. Run READ INBOX FAST or LOAD CACHED INBOX first.", "Nothing to export", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var zip = ExportReadOnlyInbox(DateTimeOffset.Now, "current loaded inbox view", _currentInboxMessages.Select(message => message.Handle).ToArray(), _currentInboxMessages);
            ShowZipCompletePopup(zip, new FileInfo(zip).Length);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Export shown inbox failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ShowMessageDetail(InboxMessageView message)
    {
        using var form = new Form
        {
            Text = "Inbox Message",
            Width = 720,
            Height = 520,
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = true
        };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Padding = new Padding(12) };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var header = new Label
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            Text = $"Phone: {message.DeviceName}\nFolder: {message.Folder}\nFrom: {message.From}\nTo: {message.To}\nDate: {message.Date}\nType: {message.Type}   Status: {message.Status}\nHandle: {message.Handle}",
            Padding = new Padding(0, 0, 0, 8)
        };
        var body = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = true,
            Text = string.IsNullOrWhiteSpace(message.Body) ? BuildInboxMessageText(message) : message.Body
        };
        layout.Controls.Add(header, 0, 0);
        layout.Controls.Add(body, 0, 1);
        form.Controls.Add(layout);
        form.ShowDialog(this);
    }

    private async Task ReadSelectedMessageAsync()
    {
        var handle = _profileGrid.SelectedRows.Count > 0 ? (_profileGrid.SelectedRows[0].Tag as PacketRowTag)?.MessageHandle : null;
        if (string.IsNullOrWhiteSpace(handle))
        {
            MessageBox.Show(this, "Select a Message Handle row first.", "No message selected", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        SetBusy("Reading selected MAP message...");
        try
        {
            await _mapSession.ReadMessageAsync(handle, _appCts.Token);
            _statusLabel.Text = "Read selected message attempted. Review packet log.";
        }
        catch (Exception ex)
        {
            _logger.Error("MAP", ex.Message);
        }
        finally
        {
            SetReady();
        }
    }

    private void ExportPacketLog()
    {
        try
        {
            var path = _mapSession.ExportPacketLog();
            MessageBox.Show(this, $"Saved packet log:\n{path}", "Packet log saved", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Export failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ExportSessionReport()
    {
        try
        {
            var path = _mapSession.ExportSessionReport();
            MessageBox.Show(this, $"Saved session report:\n{path}", "Session report saved", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Export failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void AddPacketRow(PacketLogEntry entry)
    {
        var rowIndex = _profileGrid.Rows.Add(
            entry.Timestamp.ToString("HH:mm:ss.fff"),
            entry.Layer,
            entry.Operation,
            entry.Success ? "YES" : "NO",
            entry.ElapsedMilliseconds,
            entry.RequestHex,
            entry.ResponseHex,
            entry.Status);

        if (!string.IsNullOrWhiteSpace(entry.MessageHandle))
        {
            _profileGrid.Rows[rowIndex].Tag = new PacketRowTag(entry, entry.MessageHandle);
        }
        else
        {
            _profileGrid.Rows[rowIndex].Tag = new PacketRowTag(entry, null);
        }
    }

    private void OpenPacketInspector(int rowIndex)
    {
        if (rowIndex < 0 || rowIndex >= _profileGrid.Rows.Count) return;
        if (_profileGrid.Rows[rowIndex].Tag is not PacketRowTag { Entry: { } entry }) return;
        using var inspector = new PacketInspectorForm(entry);
        inspector.ShowDialog(this);
    }

    private DeviceRecord? SelectedDeviceOrWarn()
    {
        var device = CurrentSelectedDevice();
        if (device is not null)
        {
            return device;
        }

        MessageBox.Show(this, "Select a paired Bluetooth device first.", "No device selected", MessageBoxButtons.OK, MessageBoxIcon.Information);
        return null;
    }

    private void SaveLog()
    {
        try
        {
            var path = _logger.SaveToFile();
            MessageBox.Show(this, $"Saved log:\n{path}", "Log saved", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Save failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void SetBusy(string text)
    {
        UseWaitCursor = true;
        if (!text.Contains("inbox", StringComparison.OrdinalIgnoreCase))
        {
            ConfigurePacketGrid();
        }
        _statusLabel.Text = text;
    }

    private void SetReady()
    {
        UseWaitCursor = false;
    }

    private void UpdateSessionState()
    {
        _sessionStateLabel.Text = $"Bluetooth={_mapSession.BluetoothState} | RFCOMM={_mapSession.RfcommState} | OBEX={_mapSession.ObexState} | MAP={_mapSession.MapState}";
        var obexReady = _mapSession.ObexConnected;
        _listMapFoldersButton.Enabled = obexReady;
        _listMessagesButton.Enabled = obexReady;
        _readSelectedMessageButton.Enabled = obexReady;
        UpdateDeviceActionState();
    }

    private async Task AutoSyncConnectedPhonesAsync()
    {
        var connectedPhones = _bluetooth.CurrentDevices
            .Where(device => IsLikelyMobilePhone(device) && IsConnectedDevice(device))
            .OrderBy(device => device.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(device => device.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var device in connectedPhones)
        {
            _appCts.Token.ThrowIfCancellationRequested();
            if (!_autoSyncedConnectedDeviceIds.Add(device.Id))
            {
                continue;
            }

            try
            {
                _logger.Info("AUTO SYNC", $"New connected phone detected: {device.Name} ({device.Id}). Auto-syncing messages, contacts, and call history with UI toggles off.");
                await ReadInboxFastAsync(
                    includeProfileCheck: false,
                    usePagedListing: true,
                    requestedDeviceId: device.Id,
                    showCompletionPopup: false,
                    showFailureDialog: false,
                    exportReport: false,
                    messageLimit: 500);

                await SyncPbapDataAsync(
                    includeContacts: true,
                    includeCalls: true,
                    showUserErrors: false,
                    targetDeviceId: device.Id);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Keep the phone marked as attempted for this connection so the 10-second
                // background refresh does not hammer an unavailable profile. Selecting the
                // phone manually always retries, and disconnect/reconnect clears this marker.
                _logger.Warn("AUTO SYNC", $"{device.Name}: automatic connected-phone sync attempt ended with {ex.Message}");
            }
        }

        ShowInboxMessages(_currentInboxMessages);
        RefreshPbapViews();
    }

    private async Task HandleSelectedDeviceChangedAsync()
    {
        UpdateSelectedDeviceStatus();
        if (_suppressDeviceSelectionEvents) return;

        var device = CurrentSelectedDevice();
        if (device is null) return;

        var targetDeviceId = device.Id;
        _preferredSelectedDeviceId = targetDeviceId;
        ShowInboxMessages(_currentInboxMessages);
        RefreshPbapViews();

        // MAP OBEX/RFCOMM state is device-specific. Never reuse phone 1's live
        // session for phone 2. If another phone owns the current session, release
        // it before the selected phone enters the serialized MAP workflow.
        if (_mapSession.RfcommConnected &&
            !string.Equals(_mapSession.ConnectedDeviceId, targetDeviceId, StringComparison.OrdinalIgnoreCase))
        {
            await _mapWorkflowGate.WaitAsync(_appCts.Token);
            try
            {
                if (_mapSession.RfcommConnected &&
                    !string.Equals(_mapSession.ConnectedDeviceId, targetDeviceId, StringComparison.OrdinalIgnoreCase))
                {
                    await _mapSession.DisconnectAsync();
                }
            }
            finally
            {
                _mapWorkflowGate.Release();
            }
        }

        try
        {
            _statusLabel.Text = $"Auto-syncing messages, contacts, and call history from {device.Name}...";

            var messagesOk = await ReadInboxFastAsync(
                includeProfileCheck: false,
                usePagedListing: true,
                requestedDeviceId: targetDeviceId,
                showCompletionPopup: false,
                showFailureDialog: false,
                exportReport: false,
                messageLimit: 500);

            var callsOk = await SyncPbapDataAsync(
                includeContacts: true,
                includeCalls: true,
                showUserErrors: false,
                targetDeviceId: targetDeviceId);

            // A user may switch phones while the previous device is still finishing.
            // Do not overwrite the selected phone's status with stale device text.
            if (string.Equals(_preferredSelectedDeviceId, targetDeviceId, StringComparison.OrdinalIgnoreCase))
            {
                _statusLabel.Text = messagesOk && callsOk
                    ? $"{device.Name} synced. Messages and call history are up to date."
                    : $"{device.Name} sync completed with partial data. Review Developer Diagnostics for unavailable profiles.";
                ShowInboxMessages(_currentInboxMessages);
                RefreshPbapViews();
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.Warn("AUTO SYNC", $"{device.Name}: {ex.Message}");
        }
    }

    private void UpdateSelectedDeviceStatus()
    {
        var device = CurrentSelectedDevice();
        if (device is null)
        {
            _lastLoggedSelectedDeviceId = null;
            _statusLabel.Text = "Select a paired Bluetooth device first.";
            UpdateDeviceActionState();
            return;
        }

        _statusLabel.Text = device.HasPairingMismatch
            ? "Warning: Windows Bluetooth state and DeviceInformation pairing state disagree."
            : $"Selected {device.Name}: {device.BluetoothConnectionStatus}, paired={device.EffectiveIsPaired}.";

        if (!string.Equals(_lastLoggedSelectedDeviceId, device.Id, StringComparison.Ordinal))
        {
            _lastLoggedSelectedDeviceId = device.Id;
            _logger.Info("Selection", $"Selected device: Name={device.Name}; DeviceId={device.Id}; Bluetooth Address={device.BluetoothAddress ?? "Unknown"}");
        }

        UpdateDeviceActionState();
    }

    private DeviceRecord? CurrentSelectedDevice()
    {
        if (_deviceList.SelectedItem is DeviceRecord selected)
        {
            return selected;
        }

        return _deviceList.SelectedIndex >= 0 && _deviceList.SelectedIndex < _deviceList.Items.Count
            ? _deviceList.Items[_deviceList.SelectedIndex] as DeviceRecord
            : null;
    }

    private int FindDeviceIndexById(string? selectedId)
    {
        if (string.IsNullOrWhiteSpace(selectedId)) return -1;

        for (var i = 0; i < _deviceList.Items.Count; i++)
        {
            if (string.Equals((_deviceList.Items[i] as DeviceRecord)?.Id, selectedId, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private int FindAutoSelectIndex()
    {
        if (_deviceList.Items.Count == 1) return 0;

        var connectedPhones = Enumerable.Range(0, _deviceList.Items.Count)
            .Where(index => _deviceList.Items[index] is DeviceRecord device && IsLikelyMobilePhone(device) && IsConnectedDevice(device))
            .ToArray();
        if (connectedPhones.Length == 1) return connectedPhones[0];

        var pairedPhones = Enumerable.Range(0, _deviceList.Items.Count)
            .Where(index => _deviceList.Items[index] is DeviceRecord device && IsLikelyMobilePhone(device))
            .ToArray();
        return pairedPhones.Length == 1 ? pairedPhones[0] : -1;
    }

    private DeviceRecord? SelectFirstIPhoneDevice()
    {
        for (var i = 0; i < _deviceList.Items.Count; i++)
        {
            if (_deviceList.Items[i] is DeviceRecord device &&
                device.Name.Contains("iPhone", StringComparison.OrdinalIgnoreCase))
            {
                _deviceList.SelectedIndex = i;
                UpdateSelectedDeviceStatus();
                return device;
            }
        }

        return null;
    }



    private DeviceRecord? SelectFirstMobileDevice()
    {
        for (var i = 0; i < _deviceList.Items.Count; i++)
        {
            if (_deviceList.Items[i] is DeviceRecord device && IsLikelyMobilePhone(device) && IsConnectedDevice(device))
            {
                _deviceList.SelectedIndex = i;
                UpdateSelectedDeviceStatus();
                return device;
            }
        }

        for (var i = 0; i < _deviceList.Items.Count; i++)
        {
            if (_deviceList.Items[i] is DeviceRecord device && IsLikelyMobilePhone(device))
            {
                _deviceList.SelectedIndex = i;
                UpdateSelectedDeviceStatus();
                return device;
            }
        }
        return SelectFirstIPhoneDevice();
    }

    private static bool IsLikelyMobilePhone(DeviceRecord device)
    {
        var name = device.Name;
        return name.Contains("iPhone", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Samsung", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Galaxy", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Android", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Pixel", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Motorola", StringComparison.OrdinalIgnoreCase)
            || name.Contains("OnePlus", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Huawei", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Xiaomi", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Redmi", StringComparison.OrdinalIgnoreCase)
            || name.Contains("OPPO", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Vivo", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Nokia", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Sony", StringComparison.OrdinalIgnoreCase)
            || name.Contains("LG", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Honor", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Nothing Phone", StringComparison.OrdinalIgnoreCase)
            || name.Contains("ASUS", StringComparison.OrdinalIgnoreCase)
            || name.Contains("ZTE", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsConnectedDevice(DeviceRecord device)
        => string.Equals(device.BluetoothConnectionStatus, "Connected", StringComparison.OrdinalIgnoreCase);

    private IReadOnlyList<PacketLogEntry> EntriesSince(int startIndex)
    {
        var packetLog = _mapSession.PacketLog;
        return packetLog.Skip(Math.Clamp(startIndex, 0, packetLog.Count)).ToArray();
    }

    private static bool DidListMapSucceed(IReadOnlyList<PacketLogEntry> entries)
    {
        var requiredOperations = new[]
        {
            "MAP Get MAS Instance Information",
            "MAP Set Folder Root",
            "Root Folder Listing",
            "MAP Set Folder telecom",
            "telecom Folder Listing",
            "MAP Set Folder msg",
            "telecom/msg Folder Listing"
        };

        return requiredOperations.All(operation => entries.Any(entry => entry.Operation == operation && entry.Success));
    }

    private static string BuildStageResult(bool succeeded, IReadOnlyList<PacketLogEntry> entries)
    {
        var prefix = succeeded ? "PASS" : "FAIL";
        var failure = entries.FirstOrDefault(entry => !entry.Success);
        if (failure is not null)
        {
            return $"{prefix}: {failure.Operation}: {failure.Status}";
        }

        var last = entries.LastOrDefault();
        return last is null ? $"{prefix}: no packet log entries" : $"{prefix}: {last.Operation}: {last.Status}";
    }

    private static MessageListingAnalysis AnalyzeMessageListing(IReadOnlyList<PacketLogEntry> entries, IReadOnlyList<string> handles)
    {
        var listingEntry = entries.LastOrDefault(entry => entry.Operation == "MAP Get Message Listing");
        if (listingEntry is null)
        {
            return new MessageListingAnalysis(false, false, false, "MAP Get Message Listing packet log entry was not found.");
        }

        if (!listingEntry.Success)
        {
            return new MessageListingAnalysis(false, false, false, $"GetMessageListing failed: {listingEntry.Status}");
        }

        if (string.IsNullOrWhiteSpace(listingEntry.ResponseHex) || listingEntry.ResponseHex == "<empty>")
        {
            return new MessageListingAnalysis(true, false, false, "GetMessageListing returned no OBEX response bytes.");
        }

        var body = CollectXmlBody(entries);
        if (string.IsNullOrWhiteSpace(body))
        {
            return new MessageListingAnalysis(true, false, false, "GetMessageListing response contained no Body or EndOfBody XML payload.");
        }

        var returnedXml = LooksLikeXml(body);
        if (!returnedXml)
        {
            return new MessageListingAnalysis(true, false, false, "GetMessageListing body was not XML.");
        }

        if (handles.Count == 0)
        {
            return new MessageListingAnalysis(true, true, false, "XML parsed, but no message handle attributes were present.");
        }

        return new MessageListingAnalysis(true, true, true, "Message handles parsed.");
    }

    private static string CollectXmlBody(IEnumerable<PacketLogEntry> entries)
    {
        return string.Concat(entries
            .Where(entry => entry.Operation.StartsWith("MAP Get Message Listing", StringComparison.Ordinal))
            .Select(entry => entry.ResponseBodyText)
            .Where(text => !string.IsNullOrWhiteSpace(text)));
    }

    private static string CollectResponseBody(IEnumerable<PacketLogEntry> entries)
    {
        return string.Concat(entries
            .Select(entry => entry.ResponseBodyText)
            .Where(text => !string.IsNullOrWhiteSpace(text)));
    }

    private static Dictionary<string, string> ParseBMessage(string bMessage)
    {
        var parsed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(bMessage))
        {
            parsed["parse_status"] = "EMPTY_BODY";
            return parsed;
        }

        parsed["parse_status"] = "BODY_PRESENT";
        parsed["raw_length"] = bMessage.Length.ToString();
        var lines = bMessage.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var body = new StringBuilder();
        var inBody = false;
        var inEnvelope = false;
        var inVCard = false;
        string currentVCardTel = string.Empty;
        string currentVCardName = string.Empty;
        var vCardIndex = 0;

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd();
            if (line.Equals("BEGIN:BENV", StringComparison.OrdinalIgnoreCase)) inEnvelope = true;
            else if (line.Equals("END:BENV", StringComparison.OrdinalIgnoreCase)) inEnvelope = false;

            if (line.Equals("BEGIN:VCARD", StringComparison.OrdinalIgnoreCase))
            {
                inVCard = true;
                currentVCardTel = string.Empty;
                currentVCardName = string.Empty;
                continue;
            }
            if (line.Equals("END:VCARD", StringComparison.OrdinalIgnoreCase))
            {
                if (inVCard)
                {
                    vCardIndex++;
                    var prefix = inEnvelope ? "recipient" : vCardIndex == 1 ? "originator" : "recipient";
                    if (!string.IsNullOrWhiteSpace(currentVCardTel)) parsed[prefix + "_tel"] = currentVCardTel;
                    if (!string.IsNullOrWhiteSpace(currentVCardName)) parsed[prefix + "_name"] = currentVCardName;
                }
                inVCard = false;
                continue;
            }

            if (!inBody && !inVCard)
            {
                if (line.StartsWith("VERSION:", StringComparison.OrdinalIgnoreCase)) parsed["version"] = line[8..].Trim();
                else if (line.StartsWith("STATUS:", StringComparison.OrdinalIgnoreCase)) parsed["status"] = line[7..].Trim();
                else if (line.StartsWith("TYPE:", StringComparison.OrdinalIgnoreCase)) parsed["type"] = line[5..].Trim();
                else if (line.StartsWith("FOLDER:", StringComparison.OrdinalIgnoreCase)) parsed["folder"] = line[7..].Trim();
                else if (line.StartsWith("Date:", StringComparison.OrdinalIgnoreCase)) parsed["date"] = line[5..].Trim();
                else if (line.StartsWith("From:", StringComparison.OrdinalIgnoreCase)) parsed["from"] = line[5..].Trim();
                else if (line.StartsWith("To:", StringComparison.OrdinalIgnoreCase)) parsed["to"] = line[3..].Trim();
                else if (line.StartsWith("Subject:", StringComparison.OrdinalIgnoreCase)) parsed["subject"] = line[8..].Trim();
            }

            if (inVCard)
            {
                var colon = line.IndexOf(':');
                if (colon > 0)
                {
                    var key = line[..colon];
                    var value = line[(colon + 1)..].Trim();
                    if (key.StartsWith("TEL", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(currentVCardTel)) currentVCardTel = value;
                    if (key.StartsWith("FN", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(currentVCardName)) currentVCardName = value;
                    if (key.StartsWith("TEL", StringComparison.OrdinalIgnoreCase) && !parsed.ContainsKey("tel")) parsed["tel"] = value;
                }
            }

            if (line.Equals("BEGIN:MSG", StringComparison.OrdinalIgnoreCase))
            {
                inBody = true;
                continue;
            }
            if (line.Equals("END:MSG", StringComparison.OrdinalIgnoreCase))
            {
                inBody = false;
                continue;
            }
            if (inBody)
            {
                body.AppendLine(rawLine);
            }
        }

        var bodyText = body.ToString().Trim();
        if (!string.IsNullOrWhiteSpace(bodyText)) parsed["body"] = bodyText;
        if (!parsed.ContainsKey("body")) parsed["body_parse_note"] = "No BEGIN:MSG/END:MSG body section was found.";
        return parsed;
    }

    private static bool LooksLikeXml(string text)
    {
        var trimmed = text.TrimStart('\uFEFF', ' ', '\r', '\n', '\t');
        return trimmed.StartsWith("<", StringComparison.Ordinal);
    }

    private async Task SafeSuiteStep(FullMapSuiteReport report, string testName, Func<Task> action)
    {
        var started = DateTimeOffset.Now;
        var sw = Stopwatch.StartNew();
        try
        {
            await action();
            sw.Stop();
            report.StepResults.Add(new SuiteStepResult(report.StepResults.Count + 1, testName, started, DateTimeOffset.Now, "PASS", "Completed without an unhandled exception.", sw.ElapsedMilliseconds));
        }
        catch (TimeoutException ex)
        {
            sw.Stop();
            report.StepResults.Add(new SuiteStepResult(report.StepResults.Count + 1, testName, started, DateTimeOffset.Now, "TIMEOUT", ex.Message, sw.ElapsedMilliseconds));
            AddSuiteError(report, testName, ex.Message, ex);
        }
        catch (Exception ex)
        {
            sw.Stop();
            report.StepResults.Add(new SuiteStepResult(report.StepResults.Count + 1, testName, started, DateTimeOffset.Now, "FAIL", ex.Message, sw.ElapsedMilliseconds));
            AddSuiteError(report, testName, ex.Message, ex);
        }
    }

    private void SetSuiteProgress(SuiteProgress progress, string testName)
    {
        progress.Current++;
        _statusLabel.Text = $"FULL AUTO MAP TEST {Math.Min(progress.Current, progress.Total)} / {progress.Total}: {testName}";
        _logger.Info("FULL AUTO", $"Test {Math.Min(progress.Current, progress.Total)} / {progress.Total}: {testName}");
    }

    private static void AddSuiteError(FullMapSuiteReport report, string testName, string message, Exception? ex)
    {
        report.Errors.Add(new SuiteError(
            DateTimeOffset.Now,
            testName,
            message,
            ex?.GetType().FullName,
            ex is null || ex.HResult == 0 ? "none" : $"0x{ex.HResult:X8}",
            ex?.ToString()));
    }

    private static IReadOnlyList<string> BuildFolderTestList(string folderListingXml)
    {
        var folders = new List<string>
        {
            "telecom",
            "telecom/msg",
            "telecom/msg/inbox",
            "telecom/msg/sent",
            "telecom/msg/outbox",
            "telecom/msg/deleted",
            "inbox",
            "sent",
            "outbox",
            "deleted"
        };

        foreach (var folder in ExtractAttributeValues(folderListingXml, "name"))
        {
            if (!folders.Contains(folder, StringComparer.OrdinalIgnoreCase))
            {
                folders.Add(folder);
            }
        }

        return folders;
    }

    private static IReadOnlyList<MessageListingVariant> BuildMessageListingVariants(string folder)
    {
        var prefix = $"MAP Get Message Listing [{folder}]";
        return new[]
        {
            new MessageListingVariant("MaxListCount=0", $"{prefix} MaxListCount=0", ObexMapPacketBuilder.AppParams((0x01, ObexMapPacketBuilder.UInt16Value(0)))),
            new MessageListingVariant("MaxListCount=1", $"{prefix} MaxListCount=1", ObexMapPacketBuilder.AppParams((0x01, ObexMapPacketBuilder.UInt16Value(1)))),
            new MessageListingVariant("MaxListCount=10", $"{prefix} MaxListCount=10", ObexMapPacketBuilder.AppParams((0x01, ObexMapPacketBuilder.UInt16Value(10)))),
            new MessageListingVariant("MaxListCount=50", $"{prefix} MaxListCount=50", ObexMapPacketBuilder.AppParams((0x01, ObexMapPacketBuilder.UInt16Value(50)))),
            new MessageListingVariant("ListStartOffset=0", $"{prefix} ListStartOffset=0", ObexMapPacketBuilder.AppParams((0x01, ObexMapPacketBuilder.UInt16Value(10)), (0x02, ObexMapPacketBuilder.UInt16Value(0)))),
            new MessageListingVariant("SubjectLength=255", $"{prefix} SubjectLength=255", ObexMapPacketBuilder.AppParams((0x01, ObexMapPacketBuilder.UInt16Value(10)), (0x13, new byte[] { 0xFF }))),
            new MessageListingVariant("ParameterMask=safe-metadata", $"{prefix} ParameterMask=safe-metadata", ObexMapPacketBuilder.AppParams((0x01, ObexMapPacketBuilder.UInt16Value(10)), (0x10, UInt32Value(0x0000FFFF)))),
            new MessageListingVariant("UnreadOnly", $"{prefix} UnreadOnly", ObexMapPacketBuilder.AppParams((0x01, ObexMapPacketBuilder.UInt16Value(10)), (0x06, new byte[] { 0x01 }))),
            new MessageListingVariant("ReadOnly", $"{prefix} ReadOnly", ObexMapPacketBuilder.AppParams((0x01, ObexMapPacketBuilder.UInt16Value(10)), (0x06, new byte[] { 0x02 }))),
            new MessageListingVariant("SMSOnly", $"{prefix} SMSOnly", ObexMapPacketBuilder.AppParams((0x01, ObexMapPacketBuilder.UInt16Value(10)), (0x03, new byte[] { 0x03 }))),
            new MessageListingVariant("EmailOnly", $"{prefix} EmailOnly", ObexMapPacketBuilder.AppParams((0x01, ObexMapPacketBuilder.UInt16Value(10)), (0x03, new byte[] { 0x04 }))),
            new MessageListingVariant("MMSOnly", $"{prefix} MMSOnly", ObexMapPacketBuilder.AppParams((0x01, ObexMapPacketBuilder.UInt16Value(10)), (0x03, new byte[] { 0x08 })))
        };
    }

    private static byte[] UInt32Value(uint value)
    {
        return new[] { (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value };
    }

    private static string BuildNoHandlesReason(bool success, string xml, IReadOnlyList<string> handles)
    {
        if (!success) return "GetMessageListing did not return a successful OBEX response.";
        if (string.IsNullOrWhiteSpace(xml)) return "GetMessageListing returned no XML body.";
        if (!LooksLikeXml(xml)) return "GetMessageListing returned a non-XML body.";
        if (handles.Count == 0) return "iPhone returned valid MAP XML, but it contained no message handle attributes.";
        return "Message handles were present.";
    }

    private static IReadOnlyList<string> ExtractAttributeValues(string text, string attribute)
    {
        var values = new List<string>();
        var marker = attribute + "=\"";
        var index = 0;
        while ((index = text.IndexOf(marker, index, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            index += marker.Length;
            var end = text.IndexOf('"', index);
            if (end < 0) break;
            var value = text[index..end];
            if (!string.IsNullOrWhiteSpace(value) && !values.Contains(value, StringComparer.OrdinalIgnoreCase))
            {
                values.Add(value);
            }
            index = end + 1;
        }
        return values;
    }

    private async Task<PbapResult> RunPbapDiagnosticsAsync(DeviceRecord selected, FullMapSuiteReport report)
    {
        var result = new PbapResult();
        RfcommDeviceService? service = null;
        StreamSocket? socket = null;
        DataWriter? writer = null;
        DataReader? reader = null;

        try
        {
            service = await _bluetooth.GetFirstRfcommServiceAsync(selected, BluetoothServiceIds.PbapPhoneBookServer, _appCts.Token);
            result.ServiceFound = service is not null;
            if (service is null)
            {
                result.Result = "Unsupported: PBAP/PSE RFCOMM service not found.";
                return result;
            }

            socket = new StreamSocket();
            await WithTimeout(socket.ConnectAsync(service.ConnectionHostName, service.ConnectionServiceName, SocketProtectionLevel.BluetoothEncryptionAllowNullAuthentication).AsTask(_appCts.Token), TimeSpan.FromSeconds(20), _appCts.Token);
            result.RfcommConnected = true;
            writer = new DataWriter(socket.OutputStream);
            reader = new DataReader(socket.InputStream) { InputStreamOptions = InputStreamOptions.Partial };

            var connect = ObexMapPacketBuilder.Connect(BluetoothServiceIds.ObexPbapPseTarget);
            var connectResponse = await SendObexProbeAsync(writer, reader, connect, true, _appCts.Token);
            result.RawResponses.Add(connectResponse.RawHex);
            result.ObexConnected = connectResponse.Decoded.Opcode == 0xA0 && connectResponse.Decoded.ConnectionId is not null;
            if (!result.ObexConnected)
            {
                result.Result = "PBAP OBEX Connect failed: " + connectResponse.Decoded.CodeName;
                return result;
            }

            var appParams = ObexMapPacketBuilder.AppParams((0x04, ObexMapPacketBuilder.UInt16Value(10)));
            var request = ObexMapPacketBuilder.Get(connectResponse.Decoded.ConnectionId!.Value, "x-bt/vcard-listing", "telecom/pb", appParams);
            var listingResponse = await SendObexProbeAsync(writer, reader, request, false, _appCts.Token);
            result.RawResponses.Add(listingResponse.RawHex);
            result.VCardListing = listingResponse.Decoded.BodyText;
            result.DecodedContacts = ExtractAttributeValues(result.VCardListing, "name").Take(10).ToArray();
            result.PhonebookReadWorked = listingResponse.Decoded.Opcode is 0xA0 or 0x90;
            result.Result = result.PhonebookReadWorked ? "PBAP read-only vCard listing request completed." : "PBAP phonebook listing failed: " + listingResponse.Decoded.CodeName;
        }
        catch (Exception ex)
        {
            result.Result = ex.Message;
            AddSuiteError(report, "PBAP diagnostics", ex.Message, ex);
        }
        finally
        {
            reader?.Dispose();
            writer?.DetachStream();
            writer?.Dispose();
            socket?.Dispose();
            service?.Dispose();
        }

        return result;
    }

    private async Task<HfpResult> RunHfpDiagnosticsAsync(DeviceRecord selected, FullMapSuiteReport report)
    {
        var result = new HfpResult();
        RfcommDeviceService? service = null;
        StreamSocket? socket = null;
        DataWriter? writer = null;
        DataReader? reader = null;

        try
        {
            service = await _bluetooth.GetFirstRfcommServiceAsync(selected, BluetoothServiceIds.HandsFreeAudioGateway, _appCts.Token)
                ?? await _bluetooth.GetFirstRfcommServiceAsync(selected, BluetoothServiceIds.HeadsetAudioGateway, _appCts.Token)
                ?? await _bluetooth.GetFirstRfcommServiceAsync(selected, BluetoothServiceIds.HandsFree, _appCts.Token);
            result.ServiceFound = service is not null;
            if (service is null)
            {
                result.Result = "Unsupported: HFP RFCOMM service not found.";
                return result;
            }

            socket = new StreamSocket();
            await WithTimeout(socket.ConnectAsync(service.ConnectionHostName, service.ConnectionServiceName, SocketProtectionLevel.BluetoothEncryptionAllowNullAuthentication).AsTask(_appCts.Token), TimeSpan.FromSeconds(20), _appCts.Token);
            result.RfcommConnected = true;
            writer = new DataWriter(socket.OutputStream);
            reader = new DataReader(socket.InputStream) { InputStreamOptions = InputStreamOptions.Partial };

            foreach (var command in new[] { "AT+BRSF=0\r", "AT+CIND=?\r", "AT+CIND?\r", "AT+CLAC\r" })
            {
                writer.WriteString(command);
                await WithTimeout(writer.StoreAsync().AsTask(_appCts.Token), TimeSpan.FromSeconds(5), _appCts.Token);
                await WithTimeout(writer.FlushAsync().AsTask(_appCts.Token), TimeSpan.FromSeconds(5), _appCts.Token);
                var response = await ReadTextProbeAsync(reader, _appCts.Token);
                result.AtResponses.Add(new AtCommandResult(command.Trim(), response));
            }

            result.SafeStatusWorked = result.AtResponses.Any(response => !string.IsNullOrWhiteSpace(response.Response));
            result.Result = result.SafeStatusWorked ? "HFP safe status queries returned data." : "HFP connected, but no AT response data was returned.";
        }
        catch (Exception ex)
        {
            result.Result = ex.Message;
            AddSuiteError(report, "HFP diagnostics", ex.Message, ex);
        }
        finally
        {
            reader?.Dispose();
            writer?.DetachStream();
            writer?.Dispose();
            socket?.Dispose();
            service?.Dispose();
        }

        return result;
    }

    private static async Task<ProbeObexResponse> SendObexProbeAsync(DataWriter writer, DataReader reader, byte[] request, bool connectResponse, CancellationToken ct)
    {
        writer.WriteBytes(request);
        await WithTimeout(writer.StoreAsync().AsTask(ct), TimeSpan.FromSeconds(10), ct);
        await WithTimeout(writer.FlushAsync().AsTask(ct), TimeSpan.FromSeconds(10), ct);
        var response = await ReadObexProbeResponseAsync(reader, ct);
        return new ProbeObexResponse(HexUtil.ToHex(response), ObexPacketParser.Decode(response, connectResponse));
    }

    private static async Task<byte[]> ReadObexProbeResponseAsync(DataReader reader, CancellationToken ct)
    {
        var bytes = new List<byte>();
        ushort? expectedLength = null;
        while (expectedLength is null || bytes.Count < expectedLength.Value)
        {
            var loaded = await WithTimeout(reader.LoadAsync(1).AsTask(ct), TimeSpan.FromSeconds(20), ct);
            if (loaded == 0) break;
            var value = reader.ReadByte();
            bytes.Add(value);
            if (bytes.Count == 3)
            {
                expectedLength = (ushort)((bytes[1] << 8) | bytes[2]);
                if (expectedLength < 3) break;
            }
        }
        return bytes.ToArray();
    }

    private static async Task<string> ReadTextProbeAsync(DataReader reader, CancellationToken ct)
    {
        var bytes = new List<byte>();
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(3));
            while (!timeout.IsCancellationRequested)
            {
                var loaded = await reader.LoadAsync(1).AsTask(timeout.Token);
                if (loaded == 0) break;
                bytes.Add(reader.ReadByte());
                if (bytes.Count > 0 && Encoding.ASCII.GetString(bytes.ToArray()).Contains("\r\nOK", StringComparison.OrdinalIgnoreCase)) break;
            }
        }
        catch
        {
        }
        return bytes.Count == 0 ? string.Empty : Encoding.ASCII.GetString(bytes.ToArray());
    }

    private static async Task<T> WithTimeout<T>(Task<T> task, TimeSpan timeout, CancellationToken ct)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var delay = Task.Delay(timeout, timeoutCts.Token);
        var completed = await Task.WhenAny(task, delay);
        if (completed == delay)
        {
            throw new TimeoutException($"Operation timed out after {timeout.TotalSeconds:0}s.");
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
            throw new TimeoutException($"Operation timed out after {timeout.TotalSeconds:0}s.");
        }
        timeoutCts.Cancel();
        await task;
    }

    private static void ReconcileFullReportFromPacketLog(FullMapSuiteReport report, IReadOnlyList<PacketLogEntry> packetLog)
    {
        var mapConnect = packetLog.LastOrDefault(entry => entry.Operation.Equals("Connect MAP", StringComparison.OrdinalIgnoreCase) && entry.Success);
        var obexConnect = packetLog.LastOrDefault(entry => entry.Operation.Equals("OBEX Connect", StringComparison.OrdinalIgnoreCase) && entry.Success);
        var folderListing = packetLog.LastOrDefault(entry => entry.Operation.Contains("Folder Listing", StringComparison.OrdinalIgnoreCase) && entry.Success);
        var messageListings = packetLog.Where(entry => entry.Operation.Contains("Message Listing", StringComparison.OrdinalIgnoreCase) && entry.Success).ToArray();

        if (mapConnect is not null)
        {
            report.BluetoothConnected = true;
            report.MapRfcommConnected = true;
            report.RfcommResult = BuildStageResult(true, new[] { mapConnect });
        }

        if (obexConnect is not null)
        {
            report.MapObexConnected = true;
            report.MapConnected = true;
            report.ObexResult = BuildStageResult(true, new[] { obexConnect });
            if (report.ConnectionId.Equals("none", StringComparison.OrdinalIgnoreCase))
            {
                var id = ExtractConnectionId(obexConnect.Status + " " + obexConnect.ResponseDecoded);
                if (!string.IsNullOrWhiteSpace(id)) report.ConnectionId = id;
            }
        }

        if (folderListing is not null)
        {
            report.MapFolderListingWorks = true;
            report.FolderListingResult = BuildStageResult(true, new[] { folderListing });
            if (string.IsNullOrWhiteSpace(report.FolderListingXml) && LooksLikeXml(folderListing.ResponseBodyText ?? string.Empty))
            {
                report.FolderListingXml = folderListing.ResponseBodyText ?? string.Empty;
            }
        }

        if (messageListings.Length > 0)
        {
            report.MessageListingWorks = true;
        }

        foreach (var entry in packetLog.Where(entry => !string.IsNullOrWhiteSpace(entry.MessageHandle)))
        {
            if (!report.MessageHandles.Contains(entry.MessageHandle!, StringComparer.OrdinalIgnoreCase))
            {
                report.MessageHandles.Add(entry.MessageHandle!);
            }
        }

        foreach (var entry in packetLog.Where(entry => LooksLikeXml(entry.ResponseBodyText ?? string.Empty)))
        {
            foreach (var handle in ExtractAttributeValues(entry.ResponseBodyText ?? string.Empty, "handle"))
            {
                if (!report.MessageHandles.Contains(handle, StringComparer.OrdinalIgnoreCase))
                {
                    report.MessageHandles.Add(handle);
                }
            }
        }

        var xmlMessageListings = packetLog
            .Where(entry => entry.Operation.Contains("Message Listing", StringComparison.OrdinalIgnoreCase))
            .Select(entry => entry.ResponseBodyText ?? string.Empty)
            .Where(LooksLikeXml)
            .ToArray();

        report.IPhoneReturnedEmptyXml = xmlMessageListings.Any(xml =>
            xml.Contains("<MAP-msg-listing", StringComparison.OrdinalIgnoreCase) &&
            !xml.Contains("handle=", StringComparison.OrdinalIgnoreCase));

        report.MessageHandlesFound = report.MessageHandles.Count > 0;
        report.FoldersReturningMessages = report.FolderResults
            .Where(folder => folder.MessageHandles.Count > 0)
            .Select(folder => folder.Folder)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        ValidateReportConsistency(report, packetLog);
    }

    private static string ExtractConnectionId(string text)
    {
        var marker = "ConnectionId=";
        var index = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0) return string.Empty;
        index += marker.Length;
        var end = index;
        while (end < text.Length && (char.IsLetterOrDigit(text[end]) || text[end] == 'x' || text[end] == 'X')) end++;
        return text[index..end].TrimEnd(';', ',', '.');
    }

    private static void ValidateReportConsistency(FullMapSuiteReport report, IReadOnlyList<PacketLogEntry> packetLog)
    {
        report.ConsistencyErrors.Clear();
        if (packetLog.Any(entry => entry.Operation.Equals("Connect MAP", StringComparison.OrdinalIgnoreCase) && entry.Success) && !report.MapRfcommConnected)
        {
            report.ConsistencyErrors.Add("packet-log shows successful MAP RFCOMM Connect, but report.MapRfcommConnected is false.");
        }
        if (packetLog.Any(entry => entry.Operation.Equals("OBEX Connect", StringComparison.OrdinalIgnoreCase) && entry.Success) && !report.MapObexConnected)
        {
            report.ConsistencyErrors.Add("packet-log shows successful OBEX Connect, but report.MapObexConnected is false.");
        }
        if (packetLog.Any(entry => entry.Operation.Contains("Message Listing", StringComparison.OrdinalIgnoreCase) && entry.Success) && !report.MessageListingWorks)
        {
            report.ConsistencyErrors.Add("packet-log shows successful GetMessageListing, but report.MessageListingWorks is false.");
        }
        if (packetLog.Any(entry => entry.Operation.Contains("Folder Listing", StringComparison.OrdinalIgnoreCase) && entry.Success) && !report.MapFolderListingWorks)
        {
            report.ConsistencyErrors.Add("packet-log shows successful folder listing, but report.MapFolderListingWorks is false.");
        }
    }

    private static void FinalizeSuiteConclusion(FullMapSuiteReport report)
    {
        report.MessageListingWorks = report.MessageListingWorks || report.FolderResults.SelectMany(folder => folder.MessageListings).Any(test => test.Success);
        report.IPhoneReturnedEmptyXml = report.IPhoneReturnedEmptyXml || report.FolderResults.SelectMany(folder => folder.MessageListings).Any(test => test.ReturnedXml && test.Xml.Contains("<MAP-msg-listing", StringComparison.OrdinalIgnoreCase) && !test.Handles.Any());
        report.MessageHandlesFound = report.MessageHandlesFound || report.MessageHandles.Count > 0;
        var foldersReturningMessages = report.FolderResults.Where(folder => folder.MessageHandles.Count > 0).Select(folder => folder.Folder).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (foldersReturningMessages.Length > 0) report.FoldersReturningMessages = foldersReturningMessages;
        report.PbapWorked = report.Pbap.PhonebookReadWorked;
        report.HfpWorked = report.Hfp.SafeStatusWorked;
        if (string.IsNullOrWhiteSpace(report.ExactBlocker))
        {
            report.ExactBlocker = report.MessageHandlesFound
                ? "No blocker: message handles were found."
                : report.MessageListingWorks
                    ? "iPhone returned successful MAP message-listing XML, but no tested folder/variant contained message handles."
                    : "MAP message listing did not succeed for any tested folder/variant.";
        }
    }

    private string FormatConnectionId()
    {
        return _mapSession.ConnectionId.HasValue ? $"0x{_mapSession.ConnectionId.Value:X8}" : "none";
    }

    private string ExportAutoIPhoneMapReport(AutoMapTestReport report)
    {
        report.MapConnected = _mapSession.ObexConnected;
        var packetLog = _mapSession.PacketLog;
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (string.IsNullOrWhiteSpace(desktop)) desktop = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var workingFolder = Path.Combine(Path.GetTempPath(), $"auto-iphone-map-test-{stamp}");
        var responsesFolder = Path.Combine(workingFolder, "obex-responses");
        var xmlFolder = Path.Combine(workingFolder, "map-xml");
        Directory.CreateDirectory(responsesFolder);
        Directory.CreateDirectory(xmlFolder);

        File.WriteAllText(Path.Combine(workingFolder, "packet-log.json"), JsonSerializer.Serialize(packetLog, new JsonSerializerOptions { WriteIndented = true }));
        File.WriteAllText(Path.Combine(workingFolder, "summary.txt"), BuildAutoSummary(report));
        File.WriteAllText(Path.Combine(workingFolder, "session-report.txt"), BuildAutoSessionReport(report, packetLog));
        File.WriteAllLines(Path.Combine(workingFolder, "message-handles.txt"), report.MessageHandles.Length == 0 ? new[] { report.NoMessagesParsedReason } : report.MessageHandles);

        var combinedHex = new StringBuilder();
        var responseIndex = 1;
        var xmlIndex = 1;
        foreach (var entry in packetLog.Where(entry => !string.IsNullOrWhiteSpace(entry.ResponseHex) && entry.ResponseHex != "<empty>"))
        {
            var baseName = $"{responseIndex:000}-{SafeFileName(entry.Operation)}";
            File.WriteAllBytes(Path.Combine(responsesFolder, baseName + ".bin"), HexToBytes(entry.ResponseHex));
            File.WriteAllText(Path.Combine(responsesFolder, baseName + ".hex.txt"), entry.ResponseHex);
            combinedHex.AppendLine($"{baseName}:");
            combinedHex.AppendLine(entry.ResponseHex);
            combinedHex.AppendLine();
            responseIndex++;
        }
        File.WriteAllText(Path.Combine(workingFolder, "all-obex-response-hex.txt"), combinedHex.ToString());

        foreach (var entry in packetLog.Where(entry => LooksLikeXml(entry.ResponseBodyText ?? string.Empty)))
        {
            File.WriteAllText(Path.Combine(xmlFolder, $"{xmlIndex:000}-{SafeFileName(entry.Operation)}.xml"), entry.ResponseBodyText);
            xmlIndex++;
        }

        if (xmlIndex == 1)
        {
            File.WriteAllText(Path.Combine(xmlFolder, "no-map-xml.txt"), report.NoMessagesParsedReason);
        }

        var zipPath = Path.Combine(desktop, $"AUTO-iPHONE-MAP-TEST-report-{stamp}.zip");
        if (File.Exists(zipPath)) File.Delete(zipPath);
        ZipFile.CreateFromDirectory(workingFolder, zipPath, CompressionLevel.Optimal, false);
        Directory.Delete(workingFolder, true);
        return zipPath;
    }

    private string ExportFullMapSuiteReport(FullMapSuiteReport report)
    {
        var packetLog = _mapSession.PacketLog;
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (string.IsNullOrWhiteSpace(desktop)) desktop = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var workingFolder = Path.Combine(Path.GetTempPath(), $"full-auto-map-test-{stamp}");
        var rawFolder = Path.Combine(workingFolder, "raw-obex-responses");
        var xmlFolder = Path.Combine(workingFolder, "decoded-xml");
        var folderResultsFolder = Path.Combine(workingFolder, "folder-results");
        var pbapFolder = Path.Combine(workingFolder, "pbap-results");
        var hfpFolder = Path.Combine(workingFolder, "hfp-results");
        Directory.CreateDirectory(rawFolder);
        Directory.CreateDirectory(xmlFolder);
        Directory.CreateDirectory(folderResultsFolder);
        Directory.CreateDirectory(pbapFolder);
        Directory.CreateDirectory(hfpFolder);

        var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
        ReconcileFullReportFromPacketLog(report, packetLog);
        FinalizeSuiteConclusion(report);
        ValidateReportConsistency(report, packetLog);
        File.WriteAllText(Path.Combine(workingFolder, "summary.txt"), BuildFullSummary(report));
        File.WriteAllText(Path.Combine(workingFolder, "session-report.json"), JsonSerializer.Serialize(report, jsonOptions));
        File.WriteAllText(Path.Combine(workingFolder, "state-machine-results.json"), JsonSerializer.Serialize(report.StepResults, jsonOptions));
        File.WriteAllText(Path.Combine(workingFolder, "report-consistency-errors.txt"), report.ConsistencyErrors.Count == 0 ? "No report consistency errors detected." : string.Join(Environment.NewLine, report.ConsistencyErrors));
        File.WriteAllText(Path.Combine(workingFolder, "README-RESULTS.txt"), BuildResultsReadme(report));
        File.WriteAllText(Path.Combine(workingFolder, "packet-log.json"), JsonSerializer.Serialize(packetLog, jsonOptions));
        File.WriteAllText(Path.Combine(workingFolder, "packet-log.csv"), BuildPacketLogCsv(packetLog));
        File.WriteAllText(Path.Combine(workingFolder, "folder-listing.xml"), string.IsNullOrWhiteSpace(report.FolderListingXml) ? "<no-folder-listing />" : report.FolderListingXml);
        File.WriteAllLines(Path.Combine(workingFolder, "parsed-message-handles.txt"), report.MessageHandles.Count == 0 ? new[] { report.ExactBlocker } : report.MessageHandles.Distinct(StringComparer.OrdinalIgnoreCase));

        var bMessageFolder = Path.Combine(workingFolder, "bmessage-results");
        Directory.CreateDirectory(bMessageFolder);
        File.WriteAllText(Path.Combine(bMessageFolder, "read-message-summary.json"), JsonSerializer.Serialize(report.ReadMessages, jsonOptions));
        if (report.ReadMessages.Count == 0)
        {
            File.WriteAllText(Path.Combine(bMessageFolder, "no-messages-read.txt"), report.MessageHandles.Count == 0
                ? "No message handles were found, so no bMessage reads were attempted."
                : "Message handles were found, but no read result was recorded.");
        }
        else
        {
            var readIndex = 1;
            foreach (var message in report.ReadMessages)
            {
                var safeHandle = SafeFileName(message.Handle);
                File.WriteAllText(Path.Combine(bMessageFolder, $"{readIndex:000}-{safeHandle}.bmsg"), message.BMessage);
                File.WriteAllText(Path.Combine(bMessageFolder, $"{readIndex:000}-{safeHandle}.parsed.json"), JsonSerializer.Serialize(message.Parsed, jsonOptions));
                File.WriteAllText(Path.Combine(bMessageFolder, $"{readIndex:000}-{safeHandle}.result.txt"), message.Result);
                readIndex++;
            }
        }
        File.WriteAllText(Path.Combine(workingFolder, "errors.json"), JsonSerializer.Serialize(report.Errors, jsonOptions));
        File.WriteAllText(Path.Combine(workingFolder, "final-conclusion.txt"), BuildFullConclusion(report));

        foreach (var folder in report.FolderResults)
        {
            File.WriteAllText(Path.Combine(folderResultsFolder, SafeFileName(folder.Folder) + ".json"), JsonSerializer.Serialize(folder, jsonOptions));
        }

        var rawIndex = 1;
        var xmlIndex = 1;
        foreach (var entry in packetLog.Where(entry => !string.IsNullOrWhiteSpace(entry.ResponseHex) && entry.ResponseHex != "<empty>"))
        {
            var name = $"{rawIndex:000}-{SafeFileName(entry.Operation)}";
            File.WriteAllBytes(Path.Combine(rawFolder, name + ".bin"), HexToBytes(entry.ResponseHex));
            File.WriteAllText(Path.Combine(rawFolder, name + ".hex.txt"), entry.ResponseHex);
            rawIndex++;

            if (LooksLikeXml(entry.ResponseBodyText ?? string.Empty))
            {
                File.WriteAllText(Path.Combine(xmlFolder, $"{xmlIndex:000}-{SafeFileName(entry.Operation)}.xml"), entry.ResponseBodyText);
                xmlIndex++;
            }
        }

        if (xmlIndex == 1)
        {
            File.WriteAllText(Path.Combine(xmlFolder, "no-decoded-xml.txt"), "No XML body was decoded from OBEX responses.");
        }

        File.WriteAllText(Path.Combine(pbapFolder, "pbap-results.json"), JsonSerializer.Serialize(report.Pbap, jsonOptions));
        File.WriteAllText(Path.Combine(pbapFolder, "raw-vcard-response.txt"), report.Pbap.VCardListing);
        File.WriteAllLines(Path.Combine(pbapFolder, "decoded-contacts.txt"), report.Pbap.DecodedContacts.Length == 0 ? new[] { report.Pbap.Result } : report.Pbap.DecodedContacts);
        File.WriteAllText(Path.Combine(hfpFolder, "hfp-results.json"), JsonSerializer.Serialize(report.Hfp, jsonOptions));

        var zipPath = Path.Combine(desktop, $"FULL-AUTO-MAP-TEST-report-{stamp}.zip");
        if (File.Exists(zipPath)) File.Delete(zipPath);
        ZipFile.CreateFromDirectory(workingFolder, zipPath, CompressionLevel.Optimal, false);
        Directory.Delete(workingFolder, true);
        return zipPath;
    }

    private static string BuildPacketLogCsv(IReadOnlyList<PacketLogEntry> packetLog)
    {
        var text = new StringBuilder();
        text.AppendLine("timestamp,layer,operation,success,elapsed_ms,status,request_hex,response_hex,error");
        foreach (var entry in packetLog)
        {
            text.AppendLine(string.Join(",", new[]
            {
                Csv(entry.Timestamp.ToString("O")),
                Csv(entry.Layer),
                Csv(entry.Operation),
                Csv(entry.Success.ToString()),
                Csv(entry.ElapsedMilliseconds.ToString()),
                Csv(entry.Status),
                Csv(entry.RequestHex),
                Csv(entry.ResponseHex),
                Csv(entry.Error ?? string.Empty)
            }));
        }
        return text.ToString();
    }

    private static string Csv(string value)
    {
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    private static string BuildResultsReadme(FullMapSuiteReport report)
    {
        var text = new StringBuilder();
        text.AppendLine("PhoneLink Clean-Room FULL AUTO MAP TEST Results");
        text.AppendLine();
        text.AppendLine("Start with summary.txt, session-report.json, state-machine-results.json, and packet-log.csv.");
        text.AppendLine("If report-consistency-errors.txt says PASS/no errors, the report was reconciled against the packet log before export.");
        text.AppendLine();
        text.AppendLine("No write operations are performed by this suite: no PushMessage, no delete, no mark-read, no dialing.");
        text.AppendLine();
        text.AppendLine("Current conclusion:");
        text.AppendLine(report.ExactBlocker);
        return text.ToString();
    }

    private static string SummarizeFolders(FullMapSuiteReport report)
    {
        var folders = new List<string>();
        folders.AddRange(report.FolderResults.Select(folder => folder.Folder));
        folders.AddRange(ExtractAttributeValues(report.FolderListingXml ?? string.Empty, "name"));
        var unique = folders.Where(folder => !string.IsNullOrWhiteSpace(folder)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return unique.Length == 0 ? "none" : string.Join(", ", unique);
    }

    private static string BuildFullSummary(FullMapSuiteReport report)
    {
        var text = new StringBuilder();
        text.AppendLine("FULL AUTO MAP TEST Summary");
        text.AppendLine($"Bluetooth connected: {YesNo(report.BluetoothConnected)}");
        text.AppendLine($"MAP service advertised: {YesNo(report.MapRfcommConnected)}");
        text.AppendLine($"PBAP service advertised: {YesNo(report.Pbap.ServiceFound)}");
        text.AppendLine($"HFP service advertised: {YesNo(report.Hfp.ServiceFound)}");
        text.AppendLine($"RFCOMM MAP connected: {YesNo(report.MapRfcommConnected)}");
        text.AppendLine($"OBEX MAP connected: {YesNo(report.MapObexConnected)}");
        text.AppendLine($"OBEX response code: {(report.MapObexConnected ? "0xA0" : "none")}");
        text.AppendLine($"Connection ID: {report.ConnectionId}");
        text.AppendLine($"MAP folder listing works: {YesNo(report.MapFolderListingWorks)}");
        text.AppendLine($"Folders found: {SummarizeFolders(report)}");
        text.AppendLine($"Message listing works: {YesNo(report.MessageListingWorks)}");
        text.AppendLine($"Folders returned messages: {(report.FoldersReturningMessages.Length == 0 ? "none" : string.Join(", ", report.FoldersReturningMessages))}");
        text.AppendLine($"iPhone returned empty XML: {YesNo(report.IPhoneReturnedEmptyXml)}");
        text.AppendLine($"Total message handles found: {report.MessageHandles.Distinct(StringComparer.OrdinalIgnoreCase).Count()}");
        text.AppendLine($"Message handles found: {YesNo(report.MessageHandlesFound)}");
        text.AppendLine($"Messages read: {report.ReadMessages.Count}");
        text.AppendLine($"bMessage bodies decoded: {report.ReadMessages.Count(message => !string.IsNullOrWhiteSpace(message.BMessage))}");
        text.AppendLine($"PBAP result: {report.Pbap.Result}");
        text.AppendLine($"HFP result: {report.Hfp.Result}");
        text.AppendLine($"Tests executed: {report.StepResults.Count}");
        text.AppendLine($"Passed: {report.StepResults.Count(step => step.Status == "PASS")}");
        text.AppendLine($"Failed: {report.StepResults.Count(step => step.Status == "FAIL")}");
        text.AppendLine($"Timed out: {report.StepResults.Count(step => step.Status == "TIMEOUT")}");
        text.AppendLine($"Skipped: {report.StepResults.Count(step => step.Status == "SKIPPED")}");
        text.AppendLine($"Report consistency: {(report.ConsistencyErrors.Count == 0 ? "PASS" : "FAIL")}");
        text.AppendLine($"Exact blocker: {report.ExactBlocker}");
        text.AppendLine($"Recommended next step: {(report.MessageHandlesFound ? "Read up to three handles in read-only mode and verify bMessage parsing." : "Use this truthful report to decide whether iOS is exposing an empty MAP message store or whether more documented folder/message-listing parameters are needed.")}");
        return text.ToString();
    }

    private static string BuildFullConclusion(FullMapSuiteReport report)
    {
        var text = new StringBuilder();
        text.AppendLine(BuildFullSummary(report));
        text.AppendLine("Conclusion:");
        text.AppendLine(report.ExactBlocker);
        if (report.Errors.Count > 0)
        {
            text.AppendLine();
            text.AppendLine("Errors:");
            foreach (var error in report.Errors)
            {
                text.AppendLine($"{error.Timestamp:O} | {error.TestName} | HRESULT={error.HResult} | {error.Message}");
            }
        }
        return text.ToString();
    }

    private static string BuildAutoSummary(AutoMapTestReport report)
    {
        var text = new StringBuilder();
        text.AppendLine("AUTO iPHONE MAP TEST Final Summary");
        text.AppendLine($"Did MAP connect? {YesNo(report.MapConnected)}");
        text.AppendLine($"Did GetMessageListing succeed? {YesNo(report.GetMessageListingSucceeded)}");
        text.AppendLine($"Did iPhone return XML? {YesNo(report.IPhoneReturnedXml)}");
        text.AppendLine($"Did XML contain message handles? {YesNo(report.XmlContainedMessageHandles)}");
        text.AppendLine($"If not, exact reason: {(report.XmlContainedMessageHandles ? "Message handles were present." : report.NoMessagesParsedReason)}");
        return text.ToString();
    }

    private static string BuildAutoSessionReport(AutoMapTestReport report, IReadOnlyList<PacketLogEntry> packetLog)
    {
        var text = new StringBuilder();
        text.AppendLine("AUTO iPHONE MAP TEST Session Report");
        text.AppendLine($"Generated: {DateTimeOffset.Now:O}");
        text.AppendLine($"Device selected: {report.DeviceSelected}");
        text.AppendLine($"RFCOMM result: {report.RfcommResult}");
        text.AppendLine($"OBEX result: {report.ObexResult}");
        text.AppendLine($"Connection ID: {report.ConnectionId}");
        text.AppendLine($"List MAP result: {report.ListMapResult}");
        text.AppendLine($"List Messages result: {report.ListMessagesResult}");
        text.AppendLine($"Exact failure point: {report.FailurePoint}");
        text.AppendLine($"No messages parsed reason: {report.NoMessagesParsedReason}");
        text.AppendLine();
        text.AppendLine("Full packet log:");
        foreach (var entry in packetLog)
        {
            text.AppendLine($"{entry.Timestamp:O} | {entry.Layer} | {entry.Operation} | Success={entry.Success} | ElapsedMs={entry.ElapsedMilliseconds}");
            if (entry.AttemptNumber is not null) text.AppendLine($"Attempt: {entry.AttemptNumber}");
            if (!string.IsNullOrWhiteSpace(entry.ProfileName)) text.AppendLine($"Profile: {entry.ProfileName}");
            if (!string.IsNullOrWhiteSpace(entry.MessageHandle)) text.AppendLine($"MessageHandle: {entry.MessageHandle}");
            text.AppendLine($"Status: {entry.Status}");
            if (!string.IsNullOrWhiteSpace(entry.RequestHex)) text.AppendLine($"RequestHex: {entry.RequestHex}");
            if (!string.IsNullOrWhiteSpace(entry.RequestDecoded)) text.AppendLine($"RequestDecoded: {entry.RequestDecoded}");
            if (!string.IsNullOrWhiteSpace(entry.ResponseHex)) text.AppendLine($"ResponseHex: {entry.ResponseHex}");
            if (!string.IsNullOrWhiteSpace(entry.ResponseDecoded)) text.AppendLine($"ResponseDecoded: {entry.ResponseDecoded}");
            if (!string.IsNullOrWhiteSpace(entry.ResponseBodyText)) text.AppendLine($"ResponseBodyText: {entry.ResponseBodyText}");
            if (!string.IsNullOrWhiteSpace(entry.Error)) text.AppendLine($"Error: {entry.Error}");
            text.AppendLine();
        }
        return text.ToString();
    }

    private static byte[] HexToBytes(string hex)
    {
        var parts = hex.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var bytes = new byte[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            bytes[i] = Convert.ToByte(parts[i], 16);
        }
        return bytes;
    }

    private static string SafeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? "response" : cleaned;
    }

    private static string YesNo(bool value)
    {
        return value ? "yes" : "no";
    }

    private void ShowZipCompletePopup(string zipPath, long fileSize)
    {
        using var dialog = new Form
        {
            Text = "FULL AUTO MAP TEST Complete",
            Width = 620,
            Height = 180,
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false
        };

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Padding = new Padding(12) };
        dialog.Controls.Add(layout);
        layout.Controls.Add(new Label
        {
            Text = $"ZIP saved:\n{zipPath}\n\nSize: {fileSize:N0} bytes",
            Dock = DockStyle.Fill,
            AutoSize = true
        }, 0, 0);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var close = new Button { Text = "Close", DialogResult = DialogResult.OK };
        var openFolder = new Button { Text = "Open Folder", AutoSize = true };
        openFolder.Click += (_, _) =>
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = "/select,\"" + zipPath + "\"",
                UseShellExecute = true
            });
        };
        buttons.Controls.Add(close);
        buttons.Controls.Add(openFolder);
        layout.Controls.Add(buttons, 0, 2);
        dialog.AcceptButton = close;
        dialog.ShowDialog(this);
    }

    private void UpdateDeviceActionState()
    {
        var selected = CurrentSelectedDevice();
        var hasDevice = selected is not null;
        var connected = selected is not null && IsConnectedDevice(selected);
        _capabilityButton.Enabled = hasDevice;
        _listServicesButton.Enabled = hasDevice;
        _dumpPropertiesButton.Enabled = hasDevice;
        _obexButton.Enabled = connected;
        _connectMapButton.Enabled = connected;
        _runConnectProfilesButton.Enabled = connected;
    }

    private void DrawDeviceListItem(object? sender, DrawItemEventArgs args)
    {
        if (args.Index < 0 || args.Index >= _deviceList.Items.Count) return;

        args.DrawBackground();
        var selected = (args.State & DrawItemState.Selected) == DrawItemState.Selected;
        var textColor = selected ? SystemColors.HighlightText : _deviceList.ForeColor;
        var text = _deviceList.Items[args.Index]?.ToString() ?? string.Empty;
        var bounds = new Rectangle(args.Bounds.X + 4, args.Bounds.Y, args.Bounds.Width - 8, args.Bounds.Height);
        TextRenderer.DrawText(args.Graphics, text, _deviceList.Font, bounds, textColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        args.DrawFocusRectangle();
    }

    private void SafeUi(Action action)
    {
        if (IsDisposed || Disposing || !IsHandleCreated) return;
        if (InvokeRequired)
        {
            try { BeginInvoke(action); }
            catch (InvalidOperationException) when (IsDisposed || Disposing) { }
            return;
        }
        action();
    }

    private sealed record PacketRowTag(PacketLogEntry Entry, string? MessageHandle);

    private sealed record SuiteProgress(int Total)
    {
        public int Current { get; set; }
    }

    private sealed record DeviceSummary(string Name, string DeviceId, string BluetoothAddress, string ConnectionStatus);

    private sealed record MessageListingVariant(string Name, string Operation, byte[] AppParams);

    private sealed record ProbeObexResponse(string RawHex, ObexDecodedPacket Decoded);

    private sealed record SuiteError(
        DateTimeOffset Timestamp,
        string TestName,
        string Message,
        string? ExceptionType,
        string HResult,
        string? Exception);

    private sealed record SuiteStepResult(
        int StepNumber,
        string Name,
        DateTimeOffset StartedAt,
        DateTimeOffset CompletedAt,
        string Status,
        string Reason,
        long ElapsedMilliseconds);

    private sealed record AtCommandResult(string Command, string Response);

    private sealed class FullMapSuiteReport
    {
        public DateTimeOffset StartedAt { get; set; }
        public DateTimeOffset CompletedAt { get; set; }
        public DeviceSummary? Device { get; set; }
        public bool BluetoothConnected { get; set; }
        public bool MapRfcommConnected { get; set; }
        public bool MapObexConnected { get; set; }
        public bool MapConnected { get; set; }
        public bool MapFolderListingWorks { get; set; }
        public bool MessageListingWorks { get; set; }
        public bool IPhoneReturnedEmptyXml { get; set; }
        public bool MessageHandlesFound { get; set; }
        public bool PbapWorked { get; set; }
        public bool HfpWorked { get; set; }
        public string RfcommResult { get; set; } = "NOT RUN";
        public string ObexResult { get; set; } = "NOT RUN";
        public string ConnectionId { get; set; } = "none";
        public string FolderListingResult { get; set; } = "NOT RUN";
        public string FolderListingXml { get; set; } = string.Empty;
        public List<FolderSuiteResult> FolderResults { get; } = new();
        public List<string> MessageHandles { get; } = new();
        public string[] FoldersReturningMessages { get; set; } = Array.Empty<string>();
        public List<ReadMessageResult> ReadMessages { get; } = new();
        public PbapResult Pbap { get; set; } = new();
        public HfpResult Hfp { get; set; } = new();
        public List<SuiteError> Errors { get; } = new();
        public List<SuiteStepResult> StepResults { get; } = new();
        public List<string> ConsistencyErrors { get; } = new();
        public string ExactBlocker { get; set; } = string.Empty;
    }

    private sealed class FolderSuiteResult
    {
        public string Folder { get; set; } = string.Empty;
        public string SetFolderResult { get; set; } = "NOT RUN";
        public List<MessageListingVariantResult> MessageListings { get; } = new();
        public List<string> MessageHandles { get; } = new();
        public string Blocker { get; set; } = string.Empty;
    }

    private sealed class MessageListingVariantResult
    {
        public string Name { get; set; } = string.Empty;
        public string Operation { get; set; } = string.Empty;
        public bool Success { get; set; }
        public bool ReturnedXml { get; set; }
        public string Xml { get; set; } = string.Empty;
        public string[] Handles { get; set; } = Array.Empty<string>();
        public string Result { get; set; } = string.Empty;
        public string NoHandlesReason { get; set; } = string.Empty;
    }

    private sealed record ReadMessageResult(string Handle, bool Success, string BMessage, Dictionary<string, string> Parsed, string Result);

    private sealed class PbapResult
    {
        public bool ServiceFound { get; set; }
        public bool RfcommConnected { get; set; }
        public bool ObexConnected { get; set; }
        public bool PhonebookReadWorked { get; set; }
        public string VCardListing { get; set; } = string.Empty;
        public string[] DecodedContacts { get; set; } = Array.Empty<string>();
        public List<string> RawResponses { get; } = new();
        public string Result { get; set; } = "NOT RUN";
    }

    private sealed class HfpResult
    {
        public bool ServiceFound { get; set; }
        public bool RfcommConnected { get; set; }
        public bool SafeStatusWorked { get; set; }
        public List<AtCommandResult> AtResponses { get; } = new();
        public string Result { get; set; } = "NOT RUN";
    }


    private sealed class InboxMessageView
    {
        public string Handle { get; set; } = string.Empty;
        public string Folder { get; set; } = "Inbox";
        public string DeviceId { get; set; } = string.Empty;
        public string DeviceName { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string From { get; set; } = string.Empty;
        public string To { get; set; } = string.Empty;
        public string Date { get; set; } = string.Empty;
        public string Subject { get; set; } = string.Empty;
        public string Body { get; set; } = string.Empty;
        public string RawBMessage { get; set; } = string.Empty;
        public Dictionary<string, string> Parsed { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public string Result { get; set; } = string.Empty;
    }

    private sealed class AutoMapTestReport
    {
        public string DeviceSelected { get; set; } = "none";
        public string RfcommResult { get; set; } = "NOT RUN";
        public string ObexResult { get; set; } = "NOT RUN";
        public string ConnectionId { get; set; } = "none";
        public string ListMapResult { get; set; } = "NOT RUN";
        public string ListMessagesResult { get; set; } = "NOT RUN";
        public string FailurePoint { get; set; } = "NONE";
        public bool MapConnected { get; set; }
        public bool GetMessageListingSucceeded { get; set; }
        public bool IPhoneReturnedXml { get; set; }
        public bool XmlContainedMessageHandles { get; set; }
        public string NoMessagesParsedReason { get; set; } = "Message listing was not run.";
        public string[] MessageHandles { get; set; } = Array.Empty<string>();
    }

    private sealed record MessageListingAnalysis(
        bool GetMessageListingSucceeded,
        bool ReturnedXml,
        bool ContainsHandles,
        string NoHandlesReason);
}

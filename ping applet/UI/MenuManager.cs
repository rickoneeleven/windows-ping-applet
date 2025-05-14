using System;
using System.Drawing;
using System.Windows.Forms;
using System.Diagnostics;
using System.Linq;
using ping_applet.Core.Interfaces;
using ping_applet.Utils;
using System.Collections.Generic;

namespace ping_applet.UI
{
    public class MenuManager : IDisposable
    {
        private readonly ContextMenuStrip contextMenu;
        private readonly BuildInfoProvider buildInfoProvider;
        private readonly ILoggingService loggingService;
        private readonly StartupManager startupManager;
        private readonly KnownAPManager knownAPManager;
        private readonly string logPath;
        private bool isDisposed;

        private ToolStripMenuItem locationServicesMenuItem;
        private ToolStripMenuItem startupMenuItem;
        private ToolStripMenuItem notificationsMenuItem;
        private ToolStripMenuItem currentAPMenuItem;
        private ToolStripMenuItem knownAPsMenuItem;

        private ToolStripMenuItem pingTargetMenuItem;
        private ToolStripMenuItem defaultGatewayTargetMenuItem;
        private ToolStripMenuItem customHostTargetMenuItem;
        private ToolStripMenuItem setCustomTargetDialogMenuItem;

        private string _currentGatewayDisplayValue = "N/A";
        private string _currentCustomHostDisplayValue = "Not Set"; // This should store the user-input string for the custom host

        public event EventHandler QuitRequested;
        public bool NotificationsEnabled => notificationsMenuItem?.Checked ?? true;

        public event EventHandler RequestSetDefaultGatewayTarget;
        public event EventHandler<string> RequestActivateExistingCustomTarget;
        public event EventHandler RequestShowSetCustomTargetDialog;

        public MenuManager(
            BuildInfoProvider buildInfoProvider,
            ILoggingService loggingService,
            StartupManager startupManager,
            KnownAPManager knownAPManager,
            string logPath)
        {
            this.buildInfoProvider = buildInfoProvider ?? throw new ArgumentNullException(nameof(buildInfoProvider));
            this.loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
            this.startupManager = startupManager ?? throw new ArgumentNullException(nameof(startupManager));
            this.knownAPManager = knownAPManager ?? throw new ArgumentNullException(nameof(knownAPManager));
            this.logPath = logPath ?? throw new ArgumentNullException(nameof(logPath));

            contextMenu = new ContextMenuStrip();
            InitializeContextMenu();
            loggingService.LogInfo("[MenuManager] Initialized.");
        }

        public ContextMenuStrip GetContextMenu() => contextMenu;

        private void InitializeContextMenu()
        {
            try
            {
                pingTargetMenuItem = new ToolStripMenuItem("Ping Target");

                defaultGatewayTargetMenuItem = new ToolStripMenuItem($"Default Gateway ({_currentGatewayDisplayValue})");
                defaultGatewayTargetMenuItem.Click += DefaultGatewayTargetMenuItem_Click;
                pingTargetMenuItem.DropDownItems.Add(defaultGatewayTargetMenuItem);

                customHostTargetMenuItem = new ToolStripMenuItem($"Custom Host: ({_currentCustomHostDisplayValue})");
                customHostTargetMenuItem.Click += CustomHostTargetMenuItem_Click;

                setCustomTargetDialogMenuItem = new ToolStripMenuItem("Set Target & Ping...");
                setCustomTargetDialogMenuItem.Click += SetCustomTargetDialogMenuItem_Click;
                customHostTargetMenuItem.DropDownItems.Add(setCustomTargetDialogMenuItem);
                pingTargetMenuItem.DropDownItems.Add(customHostTargetMenuItem);

                contextMenu.Items.Add(pingTargetMenuItem);
                contextMenu.Items.Add(new ToolStripSeparator());

                InitializeLocationServicesMenuItem();
                contextMenu.Items.Add(new ToolStripSeparator());
                InitializeCurrentAPMenuItem();
                InitializeKnownAPsMenuItem();
                contextMenu.Items.Add(new ToolStripSeparator());
                AddVersionMenuItem();
                contextMenu.Items.Add(new ToolStripSeparator());
                AddViewLogsMenuItem();
                contextMenu.Items.Add(new ToolStripSeparator());
                InitializeStartupMenuItem();
                InitializeNotificationsMenuItem();
                contextMenu.Items.Add(new ToolStripSeparator());
                AddQuitMenuItem();

                contextMenu.Opening += ContextMenu_Opening;
            }
            catch (Exception ex)
            {
                loggingService.LogError("Error initializing context menu", ex);
                throw;
            }
        }

        private void DefaultGatewayTargetMenuItem_Click(object sender, EventArgs e)
        {
            loggingService.LogInfo("[MenuManager] 'Default Gateway' ping target selected by user.");
            RequestSetDefaultGatewayTarget?.Invoke(this, EventArgs.Empty);
        }

        private void CustomHostTargetMenuItem_Click(object sender, EventArgs e)
        {
            // This item is "Custom Host: (current_target_value)"
            // If current_target_value is a valid host (not "Not Set"),
            // clicking this parent item should attempt to make it the active custom target.
            // _currentCustomHostDisplayValue holds the actual user-input string.
            if (_currentCustomHostDisplayValue != "Not Set" && !string.IsNullOrWhiteSpace(_currentCustomHostDisplayValue))
            {
                loggingService.LogInfo($"[MenuManager] User clicked 'Custom Host: ({_currentCustomHostDisplayValue})'. Requesting activation.");
                RequestActivateExistingCustomTarget?.Invoke(this, _currentCustomHostDisplayValue);
            }
            else
            {
                // Clicked on "Custom Host: (Not Set)".
                // As per PRD, this parent item click does nothing if no host is set.
                // User must expand it and click "Set Target & Ping...".
                loggingService.LogInfo($"[MenuManager] User clicked 'Custom Host: (Not Set)'. No action by parent item; user should use 'Set Target & Ping...'.");
            }
        }

        private void SetCustomTargetDialogMenuItem_Click(object sender, EventArgs e)
        {
            loggingService.LogInfo("[MenuManager] 'Set Target & Ping...' selected by user.");
            RequestShowSetCustomTargetDialog?.Invoke(this, EventArgs.Empty);
        }

        public void UpdatePingTargetDisplay(bool isCustomHostActive, string gatewayDisplayValue, string customHostDisplayValueFromAppController)
        {
            if (isDisposed) return;

            _currentGatewayDisplayValue = string.IsNullOrWhiteSpace(gatewayDisplayValue) ? "N/A" : gatewayDisplayValue;
            // Store the user-input string for the custom host. This is what AppController provides.
            _currentCustomHostDisplayValue = string.IsNullOrWhiteSpace(customHostDisplayValueFromAppController) ? "Not Set" : customHostDisplayValueFromAppController;

            if (defaultGatewayTargetMenuItem != null)
            {
                defaultGatewayTargetMenuItem.Text = $"Default Gateway ({_currentGatewayDisplayValue})";
                defaultGatewayTargetMenuItem.Checked = !isCustomHostActive;
            }
            if (customHostTargetMenuItem != null)
            {
                customHostTargetMenuItem.Text = $"Custom Host: ({_currentCustomHostDisplayValue})";
                customHostTargetMenuItem.Checked = isCustomHostActive;
                customHostTargetMenuItem.Enabled = true; // CRITICAL FIX: Parent item must be enabled to show its submenu.
            }
            loggingService.LogInfo($"[MenuManager] Ping target display updated. CustomActive: {isCustomHostActive}, Gateway: {_currentGatewayDisplayValue}, CustomUserInput: '{_currentCustomHostDisplayValue}'");
        }

        private void InitializeLocationServicesMenuItem() { locationServicesMenuItem = new ToolStripMenuItem("AP Tracking Disabled") { Visible = false, ForeColor = Color.Red }; locationServicesMenuItem.Click += (s, e) => OpenLocationSettings(); var ld = new ToolStripMenuItem("To enable AP Tracking, turn on Location Services for desktop apps.") { Enabled = false }; locationServicesMenuItem.DropDownItems.Add(ld); contextMenu.Items.Add(locationServicesMenuItem); }
        private void InitializeCurrentAPMenuItem() { currentAPMenuItem = new ToolStripMenuItem("AP: Initializing...") { Enabled = false }; contextMenu.Items.Add(currentAPMenuItem); }
        private void InitializeKnownAPsMenuItem() { knownAPsMenuItem = new ToolStripMenuItem("Known APs"); contextMenu.Items.Add(knownAPsMenuItem); }
        private void AddVersionMenuItem() { var vi = new ToolStripMenuItem($"Version {buildInfoProvider.VersionString} ({buildInfoProvider.BuildTimestamp})") { Enabled = false }; contextMenu.Items.Add(vi); }
        private void AddViewLogsMenuItem() { var vli = new ToolStripMenuItem("View Log"); vli.Click += ViewLogs_Click; contextMenu.Items.Add(vli); }
        private void InitializeStartupMenuItem() { startupMenuItem = new ToolStripMenuItem("Start on Login") { Checked = startupManager.IsStartupEnabled() }; startupMenuItem.Click += StartupMenuItem_Click; contextMenu.Items.Add(startupMenuItem); }
        private void InitializeNotificationsMenuItem() { notificationsMenuItem = new ToolStripMenuItem("Notification Popups") { Checked = knownAPManager.GetNotificationsEnabled() }; notificationsMenuItem.Click += NotificationsMenuItem_Click; contextMenu.Items.Add(notificationsMenuItem); }
        private void AddQuitMenuItem() { var qi = new ToolStripMenuItem("Quit"); qi.Click += (s, e) => QuitRequested?.Invoke(this, EventArgs.Empty); contextMenu.Items.Add(qi); }
        private void NotificationsMenuItem_Click(object sender, EventArgs e) { try { notificationsMenuItem.Checked = !notificationsMenuItem.Checked; knownAPManager.SetNotificationsEnabled(notificationsMenuItem.Checked); } catch (Exception ex) { loggingService.LogError("Err toggle notifications", ex); MessageBox.Show("Fail save notification pref.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error); } }
        private void OpenLocationSettings() { try { Process.Start("ms-settings:privacy-location"); } catch (Exception ex) { loggingService.LogError("Err open location settings", ex); MessageBox.Show("Can't open Location Settings. Win Settings->Privacy & Security->Location", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error); } }
        public void UpdateLocationServicesState(bool isDisabled) { if (isDisposed || locationServicesMenuItem == null) return; try { locationServicesMenuItem.Visible = isDisabled; } catch (Exception ex) { loggingService.LogError("Err update LS state menu", ex); } }
        public void UpdateCurrentAP(string displayName) { if (isDisposed || currentAPMenuItem == null) return; try { currentAPMenuItem.Text = string.IsNullOrEmpty(displayName) ? "AP: Not Connected" : $"AP: {displayName}"; } catch (Exception ex) { loggingService.LogError("Err update current AP menu", ex); } }
        private void ContextMenu_Opening(object sender, System.ComponentModel.CancelEventArgs e) { if (isDisposed) { e.Cancel = true; return; } UpdateKnownAPsMenu(); }

        private void UpdateKnownAPsMenu()
        {
            try
            {
                knownAPsMenuItem.DropDownItems.Clear();
                var allBssids = knownAPManager.RootBssids.Concat(knownAPManager.UnsortedBssids).Distinct();
                var namedAPs = new List<(string bssid, string name)>();
                var unnamedAPs = new List<string>();
                foreach (var bssid in allBssids) { var rawName = knownAPManager.GetDisplayName(bssid, includeDetails: false); var displayName = knownAPManager.GetDisplayName(bssid); if (rawName != bssid) namedAPs.Add((bssid, displayName)); else unnamedAPs.Add(bssid); }
                namedAPs = namedAPs.OrderBy(ap => !char.IsDigit(ap.name.FirstOrDefault())).ThenBy(ap => ap.name).ToList();
                foreach (var (bssid, _) in namedAPs) AddAPMenuItem(knownAPsMenuItem.DropDownItems, bssid);
                if (namedAPs.Any() && unnamedAPs.Any()) knownAPsMenuItem.DropDownItems.Add(new ToolStripSeparator());
                if (unnamedAPs.Any()) { var unsortedMenu = new ToolStripMenuItem("Unsorted"); foreach (var bssid in unnamedAPs.OrderBy(b => b)) AddAPMenuItem(unsortedMenu.DropDownItems, bssid); knownAPsMenuItem.DropDownItems.Add(unsortedMenu); }
            }
            catch (Exception ex) { loggingService.LogError("Error updating Known APs menu", ex); }
        }

        private void AddAPMenuItem(ToolStripItemCollection collection, string bssid) { var dn = knownAPManager.GetDisplayName(bssid); var item = new ToolStripMenuItem(dn); var ri = new ToolStripMenuItem("Rename"); ri.Click += (s, e) => RenameAP(bssid); var di = new ToolStripMenuItem("Delete"); di.Click += (s, e) => DeleteAP(bssid); item.DropDownItems.AddRange(new ToolStripItem[] { ri, new ToolStripSeparator(), di }); collection.Add(item); }
        private void RenameAP(string bssid) { try { using (var d = new Form { Text = "Rename AP", StartPosition = FormStartPosition.CenterScreen, Width = 300, Height = 185, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false }) { var nl = new Label { Text = "New name:", Left = 10, Top = 20, Width = 270 }; var bl = new Label { Text = "BSSID:", Left = 10, Top = 45, Width = 45 }; var btb = new TextBox { Text = bssid, Left = 60, Top = 42, Width = 220, ReadOnly = true, BorderStyle = BorderStyle.None, BackColor = SystemColors.Control, Font = new Font(bl.Font, FontStyle.Regular) }; var tb = new TextBox { Text = knownAPManager.GetDisplayName(bssid, includeDetails: false), Left = 10, Top = 75, Width = 270 }; var btn = new Button { Text = "OK", DialogResult = DialogResult.OK, Left = 105, Top = 105, Width = 75 }; d.Controls.AddRange(new Control[] { nl, bl, btb, tb, btn }); d.AcceptButton = btn; if (d.ShowDialog() == DialogResult.OK && !string.IsNullOrWhiteSpace(tb.Text)) { knownAPManager.RenameAP(bssid, tb.Text); UpdateCurrentAP(knownAPManager.GetDisplayName(bssid)); } } } catch (Exception ex) { loggingService.LogError($"Err rename AP {bssid}", ex); MessageBox.Show("Fail rename AP.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error); } }
        private void DeleteAP(string bssid) { try { if (MessageBox.Show("Sure to delete AP?", "Confirm Delete", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes) knownAPManager.DeleteAP(bssid); } catch (Exception ex) { loggingService.LogError($"Err delete AP {bssid}", ex); MessageBox.Show("Fail delete AP.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error); } }
        private void StartupMenuItem_Click(object sender, EventArgs e) { try { bool ns = !startupMenuItem.Checked; if (startupManager.SetStartupEnabled(ns)) startupMenuItem.Checked = ns; else MessageBox.Show("Fail update startup settings.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error); } catch (Exception ex) { MessageBox.Show($"Fail update startup: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error); } }
        private void ViewLogs_Click(object sender, EventArgs e) { try { if (!System.IO.File.Exists(logPath)) { MessageBox.Show("Log not found.", "Log Not Found", MessageBoxButtons.OK, MessageBoxIcon.Information); return; } Process.Start(new ProcessStartInfo { FileName = logPath, UseShellExecute = true }); } catch (Exception ex) { MessageBox.Show($"Fail open log: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error); } }

        protected virtual void Dispose(bool disposing)
        {
            if (!isDisposed && disposing)
            {
                contextMenu?.Dispose();
                isDisposed = true;
                loggingService?.LogInfo("[MenuManager] Disposed."); // Add null check for loggingService
            }
            else if (!isDisposed) // Ensure isDisposed is set even if not disposing managed resources (e.g. finalizer)
            {
                isDisposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}
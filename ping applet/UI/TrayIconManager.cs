﻿using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using ping_applet.Utils;
using ping_applet.Core.Interfaces;

namespace ping_applet.UI
{
    public class TrayIconManager : IDisposable
    {
        private readonly NotifyIcon trayIcon;
        private readonly ContextMenuStrip contextMenu;
        private readonly IconGenerator iconGenerator;
        private readonly BuildInfoProvider buildInfoProvider;
        private readonly StartupManager startupManager;
        private readonly KnownAPManager knownAPManager;
        private readonly ILoggingService loggingService;
        private bool isDisposed;
        private readonly string logPath;
        private ToolStripMenuItem startupMenuItem;
        private ToolStripMenuItem currentAPMenuItem;
        private ToolStripMenuItem knownAPsMenuItem;
        private ToolStripMenuItem locationServicesMenuItem;

        // UI state tracking
        private string currentDisplayText;
        private string currentTooltipText;
        private bool currentErrorState;
        private bool currentTransitionState;
        private bool currentUseBlackText;
        private string currentBSSID;

        // Constants for balloon tips
        private const int BALLOON_TIMEOUT = 2000; // 2 seconds
        private const string BALLOON_TITLE = "Network Change";

        public event EventHandler QuitRequested;
        public bool IsDisposed => isDisposed;

        public TrayIconManager(BuildInfoProvider buildInfoProvider, ILoggingService loggingService)
        {
            this.buildInfoProvider = buildInfoProvider ?? throw new ArgumentNullException(nameof(buildInfoProvider));
            this.loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
            this.startupManager = new StartupManager();
            this.knownAPManager = new KnownAPManager(loggingService);
            iconGenerator = new IconGenerator();

            logPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PingApplet",
                "ping.log"
            );

            contextMenu = new ContextMenuStrip();
            InitializeContextMenu();

            trayIcon = new NotifyIcon
            {
                Icon = iconGenerator.CreateNumberIcon("--"),
                Visible = true,
                ContextMenuStrip = contextMenu,
                Text = "Initializing..."
            };
        }

        public void ShowTransitionBalloon(string oldBssid, string newBssid)
        {
            if (isDisposed) return;

            try
            {
                string message;
                if (string.IsNullOrEmpty(oldBssid))
                {
                    // Initial connection or connection after being disconnected
                    var newApName = GetAPDisplayName(newBssid);
                    message = $"Connected to {newApName}";
                }
                else if (string.IsNullOrEmpty(newBssid))
                {
                    // Disconnection
                    var oldApName = GetAPDisplayName(oldBssid);
                    message = $"Disconnected from {oldApName}";
                }
                else
                {
                    // Transition between APs
                    var oldApName = GetAPDisplayName(oldBssid);
                    var newApName = GetAPDisplayName(newBssid);
                    message = $"Switched from {oldApName} to {newApName}";
                }

                trayIcon.ShowBalloonTip(
                    BALLOON_TIMEOUT,
                    BALLOON_TITLE,
                    message,
                    ToolTipIcon.Info
                );

                loggingService.LogInfo($"Showed transition balloon: {message}");
            }
            catch (Exception ex)
            {
                loggingService.LogError("Error showing transition balloon", ex);
            }
        }
        private void InitializeContextMenu()
        {
            try
            {
                // Add location services warning (initially hidden)
                locationServicesMenuItem = new ToolStripMenuItem("AP Tracking Disabled")
                {
                    Visible = false,
                    ForeColor = Color.Red
                };
                locationServicesMenuItem.Click += (s, e) => OpenLocationSettings();
                var locationDescription = new ToolStripMenuItem(
                    "To enable AP Tracking, you need to turn on \"Location Services->Let apps access your location->Let desktop apps access your location\" " +
                    "so the \"Network Command Shell\" works. We do not track your location.")
                {
                    Enabled = false
                };
                locationServicesMenuItem.DropDownItems.Add(locationDescription);
                contextMenu.Items.Add(locationServicesMenuItem);
                contextMenu.Items.Add(new ToolStripSeparator());

                // Add current AP display
                currentAPMenuItem = new ToolStripMenuItem("AP: not on wireless")
                {
                    Enabled = false
                };
                contextMenu.Items.Add(currentAPMenuItem);

                // Add Known APs menu
                knownAPsMenuItem = new ToolStripMenuItem("Known APs");
                contextMenu.Items.Add(knownAPsMenuItem);

                contextMenu.Items.Add(new ToolStripSeparator());

                // Version info
                var versionItem = new ToolStripMenuItem($"Version {buildInfoProvider.VersionString}")
                {
                    Enabled = false
                };
                contextMenu.Items.Add(versionItem);

                contextMenu.Items.Add(new ToolStripSeparator());

                // Add View Logs option
                var viewLogsItem = new ToolStripMenuItem("View Log");
                viewLogsItem.Click += ViewLogs_Click;
                contextMenu.Items.Add(viewLogsItem);

                contextMenu.Items.Add(new ToolStripSeparator());

                // Add Start on Login option
                startupMenuItem = new ToolStripMenuItem("Start on Login");
                startupMenuItem.Click += StartupMenuItem_Click;
                startupMenuItem.Checked = startupManager.IsStartupEnabled();
                contextMenu.Items.Add(startupMenuItem);

                contextMenu.Items.Add(new ToolStripSeparator());

                var quitItem = new ToolStripMenuItem("Quit");
                quitItem.Click += (s, e) => QuitRequested?.Invoke(this, EventArgs.Empty);
                contextMenu.Items.Add(quitItem);

                // Update menu on opening
                contextMenu.Opening += ContextMenu_Opening;
            }
            catch (Exception ex)
            {
                loggingService.LogError("Error initializing context menu", ex);
                throw;
            }
        }

        private void OpenLocationSettings()
        {
            try
            {
                Process.Start("ms-settings:privacy-location");
            }
            catch (Exception ex)
            {
                loggingService.LogError("Error opening location settings", ex);
                MessageBox.Show(
                    "Could not open Location Settings automatically. Please open Windows Settings -> Privacy & Security -> Location",
                    "Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
            }
        }

        public void UpdateLocationServicesState(bool isDisabled)
        {
            if (isDisposed) return;

            try
            {
                if (locationServicesMenuItem != null)
                {
                    locationServicesMenuItem.Visible = isDisabled;
                }
            }
            catch (Exception ex)
            {
                loggingService.LogError("Error updating location services state", ex);
            }
        }

        private void ContextMenu_Opening(object sender, System.ComponentModel.CancelEventArgs e)
        {
            UpdateKnownAPsMenu();
        }

        private void UpdateKnownAPsMenu()
        {
            try
            {
                knownAPsMenuItem.DropDownItems.Clear();

                // Add root level APs
                foreach (var bssid in knownAPManager.RootBssids)
                {
                    AddAPMenuItem(knownAPsMenuItem.DropDownItems, bssid, true);
                }

                if (knownAPManager.RootBssids.GetEnumerator().MoveNext())
                {
                    knownAPsMenuItem.DropDownItems.Add(new ToolStripSeparator());
                }

                // Add Unsorted submenu
                var unsortedMenu = new ToolStripMenuItem("Unsorted");
                foreach (var bssid in knownAPManager.UnsortedBssids)
                {
                    AddAPMenuItem(unsortedMenu.DropDownItems, bssid, false);
                }
                knownAPsMenuItem.DropDownItems.Add(unsortedMenu);
            }
            catch (Exception ex)
            {
                loggingService.LogError("Error updating Known APs menu", ex);
            }
        }

        private void AddAPMenuItem(ToolStripItemCollection collection, string bssid, bool isRoot)
        {
            var displayName = knownAPManager.GetDisplayName(bssid);
            var item = new ToolStripMenuItem(displayName);

            var renameItem = new ToolStripMenuItem("Rename");
            renameItem.Click += (s, e) => RenameAP(bssid);

            var moveItem = new ToolStripMenuItem(isRoot ? "Move to Unsorted" : "Move to Root");
            moveItem.Click += (s, e) => knownAPManager.SetAPRoot(bssid, !isRoot);

            var deleteItem = new ToolStripMenuItem("Delete");
            deleteItem.Click += (s, e) => DeleteAP(bssid);

            item.DropDownItems.AddRange(new ToolStripItem[]
            {
                renameItem,
                moveItem,
                new ToolStripSeparator(),
                deleteItem
            });

            collection.Add(item);
        }

        private void RenameAP(string bssid)
        {
            try
            {
                using (var dialog = new Form())
                {
                    dialog.Text = "Rename Access Point";
                    dialog.StartPosition = FormStartPosition.CenterScreen;
                    dialog.Width = 300;
                    dialog.Height = 150;
                    dialog.FormBorderStyle = FormBorderStyle.FixedDialog;
                    dialog.MaximizeBox = false;
                    dialog.MinimizeBox = false;

                    var label = new Label
                    {
                        Text = "Enter new name:",
                        Left = 10,
                        Top = 20,
                        Width = 270
                    };

                    var textBox = new TextBox
                    {
                        Text = knownAPManager.GetDisplayName(bssid),
                        Left = 10,
                        Top = 40,
                        Width = 270
                    };

                    var button = new Button
                    {
                        Text = "OK",
                        DialogResult = DialogResult.OK,
                        Left = 105,
                        Top = 70,
                        Width = 75
                    };

                    dialog.Controls.AddRange(new Control[] { label, textBox, button });
                    dialog.AcceptButton = button;

                    if (dialog.ShowDialog() == DialogResult.OK && !string.IsNullOrWhiteSpace(textBox.Text))
                    {
                        knownAPManager.RenameAP(bssid, textBox.Text);
                        if (bssid == currentBSSID)
                        {
                            UpdateCurrentAP(bssid);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                loggingService.LogError($"Error renaming AP {bssid}", ex);
                MessageBox.Show("Failed to rename access point.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void DeleteAP(string bssid)
        {
            try
            {
                var result = MessageBox.Show(
                    "Are you sure you want to delete this access point?",
                    "Confirm Delete",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question
                );

                if (result == DialogResult.Yes)
                {
                    knownAPManager.DeleteAP(bssid);
                }
            }
            catch (Exception ex)
            {
                loggingService.LogError($"Error deleting AP {bssid}", ex);
                MessageBox.Show("Failed to delete access point.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        public void UpdateCurrentAP(string bssid)
        {
            if (isDisposed) return;

            try
            {
                currentBSSID = bssid;
                if (!string.IsNullOrEmpty(bssid))
                {
                    if (!knownAPManager.GetDisplayName(bssid).Equals(bssid))
                    {
                        currentAPMenuItem.Text = $"AP: {knownAPManager.GetDisplayName(bssid)}";
                    }
                    else
                    {
                        knownAPManager.AddNewAP(bssid);
                        currentAPMenuItem.Text = $"AP: {bssid}";
                    }
                }
                else
                {
                    currentAPMenuItem.Text = "AP: Not Connected";
                }
            }
            catch (Exception ex)
            {
                loggingService.LogError("Error updating current AP", ex);
            }
        }

        private void StartupMenuItem_Click(object sender, EventArgs e)
        {
            try
            {
                bool newState = !startupMenuItem.Checked;
                if (startupManager.SetStartupEnabled(newState))
                {
                    startupMenuItem.Checked = newState;
                }
                else
                {
                    MessageBox.Show(
                        "Failed to update startup settings. Please check your permissions.",
                        "Error",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error
                    );
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Failed to update startup settings: {ex.Message}",
                    "Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
            }
        }

        private void ViewLogs_Click(object sender, EventArgs e)
        {
            try
            {
                if (!File.Exists(logPath))
                {
                    MessageBox.Show(
                        "Log file not found. The application may not have generated any logs yet.",
                        "Log File Not Found",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information
                    );
                    return;
                }

                Process.Start(new ProcessStartInfo
                {
                    FileName = logPath,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Failed to open log file: {ex.Message}",
                    "Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
            }
        }

        public void UpdateIcon(string displayText, string tooltipText, bool isError = false, bool isTransition = false, bool useBlackText = false)
        {
            if (isDisposed) return;

            currentDisplayText = displayText;
            currentTooltipText = tooltipText;
            currentErrorState = isError;
            currentTransitionState = isTransition;
            currentUseBlackText = useBlackText;

            Icon newIcon = null;
            try
            {
                if (isTransition)
                {
                    newIcon = useBlackText ?
                        iconGenerator.CreateTransitionIconWithBlackText(displayText) :
                        iconGenerator.CreateTransitionIcon(displayText);
                }
                else
                {
                    newIcon = iconGenerator.CreateNumberIcon(displayText, isError);
                }

                Icon oldIcon = trayIcon.Icon;
                trayIcon.Icon = newIcon;
                trayIcon.Text = tooltipText;

                oldIcon?.Dispose();
            }
            catch (Exception ex)
            {
                loggingService.LogError("Error updating icon", ex);
                newIcon?.Dispose();
                throw;
            }
        }

        public string GetAPDisplayName(string bssid)
        {
            if (isDisposed) return bssid;

            try
            {
                return knownAPManager.GetDisplayName(bssid);
            }
            catch (Exception ex)
            {
                loggingService.LogError("Error getting AP display name", ex);
                return bssid;
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!isDisposed && disposing)
            {
                try
                {
                    trayIcon.Visible = false;
                    contextMenu?.Dispose();
                    iconGenerator?.Dispose();
                    knownAPManager?.Dispose();
                }
                catch (Exception)
                {
                    // Swallow exceptions during cleanup
                }
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
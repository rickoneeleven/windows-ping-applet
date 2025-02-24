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
    /// <summary>
    /// Manages the context menu for the system tray icon
    /// </summary>
    public class MenuManager : IDisposable
    {
        private readonly ContextMenuStrip contextMenu;
        private readonly BuildInfoProvider buildInfoProvider;
        private readonly ILoggingService loggingService;
        private readonly StartupManager startupManager;
        private readonly KnownAPManager knownAPManager;
        private readonly string logPath;
        private bool isDisposed;

        // Menu items that need to be accessed
        private ToolStripMenuItem locationServicesMenuItem;
        private ToolStripMenuItem startupMenuItem;
        private ToolStripMenuItem notificationsMenuItem;
        private ToolStripMenuItem currentAPMenuItem;
        private ToolStripMenuItem knownAPsMenuItem;

        public event EventHandler QuitRequested;
        public bool NotificationsEnabled => notificationsMenuItem?.Checked ?? true;

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
        }

        public ContextMenuStrip GetContextMenu() => contextMenu;

        private void InitializeContextMenu()
        {
            try
            {
                // Add location services warning (initially hidden)
                InitializeLocationServicesMenuItem();
                contextMenu.Items.Add(new ToolStripSeparator());

                // Add current AP display
                InitializeCurrentAPMenuItem();

                // Add Known APs menu
                InitializeKnownAPsMenuItem();

                contextMenu.Items.Add(new ToolStripSeparator());

                // Version info
                AddVersionMenuItem();

                contextMenu.Items.Add(new ToolStripSeparator());

                // Add View Logs option
                AddViewLogsMenuItem();

                contextMenu.Items.Add(new ToolStripSeparator());

                // Add Start on Login option
                InitializeStartupMenuItem();

                // Add Notification Popups option
                InitializeNotificationsMenuItem();

                contextMenu.Items.Add(new ToolStripSeparator());

                AddQuitMenuItem();

                // Update menu on opening
                contextMenu.Opening += ContextMenu_Opening;
            }
            catch (Exception ex)
            {
                loggingService.LogError("Error initializing context menu", ex);
                throw;
            }
        }

        private void InitializeLocationServicesMenuItem()
        {
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
        }

        private void InitializeCurrentAPMenuItem()
        {
            currentAPMenuItem = new ToolStripMenuItem("AP: not on wireless")
            {
                Enabled = false
            };
            contextMenu.Items.Add(currentAPMenuItem);
        }

        private void InitializeKnownAPsMenuItem()
        {
            knownAPsMenuItem = new ToolStripMenuItem("Known APs");
            contextMenu.Items.Add(knownAPsMenuItem);
        }

        private void AddVersionMenuItem()
        {
            var versionItem = new ToolStripMenuItem($"Version {buildInfoProvider.VersionString}")
            {
                Enabled = false
            };
            contextMenu.Items.Add(versionItem);
        }

        private void AddViewLogsMenuItem()
        {
            var viewLogsItem = new ToolStripMenuItem("View Log");
            viewLogsItem.Click += ViewLogs_Click;
            contextMenu.Items.Add(viewLogsItem);
        }

        private void InitializeStartupMenuItem()
        {
            startupMenuItem = new ToolStripMenuItem("Start on Login");
            startupMenuItem.Click += StartupMenuItem_Click;
            startupMenuItem.Checked = startupManager.IsStartupEnabled();
            contextMenu.Items.Add(startupMenuItem);
        }

        private void InitializeNotificationsMenuItem()
        {
            notificationsMenuItem = new ToolStripMenuItem("Notification Popups");
            notificationsMenuItem.Click += NotificationsMenuItem_Click;
            notificationsMenuItem.Checked = knownAPManager.GetNotificationsEnabled();
            contextMenu.Items.Add(notificationsMenuItem);
        }

        private void AddQuitMenuItem()
        {
            var quitItem = new ToolStripMenuItem("Quit");
            quitItem.Click += (s, e) => QuitRequested?.Invoke(this, EventArgs.Empty);
            contextMenu.Items.Add(quitItem);
        }

        private void NotificationsMenuItem_Click(object sender, EventArgs e)
        {
            try
            {
                notificationsMenuItem.Checked = !notificationsMenuItem.Checked;
                knownAPManager.SetNotificationsEnabled(notificationsMenuItem.Checked);
            }
            catch (Exception ex)
            {
                loggingService.LogError("Error toggling notifications", ex);
                MessageBox.Show(
                    "Failed to save notification preference. Please check your permissions.",
                    "Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
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

        public void UpdateCurrentAP(string displayName)
        {
            if (isDisposed) return;

            try
            {
                if (currentAPMenuItem != null)
                {
                    currentAPMenuItem.Text = string.IsNullOrEmpty(displayName)
                        ? "AP: Not Connected"
                        : $"AP: {displayName}";
                }
            }
            catch (Exception ex)
            {
                loggingService.LogError("Error updating current AP display", ex);
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

                // Get all known BSSIDs
                var allBssids = knownAPManager.RootBssids.Concat(knownAPManager.UnsortedBssids).Distinct();

                // Create lists for named and unnamed APs
                var namedAPs = new List<(string bssid, string name)>();
                var unnamedAPs = new List<string>();

                // Classify APs based on whether they have a custom name
                foreach (var bssid in allBssids)
                {
                    // Get the raw name without details to check for customization
                    var rawName = knownAPManager.GetDisplayName(bssid, includeDetails: false);
                    var displayName = knownAPManager.GetDisplayName(bssid); // Full display name with details

                    // If the raw name is different from the BSSID, it has a custom name
                    if (rawName != bssid)
                    {
                        namedAPs.Add((bssid, displayName));
                    }
                    else
                    {
                        unnamedAPs.Add(bssid);
                    }
                }

                // Sort the named APs alphabetically (numbers first, then characters)
                namedAPs = namedAPs
                    .OrderBy(ap => !char.IsDigit(ap.name.FirstOrDefault())) // Numbers first
                    .ThenBy(ap => ap.name) // Then alphabetically
                    .ToList();

                // Add the named APs to the root menu
                foreach (var (bssid, _) in namedAPs)
                {
                    AddAPMenuItem(knownAPsMenuItem.DropDownItems, bssid);
                }

                // Add a separator if there are both named and unnamed APs
                if (namedAPs.Any() && unnamedAPs.Any())
                {
                    knownAPsMenuItem.DropDownItems.Add(new ToolStripSeparator());
                }

                // Add Unsorted submenu
                if (unnamedAPs.Any())
                {
                    var unsortedMenu = new ToolStripMenuItem("Unsorted");
                    foreach (var bssid in unnamedAPs)
                    {
                        AddAPMenuItem(unsortedMenu.DropDownItems, bssid);
                    }
                    knownAPsMenuItem.DropDownItems.Add(unsortedMenu);
                }
            }
            catch (Exception ex)
            {
                loggingService.LogError("Error updating Known APs menu", ex);
            }
        }

        private void AddAPMenuItem(ToolStripItemCollection collection, string bssid)
        {
            var displayName = knownAPManager.GetDisplayName(bssid);
            var item = new ToolStripMenuItem(displayName);

            var renameItem = new ToolStripMenuItem("Rename");
            renameItem.Click += (s, e) => RenameAP(bssid);

            var deleteItem = new ToolStripMenuItem("Delete");
            deleteItem.Click += (s, e) => DeleteAP(bssid);

            item.DropDownItems.AddRange(new ToolStripItem[]
            {
                renameItem,
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
                        UpdateCurrentAP(textBox.Text);
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
                if (!System.IO.File.Exists(logPath))
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

        protected virtual void Dispose(bool disposing)
        {
            if (!isDisposed && disposing)
            {
                contextMenu?.Dispose();
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
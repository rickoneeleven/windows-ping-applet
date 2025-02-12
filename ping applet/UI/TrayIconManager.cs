using System;
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

        // UI state tracking
        private string currentDisplayText;
        private string currentTooltipText;
        private bool currentErrorState;
        private bool currentTransitionState;
        private bool currentUseBlackText;
        private string currentBSSID;

        public event EventHandler QuitRequested;
        public bool IsDisposed => isDisposed;

        public TrayIconManager(BuildInfoProvider buildInfoProvider, ILoggingService loggingService)
        {
            this.buildInfoProvider = buildInfoProvider ?? throw new ArgumentNullException(nameof(buildInfoProvider));
            this.loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
            this.startupManager = new StartupManager();
            this.knownAPManager = new KnownAPManager(loggingService);
            iconGenerator = new IconGenerator();

            // Initialize log path
            logPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PingApplet",
                "ping.log"
            );

            // Initialize context menu with enhanced status info
            contextMenu = new ContextMenuStrip();
            InitializeContextMenu();

            // Initialize tray icon with transition state support
            trayIcon = new NotifyIcon
            {
                Icon = iconGenerator.CreateNumberIcon("--"),
                Visible = true,
                ContextMenuStrip = contextMenu,
                Text = "Initializing..."
            };
        }

        private void InitializeContextMenu()
        {
            try
            {
                // Add current AP display
                currentAPMenuItem = new ToolStripMenuItem("AP: Not Connected")
                {
                    Enabled = false
                };
                contextMenu.Items.Add(currentAPMenuItem);

                // Add Known APs menu
                knownAPsMenuItem = new ToolStripMenuItem("Known APs");
                contextMenu.Items.Add(knownAPsMenuItem);

                contextMenu.Items.Add(new ToolStripSeparator());

                // Add enhanced status item that will show version and connection info
                var statusItem = new ToolStripMenuItem("Status")
                {
                    Enabled = false
                };
                contextMenu.Items.Add(statusItem);
                contextMenu.Items.Add(new ToolStripSeparator());

                // Add Network State section
                var networkStateItem = new ToolStripMenuItem("Network State")
                {
                    Enabled = false
                };
                contextMenu.Items.Add(networkStateItem);
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

                // Update status on menu opening
                contextMenu.Opening += ContextMenu_Opening;
            }
            catch (Exception ex)
            {
                loggingService.LogError("Error initializing context menu", ex);
                throw;
            }
        }

        private void ContextMenu_Opening(object sender, System.ComponentModel.CancelEventArgs e)
        {
            UpdateMenuStatus();
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

            // Create submenu for AP options
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

        private void UpdateMenuStatus()
        {
            if (isDisposed) return;

            try
            {
                if (contextMenu?.Items.Count > 0)
                {
                    // Update version info
                    var statusItem = contextMenu.Items[3] as ToolStripMenuItem;
                    if (statusItem != null)
                    {
                        statusItem.Text = $"Version {buildInfoProvider.VersionString}";
                        statusItem.DropDownItems.Clear();
                        var buildItem = new ToolStripMenuItem($"Built on {buildInfoProvider.BuildTimestamp}")
                        {
                            Enabled = false
                        };
                        statusItem.DropDownItems.Add(buildItem);
                    }

                    // Update network state info
                    var networkStateItem = contextMenu.Items[5] as ToolStripMenuItem;
                    if (networkStateItem != null)
                    {
                        var stateText = currentTransitionState ? "AP Change in Progress" :
                                      currentErrorState ? "Error State" : "Normal Operation";

                        networkStateItem.Text = $"Status: {stateText}";

                        if (!string.IsNullOrEmpty(currentTooltipText))
                        {
                            networkStateItem.DropDownItems.Clear();
                            var detailItem = new ToolStripMenuItem(currentTooltipText)
                            {
                                Enabled = false
                            };
                            networkStateItem.DropDownItems.Add(detailItem);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                loggingService.LogError("Error updating menu status", ex);
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
                        // AP is already known
                        currentAPMenuItem.Text = $"AP: {knownAPManager.GetDisplayName(bssid)}";
                    }
                    else
                    {
                        // New AP, add to unsorted
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

            // Track state changes
            currentDisplayText = displayText;
            currentTooltipText = tooltipText;
            currentErrorState = isError;
            currentTransitionState = isTransition;
            currentUseBlackText = useBlackText;

            Icon newIcon = null;
            try
            {
                // Create icon with appropriate color based on state
                if (isTransition)
                {
                    // Orange for AP transition, with black or white text based on parameter
                    newIcon = useBlackText ?
                        iconGenerator.CreateTransitionIconWithBlackText(displayText) :
                        iconGenerator.CreateTransitionIcon(displayText);
                }
                else
                {
                    // Red for errors, black for normal, always with white text
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

        public void UpdateStatus()
        {
            if (isDisposed) return;
            UpdateMenuStatus();
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!isDisposed && disposing)
            {
                try
                {
                    // First make tray icon invisible
                    trayIcon.Visible = false;
                    // Then dispose components in reverse order of dependency
                    contextMenu?.Dispose();
                    iconGenerator?.Dispose();
                    // Dispose KnownAPManager last since it might try to log during disposal
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
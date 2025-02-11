using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using ping_applet.Utils;

namespace ping_applet.UI
{
    public class TrayIconManager : IDisposable
    {
        private readonly NotifyIcon trayIcon;
        private readonly ContextMenuStrip contextMenu;
        private readonly IconGenerator iconGenerator;
        private readonly BuildInfoProvider buildInfoProvider;
        private bool isDisposed;
        private readonly string logPath;

        // UI state tracking
        private string currentDisplayText;
        private string currentTooltipText;
        private bool currentErrorState;
        private bool currentTransitionState;
        private bool currentUseBlackText;

        public event EventHandler QuitRequested;
        public bool IsDisposed => isDisposed;

        public TrayIconManager(BuildInfoProvider buildInfoProvider)
        {
            this.buildInfoProvider = buildInfoProvider ?? throw new ArgumentNullException(nameof(buildInfoProvider));
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

                var quitItem = new ToolStripMenuItem("Quit");
                quitItem.Click += (s, e) => QuitRequested?.Invoke(this, EventArgs.Empty);
                contextMenu.Items.Add(quitItem);

                // Update status on menu opening
                contextMenu.Opening += (s, e) => UpdateMenuStatus();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error initializing context menu: {ex.Message}");
                throw;
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
                    if (contextMenu.Items[0] is ToolStripMenuItem statusItem)
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
                    if (contextMenu.Items[2] is ToolStripMenuItem networkStateItem)
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
                Debug.WriteLine($"Error updating menu status: {ex.Message}");
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
                Debug.WriteLine($"Error updating icon: {ex.Message}");
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
                trayIcon.Visible = false;
                trayIcon.Dispose();
                contextMenu.Dispose();
                iconGenerator.Dispose();
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
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

            // Initialize context menu
            contextMenu = new ContextMenuStrip();
            InitializeContextMenu();

            // Initialize tray icon
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
            // Add status item that will show version info
            var statusItem = new ToolStripMenuItem("Status")
            {
                Enabled = false
            };
            contextMenu.Items.Add(statusItem);
            contextMenu.Items.Add(new ToolStripSeparator());

            // Add View Logs option
            var viewLogsItem = new ToolStripMenuItem("View Log");
            viewLogsItem.Click += ViewLogs_Click;
            contextMenu.Items.Add(viewLogsItem);

            contextMenu.Items.Add(new ToolStripSeparator());

            var quitItem = new ToolStripMenuItem("Quit");
            quitItem.Click += (s, e) => QuitRequested?.Invoke(this, EventArgs.Empty);
            contextMenu.Items.Add(quitItem);

            contextMenu.Opening += (s, e) => UpdateStatus();
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

                // Open log file with default text editor
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

        public void UpdateIcon(string displayText, string tooltipText, bool isError = false)
        {
            if (isDisposed) return;

            Icon newIcon = null;
            try
            {
                newIcon = iconGenerator.CreateNumberIcon(displayText, isError);
                Icon oldIcon = trayIcon.Icon;

                trayIcon.Icon = newIcon;
                trayIcon.Text = tooltipText;

                if (oldIcon != null)
                {
                    oldIcon.Dispose();
                }
            }
            catch
            {
                newIcon?.Dispose();
                throw;
            }
        }

        public void UpdateStatus()
        {
            if (isDisposed) return;

            if (contextMenu?.Items.Count > 0 && contextMenu.Items[0] is ToolStripMenuItem statusItem)
            {
                statusItem.Text = $"Version {buildInfoProvider.VersionString}";

                statusItem.DropDownItems.Clear();
                var buildItem = new ToolStripMenuItem($"Built on {buildInfoProvider.BuildTimestamp}") { Enabled = false };
                statusItem.DropDownItems.Add(buildItem);

                // No tooltip needed as it causes flickering with frequent updates
            }
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
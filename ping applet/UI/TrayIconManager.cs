using System;
using System.Drawing;
using System.Windows.Forms;

namespace ping_applet.UI
{
    /// <summary>
    /// Manages the system tray icon and its context menu
    /// </summary>
    public class TrayIconManager : IDisposable
    {
        private readonly NotifyIcon trayIcon;
        private readonly ContextMenuStrip contextMenu;
        private readonly IconGenerator iconGenerator;
        private readonly string buildTimestamp;
        private bool isDisposed;

        /// <summary>
        /// Event raised when the user requests to quit the application
        /// </summary>
        public event EventHandler QuitRequested;

        /// <summary>
        /// Gets whether this instance has been disposed
        /// </summary>
        public bool IsDisposed => isDisposed;

        public TrayIconManager(string buildTimestamp)
        {
            this.buildTimestamp = buildTimestamp ?? "Unknown";
            iconGenerator = new IconGenerator();

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
            // Add status item that will show build info and gateway details
            var statusItem = new ToolStripMenuItem("Status")
            {
                Enabled = false
            };
            contextMenu.Items.Add(statusItem);
            contextMenu.Items.Add(new ToolStripSeparator());
            var quitItem = new ToolStripMenuItem("Quit");
            quitItem.Click += (s, e) => QuitRequested?.Invoke(this, EventArgs.Empty);
            contextMenu.Items.Add(quitItem);

            contextMenu.Opening += (s, e) => UpdateStatus();
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

        /// <summary>
        /// Updates the status information shown in the context menu
        /// </summary>
        public void UpdateStatus()
        {
            if (isDisposed) return;

            if (contextMenu?.Items.Count > 0 && contextMenu.Items[0] is ToolStripMenuItem statusItem)
            {
                string buildInfo = $"Build: {buildTimestamp}";
                statusItem.Text = buildInfo;

                statusItem.DropDownItems.Clear();
                // Status items are managed by context menu opening event
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
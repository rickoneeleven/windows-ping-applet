using System;
using System.Drawing;
using System.Windows.Forms;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
using System.Linq;
using System.IO;
using System.Reflection;
using ping_applet.Core.Interfaces;
using ping_applet.Services;

namespace ping_applet
{
    public partial class Form1 : Form
    {
        private readonly INetworkMonitor networkMonitor;
        private readonly IPingService pingService;
        private readonly ILoggingService loggingService;
        private NotifyIcon trayIcon;
        private ContextMenuStrip contextMenu;
        private volatile bool isDisposing = false;

        private const int PING_INTERVAL = 1000; // 1 second
        private const int PING_TIMEOUT = 1000;  // 1 second timeout

        private static readonly string BuildTimestamp = GetBuildDate();
        private static readonly string LogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PingApplet",
            "ping.log"
        );

        #region Initialization and Setup

        public Form1()
        {
            try
            {
                InitializeComponent();
                loggingService = new LoggingService(LogPath);
                loggingService.LogInfo("Application starting up");
                networkMonitor = new NetworkMonitor();
                pingService = new PingService();
                InitializeCustomComponents();
                this.FormClosing += Form1_FormClosing;
                _ = InitializeAsync(); // Fire and forget, but log any errors
            }
            catch (Exception ex)
            {
                HandleInitializationError(ex);
            }
        }

        private static string GetBuildDate()
        {
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                var attribute = assembly.GetCustomAttributes(typeof(AssemblyMetadataAttribute), false)
                    .Cast<AssemblyMetadataAttribute>()
                    .FirstOrDefault(attr => attr.Key == "BuildTimestamp");

                return attribute?.Value ?? "Unknown";
            }
            catch
            {
                return "Unknown";
            }
        }

        private void InitializeCustomComponents()
        {
            try
            {
                // Create context menu with build info and status
                contextMenu = new ContextMenuStrip();

                // Add status item that will show build info and gateway details
                var statusItem = new ToolStripMenuItem("Status")
                {
                    Enabled = false
                };
                contextMenu.Items.Add(statusItem);

                contextMenu.Items.Add(new ToolStripSeparator());
                contextMenu.Items.Add("Quit", null, OnQuit);

                // Initialize tray icon
                trayIcon = new NotifyIcon
                {
                    Icon = CreateNumberIcon("--"),
                    Visible = true,
                    ContextMenuStrip = contextMenu,
                    Text = "Initializing..."
                };

                contextMenu.Opening += (s, e) => UpdateContextMenuStatus();

                // Configure form
                ShowInTaskbar = false;
                WindowState = FormWindowState.Minimized;
                FormBorderStyle = FormBorderStyle.None;

                // Wire up ping service events
                pingService.PingCompleted += PingService_PingCompleted;
                pingService.PingError += PingService_PingError;
            }
            catch (Exception ex)
            {
                throw new ApplicationException("Failed to initialize components", ex);
            }
        }

        private async Task InitializeAsync()
        {
            try
            {
                await networkMonitor.InitializeAsync();
                networkMonitor.GatewayChanged += NetworkMonitor_GatewayChanged;
                networkMonitor.NetworkAvailabilityChanged += NetworkMonitor_NetworkAvailabilityChanged;
                pingService.StartPingTimer(PING_INTERVAL);

                // Initial ping to current gateway
                if (!string.IsNullOrEmpty(networkMonitor.CurrentGateway))
                {
                    await pingService.SendPingAsync(networkMonitor.CurrentGateway, PING_TIMEOUT);
                }
            }
            catch (Exception ex)
            {
                loggingService.LogError("Async initialization error", ex);
                ShowErrorState("INIT!");
            }
        }

        private void HandleInitializationError(Exception ex)
        {
            try
            {
                loggingService?.LogError("Initialization error", ex);
                MessageBox.Show($"Failed to initialize the application: {ex.Message}",
                    "Initialization Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                isDisposing = true;
                Application.Exit();
            }
        }

        #endregion

        #region Network Event Handlers

        private async void NetworkMonitor_GatewayChanged(object sender, string newGateway)
        {
            if (string.IsNullOrEmpty(newGateway))
            {
                ShowErrorState("GW?");
                loggingService.LogInfo("Gateway became unavailable");
            }
            else
            {
                loggingService.LogInfo($"Gateway changed to: {newGateway}");
                await pingService.SendPingAsync(newGateway, PING_TIMEOUT);
            }
        }

        private void NetworkMonitor_NetworkAvailabilityChanged(object sender, bool isAvailable)
        {
            if (!isAvailable)
            {
                loggingService.LogInfo("Network became unavailable");
                ShowErrorState("OFF");
            }
        }

        #endregion

        #region Ping Event Handlers

        private void PingService_PingCompleted(object sender, PingReply reply)
        {
            if (isDisposing || reply == null) return;

            try
            {
                if (reply.Status == IPStatus.Success)
                {
                    string displayText = reply.RoundtripTime.ToString();
                    string tooltipText = $"{networkMonitor.CurrentGateway}: {reply.RoundtripTime}ms";
                    UpdateTrayIcon(displayText, tooltipText);
                    loggingService.LogInfo($"Ping successful - Gateway: {networkMonitor.CurrentGateway}, Time: {reply.RoundtripTime}ms");
                }
                else
                {
                    string tooltipText = $"{networkMonitor.CurrentGateway}: Failed";
                    UpdateTrayIcon("X", tooltipText, true);
                    loggingService.LogInfo($"Ping failed - Gateway: {networkMonitor.CurrentGateway}, Status: {reply.Status}");
                }
            }
            catch (Exception ex)
            {
                loggingService.LogError("Error handling ping reply", ex);
            }
        }

        private void PingService_PingError(object sender, Exception ex)
        {
            if (isDisposing) return;
            loggingService.LogError("Ping error", ex);
            ShowErrorState("!");
        }

        #endregion

        #region UI Updates

        private void UpdateContextMenuStatus()
        {
            try
            {
                if (contextMenu?.Items.Count > 0 && contextMenu.Items[0] is ToolStripMenuItem statusItem)
                {
                    string buildInfo = $"Build: {BuildTimestamp}";
                    statusItem.Text = buildInfo;

                    statusItem.DropDownItems.Clear();
                    statusItem.DropDownItems.Add(new ToolStripMenuItem($"Gateway: {networkMonitor.CurrentGateway}") { Enabled = false });
                }
            }
            catch (Exception ex)
            {
                loggingService.LogError("Failed to update context menu", ex);
            }
        }

        private void UpdateTrayIcon(string displayText, string tooltipText, bool isError = false)
        {
            if (isDisposing || trayIcon == null) return;

            Icon newIcon = null;
            try
            {
                newIcon = CreateNumberIcon(displayText, isError);
                Icon oldIcon = trayIcon.Icon;

                trayIcon.Icon = newIcon;
                trayIcon.Text = tooltipText;

                UpdateContextMenuStatus();

                if (oldIcon != null)
                {
                    oldIcon.Dispose();
                }
            }
            catch (Exception ex)
            {
                loggingService.LogError("Failed to update tray icon", ex);
                newIcon?.Dispose();
            }
        }

        private Icon CreateNumberIcon(string number, bool isError = false)
        {
            IntPtr hIcon = IntPtr.Zero;
            try
            {
                using (var bitmap = new Bitmap(16, 16))
                using (var g = Graphics.FromImage(bitmap))
                {
                    g.Clear(isError ? Color.Red : Color.Black);

                    float fontSize = number.Length switch
                    {
                        1 => 10f,
                        2 => 8f,
                        3 => 6f,
                        _ => 5f
                    };

                    using (var currentFont = new Font("Arial", fontSize, FontStyle.Bold))
                    using (var brush = new SolidBrush(Color.White))
                    using (var sf = new StringFormat
                    {
                        Alignment = StringAlignment.Center,
                        LineAlignment = StringAlignment.Center
                    })
                    {
                        g.DrawString(number, currentFont, brush, new RectangleF(0, 0, 16, 16), sf);
                    }

                    hIcon = bitmap.GetHicon();
                    Icon icon = Icon.FromHandle(hIcon);

                    // Create a new icon that doesn't depend on the handle
                    using (Icon tmpIcon = icon)
                    {
                        return (Icon)tmpIcon.Clone();
                    }
                }
            }
            catch (Exception ex)
            {
                loggingService.LogError("Icon creation error", ex);
                return (Icon)SystemIcons.Error.Clone();
            }
            finally
            {
                if (hIcon != IntPtr.Zero)
                {
                    try
                    {
                        DestroyIcon(hIcon);
                    }
                    catch (Exception ex)
                    {
                        loggingService.LogError("Failed to destroy icon handle", ex);
                    }
                }
            }
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        public void ShowErrorState(string errorText)
        {
            try
            {
                string tooltipText = $"Error: {errorText}";
                UpdateTrayIcon(errorText, tooltipText, true);
            }
            catch (Exception ex)
            {
                loggingService.LogError("Error state display failed", ex);
            }
        }

        #endregion

        #region Cleanup and Event Handlers

        private void OnQuit(object sender, EventArgs e)
        {
            isDisposing = true;
            Application.Exit();
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            try
            {
                isDisposing = true;
                if (trayIcon != null)
                {
                    trayIcon.Visible = false;
                    trayIcon.Dispose();
                    trayIcon = null;
                }
                if (contextMenu != null)
                {
                    contextMenu.Dispose();
                    contextMenu = null;
                }
                pingService?.Dispose();
                networkMonitor?.Dispose();
                loggingService?.Dispose();
            }
            catch (Exception ex)
            {
                // At this point, logging service might be disposed, so we can't use it
                System.Diagnostics.Debug.WriteLine($"Cleanup error: {ex.Message}");
            }
        }

        #endregion
    }
}
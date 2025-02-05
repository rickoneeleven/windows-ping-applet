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
        private NotifyIcon trayIcon;
        private ContextMenuStrip contextMenu;
        private System.Windows.Forms.Timer pingTimer;
        private volatile bool isDisposing = false;
        private volatile bool isPinging = false;
        private readonly object pingLock = new object();

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
                networkMonitor = new NetworkMonitor();
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
                StartPingTimer();
            }
            catch (Exception ex)
            {
                LogToFile($"Async initialization error: {ex.Message}");
                ShowErrorState("INIT!");
            }
        }

        private void HandleInitializationError(Exception ex)
        {
            LogToFile($"Initialization error: {ex.Message}");
            MessageBox.Show($"Failed to initialize the application: {ex.Message}",
                "Initialization Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            isDisposing = true;
            Application.Exit();
        }

        #endregion

        #region Network Event Handlers

        private void NetworkMonitor_GatewayChanged(object sender, string newGateway)
        {
            if (string.IsNullOrEmpty(newGateway))
            {
                ShowErrorState("GW?");
                LogToFile("Gateway became unavailable");
            }
            else
            {
                LogToFile($"Gateway changed to: {newGateway}");
            }
        }

        private void NetworkMonitor_NetworkAvailabilityChanged(object sender, bool isAvailable)
        {
            if (!isAvailable)
            {
                LogToFile("Network became unavailable");
                ShowErrorState("OFF");
            }
        }

        #endregion

        #region Ping Operations

        private void StartPingTimer()
        {
            try
            {
                pingTimer = new System.Windows.Forms.Timer
                {
                    Interval = PING_INTERVAL
                };
                pingTimer.Tick += async (sender, e) => await PingGateway();
                pingTimer.Start();
            }
            catch (Exception ex)
            {
                LogToFile($"Timer setup failed: {ex.Message}");
                ShowErrorState("TMR!");
            }
        }

        private async Task PingGateway()
        {
            if (isDisposing || trayIcon == null) return;
            if (isPinging) return;

            try
            {
                lock (pingLock)
                {
                    if (isPinging) return;
                    isPinging = true;
                }

                LogToFile("Starting ping operation...");

                string currentGateway = networkMonitor.CurrentGateway;
                if (string.IsNullOrEmpty(currentGateway))
                {
                    ShowErrorState("GW!");
                    return;
                }

                using (var ping = new Ping())
                {
                    var options = new PingOptions
                    {
                        DontFragment = false,
                        Ttl = 128
                    };

                    byte[] buffer = new byte[32];

                    try
                    {
                        LogToFile($"Sending ping to {currentGateway}...");
                        PingReply reply = await ping.SendPingAsync(currentGateway, PING_TIMEOUT, buffer, options);

                        if (!isDisposing && trayIcon != null)
                        {
                            LogToFile($"Ping completed with status: {reply.Status}");
                            UpdateIconBasedOnReply(reply);
                        }
                    }
                    catch (PingException pex)
                    {
                        LogToFile($"Ping exception: {pex.Message}");
                        ShowErrorState("!");
                    }
                }
            }
            catch (Exception ex)
            {
                LogToFile($"General ping error: {ex.Message}");
                ShowErrorState("!");
            }
            finally
            {
                isPinging = false;
            }
        }

        #endregion

        #region UI Updates

        private void UpdateIconBasedOnReply(PingReply reply)
        {
            if (reply == null) return;

            try
            {
                if (reply.Status == IPStatus.Success)
                {
                    string displayText = reply.RoundtripTime.ToString();
                    string tooltipText = $"{networkMonitor.CurrentGateway}: {reply.RoundtripTime}ms";
                    UpdateTrayIcon(displayText, tooltipText);
                }
                else
                {
                    string tooltipText = $"{networkMonitor.CurrentGateway}: Failed";
                    UpdateTrayIcon("X", tooltipText, true);
                }
            }
            catch (Exception ex)
            {
                LogToFile($"Icon update error: {ex.Message}");
            }
        }

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
                LogToFile($"Failed to update context menu: {ex.Message}");
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
                LogToFile($"Failed to update tray icon: {ex.Message}");
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
                LogToFile($"Icon creation error: {ex.Message}");
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
                        LogToFile($"Failed to destroy icon handle: {ex.Message}");
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
                LogToFile($"Error state display failed: {ex.Message}");
            }
        }

        #endregion

        #region Logging

        private static void LogToFile(string message)
        {
            try
            {
                string directory = Path.GetDirectoryName(LogPath);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.AppendAllText(LogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}: {message}\n");
            }
            catch { /* Ignore logging errors */ }
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
                if (pingTimer != null)
                {
                    pingTimer.Stop();
                    pingTimer.Dispose();
                    pingTimer = null;
                }
                networkMonitor?.Dispose();
            }
            catch (Exception ex)
            {
                LogToFile($"Cleanup error: {ex.Message}");
            }
        }

        #endregion
    }
}
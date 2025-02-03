using System;
using System.Drawing;
using System.Windows.Forms;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
using System.Linq;
using System.IO;
using System.Reflection;

namespace ping_applet
{
    public partial class Form1 : Form
    {
        private NotifyIcon trayIcon;
        private ContextMenuStrip contextMenu;
        private System.Windows.Forms.Timer pingTimer;
        private string gatewayIP;
        private bool isDisposing = false;
        private const int PING_INTERVAL = 1000; // 1 second
        private const int PING_TIMEOUT = 1000;  // 1 second timeout

        // Keep the static field for the places in code that reference it
        private static readonly string BuildTimestamp = GetBuildDate().ToString("dd/MM/yy HH:mm");

        private static DateTime GetBuildDate()
        {
            try
            {
                // Get the assembly file's last write time
                string filePath = Assembly.GetExecutingAssembly().Location;
                return File.GetLastWriteTime(filePath);
            }
            catch
            {
                // If we can't get the build date for some reason, return current time
                return DateTime.Now;
            }
        }

        public Form1()
        {
            try
            {
                InitializeComponent();
                InitializeCustomComponents();
                this.FormClosing += Form1_FormClosing;
                DetectGateway();
                StartPingTimer();
            }
            catch (Exception ex)
            {
                HandleInitializationError(ex);
            }
        }

        private void HandleInitializationError(Exception ex)
        {
            MessageBox.Show($"Failed to initialize the application: {ex.Message}",
                "Initialization Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            isDisposing = true;
            Application.Exit();
        }

        private void InitializeCustomComponents()
        {
            try
            {
                // Create context menu
                contextMenu = new ContextMenuStrip();
                contextMenu.Items.Add("Quit", null, OnQuit);

                // Initialize tray icon
                trayIcon = new NotifyIcon
                {
                    Icon = CreateNumberIcon("--"),
                    Visible = true,
                    ContextMenuStrip = contextMenu,
                    Text = $"Built: {BuildTimestamp}\nInitializing..."
                };

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

        private string GetDefaultGateway()
        {
            try
            {
                // Get all network interfaces
                NetworkInterface[] interfaces = NetworkInterface.GetAllNetworkInterfaces();

                // Filter for active interfaces that support IPv4
                var activeInterface = interfaces.FirstOrDefault(ni =>
                    ni.OperationalStatus == OperationalStatus.Up &&
                    ni.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                    ni.Supports(NetworkInterfaceComponent.IPv4) &&
                    ni.GetIPProperties().GatewayAddresses.Count > 0);

                if (activeInterface != null)
                {
                    // Get the first IPv4 gateway address
                    var gateway = activeInterface.GetIPProperties()
                        .GatewayAddresses
                        .FirstOrDefault()?.Address.ToString();

                    if (!string.IsNullOrEmpty(gateway))
                    {
                        return gateway;
                    }
                }

                // Only show message box and exit if this is the initial gateway detection
                if (string.IsNullOrEmpty(gatewayIP))
                {
                    ShowErrorState("GW?");
                }
                return null;
            }
            catch (Exception ex)
            {
                ShowErrorState("ERR");
                throw new ApplicationException("Failed to detect gateway", ex);
            }
        }

        public void ShowErrorState(string errorText)
        {
            try
            {
                if (!isDisposing && trayIcon != null)
                {
                    Icon newIcon = CreateNumberIcon(errorText, true);
                    Icon oldIcon = trayIcon.Icon;
                    trayIcon.Icon = newIcon;
                    if (oldIcon != null && oldIcon != newIcon)
                    {
                        oldIcon.Dispose();
                    }
                    trayIcon.Text = $"Built: {BuildTimestamp}\nError: {errorText}";
                }
            }
            catch
            {
                // Ignore any errors during error handling
            }
        }

        private void DetectGateway()
        {
            try
            {
                string detectedGateway = GetDefaultGateway();
                if (!string.IsNullOrEmpty(detectedGateway))
                {
                    gatewayIP = detectedGateway;
                    SetupNetworkChangeDetection();
                }
            }
            catch (Exception)
            {
                ShowErrorState("GW!");
            }
        }

        private void SetupNetworkChangeDetection()
        {
            try
            {
                NetworkChange.NetworkAddressChanged += async (s, e) =>
                {
                    try
                    {
                        string newGateway = GetDefaultGateway();
                        if (!string.IsNullOrEmpty(newGateway))
                        {
                            gatewayIP = newGateway;
                        }
                        await PingGateway(); // Force immediate ping attempt
                    }
                    catch
                    {
                        ShowErrorState("NET!");
                    }
                };

                NetworkChange.NetworkAvailabilityChanged += (s, e) =>
                {
                    if (!e.IsAvailable)
                    {
                        ShowErrorState("OFF");
                    }
                };
            }
            catch (Exception)
            {
                ShowErrorState("NET!");
            }
        }

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
            catch (Exception)
            {
                ShowErrorState("TMR!");
            }
        }

        private Icon CreateNumberIcon(string number, bool isError = false)
        {
            try
            {
                using (Bitmap bitmap = new Bitmap(16, 16))
                using (Graphics g = Graphics.FromImage(bitmap))
                {
                    g.Clear(isError ? Color.Red : Color.Black);

                    // Determine font size based on text length
                    float fontSize = number.Length switch
                    {
                        1 => 10f,
                        2 => 8f,
                        3 => 6f,
                        _ => 5f
                    };

                    using (Font currentFont = new Font("Arial", fontSize, FontStyle.Bold))
                    using (Brush brush = new SolidBrush(Color.White))
                    {
                        // Center text
                        StringFormat sf = new StringFormat
                        {
                            Alignment = StringAlignment.Center,
                            LineAlignment = StringAlignment.Center
                        };

                        g.DrawString(number, currentFont, brush, new RectangleF(0, 0, 16, 16), sf);
                    }

                    IntPtr hIcon = bitmap.GetHicon();
                    return Icon.FromHandle(hIcon);
                }
            }
            catch
            {
                // If we fail to create a custom icon, return a default system icon
                return SystemIcons.Error;
            }
        }

        private async Task PingGateway()
        {
            if (isDisposing || trayIcon == null) return;

            try
            {
                if (string.IsNullOrEmpty(gatewayIP))
                {
                    DetectGateway();
                    if (string.IsNullOrEmpty(gatewayIP))
                    {
                        ShowErrorState("GW!");
                        return;
                    }
                }

                using (Ping ping = new Ping())
                {
                    PingReply reply = await ping.SendPingAsync(gatewayIP, PING_TIMEOUT);
                    if (!isDisposing && trayIcon != null)
                    {
                        if (reply.Status == IPStatus.Success)
                        {
                            Icon newIcon = CreateNumberIcon(reply.RoundtripTime.ToString());
                            Icon oldIcon = trayIcon.Icon;
                            trayIcon.Icon = newIcon;
                            if (oldIcon != null && oldIcon != newIcon)
                            {
                                oldIcon.Dispose();
                            }

                            trayIcon.Text = $"Built: {BuildTimestamp}\nPinging: {gatewayIP}\nLatency: {reply.RoundtripTime}ms";
                        }
                        else
                        {
                            Icon newIcon = CreateNumberIcon("X", true);
                            Icon oldIcon = trayIcon.Icon;
                            trayIcon.Icon = newIcon;
                            if (oldIcon != null && oldIcon != newIcon)
                            {
                                oldIcon.Dispose();
                            }
                            trayIcon.Text = $"Built: {BuildTimestamp}\nFailed to ping {gatewayIP}";
                        }
                    }
                }
            }
            catch (Exception)
            {
                if (!isDisposing && trayIcon != null)
                {
                    Icon newIcon = CreateNumberIcon("!", true);
                    Icon oldIcon = trayIcon.Icon;
                    trayIcon.Icon = newIcon;
                    if (oldIcon != null && oldIcon != newIcon)
                    {
                        oldIcon.Dispose();
                    }
                    trayIcon.Text = $"Built: {BuildTimestamp}\nNetwork error occurred";
                }
            }
        }

        private void OnQuit(object sender, EventArgs e)
        {
            isDisposing = true;
            Application.Exit();
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            isDisposing = true;
            try
            {
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
            }
            catch
            {
                // Ignore disposal errors
            }
        }
    }
}
using System;
using System.Drawing;
using System.Windows.Forms;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
using System.Linq;

namespace ping_applet
{
    public partial class Form1 : Form
    {
        private NotifyIcon trayIcon;
        private ContextMenuStrip contextMenu;
        private System.Windows.Forms.Timer pingTimer;
        private string gatewayIP;

        public Form1()
        {
            InitializeComponent();
            InitializeCustomComponents();
            this.FormClosing += Form1_FormClosing;
            DetectGateway();
            StartPingTimer();
        }

        private void InitializeCustomComponents()
        {
            // Create context menu
            contextMenu = new ContextMenuStrip();
            contextMenu.Items.Add("Quit", null, OnQuit);

            // Initialize tray icon
            trayIcon = new NotifyIcon
            {
                Icon = CreateNumberIcon("--"),
                Visible = true,
                ContextMenuStrip = contextMenu
            };

            // Configure form
            ShowInTaskbar = false;
            WindowState = FormWindowState.Minimized;
            FormBorderStyle = FormBorderStyle.None;
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
                    MessageBox.Show("No default gateway found. Please check your network connection.",
                        "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    Application.Exit();
                }
                return null;
            }
            catch (Exception ex)
            {
                // Only show message box and exit if this is the initial gateway detection
                if (string.IsNullOrEmpty(gatewayIP))
                {
                    MessageBox.Show($"Unable to get default gateway: {ex.Message}",
                        "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    Application.Exit();
                }
                return null;
            }
        }

        private void DetectGateway()
        {
            string detectedGateway = GetDefaultGateway();
            if (!string.IsNullOrEmpty(detectedGateway))
            {
                gatewayIP = detectedGateway;
                SetupNetworkChangeDetection();
            }
            // If detection fails, GetDefaultGateway will handle the error and exit the application
        }

        private void SetupNetworkChangeDetection()
        {
            NetworkChange.NetworkAddressChanged += (s, e) =>
            {
                // Just force a new ping attempt when network changes
                PingGateway();
            };

            NetworkChange.NetworkAvailabilityChanged += (s, e) =>
            {
                if (!e.IsAvailable)
                {
                    // Update icon to show network is unavailable
                    trayIcon.Icon = CreateNumberIcon("!", true);
                }
                // When network becomes available again, just let the regular ping timer handle it
            };
        }

        private void StartPingTimer()
        {
            pingTimer = new System.Windows.Forms.Timer
            {
                Interval = 1000 // 1 second
            };
            pingTimer.Tick += async (sender, e) => await PingGateway();
            pingTimer.Start();
        }

        private Icon CreateNumberIcon(string number, bool isError = false)
        {
            using (Bitmap bitmap = new Bitmap(16, 16))
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.Clear(isError ? Color.Red : Color.Black);

                float fontSize = number.Length switch
                {
                    1 => 10f,
                    2 => 8f,
                    3 => 6f,
                    _ => 5f
                };

                // Create initial font
                Font currentFont = new Font("Arial", fontSize, FontStyle.Bold);

                using (Brush brush = new SolidBrush(Color.White))
                {
                    SizeF textSize = g.MeasureString(number, currentFont);

                    // Adjust size if needed
                    if (textSize.Width > 15 || textSize.Height > 15)
                    {
                        float scale = Math.Min(15 / textSize.Width, 15 / textSize.Height);
                        currentFont.Dispose();
                        currentFont = new Font("Arial", fontSize * scale, FontStyle.Bold);
                    }

                    StringFormat sf = new StringFormat
                    {
                        Alignment = StringAlignment.Center,
                        LineAlignment = StringAlignment.Center
                    };

                    g.DrawString(number, currentFont, brush, new RectangleF(0, 0, 16, 16), sf);
                    currentFont.Dispose();
                }

                IntPtr hIcon = bitmap.GetHicon();
                return Icon.FromHandle(hIcon);
            }
        }

        private async Task PingGateway()
        {
            try
            {
                using (Ping ping = new Ping())
                {
                    PingReply reply = await ping.SendPingAsync(gatewayIP, 1000);
                    if (reply.Status == IPStatus.Success)
                    {
                        trayIcon.Icon = CreateNumberIcon(
                            reply.RoundtripTime.ToString());
                    }
                    else
                    {
                        trayIcon.Icon = CreateNumberIcon("X", true);
                    }
                }
            }
            catch (Exception)
            {
                trayIcon.Icon = CreateNumberIcon("!", true);
            }
        }

        private void OnQuit(object sender, EventArgs e)
        {
            trayIcon.Visible = false;
            Application.Exit();
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            trayIcon?.Dispose();
            contextMenu?.Dispose();
            pingTimer?.Dispose();
        }
    }
}
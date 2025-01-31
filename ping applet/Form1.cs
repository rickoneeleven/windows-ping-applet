using System;
using System.Drawing;
using System.Windows.Forms;
using System.Net.NetworkInformation;
using System.Threading.Tasks;

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

        private void DetectGateway()
        {
            // For now, using a placeholder IP
            gatewayIP = "64.71.34.109";
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

                using (Font font = new Font("Arial", 8, FontStyle.Bold))
                using (Brush brush = new SolidBrush(Color.White))
                {
                    StringFormat sf = new StringFormat
                    {
                        Alignment = StringAlignment.Center,
                        LineAlignment = StringAlignment.Center
                    };
                    g.DrawString(number, font, brush,
                        new RectangleF(0, 0, 16, 16), sf);
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

        // Add cleanup code to Form_FormClosing event
        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            trayIcon?.Dispose();
            contextMenu?.Dispose();
            pingTimer?.Dispose();
        }
    }
}
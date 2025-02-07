using System;
using System.IO;
using System.Windows.Forms;
using ping_applet.Controllers;
using ping_applet.Services;
using ping_applet.UI;
using ping_applet.Utils;

namespace ping_applet
{
    public partial class MainForm : Form
    {
        private AppController appController;
        private readonly BuildInfoProvider buildInfoProvider;
        private static readonly string LogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PingApplet",
            "ping.log"
        );

        public MainForm()
        {
            try
            {
                InitializeComponent();
                buildInfoProvider = new BuildInfoProvider();
                InitializeApplication();
            }
            catch (Exception ex)
            {
                HandleInitializationError(ex);
            }
        }

        private void InitializeApplication()
        {
            // Create core services
            var loggingService = new LoggingService(LogPath);
            var networkMonitor = new NetworkMonitor();
            var pingService = new PingService();
            var trayIconManager = new TrayIconManager(buildInfoProvider);

            // Create and initialize controller
            appController = new AppController(networkMonitor, pingService, loggingService, trayIconManager);
            appController.ApplicationExit += (s, e) => Application.Exit();

            // Configure form
            ConfigureForm();

            // Start the application
            _ = appController.InitializeAsync();
        }

        private void ConfigureForm()
        {
            ShowInTaskbar = false;
            WindowState = FormWindowState.Minimized;
            FormBorderStyle = FormBorderStyle.None;
            FormClosing += MainForm_FormClosing;
        }

        private void HandleInitializationError(Exception ex)
        {
            MessageBox.Show(
                $"Failed to initialize the application: {ex.Message}",
                "Initialization Error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error
            );
            Application.Exit();
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            appController?.Dispose();
        }
    }
}
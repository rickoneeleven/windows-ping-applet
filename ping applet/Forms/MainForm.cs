using System;
using System.IO;
using System.Threading.Tasks;
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
        private bool isDisposed;

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
                ConfigureForm();
                _ = InitializeApplicationAsync();
            }
            catch (Exception ex)
            {
                HandleInitializationError(ex);
            }
        }

        private void ConfigureForm()
        {
            ShowInTaskbar = false;
            WindowState = FormWindowState.Minimized;
            FormBorderStyle = FormBorderStyle.None;

            FormClosing += MainForm_FormClosing;
            Load += MainForm_Load;
        }

        private async Task InitializeApplicationAsync()
        {
            try
            {
                var loggingService = new LoggingService(LogPath);
                var networkMonitor = new NetworkMonitor();
                var trayIconManager = new TrayIconManager(buildInfoProvider, loggingService);

                appController = new AppController(networkMonitor, loggingService, trayIconManager);
                appController.ApplicationExit += AppController_ApplicationExit;

                await appController.InitializeAsync();
            }
            catch (Exception ex)
            {
                HandleInitializationError(ex);
            }
        }

        private void AppController_ApplicationExit(object sender, EventArgs e)
        {
            if (InvokeRequired)
            {
                Invoke(new Action(() => Application.Exit()));
            }
            else
            {
                Application.Exit();
            }
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            Hide();
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            try
            {
                if (appController != null)
                {
                    // First remove the event handler to prevent any last-minute logging attempts
                    appController.ApplicationExit -= AppController_ApplicationExit;
                    // Then dispose the controller which will handle disposing other services
                    appController.Dispose();
                    appController = null;
                }
            }
            catch (Exception ex)
            {
                // Since LoggingService might be disposed, we'll show a message box instead of logging
                MessageBox.Show(
                    $"Error during cleanup: {ex.Message}",
                    "Cleanup Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );
            }
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

        protected override void Dispose(bool disposing)
        {
            if (!isDisposed)
            {
                if (disposing)
                {
                    if (appController != null)
                    {
                        appController.ApplicationExit -= AppController_ApplicationExit;
                        appController.Dispose();
                        appController = null;
                    }
                    if (components != null)
                    {
                        components.Dispose();
                    }
                }
                isDisposed = true;
                base.Dispose(disposing);
            }
        }
    }
}
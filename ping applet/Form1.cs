using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using ping_applet.Controllers;
using ping_applet.Core.Interfaces;
using ping_applet.Services;
using ping_applet.UI;

namespace ping_applet
{
    public partial class Form1 : Form
    {
        private AppController appController;
        private static readonly string BuildTimestamp = GetBuildDate();
        private static readonly string LogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PingApplet",
            "ping.log"
        );

        public Form1()
        {
            try
            {
                InitializeComponent();
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
            var trayIconManager = new TrayIconManager(BuildTimestamp);

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
            FormClosing += Form1_FormClosing;
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

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            appController?.Dispose();
        }
    }
}
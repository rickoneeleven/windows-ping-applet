using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using ping_applet.Controllers;
using ping_applet.Services;
using ping_applet.UI;
using ping_applet.Utils; // Required for KnownAPManager
using System.Diagnostics;

namespace ping_applet
{
    public partial class MainForm : Form
    {
        private AppController appController;
        private readonly BuildInfoProvider buildInfoProvider;
        private KnownAPManager knownAPManager; // Field to hold the instance for disposal
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
                Debug.WriteLine($"[MainForm] Constructor exception: {ex.Message}");
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
            LoggingService loggingService = null; // Declare here for broader scope if needed for early logging
            try
            {
                Debug.WriteLine("[MainForm] InitializeApplicationAsync started.");
                loggingService = new LoggingService(LogPath); // Instantiate LoggingService first

                // Instantiate KnownAPManager here as it's needed by AppController
                // and it also uses LoggingService.
                this.knownAPManager = new KnownAPManager(loggingService);

                var networkMonitor = new NetworkMonitor(); // Does not depend on loggingService directly in its constructor
                var trayIconManager = new TrayIconManager(buildInfoProvider, loggingService); // TrayIconManager creates its own KnownAPManager internally for now.

                // Pass the MainForm-created knownAPManager to AppController
                appController = new AppController(networkMonitor, loggingService, trayIconManager, this.knownAPManager);
                appController.ApplicationExit += AppController_ApplicationExit;

                await appController.InitializeAsync();
                Debug.WriteLine("[MainForm] InitializeApplicationAsync completed successfully.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MainForm] InitializeApplicationAsync exception: {ex.Message}");
                // Attempt to log with loggingService if it was initialized, otherwise fallback to Debug.
                loggingService?.LogError("Critical error during InitializeApplicationAsync", ex);

                if (IsHandleCreated && InvokeRequired)
                {
                    Invoke(new Action(() => HandleInitializationError(ex)));
                }
                else
                {
                    HandleInitializationError(ex);
                }
            }
        }

        private void AppController_ApplicationExit(object sender, EventArgs e)
        {
            Program.RequestGracefulExit();
            Debug.WriteLine("[MainForm] AppController_ApplicationExit: Graceful exit requested. Calling Application.Exit().");
            if (InvokeRequired)
            {
                Invoke(new Action(Application.Exit));
            }
            else
            {
                Application.Exit();
            }
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            Hide();
            Debug.WriteLine("[MainForm] MainForm_Load: Form hidden.");
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            Program.RequestGracefulExit();
            Debug.WriteLine($"[MainForm] MainForm_FormClosing: Graceful exit signaled. CloseReason: {e.CloseReason}.");
            try
            {
                if (appController != null)
                {
                    appController.ApplicationExit -= AppController_ApplicationExit;
                    Debug.WriteLine("[MainForm] MainForm_FormClosing: Unsubscribed from AppController.ApplicationExit.");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MainForm] MainForm_FormClosing: Exception during event unsubscription: {ex.Message}");
            }
        }

        private void HandleInitializationError(Exception ex)
        {
            Debug.WriteLine($"[MainForm] HandleInitializationError: {ex.Message}");
            MessageBox.Show(
                $"Failed to initialize the application: {ex.Message}\n\n{ex.StackTrace}",
                "Initialization Error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error
            );

            Program.RequestGracefulExit();
            if (IsHandleCreated)
            {
                Application.Exit();
            }
            else
            {
                Environment.Exit(1);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (!isDisposed)
            {
                if (disposing)
                {
                    Program.RequestGracefulExit();
                    Debug.WriteLine("[MainForm] Dispose(true): Disposing managed resources.");

                    // Dispose AppController first, as it uses other services
                    if (appController != null)
                    {
                        Debug.WriteLine("[MainForm] Dispose(true): Disposing AppController.");
                        try
                        {
                            appController.ApplicationExit -= AppController_ApplicationExit; // Defensive unsubscribe
                        }
                        catch (Exception unsubEx)
                        {
                            Debug.WriteLine($"[MainForm] Dispose(true): Exception during AppController event unsubscription: {unsubEx.Message}");
                        }
                        appController.Dispose();
                        appController = null;
                        Debug.WriteLine("[MainForm] Dispose(true): AppController disposed.");
                    }

                    // Dispose KnownAPManager that was created by MainForm
                    if (knownAPManager != null)
                    {
                        Debug.WriteLine("[MainForm] Dispose(true): Disposing KnownAPManager.");
                        knownAPManager.Dispose();
                        knownAPManager = null;
                        Debug.WriteLine("[MainForm] Dispose(true): KnownAPManager disposed.");
                    }
                    // Note: LoggingService, NetworkMonitor, TrayIconManager are created in InitializeApplicationAsync
                    // and their lifetimes are managed by AppController's disposal (or TrayIconManager's internal disposal).
                    // AppController should be responsible for disposing services it was directly passed or created if not DI.
                    // Since LoggingService is passed to AppController and KnownAPManager (and TrayIconManager),
                    // its ultimate disposal is handled by AppController being disposed last among them.

                    if (components != null)
                    {
                        components.Dispose();
                        Debug.WriteLine("[MainForm] Dispose(true): Designer components disposed.");
                    }
                }
                isDisposed = true;
                Debug.WriteLine($"[MainForm] Dispose: isDisposed set to true (disposing flag was {disposing}).");
                base.Dispose(disposing);
                Debug.WriteLine("[MainForm] Dispose: Base.Dispose() called.");
            }
            else
            {
                Debug.WriteLine($"[MainForm] Dispose: Already disposed (disposing flag was {disposing}).");
            }
        }
    }
}
using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using ping_applet.Controllers;
using ping_applet.Services;
using ping_applet.UI;
using ping_applet.Utils;
using System.Diagnostics; // Required for Debug.WriteLine

namespace ping_applet
{
    public partial class MainForm : Form
    {
        private AppController appController;
        private readonly BuildInfoProvider buildInfoProvider;
        private bool isDisposed; // Standard IDisposable pattern flag

        private static readonly string LogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PingApplet",
            "ping.log"
        );

        public MainForm()
        {
            // DO NOT call Program.RequestGracefulExit() here.
            // This constructor is part of normal startup.
            try
            {
                InitializeComponent();
                buildInfoProvider = new BuildInfoProvider();
                ConfigureForm();
                // Asynchronously initialize the application core.
                // The task is intentionally not awaited here to allow the form to load.
                _ = InitializeApplicationAsync();
            }
            catch (Exception ex)
            {
                // Handle synchronous exceptions during constructor/initial setup.
                Debug.WriteLine($"[MainForm] Constructor exception: {ex.Message}");
                HandleInitializationError(ex);
            }
        }

        private void ConfigureForm()
        {
            ShowInTaskbar = false;
            WindowState = FormWindowState.Minimized;
            FormBorderStyle = FormBorderStyle.None;

            // Event subscriptions
            FormClosing += MainForm_FormClosing;
            Load += MainForm_Load;
        }

        private async Task InitializeApplicationAsync()
        {
            try
            {
                Debug.WriteLine("[MainForm] InitializeApplicationAsync started.");
                var loggingService = new LoggingService(LogPath);
                // buildInfoProvider is already initialized in constructor.
                var networkMonitor = new NetworkMonitor();
                var trayIconManager = new TrayIconManager(buildInfoProvider, loggingService);

                appController = new AppController(networkMonitor, loggingService, trayIconManager);
                appController.ApplicationExit += AppController_ApplicationExit;

                await appController.InitializeAsync();
                Debug.WriteLine("[MainForm] InitializeApplicationAsync completed successfully.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MainForm] InitializeApplicationAsync exception: {ex.Message}");
                // This is an async method. If an error occurs, it needs to be handled.
                // If InvokeRequired (not on UI thread), marshall the error handling.
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
            Program.RequestGracefulExit(); // Signal that this is a planned, graceful exit.
            Debug.WriteLine("[MainForm] AppController_ApplicationExit: Graceful exit requested. Calling Application.Exit().");

            // Ensure Application.Exit() is called on the UI thread.
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
            Hide(); // Hide the main form as it's a tray application.
            Debug.WriteLine("[MainForm] MainForm_Load: Form hidden.");
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            // This event is triggered when the form is about to close,
            // either by Application.Exit(), OS shutdown, or user action.
            Program.RequestGracefulExit(); // Re-affirm: this is part of a graceful shutdown.
            Debug.WriteLine($"[MainForm] MainForm_FormClosing: Graceful exit signaled. CloseReason: {e.CloseReason}.");

            try
            {
                // Unsubscribe from events to prevent further calls during disposal.
                if (appController != null)
                {
                    appController.ApplicationExit -= AppController_ApplicationExit;
                    Debug.WriteLine("[MainForm] MainForm_FormClosing: Unsubscribed from AppController.ApplicationExit.");
                }
            }
            catch (Exception ex)
            {
                // Log critical errors during this phase using Debug.WriteLine,
                // as LoggingService might be in the process of being disposed.
                Debug.WriteLine($"[MainForm] MainForm_FormClosing: Exception during event unsubscription: {ex.Message}");
                // Do not show MessageBox, especially during OS shutdown.
            }
            // The AppController and other resources will be disposed in MainForm.Dispose(bool)
            // which is called automatically by the framework after FormClosing completes
            // if the form is actually closing (not cancelled).
        }

        private void HandleInitializationError(Exception ex)
        {
            Debug.WriteLine($"[MainForm] HandleInitializationError: {ex.Message}");
            // Display error to the user for initialization failures.
            MessageBox.Show(
                $"Failed to initialize the application: {ex.Message}\n\n{ex.StackTrace}",
                "Initialization Error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error
            );

            Program.RequestGracefulExit(); // Signal that this is a "planned" exit due to init failure.
            // Application.Exit can throw if no message loop has started.
            // Check if the handle is created, implying the message loop is likely running.
            if (IsHandleCreated)
            {
                Application.Exit();
            }
            else
            {
                // If no message loop, direct exit is needed.
                // This might still be abrupt for Program.cs's recovery, but better than hanging.
                Environment.Exit(1); // Non-zero code indicates error.
            }
        }

        protected override void Dispose(bool disposing)
        {
            // This is the final cleanup point for the form.
            // It's called after FormClosing when the form is actually being disposed.
            // It can also be called directly if the form is part of a parent container that is disposing it.

            if (!isDisposed) // Standard IDisposable pattern: check if already disposed.
            {
                // If disposing is true, it means Dispose() was called explicitly or by the framework (managed resources).
                // If disposing is false, it means the finalizer is running (unmanaged resources only - not typical for Forms).
                if (disposing)
                {
                    Program.RequestGracefulExit(); // Re-affirm or set if another path led here.
                    Debug.WriteLine("[MainForm] Dispose(true): Disposing managed resources.");

                    // Dispose the AppController here. This is the designated place.
                    if (appController != null)
                    {
                        Debug.WriteLine("[MainForm] Dispose(true): Disposing AppController.");
                        // Event unsubscription should have happened in FormClosing.
                        // If not, or if appController was created but FormClosing didn't run (unlikely for a main form),
                        // it's safer to try unsubscribing again, carefully.
                        try
                        {
                            appController.ApplicationExit -= AppController_ApplicationExit;
                        }
                        catch (Exception unsubEx)
                        {
                            Debug.WriteLine($"[MainForm] Dispose(true): Exception during AppController event unsubscription: {unsubEx.Message}");
                        }

                        appController.Dispose();
                        appController = null; // Release the reference.
                        Debug.WriteLine("[MainForm] Dispose(true): AppController disposed.");
                    }

                    // Dispose components created by the designer (e.g., if you had any UI controls).
                    if (components != null)
                    {
                        components.Dispose();
                        Debug.WriteLine("[MainForm] Dispose(true): Designer components disposed.");
                    }
                }

                // Mark as disposed to prevent multiple disposals.
                isDisposed = true;
                Debug.WriteLine($"[MainForm] Dispose: isDisposed set to true (disposing flag was {disposing}).");

                // Call the base class's Dispose method.
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
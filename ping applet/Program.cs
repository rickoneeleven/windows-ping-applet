using System;
using System.Windows.Forms;
using System.Threading;
using System.Diagnostics; // Required for Debug.WriteLine

namespace ping_applet
{
    internal static class Program
    {
        private static MainForm mainForm;
        private static readonly int RestartDelay = 5000; // 5 seconds
        private static bool _isGracefulExitRequested = false;

        // Public property to allow observation of the shutdown state if needed externally.
        public static bool IsGracefulExitUnderway => _isGracefulExitRequested;

        /// <summary>
        /// Signals that a graceful application exit has been initiated.
        /// This should be called before Application.Exit().
        /// </summary>
        public static void RequestGracefulExit()
        {
            if (!_isGracefulExitRequested)
            {
                _isGracefulExitRequested = true;
                Debug.WriteLine("[Program] Graceful exit has been requested.");
            }
        }

        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += Application_ThreadException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

            RunApplicationWithRecovery();

            Debug.WriteLine("[Program] Main method completed. Application will now terminate.");
        }

        private static void RunApplicationWithRecovery()
        {
            while (true) // Recovery loop
            {
                // If a graceful exit was requested in a previous iteration or before starting,
                // terminate the recovery loop immediately.
                if (_isGracefulExitRequested)
                {
                    Debug.WriteLine("[Program] Graceful exit flag is set; recovery loop terminating.");
                    break;
                }

                mainForm = null; // Ensure a fresh start for the MainForm instance

                try
                {
                    Debug.WriteLine("[Program] Starting new application instance cycle.");
                    mainForm = new MainForm();
                    Application.Run(mainForm); // This call blocks until Application.Exit() is called or the last form closes.

                    // Code here executes after Application.Run() returns.
                    // This can happen if Application.Exit() was called, or if all forms closed.
                    if (_isGracefulExitRequested || (mainForm != null && mainForm.IsDisposed))
                    {
                        // This is a planned exit: either RequestGracefulExit was called,
                        // or the mainForm was properly disposed (implying a normal closure).
                        Debug.WriteLine("[Program] Application.Run exited as part of a graceful shutdown or normal form disposal. Recovery loop terminating.");
                        break;
                    }
                    else
                    {
                        // Application.Run returned, but it wasn't a requested graceful exit,
                        // and the mainForm isn't disposed. This is an unexpected state.
                        // Treat it as a crash to ensure the catch block handles it for potential restart.
                        Debug.WriteLine("[Program] Application.Run exited unexpectedly (not graceful, form not disposed). Triggering error handling.");
                        throw new InvalidOperationException("Application.Run exited without a graceful shutdown signal and the main form was not disposed.");
                    }
                }
                catch (Exception ex)
                {
                    // An exception was caught, either from Application.Run itself or from the InvalidOperationException above.
                    if (_isGracefulExitRequested)
                    {
                        // An exception occurred, but a graceful exit was already in progress.
                        // Log the error, but do not restart the application.
                        Debug.WriteLine($"[Program] Exception caught during a graceful shutdown process: {ex.Message}. Recovery loop terminating.");
                        HandleFatalException(ex, true); // true for isGracefulShutdownContext
                        break; // Exit the recovery loop.
                    }

                    // This is a genuine crash during normal operation.
                    Debug.WriteLine($"[Program] Unhandled exception caught in recovery loop: {ex.Message}. Initiating restart sequence.");
                    HandleFatalException(ex, false); // false for isGracefulShutdownContext

                    Debug.WriteLine($"[Program] Waiting {RestartDelay}ms before attempting restart.");
                    Thread.Sleep(RestartDelay);

                    // Clean up the old form if it still exists and wasn't disposed by the crash.
                    if (mainForm != null && !mainForm.IsDisposed)
                    {
                        Debug.WriteLine("[Program] Disposing of crashed MainForm instance before restart.");
                        try
                        {
                            mainForm.Dispose();
                        }
                        catch (Exception disposeEx)
                        {
                            // Log error during disposal of crashed form, but proceed with restart.
                            Debug.WriteLine($"[Program] Exception while disposing crashed MainForm: {disposeEx.Message}");
                        }
                    }
                    mainForm = null; // Ensure it's fully reset for the next iteration.
                    Debug.WriteLine("[Program] Continuing recovery loop to attempt application restart.");
                    // The loop will continue, and if _isGracefulExitRequested is still false, a new instance will be attempted.
                }
            }
            Debug.WriteLine("[Program] Exited recovery loop. Application process will now terminate.");
        }

        private static void Application_ThreadException(object sender, ThreadExceptionEventArgs e)
        {
            // Handles exceptions on the main UI thread.
            Debug.WriteLine($"[Program] UI Thread Exception occurred. GracefulExitRequested: {_isGracefulExitRequested}. Exception: {e.Exception.Message}");
            HandleFatalException(e.Exception, _isGracefulExitRequested);
            // The decision to restart or terminate is handled by the RunApplicationWithRecovery loop's catch block.
        }

        private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            // Handles exceptions on any thread that are not caught elsewhere.
            Exception ex = e.ExceptionObject as Exception;
            Debug.WriteLine($"[Program] Unhandled Domain Exception. GracefulExitRequested: {_isGracefulExitRequested}. IsTerminating: {e.IsTerminating}. Exception: {ex?.Message}");
            HandleFatalException(ex, _isGracefulExitRequested);
            // If e.IsTerminating is true, the CLR is already shutting down the application.
            // The recovery loop might not get a chance to restart if this happens on a background thread
            // and the main thread is also brought down. This is a limitation of in-process recovery.
        }

        private static void HandleFatalException(Exception ex, bool isGracefulShutdownContext)
        {
            // Centralized method to log fatal errors and optionally notify the user.
            // This method should avoid dependencies on services that might be disposed or causing the error (e.g., LoggingService).
            string contextMessage = isGracefulShutdownContext ? "during graceful shutdown" : "during normal operation";
            string fullErrorMessage = $"FATAL ERROR ({contextMessage}): {ex?.GetType().Name}: {ex?.Message}{Environment.NewLine}Stack Trace:{Environment.NewLine}{ex?.StackTrace}";

            Debug.WriteLine(fullErrorMessage);
            Console.WriteLine(fullErrorMessage); // Also to console, if visible.

            // Per PRD: "The application should not display a MessageBox during system shutdown" or graceful quit.
            if (!isGracefulShutdownContext)
            {
                try
                {
                    string userNotificationMessage = "A critical error occurred.\n\n" +
                                                     $"Error: {ex?.GetType().Name} - {ex?.Message}\n\n" +
                                                     "The application will attempt to restart.";
                    MessageBox.Show(
                        userNotificationMessage,
                        "Application Error",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error
                    );
                }
                catch (Exception mbEx)
                {
                    // If showing a MessageBox fails (e.g., UI context lost, or during very early/late stages).
                    Debug.WriteLine($"[Program] Failed to show error MessageBox in HandleFatalException: {mbEx.Message}");
                }
            }
        }
    }
}
﻿using System;
using System.Windows.Forms;
using System.Threading;

namespace ping_applet
{
    internal static class Program
    {
        private static MainForm mainForm;
        private static readonly int RestartDelay = 5000; // 5 seconds

        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // Set up global exception handlers
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += Application_ThreadException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

            // Start the application with error recovery
            RunApplicationWithRecovery();
        }

        private static void RunApplicationWithRecovery()
        {
            while (true)
            {
                try
                {
                    mainForm = new MainForm();
                    Application.Run(mainForm);

                    // If we get here normally (via Quit), exit the application
                    if (mainForm.IsDisposed)
                        break;
                }
                catch (Exception ex)
                {
                    HandleFatalException(ex);

                    // Wait before restarting
                    Thread.Sleep(RestartDelay);

                    // Clean up the old form if it exists
                    if (mainForm != null && !mainForm.IsDisposed)
                    {
                        try
                        {
                            mainForm.Dispose();
                        }
                        catch { /* Ignore disposal errors */ }
                    }

                    // Continue the loop to restart the application
                    continue;
                }
            }
        }

        private static void Application_ThreadException(object sender, ThreadExceptionEventArgs e)
        {
            HandleFatalException(e.Exception);
        }

        private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            HandleFatalException(e.ExceptionObject as Exception);
        }

        private static void HandleFatalException(Exception ex)
        {
            try
            {
                // Log the error (you could add file logging here if needed)
                Console.WriteLine($"Fatal error occurred: {ex}");

                // Show error message to user
                MessageBox.Show(
                    "A fatal error occurred. The application will restart in 5 seconds.",
                    "Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
            }
            catch
            {
                // If we can't handle the error gracefully, at least try to show a message
                try
                {
                    MessageBox.Show(
                        "A fatal error occurred. The application will restart.",
                        "Error",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error
                    );
                }
                catch
                {
                    // At this point, we can't even show a message box
                    // Nothing more we can do
                }
            }
        }
    }
}
using System;
using System.IO;
using ping_applet.Core.Interfaces;

namespace ping_applet.Services
{
    public class LoggingService : ILoggingService
    {
        private bool isDisposed;
        private readonly object logLock = new object();

        public string LogPath { get; set; }

        public LoggingService(string logPath)
        {
            LogPath = logPath ?? throw new ArgumentNullException(nameof(logPath));
            Initialize();
        }

        public void Initialize()
        {
            if (isDisposed)
                throw new ObjectDisposedException(nameof(LoggingService));

            try
            {
                string directory = Path.GetDirectoryName(LogPath);
                System.Diagnostics.Debug.WriteLine($"Initializing LoggingService with path: {LogPath}");
                System.Diagnostics.Debug.WriteLine($"Directory: {directory}");

                if (string.IsNullOrEmpty(directory))
                {
                    throw new InvalidOperationException("Log directory path is empty");
                }

                if (!Directory.Exists(directory))
                {
                    System.Diagnostics.Debug.WriteLine($"Creating directory: {directory}");
                    Directory.CreateDirectory(directory);
                }

                // Try to write a test entry to verify permissions
                File.AppendAllText(LogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [INIT]: Logging initialized{Environment.NewLine}");
                System.Diagnostics.Debug.WriteLine("Successfully wrote test entry to log file");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to initialize logging: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                throw new InvalidOperationException("Failed to initialize logging directory", ex);
            }
        }

        public void LogInfo(string message)
        {
            if (isDisposed)
                throw new ObjectDisposedException(nameof(LoggingService));

            WriteToLog("INFO", message);
        }

        public void LogError(string message, Exception ex = null)
        {
            if (isDisposed)
                throw new ObjectDisposedException(nameof(LoggingService));

            string errorMessage = message;
            if (ex != null)
            {
                errorMessage += $"\nException: {ex.Message}";
                if (ex.StackTrace != null)
                {
                    errorMessage += $"\nStack Trace: {ex.StackTrace}";
                }
            }

            WriteToLog("ERROR", errorMessage);
        }

        private void WriteToLog(string level, string message)
        {
            if (string.IsNullOrEmpty(LogPath))
                return;

            try
            {
                string formattedMessage = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}]: {message}";

                lock (logLock)
                {
                    File.AppendAllText(LogPath, formattedMessage + Environment.NewLine);
                }
            }
            catch (Exception ex)
            {
                // Output to debug to help diagnose issues
                System.Diagnostics.Debug.WriteLine($"Failed to write to log file: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Attempted to write to: {LogPath}");
                // Still suppress the exception to prevent cascading failures
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!isDisposed && disposing)
            {
                // Nothing to dispose here, but we'll set the flag
                isDisposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}
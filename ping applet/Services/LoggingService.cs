using System;
using System.IO;
using System.Linq;
using System.Text;
using ping_applet.Core.Interfaces;

namespace ping_applet.Services
{
    public class LoggingService : ILoggingService
    {
        private bool isDisposed;
        private readonly object logLock = new object();
        private const int DEFAULT_MAX_LOG_SIZE = 10 * 1024 * 1024; // 10MB in bytes
        private const int RETENTION_BUFFER = 1 * 1024 * 1024; // 1MB buffer for retained logs

        public string LogPath { get; set; }
        public long MaxLogSizeBytes { get; set; }

        public LoggingService(string logPath)
        {
            LogPath = logPath ?? throw new ArgumentNullException(nameof(logPath));
            MaxLogSizeBytes = DEFAULT_MAX_LOG_SIZE;
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
                    RotateLogIfNeeded();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to write to log file: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Attempted to write to: {LogPath}");
            }
        }

        public bool RotateLogIfNeeded()
        {
            try
            {
                var fileInfo = new FileInfo(LogPath);
                if (!fileInfo.Exists || fileInfo.Length < MaxLogSizeBytes)
                {
                    return false;
                }

                // Read all content
                string[] allLines;
                lock (logLock)
                {
                    allLines = File.ReadAllLines(LogPath);
                }

                // Calculate approximately how many lines we need to keep
                long totalSize = allLines.Sum(line => Encoding.UTF8.GetByteCount(line + Environment.NewLine));
                int linesToKeep = (int)(allLines.Length * (MaxLogSizeBytes - RETENTION_BUFFER) / totalSize);

                // Keep the most recent lines
                string[] newContent = allLines.Skip(Math.Max(0, allLines.Length - linesToKeep)).ToArray();

                // Write back the truncated content
                lock (logLock)
                {
                    File.WriteAllLines(LogPath, newContent);
                }

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to rotate log file: {ex.Message}");
                return false;
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!isDisposed && disposing)
            {
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
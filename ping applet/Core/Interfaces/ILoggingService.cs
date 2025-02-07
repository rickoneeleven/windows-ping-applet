using System;

namespace ping_applet.Core.Interfaces
{
    /// <summary>
    /// Interface for handling application logging with size management
    /// </summary>
    public interface ILoggingService : IDisposable
    {
        /// <summary>
        /// Gets or sets the path where log files will be stored
        /// </summary>
        string LogPath { get; set; }

        /// <summary>
        /// Gets or sets the maximum size of the log file in bytes
        /// </summary>
        long MaxLogSizeBytes { get; set; }

        /// <summary>
        /// Logs an informational message with timestamp
        /// </summary>
        /// <param name="message">The message to log</param>
        void LogInfo(string message);

        /// <summary>
        /// Logs an error message with timestamp and exception details
        /// </summary>
        /// <param name="message">The error message</param>
        /// <param name="ex">The exception to log</param>
        void LogError(string message, Exception ex = null);

        /// <summary>
        /// Initializes the logging service, creating necessary directories
        /// </summary>
        void Initialize();

        /// <summary>
        /// Performs log rotation if the file size exceeds the maximum limit
        /// </summary>
        /// <returns>True if rotation was performed, false otherwise</returns>
        bool RotateLogIfNeeded();
    }
}
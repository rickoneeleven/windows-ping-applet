using System;
using System.Windows.Forms;
using ping_applet.Core.Interfaces;

namespace ping_applet.UI
{
    /// <summary>
    /// Manages system tray notifications and balloon tips
    /// </summary>
    public class NotificationManager
    {
        private readonly NotifyIcon trayIcon;
        private readonly ILoggingService loggingService;
        private bool isEnabled = true;

        // Constants for balloon tips
        private const int BALLOON_TIMEOUT = 2000; // 2 seconds
        private const string BALLOON_TITLE = "Network Change";

        public NotificationManager(NotifyIcon trayIcon, ILoggingService loggingService)
        {
            this.trayIcon = trayIcon ?? throw new ArgumentNullException(nameof(trayIcon));
            this.loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
        }

        /// <summary>
        /// Gets or sets whether notifications are enabled
        /// </summary>
        public bool IsEnabled
        {
            get => isEnabled;
            set
            {
                if (isEnabled != value)
                {
                    isEnabled = value;
                    loggingService.LogInfo($"Notifications {(value ? "enabled" : "disabled")}");
                }
            }
        }

        /// <summary>
        /// Shows a transition notification when switching between access points
        /// </summary>
        public void ShowTransitionNotification(string oldBssid, string newBssid, string oldDisplayName, string newDisplayName)
        {
            if (!isEnabled) return;

            try
            {
                string message;
                if (string.IsNullOrEmpty(oldBssid))
                {
                    // Initial connection or connection after being disconnected
                    message = $"Connected to {newDisplayName}";
                }
                else if (string.IsNullOrEmpty(newBssid))
                {
                    // Disconnection
                    message = $"Disconnected from {oldDisplayName}";
                }
                else
                {
                    // Transition between APs
                    message = $"Switched from {oldDisplayName} to {newDisplayName}";
                }

                ShowBalloonTip(message);
                loggingService.LogInfo($"Showed transition notification: {message}");
            }
            catch (Exception ex)
            {
                loggingService.LogError("Error showing transition notification", ex);
            }
        }

        /// <summary>
        /// Shows a generic notification with the specified message
        /// </summary>
        public void ShowNotification(string message)
        {
            if (!isEnabled) return;

            try
            {
                ShowBalloonTip(message);
                loggingService.LogInfo($"Showed notification: {message}");
            }
            catch (Exception ex)
            {
                loggingService.LogError("Error showing notification", ex);
            }
        }

        private void ShowBalloonTip(string message)
        {
            trayIcon.ShowBalloonTip(
                BALLOON_TIMEOUT,
                BALLOON_TITLE,
                message,
                ToolTipIcon.Info
            );
        }
    }
}
using System;
using System.Drawing;
using System.Windows.Forms;
using ping_applet.Core.Interfaces;
using ping_applet.Utils;

namespace ping_applet.UI
{
    /// <summary>
    /// Manages the system tray icon display and coordinates between UI components
    /// </summary>
    public class TrayIconManager : IDisposable
    {
        private readonly NotifyIcon trayIcon;
        private readonly IconGenerator iconGenerator;
        private readonly MenuManager menuManager;
        private readonly NotificationManager notificationManager;
        private readonly KnownAPManager knownAPManager;
        private readonly ILoggingService loggingService;
        private bool isDisposed;

        // UI state tracking
        private string currentDisplayText;
        private string currentTooltipText;
        private bool currentErrorState;
        private bool currentTransitionState;
        private bool currentUseBlackText;
        private string currentBSSID;

        public event EventHandler QuitRequested;

        public TrayIconManager(BuildInfoProvider buildInfoProvider, ILoggingService loggingService)
        {
            this.loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));

            iconGenerator = new IconGenerator();
            knownAPManager = new KnownAPManager(loggingService);

            // Initialize tray icon
            trayIcon = new NotifyIcon
            {
                Icon = iconGenerator.CreateNumberIcon("--"),
                Visible = true,
                Text = "Initializing..."
            };

            // Initialize managers
            menuManager = new MenuManager(
                buildInfoProvider,
                loggingService,
                new StartupManager(),
                knownAPManager,
                System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "PingApplet",
                    "ping.log"
                )
            );

            notificationManager = new NotificationManager(trayIcon, loggingService);

            // Connect event handlers
            menuManager.QuitRequested += (s, e) => QuitRequested?.Invoke(this, EventArgs.Empty);

            // Set up tray icon context menu
            trayIcon.ContextMenuStrip = menuManager.GetContextMenu();
        }

        public void UpdateLocationServicesState(bool isDisabled)
        {
            if (isDisposed) return;
            menuManager.UpdateLocationServicesState(isDisabled);
        }

        public void ShowTransitionBalloon(string oldBssid, string newBssid)
        {
            if (isDisposed) return;

            var oldDisplayName = GetAPDisplayName(oldBssid);
            var newDisplayName = GetAPDisplayName(newBssid);
            notificationManager.ShowTransitionNotification(oldBssid, newBssid, oldDisplayName, newDisplayName);
        }

        public void UpdateCurrentAP(string bssid)
        {
            if (isDisposed) return;

            try
            {
                currentBSSID = bssid;
                string displayName;

                if (!string.IsNullOrEmpty(bssid))
                {
                    displayName = knownAPManager.GetDisplayName(bssid);
                    if (displayName.Equals(bssid))
                    {
                        knownAPManager.AddNewAP(bssid);
                    }
                }
                else
                {
                    displayName = null;
                }

                menuManager.UpdateCurrentAP(displayName);
            }
            catch (Exception ex)
            {
                loggingService.LogError("Error updating current AP", ex);
            }
        }

        public void UpdateIcon(string displayText, string tooltipText, bool isError = false, bool isTransition = false, bool useBlackText = false)
        {
            if (isDisposed) return;

            currentDisplayText = displayText;
            currentTooltipText = tooltipText;
            currentErrorState = isError;
            currentTransitionState = isTransition;
            currentUseBlackText = useBlackText;

            Icon newIcon = null;
            try
            {
                if (isTransition)
                {
                    newIcon = useBlackText ?
                        iconGenerator.CreateTransitionIconWithBlackText(displayText) :
                        iconGenerator.CreateTransitionIcon(displayText);
                }
                else
                {
                    newIcon = iconGenerator.CreateNumberIcon(displayText, isError);
                }

                Icon oldIcon = trayIcon.Icon;
                trayIcon.Icon = newIcon;
                trayIcon.Text = tooltipText;

                oldIcon?.Dispose();
            }
            catch (Exception ex)
            {
                loggingService.LogError("Error updating icon", ex);
                newIcon?.Dispose();
                throw;
            }
        }

        public string GetAPDisplayName(string bssid)
        {
            if (isDisposed) return bssid;

            try
            {
                return knownAPManager.GetDisplayName(bssid);
            }
            catch (Exception ex)
            {
                loggingService.LogError("Error getting AP display name", ex);
                return bssid;
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!isDisposed && disposing)
            {
                try
                {
                    trayIcon.Visible = false;
                    menuManager?.Dispose();
                    iconGenerator?.Dispose();
                    knownAPManager?.Dispose();
                    trayIcon?.Dispose();
                }
                catch (Exception ex)
                {
                    loggingService.LogError("Error during TrayIconManager disposal", ex);
                }
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
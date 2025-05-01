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

        // Constants
        private const int MAX_TOOLTIP_LENGTH = 63; // NotifyIcon text limit

        // UI state tracking
        private string currentDisplayText;
        private string currentTooltipText;
        private bool currentErrorState;
        private bool currentTransitionState;
        private bool currentUseBlackText;
        private string currentBSSID;
        private string currentBand;
        private string currentSSID;

        public event EventHandler QuitRequested;

        public TrayIconManager(BuildInfoProvider buildInfoProvider, ILoggingService loggingService)
        {
            this.loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));

            // Direct dependency creation - violates Principle #7, consider DI/IoC later
            iconGenerator = new IconGenerator();
            knownAPManager = new KnownAPManager(loggingService);
            var startupManager = new StartupManager(); // Also created here for MenuManager

            trayIcon = new NotifyIcon
            {
                // Default icon shown during init
                Icon = iconGenerator.CreateNumberIcon("--", isError: true), // Use error state for init?
                Visible = true,
                Text = "Initializing..." // This initial text is short enough
            };

            // Initialize managers - Direct dependency creation violates Principle #7
            string logPath = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "PingApplet",
                    "ping.log"
                );
            menuManager = new MenuManager(
                buildInfoProvider,
                loggingService,
                startupManager, // Pass instance
                knownAPManager,
                logPath
            );

            notificationManager = new NotificationManager(trayIcon, loggingService);

            // Connect event handlers
            menuManager.QuitRequested += OnQuitRequested; // Use local handler for clarity

            // Set up tray icon context menu
            trayIcon.ContextMenuStrip = menuManager.GetContextMenu();
            loggingService.LogInfo("TrayIconManager initialized.");
        }

        private void OnQuitRequested(object sender, EventArgs e)
        {
            // Forward the event
            QuitRequested?.Invoke(this, EventArgs.Empty);
        }


        public void UpdateLocationServicesState(bool isDisabled)
        {
            if (isDisposed) return;
            try
            {
                menuManager.UpdateLocationServicesState(isDisabled);
            }
            catch (Exception ex)
            {
                loggingService.LogError("Error forwarding location services state to MenuManager", ex);
            }
        }

        public void ShowTransitionBalloon(string oldBssid, string newBssid)
        {
            if (isDisposed) return;

            try
            {
                // Fetch display names within this method's context
                var oldDisplayName = GetAPDisplayName(oldBssid);
                var newDisplayName = GetAPDisplayName(newBssid);
                notificationManager.IsEnabled = menuManager.NotificationsEnabled; // Ensure notification state matches menu
                notificationManager.ShowTransitionNotification(oldBssid, newBssid, oldDisplayName, newDisplayName);
            }
            catch (Exception ex)
            {
                loggingService.LogError($"Error showing transition balloon for {oldBssid} -> {newBssid}", ex);
            }
        }

        public void UpdateCurrentAP(string bssid, string band = null, string ssid = null)
        {
            if (isDisposed) return;

            try
            {
                currentBSSID = bssid;
                currentBand = band;
                currentSSID = ssid;
                string displayName = "Not Connected"; // Default

                if (!string.IsNullOrEmpty(bssid))
                {
                    // Add or update details first
                    knownAPManager.UpdateAPDetails(bssid, band, ssid);

                    // Then get the potentially updated display name
                    displayName = knownAPManager.GetDisplayName(bssid); // Uses formatter internally

                    // If display name is still just BSSID, ensure it's known (idempotent)
                    if (displayName.Equals(bssid, StringComparison.OrdinalIgnoreCase))
                    {
                        knownAPManager.AddNewAP(bssid); // Ensures it's in the list if details updated a new one
                    }
                }

                // Update the menu display
                menuManager.UpdateCurrentAP(displayName);
            }
            catch (Exception ex)
            {
                loggingService.LogError($"Error updating current AP to {bssid} ({ssid}/{band})", ex);
            }
        }

        public void UpdateIcon(string displayText, string tooltipText, bool isError = false, bool isTransition = false, bool useBlackText = false)
        {
            if (isDisposed) return;

            // Store intended state before potential modification
            currentDisplayText = displayText;
            currentTooltipText = tooltipText ?? ""; // Ensure not null
            currentErrorState = isError;
            currentTransitionState = isTransition;
            currentUseBlackText = useBlackText;

            Icon newIcon = null;
            Icon oldIcon = null;

            try
            {
                // Generate the appropriate icon
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

                // --- MODIFICATION START: Truncate Tooltip ---
                string truncatedTooltip = currentTooltipText;
                if (currentTooltipText.Length > MAX_TOOLTIP_LENGTH)
                {
                    truncatedTooltip = currentTooltipText.Substring(0, MAX_TOOLTIP_LENGTH);
                    // Log truncation only once if it happens repeatedly for the same text
                    if (truncatedTooltip != trayIcon.Text) // Avoid log spam (check against current actual text)
                    {
                        // *** CORRECTION: Use LogInfo instead of LogWarning ***
                        loggingService.LogInfo($"[WARN] Tooltip text truncated. Original length: {currentTooltipText.Length}. Original: '{currentTooltipText}'. Truncated: '{truncatedTooltip}'");
                    }
                    // Optionally add ellipsis or indicator? For now, just truncate.
                    // truncatedTooltip += "..."; // This would exceed the limit again if MAX_TOOLTIP_LENGTH is hit exactly
                }
                // --- MODIFICATION END ---

                // Safely update the NotifyIcon
                oldIcon = trayIcon.Icon; // Store old icon reference
                trayIcon.Icon = newIcon; // Assign new icon
                trayIcon.Text = truncatedTooltip; // Assign truncated tooltip text

                // Dispose the old icon *after* successfully assigning the new one
                oldIcon?.Dispose();
                oldIcon = null; // Prevent double disposal in finally block
            }
            catch (Exception ex)
            {
                loggingService.LogError($"Error updating icon. Display: '{displayText}', Tooltip: '{currentTooltipText}', Error: {isError}, Transition: {isTransition}", ex);
                // Clean up potentially created icon if an error occurred during assignment
                newIcon?.Dispose();
                // Also clean up old icon if assignment failed after getting reference
                oldIcon?.Dispose();
                // Don't re-throw? Or handle specific exceptions? For now, log and continue.
                // throw; // Re-throwing might crash the app if called repeatedly from event handlers
            }
        }


        public string GetAPDisplayName(string bssid)
        {
            // No need to check isDisposed here, KnownAPManager handles it
            try
            {
                return knownAPManager.GetDisplayName(bssid);
            }
            catch (ObjectDisposedException)
            {
                // If KnownAPManager is disposed during shutdown, return gracefully
                return bssid ?? "Disposed";
            }
            catch (Exception ex)
            {
                // Log other errors getting the name
                loggingService.LogError($"Error getting AP display name for BSSID: {bssid}", ex);
                return bssid ?? "Error"; // Fallback
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!isDisposed)
            {
                if (disposing)
                {
                    loggingService.LogInfo("Disposing TrayIconManager...");
                    try
                    {
                        // Unsubscribe events first
                        if (menuManager != null)
                        {
                            menuManager.QuitRequested -= OnQuitRequested;
                        }

                        // Dispose managed resources safely
                        trayIcon?.Dispose(); // Dispose NotifyIcon first to remove from tray
                        menuManager?.Dispose();
                        iconGenerator?.Dispose();
                        knownAPManager?.Dispose();
                        // NotificationManager doesn't implement IDisposable currently
                        // startupManager is owned by constructor scope, not this class

                        loggingService.LogInfo("TrayIconManager disposed.");
                    }
                    catch (Exception ex)
                    {
                        // Use safe logging access during disposal
                        loggingService?.LogError("Error during TrayIconManager disposal", ex);
                    }
                }
                // Mark as disposed regardless of whether it was a managed disposal or not
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
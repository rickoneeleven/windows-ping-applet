using System;
using System.Drawing;
using System.Windows.Forms;
using ping_applet.Core.Interfaces;
using ping_applet.Utils;
using System.Diagnostics;

namespace ping_applet.UI
{
    public class TrayIconManager : IDisposable
    {
        private readonly NotifyIcon trayIcon;
        private readonly IconGenerator iconGenerator;
        private readonly MenuManager menuManager;
        private readonly NotificationManager notificationManager;
        private readonly KnownAPManager knownAPManager;
        private readonly ILoggingService loggingService;
        private bool isDisposed;

        private const int MAX_TOOLTIP_LENGTH = 63;
        private string currentBSSID;

        // Existing Event
        public event EventHandler QuitRequested;

        // --- NEW Public Events for Ping Target Control ---
        public event EventHandler SetDefaultGatewayTargetRequested;
        public event EventHandler<string> ActivateExistingCustomTargetRequested; // string is the custom host
        public event EventHandler ShowSetCustomTargetDialogRequested;
        // --- End NEW Public Events ---

        public bool IsDisposed => isDisposed;

        public TrayIconManager(BuildInfoProvider buildInfoProvider, ILoggingService loggingService)
        {
            this.loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
            this.loggingService.LogInfo("[TrayIconManager] Initializing...");

            this.knownAPManager = new KnownAPManager(this.loggingService);
            this.iconGenerator = new IconGenerator();
            var startupManager = new StartupManager();

            this.trayIcon = new NotifyIcon
            {
                Icon = iconGenerator.CreateNumberIcon("--", isError: true),
                Visible = true,
                Text = "Initializing..."
            };

            string logFilePath = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PingApplet",
                "ping.log"
            );
            this.menuManager = new MenuManager(
                buildInfoProvider,
                this.loggingService,
                startupManager,
                this.knownAPManager,
                logFilePath
            );

            this.notificationManager = new NotificationManager(this.trayIcon, this.loggingService);

            // Subscribe to existing and new events from MenuManager
            this.menuManager.QuitRequested += MenuManager_QuitRequested;
            this.menuManager.RequestSetDefaultGatewayTarget += MenuManager_RequestSetDefaultGatewayTarget;
            this.menuManager.RequestActivateExistingCustomTarget += MenuManager_RequestActivateExistingCustomTarget;
            this.menuManager.RequestShowSetCustomTargetDialog += MenuManager_RequestShowSetCustomTargetDialog;

            this.trayIcon.ContextMenuStrip = this.menuManager.GetContextMenu();
            this.loggingService.LogInfo("[TrayIconManager] Initialized successfully and subscribed to MenuManager events.");
        }

        // --- NEW Event Handlers for MenuManager's Ping Target Events ---
        private void MenuManager_RequestSetDefaultGatewayTarget(object sender, EventArgs e)
        {
            loggingService.LogInfo("[TrayIconManager] Relaying 'RequestSetDefaultGatewayTarget' event.");
            SetDefaultGatewayTargetRequested?.Invoke(this, EventArgs.Empty);
        }

        private void MenuManager_RequestActivateExistingCustomTarget(object sender, string customHost)
        {
            loggingService.LogInfo($"[TrayIconManager] Relaying 'RequestActivateExistingCustomTarget' event for host: {customHost}.");
            ActivateExistingCustomTargetRequested?.Invoke(this, customHost);
        }

        private void MenuManager_RequestShowSetCustomTargetDialog(object sender, EventArgs e)
        {
            loggingService.LogInfo("[TrayIconManager] Relaying 'RequestShowSetCustomTargetDialog' event.");
            ShowSetCustomTargetDialogRequested?.Invoke(this, EventArgs.Empty);
        }
        // --- End NEW Event Handlers ---

        private void MenuManager_QuitRequested(object sender, EventArgs e)
        {
            // Forward the event
            QuitRequested?.Invoke(this, EventArgs.Empty);
        }

        // --- NEW Public method for AppController to update menu display ---
        public void UpdatePingTargetDisplayInMenu(bool isCustomHostActive, string gatewayDisplayValue, string customHostDisplayValue)
        {
            if (isDisposed) return;
            try
            {
                loggingService.LogInfo($"[TrayIconManager] Calling MenuManager to update ping target display. CustomActive: {isCustomHostActive}, Gateway: {gatewayDisplayValue}, Custom: {customHostDisplayValue}");
                menuManager.UpdatePingTargetDisplay(isCustomHostActive, gatewayDisplayValue, customHostDisplayValue);
            }
            catch (Exception ex)
            {
                loggingService.LogError("[TrayIconManager] Error calling MenuManager.UpdatePingTargetDisplay.", ex);
            }
        }
        // --- End NEW Public method ---

        public void UpdateLocationServicesState(bool isDisabled)
        {
            if (isDisposed) return;
            try { menuManager.UpdateLocationServicesState(isDisabled); }
            catch (Exception ex) { loggingService.LogError("Error forwarding location services state to MenuManager", ex); }
        }

        public void ShowTransitionBalloon(string oldBssid, string newBssid)
        {
            if (isDisposed) return;
            try
            {
                var oldDisplayName = GetAPDisplayName(oldBssid);
                var newDisplayName = GetAPDisplayName(newBssid);
                notificationManager.IsEnabled = menuManager.NotificationsEnabled;
                notificationManager.ShowTransitionNotification(oldBssid, newBssid, oldDisplayName, newDisplayName);
            }
            catch (Exception ex) { loggingService.LogError($"Error showing transition balloon for {oldBssid} -> {newBssid}", ex); }
        }

        public void UpdateCurrentAP(string bssid, string band = null, string ssid = null)
        {
            if (isDisposed) return;
            try
            {
                currentBSSID = bssid;
                string displayName = "Not Connected";
                if (!string.IsNullOrEmpty(bssid))
                {
                    knownAPManager.UpdateAPDetails(bssid, band, ssid);
                    displayName = knownAPManager.GetDisplayName(bssid);
                    if (displayName.Equals(bssid, StringComparison.OrdinalIgnoreCase))
                    {
                        knownAPManager.AddNewAP(bssid);
                    }
                }
                menuManager.UpdateCurrentAP(displayName);
            }
            catch (Exception ex) { loggingService.LogError($"Error updating current AP to {bssid} ({ssid}/{band})", ex); }
        }

        public void UpdateIcon(string displayText, string tooltipText, bool isError = false, bool isTransition = false, bool useBlackText = false)
        {
            if (isDisposed) return;
            Icon newIcon = null;
            Icon oldIcon = null;
            try
            {
                if (isTransition)
                {
                    newIcon = useBlackText ? iconGenerator.CreateTransitionIconWithBlackText(displayText) : iconGenerator.CreateTransitionIcon(displayText);
                }
                else
                {
                    newIcon = iconGenerator.CreateNumberIcon(displayText, isError);
                }
                string truncatedTooltip = tooltipText ?? "";
                if (truncatedTooltip.Length > MAX_TOOLTIP_LENGTH)
                {
                    truncatedTooltip = truncatedTooltip.Substring(0, MAX_TOOLTIP_LENGTH);
                }
                oldIcon = trayIcon.Icon;
                trayIcon.Icon = newIcon;
                trayIcon.Text = truncatedTooltip;
                oldIcon?.Dispose();
                oldIcon = null;
            }
            catch (Exception ex)
            {
                loggingService.LogError($"Error updating icon. Display: '{displayText}', Tooltip: '{tooltipText ?? "null"}', Error: {isError}, Transition: {isTransition}", ex);
                newIcon?.Dispose();
                oldIcon?.Dispose();
            }
        }

        public string GetAPDisplayName(string bssid)
        {
            if (isDisposed) return bssid ?? "Disposed";
            try { return knownAPManager.GetDisplayName(bssid); }
            catch (ObjectDisposedException) { return bssid ?? "Disposed (internal KAM)"; }
            catch (Exception ex)
            {
                loggingService.LogError($"[TrayIconManager] Error getting AP display name for BSSID: {bssid}", ex);
                return bssid ?? "Error";
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!isDisposed)
            {
                if (disposing)
                {
                    loggingService?.LogInfo("[TrayIconManager] Disposing...");
                    if (menuManager != null)
                    {
                        // Unsubscribe from all MenuManager events
                        menuManager.QuitRequested -= MenuManager_QuitRequested;
                        menuManager.RequestSetDefaultGatewayTarget -= MenuManager_RequestSetDefaultGatewayTarget;
                        menuManager.RequestActivateExistingCustomTarget -= MenuManager_RequestActivateExistingCustomTarget;
                        menuManager.RequestShowSetCustomTargetDialog -= MenuManager_RequestShowSetCustomTargetDialog;
                        loggingService?.LogInfo("[TrayIconManager] Unsubscribed from MenuManager events.");
                    }

                    SafelyDispose(trayIcon, nameof(trayIcon));
                    SafelyDispose(menuManager, nameof(menuManager));
                    SafelyDispose(iconGenerator, nameof(iconGenerator));
                    SafelyDispose(knownAPManager, nameof(knownAPManager));
                    loggingService?.LogInfo("[TrayIconManager] Disposed.");
                }
                isDisposed = true;
            }
        }

        private void SafelyDispose(IDisposable resource, string resourceName)
        {
            if (resource != null)
            {
                try
                {
                    resource.Dispose();
                    Debug.WriteLine($"[TrayIconManager] Dispose: {resourceName} disposed successfully.");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[TrayIconManager] Dispose: Error disposing {resourceName}: {ex.Message}");
                }
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}
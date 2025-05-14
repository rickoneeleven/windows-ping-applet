using System;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
using System.Timers;
using ping_applet.Core.Interfaces;
using ping_applet.Services;
using ping_applet.UI;
using static ping_applet.Services.NetworkStateManager; // Assuming BssidChangeEventArgs is here
using System.Diagnostics; // For Debug.WriteLine

namespace ping_applet.Controllers
{
    public class AppController : IDisposable
    {
        private readonly INetworkMonitor networkMonitor;
        private readonly IPingService pingService;
        private readonly ILoggingService loggingService;
        private readonly TrayIconManager trayIconManager;
        private readonly NetworkStateManager networkStateManager;
        private readonly Timer bssidResetTimer;

        private bool isDisposed;
        private bool isInBssidTransition;
        private string currentDisplayText;

        private const int PING_INTERVAL = 1000; // 1 second
        private const int PING_TIMEOUT = 1000;  // 1 second timeout
        private const int BSSID_TRANSITION_WINDOW = 10000; // 10 seconds

        public event EventHandler ApplicationExit;

        public AppController(
            INetworkMonitor networkMonitor,
            ILoggingService loggingService,
            TrayIconManager trayIconManager)
        {
            // Services injected via constructor (good)
            this.networkMonitor = networkMonitor ?? throw new ArgumentNullException(nameof(networkMonitor));
            this.loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
            this.trayIconManager = trayIconManager ?? throw new ArgumentNullException(nameof(trayIconManager));

            // Direct instantiation of dependencies - this violates Principle #7
            // To be addressed if this class is refactored for dependency injection later.
            // For now, we focus on the disposal order as per PRD.
            this.pingService = new PingService(this.networkMonitor); // PingService depends on INetworkMonitor
            this.networkStateManager = new NetworkStateManager(this.loggingService); // NetworkStateManager depends on ILoggingService

            this.bssidResetTimer = new Timer(BSSID_TRANSITION_WINDOW)
            {
                AutoReset = false
            };

            // Subscribe to events
            SubscribeToEvents();
            Debug.WriteLine("[AppController] AppController instantiated and events subscribed.");
        }

        private void SubscribeToEvents()
        {
            this.bssidResetTimer.Elapsed += BssidResetTimer_Elapsed;
            this.networkMonitor.GatewayChanged += NetworkMonitor_GatewayChanged;
            this.networkMonitor.NetworkAvailabilityChanged += NetworkMonitor_NetworkAvailabilityChanged;
            this.pingService.PingCompleted += PingService_PingCompleted;
            this.pingService.PingError += PingService_PingError;
            this.trayIconManager.QuitRequested += OnQuitRequested; // Renamed for clarity
            this.networkStateManager.BssidChanged += NetworkStateManager_BssidChanged;
            this.networkStateManager.LocationServicesStateChanged += NetworkStateManager_LocationServicesStateChanged;
        }

        private void UnsubscribeFromEvents()
        {
            Debug.WriteLine("[AppController] Unsubscribing from events.");
            this.bssidResetTimer.Elapsed -= BssidResetTimer_Elapsed;
            this.networkMonitor.GatewayChanged -= NetworkMonitor_GatewayChanged;
            this.networkMonitor.NetworkAvailabilityChanged -= NetworkMonitor_NetworkAvailabilityChanged;
            this.pingService.PingCompleted -= PingService_PingCompleted;
            this.pingService.PingError -= PingService_PingError;
            this.trayIconManager.QuitRequested -= OnQuitRequested;
            this.networkStateManager.BssidChanged -= NetworkStateManager_BssidChanged;
            this.networkStateManager.LocationServicesStateChanged -= NetworkStateManager_LocationServicesStateChanged;
        }

        private void OnQuitRequested(object sender, EventArgs e)
        {
            if (isDisposed) return;
            Debug.WriteLine("[AppController] QuitRequested event received from TrayIconManager.");
            ApplicationExit?.Invoke(this, EventArgs.Empty);
        }

        private void NetworkStateManager_LocationServicesStateChanged(object sender, bool isEnabled)
        {
            if (isDisposed) return;
            try
            {
                trayIconManager.UpdateLocationServicesState(!isEnabled);
                if (!isEnabled)
                {
                    loggingService.LogInfo("Location services are disabled - AP tracking will not work");
                }
                else
                {
                    loggingService.LogInfo("Location services are now enabled - AP tracking resumed");
                }
            }
            catch (Exception ex)
            {
                loggingService.LogError("Error handling location services state change", ex);
            }
        }

        public async Task InitializeAsync()
        {
            if (isDisposed) throw new ObjectDisposedException(nameof(AppController));
            try
            {
                loggingService.LogInfo("Application starting up. AppController initializing.");
                await networkMonitor.InitializeAsync();
                await networkStateManager.StartMonitoring();
                pingService.StartPingTimer(PING_INTERVAL);

                if (!string.IsNullOrEmpty(networkMonitor.CurrentGateway))
                {
                    await pingService.SendPingAsync(networkMonitor.CurrentGateway, PING_TIMEOUT);
                }
                loggingService.LogInfo("AppController initialization complete.");
            }
            catch (Exception ex)
            {
                loggingService.LogError("AppController initialization error", ex);
                ShowErrorState("INIT!");
                throw; // Re-throw to allow MainForm to catch and handle.
            }
        }

        private void NetworkStateManager_BssidChanged(object sender, BssidChangeEventArgs e)
        {
            if (isDisposed) return;
            try
            {
                isInBssidTransition = true;
                bssidResetTimer.Stop();
                bssidResetTimer.Start();

                trayIconManager.UpdateCurrentAP(
                    e.NewBssid,
                    networkStateManager.CurrentBand,
                    networkStateManager.CurrentSsid
                );

                trayIconManager.ShowTransitionBalloon(e.OldBssid, e.NewBssid);

                if (!string.IsNullOrEmpty(currentDisplayText))
                {
                    string tooltipText = FormatTooltip(networkMonitor.CurrentGateway, e.NewBssid);
                    trayIconManager.UpdateIcon(
                        currentDisplayText,
                        tooltipText,
                        false,
                        true, // isTransition
                        true  // useBlackText for transition
                    );
                }
                loggingService.LogInfo($"BSSID changed from {e.OldBssid ?? "None"} to {e.NewBssid ?? "None"}. Transition state active.");
            }
            catch (Exception ex)
            {
                loggingService.LogError("Error handling BSSID change", ex);
            }
        }

        private void BssidResetTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            if (isDisposed) return;
            try
            {
                isInBssidTransition = false;
                loggingService.LogInfo("BSSID transition window ended.");
                if (!string.IsNullOrEmpty(networkMonitor.CurrentGateway))
                {
                    // Fire and forget a ping to refresh the display with normal colors.
                    _ = pingService.SendPingAsync(networkMonitor.CurrentGateway, PING_TIMEOUT);
                }
            }
            catch (Exception ex)
            {
                loggingService.LogError("Error handling BSSID reset timer elapsed", ex);
            }
        }

        private async void NetworkMonitor_GatewayChanged(object sender, string newGateway)
        {
            if (isDisposed) return;
            try
            {
                if (string.IsNullOrEmpty(newGateway))
                {
                    ShowErrorState("GW?");
                    trayIconManager.UpdateCurrentAP(null, null, null);
                    loggingService.LogInfo("Gateway became unavailable.");
                }
                else
                {
                    loggingService.LogInfo($"Gateway changed to: {newGateway}. Pinging new gateway.");
                    await pingService.SendPingAsync(newGateway, PING_TIMEOUT);
                }
            }
            catch (Exception ex)
            {
                loggingService.LogError($"Error handling gateway change to '{newGateway}'", ex);
            }
        }

        private void NetworkMonitor_NetworkAvailabilityChanged(object sender, bool isAvailable)
        {
            if (isDisposed) return;
            try
            {
                if (!isAvailable)
                {
                    loggingService.LogInfo("Network became unavailable.");
                    trayIconManager.UpdateCurrentAP(null, null, null);
                    ShowErrorState("OFF");
                }
                else
                {
                    loggingService.LogInfo("Network became available.");
                    // Gateway change event will likely handle the ping logic if gateway also changes.
                }
            }
            catch (Exception ex)
            {
                loggingService.LogError($"Error handling network availability change (isAvailable: {isAvailable})", ex);
            }
        }

        private void PingService_PingCompleted(object sender, PingReply reply)
        {
            if (isDisposed || reply == null) return;

            try
            {
                string gateway = networkMonitor.CurrentGateway; // Cache for consistent logging
                string bssid = networkStateManager.CurrentBssid; // Cache for consistent logging

                if (reply.Status == IPStatus.Success)
                {
                    currentDisplayText = reply.RoundtripTime.ToString();
                    string tooltipText = FormatTooltip(gateway, bssid);

                    trayIconManager.UpdateIcon(
                        currentDisplayText,
                        tooltipText,
                        false, // isError
                        isInBssidTransition,
                        isInBssidTransition  // useBlackText for transition
                    );

                    if (!isInBssidTransition)
                    {
                        // Avoid logging every successful ping during normal transition state updates.
                        // Log only if not in BSSID transition, to reduce log spam.
                        loggingService.LogInfo($"Ping successful - Target: {gateway}, Time: {reply.RoundtripTime}ms, BSSID: {bssid ?? "N/A"}");
                    }
                }
                else
                {
                    currentDisplayText = "X"; // Keep currentDisplayText updated for error states too
                    string tooltipText = FormatTooltip(gateway, bssid);
                    trayIconManager.UpdateIcon(currentDisplayText, tooltipText, true, false, false);
                    loggingService.LogInfo($"Ping failed - Target: {gateway}, Status: {reply.Status}, BSSID: {bssid ?? "N/A"}");
                }
            }
            catch (Exception ex)
            {
                loggingService.LogError("Error handling ping reply", ex);
            }
        }

        private void PingService_PingError(object sender, Exception ex)
        {
            if (isDisposed) return;
            try
            {
                loggingService.LogError($"Ping error to {networkMonitor.CurrentGateway}", ex);
                ShowErrorState("!");
            }
            catch (Exception logEx)
            {
                Debug.WriteLine($"[AppController] Failed to log PingService_PingError: {logEx.Message}");
            }
        }

        private void ShowErrorState(string errorText)
        {
            if (isDisposed) return;
            try
            {
                currentDisplayText = errorText;
                string tooltipText = FormatTooltip(networkMonitor.CurrentGateway, networkStateManager.CurrentBssid);
                trayIconManager.UpdateIcon(errorText, tooltipText, true, false, false);
            }
            catch (Exception ex)
            {
                // If trayIconManager itself fails, log via loggingService if possible,
                // or fallback to Debug.WriteLine if loggingService is the issue.
                try
                {
                    loggingService.LogError($"Error displaying error state '{errorText}'", ex);
                }
                catch (Exception finalEx)
                {
                    Debug.WriteLine($"[AppController] CRITICAL: Failed to display error state AND failed to log it: {finalEx.Message}");
                }
            }
        }

        private string FormatTooltip(string gateway, string bssid)
        {
            if (isDisposed) return "App Disposed";
            var gwText = string.IsNullOrEmpty(gateway) ? "GW: Not Connected" : $"GW: {gateway}";
            string apDisplay = "AP: N/A"; // Default if bssid is null or trayIconManager not available

            if (!string.IsNullOrEmpty(bssid) && trayIconManager != null)
            {
                // trayIconManager may be disposed before AppController in some shutdown paths,
                // though AppController.Dispose *should* handle trayIconManager.Dispose().
                // This check is defensive.
                try
                {
                    apDisplay = $"AP: {trayIconManager.GetAPDisplayName(bssid)}";
                }
                catch (ObjectDisposedException)
                {
                    apDisplay = "AP: (mgr disposed)";
                }
                catch (Exception ex)
                {
                    apDisplay = "AP: (error)";
                    Debug.WriteLine($"[AppController] Error formatting tooltip (GetAPDisplayName): {ex.Message}");
                }
            }
            return $"{gwText}\n{apDisplay}";
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!isDisposed)
            {
                if (disposing)
                {
                    // PRD AC2.1.AC4: Log final shutdown message *before* loggingService is disposed.
                    // Ensure loggingService is still valid here.
                    try
                    {
                        loggingService?.LogInfo("AppController: PingApplet shutting down. Disposing resources.");
                    }
                    catch (Exception ex)
                    {
                        // If loggingService itself is faulty at this point, use Debug.
                        Debug.WriteLine($"[AppController] Dispose: Failed to write final log message: {ex.Message}");
                    }

                    Debug.WriteLine("[AppController] Dispose(true): Starting resource disposal.");

                    // 1. Unsubscribe from all events to prevent handlers from firing on disposed objects.
                    UnsubscribeFromEvents();

                    // 2. Dispose timers.
                    try
                    {
                        bssidResetTimer?.Stop();
                        bssidResetTimer?.Dispose();
                        Debug.WriteLine("[AppController] Dispose: bssidResetTimer disposed.");
                    }
                    catch (Exception ex) { Debug.WriteLine($"[AppController] Dispose: Error disposing bssidResetTimer: {ex.Message}"); }

                    // 3. Dispose services. Order can be important.
                    // Services that depend on others should be disposed before their dependencies if possible,
                    // or in an order that minimizes issues.
                    // PingService depends on NetworkMonitor.
                    // NetworkStateManager depends on LoggingService.
                    // TrayIconManager holds other UI elements and KnownAPManager (which uses LoggingService).

                    SafelyDispose(pingService, nameof(pingService));
                    SafelyDispose(networkStateManager, nameof(networkStateManager)); // Uses loggingService
                    SafelyDispose(networkMonitor, nameof(networkMonitor));
                    SafelyDispose(trayIconManager, nameof(trayIconManager));     // Uses loggingService

                    // 4. Dispose LoggingService LAST.
                    SafelyDispose(loggingService, nameof(loggingService), true); // Mark as last one for special debug message

                    Debug.WriteLine("[AppController] Dispose(true): All managed resources have been processed for disposal.");
                }
                isDisposed = true;
            }
        }

        private void SafelyDispose(IDisposable resource, string resourceName, bool isLastResource = false)
        {
            if (resource != null)
            {
                try
                {
                    resource.Dispose();
                    Debug.WriteLine($"[AppController] Dispose: {resourceName} disposed successfully.");
                }
                catch (Exception ex)
                {
                    // If loggingService is the one being disposed or already gone, this log won't work.
                    // Fallback to Debug.WriteLine for errors during disposal.
                    Debug.WriteLine($"[AppController] Dispose: Error disposing {resourceName}: {ex.Message}{Environment.NewLine}Stack Trace: {ex.StackTrace}");
                    if (resource is ILoggingService && !isLastResource)
                    {
                        Debug.WriteLine($"[AppController] CRITICAL: LoggingService threw an error during its disposal but was not the last item. Subsequent logs may fail.");
                    }
                }
            }
            else
            {
                Debug.WriteLine($"[AppController] Dispose: {resourceName} was null, no disposal needed.");
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}
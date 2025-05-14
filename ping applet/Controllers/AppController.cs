using System;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading.Tasks;
// using System.Timers; // No longer needed as we will fully qualify
using ping_applet.Core.Interfaces;
using ping_applet.Services;
using ping_applet.UI;
using ping_applet.Utils;
using static ping_applet.Services.NetworkStateManager;
using System.Diagnostics;
using System.Linq;
using ping_applet.Forms;
using System.Windows.Forms; // For DialogResult and MessageBox

namespace ping_applet.Controllers
{
    public class AppController : IDisposable
    {
        private enum PingTargetType { DefaultGateway, CustomHost }

        private readonly INetworkMonitor networkMonitor;
        private readonly IPingService pingService;
        private readonly ILoggingService loggingService;
        private readonly TrayIconManager trayIconManager;
        private readonly NetworkStateManager networkStateManager;
        private readonly KnownAPManager knownAPManager;
        // Fully qualify Timer to System.Timers.Timer
        private readonly System.Timers.Timer bssidResetTimer;
        private readonly System.Timers.Timer periodicPingTimer;

        private bool isDisposed;
        private bool isInBssidTransition;
        private string currentIconDisplayText = "--";

        private PingTargetType activePingTargetType = PingTargetType.DefaultGateway;
        private string customPingTargetUserInput;
        private string currentResolvedPingAddress;
        private bool isCustomHostResolutionError;

        private const int PING_INTERVAL_MS = 1000;
        private const int PING_TIMEOUT_MS = 1000;
        private const int BSSID_TRANSITION_WINDOW_MS = 10000;

        public event EventHandler ApplicationExit;

        public AppController(
            INetworkMonitor networkMonitor,
            ILoggingService loggingService,
            TrayIconManager trayIconManager,
            KnownAPManager knownAPManager)
        {
            this.networkMonitor = networkMonitor ?? throw new ArgumentNullException(nameof(networkMonitor));
            this.loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
            this.trayIconManager = trayIconManager ?? throw new ArgumentNullException(nameof(trayIconManager));
            this.knownAPManager = knownAPManager ?? throw new ArgumentNullException(nameof(knownAPManager));
            this.pingService = new PingService(this.networkMonitor);
            this.networkStateManager = new NetworkStateManager(this.loggingService);
            // Fully qualify Timer instantiation
            this.bssidResetTimer = new System.Timers.Timer(BSSID_TRANSITION_WINDOW_MS) { AutoReset = false };
            this.periodicPingTimer = new System.Timers.Timer(PING_INTERVAL_MS) { AutoReset = true };
            SubscribeToEvents();
            Debug.WriteLine("[AppController] AppController instantiated and events subscribed.");
        }

        private void SubscribeToEvents()
        {
            this.bssidResetTimer.Elapsed += BssidResetTimer_Elapsed;
            this.periodicPingTimer.Elapsed += PeriodicPingTimer_Elapsed;
            this.networkMonitor.GatewayChanged += NetworkMonitor_GatewayChanged;
            this.networkMonitor.NetworkAvailabilityChanged += NetworkMonitor_NetworkAvailabilityChanged;
            this.pingService.PingCompleted += PingService_PingCompleted;
            this.pingService.PingError += PingService_PingError;
            this.trayIconManager.QuitRequested += OnQuitRequested;
            this.trayIconManager.SetDefaultGatewayTargetRequested += TrayIconManager_SetDefaultGatewayTargetRequested;
            this.trayIconManager.ActivateExistingCustomTargetRequested += TrayIconManager_ActivateExistingCustomTargetRequested;
            this.trayIconManager.ShowSetCustomTargetDialogRequested += TrayIconManager_ShowSetCustomTargetDialogRequested;
            this.networkStateManager.BssidChanged += NetworkStateManager_BssidChanged;
            this.networkStateManager.LocationServicesStateChanged += NetworkStateManager_LocationServicesStateChanged;
        }

        private void UnsubscribeFromEvents()
        {
            Debug.WriteLine("[AppController] Unsubscribing from events.");
            if (bssidResetTimer != null) bssidResetTimer.Elapsed -= BssidResetTimer_Elapsed;
            if (periodicPingTimer != null) periodicPingTimer.Elapsed -= PeriodicPingTimer_Elapsed;
            if (networkMonitor != null) { networkMonitor.GatewayChanged -= NetworkMonitor_GatewayChanged; networkMonitor.NetworkAvailabilityChanged -= NetworkMonitor_NetworkAvailabilityChanged; }
            if (pingService != null) { pingService.PingCompleted -= PingService_PingCompleted; pingService.PingError -= PingService_PingError; }
            if (trayIconManager != null) { trayIconManager.QuitRequested -= OnQuitRequested; trayIconManager.SetDefaultGatewayTargetRequested -= TrayIconManager_SetDefaultGatewayTargetRequested; trayIconManager.ActivateExistingCustomTargetRequested -= TrayIconManager_ActivateExistingCustomTargetRequested; trayIconManager.ShowSetCustomTargetDialogRequested -= TrayIconManager_ShowSetCustomTargetDialogRequested; }
            if (networkStateManager != null) { networkStateManager.BssidChanged -= NetworkStateManager_BssidChanged; networkStateManager.LocationServicesStateChanged -= NetworkStateManager_LocationServicesStateChanged; }
        }

        private async void TrayIconManager_SetDefaultGatewayTargetRequested(object sender, EventArgs e) { if (isDisposed) return; loggingService.LogInfo("[AppController] Request to set ping target to Default Gateway."); await SetPingTargetToDefaultGatewayAsync(); }
        private async void TrayIconManager_ActivateExistingCustomTargetRequested(object sender, string host) { if (isDisposed) return; loggingService.LogInfo($"[AppController] Request to activate existing custom ping target: {host}."); await SetPingTargetToCustomHostAsync(host); }

        private async void TrayIconManager_ShowSetCustomTargetDialogRequested(object sender, EventArgs e)
        {
            if (isDisposed) return;
            loggingService.LogInfo("[AppController] Received request to show 'Set Custom Ping Target' dialog.");
            try
            {
                using (var dialog = new SetCustomTargetForm(this.customPingTargetUserInput))
                {
                    DialogResult result = dialog.ShowDialog();
                    if (result == DialogResult.OK)
                    {
                        string newTarget = dialog.TargetHost;
                        loggingService.LogInfo($"[AppController] 'Set Custom Ping Target' dialog OK. New target: {newTarget}");
                        if (!string.IsNullOrWhiteSpace(newTarget)) { await SetPingTargetToCustomHostAsync(newTarget); }
                        else { loggingService.LogInfo("[AppController] 'Set Custom Ping Target' dialog returned OK but with an empty target. No change made."); }
                    }
                    else { loggingService.LogInfo($"[AppController] 'Set Custom Ping Target' dialog Cancelled or closed. Result: {result}"); }
                }
            }
            catch (Exception ex)
            {
                loggingService.LogError("[AppController] Error showing or processing 'Set Custom Ping Target' dialog.", ex);
                MessageBox.Show("Error opening the custom target dialog. Check logs for details.", "Dialog Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void UpdateMenuPingTargetDisplay()
        {
            if (isDisposed || trayIconManager == null || trayIconManager.IsDisposed) return;
            string gwDisplay = networkMonitor.CurrentGateway;
            string customDisplay = customPingTargetUserInput;
            trayIconManager.UpdatePingTargetDisplayInMenu(activePingTargetType == PingTargetType.CustomHost, gwDisplay, customDisplay);
        }

        // Event handler for System.Timers.Timer.Elapsed
        private async void PeriodicPingTimer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            if (isDisposed || string.IsNullOrEmpty(currentResolvedPingAddress)) return;
            try { await pingService.SendPingAsync(currentResolvedPingAddress, PING_TIMEOUT_MS); }
            catch (Exception ex) { loggingService.LogError($"Error in periodic ping to {currentResolvedPingAddress}", ex); }
        }

        public async Task InitializeAsync()
        {
            if (isDisposed) throw new ObjectDisposedException(nameof(AppController));
            try
            {
                loggingService.LogInfo("AppController initializing.");
                await networkMonitor.InitializeAsync();
                await networkStateManager.StartMonitoring();
                customPingTargetUserInput = knownAPManager.GetLastCustomPingTarget();
                if (!string.IsNullOrEmpty(customPingTargetUserInput))
                {
                    loggingService.LogInfo($"Found last custom ping target input: {customPingTargetUserInput}. Attempting to set.");
                    await SetPingTargetToCustomHostAsync(customPingTargetUserInput, isInitialLoad: true);
                }
                else
                {
                    loggingService.LogInfo("No last custom ping target. Defaulting to gateway.");
                    currentResolvedPingAddress = networkMonitor.CurrentGateway;
                    activePingTargetType = PingTargetType.DefaultGateway;
                    if (string.IsNullOrEmpty(currentResolvedPingAddress)) ShowErrorState("GW?");
                }
                LogActivePingTarget();
                UpdateMenuPingTargetDisplay();
                periodicPingTimer.Start();
                loggingService.LogInfo($"AppController init complete. Pings started for: {currentResolvedPingAddress ?? "None"}.");
                if (!string.IsNullOrEmpty(currentResolvedPingAddress)) { await pingService.SendPingAsync(currentResolvedPingAddress, PING_TIMEOUT_MS); }
            }
            catch (Exception ex) { loggingService.LogError("AppController init error", ex); ShowErrorState("INIT!"); throw; }
        }

        public async Task SetPingTargetToDefaultGatewayAsync()
        {
            if (isDisposed) return;
            loggingService.LogInfo("Setting ping target to Default Gateway.");
            activePingTargetType = PingTargetType.DefaultGateway;
            customPingTargetUserInput = null;
            isCustomHostResolutionError = false;
            currentResolvedPingAddress = networkMonitor.CurrentGateway;
            knownAPManager.SetLastCustomPingTarget(null);
            LogActivePingTarget();
            UpdateMenuPingTargetDisplay();
            if (!string.IsNullOrEmpty(currentResolvedPingAddress)) { await pingService.SendPingAsync(currentResolvedPingAddress, PING_TIMEOUT_MS); }
            else { ShowErrorState("GW?"); loggingService.LogInfo("Default Gateway unavailable."); }
        }

        public async Task SetPingTargetToCustomHostAsync(string hostInput, bool isInitialLoad = false)
        {
            if (isDisposed) return;
            if (string.IsNullOrWhiteSpace(hostInput)) { loggingService.LogError("Empty custom host.", new ArgumentException("Host empty.", nameof(hostInput))); ShowErrorState("HOST?"); return; }
            loggingService.LogInfo($"Setting ping target to Custom Host (input: {hostInput}). Initial load: {isInitialLoad}");
            activePingTargetType = PingTargetType.CustomHost;
            customPingTargetUserInput = hostInput.Trim();
            isCustomHostResolutionError = false;
            try
            {
                IPAddress[] addresses = await Dns.GetHostAddressesAsync(customPingTargetUserInput);
                IPAddress chosenAddress = addresses.FirstOrDefault(ip => ip.AddressFamily == AddressFamily.InterNetwork) ?? addresses.FirstOrDefault(ip => ip.AddressFamily == AddressFamily.InterNetworkV6) ?? addresses.FirstOrDefault();
                if (chosenAddress != null) { currentResolvedPingAddress = chosenAddress.ToString(); loggingService.LogInfo($"Resolved '{customPingTargetUserInput}' to '{currentResolvedPingAddress}'."); }
                else { currentResolvedPingAddress = customPingTargetUserInput; isCustomHostResolutionError = true; loggingService.LogInfo($"Could not get preferred IP for '{customPingTargetUserInput}', will ping hostname."); }
            }
            catch (SocketException ex) { currentResolvedPingAddress = customPingTargetUserInput; isCustomHostResolutionError = true; loggingService.LogError($"Failed to resolve '{customPingTargetUserInput}'. Ping will target hostname.", ex); }
            catch (Exception ex) { currentResolvedPingAddress = customPingTargetUserInput; isCustomHostResolutionError = true; loggingService.LogError($"Error setting custom host '{customPingTargetUserInput}'.", ex); }
            if (!isInitialLoad) { knownAPManager.SetLastCustomPingTarget(customPingTargetUserInput); }
            LogActivePingTarget();
            UpdateMenuPingTargetDisplay();
            await pingService.SendPingAsync(currentResolvedPingAddress, PING_TIMEOUT_MS);
        }

        private void LogActivePingTarget()
        {
            if (activePingTargetType == PingTargetType.CustomHost) { loggingService.LogInfo($"Active ping target: Custom (User: '{customPingTargetUserInput}', Resolved: '{currentResolvedPingAddress}'). ResErr: {isCustomHostResolutionError}."); }
            else { loggingService.LogInfo($"Active ping target: Default Gateway ('{networkMonitor.CurrentGateway ?? "N/A"}')."); }
        }

        private async void NetworkMonitor_GatewayChanged(object sender, string newGateway)
        {
            if (isDisposed) return;
            loggingService.LogInfo($"GW changed: '{newGateway ?? "N"}' Active: {activePingTargetType}.");
            bool menuNeedsUpdate = false;
            if (activePingTargetType == PingTargetType.DefaultGateway)
            {
                currentResolvedPingAddress = newGateway;
                LogActivePingTarget();
                menuNeedsUpdate = true;
                if (string.IsNullOrEmpty(newGateway)) { ShowErrorState("GW?"); trayIconManager.UpdateCurrentAP(null, null, null); }
                else { await pingService.SendPingAsync(newGateway, PING_TIMEOUT_MS); }
            }
            else { menuNeedsUpdate = true; }
            if (menuNeedsUpdate) UpdateMenuPingTargetDisplay();
        }

        private void PingService_PingCompleted(object sender, PingReply reply)
        {
            if (isDisposed || reply == null) return;
            try
            {
                string targetForLog = activePingTargetType == PingTargetType.CustomHost ? customPingTargetUserInput : networkMonitor.CurrentGateway;
                targetForLog = string.IsNullOrEmpty(targetForLog) ? currentResolvedPingAddress : targetForLog;
                if (reply.Status == IPStatus.Success)
                {
                    currentIconDisplayText = reply.RoundtripTime.ToString();
                    isCustomHostResolutionError = false;
                    string tooltipText = FormatTooltip();
                    trayIconManager.UpdateIcon(currentIconDisplayText, tooltipText, false, isInBssidTransition, isInBssidTransition);
                    if (!isInBssidTransition) { loggingService.LogInfo($"Ping OK - Tgt: {targetForLog} ({currentResolvedPingAddress}), Time: {reply.RoundtripTime}ms, BSSID: {networkStateManager.CurrentBssid ?? "N/A"}"); }
                }
                else
                {
                    currentIconDisplayText = "X";
                    string tooltipText = FormatTooltip(reply.Status.ToString());
                    trayIconManager.UpdateIcon(currentIconDisplayText, tooltipText, true, false, false);
                    loggingService.LogInfo($"Ping Fail - Tgt: {targetForLog} ({currentResolvedPingAddress}), Status: {reply.Status}, BSSID: {networkStateManager.CurrentBssid ?? "N/A"}");
                }
            }
            catch (Exception ex) { loggingService.LogError("Err handling ping reply", ex); }
        }

        private void PingService_PingError(object sender, Exception ex)
        {
            if (isDisposed) return;
            try
            {
                string targetForLog = activePingTargetType == PingTargetType.CustomHost ? customPingTargetUserInput : networkMonitor.CurrentGateway;
                targetForLog = string.IsNullOrEmpty(targetForLog) ? currentResolvedPingAddress : targetForLog;
                loggingService.LogError($"Ping error to {targetForLog} ({currentResolvedPingAddress})", ex);
                bool isDnsError = ex is SocketException se && (se.SocketErrorCode == SocketError.HostNotFound || se.SocketErrorCode == SocketError.TryAgain || se.SocketErrorCode == SocketError.NoData);
                if (activePingTargetType == PingTargetType.CustomHost && isDnsError) { isCustomHostResolutionError = true; ShowErrorState("DNS", "Unresolvable"); }
                else { ShowErrorState("!", ex.GetType().Name); }
            }
            catch (Exception logEx) { Debug.WriteLine($"[AC] Failed log PingService_PingError: {logEx.Message}"); }
        }

        private void ShowErrorState(string iconText, string specificErrorForTooltip = null)
        {
            if (isDisposed) return;
            try { currentIconDisplayText = iconText; string tt = FormatTooltip(specificErrorForTooltip ?? "Error"); trayIconManager.UpdateIcon(iconText, tt, true, false, false); }
            catch (Exception ex) { try { loggingService.LogError($"Err display state '{iconText}'", ex); } catch (Exception fEx) { Debug.WriteLine($"[AC] CRIT: Fail display state AND log: {fEx.Message}"); } }
        }

        private string FormatTooltip(string statusOverride = null)
        {
            if (isDisposed) return "App Disposed";
            string line1, line2, line3;
            string gwForTooltip = networkMonitor.CurrentGateway;
            string bssidForTooltip = networkStateManager.CurrentBssid;
            if (activePingTargetType == PingTargetType.CustomHost)
            {
                line1 = $"Pinging: {customPingTargetUserInput ?? currentResolvedPingAddress}";
                if (isCustomHostResolutionError) line2 = "Error: Unresolvable";
                else if (!string.IsNullOrEmpty(statusOverride)) line2 = $"Error: {statusOverride}";
                else line2 = $"Latency: {currentIconDisplayText}ms";
            }
            else
            {
                line1 = $"GW: {gwForTooltip ?? "Not Connected"}";
                if (!string.IsNullOrEmpty(statusOverride)) line2 = $"Error: {statusOverride}";
                else line2 = $"Latency: {currentIconDisplayText}ms";
            }
            string apDisplay = "AP: N/A";
            if (!string.IsNullOrEmpty(bssidForTooltip) && trayIconManager != null && !trayIconManager.IsDisposed)
            { try { apDisplay = $"AP: {trayIconManager.GetAPDisplayName(bssidForTooltip)}"; } catch (ObjectDisposedException) { apDisplay = "AP: (mgr dispo)"; } catch (Exception ex) { apDisplay = "AP: (err)"; Debug.WriteLine($"[AC] Err fmt tooltip (GetAPDisplayName): {ex.Message}"); } }
            line3 = apDisplay;
            return $"{line1}\n{line2}\n{line3}";
        }

        private void OnQuitRequested(object sender, EventArgs e) { if (isDisposed) return; ApplicationExit?.Invoke(this, EventArgs.Empty); }
        private void NetworkStateManager_LocationServicesStateChanged(object sender, bool isEnabled) { if (isDisposed) return; try { trayIconManager.UpdateLocationServicesState(!isEnabled); loggingService.LogInfo(isEnabled ? "LS Enabled - AP tracking resumed" : "LS Disabled - AP tracking off"); } catch (Exception ex) { loggingService.LogError("Err LS state change", ex); } }

        // Event handler for System.Timers.Timer.Elapsed
        private void BssidResetTimer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            if (isDisposed) return;
            try
            {
                isInBssidTransition = false;
                loggingService.LogInfo("BSSID transition end.");
                if (!string.IsNullOrEmpty(currentResolvedPingAddress)) Task.Run(() => pingService.SendPingAsync(currentResolvedPingAddress, PING_TIMEOUT_MS)); // Fire and forget for UI refresh
            }
            catch (Exception ex) { loggingService.LogError("Err BSSID reset timer", ex); }
        }

        private void NetworkStateManager_BssidChanged(object sender, BssidChangeEventArgs e) { if (isDisposed) return; try { isInBssidTransition = true; bssidResetTimer.Stop(); bssidResetTimer.Start(); trayIconManager.UpdateCurrentAP(e.NewBssid, networkStateManager.CurrentBand, networkStateManager.CurrentSsid); trayIconManager.ShowTransitionBalloon(e.OldBssid, e.NewBssid); if (!string.IsNullOrEmpty(currentIconDisplayText)) { string tt = FormatTooltip(); trayIconManager.UpdateIcon(currentIconDisplayText, tt, isCustomHostResolutionError || currentIconDisplayText == "X" || currentIconDisplayText == "!", true, true); } loggingService.LogInfo($"BSSID {e.OldBssid ?? "N"}->{e.NewBssid ?? "N"}. Transition."); UpdateMenuPingTargetDisplay(); } catch (Exception ex) { loggingService.LogError("Err BSSID change", ex); } }
        private void NetworkMonitor_NetworkAvailabilityChanged(object sender, bool isAvailable) { if (isDisposed) return; try { loggingService.LogInfo(isAvailable ? "Net available." : "Net unavailable."); if (!isAvailable) { trayIconManager.UpdateCurrentAP(null, null, null); if (activePingTargetType == PingTargetType.DefaultGateway) ShowErrorState("OFF", "Net Down"); else ShowErrorState("NET?", "Net Down"); } else if (!string.IsNullOrEmpty(currentResolvedPingAddress)) Task.Run(() => pingService.SendPingAsync(currentResolvedPingAddress, PING_TIMEOUT_MS)); UpdateMenuPingTargetDisplay(); } catch (Exception ex) { loggingService.LogError($"Err net avail change (avail: {isAvailable})", ex); } }

        protected virtual void Dispose(bool disposing) { if (!isDisposed) { if (disposing) { try { loggingService?.LogInfo("AppController: Shutting down. Disposing resources."); } catch (Exception ex) { Debug.WriteLine($"[AC] Dispose: Failed final log: {ex.Message}"); } Debug.WriteLine("[AC] Dispose(true): Start."); UnsubscribeFromEvents(); SafelyStopAndDisposeTimer(periodicPingTimer, nameof(periodicPingTimer)); SafelyStopAndDisposeTimer(bssidResetTimer, nameof(bssidResetTimer)); SafelyDispose(pingService, nameof(pingService)); SafelyDispose(networkStateManager, nameof(networkStateManager)); SafelyDispose(networkMonitor, nameof(networkMonitor)); SafelyDispose(trayIconManager, nameof(trayIconManager)); SafelyDispose(loggingService, nameof(loggingService), true); Debug.WriteLine("[AC] Dispose(true): End."); } isDisposed = true; } }
        private void SafelyStopAndDisposeTimer(System.Timers.Timer timer, string timerName) { if (timer != null) { try { timer.Stop(); timer.Dispose(); Debug.WriteLine($"[AC] Dispose: {timerName} disposed."); } catch (Exception ex) { Debug.WriteLine($"[AC] Dispose Err: {timerName}: {ex.Message}"); } } }
        private void SafelyDispose(IDisposable resource, string resourceName, bool isLastResource = false) { if (resource != null) { try { resource.Dispose(); Debug.WriteLine($"[AC] Dispose: {resourceName} disposed."); } catch (Exception ex) { Debug.WriteLine($"[AC] Dispose Err {resourceName}: {ex.Message}{Environment.NewLine}{ex.StackTrace}"); if (resource is ILoggingService && !isLastResource) Debug.WriteLine($"[AC] CRIT: LoggingService err during dispose but not last."); } } else Debug.WriteLine($"[AC] Dispose: {resourceName} null."); }
        public void Dispose() { Dispose(true); GC.SuppressFinalize(this); }
    }
}
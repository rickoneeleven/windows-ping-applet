using System;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
using System.Timers;
using ping_applet.Core.Interfaces;
using ping_applet.Services;
using ping_applet.UI;

namespace ping_applet.Controllers
{
    /// <summary>
    /// Controls the main application flow and coordinates between services
    /// </summary>
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
            IPingService pingService,
            ILoggingService loggingService,
            TrayIconManager trayIconManager)
        {
            this.networkMonitor = networkMonitor ?? throw new ArgumentNullException(nameof(networkMonitor));
            this.pingService = pingService ?? throw new ArgumentNullException(nameof(pingService));
            this.loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
            this.trayIconManager = trayIconManager ?? throw new ArgumentNullException(nameof(trayIconManager));

            this.networkStateManager = new NetworkStateManager(loggingService);

            this.bssidResetTimer = new Timer(BSSID_TRANSITION_WINDOW);
            this.bssidResetTimer.AutoReset = false;
            this.bssidResetTimer.Elapsed += BssidResetTimer_Elapsed;

            this.networkMonitor.GatewayChanged += NetworkMonitor_GatewayChanged;
            this.networkMonitor.NetworkAvailabilityChanged += NetworkMonitor_NetworkAvailabilityChanged;
            this.pingService.PingCompleted += PingService_PingCompleted;
            this.pingService.PingError += PingService_PingError;
            this.trayIconManager.QuitRequested += (s, e) => ApplicationExit?.Invoke(this, EventArgs.Empty);
            this.networkStateManager.BssidChanged += NetworkStateManager_BssidChanged;
        }

        public async Task InitializeAsync()
        {
            try
            {
                loggingService.LogInfo("Application starting up");
                await networkMonitor.InitializeAsync();
                await networkStateManager.StartMonitoring();
                pingService.StartPingTimer(PING_INTERVAL);

                if (!string.IsNullOrEmpty(networkMonitor.CurrentGateway))
                {
                    await pingService.SendPingAsync(networkMonitor.CurrentGateway, PING_TIMEOUT);
                }
            }
            catch (Exception ex)
            {
                loggingService.LogError("Initialization error", ex);
                ShowErrorState("INIT!");
                throw;
            }
        }

        private void NetworkStateManager_BssidChanged(object sender, string newBssid)
        {
            try
            {
                isInBssidTransition = true;
                bssidResetTimer.Stop();
                bssidResetTimer.Start();

                if (!string.IsNullOrEmpty(currentDisplayText))
                {
                    trayIconManager.UpdateIcon(
                        currentDisplayText,
                        $"Access Point Change - BSSID: {newBssid}",
                        false,
                        true,
                        true  // Use black text during transition
                    );
                }
            }
            catch (Exception ex)
            {
                loggingService.LogError("Error handling BSSID change", ex);
            }
        }

        private void BssidResetTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            try
            {
                isInBssidTransition = false;
                if (!string.IsNullOrEmpty(networkMonitor.CurrentGateway))
                {
                    _ = pingService.SendPingAsync(networkMonitor.CurrentGateway, PING_TIMEOUT);
                }
            }
            catch (Exception ex)
            {
                loggingService.LogError("Error handling BSSID reset", ex);
            }
        }

        private async void NetworkMonitor_GatewayChanged(object sender, string newGateway)
        {
            if (string.IsNullOrEmpty(newGateway))
            {
                ShowErrorState("GW?");
                loggingService.LogInfo("Gateway became unavailable");
            }
            else
            {
                loggingService.LogInfo($"Gateway changed to: {newGateway}");
                await pingService.SendPingAsync(newGateway, PING_TIMEOUT);
            }
            trayIconManager.UpdateStatus();
        }

        private void NetworkMonitor_NetworkAvailabilityChanged(object sender, bool isAvailable)
        {
            if (!isAvailable)
            {
                loggingService.LogInfo("Network became unavailable");
                ShowErrorState("OFF");
            }
        }

        private void PingService_PingCompleted(object sender, PingReply reply)
        {
            if (isDisposed || reply == null) return;

            try
            {
                if (reply.Status == IPStatus.Success)
                {
                    string displayText = reply.RoundtripTime.ToString();
                    string tooltipText = $"{networkMonitor.CurrentGateway}: {reply.RoundtripTime}ms";
                    currentDisplayText = displayText;

                    trayIconManager.UpdateIcon(
                        displayText,
                        tooltipText,
                        false,
                        isInBssidTransition,
                        isInBssidTransition  // Use black text during transition
                    );

                    if (!isInBssidTransition)
                    {
                        loggingService.LogInfo($"Ping successful - Gateway: {networkMonitor.CurrentGateway}, Time: {reply.RoundtripTime}ms");
                    }
                }
                else
                {
                    string tooltipText = $"{networkMonitor.CurrentGateway}: Failed";
                    trayIconManager.UpdateIcon("X", tooltipText, true, false, false);
                    loggingService.LogInfo($"Ping failed - Gateway: {networkMonitor.CurrentGateway}, Status: {reply.Status}");
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
            loggingService.LogError("Ping error", ex);
            ShowErrorState("!");
        }

        private void ShowErrorState(string errorText)
        {
            try
            {
                string tooltipText = $"Error: {errorText}";
                trayIconManager.UpdateIcon(errorText, tooltipText, true, false, false);
            }
            catch (Exception ex)
            {
                loggingService.LogError("Error state display failed", ex);
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!isDisposed && disposing)
            {
                networkStateManager.Dispose();
                networkMonitor.Dispose();
                pingService.Dispose();
                loggingService.Dispose();
                trayIconManager.Dispose();
                bssidResetTimer.Dispose();
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
using System;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
using System.Timers; // Added for Timer
using ping_applet.Core.Interfaces;
using System.Diagnostics; // Added for Debug

namespace ping_applet.Services
{
    public class NetworkMonitor : INetworkMonitor, IDisposable // Explicitly implement IDisposable here for clarity
    {
        // Constants
        private const int GATEWAY_POLLING_INTERVAL_MS = 30000; // 30 seconds - periodic check for gateway changes
        private const int NETWORK_STABILIZATION_DELAY_MS = 1000; // 1 second delay after network becomes available

        // Fields
        private Timer gatewayPollingTimer; // Timer for periodic checks
        private string currentGateway;
        private bool isMonitoring;
        private bool isDisposed;
        // Use a simple lock for thread safety around state modification and event invocation
        private readonly object monitorLock = new object();
        // Logging service placeholder (assuming we'll add it later if needed for deeper debugging here)
        // private readonly ILoggingService _loggingService;

        // Events
        public event EventHandler<string> GatewayChanged;
        public event EventHandler<bool> NetworkAvailabilityChanged;

        // Properties
        public bool IsNetworkAvailable => NetworkInterface.GetIsNetworkAvailable();
        public string CurrentGateway
        {
            get
            {
                lock (monitorLock)
                {
                    return currentGateway;
                }
            }
            private set // Keep setter private, modification should go through UpdateGateway
            {
                lock (monitorLock)
                {
                    currentGateway = value;
                }
            }
        }

        public NetworkMonitor(/* Inject ILoggingService here if needed */)
        {
            isMonitoring = false;
            isDisposed = false;
            InitializePollingTimer();
        }

        private void InitializePollingTimer()
        {
            gatewayPollingTimer = new Timer(GATEWAY_POLLING_INTERVAL_MS);
            gatewayPollingTimer.Elapsed += GatewayPollingTimer_Elapsed;
            gatewayPollingTimer.AutoReset = true;
            gatewayPollingTimer.Enabled = false; // Start disabled, enable in StartMonitoring
        }

        public async Task InitializeAsync()
        {
            ThrowIfDisposed();
            // Perform initial gateway detection before starting monitoring
            await UpdateGateway(forceUpdate: true); // Force initial update
            StartMonitoring();
        }

        public Task<bool> UpdateGateway(bool forceUpdate = false)
        {
            ThrowIfDisposed();

            string newGateway = null;
            bool gatewayHasChanged = false;

            try
            {
                newGateway = GetDefaultGatewayInternal(); // Use internal method

                // Check if the gateway has actually changed or if forced update
                lock (monitorLock)
                {
                    if (forceUpdate || newGateway != currentGateway)
                    {
                        gatewayHasChanged = true;
                        currentGateway = newGateway; // Update the stored gateway
                        // Note: Logging would go here if service was injected
                        // _loggingService?.LogInfo($"Gateway updated. New Gateway: {newGateway ?? "None"}. Force update: {forceUpdate}");
                        Debug.WriteLine($"[NetworkMonitor] Gateway updated. New Gateway: {newGateway ?? "None"}. Force update: {forceUpdate}");
                    }
                }

                // Invoke event outside the lock if changed
                if (gatewayHasChanged)
                {
                    // Safely invoke the event handler
                    GatewayChanged?.Invoke(this, newGateway);
                }

                return Task.FromResult(gatewayHasChanged);
            }
            catch (Exception ex) // Catch potential exceptions during gateway detection
            {
                // Log the error if logger available (placeholder until injected)
                // _loggingService?.LogError("Error getting default gateway", ex);
                Debug.WriteLine($"[NetworkMonitor] ERROR getting default gateway: {ex.Message}"); // *** USE ex HERE ***

                // Ensure state is consistent on error
                bool needsEventTrigger = false;
                lock (monitorLock)
                {
                    // Trigger if it changed to null or forced (e.g., was "1.1.1.1", error occurred, now should be null)
                    if (currentGateway != null || forceUpdate)
                    {
                        needsEventTrigger = true;
                        currentGateway = null;
                    }
                }

                if (needsEventTrigger)
                {
                    GatewayChanged?.Invoke(this, null);
                }
                return Task.FromResult(needsEventTrigger); // Return true if state changed (likely to null)
            }
        }

        // Renamed original GetDefaultGateway to avoid confusion with the interface member
        private string GetDefaultGatewayInternal()
        {
            // No need for ThrowIfDisposed here as it's private and called by public methods

            // Prioritize active interfaces first
            var activeInterfaces = NetworkInterface.GetAllNetworkInterfaces()
                .Where(ni =>
                    ni.OperationalStatus == OperationalStatus.Up &&
                    ni.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                    ni.Supports(NetworkInterfaceComponent.IPv4) &&
                    ni.GetIPProperties()?.GatewayAddresses?.Count > 0)
                .OrderByDescending(ni => ni.Speed) // Prefer faster interfaces if multiple are active
                .ThenBy(ni => ni.Description); // Consistent ordering

            foreach (var activeInterface in activeInterfaces)
            {
                var gateway = activeInterface.GetIPProperties()
                    .GatewayAddresses
                    .FirstOrDefault(ga =>
                        ga?.Address != null &&
                        !ga.Address.Equals(System.Net.IPAddress.Parse("0.0.0.0"))) // Exclude 0.0.0.0
                    ?.Address.ToString();

                if (!string.IsNullOrEmpty(gateway))
                {
                    // Log found gateway
                    // _loggingService?.LogInfo($"Found gateway {gateway} on interface {activeInterface.Description}");
                    Debug.WriteLine($"[NetworkMonitor] Found gateway {gateway} on interface {activeInterface.Description}");
                    return gateway;
                }
            }

            // Log if no gateway found
            // _loggingService?.LogInfo("No active default gateway found.");
            Debug.WriteLine("[NetworkMonitor] No active default gateway found.");
            return null;
        }

        // Public GetDefaultGateway simply returns the current cached value
        public string GetDefaultGateway()
        {
            ThrowIfDisposed();
            return CurrentGateway;
        }


        public void StartMonitoring()
        {
            ThrowIfDisposed();
            lock (monitorLock)
            {
                if (!isMonitoring)
                {
                    // Log start
                    // _loggingService?.LogInfo("Starting network monitoring...");
                    Debug.WriteLine("[NetworkMonitor] Starting network monitoring...");
                    NetworkChange.NetworkAddressChanged += NetworkAddressChangedHandler; // Re-evaluate if needed, but keep for now
                    NetworkChange.NetworkAvailabilityChanged += NetworkAvailabilityChangedHandler;
                    gatewayPollingTimer.Start(); // Start the periodic polling timer
                    isMonitoring = true;
                    // Log started
                    // _loggingService?.LogInfo("Network monitoring started.");
                    Debug.WriteLine("[NetworkMonitor] Network monitoring started.");
                }
            }
        }

        public void StopMonitoring()
        {
            // No ThrowIfDisposed here, should be safe to call stop multiple times or during disposal
            lock (monitorLock)
            {
                if (isMonitoring)
                {
                    // Log stop
                    // _loggingService?.LogInfo("Stopping network monitoring...");
                    Debug.WriteLine("[NetworkMonitor] Stopping network monitoring...");
                    NetworkChange.NetworkAddressChanged -= NetworkAddressChangedHandler;
                    NetworkChange.NetworkAvailabilityChanged -= NetworkAvailabilityChangedHandler;
                    gatewayPollingTimer.Stop(); // Stop the periodic polling timer
                    isMonitoring = false;
                    // Log stopped
                    // _loggingService?.LogInfo("Network monitoring stopped.");
                    Debug.WriteLine("[NetworkMonitor] Network monitoring stopped.");
                }
            }
        }

        // --- Event Handlers ---

        private async void GatewayPollingTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            // Timer runs on a background thread, UpdateGateway handles its own thread safety
            try
            {
                // No need to force update here, just check if it changed
                await UpdateGateway(forceUpdate: false);
            }
            catch (Exception ex)
            {
                // Should not happen if UpdateGateway handles its exceptions, but as a safeguard
                // _loggingService?.LogError("Unhandled exception in GatewayPollingTimer_Elapsed", ex);
                Debug.WriteLine($"[NetworkMonitor] ERROR in GatewayPollingTimer_Elapsed: {ex.Message}");
            }
        }

        private void NetworkAddressChangedHandler(object sender, EventArgs e)
        {
            // This event can be noisy, especially during Wi-Fi transitions.
            // Currently, the periodic polling timer is the main mechanism for detecting
            // static IP changes. We could add debouncing here if needed later,
            // but for now, rely on the timer.
            // _loggingService?.LogInfo("NetworkAddressChanged event received.");
            Debug.WriteLine("[NetworkMonitor] NetworkAddressChanged event received.");
            // Consider adding a debounced call to UpdateGateway(false) here if polling proves too slow.
        }

        private async void NetworkAvailabilityChangedHandler(object sender, NetworkAvailabilityEventArgs e)
        {
            // Use a temporary variable to hold disposal status to avoid race condition after await
            bool disposedBeforeCheck = isDisposed;
            if (disposedBeforeCheck) return;

            // Log availability change
            // _loggingService?.LogInfo($"Network availability changed. IsAvailable: {e.IsAvailable}");
            Debug.WriteLine($"[NetworkMonitor] Network availability changed. IsAvailable: {e.IsAvailable}");

            // Safely invoke the event handler
            NetworkAvailabilityChanged?.Invoke(this, e.IsAvailable);

            // If network becomes available, update the gateway after a short delay
            // to allow the network stack to stabilize. Force the update to ensure
            // the state is refreshed immediately upon reconnection.
            if (e.IsAvailable)
            {
                try
                {
                    await Task.Delay(NETWORK_STABILIZATION_DELAY_MS);
                    // Re-check disposal status after delay
                    if (isDisposed) return;
                    await UpdateGateway(forceUpdate: true);
                }
                catch (ObjectDisposedException)
                {
                    Debug.WriteLine("[NetworkMonitor] NetworkMonitor disposed during stabilization delay.");
                    // Ignore if disposed during the delay
                }
                catch (Exception ex)
                {
                    // Log error during update after network available
                    // _loggingService?.LogError("Error updating gateway after network became available", ex);
                    Debug.WriteLine($"[NetworkMonitor] ERROR updating gateway after network became available: {ex.Message}");
                }
            }
            else // Network became unavailable
            {
                try
                {
                    // Immediately update gateway state to null if network is lost
                    // Re-check disposal status before update
                    if (isDisposed) return;
                    await UpdateGateway(forceUpdate: true); // Force update to ensure state becomes null
                }
                catch (ObjectDisposedException)
                {
                    Debug.WriteLine("[NetworkMonitor] NetworkMonitor disposed during unavailable update.");
                    // Ignore if disposed during update call
                }
                catch (Exception ex)
                {
                    // Log error during update after network unavailable
                    // _loggingService?.LogError("Error updating gateway after network became unavailable", ex);
                    Debug.WriteLine($"[NetworkMonitor] ERROR updating gateway after network became unavailable: {ex.Message}");
                }
            }
        }

        // --- Disposal ---

        protected virtual void Dispose(bool disposing)
        {
            if (!isDisposed)
            {
                if (disposing)
                {
                    // Stop monitoring first
                    StopMonitoring();

                    // Dispose managed resources
                    if (gatewayPollingTimer != null)
                    {
                        gatewayPollingTimer.Elapsed -= GatewayPollingTimer_Elapsed; // Unsubscribe
                        gatewayPollingTimer.Dispose();
                        gatewayPollingTimer = null;
                    }
                    // Log disposal
                    // _loggingService?.LogInfo("NetworkMonitor disposed.");
                    Debug.WriteLine("[NetworkMonitor] NetworkMonitor disposed.");
                }
                // No unmanaged resources to free directly

                isDisposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this); // Suppress finalization as we've handled disposal
        }

        // Helper to check disposal status
        private void ThrowIfDisposed()
        {
            if (isDisposed)
            {
                throw new ObjectDisposedException(nameof(NetworkMonitor));
            }
        }

        // Destructor (Finalizer) - only if needed for unmanaged resources
        // ~NetworkMonitor()
        // {
        //     Dispose(false);
        // }
    }
}
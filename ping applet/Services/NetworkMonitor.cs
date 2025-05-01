using System;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
using System.Timers; // Added for Timer
using ping_applet.Core.Interfaces;
using System.Diagnostics; // Added for Debug
using System.Net.Sockets; // Added for AddressFamily

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
                        Debug.WriteLine($"[NetworkMonitor] Gateway updated. New Gateway: {newGateway ?? "None"} (IPv4 Preferred). Force update: {forceUpdate}");
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
                    ni.Supports(NetworkInterfaceComponent.IPv4) && // Ensure IPv4 support on interface
                    ni.GetIPProperties()?.GatewayAddresses?.Count > 0)
                .OrderByDescending(ni => ni.Speed) // Prefer faster interfaces if multiple are active
                .ThenBy(ni => ni.Description); // Consistent ordering

            foreach (var activeInterface in activeInterfaces)
            {
                var ipProperties = activeInterface.GetIPProperties();
                if (ipProperties == null) continue;

                // --- MODIFICATION START: Prioritize IPv4 Gateway ---
                var ipv4Gateway = ipProperties.GatewayAddresses
                    .FirstOrDefault(ga =>
                        ga?.Address != null &&
                        ga.Address.AddressFamily == AddressFamily.InterNetwork && // Check for IPv4
                        !ga.Address.Equals(System.Net.IPAddress.Parse("0.0.0.0"))) // Exclude 0.0.0.0
                    ?.Address.ToString();

                if (!string.IsNullOrEmpty(ipv4Gateway))
                {
                    Debug.WriteLine($"[NetworkMonitor] Found IPv4 gateway {ipv4Gateway} on interface {activeInterface.Description}");
                    return ipv4Gateway;
                }
                // --- MODIFICATION END ---

                // Fallback: If no IPv4 gateway, check for *any* valid gateway (including IPv6)
                // This maintains connectivity if only IPv6 is configured, although it might be long.
                // The tooltip truncation will handle length issues later.
                var anyGateway = ipProperties.GatewayAddresses
                   .FirstOrDefault(ga =>
                       ga?.Address != null &&
                       !ga.Address.Equals(System.Net.IPAddress.Parse("0.0.0.0")))
                   ?.Address.ToString();

                if (!string.IsNullOrEmpty(anyGateway))
                {
                    Debug.WriteLine($"[NetworkMonitor] Found non-IPv4 gateway {anyGateway} on interface {activeInterface.Description} (using as fallback)");
                    return anyGateway; // Return the first valid one found if no IPv4
                }
            }

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
                    Debug.WriteLine("[NetworkMonitor] Starting network monitoring...");
                    NetworkChange.NetworkAddressChanged += NetworkAddressChangedHandler;
                    NetworkChange.NetworkAvailabilityChanged += NetworkAvailabilityChangedHandler;
                    gatewayPollingTimer.Start(); // Start the periodic polling timer
                    isMonitoring = true;
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
                    Debug.WriteLine("[NetworkMonitor] Stopping network monitoring...");
                    NetworkChange.NetworkAddressChanged -= NetworkAddressChangedHandler;
                    NetworkChange.NetworkAvailabilityChanged -= NetworkAvailabilityChangedHandler;
                    gatewayPollingTimer.Stop(); // Stop the periodic polling timer
                    isMonitoring = false;
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
                Debug.WriteLine($"[NetworkMonitor] ERROR in GatewayPollingTimer_Elapsed: {ex.Message}");
            }
        }

        private void NetworkAddressChangedHandler(object sender, EventArgs e)
        {
            // This event can be noisy. Relying on periodic polling and availability changes.
            Debug.WriteLine("[NetworkMonitor] NetworkAddressChanged event received.");
            // Consider adding a debounced call to UpdateGateway(false) here if polling proves too slow.
        }

        private async void NetworkAvailabilityChangedHandler(object sender, NetworkAvailabilityEventArgs e)
        {
            bool disposedBeforeCheck = isDisposed;
            if (disposedBeforeCheck) return;

            Debug.WriteLine($"[NetworkMonitor] Network availability changed. IsAvailable: {e.IsAvailable}");

            NetworkAvailabilityChanged?.Invoke(this, e.IsAvailable);

            if (e.IsAvailable)
            {
                try
                {
                    await Task.Delay(NETWORK_STABILIZATION_DELAY_MS);
                    if (isDisposed) return;
                    await UpdateGateway(forceUpdate: true); // Force update on reconnect
                }
                catch (ObjectDisposedException)
                {
                    Debug.WriteLine("[NetworkMonitor] NetworkMonitor disposed during stabilization delay.");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[NetworkMonitor] ERROR updating gateway after network became available: {ex.Message}");
                }
            }
            else // Network became unavailable
            {
                try
                {
                    if (isDisposed) return;
                    await UpdateGateway(forceUpdate: true); // Force update to null gateway state
                }
                catch (ObjectDisposedException)
                {
                    Debug.WriteLine("[NetworkMonitor] NetworkMonitor disposed during unavailable update.");
                }
                catch (Exception ex)
                {
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
                    StopMonitoring();
                    if (gatewayPollingTimer != null)
                    {
                        gatewayPollingTimer.Elapsed -= GatewayPollingTimer_Elapsed;
                        gatewayPollingTimer.Dispose();
                        gatewayPollingTimer = null;
                    }
                    Debug.WriteLine("[NetworkMonitor] NetworkMonitor disposed.");
                }
                isDisposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void ThrowIfDisposed()
        {
            if (isDisposed)
            {
                throw new ObjectDisposedException(nameof(NetworkMonitor));
            }
        }
    }
}
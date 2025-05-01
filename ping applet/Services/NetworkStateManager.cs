using System;
using System.Diagnostics;
using System.Net.NetworkInformation; // Added
using System.Linq; // Added
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Timers;
using ping_applet.Core.Interfaces;

namespace ping_applet.Services
{
    public class NetworkStateManager : IDisposable
    {
        private readonly ILoggingService _loggingService;
        private Timer _monitorTimer;
        private string _currentBssid;
        private string _previousBssid;
        private int _currentSignalStrength;
        private int _currentChannel;
        private string _currentBand;
        private string _currentSsid;
        private bool _isDisposed;
        // Removed _isInitialCheck as its logic was moved/simplified
        private bool _isLocationServicesEnabled = true;
        private bool _hasWifiInterface = true; // Assume yes initially, check at startup
        private const int CHECK_INTERVAL = 1000; // 1 second

        public event EventHandler<BssidChangeEventArgs> BssidChanged;
        public event EventHandler<int> SignalStrengthChanged;
        public event EventHandler<bool> LocationServicesStateChanged;

        public string CurrentBssid => _currentBssid;
        public string PreviousBssid => _previousBssid;
        public int CurrentSignalStrength => _currentSignalStrength;
        public int CurrentChannel => _currentChannel;
        public string CurrentBand => _currentBand;
        public string CurrentSsid => _currentSsid;

        public NetworkStateManager(ILoggingService loggingService)
        {
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
            _loggingService.LogInfo("NetworkStateManager initialized");
        }

        public async Task StartMonitoring()
        {
            if (_isDisposed) throw new ObjectDisposedException(nameof(NetworkStateManager));

            try
            {
                _hasWifiInterface = NetworkInterface.GetAllNetworkInterfaces()
                                      .Any(ni => ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211);

                if (!_hasWifiInterface)
                {
                    _loggingService.LogInfo("No WiFi interface detected. AP tracking will be disabled.");
                    _currentBssid = null;
                    _currentChannel = 0;
                    _currentBand = null;
                    _currentSsid = null;
                }
                else
                {
                    _loggingService.LogInfo("WiFi interface detected. Starting initial network state check.");
                    // Perform initial state check immediately only if WiFi exists
                    await CheckNetworkState();
                    // Initial state logged within CheckNetworkState/GetWifiInfo if connection found
                }

                _monitorTimer = new Timer(CHECK_INTERVAL);
                _monitorTimer.Elapsed += async (s, e) => await CheckNetworkState();
                _monitorTimer.AutoReset = true;
                _monitorTimer.Start();

                _loggingService.LogInfo("Network state monitoring timer started");
            }
            catch (Exception ex)
            {
                _loggingService.LogError("Failed to start network state monitoring", ex);
                throw; // Re-throw critical startup errors
            }
        }

        private async Task CheckNetworkState()
        {
            if (_isDisposed || !_hasWifiInterface)
            {
                return;
            }

            try
            {
                var (bssid, signalStrength, channel, band, ssid) = await GetWifiInfo();

                // Handle case where WiFi exists but is not connected or info couldn't be retrieved
                if (string.IsNullOrEmpty(bssid))
                {
                    if (_currentBssid != null) // If we were previously connected
                    {
                        _loggingService.LogInfo($"WiFi disconnected from BSSID: {_currentBssid}");
                        _previousBssid = _currentBssid;
                        _currentBssid = null;
                        _currentChannel = 0;
                        _currentBand = null;
                        _currentSsid = null;
                        BssidChanged?.Invoke(this, new BssidChangeEventArgs(_previousBssid, null));
                    }
                    // If _currentBssid was already null, do nothing (still disconnected)
                    return;
                }

                // --- BSSID Change Handling ---
                if (bssid != _currentBssid)
                {
                    _previousBssid = _currentBssid; // Store the old BSSID before updating
                    var oldBssidForLog = _currentBssid ?? "none";
                    var oldChannel = _currentChannel;
                    var oldBand = _currentBand ?? "unknown";
                    var oldSsid = _currentSsid ?? "unknown";

                    _currentBssid = bssid;
                    _currentChannel = channel;
                    _currentBand = band;
                    _currentSsid = ssid;

                    _loggingService.LogInfo(
                        $"WiFi connected/changed:\n" +
                        $"  From: BSSID={oldBssidForLog}, Channel={oldChannel}, Band={oldBand}, SSID={oldSsid}\n" +
                        $"  To:   BSSID={bssid}, Channel={channel}, Band={band}, SSID={ssid}"
                    );

                    BssidChanged?.Invoke(this, new BssidChangeEventArgs(_previousBssid, bssid)); // Pass actual previous BSSID
                }
                // --- Signal Strength / Details Change Handling (only if BSSID is the same) ---
                else
                {
                    if (Math.Abs(signalStrength - _currentSignalStrength) > 5)
                    {
                        var oldStrength = _currentSignalStrength;
                        _currentSignalStrength = signalStrength;
                        _loggingService.LogInfo(
                            $"Signal strength changed from {oldStrength}% to {signalStrength}% " +
                            $"(BSSID: {bssid}, Channel: {channel}, Band: {band}, SSID: {ssid})"
                        );
                        SignalStrengthChanged?.Invoke(this, signalStrength);
                    }

                    // Check if other details changed while connected to the same BSSID
                    if (channel != _currentChannel || band != _currentBand || ssid != _currentSsid)
                    {
                        _loggingService.LogInfo(
                            $"Network details changed for BSSID {bssid}:\n" +
                            $"  From: Channel={_currentChannel}, Band={_currentBand}, SSID={_currentSsid}\n" +
                            $"  To:   Channel={channel}, Band={band}, SSID={ssid}"
                        );
                        _currentChannel = channel;
                        _currentBand = band;
                        _currentSsid = ssid;
                        // Removed unused 'detailsChanged' variable assignment
                    }
                }
            }
            catch (Exception ex)
            {
                // Log errors during the check but allow the timer to continue
                _loggingService.LogError("Error checking network state", ex);
            }
        }


        private async Task<(string bssid, int signalStrength, int channel, string band, string ssid)> GetWifiInfo()
        {
            if (_isDisposed) throw new ObjectDisposedException(nameof(NetworkStateManager));

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "netsh",
                    Arguments = "wlan show interfaces",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };

                using (var process = new Process { StartInfo = startInfo })
                {
                    process.Start();
                    string output = await process.StandardOutput.ReadToEndAsync();
                    await Task.Run(() => process.WaitForExit());


                    if (process.ExitCode != 0)
                    {
                        bool isLocationServicesError = output.Contains("Network shell commands need location permission");
                        bool isElevationError = output.Contains("error 5") && output.Contains("requires elevation");
                        bool isNoInterfaceError = output.Contains("There is no wireless interface on the system");

                        if (isLocationServicesError || isElevationError)
                        {
                            if (_isLocationServicesEnabled)
                            {
                                _isLocationServicesEnabled = false;
                                LocationServicesStateChanged?.Invoke(this, false);
                                // Use LogInfo for operational state change, not warning/error
                                _loggingService.LogInfo("Location services are disabled or netsh requires elevation. AP tracking requires these.");
                            }
                            return (null, 0, 0, null, null);
                        }
                        else if (isNoInterfaceError)
                        {
                            _loggingService.LogInfo("netsh reported no wireless interface.");
                            _hasWifiInterface = false; // Update state
                            return (null, 0, 0, null, null);
                        }
                        else // Log other unexpected errors
                        {
                            // Log as Error because the command failed unexpectedly
                            _loggingService.LogError($"netsh command failed. Exit Code: {process.ExitCode}. Output: {output.Trim()}");
                            return (null, 0, 0, null, null);
                        }
                    }

                    if (!_isLocationServicesEnabled)
                    {
                        _isLocationServicesEnabled = true;
                        LocationServicesStateChanged?.Invoke(this, true);
                        _loggingService.LogInfo("Location services are now enabled/available.");
                    }

                    // Check for disconnected state *after* successful exit code
                    if (output.Contains("State              : disconnected"))
                    {
                        _loggingService.LogInfo("netsh reports WiFi interface is disconnected.");
                        return (null, 0, 0, null, null);
                    }

                    // --- Parsing Logic ---
                    var bssidMatch = Regex.Match(output, @"BSSID\s+:\s+([0-9A-Fa-f]{2}(?::[0-9A-Fa-f]{2}){5})", RegexOptions.IgnoreCase);
                    var signalMatch = Regex.Match(output, @"Signal\s+:\s+(\d+)%");
                    var channelMatch = Regex.Match(output, @"Channel\s+:\s+(\d+)");
                    var bandMatch = Regex.Match(output, @"Radio type\s+:\s+(.+?)\r?\n"); // Changed to Radio type for band info (like 802.11ax)
                    var ssidMatch = Regex.Match(output, @"SSID\s+:\s+(.+?)\r?\n");

                    string bssid = bssidMatch.Success ? bssidMatch.Groups[1].Value : null;
                    int signalStrength = signalMatch.Success ? int.Parse(signalMatch.Groups[1].Value) : 0;
                    int channel = channelMatch.Success ? int.Parse(channelMatch.Groups[1].Value) : 0;
                    string band = bandMatch.Success ? bandMatch.Groups[1].Value.Trim() : null;
                    string ssid = ssidMatch.Success ? ssidMatch.Groups[1].Value.Trim() : null;

                    if (!bssidMatch.Success)
                    {
                        // Log as Info, as this usually means connected but BSSID not available (e.g., connecting state)
                        _loggingService.LogInfo($"Could not parse BSSID from netsh output (State may be connecting/authenticating). Output: {output.Trim()}");
                    }

                    return (bssid, signalStrength, channel, band, ssid);
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogError("Error executing or parsing netsh command", ex);
                return (null, 0, 0, null, null); // Return nulls on exception
            }
        }

        public void StopMonitoring()
        {
            try
            {
                // Safely stop and dispose the timer
                _monitorTimer?.Stop();
                _monitorTimer?.Dispose();
                _monitorTimer = null;
                _loggingService.LogInfo("Network state monitoring stopped");
            }
            catch (Exception ex)
            {
                // Log potential errors during stop, but don't crash
                _loggingService.LogError("Error stopping network monitoring", ex);
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_isDisposed)
            {
                if (disposing)
                {
                    try
                    {
                        StopMonitoring(); // Ensure timer is stopped and disposed
                    }
                    catch (Exception ex)
                    {
                        _loggingService?.LogError("Error stopping monitor during NetworkStateManager disposal", ex);
                    }
                    _isDisposed = true;
                    _loggingService?.LogInfo("NetworkStateManager disposed");
                }
                _isDisposed = true;
            }
        }

        public class BssidChangeEventArgs : EventArgs
        {
            public string OldBssid { get; }
            public string NewBssid { get; }

            public BssidChangeEventArgs(string oldBssid, string newBssid)
            {
                OldBssid = oldBssid;
                NewBssid = newBssid;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}
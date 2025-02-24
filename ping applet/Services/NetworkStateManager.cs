using System;
using System.Diagnostics;
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
        private string _previousBssid;  // Added to track previous BSSID
        private int _currentSignalStrength;
        private int _currentChannel;
        private string _currentBand;
        private string _currentSsid; // Added to track SSID name
        private bool _isDisposed;
        private bool _isInitialCheck = true;
        private bool _isLocationServicesEnabled = true;
        private const int CHECK_INTERVAL = 1000; // 1 second

        // Updated event to include both old and new BSSIDs
        public event EventHandler<BssidChangeEventArgs> BssidChanged;
        public event EventHandler<int> SignalStrengthChanged;
        public event EventHandler<bool> LocationServicesStateChanged;

        public string CurrentBssid => _currentBssid;
        public string PreviousBssid => _previousBssid;  // Added property for previous BSSID
        public int CurrentSignalStrength => _currentSignalStrength;
        public int CurrentChannel => _currentChannel;
        public string CurrentBand => _currentBand;
        public string CurrentSsid => _currentSsid; // Added property for SSID

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
                _loggingService.LogInfo("Starting network state monitoring");

                // Perform initial state check immediately
                await CheckNetworkState();

                // Start periodic monitoring
                _monitorTimer = new Timer(CHECK_INTERVAL);
                _monitorTimer.Elapsed += async (s, e) => await CheckNetworkState();
                _monitorTimer.Start();

                _loggingService.LogInfo("Network state monitoring started successfully");
            }
            catch (Exception ex)
            {
                _loggingService.LogError("Failed to start network monitoring", ex);
                throw;
            }
        }

        private async Task CheckNetworkState()
        {
            if (_isDisposed) return;

            try
            {
                var (bssid, signalStrength, channel, band, ssid) = await GetWifiInfo();

                if (string.IsNullOrEmpty(bssid))
                {
                    if (_currentBssid != null)
                    {
                        _loggingService.LogInfo("WiFi connection lost");
                        _previousBssid = _currentBssid;  // Store the last known BSSID
                        _currentBssid = null;
                        _currentChannel = 0;
                        _currentBand = null;
                        _currentSsid = null; // Clear SSID
                        BssidChanged?.Invoke(this, new BssidChangeEventArgs(_previousBssid, null));
                    }
                    return;
                }

                // Handle BSSID changes
                if (bssid != _currentBssid)
                {
                    _previousBssid = _currentBssid;  // Store the old BSSID before updating
                    var oldBssid = _currentBssid ?? "none";
                    var oldChannel = _currentChannel;
                    var oldBand = _currentBand ?? "unknown";
                    var oldSsid = _currentSsid ?? "unknown";

                    _currentBssid = bssid;
                    _currentChannel = channel;
                    _currentBand = band;
                    _currentSsid = ssid;

                    if (_isInitialCheck)
                    {
                        _loggingService.LogInfo($"Initial connection - BSSID: {bssid}, Channel: {channel}, Band: {band}, SSID: {ssid}");
                        _isInitialCheck = false;
                    }
                    else
                    {
                        _loggingService.LogInfo(
                            $"Network transition detected:\n" +
                            $"  From: BSSID={oldBssid}, Channel={oldChannel}, Band={oldBand}, SSID={oldSsid}\n" +
                            $"  To: BSSID={bssid}, Channel={channel}, Band={band}, SSID={ssid}"
                        );
                    }
                    BssidChanged?.Invoke(this, new BssidChangeEventArgs(_previousBssid, bssid));
                }
                // Rest of the method remains unchanged
                else if (Math.Abs(signalStrength - _currentSignalStrength) > 5)
                {
                    var oldStrength = _currentSignalStrength;
                    _currentSignalStrength = signalStrength;
                    _loggingService.LogInfo(
                        $"Signal strength changed from {oldStrength}% to {signalStrength}% " +
                        $"(Channel: {channel}, Band: {band}, SSID: {ssid})"
                    );
                    SignalStrengthChanged?.Invoke(this, signalStrength);
                }
                else if (channel != _currentChannel || band != _currentBand || ssid != _currentSsid)
                {
                    // Also track SSID changes
                    _loggingService.LogInfo(
                        $"Network details changed for BSSID {bssid}:\n" +
                        $"  From: Channel={_currentChannel}, Band={_currentBand}, SSID={_currentSsid}\n" +
                        $"  To: Channel={channel}, Band={band}, SSID={ssid}"
                    );
                    _currentChannel = channel;
                    _currentBand = band;
                    _currentSsid = ssid;
                }

                // Always invoke BssidChanged on the initial check to update UI
                if (_isInitialCheck)
                {
                    BssidChanged?.Invoke(this, new BssidChangeEventArgs(null, bssid));
                }
            }
            catch (Exception ex)
            {
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
                    process.WaitForExit();

                    if (process.ExitCode != 0)
                    {
                        // Check if output contains location services error or elevation error
                        bool isLocationServicesError = output.Contains("Network shell commands need location permission");
                        bool isElevationError = output.Contains("error 5") && output.Contains("requires elevation");

                        if (isLocationServicesError || isElevationError)
                        {
                            if (_isLocationServicesEnabled)
                            {
                                _isLocationServicesEnabled = false;
                                LocationServicesStateChanged?.Invoke(this, false);
                                _loggingService.LogError("Location services are disabled or netsh requires elevation");
                            }
                        }
                        else
                        {
                            _loggingService.LogError($"netsh command failed with exit code: {process.ExitCode}");
                        }
                        return (null, 0, 0, null, null);
                    }

                    // If we got here successfully and location services were previously disabled,
                    // notify that they're now enabled
                    if (!_isLocationServicesEnabled)
                    {
                        _isLocationServicesEnabled = true;
                        LocationServicesStateChanged?.Invoke(this, true);
                        _loggingService.LogInfo("Location services are now enabled");
                    }

                    // Parse BSSID (XX:XX:XX:XX:XX:XX format)
                    var bssidMatch = Regex.Match(output, @"BSSID\s+:\s+([0-9A-Fa-f]{2}(?::[0-9A-Fa-f]{2}){5})",
                        RegexOptions.IgnoreCase);

                    // Parse Signal Strength (xx% format)
                    var signalMatch = Regex.Match(output, @"Signal\s+:\s+(\d+)%");

                    // Parse Channel and Band
                    var channelMatch = Regex.Match(output, @"Channel\s+:\s+(\d+)");
                    var bandMatch = Regex.Match(output, @"Band\s+:\s+(.+?)\r?\n");

                    // Parse SSID
                    var ssidMatch = Regex.Match(output, @"SSID\s+:\s+(.+?)\r?\n");

                    string bssid = bssidMatch.Success ? bssidMatch.Groups[1].Value : null;
                    int signalStrength = signalMatch.Success ? int.Parse(signalMatch.Groups[1].Value) : 0;
                    int channel = channelMatch.Success ? int.Parse(channelMatch.Groups[1].Value) : 0;
                    string band = bandMatch.Success ? bandMatch.Groups[1].Value.Trim() : null;
                    string ssid = ssidMatch.Success ? ssidMatch.Groups[1].Value.Trim() : null;

                    if (!bssidMatch.Success)
                    {
                        _loggingService.LogInfo("No BSSID found in netsh output");
                    }

                    return (bssid, signalStrength, channel, band, ssid);
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogError("Error getting WiFi info", ex);
                return (null, 0, 0, null, null);
            }
        }

        public void StopMonitoring()
        {
            try
            {
                _monitorTimer?.Stop();
                _monitorTimer?.Dispose();
                _monitorTimer = null;
                _loggingService.LogInfo("Network state monitoring stopped");
            }
            catch (Exception ex)
            {
                _loggingService.LogError("Error stopping network monitoring", ex);
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_isDisposed && disposing)
            {
                try
                {
                    StopMonitoring();
                    _isDisposed = true;
                    _loggingService.LogInfo("NetworkStateManager disposed");
                }
                catch (Exception ex)
                {
                    _loggingService.LogError("Error during NetworkStateManager disposal", ex);
                }
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
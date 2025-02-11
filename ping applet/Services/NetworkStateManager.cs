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
        private int _currentSignalStrength;
        private bool _isDisposed;
        private bool _isInitialCheck = true;
        private const int CHECK_INTERVAL = 1000; // 1 second

        public event EventHandler<string> BssidChanged;
        public event EventHandler<int> SignalStrengthChanged;

        public string CurrentBssid => _currentBssid;
        public int CurrentSignalStrength => _currentSignalStrength;

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

                // Initial state check
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
                var (bssid, signalStrength) = await GetWifiInfo();

                if (string.IsNullOrEmpty(bssid))
                {
                    if (_currentBssid != null)
                    {
                        _loggingService.LogInfo("WiFi connection lost");
                        _currentBssid = null;
                    }
                    return;
                }

                // Handle BSSID changes
                if (bssid != _currentBssid)
                {
                    if (_isInitialCheck)
                    {
                        _currentBssid = bssid;
                        _loggingService.LogInfo($"Initial BSSID detected: {bssid}");
                        _isInitialCheck = false;
                    }
                    else
                    {
                        var oldBssid = _currentBssid ?? "none";
                        _currentBssid = bssid;
                        _loggingService.LogInfo($"BSSID transition detected - From: {oldBssid} To: {bssid}");
                        BssidChanged?.Invoke(this, bssid);
                    }
                }

                // Handle signal strength changes
                if (Math.Abs(signalStrength - _currentSignalStrength) > 5)
                {
                    var oldStrength = _currentSignalStrength;
                    _currentSignalStrength = signalStrength;
                    _loggingService.LogInfo($"Signal strength changed from {oldStrength}% to {signalStrength}%");
                    SignalStrengthChanged?.Invoke(this, signalStrength);
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogError("Error checking network state", ex);
            }
        }

        private async Task<(string bssid, int signalStrength)> GetWifiInfo()
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
                        _loggingService.LogError($"netsh command failed with exit code: {process.ExitCode}");
                        return (null, 0);
                    }

                    // Parse BSSID (XX:XX:XX:XX:XX:XX format)
                    var bssidMatch = Regex.Match(output, @"BSSID\s+:\s+([0-9A-Fa-f]{2}(?::[0-9A-Fa-f]{2}){5})",
                        RegexOptions.IgnoreCase);

                    // Parse Signal Strength (xx% format)
                    var signalMatch = Regex.Match(output, @"Signal\s+:\s+(\d+)%");

                    string bssid = bssidMatch.Success ? bssidMatch.Groups[1].Value : null;
                    int signalStrength = signalMatch.Success ? int.Parse(signalMatch.Groups[1].Value) : 0;

                    // Log parsing failures for debugging
                    if (!bssidMatch.Success)
                    {
                        _loggingService.LogInfo("No BSSID found in netsh output");
                    }

                    return (bssid, signalStrength);
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogError("Error getting WiFi info", ex);
                return (null, 0);
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

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}
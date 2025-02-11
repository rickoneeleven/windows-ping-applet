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
        }

        public async Task StartMonitoring()
        {
            if (_isDisposed) throw new ObjectDisposedException(nameof(NetworkStateManager));

            // Initial check
            await CheckNetworkState();

            // Start periodic monitoring
            _monitorTimer = new Timer(CHECK_INTERVAL);
            _monitorTimer.Elapsed += async (s, e) => await CheckNetworkState();
            _monitorTimer.Start();
        }

        private async Task CheckNetworkState()
        {
            try
            {
                var (bssid, signalStrength) = await GetWifiInfo();

                // Check for BSSID change, ignoring the initial detection
                if (!string.IsNullOrEmpty(bssid))
                {
                    if (bssid != _currentBssid && !_isInitialCheck)
                    {
                        _currentBssid = bssid;
                        _loggingService.LogInfo($"BSSID changed to: {bssid}");
                        BssidChanged?.Invoke(this, bssid);
                    }
                    else if (_isInitialCheck)
                    {
                        _currentBssid = bssid;
                        _loggingService.LogInfo($"Initial BSSID detected: {bssid}");
                        _isInitialCheck = false;
                    }
                }

                // Check for significant signal strength change (>5%)
                if (Math.Abs(signalStrength - _currentSignalStrength) > 5)
                {
                    _currentSignalStrength = signalStrength;
                    _loggingService.LogInfo($"Signal strength changed to: {signalStrength}%");
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

                    // Match BSSID pattern XX:XX:XX:XX:XX:XX where X is a hex digit
                    var bssidMatch = Regex.Match(output, @"BSSID\s+:\s+([0-9A-Fa-f]{2}(?::[0-9A-Fa-f]{2}){5})");
                    string bssid = bssidMatch.Success ? bssidMatch.Groups[1].Value : null;

                    // Extract Signal Strength
                    var signalMatch = Regex.Match(output, @"Signal\s+:\s+(\d+)%");
                    int signalStrength = signalMatch.Success ? int.Parse(signalMatch.Groups[1].Value) : 0;

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
            _monitorTimer?.Stop();
            _monitorTimer?.Dispose();
            _monitorTimer = null;
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_isDisposed && disposing)
            {
                StopMonitoring();
                _isDisposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}
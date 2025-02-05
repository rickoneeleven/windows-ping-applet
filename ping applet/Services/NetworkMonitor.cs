using System;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
using ping_applet.Core.Interfaces;

namespace ping_applet.Services
{
    public class NetworkMonitor : INetworkMonitor
    {
        private bool isMonitoring;
        private string currentGateway;
        private bool isDisposed;

        public event EventHandler<string> GatewayChanged;
        public event EventHandler<bool> NetworkAvailabilityChanged;

        public bool IsNetworkAvailable => NetworkInterface.GetIsNetworkAvailable();
        public string CurrentGateway => currentGateway;

        public NetworkMonitor()
        {
            isMonitoring = false;
            isDisposed = false;
        }

        public async Task InitializeAsync()
        {
            if (isDisposed)
                throw new ObjectDisposedException(nameof(NetworkMonitor));

            await UpdateGateway(); // Initial gateway detection
            StartMonitoring();
        }

        public Task<bool> UpdateGateway()
        {
            if (isDisposed)
                throw new ObjectDisposedException(nameof(NetworkMonitor));

            try
            {
                string newGateway = GetDefaultGateway();

                if (newGateway != currentGateway)
                {
                    currentGateway = newGateway;
                    GatewayChanged?.Invoke(this, currentGateway);
                    return Task.FromResult(true);
                }

                return Task.FromResult(false);
            }
            catch (Exception)
            {
                currentGateway = null;
                GatewayChanged?.Invoke(this, null);
                return Task.FromResult(false);
            }
        }

        public string GetDefaultGateway()
        {
            if (isDisposed)
                throw new ObjectDisposedException(nameof(NetworkMonitor));

            NetworkInterface[] interfaces = NetworkInterface.GetAllNetworkInterfaces();

            var activeInterfaces = interfaces.Where(ni =>
                ni.OperationalStatus == OperationalStatus.Up &&
                ni.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                ni.Supports(NetworkInterfaceComponent.IPv4) &&
                ni.GetIPProperties().GatewayAddresses.Count > 0);

            foreach (var activeInterface in activeInterfaces)
            {
                var gateway = activeInterface.GetIPProperties()
                    .GatewayAddresses
                    .FirstOrDefault(ga =>
                        ga?.Address != null &&
                        !ga.Address.Equals(System.Net.IPAddress.Parse("0.0.0.0")))
                    ?.Address.ToString();

                if (!string.IsNullOrEmpty(gateway))
                {
                    return gateway;
                }
            }

            return null;
        }

        public void StartMonitoring()
        {
            if (isDisposed)
                throw new ObjectDisposedException(nameof(NetworkMonitor));

            if (!isMonitoring)
            {
                NetworkChange.NetworkAddressChanged += NetworkAddressChanged;
                NetworkChange.NetworkAvailabilityChanged += NetworkAvailabilityChangedHandler;
                isMonitoring = true;
            }
        }

        public void StopMonitoring()
        {
            if (isMonitoring)
            {
                NetworkChange.NetworkAddressChanged -= NetworkAddressChanged;
                NetworkChange.NetworkAvailabilityChanged -= NetworkAvailabilityChangedHandler;
                isMonitoring = false;
            }
        }

        private async void NetworkAddressChanged(object sender, EventArgs e)
        {
            if (!isDisposed)
            {
                await UpdateGateway();
            }
        }

        private void NetworkAvailabilityChangedHandler(object sender, NetworkAvailabilityEventArgs e)
        {
            if (!isDisposed)
            {
                NetworkAvailabilityChanged?.Invoke(this, e.IsAvailable);
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!isDisposed && disposing)
            {
                StopMonitoring();
                isDisposed = true;
                GC.SuppressFinalize(this);
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }
    }
}
using System;
using System.Threading.Tasks;

namespace ping_applet.Core.Interfaces
{
    public interface INetworkMonitor : IDisposable
    {
        string GetDefaultGateway();
        event EventHandler<string> GatewayChanged;
        event EventHandler<bool> NetworkAvailabilityChanged;
        bool IsNetworkAvailable { get; }
        string CurrentGateway { get; }
        Task InitializeAsync();
        Task<bool> UpdateGateway(bool forceUpdate = false);
        void StartMonitoring();
        void StopMonitoring();
    }
}
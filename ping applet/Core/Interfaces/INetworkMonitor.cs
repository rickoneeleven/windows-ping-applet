using System;
using System.Net.NetworkInformation;
using System.Threading.Tasks;

namespace ping_applet.Core.Interfaces
{
    /// <summary>
    /// Interface for monitoring network status and gateway information
    /// </summary>
    public interface INetworkMonitor : IDisposable
    {
        /// <summary>
        /// Gets the current gateway IP address by checking network interfaces
        /// </summary>
        /// <returns>The gateway IP address or null if not available</returns>
        string GetDefaultGateway();

        /// <summary>
        /// Event that fires when the gateway IP changes
        /// </summary>
        event EventHandler<string> GatewayChanged;

        /// <summary>
        /// Event that fires when network availability changes
        /// </summary>
        event EventHandler<bool> NetworkAvailabilityChanged;

        /// <summary>
        /// Gets whether a network connection is currently available
        /// </summary>
        bool IsNetworkAvailable { get; }

        /// <summary>
        /// Gets the current gateway IP address
        /// </summary>
        string CurrentGateway { get; }

        /// <summary>
        /// Initializes network monitoring and starts listening for network changes
        /// </summary>
        void Initialize();

        /// <summary>
        /// Updates the current gateway and notifies subscribers if changed
        /// </summary>
        /// <returns>True if gateway was updated, false if unchanged or unavailable</returns>
        Task<bool> UpdateGateway();

        /// <summary>
        /// Starts monitoring network changes
        /// </summary>
        void StartMonitoring();

        /// <summary>
        /// Stops monitoring network changes
        /// </summary>
        void StopMonitoring();
    }
}
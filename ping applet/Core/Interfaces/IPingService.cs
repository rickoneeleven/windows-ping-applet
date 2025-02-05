using System;
using System.Net.NetworkInformation;
using System.Threading.Tasks;

namespace ping_applet.Core.Interfaces
{
    /// <summary>
    /// Interface for handling ping operations
    /// </summary>
    public interface IPingService : IDisposable
    {
        /// <summary>
        /// Event that fires when a ping result is received
        /// </summary>
        event EventHandler<PingReply> PingCompleted;

        /// <summary>
        /// Event that fires when a ping error occurs
        /// </summary>
        event EventHandler<Exception> PingError;

        /// <summary>
        /// Gets whether a ping operation is currently in progress
        /// </summary>
        bool IsPinging { get; }

        /// <summary>
        /// Sends a ping to the specified address
        /// </summary>
        /// <param name="address">The IP address to ping</param>
        /// <param name="timeout">The timeout in milliseconds</param>
        /// <returns>A task representing the ping operation</returns>
        Task SendPingAsync(string address, int timeout);

        /// <summary>
        /// Starts the ping timer with the specified interval
        /// </summary>
        /// <param name="interval">The interval between pings in milliseconds</param>
        void StartPingTimer(int interval);

        /// <summary>
        /// Stops the ping timer
        /// </summary>
        void StopPingTimer();
    }
}
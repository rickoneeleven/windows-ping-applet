using System;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
using System.Timers;
using ping_applet.Core.Interfaces;

namespace ping_applet.Services
{
    public class PingService : IPingService
    {
        private readonly object pingLock = new object();
        private Timer pingTimer;
        private bool isDisposed;
        private volatile bool isPinging;
        private string currentAddress;
        private readonly byte[] buffer = new byte[32];
        private readonly PingOptions options;

        public event EventHandler<PingReply> PingCompleted;
        public event EventHandler<Exception> PingError;

        public bool IsPinging => isPinging;

        public PingService()
        {
            options = new PingOptions
            {
                DontFragment = false,
                Ttl = 128
            };
        }

        public async Task SendPingAsync(string address, int timeout)
        {
            if (isDisposed) throw new ObjectDisposedException(nameof(PingService));
            if (string.IsNullOrEmpty(address)) throw new ArgumentNullException(nameof(address));
            if (timeout <= 0) throw new ArgumentOutOfRangeException(nameof(timeout));

            if (isPinging) return;

            try
            {
                lock (pingLock)
                {
                    if (isPinging) return;
                    isPinging = true;
                    currentAddress = address;
                }

                using (var ping = new Ping())
                {
                    try
                    {
                        var reply = await ping.SendPingAsync(address, timeout, buffer, options);
                        if (!isDisposed)
                        {
                            PingCompleted?.Invoke(this, reply);
                        }
                    }
                    catch (PingException pex)
                    {
                        if (!isDisposed)
                        {
                            PingError?.Invoke(this, pex);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                if (!isDisposed)
                {
                    PingError?.Invoke(this, ex);
                }
            }
            finally
            {
                isPinging = false;
            }
        }

        public void StartPingTimer(int interval)
        {
            if (isDisposed) throw new ObjectDisposedException(nameof(PingService));
            if (interval <= 0) throw new ArgumentOutOfRangeException(nameof(interval));

            StopPingTimer(); // Ensure any existing timer is stopped

            pingTimer = new Timer(interval);
            pingTimer.Elapsed += async (sender, e) =>
            {
                if (!string.IsNullOrEmpty(currentAddress))
                {
                    await SendPingAsync(currentAddress, interval);
                }
            };
            pingTimer.Start();
        }

        public void StopPingTimer()
        {
            if (pingTimer != null)
            {
                pingTimer.Stop();
                pingTimer.Dispose();
                pingTimer = null;
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!isDisposed && disposing)
            {
                StopPingTimer();
                isDisposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}
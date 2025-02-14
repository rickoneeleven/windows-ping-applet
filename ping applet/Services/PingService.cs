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
        private Timer retryTimer;
        private bool isDisposed;
        private volatile bool isPinging;
        private string currentAddress;
        private readonly byte[] buffer = new byte[32];
        private readonly PingOptions options;
        private int consecutiveFailures;
        private readonly INetworkMonitor networkMonitor;

        private const int MAX_CONSECUTIVE_FAILURES = 5;
        private const int RETRY_INTERVAL = 10000; // 10 seconds

        public event EventHandler<PingReply> PingCompleted;
        public event EventHandler<Exception> PingError;

        public bool IsPinging => isPinging;

        public PingService(INetworkMonitor networkMonitor)
        {
            this.networkMonitor = networkMonitor ?? throw new ArgumentNullException(nameof(networkMonitor));

            options = new PingOptions
            {
                DontFragment = false,
                Ttl = 128
            };

            InitializeRetryTimer();
        }

        private void InitializeRetryTimer()
        {
            retryTimer = new Timer(RETRY_INTERVAL);
            retryTimer.AutoReset = true;
            retryTimer.Elapsed += async (s, e) => await HandleRetryTimerElapsed();
        }

        private async Task HandleRetryTimerElapsed()
        {
            if (consecutiveFailures >= MAX_CONSECUTIVE_FAILURES)
            {
                // Force a gateway refresh
                if (await networkMonitor.UpdateGateway())
                {
                    // If gateway changed, reset our state
                    ResetPingState();
                }
            }
        }

        private void ResetPingState()
        {
            lock (pingLock)
            {
                isPinging = false;
                consecutiveFailures = 0;
                currentAddress = networkMonitor.CurrentGateway;
            }
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
                            if (reply.Status == IPStatus.Success)
                            {
                                HandlePingSuccess(reply);
                            }
                            else
                            {
                                HandlePingFailure(reply);
                            }
                        }
                    }
                    catch (PingException pex)
                    {
                        if (!isDisposed)
                        {
                            HandlePingError(pex);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                if (!isDisposed)
                {
                    HandlePingError(ex);
                }
            }
            finally
            {
                isPinging = false;
            }
        }

        private void HandlePingSuccess(PingReply reply)
        {
            consecutiveFailures = 0;
            retryTimer.Stop();
            PingCompleted?.Invoke(this, reply);
        }

        private void HandlePingFailure(PingReply reply)
        {
            consecutiveFailures++;
            PingCompleted?.Invoke(this, reply);

            if (consecutiveFailures >= MAX_CONSECUTIVE_FAILURES)
            {
                // Start retry timer if not already running
                if (!retryTimer.Enabled)
                {
                    retryTimer.Start();
                }
            }
        }

        private void HandlePingError(Exception ex)
        {
            consecutiveFailures++;
            PingError?.Invoke(this, ex);

            if (consecutiveFailures >= MAX_CONSECUTIVE_FAILURES)
            {
                if (!retryTimer.Enabled)
                {
                    retryTimer.Start();
                }
            }
        }

        public void StartPingTimer(int interval)
        {
            if (isDisposed) throw new ObjectDisposedException(nameof(PingService));
            if (interval <= 0) throw new ArgumentOutOfRangeException(nameof(interval));

            StopPingTimer();

            pingTimer = new Timer(interval);
            pingTimer.Elapsed += async (sender, e) =>
            {
                // Always use the latest gateway address
                string latestGateway = networkMonitor.CurrentGateway;
                if (!string.IsNullOrEmpty(latestGateway))
                {
                    await SendPingAsync(latestGateway, interval);
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
                retryTimer?.Stop();
                retryTimer?.Dispose();
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
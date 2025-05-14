using System;
using System.Collections.Generic;
using System.Linq;
using ping_applet.Core.Interfaces;
using ping_applet.Utils.Models;
using System.Diagnostics; // For Debug.WriteLine

namespace ping_applet.Utils
{
    public class KnownAPManager : IDisposable
    {
        private readonly ILoggingService _loggingService;
        private readonly APSettingsStorage _settingsStorage;
        private readonly APDisplayFormatter _displayFormatter;

        private readonly Dictionary<string, string> _bssidToName;
        private readonly Dictionary<string, APDetails> _bssidToDetails;
        private readonly HashSet<string> _rootBssids;

        private APSettings _settings;
        private bool _isDisposed;

        public KnownAPManager(ILoggingService loggingService)
        {
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
            _settingsStorage = new APSettingsStorage(_loggingService);
            _displayFormatter = new APDisplayFormatter();

            _bssidToName = new Dictionary<string, string>();
            _bssidToDetails = new Dictionary<string, APDetails>();
            _rootBssids = new HashSet<string>();
            _settings = new APSettings();

            try
            {
                LoadSettings();
                _loggingService.LogInfo($"KnownAPManager initialized. LastCustomPingTarget on init: '{_settings.LastCustomPingTarget ?? "Not Set"}'.");
            }
            catch (Exception ex)
            {
                _loggingService.LogError("Failed to initialize KnownAPManager during LoadSettings.", ex);
                throw;
            }
        }

        public IEnumerable<string> RootBssids
        {
            get
            {
                ThrowIfDisposed();
                return _rootBssids.ToList();
            }
        }

        public IEnumerable<string> UnsortedBssids
        {
            get
            {
                ThrowIfDisposed();
                var allKnownKeys = _bssidToName.Keys.ToList();
                return allKnownKeys.Except(_rootBssids).ToList();
            }
        }

        public bool GetNotificationsEnabled()
        {
            ThrowIfDisposed();
            return _settings.NotificationsEnabled;
        }

        public void SetNotificationsEnabled(bool enabled)
        {
            ThrowIfDisposed();
            try
            {
                if (_settings.NotificationsEnabled != enabled)
                {
                    _settings.NotificationsEnabled = enabled;
                    SaveSettings();
                    _loggingService.LogInfo($"User notifications preference changed to: {(enabled ? "Enabled" : "Disabled")}");
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogError("Failed to set notifications state in KnownAPManager.", ex);
                throw;
            }
        }

        public string GetLastCustomPingTarget()
        {
            ThrowIfDisposed();
            Debug.WriteLine($"[KnownAPManager] GetLastCustomPingTarget called. Returning: '{_settings.LastCustomPingTarget ?? "null"}'");
            return _settings.LastCustomPingTarget;
        }

        public void SetLastCustomPingTarget(string target)
        {
            ThrowIfDisposed();
            try
            {
                string previousTarget = _settings.LastCustomPingTarget;
                if (previousTarget != target)
                {
                    _settings.LastCustomPingTarget = target;
                    SaveSettings();
                    _loggingService.LogInfo($"Last custom ping target changed from '{previousTarget ?? "Not Set"}' to '{target ?? "Not Set"}'.");
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"Failed to set last custom ping target to '{target}'.", ex);
                throw;
            }
        }

        public void AddNewAP(string bssid)
        {
            ThrowIfDisposed();
            if (string.IsNullOrEmpty(bssid))
                throw new ArgumentNullException(nameof(bssid));

            try
            {
                if (!_bssidToName.ContainsKey(bssid))
                {
                    _bssidToName[bssid] = bssid;
                    _bssidToDetails[bssid] = new APDetails();
                    SaveSettings();
                    _loggingService.LogInfo($"Added new AP (unsorted): {bssid}");
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"Failed to add new AP: {bssid}", ex);
                throw;
            }
        }

        public void UpdateAPDetails(string bssid, string band, string ssid)
        {
            ThrowIfDisposed();
            if (string.IsNullOrEmpty(bssid))
                throw new ArgumentNullException(nameof(bssid));

            try
            {
                if (!_bssidToName.ContainsKey(bssid))
                {
                    _loggingService.LogInfo($"AP {bssid} not known. Adding before updating details.");
                    AddNewAP(bssid);
                }

                var details = _bssidToDetails[bssid];
                bool updated = false;

                if (!string.IsNullOrEmpty(band) && details.Band != band)
                {
                    details.Band = band;
                    updated = true;
                }
                if (!string.IsNullOrEmpty(ssid) && details.SSID != ssid)
                {
                    details.SSID = ssid;
                    updated = true;
                }

                if (updated)
                {
                    _loggingService.LogInfo($"Updated details for AP {bssid}: Band='{band ?? "N/A"}', SSID='{ssid ?? "N/A"}'");
                    SaveSettings();
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"Failed to update details for AP {bssid}", ex);
            }
        }

        public void RenameAP(string bssid, string newName)
        {
            ThrowIfDisposed();
            if (string.IsNullOrEmpty(bssid)) throw new ArgumentNullException(nameof(bssid));
            if (string.IsNullOrWhiteSpace(newName)) throw new ArgumentException("New name cannot be empty or whitespace.", nameof(newName));

            try
            {
                if (_bssidToName.TryGetValue(bssid, out string oldName))
                {
                    if (oldName != newName)
                    {
                        _bssidToName[bssid] = newName;
                        SaveSettings();
                        _loggingService.LogInfo($"Renamed AP {bssid} from '{oldName}' to: '{newName}'");
                    }
                }
                else
                {
                    // Corrected LogWarn to LogInfo with a prefix
                    _loggingService.LogInfo($"WARNING: Attempted to rename non-existent AP: {bssid}.");
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"Failed to rename AP {bssid} to {newName}", ex);
                throw;
            }
        }

        public void SetAPRoot(string bssid, bool isRoot)
        {
            ThrowIfDisposed();
            if (string.IsNullOrEmpty(bssid)) throw new ArgumentNullException(nameof(bssid));

            try
            {
                if (!_bssidToName.ContainsKey(bssid))
                {
                    // Corrected LogWarn to LogInfo with a prefix
                    _loggingService.LogInfo($"WARNING: Attempted to set root status for non-existent AP: {bssid}.");
                    return;
                }

                bool changed = false;
                if (isRoot)
                {
                    if (_rootBssids.Add(bssid)) changed = true;
                }
                else
                {
                    if (_rootBssids.Remove(bssid)) changed = true;
                }

                if (changed)
                {
                    SaveSettings();
                    _loggingService.LogInfo($"Set AP {bssid} root status to: {isRoot}");
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"Failed to set root status for AP {bssid}", ex);
                throw;
            }
        }

        public void DeleteAP(string bssid)
        {
            ThrowIfDisposed();
            if (string.IsNullOrEmpty(bssid)) throw new ArgumentNullException(nameof(bssid));

            try
            {
                bool changed = false;
                if (_bssidToName.Remove(bssid)) changed = true;
                if (_bssidToDetails.Remove(bssid)) changed = true;
                if (_rootBssids.Remove(bssid)) changed = true;

                if (changed)
                {
                    SaveSettings();
                    _loggingService.LogInfo($"Deleted AP data for: {bssid}");
                }
                else
                {
                    _loggingService.LogInfo($"Attempted to delete AP {bssid}, but it was not found in any collection.");
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"Failed to delete AP: {bssid}", ex);
                throw;
            }
        }

        public string GetDisplayName(string bssid, bool includeDetails = true)
        {
            ThrowIfDisposed();
            if (string.IsNullOrEmpty(bssid))
                return _displayFormatter.FormatDisconnectedText();

            try
            {
                string baseName = _bssidToName.TryGetValue(bssid, out string name) ? name : bssid;
                if (!includeDetails || !_bssidToDetails.TryGetValue(bssid, out var details) || details == null)
                {
                    return baseName;
                }
                return _displayFormatter.FormatDisplayName(baseName, details);
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"Failed to get display name for AP: {bssid}", ex);
                return bssid;
            }
        }

        private void LoadSettings()
        {
            _settings = _settingsStorage.LoadSettings();

            _bssidToName.Clear();
            _bssidToDetails.Clear();
            _rootBssids.Clear();

            if (_settings.BssidToName != null)
            {
                foreach (var kvp in _settings.BssidToName) _bssidToName[kvp.Key] = kvp.Value;
            }
            if (_settings.BssidToDetails != null)
            {
                foreach (var kvp in _settings.BssidToDetails) _bssidToDetails[kvp.Key] = kvp.Value;
            }
            if (_settings.RootBssids != null)
            {
                foreach (var bssidValue in _settings.RootBssids) _rootBssids.Add(bssidValue); // Corrected variable name
            }
            _loggingService.LogInfo($"KnownAPManager: Settings loaded into working collections. Found {_bssidToName.Count} named APs, {_rootBssids.Count} root APs.");
        }

        private void SaveSettings()
        {
            _settings.BssidToName = new Dictionary<string, string>(_bssidToName);
            _settings.BssidToDetails = new Dictionary<string, APDetails>(_bssidToDetails);
            _settings.RootBssids = _rootBssids.ToList();

            if (!_settingsStorage.SaveSettings(_settings))
            {
                _loggingService.LogError("KnownAPManager: SaveSettings failed at APSettingsStorage level.");
            }
            else
            {
                _loggingService.LogInfo("KnownAPManager: Settings successfully prepared and saved via APSettingsStorage.");
            }
        }

        private void ThrowIfDisposed()
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(KnownAPManager));
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_isDisposed)
            {
                if (disposing)
                {
                    Debug.WriteLine("[KnownAPManager] Disposing.");
                    try
                    {
                        if (_settings != null && _settingsStorage != null)
                        {
                            _loggingService.LogInfo("KnownAPManager disposing. Attempting final save of settings.");
                            SaveSettings();
                        }

                        _settingsStorage?.Dispose();
                        _loggingService.LogInfo("KnownAPManager disposed.");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[KnownAPManager] Error during disposal: {ex.Message}");
                    }
                }
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
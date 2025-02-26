using System;
using System.Collections.Generic;
using System.Linq;
using ping_applet.Core.Interfaces;
using ping_applet.Utils.Models;

namespace ping_applet.Utils
{
    /// <summary>
    /// Manages the storage and organization of known access points (APs)
    /// </summary>
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

        /// <summary>
        /// Initializes a new instance of the KnownAPManager class
        /// </summary>
        /// <param name="loggingService">The logging service to use</param>
        public KnownAPManager(ILoggingService loggingService)
        {
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
            _settingsStorage = new APSettingsStorage(loggingService);
            _displayFormatter = new APDisplayFormatter();
            _bssidToName = new Dictionary<string, string>();
            _bssidToDetails = new Dictionary<string, APDetails>();
            _rootBssids = new HashSet<string>();
            _settings = new APSettings();

            try
            {
                LoadSettings();
                _loggingService.LogInfo("KnownAPManager initialized successfully");
            }
            catch (Exception ex)
            {
                _loggingService.LogError("Failed to initialize KnownAPManager", ex);
                throw;
            }
        }

        /// <summary>
        /// Gets all known BSSIDs that are in the root level
        /// </summary>
        public IEnumerable<string> RootBssids
        {
            get
            {
                ThrowIfDisposed();
                return _rootBssids.ToList();
            }
        }

        /// <summary>
        /// Gets all known BSSIDs that are in the unsorted category
        /// </summary>
        public IEnumerable<string> UnsortedBssids
        {
            get
            {
                ThrowIfDisposed();
                return _bssidToName.Keys.Except(_rootBssids).ToList();
            }
        }

        /// <summary>
        /// Gets whether notifications are enabled
        /// </summary>
        public bool GetNotificationsEnabled()
        {
            ThrowIfDisposed();
            return _settings.NotificationsEnabled;
        }

        /// <summary>
        /// Sets whether notifications are enabled
        /// </summary>
        public void SetNotificationsEnabled(bool enabled)
        {
            ThrowIfDisposed();
            try
            {
                _settings.NotificationsEnabled = enabled;
                SaveSettings();
                _loggingService.LogInfo($"Notifications {(enabled ? "enabled" : "disabled")}");
            }
            catch (Exception ex)
            {
                _loggingService.LogError("Failed to set notifications state", ex);
                throw;
            }
        }

        /// <summary>
        /// Adds a new AP to the unsorted category
        /// </summary>
        public void AddNewAP(string bssid)
        {
            ThrowIfDisposed();
            if (string.IsNullOrEmpty(bssid))
                throw new ArgumentNullException(nameof(bssid));

            try
            {
                if (!_bssidToName.ContainsKey(bssid))
                {
                    _bssidToName[bssid] = bssid; // Default name is the BSSID itself
                    _bssidToDetails[bssid] = new APDetails(); // Initialize with empty details
                    SaveSettings();
                    _loggingService.LogInfo($"Added new AP: {bssid}");
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"Failed to add new AP: {bssid}", ex);
                throw;
            }
        }

        /// <summary>
        /// Updates the network details for an AP
        /// </summary>
        public void UpdateAPDetails(string bssid, string band, string ssid)
        {
            ThrowIfDisposed();
            if (string.IsNullOrEmpty(bssid))
                throw new ArgumentNullException(nameof(bssid));

            try
            {
                // Ensure the AP exists in our dictionary
                if (!_bssidToName.ContainsKey(bssid))
                {
                    AddNewAP(bssid);
                }

                // Create or update details
                if (!_bssidToDetails.TryGetValue(bssid, out var details))
                {
                    details = new APDetails();
                    _bssidToDetails[bssid] = details;
                }

                // Only update if we have valid data
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
                    _loggingService.LogInfo($"Updated details for AP {bssid}: Band={band}, SSID={ssid}");
                    SaveSettings();
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"Failed to update details for AP {bssid}", ex);
                // Don't throw - this is a non-critical operation
            }
        }

        /// <summary>
        /// Renames an AP with a custom friendly name
        /// </summary>
        public void RenameAP(string bssid, string newName)
        {
            ThrowIfDisposed();
            if (string.IsNullOrEmpty(bssid))
                throw new ArgumentNullException(nameof(bssid));
            if (string.IsNullOrEmpty(newName))
                throw new ArgumentNullException(nameof(newName));

            try
            {
                if (_bssidToName.ContainsKey(bssid))
                {
                    _bssidToName[bssid] = newName;
                    SaveSettings();
                    _loggingService.LogInfo($"Renamed AP {bssid} to: {newName}");
                }
                else
                {
                    throw new KeyNotFoundException($"BSSID not found: {bssid}");
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"Failed to rename AP {bssid} to {newName}", ex);
                throw;
            }
        }

        /// <summary>
        /// Moves an AP to the root level or back to unsorted
        /// </summary>
        public void SetAPRoot(string bssid, bool isRoot)
        {
            ThrowIfDisposed();
            if (string.IsNullOrEmpty(bssid))
                throw new ArgumentNullException(nameof(bssid));

            try
            {
                if (!_bssidToName.ContainsKey(bssid))
                {
                    throw new KeyNotFoundException($"BSSID not found: {bssid}");
                }

                if (isRoot)
                {
                    _rootBssids.Add(bssid);
                }
                else
                {
                    _rootBssids.Remove(bssid);
                }

                SaveSettings();
                _loggingService.LogInfo($"Set AP {bssid} root status to: {isRoot}");
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"Failed to set root status for AP {bssid}", ex);
                throw;
            }
        }

        /// <summary>
        /// Deletes an AP from the known list
        /// </summary>
        public void DeleteAP(string bssid)
        {
            ThrowIfDisposed();
            if (string.IsNullOrEmpty(bssid))
                throw new ArgumentNullException(nameof(bssid));

            try
            {
                _bssidToName.Remove(bssid);
                _bssidToDetails.Remove(bssid);
                _rootBssids.Remove(bssid);
                SaveSettings();
                _loggingService.LogInfo($"Deleted AP: {bssid}");
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"Failed to delete AP: {bssid}", ex);
                throw;
            }
        }

        /// <summary>
        /// Gets the display name for a BSSID, with optional network details
        /// </summary>
        public string GetDisplayName(string bssid, bool includeDetails = true)
        {
            ThrowIfDisposed();

            if (string.IsNullOrEmpty(bssid))
                return _displayFormatter.FormatDisconnectedText();

            try
            {
                // Get the base name (custom or BSSID)
                string baseName = _bssidToName.TryGetValue(bssid, out string name) ? name : bssid;

                // If we're not including details or no details exist, return just the name
                if (!includeDetails || !_bssidToDetails.TryGetValue(bssid, out var details))
                    return baseName;

                // Use formatter to build the formatted name with network details
                return _displayFormatter.FormatDisplayName(baseName, details);
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"Failed to get display name for AP: {bssid}", ex);
                return bssid; // Fallback to BSSID on error
            }
        }

        /// <summary>
        /// Loads settings from persistent storage
        /// </summary>
        private void LoadSettings()
        {
            try
            {
                _settings = _settingsStorage.LoadSettings();

                _bssidToName.Clear();
                foreach (var kvp in _settings.BssidToName)
                {
                    _bssidToName[kvp.Key] = kvp.Value;
                }

                _bssidToDetails.Clear();
                foreach (var kvp in _settings.BssidToDetails)
                {
                    _bssidToDetails[kvp.Key] = kvp.Value;
                }

                _rootBssids.Clear();
                foreach (var bssid in _settings.RootBssids)
                {
                    _rootBssids.Add(bssid);
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogError("Error loading settings, using defaults", ex);
                _settings = new APSettings();
            }
        }

        /// <summary>
        /// Saves settings to persistent storage
        /// </summary>
        private void SaveSettings()
        {
            try
            {
                _settings.BssidToName = new Dictionary<string, string>(_bssidToName);
                _settings.BssidToDetails = new Dictionary<string, APDetails>(_bssidToDetails);
                _settings.RootBssids = _rootBssids.ToList();

                if (!_settingsStorage.SaveSettings(_settings))
                {
                    _loggingService.LogError("Failed to save settings via storage");
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogError("Error saving settings", ex);
                throw;
            }
        }

        /// <summary>
        /// Throws an ObjectDisposedException if this object has been disposed
        /// </summary>
        private void ThrowIfDisposed()
        {
            if (_isDisposed)
            {
                throw new ObjectDisposedException(nameof(KnownAPManager));
            }
        }

        /// <summary>
        /// Disposes resources used by this class
        /// </summary>
        /// <param name="disposing">Whether to dispose managed resources</param>
        protected virtual void Dispose(bool disposing)
        {
            if (!_isDisposed && disposing)
            {
                try
                {
                    SaveSettings();
                    _settingsStorage.Dispose();
                }
                catch (Exception ex)
                {
                    _loggingService.LogError("Error during disposal", ex);
                }
                _isDisposed = true;
            }
        }

        /// <summary>
        /// Disposes resources used by this class
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}
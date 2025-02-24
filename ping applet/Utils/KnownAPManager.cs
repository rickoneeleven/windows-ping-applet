using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using ping_applet.Core.Interfaces;

namespace ping_applet.Utils
{
    /// <summary>
    /// Manages the storage and organization of known access points (APs)
    /// </summary>
    public class KnownAPManager : IDisposable
    {
        private readonly ILoggingService _loggingService;
        private readonly string _settingsPath;
        private readonly Dictionary<string, string> _bssidToName;
        private readonly Dictionary<string, APDetails> _bssidToDetails; // New - stores additional AP details
        private readonly HashSet<string> _rootBssids;
        private readonly object _lockObject = new object();
        private bool _isDisposed;
        private StoredSettings settings;

        /// <summary>
        /// Initializes a new instance of the KnownAPManager class
        /// </summary>
        /// <param name="loggingService">The logging service to use</param>
        public KnownAPManager(ILoggingService loggingService)
        {
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
            _settingsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PingApplet",
                "known_aps.json"
            );
            _bssidToName = new Dictionary<string, string>();
            _bssidToDetails = new Dictionary<string, APDetails>();
            _rootBssids = new HashSet<string>();
            settings = new StoredSettings();

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
                lock (_lockObject)
                {
                    return _rootBssids.ToList();
                }
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
                lock (_lockObject)
                {
                    return _bssidToName.Keys.Except(_rootBssids).ToList();
                }
            }
        }

        /// <summary>
        /// Gets whether notifications are enabled
        /// </summary>
        public bool GetNotificationsEnabled()
        {
            ThrowIfDisposed();
            lock (_lockObject)
            {
                return settings.NotificationsEnabled;
            }
        }

        /// <summary>
        /// Sets whether notifications are enabled
        /// </summary>
        public void SetNotificationsEnabled(bool enabled)
        {
            ThrowIfDisposed();
            lock (_lockObject)
            {
                settings.NotificationsEnabled = enabled;
                SaveSettings();
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
                lock (_lockObject)
                {
                    if (!_bssidToName.ContainsKey(bssid))
                    {
                        _bssidToName[bssid] = bssid; // Default name is the BSSID itself
                        _bssidToDetails[bssid] = new APDetails(); // Initialize with empty details
                        SaveSettings();
                        _loggingService.LogInfo($"Added new AP: {bssid}");
                    }
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
                lock (_lockObject)
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
                lock (_lockObject)
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
                lock (_lockObject)
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
                lock (_lockObject)
                {
                    _bssidToName.Remove(bssid);
                    _bssidToDetails.Remove(bssid);
                    _rootBssids.Remove(bssid);
                    SaveSettings();
                    _loggingService.LogInfo($"Deleted AP: {bssid}");
                }
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
                return "Not Connected";

            try
            {
                lock (_lockObject)
                {
                    // Get the base name (custom or BSSID)
                    string baseName = _bssidToName.TryGetValue(bssid, out string name) ? name : bssid;

                    // If we're not including details or no details exist, return just the name
                    if (!includeDetails || !_bssidToDetails.TryGetValue(bssid, out var details))
                        return baseName;

                    // Build the formatted name with network details
                    return FormatDisplayName(baseName, details);
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"Failed to get display name for AP: {bssid}", ex);
                return bssid; // Fallback to BSSID on error
            }
        }

        /// <summary>
        /// Formats the display name with network details
        /// </summary>
        private string FormatDisplayName(string baseName, APDetails details)
        {
            // If we don't have any details, just return the base name
            if (string.IsNullOrEmpty(details.Band) && string.IsNullOrEmpty(details.SSID))
                return baseName;

            // Build details string
            string detailsStr = "";
            if (!string.IsNullOrEmpty(details.Band))
                detailsStr += details.Band;

            if (!string.IsNullOrEmpty(details.SSID))
            {
                if (!string.IsNullOrEmpty(detailsStr))
                    detailsStr += " - ";
                detailsStr += details.SSID;
            }

            // Return formatted name
            return $"{baseName} ({detailsStr})";
        }

        private void LoadSettings()
        {
            try
            {
                if (File.Exists(_settingsPath))
                {
                    var json = File.ReadAllText(_settingsPath);
                    settings = JsonConvert.DeserializeObject<StoredSettings>(json) ?? new StoredSettings();

                    lock (_lockObject)
                    {
                        _bssidToName.Clear();
                        foreach (var kvp in settings.BssidToName)
                        {
                            _bssidToName[kvp.Key] = kvp.Value;
                        }

                        _bssidToDetails.Clear();
                        foreach (var kvp in settings.BssidToDetails ?? new Dictionary<string, APDetails>())
                        {
                            _bssidToDetails[kvp.Key] = kvp.Value;
                        }

                        _rootBssids.Clear();
                        foreach (var bssid in settings.RootBssids)
                        {
                            _rootBssids.Add(bssid);
                        }
                    }

                    _loggingService.LogInfo("Settings loaded successfully");
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogError("Failed to load settings", ex);
                settings = new StoredSettings(); // Use defaults on error
            }
        }

        private void SaveSettings()
        {
            try
            {
                var settingsToSave = new StoredSettings
                {
                    BssidToName = new Dictionary<string, string>(_bssidToName),
                    BssidToDetails = new Dictionary<string, APDetails>(_bssidToDetails),
                    RootBssids = _rootBssids.ToList(),
                    NotificationsEnabled = settings.NotificationsEnabled
                };

                var json = JsonConvert.SerializeObject(settingsToSave, Formatting.Indented);
                File.WriteAllText(_settingsPath, json);
            }
            catch (Exception ex)
            {
                _loggingService.LogError("Failed to save settings", ex);
                throw;
            }
        }

        private void ThrowIfDisposed()
        {
            if (_isDisposed)
            {
                throw new ObjectDisposedException(nameof(KnownAPManager));
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_isDisposed && disposing)
            {
                try
                {
                    SaveSettings();
                }
                catch (Exception ex)
                {
                    _loggingService.LogError("Error during disposal", ex);
                }
                _isDisposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Class to store additional details about an AP
        /// </summary>
        public class APDetails
        {
            public string Band { get; set; }
            public string SSID { get; set; }
        }

        private class StoredSettings
        {
            public Dictionary<string, string> BssidToName { get; set; } = new Dictionary<string, string>();
            public Dictionary<string, APDetails> BssidToDetails { get; set; } = new Dictionary<string, APDetails>();
            public List<string> RootBssids { get; set; } = new List<string>();
            public bool NotificationsEnabled { get; set; } = true;
        }
    }
}
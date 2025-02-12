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
        private readonly HashSet<string> _rootBssids;
        private readonly object _lockObject = new object();
        private bool _isDisposed;

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
            _rootBssids = new HashSet<string>();

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
        /// Gets the display name for a BSSID
        /// </summary>
        public string GetDisplayName(string bssid)
        {
            ThrowIfDisposed();
            if (string.IsNullOrEmpty(bssid))
                throw new ArgumentNullException(nameof(bssid));

            try
            {
                lock (_lockObject)
                {
                    return _bssidToName.TryGetValue(bssid, out string name) ? name : bssid;
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"Failed to get display name for AP: {bssid}", ex);
                throw;
            }
        }

        private void LoadSettings()
        {
            try
            {
                if (File.Exists(_settingsPath))
                {
                    var json = File.ReadAllText(_settingsPath);
                    var settings = JsonConvert.DeserializeObject<StoredSettings>(json);

                    lock (_lockObject)
                    {
                        _bssidToName.Clear();
                        foreach (var kvp in settings.BssidToName)
                        {
                            _bssidToName[kvp.Key] = kvp.Value;
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
                // Continue with empty settings rather than throwing
            }
        }

        private void SaveSettings()
        {
            try
            {
                var settings = new StoredSettings
                {
                    BssidToName = _bssidToName.ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
                    RootBssids = _rootBssids.ToList()
                };

                var json = JsonConvert.SerializeObject(settings, Formatting.Indented);
                var directory = Path.GetDirectoryName(_settingsPath);

                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(_settingsPath, json);
                _loggingService.LogInfo("Settings saved successfully");
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

        private class StoredSettings
        {
            public Dictionary<string, string> BssidToName { get; set; } = new Dictionary<string, string>();
            public List<string> RootBssids { get; set; } = new List<string>();
        }
    }
}
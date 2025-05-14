using System;
using System.IO;
using Newtonsoft.Json;
using ping_applet.Core.Interfaces;
using ping_applet.Utils.Models;
using System.Diagnostics; // For Debug.WriteLine

namespace ping_applet.Utils
{
    public class APSettingsStorage : IDisposable
    {
        private readonly ILoggingService _loggingService;
        private readonly string _settingsPath;
        private readonly object _fileLock = new object(); // Renamed for clarity to avoid confusion with thread locks
        private bool _isDisposed;

        public APSettingsStorage(ILoggingService loggingService)
        {
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
            _settingsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PingApplet",
                "known_aps.json"
            );
            Debug.WriteLine($"[APSettingsStorage] Settings path configured to: {_settingsPath}");
        }

        public APSettings LoadSettings()
        {
            if (_isDisposed) throw new ObjectDisposedException(nameof(APSettingsStorage));
            Debug.WriteLine("[APSettingsStorage] Attempting to load settings.");

            try
            {
                lock (_fileLock)
                {
                    if (File.Exists(_settingsPath))
                    {
                        _loggingService.LogInfo($"Loading AP settings from: {_settingsPath}");
                        var json = File.ReadAllText(_settingsPath);
                        var settings = JsonConvert.DeserializeObject<APSettings>(json);

                        if (settings == null)
                        {
                            _loggingService.LogInfo("AP settings file was empty or invalid; creating new default settings.");
                            settings = new APSettings();
                        }
                        else
                        {
                            // Ensure collections are initialized if JSON had nulls (defensive)
                            settings.BssidToName ??= new System.Collections.Generic.Dictionary<string, string>();
                            settings.BssidToDetails ??= new System.Collections.Generic.Dictionary<string, APDetails>();
                            settings.RootBssids ??= new System.Collections.Generic.List<string>();
                            // LastCustomPingTarget will be null if not in JSON, which is fine.
                            _loggingService.LogInfo($"AP settings loaded successfully. LastCustomPingTarget: '{settings.LastCustomPingTarget ?? "Not Set"}'.");
                        }
                        return settings;
                    }
                    else
                    {
                        _loggingService.LogInfo($"AP settings file not found at {_settingsPath}. Returning new default settings.");
                        return new APSettings(); // Return new object with defaults
                    }
                }
            }
            catch (JsonException jsonEx)
            {
                _loggingService.LogError($"Failed to deserialize AP settings from {_settingsPath}. File might be corrupt. Returning defaults.", jsonEx);
                return new APSettings(); // Critical to return defaults on error
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"Failed to load AP settings from {_settingsPath}. Returning defaults.", ex);
                return new APSettings(); // Critical to return defaults on error
            }
        }

        public bool SaveSettings(APSettings settings)
        {
            if (_isDisposed) throw new ObjectDisposedException(nameof(APSettingsStorage));
            if (settings == null)
            {
                _loggingService.LogError("Attempted to save null APSettings. Operation aborted.", new ArgumentNullException(nameof(settings)));
                return false;
            }
            Debug.WriteLine("[APSettingsStorage] Attempting to save settings.");

            try
            {
                lock (_fileLock)
                {
                    string directory = Path.GetDirectoryName(_settingsPath);
                    if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                    {
                        _loggingService.LogInfo($"Creating directory for AP settings: {directory}");
                        Directory.CreateDirectory(directory);
                    }

                    var json = JsonConvert.SerializeObject(settings, Formatting.Indented);
                    File.WriteAllText(_settingsPath, json);
                    _loggingService.LogInfo($"AP settings saved successfully to {_settingsPath}. LastCustomPingTarget: '{settings.LastCustomPingTarget ?? "Not Set"}'.");
                    return true;
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"Failed to save AP settings to {_settingsPath}", ex);
                return false;
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_isDisposed)
            {
                if (disposing)
                {
                    // No managed resources to dispose directly in this class other than what the GC handles.
                    // _loggingService is injected and disposed by its owner.
                    Debug.WriteLine("[APSettingsStorage] Disposed.");
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
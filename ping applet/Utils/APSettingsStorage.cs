using System;
using System.IO;
using Newtonsoft.Json;
using ping_applet.Core.Interfaces;
using ping_applet.Utils.Models;

namespace ping_applet.Utils
{
    /// <summary>
    /// Handles loading and saving of AP settings to persistent storage
    /// </summary>
    public class APSettingsStorage : IDisposable
    {
        private readonly ILoggingService _loggingService;
        private readonly string _settingsPath;
        private readonly object _lockObject = new object();
        private bool _isDisposed;

        /// <summary>
        /// Initializes a new instance of the APSettingsStorage class
        /// </summary>
        /// <param name="loggingService">The logging service to use</param>
        public APSettingsStorage(ILoggingService loggingService)
        {
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
            _settingsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PingApplet",
                "known_aps.json"
            );
        }

        /// <summary>
        /// Loads settings from the persistent storage
        /// </summary>
        /// <returns>The loaded settings or default settings if loading fails</returns>
        public APSettings LoadSettings()
        {
            try
            {
                if (File.Exists(_settingsPath))
                {
                    lock (_lockObject)
                    {
                        var json = File.ReadAllText(_settingsPath);
                        var settings = JsonConvert.DeserializeObject<APSettings>(json) ?? new APSettings();

                        // Ensure all collections are initialized
                        settings.BssidToName ??= new System.Collections.Generic.Dictionary<string, string>();
                        settings.BssidToDetails ??= new System.Collections.Generic.Dictionary<string, APDetails>();
                        settings.RootBssids ??= new System.Collections.Generic.List<string>();

                        _loggingService.LogInfo("AP settings loaded successfully");
                        return settings;
                    }
                }

                return new APSettings();
            }
            catch (Exception ex)
            {
                _loggingService.LogError("Failed to load AP settings", ex);
                return new APSettings(); // Use defaults on error
            }
        }

        /// <summary>
        /// Saves settings to the persistent storage
        /// </summary>
        /// <param name="settings">The settings to save</param>
        /// <returns>True if saving was successful, false otherwise</returns>
        public bool SaveSettings(APSettings settings)
        {
            try
            {
                lock (_lockObject)
                {
                    // Ensure directory exists
                    string directory = Path.GetDirectoryName(_settingsPath);
                    if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    var json = JsonConvert.SerializeObject(settings, Formatting.Indented);
                    File.WriteAllText(_settingsPath, json);
                    return true;
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogError("Failed to save AP settings", ex);
                return false;
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

        /// <summary>
        /// Disposes resources used by this class
        /// </summary>
        /// <param name="disposing">Whether to dispose managed resources</param>
        protected virtual void Dispose(bool disposing)
        {
            if (!_isDisposed && disposing)
            {
                _isDisposed = true;
            }
        }
    }
}
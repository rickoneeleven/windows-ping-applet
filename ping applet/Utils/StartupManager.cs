using System;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ping_applet.Utils
{
    /// <summary>
    /// Manages application startup settings using the Windows Registry
    /// </summary>
    public class StartupManager
    {
        private const string RUN_LOCATION = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string APP_NAME = "PingApplet";
        private readonly string executablePath;

        public StartupManager()
        {
            executablePath = Application.ExecutablePath;
        }

        /// <summary>
        /// Checks if the application is configured to run at startup
        /// </summary>
        public bool IsStartupEnabled()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RUN_LOCATION))
                {
                    return key?.GetValue(APP_NAME)?.ToString() == executablePath;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Enables or disables running the application at startup
        /// </summary>
        public bool SetStartupEnabled(bool enable)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RUN_LOCATION, true))
                {
                    if (key == null)
                        return false;

                    if (enable)
                    {
                        key.SetValue(APP_NAME, executablePath);
                    }
                    else
                    {
                        key.DeleteValue(APP_NAME, false);
                    }

                    return true;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
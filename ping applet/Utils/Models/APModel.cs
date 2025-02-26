using System.Collections.Generic;

namespace ping_applet.Utils.Models
{
    /// <summary>
    /// Models for AP data persistence and management
    /// </summary>
    public class APSettings
    {
        /// <summary>
        /// Maps BSSID to custom name
        /// </summary>
        public Dictionary<string, string> BssidToName { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// Maps BSSID to additional details
        /// </summary>
        public Dictionary<string, APDetails> BssidToDetails { get; set; } = new Dictionary<string, APDetails>();

        /// <summary>
        /// BSSIDs that are in the root level
        /// </summary>
        public List<string> RootBssids { get; set; } = new List<string>();

        /// <summary>
        /// Whether notifications are enabled
        /// </summary>
        public bool NotificationsEnabled { get; set; } = true;
    }

    /// <summary>
    /// Additional details about an access point
    /// </summary>
    public class APDetails
    {
        /// <summary>
        /// The frequency band of the AP (e.g., "2.4 GHz", "5 GHz")
        /// </summary>
        public string Band { get; set; }

        /// <summary>
        /// The SSID (network name) of the AP
        /// </summary>
        public string SSID { get; set; }
    }
}
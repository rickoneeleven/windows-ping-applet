using System.Collections.Generic;
using Newtonsoft.Json; // Required for JsonProperty if we want to control serialization name explicitly

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
        [JsonProperty("BssidToName")]
        public Dictionary<string, string> BssidToName { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// Maps BSSID to additional details
        /// </summary>
        [JsonProperty("BssidToDetails")]
        public Dictionary<string, APDetails> BssidToDetails { get; set; } = new Dictionary<string, APDetails>();

        /// <summary>
        /// BSSIDs that are in the root level
        /// </summary>
        [JsonProperty("RootBssids")]
        public List<string> RootBssids { get; set; } = new List<string>();

        /// <summary>
        /// Whether notifications are enabled
        /// </summary>
        [JsonProperty("NotificationsEnabled")]
        public bool NotificationsEnabled { get; set; } = true;

        /// <summary>
        /// Stores the last successfully used custom ping target (IP address or FQDN).
        /// Optional; null or empty if not set or if default gateway is preferred.
        /// </summary>
        [JsonProperty("LastCustomPingTarget", NullValueHandling = NullValueHandling.Ignore)] // Only serialize if not null
        public string LastCustomPingTarget { get; set; }
    }

    /// <summary>
    /// Additional details about an access point
    /// </summary>
    public class APDetails
    {
        /// <summary>
        /// The frequency band of the AP (e.g., "802.11ax", "802.11ac")
        /// </summary>
        [JsonProperty("Band")]
        public string Band { get; set; }

        /// <summary>
        /// The SSID (network name) of the AP
        /// </summary>
        [JsonProperty("SSID")]
        public string SSID { get; set; }
    }
}
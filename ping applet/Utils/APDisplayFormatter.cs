using ping_applet.Utils.Models;

namespace ping_applet.Utils
{
    /// <summary>
    /// Handles formatting of AP display names and related UI text
    /// </summary>
    public class APDisplayFormatter
    {
        /// <summary>
        /// Formats the display name for an AP with optional network details
        /// </summary>
        /// <param name="baseName">The base name (custom name or BSSID)</param>
        /// <param name="details">The AP details to include</param>
        /// <returns>The formatted display name</returns>
        public string FormatDisplayName(string baseName, APDetails details)
        {
            // If no details provided or details are empty, return just the base name
            if (details == null || (string.IsNullOrEmpty(details.Band) && string.IsNullOrEmpty(details.SSID)))
            {
                return baseName;
            }

            // Build details string
            string detailsStr = "";

            if (!string.IsNullOrEmpty(details.Band))
            {
                detailsStr += details.Band;
            }

            if (!string.IsNullOrEmpty(details.SSID))
            {
                if (!string.IsNullOrEmpty(detailsStr))
                {
                    detailsStr += " - ";
                }
                detailsStr += details.SSID;
            }

            // Return formatted name
            return $"{baseName} ({detailsStr})";
        }

        /// <summary>
        /// Formats the default text for disconnected state
        /// </summary>
        /// <returns>The formatted text for disconnected state</returns>
        public string FormatDisconnectedText()
        {
            return "Not Connected";
        }
    }
}
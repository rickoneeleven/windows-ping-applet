using System;
using System.Linq;
using System.Reflection;

namespace ping_applet.Utils
{
    /// <summary>
    /// Provides centralized access to build and version information
    /// </summary>
    public class BuildInfoProvider
    {
        private readonly Assembly _assembly;

        public string BuildTimestamp { get; }
        public Version Version { get; }
        public string VersionString { get; }
        public string BuildInfo { get; }

        public BuildInfoProvider()
        {
            _assembly = Assembly.GetExecutingAssembly();

            // Get build timestamp
            BuildTimestamp = GetBuildTimestamp();

            // Generate version from build timestamp
            Version = GenerateVersionFromTimestamp(BuildTimestamp);
            VersionString = $"{Version.Major}.{Version.Minor}.{Version.Build}.{Version.Revision}";

            // Combine for full build info
            BuildInfo = $"v{VersionString} ({BuildTimestamp})";
        }

        private string GetBuildTimestamp()
        {
            try
            {
                var attribute = _assembly.GetCustomAttributes(typeof(AssemblyMetadataAttribute), false)
                    .Cast<AssemblyMetadataAttribute>()
                    .FirstOrDefault(attr => attr.Key == "BuildTimestamp");

                return attribute?.Value ?? "Unknown";
            }
            catch
            {
                return "Unknown";
            }
        }

        private Version GenerateVersionFromTimestamp(string timestamp)
        {
            try
            {
                // Expected format: "dd/MM/yy HH:mm"
                if (DateTime.TryParseExact(timestamp, "dd/MM/yy HH:mm",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None,
                    out DateTime buildDate))
                {
                    // Major = Year (2-digit)
                    int major = buildDate.Year % 100;

                    // Minor = Month
                    int minor = buildDate.Month;

                    // Build = Day
                    int build = buildDate.Day;

                    // Revision = Hour * 100 + Minute
                    // This gives us a number between 0-2359
                    int revision = (buildDate.Hour * 100) + buildDate.Minute;

                    return new Version(major, minor, build, revision);
                }

                // Fallback version if parsing fails
                return new Version(1, 0, 0, 0);
            }
            catch
            {
                return new Version(1, 0, 0, 0);
            }
        }
    }
}
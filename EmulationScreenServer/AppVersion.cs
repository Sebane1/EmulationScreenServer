using System.Reflection;

namespace EmulationScreenServer
{
    public static class AppVersion
    {
        public static string Current =>
            Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
            ?? "0.0.0";

        public static bool TryParseVersion(string? value, out Version version)
        {
            version = new Version(0, 0, 0);
            if (string.IsNullOrWhiteSpace(value))
                return false;

            var trimmed = value.Trim();
            if (trimmed.StartsWith("v", StringComparison.OrdinalIgnoreCase))
                trimmed = trimmed[1..];

            var dash = trimmed.IndexOf('-');
            if (dash >= 0)
                trimmed = trimmed[..dash];

            return Version.TryParse(trimmed, out version!);
        }

        public static bool IsNewerThanCurrent(string remoteVersion)
        {
            if (!TryParseVersion(remoteVersion, out var remote))
                return false;
            if (!TryParseVersion(Current, out var current))
                return true;

            return remote > current;
        }
    }
}

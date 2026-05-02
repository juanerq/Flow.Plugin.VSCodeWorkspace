using System;

namespace Flow.Plugin.VSCodeWorkspaces.VSCodeHelper
{
    internal static class ProfileIconHelper
    {
        public static string GetIconPath(string profileName)
        {
            if (string.IsNullOrWhiteSpace(profileName))
                return null;

            var normalized = Normalize(profileName);

            if (normalized.Contains("astro", StringComparison.OrdinalIgnoreCase))
                return @"Images\icons\astro-svgrepo-com.svg";

            if (normalized.Contains("python", StringComparison.OrdinalIgnoreCase) ||
                normalized.Contains("py", StringComparison.OrdinalIgnoreCase))
            {
                return @"Images\icons\python-svgrepo-com.svg";
            }

            if (normalized.Contains("golang", StringComparison.OrdinalIgnoreCase) ||
                normalized.Contains("go", StringComparison.OrdinalIgnoreCase))
            {
                return @"Images\icons\go-svgrepo-com.svg";
            }

            if (normalized.Contains("node", StringComparison.OrdinalIgnoreCase) ||
                normalized.Contains("nodejs", StringComparison.OrdinalIgnoreCase) ||
                normalized.Contains("javascript", StringComparison.OrdinalIgnoreCase) ||
                normalized.Contains("typescript", StringComparison.OrdinalIgnoreCase))
            {
                return @"Images\icons\node-svgrepo-com (1).svg";
            }

            return null;
        }

        private static string Normalize(string profileName)
        {
            return profileName
                .Replace(" ", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("-", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("_", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace(".", string.Empty, StringComparison.OrdinalIgnoreCase);
        }
    }
}

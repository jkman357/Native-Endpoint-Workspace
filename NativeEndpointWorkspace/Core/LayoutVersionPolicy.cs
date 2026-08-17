using System;
using System.Text.RegularExpressions;

namespace NativeEndpointWorkspace.Core
{
    public static class LayoutVersionPolicy
    {
        private static readonly Regex LegacyV001 = new Regex(@"^0\.0\.1(?:rc\d+)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public static bool IsSupported(int layoutSchemaVersion, string applicationVersion)
        {
            if (layoutSchemaVersion == WorkspaceConstants.LayoutSchemaVersion)
                return true;

            // SchemaVersion 0 represents legacy rc01-rc11 files written before schema
            // versioning existed. Only the exact v0.0.1 / v0.0.1rcN application line is accepted.
            return layoutSchemaVersion == 0 &&
                   !string.IsNullOrWhiteSpace(applicationVersion) &&
                   LegacyV001.IsMatch(applicationVersion.Trim());
        }
    }
}

using GVFS.Common.Tracing;
using System;
using System.Collections.Generic;
using System.IO;

namespace GVFS.Common
{
    public class ProductUpgraderInfo
    {
        public const string UpgradeDirectoryName = "GVFS.Upgrade";
        public const string LogDirectory = "Logs";
        public const string DownloadDirectory = "Downloads";
        public const string HighestAvailableVersionFileName = "HighestAvailableVersion";

        protected const string RootDirectory = UpgradeDirectoryName;

        public static bool IsLocalUpgradeAvailable()
        {
            string highestAvailableVersionFile = GetHighestAvailableVersionFilePath();
            if (File.Exists(highestAvailableVersionFile))
            {
                string contents = File.ReadAllText(highestAvailableVersionFile).Trim();

                Version highestAvailableVersion;
                if (!string.IsNullOrEmpty(contents) &&
                    Version.TryParse(contents, out highestAvailableVersion))
                {
                    Version currentVersion = new Version(CurrentGVFSVersion());
                    return highestAvailableVersion > currentVersion;
                }
            }

            return false;
        }

        public static void RecordHighestAvailableVersion(string highestAvailableVersion)
        {
            string highestAvailableVersionFile = GetHighestAvailableVersionFilePath();
            if (string.IsNullOrEmpty(highestAvailableVersion))
            {
                if (File.Exists(highestAvailableVersionFile))
                {
                    File.Delete(highestAvailableVersionFile);
                }
            }
            else
            {
                File.WriteAllText(highestAvailableVersion, GetHighestAvailableVersionFilePath());
            }
        }

        public static string CurrentGVFSVersion()
        {
            return ProcessHelper.GetCurrentProcessVersion();
        }

        public static string GetUpgradesDirectoryPath()
        {
            return Paths.GetServiceDataRoot(RootDirectory);
        }

        public static string GetLogDirectoryPath()
        {
            return Path.Combine(Paths.GetServiceDataRoot(RootDirectory), LogDirectory);
        }

        public static string GetAssetDownloadsPath()
        {
            return Path.Combine(
                Paths.GetServiceDataRoot(RootDirectory),
                DownloadDirectory);
        }

        public static string GetHighestAvailableVersionFilePath()
        {
            return Path.Combine(GetAssetDownloadsPath(), HighestAvailableVersionFileName);
        }
    }
}

using GVFS.Common.FileSystem;
using GVFS.Common.Git;
using GVFS.Common.Tracing;
using NuGet.Protocol.Core.Types;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading;

namespace GVFS.Common.NuGetUpgrader
{
    public class NuGetUpgrader : IProductUpgrader
    {
        private const string ContentDirectoryName = "content";
        private const string InstallManifestFileName = "install-manifest.json";
        private readonly bool dryRun;
        private readonly PhysicalFileSystem fileSystem;
        private readonly Version installedVersion;
        private readonly UpgraderUtils upgraderUtils;
        private readonly NuGetUpgraderConfig nuGetUpgraderConfig;
        private readonly ITracer tracer;

        private ReleaseManifest releaseManifest;
        private IPackageSearchMetadata highestVersionAvailable;
        private NuGetFeed nuGetFeed;

        public NuGetUpgrader(
            string currentVersion,
            ITracer tracer,
            NuGetUpgraderConfig config,
            bool dryRun,
            string downloadFolder,
            string personalAccessToken)
            : this(
                currentVersion,
                tracer,
                config,
                dryRun,
                new PhysicalFileSystem(),
                new NuGetFeed(
                    config.FeedUrl,
                    config.PackageFeedName,
                    downloadFolder,
                    personalAccessToken,
                    tracer))
        {
        }

        public NuGetUpgrader(
            string currentVersion,
            ITracer tracer,
            NuGetUpgraderConfig config,
            bool dryRun,
            PhysicalFileSystem fileSystem,
            NuGetFeed nuGetFeed)
            : this(
                currentVersion,
                tracer,
                config,
                dryRun,
                fileSystem,
                nuGetFeed,
                new UpgraderUtils(tracer, fileSystem))
        {
        }

        public NuGetUpgrader(
            string currentVersion,
            ITracer tracer,
            NuGetUpgraderConfig config,
            bool dryRun,
            PhysicalFileSystem fileSystem,
            NuGetFeed nuGetFeed,
            UpgraderUtils localUpgraderServices)
        {
            this.dryRun = dryRun;
            this.nuGetUpgraderConfig = config;
            this.tracer = tracer;
            this.installedVersion = new Version(currentVersion);
            this.InstallationId = DateTime.Now.ToString("yyyyMMdd_HHmmss");

            this.fileSystem = fileSystem;
            this.nuGetFeed = nuGetFeed;
            this.upgraderUtils = localUpgraderServices;
        }

        public string DownloadedPackagePath { get; private set; }

        /// <summary>
        /// A unique string generated by this upgrade instance.
        /// </summary>
        public string InstallationId { get; private set; }

        /// <summary>
        /// Try to load a NuGetUpgrader from config settings.
        /// <isConfigured>Flag to indicate whether the system is configured to use a NuGetUpgrader.
        /// A NuGetUpgrader can be set as the Upgrader to use, but it might not be properly configured.
        /// </isConfigured>
        /// <Returns>True if able to load a properly configured NuGetUpgrader<Returns>
        /// </summary>
        public static bool TryCreate(
            ITracer tracer,
            bool dryRun,
            bool noVerify,
            out NuGetUpgrader nuGetUpgrader,
            out bool isConfigured,
            out string error)
        {
            NuGetUpgraderConfig upgraderConfig = new NuGetUpgraderConfig(tracer, new LocalGVFSConfig());
            nuGetUpgrader = null;
            isConfigured = false;

            if (!upgraderConfig.TryLoad(out error))
            {
                nuGetUpgrader = null;
                return false;
            }

            if (!(isConfigured = upgraderConfig.IsConfigured(out error)))
            {
                return false;
            }

            // At this point, we have determined that the system is set up to use
            // the NuGetUpgrader

            if (!upgraderConfig.IsReady(out error))
            {
                return false;
            }

            if (!TryGetPersonalAccessToken(
                    GVFSPlatform.Instance.GitInstallation.GetInstalledGitBinPath(),
                    upgraderConfig.FeedUrlForCredentials,
                    tracer,
                    out string token,
                    out string getPatError))
            {
                error = $"NuGetUpgrader was not able to acquire Personal Access Token to access NuGet feed. Error: {getPatError}";
                tracer.RelatedError(error);
                return false;
            }

            nuGetUpgrader = new NuGetUpgrader(
                ProcessHelper.GetCurrentProcessVersion(),
                tracer,
                upgraderConfig,
                dryRun,
                ProductUpgraderInfo.GetAssetDownloadsPath(),
                token);

            return true;
        }

        /// <summary>
        /// Performs a replacement on well known strings in the arguments field of a manifest entry.
        /// </summary>
        /// <param name="src">The unprocessed string to use as arguments to an install command</param>
        /// <param name="installationId">A unique installer ID to replace the installer_id token with.</param>
        /// <returns>The argument string with tokens replaced.</returns>
        public static string ReplaceArgTokens(string src, string installationId)
        {
            string logDirectory = ProductUpgraderInfo.GetLogDirectoryPath();

            string dst = src.Replace(NuGetUpgrader.replacementToken(InstallActionInfo.ManifestEntryLogDirectoryToken), logDirectory)
                .Replace(NuGetUpgrader.replacementToken(InstallActionInfo.ManifestEntryInstallationIdToken), installationId);
            return dst;
        }

        public void Dispose()
        {
            this.nuGetFeed?.Dispose();
            this.nuGetFeed = null;
        }

        public bool UpgradeAllowed(out string message)
        {
            if (string.IsNullOrEmpty(this.nuGetUpgraderConfig.FeedUrl))
            {
                message = "Nuget Feed URL has not been configured";
                return false;
            }
            else if (string.IsNullOrEmpty(this.nuGetUpgraderConfig.PackageFeedName))
            {
                message = "NuGet package feed has not been configured";
                return false;
            }
            else if (string.IsNullOrEmpty(this.nuGetUpgraderConfig.FeedUrlForCredentials))
            {
                message = "URL to lookup credentials has not been configured";
                return false;
            }

            message = null;
            return true;
        }

        public bool TryQueryNewestVersion(out Version newVersion, out string errorMessage)
        {
            try
            {
                IList<IPackageSearchMetadata> queryResults = this.nuGetFeed.QueryFeedAsync(this.nuGetUpgraderConfig.PackageFeedName).GetAwaiter().GetResult();

                // Find the package with the highest version
                IPackageSearchMetadata highestVersionAvailable = null;
                foreach (IPackageSearchMetadata result in queryResults)
                {
                    if (highestVersionAvailable == null || result.Identity.Version > highestVersionAvailable.Identity.Version)
                    {
                        highestVersionAvailable = result;
                    }
                }

                if (highestVersionAvailable != null &&
                    highestVersionAvailable.Identity.Version.Version > this.installedVersion)
                {
                    this.highestVersionAvailable = highestVersionAvailable;
                }

                newVersion = this.highestVersionAvailable?.Identity?.Version?.Version;

                if (newVersion != null)
                {
                    this.tracer.RelatedInfo($"{nameof(this.TryQueryNewestVersion)} - new version available: installedVersion: {this.installedVersion}, highestVersionAvailable: {highestVersionAvailable}");
                    errorMessage = $"New version {highestVersionAvailable.Identity.Version} is available.";
                    return true;
                }
                else if (highestVersionAvailable != null)
                {
                    this.tracer.RelatedInfo($"{nameof(this.TryQueryNewestVersion)} - up-to-date");
                    errorMessage = $"highest version available is {highestVersionAvailable.Identity.Version}, you are up-to-date";
                    return true;
                }
                else
                {
                    this.tracer.RelatedInfo($"{nameof(this.TryQueryNewestVersion)} - no versions available from feed.");
                    errorMessage = $"No versions available via feed.";
                }
            }
            catch (Exception ex)
            {
                UpgraderUtils.TraceException(
                    this.tracer,
                    ex,
                    nameof(this.TryQueryNewestVersion),
                    "Exception encountered querying for newest version of upgrade package.");
                errorMessage = ex.Message;
                newVersion = null;
            }

            return false;
        }

        public bool TryDownloadNewestVersion(out string errorMessage)
        {
            if (this.highestVersionAvailable == null)
            {
                // If we hit this code path, it indicates there was a
                // programmer error.  The expectation is that this
                // method will only be called after
                // TryQueryNewestVersion has been called, and
                // indicates that a newer version is available.
                errorMessage = "No new version to download. Query for newest version to ensure a new version is available before downloading.";
                return false;
            }

            using (ITracer activity = this.tracer.StartActivity(nameof(this.TryDownloadNewestVersion), EventLevel.Informational))
            {
                try
                {
                    this.DownloadedPackagePath = this.nuGetFeed.DownloadPackageAsync(this.highestVersionAvailable.Identity).GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    UpgraderUtils.TraceException(
                        activity,
                        ex,
                        nameof(this.TryDownloadNewestVersion),
                        "Exception encountered downloading newest version of upgrade package.");
                    errorMessage = ex.Message;
                    return false;
                }
            }

            errorMessage = null;
            return true;
        }

        public bool TryCleanup(out string error)
        {
            error = null;
            Exception e;
            bool success = this.fileSystem.TryDeleteDirectory(this.upgraderUtils.TempPath, out e);

            if (!success)
            {
                error = e?.Message;
                UpgraderUtils.TraceException(
                    this.tracer,
                    e,
                    nameof(this.TryDownloadNewestVersion),
                    "Exception encountered cleaning up downloaded upgrade package.");
            }

            return success;
        }

        public bool TryRunInstaller(InstallActionWrapper installActionWrapper, out string error)
        {
            string localError = null;
            int installerExitCode;
            bool installSuccessful = true;
            using (ITracer activity = this.tracer.StartActivity(nameof(this.TryRunInstaller), EventLevel.Informational))
            {
                try
                {
                    string platformKey = ReleaseManifest.WindowsPlatformKey;
                    string upgradesDirectoryPath = ProductUpgraderInfo.GetUpgradesDirectoryPath();

                    Exception e;
                    if (!this.fileSystem.TryDeleteDirectory(this.upgraderUtils.TempPath, out e))
                    {
                        UpgraderUtils.TraceException(
                            this.tracer,
                            e,
                            nameof(this.TryRunInstaller),
                            "Exception encountered trying to delete download directory in preperation for download.");

                        error = e?.Message;
                        return false;
                    }

                    string extractedPackagePath = this.UnzipPackageToTempLocation();
                    this.releaseManifest = ReleaseManifest.FromJsonFile(Path.Combine(extractedPackagePath, ContentDirectoryName, InstallManifestFileName));
                    if (!this.releaseManifest.PlatformInstallManifests.TryGetValue(platformKey, out InstallManifestPlatform platformInstallManifest) ||
                        platformInstallManifest == null)
                    {
                        activity.RelatedError($"Extracted ReleaseManifest from JSON, but there was no entry for {platformKey}.");
                        error = $"No entry in the manifest for the current platform ({platformKey}). Please verify the upgrade package.";
                        return false;
                    }

                    activity.RelatedInfo($"Extracted ReleaseManifest from JSON. InstallActions: {platformInstallManifest.InstallActions.Count}");

                    this.fileSystem.CreateDirectory(upgradesDirectoryPath);

                    foreach (InstallActionInfo entry in platformInstallManifest.InstallActions)
                    {
                        string installerPath = Path.Combine(extractedPackagePath, ContentDirectoryName, entry.InstallerRelativePath);

                        string args = entry.Args ?? string.Empty;

                        // Replace tokens on args
                        string processedArgs = NuGetUpgrader.ReplaceArgTokens(args, this.InstallationId);

                        activity.RelatedInfo(
                            "Running install action: Name: {0}, Version: {1}, InstallerPath: {2} RawArgs: {3}, ProcessedArgs: {4}",
                            entry.Name,
                            entry.Version,
                            installerPath,
                            args,
                            processedArgs);

                        string progressMessage = string.IsNullOrWhiteSpace(entry.Version) ?
                            $"Running {entry.Name}" :
                            $"Running {entry.Name} (version {entry.Version})";

                        installActionWrapper(
                            () =>
                            {
                                if (!this.dryRun)
                                {
                                    this.upgraderUtils.RunInstaller(installerPath, processedArgs, out installerExitCode, out localError);
                                }
                                else
                                {
                                    // We add a sleep here to ensure
                                    // the message for this install
                                    // action is written to the
                                    // console.  Even though the
                                    // message is written with a delay
                                    // of 0, the messages are not
                                    // always written out.  If / when
                                    // we can ensure that the message
                                    // is written out to console, then
                                    // we can remove this sleep.
                                    Thread.Sleep(1500);
                                    installerExitCode = 0;
                                }

                                installSuccessful = installerExitCode == 0;

                                return installSuccessful;
                            },
                            progressMessage);

                        if (!installSuccessful)
                        {
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    localError = ex.Message;
                    installSuccessful = false;
                }

                if (!installSuccessful)
                {
                    activity.RelatedError($"Could not complete all install actions. The following error was encountered: {localError}");
                    error = localError;
                    return false;
                }
                else
                {
                    activity.RelatedInfo($"Install actions completed successfully.");
                    error = null;
                    return true;
                }
            }
        }

        public bool TrySetupToolsDirectory(out string upgraderToolPath, out string error)
        {
            return this.upgraderUtils.TrySetupToolsDirectory(out upgraderToolPath, out error);
        }

        private static bool TryGetPersonalAccessToken(string gitBinaryPath, string credentialUrl, ITracer tracer, out string token, out string error)
        {
            GitProcess gitProcess = new GitProcess(gitBinaryPath, null, null);
            return gitProcess.TryGetCredentials(tracer, credentialUrl, out string username, out token, out error);
        }

        private static string replacementToken(string tokenString)
        {
            return "{" + tokenString + "}";
        }

        private string UnzipPackageToTempLocation()
        {
            string extractedPackagePath = this.upgraderUtils.TempPath;
            ZipFile.ExtractToDirectory(this.DownloadedPackagePath, extractedPackagePath);
            return extractedPackagePath;
        }

        public class NuGetUpgraderConfig
        {
            private readonly ITracer tracer;
            private readonly LocalGVFSConfig localConfig;

            public NuGetUpgraderConfig(ITracer tracer, LocalGVFSConfig localGVFSConfig)
            {
                this.tracer = tracer;
                this.localConfig = localGVFSConfig;
            }

            public NuGetUpgraderConfig(
                ITracer tracer,
                LocalGVFSConfig localGVFSConfig,
                string feedUrl,
                string packageFeedName,
                string feedUrlForCredentials)
                : this(tracer, localGVFSConfig)
            {
                this.FeedUrl = feedUrl;
                this.PackageFeedName = packageFeedName;
                this.FeedUrlForCredentials = feedUrlForCredentials;
            }

            public string FeedUrl { get; private set; }
            public string PackageFeedName { get; private set; }
            public string FeedUrlForCredentials { get; private set; }

            /// <summary>
            /// Check if the NuGetUpgrader is ready for use. A
            /// NuGetUpgrader is considered ready if all required
            /// config settings are present.
            /// </summary>
            public bool IsReady(out string error)
            {
                if (string.IsNullOrEmpty(this.FeedUrl) ||
                    string.IsNullOrEmpty(this.PackageFeedName) ||
                    string.IsNullOrEmpty(this.FeedUrlForCredentials))
                {
                    error = string.Join(
                        Environment.NewLine,
                        "One or more required settings for NuGetUpgrader are missing.",
                        $"Use `gvfs config [{GVFSConstants.LocalGVFSConfig.UpgradeFeedUrl} | {GVFSConstants.LocalGVFSConfig.UpgradeFeedCredentialUrl} | {GVFSConstants.LocalGVFSConfig.UpgradeFeedPackageName}] <value>` to set the config.");
                    return false;
                }

                error = null;
                return true;
            }

            /// <summary>
            /// Check if the NuGetUpgrader is configured.
            /// </summary>
            public bool IsConfigured(out string error)
            {
                if (string.IsNullOrEmpty(this.FeedUrl) &&
                    string.IsNullOrEmpty(this.PackageFeedName) &&
                    string.IsNullOrEmpty(this.FeedUrlForCredentials))
                {
                    error = string.Join(
                        Environment.NewLine,
                        "NuGet upgrade server is not configured.",
                        $"Use `gvfs config [{GVFSConstants.LocalGVFSConfig.UpgradeFeedUrl} | {GVFSConstants.LocalGVFSConfig.UpgradeFeedCredentialUrl} | {GVFSConstants.LocalGVFSConfig.UpgradeFeedPackageName}] <value>` to set the config.");
                    return false;
                }

                error = null;
                return true;
            }

            /// <summary>
            /// Try to load the config for a NuGet upgrader. Returns false if there was an error reading the config.
            /// </summary>
            public bool TryLoad(out string error)
            {
                string configValue;
                if (!this.localConfig.TryGetConfig(GVFSConstants.LocalGVFSConfig.UpgradeFeedUrl, out configValue, out error))
                {
                    this.tracer.RelatedError(error);
                    return false;
                }

                this.FeedUrl = configValue;

                if (!this.localConfig.TryGetConfig(GVFSConstants.LocalGVFSConfig.UpgradeFeedCredentialUrl, out configValue, out error))
                {
                    this.tracer.RelatedError(error);
                    return false;
                }

                this.FeedUrlForCredentials = configValue;

                if (!this.localConfig.TryGetConfig(GVFSConstants.LocalGVFSConfig.UpgradeFeedPackageName, out configValue, out error))
                {
                    this.tracer.RelatedError(error);
                    return false;
                }

                this.PackageFeedName = configValue;
                return true;
            }
        }
    }
}

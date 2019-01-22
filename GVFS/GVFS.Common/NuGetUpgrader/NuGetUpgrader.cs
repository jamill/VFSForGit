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
        private static readonly string GitBinPath = GVFSPlatform.Instance.GitInstallation.GetInstalledGitBinPath();

        private readonly bool dryRun;
        private readonly PhysicalFileSystem fileSystem;
        private readonly Version installedVersion;
        private readonly LocalUpgraderServices localUpgradeServices;
        private readonly NugetUpgraderConfig nugetUpgraderConfig;
        private readonly NuGetFeed nuGetFeed;
        private readonly ITracer tracer;

        private ReleaseManifest releaseManifest;
        private IPackageSearchMetadata latestVersion;

        public NuGetUpgrader(
            string currentVersion,
            ITracer tracer,
            NugetUpgraderConfig config,
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
            NugetUpgraderConfig config,
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
                new LocalUpgraderServices(tracer, fileSystem))
        {
        }

        public NuGetUpgrader(
            string currentVersion,
            ITracer tracer,
            NugetUpgraderConfig config,
            bool dryRun,
            PhysicalFileSystem fileSystem,
            NuGetFeed nuGetFeed,
            LocalUpgraderServices localUpgraderServices)
        {
            this.dryRun = dryRun;
            this.nugetUpgraderConfig = config;
            this.tracer = tracer;
            this.installedVersion = new Version(currentVersion);

            this.fileSystem = fileSystem;
            this.nuGetFeed = nuGetFeed;
            this.localUpgradeServices = localUpgraderServices;
        }

        public string DownloadedPackagePath { get; private set; }

        public static NuGetUpgrader Create(
            ITracer tracer,
            bool dryRun,
            bool noVerify,
            out string error)
        {
            NugetUpgraderConfig upgraderConfig = new NugetUpgraderConfig(tracer, new LocalGVFSConfig());
            bool isConfigured;
            bool isEnabled;

            if (!upgraderConfig.TryLoad(out isEnabled, out isConfigured, out error))
            {
                if (isEnabled && !isConfigured)
                {
                    tracer.RelatedWarning($"NuGetUpgrader is enabled, but is not properly configured. Error: {error}");

                    return new NuGetUpgrader(
                        ProcessHelper.GetCurrentProcessVersion(),
                        tracer,
                        upgraderConfig,
                        dryRun,
                        ProductUpgraderInfo.GetAssetDownloadsPath(),
                        personalAccessToken: null);
                }

                return null;
            }

            if (!TryGetPersonalAccessToken(
                GitBinPath,
                upgraderConfig.FeedUrlForCredentials,
                tracer,
                out string token,
                out error))
            {
                tracer.RelatedWarning($"NuGetUpgrader was not able to acquire Personal Access Token to access NuGet feed. Error: {error}");
            }

            NuGetUpgrader upgrader = new NuGetUpgrader(
                ProcessHelper.GetCurrentProcessVersion(),
                tracer,
                upgraderConfig,
                dryRun,
                ProductUpgraderInfo.GetAssetDownloadsPath(),
                token);

            return upgrader;
        }

        public void Dispose()
        {
            this.nuGetFeed?.Dispose();
        }

        public bool UpgradeAllowed(out string message)
        {
            if (string.IsNullOrEmpty(this.nugetUpgraderConfig.FeedUrl))
            {
                message = "Nuget Feed URL has not been configured";
                return false;
            }
            else if (string.IsNullOrEmpty(this.nugetUpgraderConfig.PackageFeedName))
            {
                message = "URL to lookup credentials has not been configured";
                return false;
            }
            else if (string.IsNullOrEmpty(this.nugetUpgraderConfig.FeedUrlForCredentials))
            {
                message = "URL to lookup credentials has not been configured";
                return false;
            }
            else
            {
                message = null;
            }

            message = null;
            return true;
        }

        public bool TryQueryNewestVersion(out Version newVersion, out string message)
        {
            try
            {
                IList<IPackageSearchMetadata> queryResults = this.nuGetFeed.QueryFeedAsync(this.nugetUpgraderConfig.PackageFeedName).GetAwaiter().GetResult();

                // Find the latest package
                IPackageSearchMetadata highestVersion = null;
                foreach (IPackageSearchMetadata result in queryResults)
                {
                    if (highestVersion == null || result.Identity.Version > highestVersion.Identity.Version)
                    {
                        highestVersion = result;
                    }
                }

                if (highestVersion != null &&
                    highestVersion.Identity.Version.Version > this.installedVersion)
                {
                    this.latestVersion = highestVersion;
                }

                newVersion = this.latestVersion?.Identity?.Version?.Version;

                if (newVersion != null)
                {
                    this.tracer.RelatedInfo($"{nameof(this.TryQueryNewestVersion)} - new version available: installedVersion: {this.installedVersion}, latestAvailableVersion: {highestVersion}");
                    message = $"New version {highestVersion.Identity.Version} is available.";
                    return true;
                }
                else if (highestVersion != null)
                {
                    this.tracer.RelatedInfo($"{nameof(this.TryQueryNewestVersion)} - up-to-date");
                    message = $"Latest available version is {highestVersion.Identity.Version}, you are up-to-date";
                    return true;
                }
                else
                {
                    this.tracer.RelatedInfo($"{nameof(this.TryQueryNewestVersion)} - no versions available from feed.");
                    message = $"No versions available via feed.";
                }
            }
            catch (Exception ex)
            {
                this.tracer.RelatedError($"{nameof(this.TryQueryNewestVersion)} failed with: {ex.Message}");
                message = ex.Message;
                newVersion = null;
            }

            return false;
        }

        public bool TryDownloadNewestVersion(out string errorMessage)
        {
            if (this.latestVersion == null)
            {
                errorMessage = "No new version to download. Query for latest version to ensure a new version is available before downloading.";
                return false;
            }

            using (ITracer activity = this.tracer.StartActivity(nameof(this.TryDownloadNewestVersion), EventLevel.Informational))
            {
                try
                {
                    this.DownloadedPackagePath = this.nuGetFeed.DownloadPackage(this.latestVersion.Identity).GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    activity.RelatedError($"{nameof(this.TryDownloadNewestVersion)} - error encountered: ${ex.Message}");
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
            bool success = this.fileSystem.TryDeleteDirectory(this.localUpgradeServices.TempPath, out e);

            if (!success)
            {
                error = e?.Message;
                this.tracer.RelatedError($"{nameof(this.TryCleanup)} - Error encountered: {error}");
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
                    if (!this.fileSystem.TryDeleteDirectory(this.localUpgradeServices.TempPath, out e))
                    {
                        error = e?.Message;
                        return false;
                    }

                    string extractedPackagePath = this.UnzipPackageToTempLocation();
                    this.releaseManifest = ReleaseManifest.FromJsonFile(Path.Combine(extractedPackagePath, "content", "install-manifest.json"));
                    InstallManifestPlatform platformInstallManifest = this.releaseManifest.PlatformInstallManifests[platformKey];

                    if (platformInstallManifest == null)
                    {
                        activity.RelatedError($"Extracted ReleaseManifest from JSON, but there was no entry for {platformKey}.");
                        error = $"No entry in the manifest for the current platform ({platformKey}). Please verify the upgrade package.";
                        return false;
                    }

                    activity.RelatedInfo($"Extracted ReleaseManifest from JSON. InstallActions: {platformInstallManifest.InstallActions.Count}");

                    this.fileSystem.CreateDirectory(upgradesDirectoryPath);

                    foreach (ManifestEntry entry in platformInstallManifest.InstallActions)
                    {
                        string installerPath = Path.Combine(extractedPackagePath, "content", entry.InstallerRelativePath);

                        activity.RelatedInfo(
                            $"Running install action: Name: {entry.Name}, Version: {entry.Version}" +
                            $"InstallerPath: {installerPath} Args: {entry.Args}");

                        installActionWrapper(
                            () =>
                            {
                                if (!this.dryRun)
                                {
                                    this.localUpgradeServices.RunInstaller(installerPath, entry.Args, out installerExitCode, out localError);
                                }
                                else
                                {
                                    // This is a temporary workaround to force the step to be written
                                    // out to the console.
                                    Thread.Sleep(2500);
                                    installerExitCode = 0;
                                }

                                installSuccessful = installerExitCode == 0;

                                return installSuccessful;
                            },
                            $"Installing {entry.Name} Version: {entry.Version}");
                    }
                }
                catch (Exception ex)
                {
                    localError = ex.Message;
                    installSuccessful = false;
                }

                if (!installSuccessful)
                {
                    activity.RelatedError($"Could not complete all install actions: {localError}");
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
            return this.localUpgradeServices.TrySetupToolsDirectory(out upgraderToolPath, out error);
        }

        private static bool TryGetPersonalAccessToken(string gitBinaryPath, string credentialUrl, ITracer tracer, out string token, out string error)
        {
            GitProcess gitProcess = new GitProcess(gitBinaryPath, null, null);
            return gitProcess.TryGetCredentials(tracer, credentialUrl, out string username, out token, out error);
        }

        private string UnzipPackageToTempLocation()
        {
            string extractedPackagePath = this.localUpgradeServices.TempPath;
            ZipFile.ExtractToDirectory(this.DownloadedPackagePath, extractedPackagePath);
            return extractedPackagePath;
        }

        public class NugetUpgraderConfig
        {
            private readonly ITracer tracer;
            private readonly LocalGVFSConfig localConfig;

            public NugetUpgraderConfig(ITracer tracer, LocalGVFSConfig localGVFSConfig)
            {
                this.tracer = tracer;
                this.localConfig = localGVFSConfig;
            }

            public NugetUpgraderConfig(
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

            public bool TryLoad(out bool isEnabled, out bool isCorrectlyConfigured, out string error)
            {
                error = string.Empty;

                string configValue;
                string readError;

                bool feedURLAvailable = false;
                if (this.localConfig.TryGetConfig(GVFSConstants.LocalGVFSConfig.UpgradeFeedUrl, out configValue, out readError))
                {
                    feedURLAvailable = !string.IsNullOrWhiteSpace(configValue);
                }
                else
                {
                    this.tracer.RelatedError(readError);
                }

                this.FeedUrl = configValue;

                bool credentialURLAvailable = false;
                if (this.localConfig.TryGetConfig(GVFSConstants.LocalGVFSConfig.UpgradeFeedCredentialUrl, out configValue, out readError))
                {
                    credentialURLAvailable = !string.IsNullOrWhiteSpace(configValue);
                }
                else
                {
                    this.tracer.RelatedError(readError);
                }

                this.FeedUrlForCredentials = configValue;

                bool feedNameAvailable = false;
                if (this.localConfig.TryGetConfig(GVFSConstants.LocalGVFSConfig.UpgradeFeedPackageName, out configValue, out readError))
                {
                    feedNameAvailable = !string.IsNullOrWhiteSpace(configValue);
                }
                else
                {
                    this.tracer.RelatedError(readError);
                }

                this.PackageFeedName = configValue;

                isEnabled = feedURLAvailable || credentialURLAvailable || feedNameAvailable;
                isCorrectlyConfigured = feedURLAvailable && credentialURLAvailable && feedNameAvailable;

                if (!isEnabled)
                {
                    error = string.Join(
                        Environment.NewLine,
                        "Nuget upgrade server is not configured.",
                        $"Use `gvfs config [{GVFSConstants.LocalGVFSConfig.UpgradeFeedUrl} | {GVFSConstants.LocalGVFSConfig.UpgradeFeedCredentialUrl} | {GVFSConstants.LocalGVFSConfig.UpgradeFeedPackageName}] <value>` to set the config.");
                    return false;
                }

                if (!isCorrectlyConfigured)
                {
                    error = string.Join(
                            Environment.NewLine,
                            "One or more required settings for NuGetUpgrader are missing.",
                            $"Use `gvfs config [{GVFSConstants.LocalGVFSConfig.UpgradeFeedUrl} | {GVFSConstants.LocalGVFSConfig.UpgradeFeedCredentialUrl} | {GVFSConstants.LocalGVFSConfig.UpgradeFeedPackageName}] <value>` to set the config.");
                    return false;
                }

                return true;
            }
        }
    }
}

using GVFS.Common;
using GVFS.Common.FileSystem;
using GVFS.Common.Git;
using GVFS.Common.Tracing;
using NuGet.Packaging.Core;
using NuGet.Protocol.Core.Types;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Threading;

namespace GVFS.Common.NuGetUpgrade
{
    public class NuGetUpgrader : ProductUpgrader
    {
        protected readonly NuGetUpgraderConfig nuGetUpgraderConfig;
        protected Version highestVersionAvailable;

        private const string ContentDirectoryName = "content";
        private const string InstallManifestFileName = "install-manifest.json";
        private const string ExtractedInstallerDirectoryName = "InstallerTemp";

        private InstallManifest installManifest;
        private NuGetFeed nuGetFeed;
        private ICredentialStore credentialStore;
        private bool isNuGetFeedInitialized;

        public NuGetUpgrader(
            string currentVersion,
            ITracer tracer,
            PhysicalFileSystem fileSystem,
            bool dryRun,
            bool noVerify,
            NuGetUpgraderConfig config,
            string downloadFolder,
            ICredentialStore credentialStore)
            : this(
                currentVersion,
                tracer,
                dryRun,
                noVerify,
                fileSystem,
                config,
                new NuGetFeed(
                    config.FeedUrl,
                    config.PackageFeedName,
                    downloadFolder,
                    null,
                    tracer),
                credentialStore)
        {
        }

        internal NuGetUpgrader(
            string currentVersion,
            ITracer tracer,
            bool dryRun,
            bool noVerify,
            PhysicalFileSystem fileSystem,
            NuGetUpgraderConfig config,
            NuGetFeed nuGetFeed,
            ICredentialStore credentialStore)
            : base(
                currentVersion,
                tracer,
                dryRun,
                noVerify,
                fileSystem)
        {
            this.nuGetUpgraderConfig = config;

            this.nuGetFeed = nuGetFeed;
            this.credentialStore = credentialStore;

            // Extract the folder inside ProductUpgraderInfo.GetAssetDownloadsPath to ensure the
            // correct ACLs are in place
            this.ExtractedInstallerPath = Path.Combine(
                ProductUpgraderInfo.GetAssetDownloadsPath(),
                ExtractedInstallerDirectoryName);
        }

        public string DownloadedPackagePath { get; private set; }

        public override bool SupportsAnonymousVersionQuery { get => false; }

        /// <summary>
        /// Path to unzip the downloaded upgrade package
        /// </summary>
        private string ExtractedInstallerPath { get; }

        /// <summary>
        /// Try to load a NuGetUpgrader from config settings.
        /// <isConfigured>Flag to indicate whether the system is configured to use a NuGetUpgrader.
        /// A NuGetUpgrader can be set as the Upgrader to use, but it might not be properly configured.
        /// </isConfigured>
        /// <Returns>True if able to load a properly configured NuGetUpgrader<Returns>
        /// </summary>
        public static bool TryCreate(
            ITracer tracer,
            PhysicalFileSystem fileSystem,
            LocalGVFSConfig gvfsConfig,
            ICredentialStore credentialStore,
            bool dryRun,
            bool noVerify,
            out NuGetUpgrader nuGetUpgrader,
            out bool isConfigured,
            out string error)
        {
            NuGetUpgraderConfig upgraderConfig = new NuGetUpgraderConfig(tracer, gvfsConfig);
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

            nuGetUpgrader = new NuGetUpgrader(
                ProcessHelper.GetCurrentProcessVersion(),
                tracer,
                fileSystem,
                dryRun,
                noVerify,
                upgraderConfig,
                ProductUpgraderInfo.GetAssetDownloadsPath(),
                credentialStore);

            return true;
        }

        /// <summary>
        /// Performs a replacement on well known strings in the arguments field of a manifest entry.
        /// </summary>
        /// <param name="src">The unprocessed string to use as arguments to an install command</param>
        /// <param name="installationId">A unique installer ID to replace the installer_id token with.</param>
        /// <returns>The argument string with tokens replaced.</returns>
        public static string ReplaceArgTokens(string src, string installationId, string logsDirectory)
        {
            string dst = src.Replace(NuGetUpgrader.replacementToken(InstallActionInfo.ManifestEntryLogDirectoryToken), logsDirectory)
                .Replace(NuGetUpgrader.replacementToken(InstallActionInfo.ManifestEntryInstallationIdToken), installationId);
            return dst;
        }

        public override void Dispose()
        {
            this.nuGetFeed?.Dispose();
            this.nuGetFeed = null;
            base.Dispose();
        }

        public override bool UpgradeAllowed(out string message)
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

            message = null;
            return true;
        }

        public override bool TryQueryNewestVersion(out Version newVersion, out string message)
        {
            try
            {
                if (!this.EnsureNuGetFeedInitialized(out message))
                {
                    newVersion = null;
                    return false;
                }

                IList<IPackageSearchMetadata> queryResults = this.QueryFeed(firstAttempt: true);

                // Find the package with the highest version
                IPackageSearchMetadata newestPackage = null;
                foreach (IPackageSearchMetadata result in queryResults)
                {
                    if (newestPackage == null || result.Identity.Version > newestPackage.Identity.Version)
                    {
                        newestPackage = result;
                    }
                }

                if (newestPackage != null &&
                    newestPackage.Identity.Version.Version > this.installedVersion)
                {
                    this.highestVersionAvailable = newestPackage.Identity.Version.Version;
                }

                newVersion = this.highestVersionAvailable;

                if (newVersion != null)
                {
                    this.tracer.RelatedInfo($"{nameof(this.TryQueryNewestVersion)} - new version available: installedVersion: {this.installedVersion}, highestVersionAvailable: {newVersion}");
                    message = $"New version {newestPackage.Identity.Version} is available.";
                    return true;
                }
                else if (newestPackage != null)
                {
                    this.tracer.RelatedInfo($"{nameof(this.TryQueryNewestVersion)} - up-to-date");
                    message = $"highest version available is {newestPackage.Identity.Version}, you are up-to-date";
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
                this.TraceException(
                    ex,
                    nameof(this.TryQueryNewestVersion),
                    "Exception encountered querying for newest version of upgrade package.");
                message = ex.Message;
                newVersion = null;
            }

            return false;
        }

        public override bool TryDownloadNewestVersion(out string errorMessage)
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

            if (!this.EnsureNuGetFeedInitialized(out errorMessage))
            {
                return false;
            }

            if (!this.TryCreateAndConfigureDownloadDirectory(this.tracer, out errorMessage))
            {
                this.tracer.RelatedError($"{nameof(NuGetUpgrader)}.{nameof(this.TryCreateAndConfigureDownloadDirectory)} failed. {errorMessage}");
                return false;
            }

            using (ITracer activity = this.tracer.StartActivity(nameof(this.TryDownloadNewestVersion), EventLevel.Informational))
            {
                try
                {
                    PackageIdentity packageId = this.GetPackageForVersion(this.highestVersionAvailable);

                    if (packageId == null)
                    {
                        errorMessage = "Could not find package for version. This indicates the package feed is out of sync.";
                        return false;
                    }

                    this.DownloadedPackagePath = this.nuGetFeed.DownloadPackageAsync(packageId).GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    this.TraceException(
                        activity,
                        ex,
                        nameof(this.TryDownloadNewestVersion),
                        "Exception encountered downloading newest version of upgrade package.");
                    errorMessage = ex.Message;
                    return false;
                }
            }

            if (!this.noVerify)
            {
                if (!this.nuGetFeed.VerifyPackage(this.DownloadedPackagePath))
                {
                    errorMessage = "Package signature validation failed. Check the upgrade logs for more details.";
                    this.tracer.RelatedError(errorMessage);
                    this.fileSystem.DeleteFile(this.DownloadedPackagePath);
                    return false;
                }
            }

            errorMessage = null;
            return true;
        }

        public override bool TryCleanup(out string error)
        {
            return this.TryRecursivelyDeleteInstallerDirectory(out error);
        }

        public override bool TryRunInstaller(InstallActionWrapper installActionWrapper, out string error)
        {
            string localError = null;
            int installerExitCode;
            bool installSuccessful = true;
            using (ITracer activity = this.tracer.StartActivity(nameof(this.TryRunInstaller), EventLevel.Informational))
            {
                InstallActionInfo currentInstallAction = null;
                try
                {
                    string platformKey = InstallManifest.WindowsPlatformKey;

                    if (!this.TryRecursivelyDeleteInstallerDirectory(out error))
                    {
                        return false;
                    }

                    if (!this.noVerify)
                    {
                        if (!this.nuGetFeed.VerifyPackage(this.DownloadedPackagePath))
                        {
                            error = "Package signature validation failed. Check the upgrade logs for more details.";
                            activity.RelatedError(error);
                            this.fileSystem.DeleteFile(this.DownloadedPackagePath);
                            return false;
                        }
                    }

                    this.UnzipPackage();
                    this.installManifest = InstallManifest.FromJsonFile(Path.Combine(this.ExtractedInstallerPath, ContentDirectoryName, InstallManifestFileName));
                    if (!this.installManifest.PlatformInstallManifests.TryGetValue(platformKey, out InstallManifestPlatform platformInstallManifest) ||
                        platformInstallManifest == null)
                    {
                        activity.RelatedError($"Extracted InstallManifest from JSON, but there was no entry for {platformKey}.");
                        error = $"No entry in the manifest for the current platform ({platformKey}). Please verify the upgrade package.";
                        return false;
                    }

                    activity.RelatedInfo($"Extracted InstallManifest from JSON. InstallActions: {platformInstallManifest.InstallActions.Count}");

                    foreach (InstallActionInfo entry in platformInstallManifest.InstallActions)
                    {
                        currentInstallAction = entry;
                        string installerPath = Path.Combine(this.ExtractedInstallerPath, ContentDirectoryName, entry.InstallerRelativePath);

                        string args = entry.Args ?? string.Empty;

                        // Replace tokens on args
                        string processedArgs = NuGetUpgrader.ReplaceArgTokens(args, this.UpgradeInstanceId, ProductUpgraderInfo.GetLogDirectoryPath());

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
                                    this.RunInstaller(installerPath, processedArgs, out installerExitCode, out localError);
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
                    string installActionName = string.IsNullOrEmpty(currentInstallAction?.Name) ?
                        "installer" :
                        currentInstallAction.Name;

                    error = string.IsNullOrEmpty(localError) ?
                       $"The {installActionName} failed, but no error message was provided by the failing command." :
                       $"The {installActionName} failed with the following error: {localError}";

                    activity.RelatedError($"Could not complete all install actions. The following error was encountered: {error}");
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

        protected static EventMetadata CreateEventMetadata(Exception e = null)
        {
            EventMetadata metadata = new EventMetadata();
            metadata.Add("Area", nameof(NuGetFeed));
            if (e != null)
            {
                metadata.Add("Exception", e.ToString());
            }

            return metadata;
        }

        private static string replacementToken(string tokenString)
        {
            return "{" + tokenString + "}";
        }

        private PackageIdentity GetPackageForVersion(Version version)
        {
            IList<IPackageSearchMetadata> queryResults = this.QueryFeed(firstAttempt: true);

            IPackageSearchMetadata packageForVersion = null;
            foreach (IPackageSearchMetadata result in queryResults)
            {
                if (result.Identity.Version.Version == version)
                {
                    packageForVersion = result;
                    break;
                }
            }

            return packageForVersion?.Identity;
        }

        private bool TryGetPersonalAccessToken(string credentialUrl, ITracer tracer, out string token, out string error)
        {
            error = null;
            return this.credentialStore.TryGetCredential(this.tracer, credentialUrl, out string username, out token, out error);
        }

        private bool TryReacquirePersonalAccessToken(string credentialUrl, ITracer tracer, out string token, out string error)
        {
            if (!this.credentialStore.TryDeleteCredential(this.tracer, credentialUrl, username: null, password: null, error: out error))
            {
                token = null;
                return false;
            }

            return this.TryGetPersonalAccessToken(credentialUrl, tracer, out token, out error);
        }

        private void UnzipPackage()
        {
            ZipFile.ExtractToDirectory(this.DownloadedPackagePath, this.ExtractedInstallerPath);
        }

        private bool TryRecursivelyDeleteInstallerDirectory(out string error)
        {
            error = null;
            Exception e;
            if (!this.fileSystem.TryDeleteDirectory(this.ExtractedInstallerPath, out e))
            {
                if (e != null)
                {
                    this.TraceException(
                        e,
                        nameof(this.TryRecursivelyDeleteInstallerDirectory),
                        $"Exception encountered while deleting {this.ExtractedInstallerPath}.");
                }

                error = e?.Message ?? "Failed to delete directory, but no error was specified.";
                return false;
            }

            return true;
        }

        private IList<IPackageSearchMetadata> QueryFeed(bool firstAttempt)
        {
            try
            {
                return this.nuGetFeed.QueryFeedAsync(this.nuGetUpgraderConfig.PackageFeedName).GetAwaiter().GetResult();
            }
            catch (Exception ex) when (firstAttempt &&
                                       this.IsAuthRelatedException(ex))
            {
                // If we fail to query the feed due to an authorization error, then it is possible we have stale
                // credentials, or credentials without the correct scope. Re-aquire fresh credentials and try again.
                EventMetadata data = CreateEventMetadata(ex);
                this.tracer.RelatedWarning(data, "Failed to query feed due to unauthorized error. Re-acquiring new credentials and trying again.");

                if (!this.TryRefreshCredentials(out string error))
                {
                    // If we were unable to re-acquire credentials, throw a new exception indicating that we tried to handle this, but were unable to.
                    throw new Exception($"Failed to query the feed for upgrade packages due to: {ex.Message}, and was not able to re-acquire new credentials due to: {error}", ex);
                }

                // Now that we have re-acquired credentials, try again - but with the retry flag set to false.
                return this.QueryFeed(firstAttempt: false);
            }
            catch (Exception ex)
            {
                EventMetadata data = CreateEventMetadata(ex);
                string message = $"Error encountered when querying NuGet feed. Is first attempt: {firstAttempt}.";
                this.tracer.RelatedWarning(data, message);
                throw new Exception($"Failed to query the NuGet package feed due to error: {ex.Message}", ex);
            }
        }

        private bool IsAuthRelatedException(Exception ex)
        {
            // In observation, we have seen either an HttpRequestException directly, or
            // a FatalProtocolException wrapping an HttpRequestException when we are not able
            // to auth against the NuGet feed.
            System.Net.Http.HttpRequestException httpRequestException = null;
            if (ex is System.Net.Http.HttpRequestException)
            {
                httpRequestException = ex as System.Net.Http.HttpRequestException;
            }
            else if (ex is FatalProtocolException &&
                ex.InnerException is System.Net.Http.HttpRequestException)
            {
                httpRequestException = ex.InnerException as System.Net.Http.HttpRequestException;
            }

            if (httpRequestException != null &&
                (httpRequestException.Message.Contains("401") || httpRequestException.Message.Contains("403")))
            {
                return true;
            }

            return false;
        }

        private bool TryRefreshCredentials(out string error)
        {
            try
            {
                string authUrl;
                if (!AzDevOpsOrgFromNuGetFeed.TryCreateCredentialQueryUrl(this.nuGetUpgraderConfig.FeedUrl, out authUrl, out error))
                {
                    return false;
                }

                if (!this.TryReacquirePersonalAccessToken(authUrl, this.tracer, out string token, out error))
                {
                    return false;
                }

                this.nuGetFeed.SetCredentials(token);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                this.TraceException(ex, nameof(this.TryRefreshCredentials), "Failed to refresh credentials.");
                return false;
            }
        }

        private bool EnsureNuGetFeedInitialized(out string error)
        {
            if (!this.isNuGetFeedInitialized)
            {
                if (this.credentialStore == null)
                {
                    throw new InvalidOperationException("Attempted to call method that requires authentication but no CredentialStore is configured.");
                }

                string authUrl;
                if (!AzDevOpsOrgFromNuGetFeed.TryCreateCredentialQueryUrl(this.nuGetUpgraderConfig.FeedUrl, out authUrl, out error))
                {
                    return false;
                }

                if (!this.TryGetPersonalAccessToken(authUrl, this.tracer, out string token, out error))
                {
                    return false;
                }

                this.nuGetFeed.SetCredentials(token);
                this.isNuGetFeedInitialized = true;
            }

            error = null;
            return true;
        }

        public class NuGetUpgraderConfig
        {
            protected readonly ITracer tracer;
            protected readonly LocalGVFSConfig localConfig;

            public NuGetUpgraderConfig(ITracer tracer, LocalGVFSConfig localGVFSConfig)
            {
                this.tracer = tracer;
                this.localConfig = localGVFSConfig;
            }

            public NuGetUpgraderConfig(
                ITracer tracer,
                LocalGVFSConfig localGVFSConfig,
                string feedUrl,
                string packageFeedName)
                : this(tracer, localGVFSConfig)
            {
                this.FeedUrl = feedUrl;
                this.PackageFeedName = packageFeedName;
            }

            public string FeedUrl { get; private set; }
            public string PackageFeedName { get; private set; }
            public string CertificateFingerprint { get; private set; }

            /// <summary>
            /// Check if the NuGetUpgrader is ready for use. A
            /// NuGetUpgrader is considered ready if all required
            /// config settings are present.
            /// </summary>
            public virtual bool IsReady(out string error)
            {
                if (string.IsNullOrEmpty(this.FeedUrl) ||
                    string.IsNullOrEmpty(this.PackageFeedName))
                {
                    error = string.Join(
                        Environment.NewLine,
                        "One or more required settings for NuGetUpgrader are missing.",
                        $"Use `gvfs config [{GVFSConstants.LocalGVFSConfig.UpgradeFeedUrl} | {GVFSConstants.LocalGVFSConfig.UpgradeFeedPackageName}] <value>` to set the config.");
                    return false;
                }

                error = null;
                return true;
            }

            /// <summary>
            /// Check if the NuGetUpgrader is configured.
            /// </summary>
            public virtual bool IsConfigured(out string error)
            {
                if (string.IsNullOrEmpty(this.FeedUrl) &&
                    string.IsNullOrEmpty(this.PackageFeedName))
                {
                    error = string.Join(
                        Environment.NewLine,
                        "NuGet upgrade server is not configured.",
                        $"Use `gvfs config [ {GVFSConstants.LocalGVFSConfig.UpgradeFeedUrl} | {GVFSConstants.LocalGVFSConfig.UpgradeFeedPackageName}] <value>` to set the config.");
                    return false;
                }

                error = null;
                return true;
            }

            /// <summary>
            /// Try to load the config for a NuGet upgrader. Returns false if there was an error reading the config.
            /// </summary>
            public virtual bool TryLoad(out string error)
            {
                string configValue;
                if (!this.localConfig.TryGetConfig(GVFSConstants.LocalGVFSConfig.UpgradeFeedUrl, out configValue, out error))
                {
                    this.tracer.RelatedError(error);
                    return false;
                }

                this.FeedUrl = configValue;

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

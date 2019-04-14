using GVFS.Common.FileSystem;
using GVFS.Common.Git;
using GVFS.Common.Tracing;
using System;
using System.Net.Http;
using System.Text.RegularExpressions;

namespace GVFS.Common.NuGetUpgrade
{
    public class ModernNugetUpgrader : NuGetUpgrader
    {
        private IQueryGVFSVersion gvfsVersionFetcher;

        public ModernNugetUpgrader(
           string currentVersion,
           ITracer tracer,
           PhysicalFileSystem fileSystem,
           IQueryGVFSVersion gvfsVersionFetcher,
           bool dryRun,
           bool noVerify,
           ModernNuGetUpgraderConfig config,
           string downloadFolder,
           ICredentialStore credentialStore)
           : base(
               currentVersion,
               tracer,
               fileSystem,
               dryRun,
               noVerify,
               config,
               downloadFolder,
               credentialStore)
        {
            this.gvfsVersionFetcher = gvfsVersionFetcher;
        }

        public override bool SupportsAnonymousVersionQuery { get => true; }

        private ModernNuGetUpgraderConfig Config { get => this.nuGetUpgraderConfig as ModernNuGetUpgraderConfig;  }

        public static bool TryCreate(
            ITracer tracer,
            PhysicalFileSystem fileSystem,
            LocalGVFSConfig gvfsConfig,
            HttpClient httpClient,
            ICredentialStore credentialStore,
            bool dryRun,
            bool noVerify,
            out ModernNugetUpgrader upgrader,
            out string error)
        {
            ModernNuGetUpgraderConfig upgraderConfig = new ModernNuGetUpgraderConfig(tracer, gvfsConfig);
            upgrader = null;

            if (!upgraderConfig.TryLoad(out error))
            {
                upgrader = null;
                return false;
            }

            if (!upgraderConfig.IsConfigured(out error))
            {
                return false;
            }

            if (!upgraderConfig.IsReady(out error))
            {
                return false;
            }

            if (!TryParseOrgFromNugetFeedUrl(upgraderConfig.FeedUrl, out string orgName))
            {
                error = "Unable to parse org name from NuGet feed URL";
                return false;
            }

            QueryGVFSVersionFromOrgInfo gvfsVersionFetcher = new QueryGVFSVersionFromOrgInfo(
                httpClient,
                upgraderConfig.OrgInfoServer,
                orgName,
                "windows",
                upgraderConfig.UpgradeRing);

            upgrader = new ModernNugetUpgrader(
                ProcessHelper.GetCurrentProcessVersion(),
                tracer,
                fileSystem,
                gvfsVersionFetcher,
                dryRun,
                noVerify,
                upgraderConfig,
                ProductUpgraderInfo.GetAssetDownloadsPath(),
                credentialStore);

            return true;
        }

        public override bool TryQueryNewestVersion(out Version newVersion, out string message)
        {
            newVersion = null;
            this.highestVersionAvailable = this.gvfsVersionFetcher.QueryVersion();

            if (this.highestVersionAvailable != null &&
                this.highestVersionAvailable > this.installedVersion)
            {
                newVersion = this.highestVersionAvailable;
            }

            if (newVersion != null)
            {
                this.tracer.RelatedInfo($"{nameof(this.TryQueryNewestVersion)} - new version available: installedVersion: {this.installedVersion}, highestVersionAvailable: {newVersion}");
                message = $"New version {this.highestVersionAvailable} is available.";
                return true;
            }
            else if (this.highestVersionAvailable != null)
            {
                this.tracer.RelatedInfo($"{nameof(this.TryQueryNewestVersion)} - up-to-date");
                message = $"highest version available is {this.highestVersionAvailable}, you are up-to-date";
                return true;
            }
            else
            {
                this.tracer.RelatedInfo($"{nameof(this.TryQueryNewestVersion)} - no versions available from feed.");
                message = $"No versions available via endpoint.";
            }

            newVersion = this.highestVersionAvailable;
            message = null;
            return true;
        }

        private static bool TryParseOrgFromNugetFeedUrl(string packageFeedUrl, out string orgName)
        {
            // We expect a URL of the form https://pkgs.dev.azure.com/{org}
            // and want to convert it to a URL of the form https://{org}.visualstudio.com
            Regex packageUrlRegex = new Regex(
                @"^https://pkgs.dev.azure.com/(?<org>.+?)/",
                RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
            Match urlMatch = packageUrlRegex.Match(packageFeedUrl);

            if (!urlMatch.Success)
            {
                orgName = null;
                return false;
            }

            orgName = urlMatch.Groups["org"].Value;
            return true;
        }

        public class ModernNuGetUpgraderConfig : NuGetUpgraderConfig
        {
            public ModernNuGetUpgraderConfig(ITracer tracer, LocalGVFSConfig localGVFSConfig)
                : base(tracer, localGVFSConfig)
            {
            }

            public string OrgInfoServer { get; set; }

            public string UpgradeRing { get; set; }

            public override bool TryLoad(out string error)
            {
                if (!base.TryLoad(out error))
                {
                    return false;
                }

                if (!this.localConfig.TryGetConfig(GVFSConstants.LocalGVFSConfig.OrgInfoServerUrl, out string orgInfoServerUrl, out error))
                {
                    return false;
                }

                this.OrgInfoServer = orgInfoServerUrl;

                if (!this.localConfig.TryGetConfig(GVFSConstants.LocalGVFSConfig.UpgradeRing, out string upgradeRing, out error))
                {
                    this.tracer.RelatedError(error);
                    return false;
                }

                this.UpgradeRing = upgradeRing;

                return true;
            }

            public override bool IsReady(out string error)
            {
                if (!base.IsReady(out error) ||
                    string.IsNullOrEmpty(this.UpgradeRing) ||
                    string.IsNullOrEmpty(this.OrgInfoServer))
                {
                    error = string.Join(
                        Environment.NewLine,
                        "One or more required settings for ModernNuGetUpgrader are missing.",
                        $"Use `gvfs config [{GVFSConstants.LocalGVFSConfig.UpgradeFeedUrl} | {GVFSConstants.LocalGVFSConfig.UpgradeFeedPackageName} | {GVFSConstants.LocalGVFSConfig.UpgradeRing} | {GVFSConstants.LocalGVFSConfig.OrgInfoServerUrl}] <value>` to set the config.");
                    return false;
                }

                return true;
            }
        }
    }
}

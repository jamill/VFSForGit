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
        private HttpClient httpClient;

        public ModernNugetUpgrader(
           string currentVersion,
           ITracer tracer,
           PhysicalFileSystem fileSystem,
           HttpClient httpClient,
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
            this.httpClient = httpClient;
        }

        public override bool SupportsAnonymousVersionQuery { get => true; }

        private ModernNuGetUpgraderConfig Config { get => this.nuGetUpgraderConfig as ModernNuGetUpgraderConfig;  }
        private string OrgInfoServerUrl { get => this.Config.OrgInfoServer; }
        private string Ring { get => this.Config.UpgradeRing; }
        private string OrgName
        {
            get
            {
                if (!TryParseOrgFromNugetFeedUrl(this.Config.FeedUrl, out string orgName))
                {
                    return null;
                }

                return orgName;
            }
        }

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

            upgrader = new ModernNugetUpgrader(
                ProcessHelper.GetCurrentProcessVersion(),
                tracer,
                fileSystem,
                httpClient,
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
            string orgName = this.OrgName;

            if (orgName == null)
            {
                message = "ModernNuGetUpgrader is not able to parse org name from NuGet Package Feed URL";
                return false;
            }

            OrgInfoApiClient infoServer = new OrgInfoApiClient(this.httpClient, this.OrgInfoServerUrl);
            this.highestVersionAvailable = infoServer.QueryNewestVersion(orgName, "windows", this.Ring);

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

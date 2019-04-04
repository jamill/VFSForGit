﻿using GVFS.Common;
using GVFS.Common.FileSystem;
using GVFS.Common.NuGetUpgrade;
using GVFS.Common.Tracing;
using GVFS.Upgrader;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using static GVFS.Common.GitHubUpgrader.GitHubUpgraderConfig;

namespace GVFS.Service
{
    public class ProductUpgradeTimer : IDisposable
    {
        private static readonly TimeSpan TimeInterval = TimeSpan.FromDays(1);
        private JsonTracer tracer;
        private PhysicalFileSystem fileSystem;
        private Timer timer;

        public ProductUpgradeTimer(JsonTracer tracer)
        {
            this.tracer = tracer;
            this.fileSystem = new PhysicalFileSystem();
        }

        public void Start()
        {
            if (!GVFSEnlistment.IsUnattended(this.tracer))
            {
                TimeSpan startTime = TimeSpan.Zero;

                this.tracer.RelatedInfo("Starting auto upgrade checks.");
                this.timer = new Timer(
                    this.TimerCallback,
                    state: null,
                    dueTime: startTime,
                    period: TimeInterval);
            }
            else
            {
                this.tracer.RelatedInfo("No upgrade checks scheduled, GVFS is running in unattended mode.");
            }
        }

        public void Stop()
        {
            this.tracer.RelatedInfo("Stopping auto upgrade checks");
            this.Dispose();
        }

        public void Dispose()
        {
            if (this.timer != null)
            {
                this.timer.Dispose();
                this.timer = null;
            }
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

        private static EventMetadata CreateEventMetadata(Exception e)
        {
            EventMetadata metadata = new EventMetadata();
            if (e != null)
            {
                metadata.Add("Exception", e.ToString());
            }

            return metadata;
        }

        private void TimerCallback(object unusedState)
        {
            string errorMessage = null;

            using (ITracer activity = this.tracer.StartActivity("Checking for product upgrades.", EventLevel.Informational))
            {
                try
                {
                    ProductUpgraderInfo info = new ProductUpgraderInfo(
                        this.tracer,
                        this.fileSystem);

                    // Load the GVFS config
                    LocalGVFSConfig gvfsConfig = new LocalGVFSConfig();

                    Dictionary<string, string> configSettings = gvfsConfig.GetAllConfig();

                    // The upgrade check always goes against GitHub
                    ProductUpgrader.TryCreateUpgrader(
                        this.tracer,
                        this.fileSystem,
                        dryRun: false,
                        noVerify: false,
                        newUpgrader: out ProductUpgrader productUpgrader,
                        error: out errorMessage);

                    if (productUpgrader == null)
                    {
                        string message = string.Format(
                            "{0}.{1}: GitHubUpgrader.Create failed to create upgrader: {2}",
                            nameof(ProductUpgradeTimer),
                            nameof(this.TimerCallback),
                            errorMessage);

                        activity.RelatedWarning(
                            metadata: new EventMetadata(),
                            message: message,
                            keywords: Keywords.Telemetry);

                        info.RecordHighestAvailableVersion(highestAvailableVersion: null);
                        return;
                    }

                    InstallerPreRunChecker prerunChecker = new InstallerPreRunChecker(this.tracer, string.Empty);
                    if (!prerunChecker.TryRunPreUpgradeChecks(out errorMessage))
                    {
                        string message = string.Format(
                            "{0}.{1}: PreUpgradeChecks failed with: {2}",
                            nameof(ProductUpgradeTimer),
                            nameof(this.TimerCallback),
                            errorMessage);

                        activity.RelatedWarning(
                            metadata: new EventMetadata(),
                            message: message,
                            keywords: Keywords.Telemetry);

                        info.RecordHighestAvailableVersion(highestAvailableVersion: null);
                        return;
                    }

                    if (!productUpgrader.UpgradeAllowed(out errorMessage))
                    {
                        errorMessage = errorMessage ??
                            $"{nameof(ProductUpgradeTimer)}.{nameof(this.TimerCallback)}: Upgrade is not allowed, but no reason provided.";
                        activity.RelatedWarning(
                            metadata: new EventMetadata(),
                            message: errorMessage,
                            keywords: Keywords.Telemetry);

                        info.RecordHighestAvailableVersion(highestAvailableVersion: null);
                        return;
                    }

                    if (!this.TryQueryForNewerVersion(
                            activity,
                            configSettings,
                            productUpgrader,
                            out Version newerVersion,
                            out errorMessage))
                    {
                        string message = string.Format(
                            "{0}.{1}: TryQueryForNewerVersion failed with: {2}",
                            nameof(ProductUpgradeTimer),
                            nameof(this.TimerCallback),
                            errorMessage);

                        activity.RelatedWarning(
                            metadata: new EventMetadata(),
                            message: message,
                            keywords: Keywords.Telemetry);

                        info.RecordHighestAvailableVersion(highestAvailableVersion: null);
                        return;
                    }

                    info.RecordHighestAvailableVersion(highestAvailableVersion: newerVersion);
                }
                catch (Exception ex) when (
                    ex is GVFSException ||
                    ex is IOException ||
                    ex is UnauthorizedAccessException ||
                    ex is NotSupportedException)
                {
                    this.tracer.RelatedWarning(
                        CreateEventMetadata(ex),
                        "Exception encountered recording highest available version");
                }
                catch (Exception ex)
                {
                    this.tracer.RelatedError(
                        CreateEventMetadata(ex),
                        "Unhanlded exception encountered recording highest available version");
                    Environment.Exit((int)ReturnCode.GenericError);
                }
            }
        }

        private bool TryQueryForNewerVersion(
            ITracer tracer,
            Dictionary<string, string> configSettings,
            ProductUpgrader productUpgrader,
            out Version newVersion,
            out string errorMessage)
        {
            errorMessage = null;

            // Read the upgrade ring
            if (!configSettings.TryGetValue(GVFSConstants.LocalGVFSConfig.UpgradeRing, out string ringString))
            {
                ringString = null;
            }

            RingType ringType = LocalGVFSConfig.ParseUpgradeRing(ringString);

            // Read the engineering System
            if (!configSettings.TryGetValue(GVFSConstants.LocalGVFSConfig.UpgradeFeedUrl, out string nugetPackageFeedUrl))
            {
                nugetPackageFeedUrl = null;
            }

            if (!TryParseOrgFromNugetFeedUrl(nugetPackageFeedUrl, out string org))
            {
                org = string.Empty;
            }

            if (productUpgrader is NuGetUpgrader)
            {
                tracer.RelatedInfo($"Querying server for latest version in ring {ringType}...");

                // Use the OrgInfoServer instead of NuGet server
                HttpClient httpClient = new HttpClient();
                string baseAddress = "https://www.contoso.com";

                OrgInfoServer queryServer = new OrgInfoServer(httpClient, baseAddress);
                newVersion = queryServer.QueryNewestVersion(org, ringType.ToString());
            }
            else
            {
                GitHubUpgrader gitHubUpgrader = productUpgrader as GitHubUpgrader;
                tracer.RelatedInfo($"Querying server for latest version in ring {ringType}...");

                if (!gitHubUpgrader.TryQueryNewestVersion(out newVersion, out string detailedError))
                {
                    errorMessage = "Could not fetch new version info. " + detailedError;
                    return false;
                }

                string logMessage = newVersion == null ? "No newer versions available." : $"Newer version available: {newVersion}.";
                tracer.RelatedInfo(logMessage);
            }

            return true;
        }
    }
}

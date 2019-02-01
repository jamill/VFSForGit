using GVFS.Common;
using GVFS.Common.FileSystem;
using GVFS.Common.Tracing;
using GVFS.Upgrader;
using System;
using System.Collections.Generic;
using System.Threading;

namespace GVFS.Service
{
    public class ProductUpgradeTimer : IDisposable
    {
        private static readonly TimeSpan TimeInterval = TimeSpan.FromDays(1);
        private JsonTracer tracer;
        private Timer timer;

        public ProductUpgradeTimer(JsonTracer tracer)
        {
            this.tracer = tracer;
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

        private void TimerCallback(object unusedState)
        {
            string errorMessage = null;
            InstallerPreRunChecker prerunChecker = new InstallerPreRunChecker(this.tracer, string.Empty);
            bool deleteExistingDownloads = true;

            try
            {
                // The upgrade check always goes against GitHub
                productUpgrader productUpgrader = GitHubUpgrader.Create(tracer,
                                                                        dryRun: false,
                                                                        noVerify: false,
                                                                        out error);
                if (productUpgrader != null)
                {
                    if (prerunChecker.TryRunPreUpgradeChecks(out _) &&
                        this.TryQueryForNewerVersion(productUpgrader,
                                                     out string newerVersion,
                                                     out errorMessage))
                    {
                        ProductUpgraderInfo.RecordHighestAvailableVersion(newerVersion);
                    }
                }
            }
            catch (IOException ex)
            {
                this.tracer.RelatedError(ex.Message);
                errorMessage = ex.Message;
            }

            if (errorMessage != null)
            {
                this.tracer.RelatedError(errorMessage);
            }
        }

        private bool TryDownloadUpgrade(ProductUpgrader productUpgrader, out string newVersion, out string errorMessage)
        {
            using (ITracer activity = this.tracer.StartActivity("Checking for product upgrades.", EventLevel.Informational))
            {
                string detailedError = null;
                if (!productUpgrader.UpgradeAllowed(out errorMessage))
                {
                    return false;
                }

                if (!productUpgrader.TryQueryNewestVersion(out newVersion, out detailedError))
                {
                    errorMessage = "Could not fetch new version info. " + detailedError;
                    return false;
                }
            }
        }
    }
}

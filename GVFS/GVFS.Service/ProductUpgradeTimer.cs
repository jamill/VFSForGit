using GVFS.Common;
using GVFS.Common.Tracing;
using GVFS.Upgrader;
using System;
using System.IO;
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
                TimeSpan startTime = TimeSpan.FromMinutes(1);

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
                    // The upgrade check always goes against GitHub
                    GitHubUpgrader productUpgrader = GitHubUpgrader.Create(
                        this.tracer,
                        false,
                        false,
                        out errorMessage);

                    if (productUpgrader == null)
                    {
                        if (errorMessage != null)
                        {
                            this.tracer.RelatedError(errorMessage);
                        }

                        return;
                    }

                    InstallerPreRunChecker prerunChecker = new InstallerPreRunChecker(this.tracer, string.Empty);
                    if (!prerunChecker.TryRunPreUpgradeChecks(out _))
                    {
                        return;
                    }

                    if (!productUpgrader.UpgradeAllowed(out errorMessage))
                    {
                        if (errorMessage != null)
                        {
                            this.tracer.RelatedError(errorMessage);
                        }

                        return;
                    }

                    if (!this.TryQueryForNewerVersion(
                            activity,
                            productUpgrader,
                            out Version newerVersion,
                            out errorMessage))
                    {
                        if (errorMessage != null)
                        {
                            this.tracer.RelatedError(errorMessage);
                        }

                        return;
                    }

                    ProductUpgraderInfo.RecordHighestAvailableVersion(newerVersion);
                }
                catch (Exception ex) when (
                    ex is IOException ||
                    ex is UnauthorizedAccessException ||
                    ex is NotSupportedException)
                {
                    this.tracer.RelatedError(
                        CreateEventMetadata(ex),
                        "Exception encountered recording highest available version");
                }
            }
        }

        private bool TryQueryForNewerVersion(ITracer tracer, GitHubUpgrader productUpgrader, out Version newVersion, out string errorMessage)
        {
            errorMessage = null;
            tracer.RelatedInfo("Querying server for latest version...");

            if (!productUpgrader.TryQueryNewestVersion(out newVersion, out string detailedError))
            {
                errorMessage = "Could not fetch new version info. " + detailedError;
                return false;
            }

            string logMessage = newVersion is null ? "No newer versions available." : $"Newer version available: {newVersion}.";
            tracer.RelatedInfo(logMessage);

            return true;
        }
    }
}

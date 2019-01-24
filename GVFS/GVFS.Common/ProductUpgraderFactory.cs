using GVFS.Common.FileSystem;
using GVFS.Common.Git;
using GVFS.Common.NuGetUpgrader;
using GVFS.Common.Tracing;
using System;
using System.IO;

namespace GVFS.Common
{
    public class ProductUpgraderFactory
    {
        public static bool TryCreateUpgrader(
            out IProductUpgrader newUpgrader,
            ITracer tracer,
            out string error,
            bool dryRun = false,
            bool noVerify = false)
        {
            // Prefer to use the NuGet upgrader if it is configured. If the NuGet upgrader is not configured,
            // then try to use the GitHubUpgrader.
            if (NuGetUpgrader.NuGetUpgrader.TryCreate(tracer, dryRun, noVerify, out NuGetUpgrader.NuGetUpgrader nuGetUpgrader, out bool isConfigured, out error))
            {
                // We were successfully able to load a NuGetUpgrader - use that.
                newUpgrader = nuGetUpgrader;
                return true;
            }
            else
            {
                if (isConfigured)
                {
                    tracer.RelatedError($"{nameof(TryCreateUpgrader)}: Could not create upgrader. {error}");

                    // We did not successfully load a NuGetUpgrader, but it is configured.
                    newUpgrader = null;
                    return false;
                }

                // We did not load a NuGetUpgrader, but it is not the configured upgrader.
                // Try to load other upgraders as appropriate.
            }

            newUpgrader = GitHubUpgrader.Create(tracer, dryRun, noVerify, out error);
            if (newUpgrader == null)
            {
                tracer.RelatedError($"{nameof(TryCreateUpgrader)}: Could not create upgrader. {error}");
                return false;
            }

            return true;
        }
    }
}

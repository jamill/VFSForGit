using GVFS.Common.Tracing;
using System;

namespace GVFS.Common
{
    public class ProductUpgraderFactory
    {
        public static bool TryCreateUpgrader(
            out IProductUpgrader newUpgrader,
            ITracer tracer,
            out string error,
            bool dryRun = false,
            bool noVerify = false,
            Func<string, ITracer, string> credentialDelegate = null)
        {
            newUpgrader = NuGetUpgrader.NuGetUpgrader.Create(tracer, dryRun, noVerify, credentialDelegate, out error);
            if (newUpgrader != null)
            {
               return true;
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

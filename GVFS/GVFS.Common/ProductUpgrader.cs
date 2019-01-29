using GVFS.Common.FileSystem;
using GVFS.Common.Git;
using GVFS.Common.Tracing;
using System;
using System.Collections.Generic;
using System.IO;

namespace GVFS.Common
{
    public abstract class ProductUpgrader : IProductUpgrader
    {
        protected readonly bool dryRun;
        protected readonly bool noVerify;
        protected readonly ITracer tracer;
        protected readonly PhysicalFileSystem fileSystem;

        private const string ToolsDirectory = "Tools";
        private static readonly string UpgraderToolName = GVFSPlatform.Instance.Constants.GVFSUpgraderExecutableName;
        private static readonly string UpgraderToolConfigFile = UpgraderToolName + ".config";
        private static readonly string[] UpgraderToolAndLibs =
            {
                UpgraderToolName,
                UpgraderToolConfigFile,
                "GVFS.Common.dll",
                "GVFS.Platform.Windows.dll",
                "Microsoft.Diagnostics.Tracing.EventSource.dll",
                "netstandard.dll",
                "System.Net.Http.dll",
                "Newtonsoft.Json.dll",
                "CommandLine.dll",
                "NuGet.Common.dll",
                "NuGet.Configuration.dll",
                "NuGet.Frameworks.dll",
                "NuGet.Packaging.Core.dll",
                "NuGet.Packaging.dll",
                "NuGet.Protocol.dll",
                "NuGet.Versioning.dll",
                "System.IO.Compression.dll"
            };

        public ProductUpgrader(
            string currentVersion,
            ITracer tracer,
            bool dryRun,
            bool noVerify)
        {
            this.dryRun = dryRun;
            this.noVerify = noVerify;
            this.tracer = tracer;
        }

        /// <summary>
        /// Deletes any previously downloaded installers in the Upgrader Download directory.
        /// This can include old installers which were downloaded but never installed.
        /// </summary>
        public static void DeleteAllInstallerDownloads(ITracer tracer = null)
        {
            try
            {
                PhysicalFileSystem.RecursiveDelete(ProductUpgraderInfo.GetAssetDownloadsPath());
            }
            catch (Exception ex)
            {
                if (tracer != null)
                {
                    tracer.RelatedError($"{nameof(DeleteAllInstallerDownloads)}: Could not remove directory: {ProductUpgraderInfo.GetAssetDownloadsPath()}.{ex.ToString()}");
                }
            }
        }

        public abstract bool UpgradeAllowed(out string message);

        public abstract bool TryQueryNewestVersion(out Version newVersion, out string message);

        public abstract bool TryDownloadNewestVersion(out string errorMessage);

        public abstract bool TryRunInstaller(InstallActionWrapper installActionWrapper, out string error);

        public virtual bool TrySetupToolsDirectory(out string upgraderToolPath, out string error)
        {
            string rootDirectoryPath = ProductUpgraderInfo.GetUpgradesDirectoryPath();
            string toolsDirectoryPath = Path.Combine(rootDirectoryPath, ToolsDirectory);
            Exception exception;
            if (!this.fileSystem.TryCreateDirectory(toolsDirectoryPath, out exception))
            {
                upgraderToolPath = null;
                error = exception.Message;
                this.TraceException(
                    exception,
                    nameof(this.TrySetupToolsDirectory),
                    $"Error creating upgrade tools directory {toolsDirectoryPath}.");
                return false;
            }

            string currentPath = ProcessHelper.GetCurrentProcessLocation();
            error = null;
            foreach (string name in UpgraderToolAndLibs)
            {
                string toolPath = Path.Combine(currentPath, name);
                string destinationPath = Path.Combine(toolsDirectoryPath, name);
                try
                {
                    this.fileSystem.CopyFile(toolPath, destinationPath, overwrite: true);
                }
                catch (UnauthorizedAccessException e)
                {
                    error = string.Join(
                        Environment.NewLine,
                        "File copy error - " + e.Message,
                        $"Make sure you have write permissions to directory {toolsDirectoryPath} and run {GVFSConstants.UpgradeVerbMessages.GVFSUpgradeConfirm} again.");
                    this.TraceException(e, nameof(this.TrySetupToolsDirectory), $"Error copying {toolPath} to {destinationPath}.");
                    break;
                }
                catch (IOException e)
                {
                    error = "File copy error - " + e.Message;
                    this.TraceException(e, nameof(this.TrySetupToolsDirectory), $"Error copying {toolPath} to {destinationPath}.");
                    break;
                }
            }

            if (string.IsNullOrEmpty(error))
            {
                // There was no error - set upgradeToolPath and return success.
                upgraderToolPath = Path.Combine(toolsDirectoryPath, UpgraderToolName);
                return true;
            }
            else
            {
                // Encountered error - do not set upgrade tool path and return failure.
                upgraderToolPath = null;
                return false;
            }
        }

        public abstract bool TryCleanup(out string error);

        public void TraceException(Exception exception, string method, string message)
        {
            this.TraceException(this.tracer, exception, method, message);
        }

        public void TraceException(ITracer tracer, Exception exception, string method, string message)
        {
            EventMetadata metadata = new EventMetadata();
            metadata.Add("Method", method);
            metadata.Add("Exception", exception.ToString());
            tracer.RelatedError(metadata, message, Keywords.Telemetry);
        }

        public virtual void Dispose()
        {
        }

        protected virtual void RunInstaller(string path, string args, out int exitCode, out string error)
        {
            ProcessResult processResult = ProcessHelper.Run(path, args);

            exitCode = processResult.ExitCode;
            error = processResult.Errors;
        }
    }
}

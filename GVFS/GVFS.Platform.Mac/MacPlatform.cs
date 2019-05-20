using GVFS.Common;
using GVFS.Common.FileSystem;
using GVFS.Common.Tracing;
using GVFS.Platform.POSIX;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GVFS.Platform.Mac
{
    public partial class MacPlatform : POSIXPlatform
    {
        public MacPlatform()
        {
        }

        public override IDiskLayoutUpgradeData DiskLayoutUpgrade { get; } = new MacDiskLayoutUpgradeData();
        public override IKernelDriver KernelDriver { get; } = new ProjFSKext();
        public override string Name { get => "macOS"; }
        public override GVFSPlatformConstants Constants { get; } = new MacPlatformConstants();
        public override IPlatformFileSystem FileSystem { get; } = new MacFileSystem();

        public override string GetOSVersionInformation()
        {
            ProcessResult result = ProcessHelper.Run("sw_vers", args: string.Empty, redirectOutput: true);
            return string.IsNullOrWhiteSpace(result.Output) ? result.Errors : result.Output;
        }

        public override string GetDataRootForGVFS()
        {
            return MacPlatform.GetDataRootForGVFSImplementation();
        }

        public override string GetDataRootForGVFSComponent(string componentName)
        {
            return MacPlatform.GetDataRootForGVFSComponentImplementation(componentName);
        }

        public override bool TryGetGVFSEnlistmentRoot(string directory, out string enlistmentRoot, out string errorMessage)
        {
            return MacPlatform.TryGetGVFSEnlistmentRootImplementation(directory, out enlistmentRoot, out errorMessage);
        }

        public override string GetNamedPipeName(string enlistmentRoot)
        {
            return MacPlatform.GetNamedPipeNameImplementation(enlistmentRoot);
        }

        public override FileBasedLock CreateFileBasedLock(
            PhysicalFileSystem fileSystem,
            ITracer tracer,
            string lockPath)
        {
            return new MacFileBasedLock(fileSystem, tracer, lockPath);
        }

        public override void IsServiceInstalledAndRunning(string name, out bool installed, out bool running)
        {
            ServiceInfo gvfsService = MacServiceProcess.GetServices().FirstOrDefault(sc => string.Equals(sc.Name, "org.vfsforgit.service"));
            installed = gvfsService != null;
            running = installed && gvfsService.IsRunning;
        }

        public class MacPlatformConstants : POSIXPlatformConstants
        {
            public override string InstallerExtension
            {
                get { return ".dmg"; }
            }

            public override string WorkingDirectoryBackingRootPath
            {
                get { return GVFSConstants.WorkingDirectoryRootName; }
            }

            public override string DotGVFSRoot
            {
                get { return MacPlatform.DotGVFSRoot; }
            }

            public override string GVFSBinDirectoryPath
            {
                get { return Path.Combine("/usr", "local", this.GVFSBinDirectoryName); }
            }

            public override string GVFSBinDirectoryName
            {
                get { return "vfsforgit"; }
            }
        }

        private class MacServiceProcess
        {
            public static List<ServiceInfo> GetServices()
            {
                ProcessResult result = ProcessHelper.Run("/bin/launchctl", "list");
                return ParseOutput(result.Output);
            }

            private static List<ServiceInfo> ParseOutput(string output)
            {
                List<ServiceInfo> serviceInfos = new List<ServiceInfo>();
                foreach (string line in output.Split(new string[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries).Skip(1))
                {
                    // The expected output is a list of tab delimited entried:
                    // PID | STATUS | LABEL
                    string[] tokens = line.Split('\t');

                    if (tokens.Length != 3)
                    {
                        continue;
                    }

                    string label = tokens[2];
                    if (!int.TryParse(tokens[0], out int pid))
                    {
                        pid = -1;
                    }

                    // TODO: harden this code
                    serviceInfos.Add(new ServiceInfo() { Name = label, IsRunning = pid > 0 });
                }

                return serviceInfos;
            }
        }

        private class ServiceInfo
        {
            public string Name { get; set; }
            public bool IsRunning { get; set; }
        }
    }
}

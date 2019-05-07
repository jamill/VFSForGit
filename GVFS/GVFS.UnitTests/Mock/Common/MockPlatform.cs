﻿using GVFS.Common;
using GVFS.Common.FileSystem;
using GVFS.Common.Git;
using GVFS.Common.Tracing;
using GVFS.UnitTests.Mock.FileSystem;
using GVFS.UnitTests.Mock.Git;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;

namespace GVFS.UnitTests.Mock.Common
{
    public class MockPlatform : GVFSPlatform
    {
        public MockPlatform() : base(underConstruction: new UnderConstructionFlags())
        {
        }

        public string MockCurrentUser { get; set; }

        public override IKernelDriver KernelDriver => throw new NotSupportedException();

        public override IGitInstallation GitInstallation { get; } = new MockGitInstallation();

        public override IDiskLayoutUpgradeData DiskLayoutUpgrade => throw new NotSupportedException();

        public override IPlatformFileSystem FileSystem { get; } = new MockPlatformFileSystem();

        public override string Name { get => "Mock"; }

        public override GVFSPlatformConstants Constants { get; } = new MockPlatformConstants();

        public HashSet<int> ActiveProcesses { get; } = new HashSet<int>();

        public override void ConfigureVisualStudio(string gitBinPath, ITracer tracer)
        {
            throw new NotSupportedException();
        }

        public override bool TryGetGVFSHooksPathAndVersion(out string hooksPaths, out string hooksVersion, out string error)
        {
            throw new NotSupportedException();
        }

        public override bool TryInstallGitCommandHooks(GVFSContext context, string executingDirectory, string hookName, string commandHookPath, out string errorMessage)
        {
            throw new NotSupportedException();
        }

        public override bool TryVerifyAuthenticodeSignature(string path, out string subject, out string issuer, out string error)
        {
            throw new NotImplementedException();
        }

        public override string GetNamedPipeName(string enlistmentRoot)
        {
            return "GVFS_Mock_PipeName";
        }

        public override string GetGVFSServiceNamedPipeName(string serviceName)
        {
            return Path.Combine("GVFS_Mock_ServicePipeName", serviceName);
        }

        public override NamedPipeServerStream CreatePipeByName(string pipeName)
        {
            throw new NotSupportedException();
        }

        public override string GetCurrentUser()
        {
            return this.MockCurrentUser;
        }

        public override string GetUserIdFromLoginSessionId(int sessionId, ITracer tracer)
        {
            return sessionId.ToString();
        }

        public override string GetOSVersionInformation()
        {
            throw new NotSupportedException();
        }

        public override string GetDataRootForGVFS()
        {
            // TODO: Update this method to return non existant file path.
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "GVFS");
        }

        public override string GetDataRootForGVFSComponent(string componentName)
        {
            return Path.Combine(this.GetDataRootForGVFS(), componentName);
        }

        public override Dictionary<string, string> GetPhysicalDiskInfo(string path, bool sizeStatsOnly)
        {
            return new Dictionary<string, string>();
        }

        public override void InitializeEnlistmentACLs(string enlistmentPath)
        {
            throw new NotSupportedException();
        }

        public override bool IsConsoleOutputRedirectedToFile()
        {
            throw new NotSupportedException();
        }

        public override bool IsElevated()
        {
            throw new NotSupportedException();
        }

        public override bool IsProcessActive(int processId)
        {
            return this.ActiveProcesses.Contains(processId);
        }

        public override void IsServiceInstalledAndRunning(string name, out bool installed, out bool running)
        {
            throw new NotSupportedException();
        }

        public override bool TryGetGVFSEnlistmentRoot(string directory, out string enlistmentRoot, out string errorMessage)
        {
            throw new NotSupportedException();
        }

        public override bool TryGetDefaultLocalCacheRoot(string enlistmentRoot, out string localCacheRoot, out string localCacheRootError)
        {
            throw new NotImplementedException();
        }

        public override void StartBackgroundProcess(ITracer tracer, string programName, string[] args)
        {
            throw new NotSupportedException();
        }

        public override bool IsGitStatusCacheSupported()
        {
            return true;
        }

        public override string GetProgramLocater()
        {
            return "MockWhere";
        }

        public override FileBasedLock CreateFileBasedLock(PhysicalFileSystem fileSystem, ITracer tracer, string lockPath)
        {
            return new MockFileBasedLock(fileSystem, tracer, lockPath);
        }

        public override bool TryKillProcessTree(int processId, out int exitCode, out string error)
        {
            error = null;
            exitCode = 0;
            return true;
        }

        public class MockPlatformConstants : GVFSPlatformConstants
        {
            public override string ExecutableExtension
            {
                get { return ".mockexe"; }
            }

            public override string InstallerExtension
            {
                get { return ".mockexe"; }
            }

            public override string WorkingDirectoryBackingRootPath
            {
                get { return GVFSConstants.WorkingDirectoryRootName; }
            }

            public override string DotGVFSRoot
            {
                get { return ".mockvfsforgit"; }
            }

            public override string GVFSBinDirectoryPath
            {
                get { return Path.Combine("MockProgramFiles", this.GVFSBinDirectoryName); }
            }

            public override string GVFSBinDirectoryName
            {
                get { return "MockGVFS"; }
            }

            public override string GVFSExecutableName
            {
                get { return "MockGVFS" + this.ExecutableExtension; }
            }
        }
    }
}

using GVFS.Common;
using GVFS.Common.Git;
using GVFS.UnitTests.Mock.Git;

namespace GVFS.UnitTests.Mock.Common
{
    public class MockGVFSEnlistment : GVFSEnlistment
    {
        public MockGVFSEnlistment(string enlistmentRoot, string repoUrl, string gitBinPath, string gvfsHooksRoot, GitProcessFactory gitProcessFactory)
            : base(enlistmentRoot, repoUrl, gitBinPath, gvfsHooksRoot, authentication: null, gitProcessFactory: gitProcessFactory)
        {
        }

        public MockGVFSEnlistment(GitProcessFactory gitProcessFactory)
            : this("mock:\\path", "mock://repoUrl", "mock:\\git", gvfsHooksRoot: null, gitProcessFactory: gitProcessFactory)
        {
        }

        public MockGVFSEnlistment()
            : this(gitProcessFactory: new GitProcessFactory())
        {
            this.GitObjectsRoot = "mock:\\path\\.git\\objects";
            this.LocalObjectsRoot = this.GitObjectsRoot;
            this.GitPackRoot = "mock:\\path\\.git\\objects\\pack";
        }

        public MockGVFSEnlistment(MockGitProcess gitProcess)
            : this(gitProcessFactory: new StaticGitProcessFactory(gitProcess))
        {
        }

        public override string GitObjectsRoot { get; protected set; }

        public override string LocalObjectsRoot { get; protected set; }

        public override string GitPackRoot { get; protected set; }
    }
}

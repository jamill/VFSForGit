namespace GVFS.Common.Git
{
    public class StaticGitProcessFactory : GitProcessFactory
    {
        private GitProcess process;

        public StaticGitProcessFactory(GitProcess process)
        {
            this.process = process;
        }

        public override GitProcess CreateGitProcess(Enlistment enlistment)
        {
            return this.process;
        }

        public override GitProcess CreateGitProcess(string gitBinPath, string workingDirectoryRoot, string gvfsHooksRoot)
        {
            return this.process;
        }
    }
}

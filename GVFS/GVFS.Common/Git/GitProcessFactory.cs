namespace GVFS.Common.Git
{
    public class GitProcessFactory
    {
        public virtual GitProcess CreateGitProcess(Enlistment enlistment)
        {
            return new GitProcess(enlistment.GitBinPath, enlistment.WorkingDirectoryRoot, enlistment.GVFSHooksRoot);
        }

        public virtual GitProcess CreateGitProcess(string gitBinPath, string workingDirectoryRoot, string gvfsHooksRoot)
        {
            return new GitProcess(gitBinPath, workingDirectoryRoot, gvfsHooksRoot);
        }
    }
}

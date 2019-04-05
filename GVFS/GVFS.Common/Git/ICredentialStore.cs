using GVFS.Common.Tracing;

namespace GVFS.Common.Git
{
    public interface ICredentialStore
    {
        bool TryGetCredential(ITracer tracer, string url, out string username, out string password, out string error);

        void StoreCredential(ITracer tracer, string url, string username, string password);

        void DeleteCredential(ITracer tracer, string url, string username, string password);
    }
}

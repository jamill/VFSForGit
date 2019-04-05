using GVFS.Common.Tracing;

namespace GVFS.Common.Git
{
    public interface ICredentialStore
    {
        /// <summary>
        /// Get a credential to be used for a URL.
        /// </summary>
        /// <param name="tracer"></param>
        /// <param name="url"></param>
        /// </<exception cref="GVFSException">Indicates an error querying for credentials.</exception>
        /// <returns>Credential if it was able to query the credential store. If there is no credential for the URL, will retun null.</returns>
        SimpleCredential GetCredential(ITracer tracer, string url);

        /// <summary>
        /// Store a credential
        /// </summary>
        /// <param name="tracer"></param>
        /// <param name="url"></param>
        /// </<exception cref="GVFSException">Indicates an error occured when storing the credential.</exception>
        void StoreCredential(ITracer tracer, string url, string username, string password);

        /// <summary>
        /// Delete a credential
        /// </summary>
        /// <param name="tracer"></param>
        /// <param name="url"></param>
        /// </<exception cref="GVFSException">Indicates an error occured when Deleting the credential.</exception>
        void DeleteCredential(ITracer tracer, string url, string username, string password);
    }
}

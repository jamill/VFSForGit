using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;

namespace GVFS.Common
{
    /// <summary>
    /// Class that handles communication with a server that contains version information.
    /// </summary>
    public class OrgInfoServer
    {
        private HttpClient client;
        private string baseUrl;

        public OrgInfoServer(HttpClient client, string baseUrl)
        {
            this.client = client;
            this.baseUrl = baseUrl;
        }

        private string VersionUrl
        {
            get
            {
                return this.baseUrl + "/version";
            }
        }

        public Version QueryNewestVersion()
        {
            string responseString = this.client.GetStringAsync(this.VersionUrl).GetAwaiter().GetResult();
            VersionResponse versionResponse = VersionResponse.FromJsonString(responseString);

            return new Version(versionResponse.Version);
        }

        public Version QueryNewestVersion(string orgName, string ring)
        {
            Dictionary<string, string> queryParams = new Dictionary<string, string>()
            {
                { "Organization", orgName },
                { "Ring", ring },
            };

            string responseString = this.client.GetStringAsync(this.ConstructRequest(this.VersionUrl, queryParams)).GetAwaiter().GetResult();
            VersionResponse versionResponse = VersionResponse.FromJsonString(responseString);

            return new Version(versionResponse.Version);
        }

        private string ConstructRequest(string baseUrl, Dictionary<string, string> queryParams)
        {
            StringBuilder sb = new StringBuilder(baseUrl);

            if (queryParams.Any())
            {
                sb.Append("?");
            }

            bool isFirst = true;
            foreach (KeyValuePair<string, string> kvp in queryParams)
            {
                if (!isFirst)
                {
                    sb.Append("&");
                    isFirst = false;
                }

                sb.Append($"{kvp.Key}={kvp.Value}");
            }

            return sb.ToString();
        }
    }
}

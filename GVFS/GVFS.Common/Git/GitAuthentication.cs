﻿using GVFS.Common.Http;
using GVFS.Common.Tracing;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace GVFS.Common.Git
{
    public class GitAuthentication
    {
        private const double MaxBackoffSeconds = 30;

        private readonly object gitAuthLock = new object();
        private readonly ICredentialStore credentialStore;
        private readonly string repoUrl;

        private int numberOfAttempts = 0;
        private DateTime lastAuthAttempt = DateTime.MinValue;

        private string cachedCredentialString;
        private bool isCachedCredentialStringApproved = false;

        private bool isInitialized;

        public GitAuthentication(GitProcess git, string repoUrl)
        {
            this.credentialStore = git;
            this.repoUrl = repoUrl;

            if (git.TryGetConfigUrlMatch("http", this.repoUrl, out Dictionary<string, GitConfigSetting> configSettings))
            {
                this.GitSsl = new GitSsl(configSettings);
            }
        }

        public bool IsBackingOff
        {
            get
            {
                return this.GetNextAuthAttemptTime() > DateTime.Now;
            }
        }

        public bool IsAnonymous { get; private set; } = true;

        private GitSsl GitSsl { get; }

        public void ApproveCredentials(ITracer tracer, string credentialString)
        {
            lock (this.gitAuthLock)
            {
                // Don't reset the backoff if this is for a different credential than we have cached
                if (credentialString == this.cachedCredentialString)
                {
                    this.numberOfAttempts = 0;
                    this.lastAuthAttempt = DateTime.MinValue;

                    // Tell Git to store the valid credential if we haven't already
                    // done so for this cached credential.
                    if (!this.isCachedCredentialStringApproved)
                    {
                        string username;
                        string password;
                        if (TryParseCredentialString(this.cachedCredentialString, out username, out password))
                        {
                            try
                            {
                                this.credentialStore.StoreCredential(tracer, this.repoUrl, username, password);
                            }
                            catch (GVFSException exception)
                            {
                                // Storing credentials is best effort attempt - log failure, but do not fail
                                tracer.RelatedWarning("Failed to store credential string: {0}", exception.Message);
                            }

                            this.isCachedCredentialStringApproved = true;
                        }
                        else
                        {
                            EventMetadata metadata = new EventMetadata(new Dictionary<string, object>
                            {
                                ["RepoUrl"] = this.repoUrl,
                            });
                            tracer.RelatedError(metadata, "Failed to parse credential string for approval");
                        }
                    }
                }
            }
        }

        public void RejectCredentials(ITracer tracer, string credentialString)
        {
            lock (this.gitAuthLock)
            {
                // Don't stomp a different credential
                if (credentialString == this.cachedCredentialString && this.cachedCredentialString != null)
                {
                    // If we can we should pass the actual username/password values we used (and found to be invalid)
                    // to `git-credential reject` so the credential helpers can attempt to check if they're erasing
                    // the expected credentials, if they so choose to.
                    string username;
                    string password;
                    if (TryParseCredentialString(this.cachedCredentialString, out username, out password))
                    {
                        try
                        {
                            this.credentialStore.DeleteCredential(tracer, this.repoUrl, username, password);
                        }
                        catch (GVFSException exception)
                        {
                            // Deleting credentials is best effort attempt - log failure, but do not fail
                            tracer.RelatedWarning("Failed to delete credential string: {0}", exception.Message);
                        }
                    }
                    else
                    {
                        // We failed to parse the credential string so instead (as a recovery) we try to erase without
                        // specifying the particular username/password.
                        EventMetadata metadata = new EventMetadata(new Dictionary<string, object>
                        {
                            ["RepoUrl"] = this.repoUrl,
                        });
                        tracer.RelatedWarning(metadata, "Failed to parse credential string for rejection. Rejecting any credential for this repo URL.");

                        try
                        {
                            this.credentialStore.DeleteCredential(tracer, this.repoUrl, username, password);
                        }
                        catch (GVFSException exception)
                        {
                            // Deleting credentials is best effort attempt - log failure, but do not fail
                            tracer.RelatedWarning("Failed to delete credential string: {0}", exception.Message);
                        }
                    }

                    this.cachedCredentialString = null;
                    this.isCachedCredentialStringApproved = false;
                    this.UpdateBackoff();
                }
            }
        }

        public bool TryGetCredentials(ITracer tracer, out string credentialString, out string errorMessage)
        {
            if (!this.isInitialized)
            {
                throw new InvalidOperationException("This auth instance must be initialized before it can be used");
            }

            credentialString = this.cachedCredentialString;
            if (credentialString == null)
            {
                lock (this.gitAuthLock)
                {
                    if (this.cachedCredentialString == null)
                    {
                        if (this.IsBackingOff)
                        {
                            errorMessage = "Auth failed. No retries will be made until: " + this.GetNextAuthAttemptTime();
                            return false;
                        }

                        if (!this.TryCallGitCredential(tracer, out errorMessage))
                        {
                            return false;
                        }
                    }

                    credentialString = this.cachedCredentialString;
                }
            }

            errorMessage = null;
            return true;
        }

        public bool TryInitialize(ITracer tracer, Enlistment enlistment, out string errorMessage)
        {
            if (this.isInitialized)
            {
                throw new InvalidOperationException("Already initialized");
            }

            errorMessage = null;

            bool isAnonymous;
            if (!this.TryAnonymousQuery(tracer, enlistment, out isAnonymous))
            {
                errorMessage = $"Unable to determine if authentication is required";
                return false;
            }

            if (!isAnonymous &&
                !this.TryCallGitCredential(tracer, out errorMessage))
            {
                return false;
            }

            this.IsAnonymous = isAnonymous;
            this.isInitialized = true;
            return true;
        }

        public bool TryInitializeAndRequireAuth(ITracer tracer, out string errorMessage)
        {
            if (this.isInitialized)
            {
                throw new InvalidOperationException("Already initialized");
            }

            if (this.TryCallGitCredential(tracer, out errorMessage))
            {
                this.isInitialized = true;
                return true;
            }

            return false;
        }

        public void ConfigureHttpClientHandlerSslIfNeeded(ITracer tracer, HttpClientHandler httpClientHandler, GitProcess gitProcess)
        {
            X509Certificate2 cert = this.GitSsl?.GetCertificate(tracer, gitProcess);
            if (cert != null)
            {
                if (this.GitSsl != null && !this.GitSsl.ShouldVerify)
                {
                    httpClientHandler.ServerCertificateCustomValidationCallback =
                        (httpRequestMessage, c, cetChain, policyErrors) =>
                        {
                            return true;
                        };
                }

                httpClientHandler.ClientCertificateOptions = ClientCertificateOption.Manual;
                httpClientHandler.ClientCertificates.Add(cert);
            }
        }

        private static bool TryParseCredentialString(string credentialString, out string username, out string password)
        {
            if (credentialString != null)
            {
                byte[] data = Convert.FromBase64String(credentialString);
                string rawCredString = Encoding.ASCII.GetString(data);

                string[] usernamePassword = rawCredString.Split(':');
                if (usernamePassword.Length == 2)
                {
                    username = usernamePassword[0];
                    password = usernamePassword[1];

                    return true;
                }
            }

            username = null;
            password = null;
            return false;
        }

        private bool TryAnonymousQuery(ITracer tracer, Enlistment enlistment, out bool isAnonymous)
        {
            bool querySucceeded;
            using (ITracer anonymousTracer = tracer.StartActivity("AttemptAnonymousAuth", EventLevel.Informational))
            {
                HttpStatusCode? httpStatus;

                using (ConfigHttpRequestor configRequestor = new ConfigHttpRequestor(anonymousTracer, enlistment, new RetryConfig()))
                {
                    ServerGVFSConfig gvfsConfig;
                    const bool LogErrors = false;
                    if (configRequestor.TryQueryGVFSConfig(LogErrors, out gvfsConfig, out httpStatus, out _))
                    {
                        querySucceeded = true;
                        isAnonymous = true;
                    }
                    else if (httpStatus == HttpStatusCode.Unauthorized)
                    {
                        querySucceeded = true;
                        isAnonymous = false;
                    }
                    else
                    {
                        querySucceeded = false;
                        isAnonymous = false;
                    }
                }

                anonymousTracer.Stop(new EventMetadata
                {
                    { "HttpStatus", httpStatus.HasValue ? ((int)httpStatus).ToString() : "None" },
                    { "QuerySucceeded", querySucceeded },
                    { "IsAnonymous", isAnonymous },
                });
            }

            return querySucceeded;
        }

        private DateTime GetNextAuthAttemptTime()
        {
            if (this.numberOfAttempts <= 1)
            {
                return DateTime.MinValue;
            }

            double backoffSeconds = RetryBackoff.CalculateBackoffSeconds(this.numberOfAttempts, MaxBackoffSeconds);
            return this.lastAuthAttempt + TimeSpan.FromSeconds(backoffSeconds);
        }

        private void UpdateBackoff()
        {
            this.lastAuthAttempt = DateTime.Now;
            this.numberOfAttempts++;
        }

        private bool TryCallGitCredential(ITracer tracer, out string errorMessage)
        {
            SimpleCredential credential;

            try
            {
                credential = this.credentialStore.GetCredential(tracer, this.repoUrl);
            }
            catch (GVFSException ex)
            {
                errorMessage = ex.Message;
                this.UpdateBackoff();
                return false;
            }

            if (!string.IsNullOrEmpty(credential?.Username) &&
                !string.IsNullOrEmpty(credential?.Password))
            {
                this.cachedCredentialString = credential.BasicAuthString;
                this.isCachedCredentialStringApproved = false;
            }
            else
            {
                this.UpdateBackoff();
                errorMessage = "Got back empty credentials from git";
                return false;
            }

            errorMessage = null;
            return true;
        }
    }
}

﻿using GVFS.Common.Git;
using GVFS.Common.Tracing;
using GVFS.Tests.Should;
using GVFS.UnitTests.Mock.Common;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace GVFS.UnitTests.Mock.Git
{
    public class MockGitProcess : GitProcess
    {
        private List<CommandInfo> expectedCommandInfos = new List<CommandInfo>();

        public MockGitProcess()
            : base(new MockGVFSEnlistment())
        {
            this.CommandsRun = new List<string>();
            this.StoredCredentials = new Dictionary<string, SimpleCredential>(StringComparer.OrdinalIgnoreCase);
            this.CredentialApprovals = new Dictionary<string, List<SimpleCredential>>();
            this.CredentialRejections = new Dictionary<string, List<SimpleCredential>>();
        }

        public List<string> CommandsRun { get; }
        public bool ShouldFail { get; set; }
        public Dictionary<string, SimpleCredential> StoredCredentials { get; }
        public Dictionary<string, List<SimpleCredential>> CredentialApprovals { get; }
        public Dictionary<string, List<SimpleCredential>> CredentialRejections { get; }

        public void SetExpectedCommandResult(string command, Func<Result> result, bool matchPrefix = false)
        {
            CommandInfo commandInfo = new CommandInfo(command, result, matchPrefix);
            this.expectedCommandInfos.Add(commandInfo);
        }

        public override void StoreCredential(ITracer tracer, string repoUrl, string username, string password)
        {
            SimpleCredential credential = new SimpleCredential(username, password);

            // Record the approval request for this credential
            List<SimpleCredential> acceptedCredentials;
            if (!this.CredentialApprovals.TryGetValue(repoUrl, out acceptedCredentials))
            {
                acceptedCredentials = new List<SimpleCredential>();
                this.CredentialApprovals[repoUrl] = acceptedCredentials;
            }

            acceptedCredentials.Add(credential);

            // Store the credential
            this.StoredCredentials[repoUrl] = credential;

            base.StoreCredential(tracer, repoUrl, username, password);
        }

        public override void DeleteCredential(ITracer tracer, string repoUrl, string username, string password)
        {
            SimpleCredential credential = new SimpleCredential(username, password);

            // Record the rejection request for this credential
            List<SimpleCredential> rejectedCredentials;
            if (!this.CredentialRejections.TryGetValue(repoUrl, out rejectedCredentials))
            {
                rejectedCredentials = new List<SimpleCredential>();
                this.CredentialRejections[repoUrl] = rejectedCredentials;
            }

            rejectedCredentials.Add(credential);

            // Erase the credential
            this.StoredCredentials.Remove(repoUrl);

            base.DeleteCredential(tracer, repoUrl, username, password);
        }

        protected override Result InvokeGitImpl(
            string command,
            string workingDirectory,
            string dotGitDirectory,
            bool useReadObjectHook,
            Action<StreamWriter> writeStdIn,
            Action<string> parseStdOutLine,
            int timeoutMs,
            string gitObjectsDirectory = null)
        {
            this.CommandsRun.Add(command);

            if (this.ShouldFail)
            {
                return new Result(string.Empty, string.Empty, Result.GenericFailureCode);
            }

            Predicate<CommandInfo> commandMatchFunction =
                (CommandInfo commandInfo) =>
                {
                    if (commandInfo.MatchPrefix)
                    {
                        return command.StartsWith(commandInfo.Command);
                    }
                    else
                    {
                        return string.Equals(command, commandInfo.Command, StringComparison.Ordinal);
                    }
                };

            CommandInfo matchedCommand = this.expectedCommandInfos.Find(commandMatchFunction);
            matchedCommand.ShouldNotBeNull("Unexpected command: " + command);

            return matchedCommand.Result();
        }

        private class CommandInfo
        {
            public CommandInfo(string command, Func<Result> result, bool matchPrefix)
            {
                this.Command = command;
                this.Result = result;
                this.MatchPrefix = matchPrefix;
            }

            public string Command { get; private set; }

            public Func<Result> Result { get; private set; }

            public bool MatchPrefix { get; private set; }
        }
    }
}

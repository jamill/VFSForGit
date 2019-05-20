using GVFS.Common;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GVFS.Platform.Mac
{
    /// <summary>
    /// Class to query the configured services on macOS
    /// </summary>
    public class MacServiceProcess
    {
        private const string LaunchCtlPath = @"/bin/launchctl";
        private const string LaunchCtlArg = @"list";

        private IProcessHelper processHelper;

        public MacServiceProcess()
        : this(new ProcessHelperImpl())
        {
        }

        public MacServiceProcess(IProcessHelper processHelper)
        {
            this.processHelper = processHelper;
        }

        public bool TryGetServices(string currentUser, out List<ServiceInfo> services, out string error)
        {
            // HACK:
            // Use Launchtl to run Launchctl as the "real" user, so we can get the process list from the user.
            ProcessResult result = this.processHelper.Run(LaunchCtlPath, "asuser " + currentUser + " "  + LaunchCtlPath + " " + LaunchCtlArg, true);

            if (result.ExitCode != 0)
            {
                error = result.Output;
                services = null;
                return false;
            }

            return this.TryParseOutput(result.Output, out services, out error);
        }

        private bool TryParseOutput(string output, out List<ServiceInfo> serviceInfos, out string error)
        {
            serviceInfos = new List<ServiceInfo>();

            // 1st line is the header, skip it
            foreach (string line in output.Split(new string[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries).Skip(1))
            {
                // The expected output is a list of tab delimited entried:
                // PID\tSTATUS\tLABEL
                string[] tokens = line.Split('\t');

                if (tokens.Length != 3)
                {
                    serviceInfos = null;
                    error = $"Unexpected number of tokens in line: {line}";
                    return false;
                }

                string label = tokens[2];
                bool isRunning = int.TryParse(tokens[0], out _);

                serviceInfos.Add(new ServiceInfo() { Name = label, IsRunning = isRunning });
            }

            error = null;
            return true;
        }

        public class ServiceInfo
        {
            public string Name { get; set; }
            public bool IsRunning { get; set; }
        }
    }
}

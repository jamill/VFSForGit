using GVFS.Common;
using GVFS.Platform.Mac;
using GVFS.Tests.Should;
using Moq;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace GVFS.UnitTests.Platform.Mac
{
    [TestFixture]
    public class MacServiceProcessTests
    {
        [TestCase]
        public void CanGetServices()
        {
            Mock<IProcessHelper> processHelperMock = new Mock<IProcessHelper>(MockBehavior.Strict);

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("PID\tStatus\tLabel");
            sb.AppendLine("1\t0\tcom.apple.process1");
            sb.AppendLine("2\t0\tcom.apple.process2");
            sb.AppendLine("3\t0\tcom.apple.process3");
            sb.AppendLine("-\t0\tcom.apple.process4");

            ProcessResult processResult = new ProcessResult(sb.ToString(), string.Empty, 0);

            processHelperMock.Setup(m => m.Run("/bin/launchctl", "asuser 521 /bin/launchctl list", true)).Returns(processResult);

            MacServiceProcess macServiceProcess = new MacServiceProcess(processHelperMock.Object);
            bool success = macServiceProcess.TryGetServices("521", out List<MacServiceProcess.ServiceInfo> services, out string error);

            success.ShouldBeTrue();
            services.ShouldNotBeNull();
            services.Count.ShouldEqual(4);
            processHelperMock.VerifyAll();
        }
    }
}

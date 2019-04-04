using GVFS.Common.FileSystem;
using GVFS.Service;
using GVFS.UnitTests.Mock.Common;
using GVFS.UnitTests.Windows.Mock.Upgrader;
using Moq;
using NUnit.Framework;

namespace GVFS.UnitTests.Windows.Windows.Service
{
    public class ProductUpgraderTimerTests
    {
        [TestCase]
        public void QueryGitHubForUpdates()
        {
            MockTracer tracer = new MockTracer();
            Mock<PhysicalFileSystem> fileSystemMock = new Mock<PhysicalFileSystem>();
            MockLocalGVFSConfig gvfsConfig = new MockLocalGVFSConfig();

            using (ProductUpgradeTimer upgradeChecker = new ProductUpgradeTimer(tracer, fileSystemMock.Object, gvfsConfig))
            {
                upgradeChecker.Start();
            }
        }

        [TestCase]
        public void QueriesOrgInfoServerForUpdates()
        {
        }
    }
}

using GVFS.Common;
using GVFS.Common.FileSystem;
using GVFS.Common.NuGetUpgrader;
using GVFS.Tests.Should;
using GVFS.UnitTests.Mock.Common;
using Moq;
using NuGet.Packaging.Core;
using NuGet.Protocol.Core.Types;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace GVFS.UnitTests.Common
{
    [TestFixture]
    public class NuGetUpgraderTests
    {
        private const string OlderVersion = "1.0.1185.0";
        private const string CurrentVersion = "1.5.1185.0";
        private const string NewerVersion = "1.6.1185.0";
        private const string NewerVersion2 = "1.7.1185.0";

        private const string NuGetFeedUrl = "feedUrlValue";
        private const string NuGetFeedUrlForCredentials = "feedUrlForCredentialsValue";
        private const string NuGetFeedName = "feedNameValue";

        private NuGetUpgrader upgrader;
        private MockTracer tracer;

        private NuGetUpgrader.NugetUpgraderConfig upgraderConfig;
        private string downloadFolder;

        private Mock<NuGetFeed> mockNuGetFeed;
        private Mock<PhysicalFileSystem> mockFileSystem;

        [SetUp]
        public void SetUp()
        {
            this.upgraderConfig = new NuGetUpgrader.NugetUpgraderConfig(this.tracer, null, NuGetFeedUrl, NuGetFeedName, NuGetFeedUrlForCredentials);
            this.downloadFolder = "downloadFolderTestValue";

            this.tracer = new MockTracer();

            this.mockNuGetFeed = new Mock<NuGetFeed>(
                NuGetFeedUrl,
                NuGetFeedName,
                this.downloadFolder,
                null,
                this.tracer);
            this.mockFileSystem = new Mock<PhysicalFileSystem>();

            this.upgrader = new NuGetUpgrader(
                CurrentVersion,
                this.tracer,
                this.upgraderConfig,
                false,
                this.mockFileSystem.Object,
                this.mockNuGetFeed.Object,
                new LocalUpgraderServices(this.tracer, this.mockFileSystem.Object));
        }

        [TearDown]
        public void TearDown()
        {
            this.mockNuGetFeed.Object.Dispose();
        }

        [TestCase]
        public void TryQueryNewestVersion_NewVersionAvailable()
        {
            Version newVersion;
            string message;
            List<IPackageSearchMetadata> availablePackages = new List<IPackageSearchMetadata>()
            {
                this.GeneratePackageSeachMetadata(new Version(CurrentVersion)),
                this.GeneratePackageSeachMetadata(new Version(NewerVersion)),
            };

            this.mockNuGetFeed.Setup(foo => foo.QueryFeedAsync(It.IsAny<string>())).ReturnsAsync(new List<IPackageSearchMetadata>(availablePackages));

            bool success = this.upgrader.TryQueryNewestVersion(out newVersion, out message);

            // Assert that we found the newer version
            success.ShouldBeTrue();
            newVersion.ShouldNotBeNull();
            newVersion.ShouldEqual<Version>(new Version(NewerVersion));
            message.ShouldNotBeNull();
        }

        [TestCase]
        public void TryQueryNewestVersion_MultipleNewVersionsAvailable()
        {
            Version newVersion;
            string message;
            List<IPackageSearchMetadata> availablePackages = new List<IPackageSearchMetadata>()
            {
                this.GeneratePackageSeachMetadata(new Version(CurrentVersion)),
                this.GeneratePackageSeachMetadata(new Version(NewerVersion)),
                this.GeneratePackageSeachMetadata(new Version(NewerVersion2)),
            };

            this.mockNuGetFeed.Setup(foo => foo.QueryFeedAsync(It.IsAny<string>())).ReturnsAsync(new List<IPackageSearchMetadata>(availablePackages));

            bool success = this.upgrader.TryQueryNewestVersion(out newVersion, out message);

            // Assert that we found the newest version
            success.ShouldBeTrue();
            newVersion.ShouldNotBeNull();
            newVersion.ShouldEqual<Version>(new Version(NewerVersion2));
            message.ShouldNotBeNull();
        }

        [TestCase]
        public void TryQueryNewestVersion_NoNewerVersionsAvailable()
        {
            Version newVersion;
            string message;
            List<IPackageSearchMetadata> availablePackages = new List<IPackageSearchMetadata>()
            {
                this.GeneratePackageSeachMetadata(new Version(OlderVersion)),
                this.GeneratePackageSeachMetadata(new Version(CurrentVersion)),
            };

            this.mockNuGetFeed.Setup(foo => foo.QueryFeedAsync(It.IsAny<string>())).ReturnsAsync(new List<IPackageSearchMetadata>(availablePackages));

            bool success = this.upgrader.TryQueryNewestVersion(out newVersion, out message);

            // Assert that no new version was returned
            success.ShouldBeTrue();
            newVersion.ShouldBeNull();
        }

        [TestCase]
        public void TryQueryNewestVersion_Exception()
        {
            Version newVersion;
            string message;
            List<IPackageSearchMetadata> availablePackages = new List<IPackageSearchMetadata>()
            {
                this.GeneratePackageSeachMetadata(new Version(OlderVersion)),
                this.GeneratePackageSeachMetadata(new Version(CurrentVersion)),
            };

            this.mockNuGetFeed.Setup(foo => foo.QueryFeedAsync(It.IsAny<string>())).Throws(new Exception("Network Error"));

            bool success = this.upgrader.TryQueryNewestVersion(out newVersion, out message);

            // Assert that no new version was returned
            success.ShouldBeFalse();
            newVersion.ShouldBeNull();
        }

        [TestCase]
        public void CanDownloadNewestVersion()
        {
            Version newVersion;
            string message;
            List<IPackageSearchMetadata> availablePackages = new List<IPackageSearchMetadata>()
            {
                this.GeneratePackageSeachMetadata(new Version(CurrentVersion)),
                this.GeneratePackageSeachMetadata(new Version(NewerVersion)),
            };

            this.mockNuGetFeed.Setup(foo => foo.QueryFeedAsync(It.IsAny<string>())).ReturnsAsync(new List<IPackageSearchMetadata>(availablePackages));

            // TODO: verify expected argument
            this.mockNuGetFeed.Setup(foo => foo.DownloadPackage(It.IsAny<PackageIdentity>())).Returns(Task.FromResult("package_path"));

            bool success = this.upgrader.TryQueryNewestVersion(out newVersion, out message);

            // Assert that no new version was returned
            success.ShouldBeTrue($"Expecting TryQueryNewestVersion to have completed sucessfully. Error: {message}");
            newVersion.ShouldNotBeNull();

            bool downloadSuccessful = this.upgrader.TryDownloadNewestVersion(out message);
            downloadSuccessful.ShouldBeTrue();
        }

        [TestCase]
        public void DownloadNewestVersion_HandleException()
        {
            Version newVersion;
            string message;
            List<IPackageSearchMetadata> availablePackages = new List<IPackageSearchMetadata>()
            {
                this.GeneratePackageSeachMetadata(new Version(CurrentVersion)),
                this.GeneratePackageSeachMetadata(new Version(NewerVersion)),
            };

            this.mockNuGetFeed.Setup(foo => foo.QueryFeedAsync(It.IsAny<string>())).ReturnsAsync(new List<IPackageSearchMetadata>(availablePackages));

            // TODO: verify expected argument
            this.mockNuGetFeed.Setup(foo => foo.DownloadPackage(It.IsAny<PackageIdentity>())).Throws(new Exception("Network Error"));

            bool success = this.upgrader.TryQueryNewestVersion(out newVersion, out message);

            // Assert that no new version was returned
            success.ShouldBeTrue($"Expecting TryQueryNewestVersion to have completed sucessfully. Error: {message}");
            newVersion.ShouldNotBeNull();

            bool downloadSuccessful = this.upgrader.TryDownloadNewestVersion(out message);
            downloadSuccessful.ShouldBeFalse();
        }

        [TestCase]
        public void TestUpgradeAllowed()
        {
            // Properly Configured NuGet config FeedUrlForCredentials
            NuGetUpgrader.NugetUpgraderConfig nuGetUpgraderConfig =
                new NuGetUpgrader.NugetUpgraderConfig(this.tracer, null, NuGetFeedUrl, NuGetFeedName, NuGetFeedUrlForCredentials);

            NuGetUpgrader nuGetUpgrader = new NuGetUpgrader(
                CurrentVersion,
                this.tracer,
                nuGetUpgraderConfig,
                false,
                this.mockFileSystem.Object,
                this.mockNuGetFeed.Object,
                new LocalUpgraderServices(this.tracer, this.mockFileSystem.Object));

            nuGetUpgrader.UpgradeAllowed(out string _).ShouldBeTrue("NuGetUpgrader config is complete: upgrade should be allowed.");

            // Empty FeedURL
            nuGetUpgraderConfig =
                new NuGetUpgrader.NugetUpgraderConfig(this.tracer, null, string.Empty, NuGetFeedName, NuGetFeedUrlForCredentials);

             nuGetUpgrader = new NuGetUpgrader(
                CurrentVersion,
                this.tracer,
                nuGetUpgraderConfig,
                false,
                this.mockFileSystem.Object,
                this.mockNuGetFeed.Object,
                new LocalUpgraderServices(this.tracer, this.mockFileSystem.Object));

            nuGetUpgrader.UpgradeAllowed(out string _).ShouldBeFalse("Upgrade without FeedURL configured should not be allowed.");

            // Empty packageFeedName
            nuGetUpgraderConfig =
                new NuGetUpgrader.NugetUpgraderConfig(this.tracer, null, NuGetFeedUrl, string.Empty, NuGetFeedUrlForCredentials);

            // Empty packageFeedName
            nuGetUpgrader = new NuGetUpgrader(
                CurrentVersion,
                this.tracer,
                nuGetUpgraderConfig,
                false,
                this.mockFileSystem.Object,
                this.mockNuGetFeed.Object,
                new LocalUpgraderServices(this.tracer, this.mockFileSystem.Object));

            nuGetUpgrader.UpgradeAllowed(out string _).ShouldBeFalse("Upgrade without FeedName configured should not be allowed.");

            // Empty FeedUrlForCredentials
            nuGetUpgraderConfig =
                new NuGetUpgrader.NugetUpgraderConfig(this.tracer, null, NuGetFeedUrl, NuGetFeedName, string.Empty);

            nuGetUpgrader = new NuGetUpgrader(
                CurrentVersion,
                this.tracer,
                nuGetUpgraderConfig,
                false,
                this.mockFileSystem.Object,
                this.mockNuGetFeed.Object,
                new LocalUpgraderServices(this.tracer, this.mockFileSystem.Object));

            nuGetUpgrader.UpgradeAllowed(out string _).ShouldBeFalse("Upgrade without FeedUrlForCredentials configured should not be allowed.");
        }

        private IPackageSearchMetadata GeneratePackageSeachMetadata(Version version)
        {
            Mock<IPackageSearchMetadata> mockPackageSearchMetaData = new Mock<IPackageSearchMetadata>();
            NuGet.Versioning.NuGetVersion nuGetVersion = new NuGet.Versioning.NuGetVersion(version);
            mockPackageSearchMetaData.Setup(foo => foo.Identity).Returns(new NuGet.Packaging.Core.PackageIdentity("generatedPackedId", nuGetVersion));

            return mockPackageSearchMetaData.Object;
        }
    }
}

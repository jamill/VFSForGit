using GVFS.Common;
using GVFS.Common.NuGetUpgrader;
using GVFS.Tests.Should;
using Newtonsoft.Json;
using NUnit.Framework;
using System.Collections.Generic;
using System.IO;

namespace GVFS.UnitTests.Common
{
    [TestFixture]
    public class JsonReleaseManifestTests
    {
        private static int manifestEntryCount = 0;

        [TestCase]
        public void CanReadExpectedJsonString()
        {
            string releaseManifestJsonString =
@"
{
  ""Version"" : ""1"",
  ""PlatformInstallManifests"" : {
    ""Windows"": {
      ""InstallActions"": [
        {
 	    ""Name"" : ""Git"",
        ""Version"" : ""2.19.0.1.34"",
        ""InstallerRelativePath"" : ""Installers\\Windows\\G4W\\Git-2.19.0.gvfs.1.34.gc7fb556-64-bit.exe"",
        ""Args"" : ""/VERYSILENT /CLOSEAPPLICATIONS""
      },
      {
 	    ""Name"" : ""PostGitInstall script"",
 	    ""InstallerRelativePath"" : ""Installers\\Windows\\GSD\\postinstall.ps1""
      },
      ]
    }
  }
}
";
            ReleaseManifest releaseManifest = ReleaseManifest.FromJsonString(releaseManifestJsonString);

            releaseManifest.ShouldNotBeNull();
            InstallManifestPlatform platformInstallManifest = releaseManifest.PlatformInstallManifests[ReleaseManifest.WindowsPlatformKey];
            platformInstallManifest.ShouldNotBeNull();
            platformInstallManifest.InstallActions.Count.ShouldEqual(2);

            this.VerifyInstallActionInfo(
                platformInstallManifest.InstallActions[0],
                "Git",
                "2.19.0.1.34",
                "/VERYSILENT /CLOSEAPPLICATIONS",
                "Installers\\Windows\\G4W\\Git-2.19.0.gvfs.1.34.gc7fb556-64-bit.exe");

            this.VerifyInstallActionInfo(
                platformInstallManifest.InstallActions[1],
                "PostGitInstall script",
                null,
                null,
                "Installers\\Windows\\GSD\\postinstall.ps1");
        }

        [TestCase]
        public void CanDeserializeAndSerializeReleaseManifest()
        {
            List<InstallActionInfo> entries = new List<InstallActionInfo>()
            {
                this.CreateInstallActionInfo(),
                this.CreateInstallActionInfo()
            };

            ReleaseManifest releaseManifest = new ReleaseManifest();
            releaseManifest.AddPlatformInstallManifest(ReleaseManifest.WindowsPlatformKey, entries);

            JsonSerializer serializer = new JsonSerializer();

            using (MemoryStream ms = new MemoryStream())
            using (StreamWriter streamWriter = new StreamWriter(ms))
            using (JsonWriter jsWriter = new JsonTextWriter(streamWriter))
            {
                string output = JsonConvert.SerializeObject(releaseManifest);
                serializer.Serialize(jsWriter, releaseManifest);
                jsWriter.Flush();

                ms.Seek(0, SeekOrigin.Begin);

                StreamReader streamReader = new StreamReader(ms);
                ReleaseManifest deserializedReleaseManifest = ReleaseManifest.FromJson(streamReader);

                this.VerifyReleaseManifestsAreEqual(releaseManifest, deserializedReleaseManifest);
            }
        }

        private InstallActionInfo CreateInstallActionInfo()
        {
            int entrySuffix = manifestEntryCount++;
            return new InstallActionInfo(
                name: $"Installer{entrySuffix}",
                version: $"1.{entrySuffix}.1.2",
                args: $"/nodowngrade{entrySuffix}",
                installerRelativePath: $"installers/installer1{entrySuffix}");
        }

        private void VerifyReleaseManifestsAreEqual(ReleaseManifest expected, ReleaseManifest actual)
        {
            actual.PlatformInstallManifests.Count.ShouldEqual(expected.PlatformInstallManifests.Count, $"The number of platforms ({actual.PlatformInstallManifests.Count}) do not match the expected number of platforms ({expected.PlatformInstallManifests.Count}).");

            foreach (KeyValuePair<string, InstallManifestPlatform> kvp in expected.PlatformInstallManifests)
            {
                this.VerifyPlatformManifestsAreEqual(kvp.Value, actual.PlatformInstallManifests[kvp.Key]);
            }
        }

        private void VerifyInstallActionInfo(
            InstallActionInfo actualEntry,
            string expectedName,
            string expectedVersion,
            string expectedArgs,
            string expectedInstallerRelativePath)
        {
            actualEntry.Name.ShouldEqual(expectedName, "InstallActionInfo name does not match expected value");
            actualEntry.Version.ShouldEqual(expectedVersion, "InstallActionInfo version does not match expected value");
            actualEntry.Args.ShouldEqual(expectedArgs, "InstallActionInfo Args does not match expected value");
            actualEntry.InstallerRelativePath.ShouldEqual(expectedInstallerRelativePath, "InstallActionInfo InstallerRelativePath does not match expected value");
        }

        private void VerifyPlatformManifestsAreEqual(InstallManifestPlatform expected, InstallManifestPlatform actual)
        {
            actual.InstallActions.Count.ShouldEqual(expected.InstallActions.Count, $"The number of platforms ({actual.InstallActions.Count}) do not match the expected number of platforms ({expected.InstallActions.Count}).");

            for (int i = 0; i < actual.InstallActions.Count; i++)
            {
                actual.InstallActions[i].Version.ShouldEqual(expected.InstallActions[i].Version);
                actual.InstallActions[i].Args.ShouldEqual(expected.InstallActions[i].Args);
                actual.InstallActions[i].Name.ShouldEqual(expected.InstallActions[i].Name);
                actual.InstallActions[i].InstallerRelativePath.ShouldEqual(expected.InstallActions[i].InstallerRelativePath);
            }
        }
    }
}

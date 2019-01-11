namespace GVFS.Common.NuGetUpgrader
{
    public class ManifestEntry
    {
        public ManifestEntry(string name, string version, string args, string installerRelativePath)
        {
            this.Name = name;
            this.Version = version;
            this.Args = args;
            this.InstallerRelativePath = installerRelativePath;
        }

        /// <summary>
        /// The arguments that should be passed to the install command
        /// </summary>
        public string Args { get; set; }

        /// <summary>
        /// User friendly name for the install action
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// The relative path to the install script / binary.
        /// The path is relative to the content directory of the NuGet package.
        /// </summary>
        public string InstallerRelativePath { get; set; }

        /// <summary>
        /// The version of the component that this entry installs
        /// </summary>
        public string Version { get; set; }
    }
}

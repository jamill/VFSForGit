[CmdletBinding()]
param (
)

# dot source common functions
. "$PSScriptRoot\Common-Functions.ps1"

$srcDir = "$PSScriptRoot/../../"
$enlistmentDir = "$srcDir/../"
$rootDir = "$srcDir/../"
$buildDir = "$rootDir/BuildOutput"
$publishDir = "$rootDir/Publish"
$packageDir = "$rootDir/packages"

$buildConfig = "Debug"

# Update version
$Vfs4g_version = "0.2.173.2"

# Build ProjFS Kext
$build_projfs = "$srcDir/ProjFS.Mac/Scripts/Build.sh $buildConfig"
Invoke-Expression $build_projfs

# Prebuild tasks
#   - GVFS.Build
#   - Download git for VFS
#   - Generate GVFSConstants.GitVersion.cs
#   - Generating CommonAssemblyVersion.cs

# Restore Packages
dotnet restore $srcDir/GVFS.sln /p:Configuration=$buildConfig.Mac --packages $packageDir /warnasmessage:MSB4011

# Build solution
Write-Host "Building GVFS.sln..."
dotnet build $srcDir/GVFS.sln --runtime osx-x64 --framework netcoreapp2.1 --configuration Debug.Mac /warnasmessage:MSB4011 /maxcpucount:1 -v q
Write-Host "Finished building GVFS.sln"

# Build native project
# Build Notification project
#    - Build tests
# Copy native build artifacts to publish directory
# dotnet publish
# Copy GitInstaller to output directory
# Install shared data queue stall workaround...
# Run GVFS Unit tests


# Quiet mode

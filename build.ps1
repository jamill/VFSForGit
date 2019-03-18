
[CmdletBinding()]
param(
    [switch]$CleanBuildOutput,
    [switch]$CleanPackages,
    [switch]$Build
)

Write-Host ("`r`n" * 3)

$SrcRoot = Split-Path -Path $PSScriptRoot
$EnlistRoot = Split-Path -Path $PSScriptRoot -Parent
$BuildOutput = Join-Path $EnlistRoot BuildOutput
$Packages = Join-Path $EnlistRoot packages

$Config = "Debug"
$GvfsVersion = "0.2.173.2"
$SolutionConfiguration=$Config + ".Windows"

Function Clear-BuildOutput {
    [CmdletBinding()]
    param()
    if (Test-Path $BuildOutput) {
        Write-Host 'Cleaning the BuidOutput folder'
	Write-Host $BuildOutput
        Remove-Item $BuildOutput\* -Recurse -Force
    }
}

Function Clear-Packages {
    [CmdletBinding()]
    param()
    if (Test-Path $Packages) {
        Write-Host 'Cleaning the Packages folder'
	Write-Host $Packages
        Remove-Item $Packages\* -Recurse -Force
    }
}

Function Get-VsWhere {
    [CmdletBinding()]
    param()
    $VsWhereVersion = "2.5.2"
    $VsWhereDir = "vswhere." + $VsWhereVersion 
    $VsWhereExe = [IO.Path]::Combine($Packages, $VsWhereDir, 'tools', 'vswhere.exe')
    $VsWhereExe
}

Function Get-MSBuild {
    [CmdletBinding()]
    param()
    # See https://github.com/Microsoft/vswhere/wiki/Find-MSBuild
    $VsInstallationPath = & $VsWhereExe -latest -prerelease -version "[15.0,16.0)" -products * -requires Microsoft.Component.MSBuild Microsoft.VisualStudio.Workload.ManagedDesktop Microsoft.VisualStudio.Workload.NativeDesktop Microsoft.VisualStudio.Workload.NetCoreTools Microsoft.Component.NetFX.Core.Runtime Microsoft.VisualStudio.Component.Windows10SDK.10240 -property installationPath

    $VsInstallationPath
}

if ($CleanBuildOutput) {
   Clear-BuildOutput
}

if ($CleanPackages) {
    Clear-Packages
}

if ($Build) {
    $VsWhereExe = Get-VsWhere
    $VsInstall = Get-MSBuild
    $MsBuild = $VsInstall + "\MSBuild\15.0\Bin\amd64\msbuild.exe"
    Write-Host $VsInstall
    & $MsBuild $SrcRoot\src\GVFS.sln /p:GVFSVersion=$GvfsVersion /p:Configuration=$SolutionConfiguration /p:Platform=x64
}


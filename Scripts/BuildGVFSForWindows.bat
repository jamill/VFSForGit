@ECHO OFF
SETLOCAL
setlocal enabledelayedexpansion
CALL %~dp0\InitializeEnvironment.bat || EXIT /b 10

IF "%1"=="" (SET "Configuration=Debug") ELSE (SET "Configuration=%1")
IF "%2"=="" (SET "GVFSVersion=0.2.173.2") ELSE (SET "GVFSVersion=%2")

SET SolutionConfiguration=%Configuration%.Windows

SET nuget="%VFS_TOOLSDIR%\nuget.exe"
IF NOT EXIST %nuget% (
  mkdir %nuget%\..
  powershell -ExecutionPolicy Bypass -Command "Invoke-WebRequest 'https://dist.nuget.org/win-x86-commandline/latest/nuget.exe' -OutFile %nuget%"
)

:: Acquire vswhere to find dev15 installations reliably.
SET vswherever=2.6.7
%nuget% install vswhere -Version %vswherever% || exit /b 1
SET vswhere=%VFS_PACKAGESDIR%\vswhere.%vswherever%\tools\vswhere.exe

:: Use vswhere to find the latest VS installation with the msbuild component.
:: See https://github.com/Microsoft/vswhere/wiki/Find-MSBuild
for /f "usebackq tokens=*" %%i in (`%vswhere% -all -prerelease -latest -products * -requires Microsoft.Component.MSBuild Microsoft.VisualStudio.Workload.ManagedDesktop Microsoft.VisualStudio.Workload.NativeDesktop Microsoft.VisualStudio.Workload.NetCoreTools Microsoft.Net.Core.Component.SDK.2.1 -property installationPath`) do (
  set VsInstallDir=%%i
)

:: Assumes default installation location for Windows 10 SDKs
IF NOT EXIST "c:\Program Files (x86)\Windows Kits\10\Include\10.0.10240.0" (
  echo ERROR: Could not find Windows 10 SDK Version 10240
  exit /b 1
)

for /f "usebackq tokens=*" %%i in (`%vswhere% -all -prerelease -latest -products * -requires Microsoft.Component.MSBuild Microsoft.VisualStudio.Workload.ManagedDesktop Microsoft.VisualStudio.Workload.NativeDesktop Microsoft.VisualStudio.Workload.NetCoreTools Microsoft.Net.Core.Component.SDK.2.1 -find MSBuild\**\Bin\amd64\MSBuild.exe`) do (
 set msbuild="%%i"
)

IF NOT DEFINED VsInstallDir (
  echo ERROR: Could not locate a Visual Studio installation with required components.
  echo Refer to Readme.md for a list of the required Visual Studio components.
  exit /b 10
)

IF NOT EXIST %msbuild% (
  echo ERROR: Could not find msbuild
  exit /b 1
)

%msbuild% %VFS_SRCDIR%\GVFS.sln /p:GVFSVersion=%GVFSVersion% /p:Configuration=%SolutionConfiguration% /p:Platform=x64 || exit /b 1

dotnet publish %VFS_SRCDIR%\GVFS\FastFetch\FastFetch.csproj /p:Configuration=%Configuration% /p:Platform=x64 /p:SolutionDir=%VFS_SRCDIR%\ --runtime win-x64 --framework netcoreapp2.1 --self-contained --output %VFS_PUBLISHDIR%\FastFetch || exit /b 1
ENDLOCAL

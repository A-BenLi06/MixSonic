param(
    [Parameter(Mandatory = $true)][string]$OutputPath,
    [string]$VirtualMachineName,
    [string]$CompilerPath = $env:MIXDROME_CSC,
    [ValidateRange(5, 60)][int]$TimeoutSeconds = 30
)

$ErrorActionPreference = 'Stop'
$phoneCaptureProjectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$phoneCaptureOutputPath = [IO.Path]::GetFullPath($OutputPath)
if ([IO.Path]::GetExtension($phoneCaptureOutputPath) -ne '.png') {
    throw 'The emulator capture output must have a .png extension.'
}
if (Test-Path -LiteralPath $phoneCaptureOutputPath) {
    throw 'Choose a new output path; existing capture evidence is not overwritten.'
}
if (-not (Test-Path -LiteralPath ([IO.Path]::GetDirectoryName($phoneCaptureOutputPath)) -PathType Container)) {
    throw 'The capture output directory must already exist.'
}
if (-not $VirtualMachineName) {
    $phoneCaptureCandidates = @(Get-CimInstance Win32_Process -Filter "Name = 'XDE.exe'" |
        ForEach-Object {
            if ($_.CommandLine -match '(?:^|\s)/name\s+"([^"]+)"') { $Matches[1] }
        })
    if ($phoneCaptureCandidates.Count -ne 1) {
        throw 'Supply -VirtualMachineName to select exactly one running Windows Phone emulator.'
    }
    $VirtualMachineName = $phoneCaptureCandidates[0]
}
if (-not $CompilerPath) {
    $CompilerPath = Join-Path ${env:ProgramFiles(x86)} 'MSBuild\14.0\Bin\csc.exe'
}
if (-not (Test-Path -LiteralPath $CompilerPath -PathType Leaf)) {
    throw 'Supply -CompilerPath or MIXDROME_CSC for the .NET Framework C# compiler.'
}
$phoneCaptureGacRoot = Join-Path $env:WINDIR 'Microsoft.NET\assembly\GAC_32'
$phoneCaptureInterface = Join-Path $phoneCaptureGacRoot 'Microsoft.Xde.Interface\v4.0_8.1.0.0__31bf3856ad364e35\Microsoft.Xde.Interface.dll'
$phoneCaptureCommon = Join-Path $phoneCaptureGacRoot 'Microsoft.Xde.Common\v4.0_8.1.0.0__31bf3856ad364e35\Microsoft.Xde.Common.dll'
foreach ($phoneCaptureReference in @($phoneCaptureInterface, $phoneCaptureCommon)) {
    if (-not (Test-Path -LiteralPath $phoneCaptureReference -PathType Leaf)) {
        throw 'The Windows Phone 8.1 emulator automation SDK is required.'
    }
}
$phoneCaptureWorkRoot = Join-Path $phoneCaptureProjectRoot '.tmp\phone-capture'
New-Item -ItemType Directory -Path $phoneCaptureWorkRoot -Force | Out-Null
$phoneCaptureSourcePath = Join-Path $phoneCaptureWorkRoot 'Capture.cs'
$phoneCaptureExecutable = Join-Path $phoneCaptureWorkRoot 'capture.exe'
@'
using System;
using System.IO;
using Microsoft.Xde.Common;
using Microsoft.Xde.Interface;

internal static class Capture
{
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length != 2) throw new ArgumentException("Expected emulator name and PNG output path.");
            if (File.Exists(args[1])) throw new IOException("Capture output already exists.");
            using (IXdeAutomation emulator = AutomationClient.CreateAutomationClient(args[0]))
            {
                if (!emulator.IsShellReady) throw new InvalidOperationException("The emulator shell is not ready.");
                emulator.CaptureImage(args[1]);
            }
            if (!File.Exists(args[1]) || new FileInfo(args[1]).Length == 0)
                throw new IOException("The emulator did not produce a screenshot.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error.Message);
            return 1;
        }
    }
}
'@ | Set-Content -LiteralPath $phoneCaptureSourcePath -Encoding utf8NoBOM
& $CompilerPath /nologo /platform:x86 "/out:$phoneCaptureExecutable" $phoneCaptureSourcePath "/r:$phoneCaptureInterface" "/r:$phoneCaptureCommon" /r:System.ServiceModel.dll
if ($LASTEXITCODE -ne 0) { throw 'Emulator capture SDK compilation failed.' }
$phoneCaptureStartInfo = [Diagnostics.ProcessStartInfo]::new($phoneCaptureExecutable)
$phoneCaptureStartInfo.UseShellExecute = $false
$phoneCaptureStartInfo.CreateNoWindow = $true
$phoneCaptureStartInfo.RedirectStandardError = $true
$phoneCaptureStartInfo.ArgumentList.Add($VirtualMachineName)
$phoneCaptureStartInfo.ArgumentList.Add($phoneCaptureOutputPath)
$phoneCaptureProcess = [Diagnostics.Process]::Start($phoneCaptureStartInfo)
try {
    if (-not $phoneCaptureProcess.WaitForExit($TimeoutSeconds * 1000)) {
        $phoneCaptureProcess.Kill()
        $phoneCaptureProcess.WaitForExit()
        throw "The emulator capture SDK timed out after $TimeoutSeconds seconds."
    }
    $phoneCaptureError = $phoneCaptureProcess.StandardError.ReadToEnd()
    if ($phoneCaptureProcess.ExitCode -ne 0) { throw "Emulator capture failed: $phoneCaptureError" }
} finally {
    $phoneCaptureProcess.Dispose()
}
[pscustomobject]@{
    VirtualMachineName = $VirtualMachineName
    Screenshot = $phoneCaptureOutputPath
    Sha256 = (Get-FileHash -LiteralPath $phoneCaptureOutputPath -Algorithm SHA256).Hash
    CaptureSource = 'Windows Phone 8.1 SDK guest display'
} | ConvertTo-Json

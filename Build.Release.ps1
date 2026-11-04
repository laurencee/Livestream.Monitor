#Requires -Version 7.0
[CmdletBinding()]
param(
    [string]$ArchiveDirectory = (Join-Path $PSScriptRoot 'artifacts'),
    [string]$PublishDirectory = (Join-Path $PSScriptRoot 'artifacts\publish')
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Invoke-NativeCommand([string]$Path, [string[]]$Arguments) {
    & $Path @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$([IO.Path]::GetFileName($Path)) failed with exit code $LASTEXITCODE." }
}

# Copies only the file, leaving personal settings, stream lists and logs in the folder in place.
function Copy-FileToFolder([string]$File, [string]$Folder) {
    [void][IO.Directory]::CreateDirectory($Folder)
    [IO.File]::Copy($File, (Join-Path $Folder ([IO.Path]::GetFileName($File))), $true)
}

$archivePath = [IO.Path]::GetFullPath($ArchiveDirectory, $PSScriptRoot)
$publishPath = [IO.Path]::GetFullPath($PublishDirectory, $PSScriptRoot)
$sevenZip = (Get-Command 7z.exe -CommandType Application -ErrorAction Ignore)?.Source ??
            (Join-Path $env:ProgramFiles '7-Zip\7z.exe')
if (!(Test-Path -LiteralPath $sevenZip -PathType Leaf)) { throw '7-Zip must be installed or 7z.exe available on PATH.' }

if (!('LivestreamReleaseBrotli' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
public static class LivestreamReleaseBrotli
{
    public static void Write(string source, string destination)
    {
        const int Quality = 11;
        const int Window = 24;
        byte[] input = File.ReadAllBytes(source);
        byte[] packed = new byte[BrotliEncoder.GetMaxCompressedLength(input.Length)];
        if (!BrotliEncoder.TryCompress(input, packed, out int written, Quality, Window))
            throw new InvalidDataException("Unable to compress update payload.");

        using (var output = File.Create(destination)) output.Write(packed, 0, written);
        using (var file = File.OpenRead(destination))
        using (var decoder = new BrotliStream(file, CompressionMode.Decompress))
        using (var decoded = new MemoryStream())
        {
            decoder.CopyTo(decoded);
            using (var sha = SHA256.Create())
                if (!Convert.ToBase64String(sha.ComputeHash(input)).Equals(Convert.ToBase64String(sha.ComputeHash(decoded.ToArray()))))
                    throw new InvalidDataException("Brotli update round-trip failed.");
        }
    }
}
'@
}

$name = 'Livestream.Monitor'
$projectName = 'Livestream.Monitor.Direct2D'
$publishedPath = Join-Path $publishPath $projectName
$published = Join-Path $publishedPath "$name.exe"
foreach ($process in @(Get-Process -Name $name -ErrorAction SilentlyContinue)) {
    if ($process.Path -eq $published) { throw "Close $published before releasing a new build." }
}

$stagingPath = Join-Path ([IO.Path]::GetTempPath()) "Livestream.Monitor.Release-$([Guid]::NewGuid().ToString('N'))"
try {
    $assemblies = Join-Path $stagingPath 'assemblies'
    $payloadDirectory = Join-Path $stagingPath 'payload'
    $output = Join-Path $stagingPath $name
    $project = Join-Path $PSScriptRoot "$projectName\$projectName.csproj"
    Invoke-NativeCommand 'dotnet' @(
        'publish', $project, '--configuration', 'Release', '--runtime', 'win-x64', '-p:DebugType=None',
        '-p:PublishSingleFile=false', '-p:UseAppHost=false', '--output', $assemblies
    )

    # The launcher resolves the three managed assemblies from memory. Detect new publish dependencies here.
    $expected = 'ExternalAPIs.dll', 'Livestream.Monitor.Core.dll', "$name.dll", "$name.deps.json", "$name.runtimeconfig.json"
    $files = Get-ChildItem -LiteralPath $assemblies -File -Name -Force
    if ((Compare-Object $expected @($files)) -or (Get-ChildItem -LiteralPath $assemblies -Directory -Force)) {
        throw "Unexpected application publish files in $assemblies."
    }

    Invoke-NativeCommand 'dotnet' @(
        'run', '--configuration', 'Release', '--file', (Join-Path $PSScriptRoot 'tools\BrotliPayload.cs'), '--', $assemblies, $payloadDirectory
    )
    $launcher = Join-Path $PSScriptRoot 'tools\Launcher\Launcher.csproj'
    Invoke-NativeCommand 'dotnet' @(
        'publish', $launcher, '--configuration', 'Release', '--runtime', 'win-x64', '-p:DebugType=None',
        "-p:PayloadDirectory=$payloadDirectory", '--output', $output
    )

    $launcherName = "$name.Launcher.exe"
    $launcherFiles = @(Get-ChildItem -LiteralPath $output -File -Name -Recurse -Force -FollowSymlink)
    if ($launcherFiles.Count -ne 1 -or $launcherFiles[0] -ne $launcherName) {
        throw "$name release must contain only its framework-dependent single executable."
    }

    Rename-Item -LiteralPath (Join-Path $output $launcherName) -NewName "$name.exe"
    $executable = Get-Item -LiteralPath (Join-Path $output "$name.exe")

    # Create a new archive in staging: adding to an existing archive would update it instead of replacing it.
    $version = [Version]$executable.VersionInfo.FileVersion
    $releaseVersion = $version.ToString($version.Revision -eq 0 ? 3 : 4)
    $archiveName = "$name.$releaseVersion.7z"
    $archive = Join-Path $stagingPath $archiveName
    Invoke-NativeCommand $sevenZip @('a', '-mx=9', $archive, $executable.FullName)
    Invoke-NativeCommand $sevenZip @('t', $archive)

    $framework = & dotnet msbuild $launcher '-getProperty:TargetFramework' '-nologo'
    if ($LASTEXITCODE -ne 0 -or $framework -notmatch '^net(\d+)\.') { throw "Unable to determine runtime for $name." }
    $update = Join-Path $stagingPath "$name.$releaseVersion.win-x64.net$($Matches[1]).exe.br"
    [LivestreamReleaseBrotli]::Write($executable.FullName, $update)

    Copy-FileToFolder $update $archivePath
    Copy-FileToFolder $executable.FullName $publishedPath
    Copy-FileToFolder $archive $archivePath
    $bytes = $executable.Length
    Write-Host "Published ${name}: $bytes bytes to $publishedPath"
    Write-Host "Archived $archiveName in $archivePath"
} finally {
    if ([IO.Directory]::Exists($stagingPath)) { [IO.Directory]::Delete($stagingPath, $true) }
}

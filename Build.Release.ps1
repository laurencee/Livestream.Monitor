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
if (![IO.File]::Exists($sevenZip)) { throw '7-Zip must be installed or 7z.exe available on PATH.' }

$name = 'Livestream.Monitor'
$projectName = 'Livestream.Monitor.Direct2D'
$publishedPath = Join-Path $publishPath $projectName
$published = Join-Path $publishedPath "$name.exe"
foreach ($process in @(Get-Process -Name $name -ErrorAction SilentlyContinue)) {
    if ($process.Path -eq $published) { throw "Close $published before releasing a new build." }
}

$stagingPath = Join-Path ([IO.Path]::GetTempPath()) "Livestream.Monitor.Release-$([Guid]::NewGuid().ToString('N'))"
try {
    $output = Join-Path $stagingPath $name
    $project = Join-Path $PSScriptRoot "$projectName\$projectName.csproj"
    Invoke-NativeCommand 'dotnet' @(
        'publish', $project, '--configuration', 'Release', '--runtime', 'win-x64', '-p:DebugType=None',
        '--output', $output
    )

    $executable = Join-Path $output "$name.exe"
    $payload = [IO.Directory]::GetFiles($output, '*', [IO.SearchOption]::AllDirectories)
    if ($payload.Count -ne 1 -or $payload[0] -ne $executable) {
        throw "$name release must contain only its framework-dependent single executable."
    }

    # A new archive in staging, because adding to an existing archive would update it rather than replace it.
    $version = [Version][Diagnostics.FileVersionInfo]::GetVersionInfo($executable).FileVersion
    $archive = Join-Path $stagingPath "$name.$($version.ToString($version.Revision -eq 0 ? 3 : 4)).7z"
    Invoke-NativeCommand $sevenZip @('a', '-mx=9', $archive, $executable)
    Invoke-NativeCommand $sevenZip @('t', $archive)

    Copy-FileToFolder $executable $publishedPath
    Copy-FileToFolder $archive $archivePath
    $bytes = [IO.FileInfo]::new($executable).Length
    Write-Host "Published ${name}: $bytes bytes to $publishedPath"
    Write-Host "Archived $([IO.Path]::GetFileName($archive)) in $archivePath"
} finally {
    if ([IO.Directory]::Exists($stagingPath)) { [IO.Directory]::Delete($stagingPath, $true) }
}

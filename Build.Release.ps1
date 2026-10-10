#Requires -Version 7.0
[CmdletBinding()]
param(
    [string]$ArchiveDirectory = (Join-Path $PSScriptRoot 'artifacts'),
    # Receives each frontend's executable in its own subfolder.
    [string]$PublishDirectory = (Join-Path $PSScriptRoot 'artifacts\publish'),
    [ValidateSet('Both', 'Wpf', 'Direct2D')]
    [string]$Frontend = 'Both'
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

$apps = @(
    @{ Kind = 'Wpf'; Name = 'Livestream.Monitor.WPF' }
    @{ Kind = 'Direct2D'; Name = 'Livestream.Monitor.Direct2D' }
).Where({ $Frontend -in 'Both', $_.Kind })

foreach ($app in $apps) {
    $published = Join-Path $publishPath "$($app.Name)\$($app.Name).exe"
    foreach ($process in @(Get-Process -Name $app.Name -ErrorAction SilentlyContinue)) {
        if ($process.Path -eq $published) { throw "Close $published before releasing a new build." }
    }
}

$stagingPath = Join-Path ([IO.Path]::GetTempPath()) "Livestream.Monitor.Release-$([Guid]::NewGuid().ToString('N'))"
try {
    # Every frontend is prepared in staging before any is released, so a failed step releases nothing. Each publishes
    # as one framework-dependent executable, as its project file sets.
    foreach ($app in $apps) {
        $name = $app.Name
        $output = Join-Path $stagingPath $name
        $project = Join-Path $PSScriptRoot "$name\$name.csproj"
        Invoke-NativeCommand 'dotnet' @(
            'publish', $project, '--configuration', 'Release', '--runtime', 'win-x64', '-p:DebugType=None',
            '--output', $output
        )

        $app.Executable = Join-Path $output "$name.exe"
        $payload = [IO.Directory]::GetFiles($output, '*', [IO.SearchOption]::AllDirectories)
        if ($payload.Count -ne 1 -or $payload[0] -ne $app.Executable) {
            throw "$name release must contain only its framework-dependent single executable."
        }

        # A new archive in staging, because adding to an existing archive would update it rather than replace it.
        $version = [Version][Diagnostics.FileVersionInfo]::GetVersionInfo($app.Executable).FileVersion
        $app.Archive = Join-Path $stagingPath "$name.$($version.ToString($version.Revision -eq 0 ? 3 : 4)).7z"
        Invoke-NativeCommand $sevenZip @('a', '-mx=9', $app.Archive, $app.Executable)
        Invoke-NativeCommand $sevenZip @('t', $app.Archive)
    }

    foreach ($app in $apps) {
        $publishedPath = Join-Path $publishPath $app.Name
        Copy-FileToFolder $app.Executable $publishedPath
        Copy-FileToFolder $app.Archive $archivePath
        $bytes = [IO.FileInfo]::new($app.Executable).Length
        Write-Host "Published $($app.Name): $bytes bytes to $publishedPath"
        Write-Host "Archived $([IO.Path]::GetFileName($app.Archive)) in $archivePath"
    }
} finally {
    if ([IO.Directory]::Exists($stagingPath)) { [IO.Directory]::Delete($stagingPath, $true) }
}

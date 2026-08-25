#Requires -Version 5.1
param(
    [string]$Configuration = "Release",
    [switch]$KeepSymbols,
    [switch]$RepackOnly,
    [switch]$RequireSignature
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$projectDir = Split-Path -Parent $PSScriptRoot
$appProject = Join-Path $projectDir 'KillerPDF.csproj'
$launcherProject = Join-Path $projectDir 'Packaging\KillerLauncher\KillerLauncher.csproj'
$artifactRoot = Join-Path $projectDir "bin\$Configuration\net10.0-windows\portable-package"
$payloadDir = Join-Path $artifactRoot 'payload'
$payloadZip = Join-Path $artifactRoot 'payload.zip'
$launcherOutput = Join-Path $artifactRoot 'launcher'
$publicDir = Join-Path $projectDir "bin\$Configuration\net10.0-windows\publish"
$publicExe = Join-Path $publicDir 'KillerPDF.exe'

if (-not $RepackOnly) {
    if ([IO.Directory]::Exists($artifactRoot)) { [IO.Directory]::Delete($artifactRoot, $true) }
}
if ([IO.Directory]::Exists($launcherOutput)) { [IO.Directory]::Delete($launcherOutput, $true) }
[IO.Directory]::CreateDirectory($artifactRoot) | Out-Null
[IO.Directory]::CreateDirectory($payloadDir) | Out-Null
[IO.Directory]::CreateDirectory($launcherOutput) | Out-Null
[IO.Directory]::CreateDirectory($publicDir) | Out-Null

$versionXml = [xml](Get-Content -Raw -LiteralPath $appProject)
$versionNode = $versionXml.SelectSingleNode('/Project/PropertyGroup/Version')
$version = if ($versionNode) { [string]$versionNode.InnerText } else { '' }
if (-not $version) { throw 'KillerPDF.csproj has no Version.' }
$fileVersionNode = $versionXml.SelectSingleNode('/Project/PropertyGroup/FileVersion')
$fileVersion = if ($fileVersionNode) { [string]$fileVersionNode.InnerText } else { '' }
if (-not $fileVersion) { throw 'KillerPDF.csproj has no FileVersion.' }

if (-not $RepackOnly) {
    Write-Host "==> Building loose KillerPDF payload $version..." -ForegroundColor Cyan
    & dotnet publish $appProject -c $Configuration `
        -r win-x64 `
        --self-contained true `
        -p:KillerPayloadBuild=true `
        -p:PublishDir="$payloadDir\"
    if ($LASTEXITCODE -ne 0) { throw 'Payload build failed.' }

    if (-not $KeepSymbols) {
        foreach ($symbol in [IO.Directory]::GetFiles($payloadDir, '*.pdb', [IO.SearchOption]::AllDirectories)) {
            [IO.File]::Delete($symbol)
        }
    }

    # The PDF file-type icon is a loose installed asset even though the application also embeds it.
    [IO.File]::Copy((Join-Path $projectDir 'Resources\pdf-file.ico'),
                    (Join-Path $payloadDir 'pdf-file.ico'), $true)
} elseif (-not [IO.File]::Exists((Join-Path $payloadDir 'KillerPDF.App.exe'))) {
    throw 'RepackOnly requested but the prepared payload is missing.'
}

$manifestPath = Join-Path $payloadDir 'payload.manifest'
$payloadFiles = @([IO.Directory]::GetFiles($payloadDir, '*', [IO.SearchOption]::AllDirectories) |
    Where-Object { -not [string]::Equals($_, $manifestPath, [StringComparison]::OrdinalIgnoreCase) } |
    Sort-Object { $_.Substring($payloadDir.Length + 1) })
$actualPayloadNames = @($payloadFiles | ForEach-Object {
    $_.Substring($payloadDir.Length + 1).Replace('\', '/')
})
$expectedPayloadNames = @(Get-Content -LiteralPath (Join-Path $PSScriptRoot 'payload-files.txt') |
    Where-Object { $_ -and -not $_.StartsWith('#') } | Sort-Object)
$payloadDifference = @(Compare-Object $expectedPayloadNames $actualPayloadNames)
if ($payloadDifference.Count -gt 0) {
    $details = ($payloadDifference | ForEach-Object { "$($_.SideIndicator) $($_.InputObject)" }) -join [Environment]::NewLine
    throw "Payload file set changed. Review dependencies and update build\payload-files.txt deliberately:`n$details"
}
foreach ($required in 'KillerPDF.App.exe', 'KillerPdf.Engine.dll', 'pdfium.dll', 'System.Text.Json.dll') {
    if ($actualPayloadNames -notcontains $required) {
        throw "Required loose payload file is missing ($required). Costura/Fody may have run accidentally."
    }
}
$manifestLines = foreach ($file in $payloadFiles) {
    $relative = $file.Substring($payloadDir.Length + 1).Replace('\', '/')
    $hash = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash
    $size = ([IO.FileInfo]$file).Length
    "$hash`t$size`t$relative"
}
[IO.File]::WriteAllLines($manifestPath, $manifestLines, [Text.UTF8Encoding]::new($false))

Write-Host "==> Compressing one verified payload..." -ForegroundColor Cyan
Add-Type -AssemblyName System.IO.Compression
if ([IO.File]::Exists($payloadZip)) { [IO.File]::Delete($payloadZip) }
$zipStream = [IO.File]::Open($payloadZip, [IO.FileMode]::CreateNew)
try {
    $archive = [IO.Compression.ZipArchive]::new($zipStream, [IO.Compression.ZipArchiveMode]::Create, $false)
    try {
        $allFiles = @([IO.Directory]::GetFiles($payloadDir, '*', [IO.SearchOption]::AllDirectories) |
            Sort-Object { $_.Substring($payloadDir.Length + 1) })
        foreach ($file in $allFiles) {
            $relative = $file.Substring($payloadDir.Length + 1).Replace('\', '/')
            $entry = $archive.CreateEntry($relative, [IO.Compression.CompressionLevel]::Optimal)
            $entry.LastWriteTime = [DateTimeOffset]::new(2020, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
            $input = [IO.File]::OpenRead($file)
            $output = $entry.Open()
            try { $input.CopyTo($output) }
            finally { $output.Dispose(); $input.Dispose() }
        }
    }
    finally { $archive.Dispose() }
}
finally { $zipStream.Dispose() }

Write-Host "==> Building the public portable launcher..." -ForegroundColor Cyan
& dotnet publish $launcherProject -c $Configuration `
    -p:LauncherAssemblyName=KillerPDF `
    -p:LauncherVersion=$version `
    -p:LauncherFileVersion=$fileVersion `
    -p:LauncherIcon="$(Join-Path $projectDir 'Resources\kp-icon.ico')" `
    -p:PayloadZip="$payloadZip" `
    -p:AllowUnsignedInstall="$(!$RequireSignature)" `
    -p:PublishDir="$launcherOutput\"
if ($LASTEXITCODE -ne 0) { throw 'Launcher build failed.' }

$builtLauncher = Join-Path $launcherOutput 'KillerPDF.exe'
if (-not [IO.File]::Exists($builtLauncher)) { throw "Launcher output is missing: $builtLauncher" }
[IO.File]::Copy($builtLauncher, $publicExe, $true)

$payloadBytes = ([IO.FileInfo]$payloadZip).Length
$publicBytes = ([IO.FileInfo]$publicExe).Length
Write-Host "    Payload files : $($payloadFiles.Count)" -ForegroundColor Green
Write-Host "    Payload zip   : $payloadBytes bytes" -ForegroundColor Green
Write-Host "    Public EXE    : $publicBytes bytes" -ForegroundColor Green
Write-Host "    Output        : $publicExe" -ForegroundColor Green

[pscustomobject]@{
    Version = $version
    PayloadFiles = $payloadFiles.Count
    PayloadZipBytes = $payloadBytes
    PublicExeBytes = $publicBytes
    PublicExe = $publicExe
}

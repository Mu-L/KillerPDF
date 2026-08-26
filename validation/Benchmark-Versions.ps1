[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateScript({ Test-Path -LiteralPath $_ -PathType Leaf })]
    [string] $BaselineExe,

    [Parameter(Mandatory)]
    [ValidateScript({ Test-Path -LiteralPath $_ -PathType Leaf })]
    [string] $CandidateExe,

    [Parameter(Mandatory)]
    [ValidateScript({ Test-Path -LiteralPath $_ -PathType Container })]
    [string] $InputDirectory,

    [Parameter(Mandatory)]
    [string] $OutputDirectory,

    [ValidateRange(1, 25)]
    [int] $Runs = 5,

    [string] $BaselineLabel = 'Baseline',

    [string] $CandidateLabel = 'Candidate'
)

$ErrorActionPreference = 'Stop'
$resolvedOutput = [System.IO.Path]::GetFullPath($OutputDirectory)
$profileRoot = [System.IO.Path]::GetFullPath($env:USERPROFILE)

if (-not $resolvedOutput.StartsWith($profileRoot + [System.IO.Path]::DirectorySeparatorChar,
        [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "OutputDirectory must be inside the current user profile: $profileRoot"
}

if ($resolvedOutput -eq $profileRoot) {
    throw 'OutputDirectory cannot be the user profile root.'
}

New-Item -ItemType Directory -Force -Path $resolvedOutput | Out-Null
$results = [System.Collections.Generic.List[object]]::new()

function Invoke-BenchmarkRun {
    param(
        [Parameter(Mandatory)][string] $Label,
        [Parameter(Mandatory)][string] $Executable,
        [Parameter(Mandatory)][string] $RunName,
        [Parameter(Mandatory)][bool] $Measured
    )

    $runOutput = Join-Path $resolvedOutput "output-$RunName"
    $runLog = Join-Path $resolvedOutput "$RunName.csv"
    $resolvedRunOutput = [System.IO.Path]::GetFullPath($runOutput)
    $requiredPrefix = $resolvedOutput + [System.IO.Path]::DirectorySeparatorChar + 'output-'

    if (-not $resolvedRunOutput.StartsWith($requiredPrefix,
            [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Unsafe benchmark output path: $resolvedRunOutput"
    }

    if (Test-Path -LiteralPath $runOutput) {
        Remove-Item -LiteralPath $runOutput -Recurse -Force
    }
    if (Test-Path -LiteralPath $runLog) {
        Remove-Item -LiteralPath $runLog -Force
    }

    New-Item -ItemType Directory -Path $runOutput | Out-Null
    [System.GC]::Collect()
    [System.GC]::WaitForPendingFinalizers()

    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    $process = Start-Process -FilePath $Executable -ArgumentList @(
        '--batch-resave',
        $InputDirectory,
        $runOutput,
        '--log',
        $runLog
    ) -PassThru -Wait
    $stopwatch.Stop()

    if (-not (Test-Path -LiteralPath $runLog)) {
        throw "$Label did not create its batch-resave log: $runLog"
    }

    $rows = Import-Csv -LiteralPath $runLog
    $saved = @($rows | Where-Object Status -eq 'OK').Count
    $result = [pscustomobject]@{
        Version = $Label
        Run = $RunName
        Measured = $Measured
        Seconds = [math]::Round($stopwatch.Elapsed.TotalSeconds, 3)
        Files = $saved
        FilesPerSecond = [math]::Round($saved / $stopwatch.Elapsed.TotalSeconds, 2)
        ExitCode = $process.ExitCode
    }

    $results.Add($result)
    $result | Format-Table -AutoSize
    Remove-Item -LiteralPath $runOutput -Recurse -Force
}

Invoke-BenchmarkRun -Label $BaselineLabel -Executable $BaselineExe `
    -RunName 'warmup-baseline' -Measured $false
Invoke-BenchmarkRun -Label $CandidateLabel -Executable $CandidateExe `
    -RunName 'warmup-candidate' -Measured $false

for ($run = 1; $run -le $Runs; $run++) {
    if ($run % 2 -eq 1) {
        Invoke-BenchmarkRun -Label $BaselineLabel -Executable $BaselineExe `
            -RunName "run-$run-baseline" -Measured $true
        Invoke-BenchmarkRun -Label $CandidateLabel -Executable $CandidateExe `
            -RunName "run-$run-candidate" -Measured $true
    }
    else {
        Invoke-BenchmarkRun -Label $CandidateLabel -Executable $CandidateExe `
            -RunName "run-$run-candidate" -Measured $true
        Invoke-BenchmarkRun -Label $BaselineLabel -Executable $BaselineExe `
            -RunName "run-$run-baseline" -Measured $true
    }
}

$resultsPath = Join-Path $resolvedOutput 'benchmark-results.csv'
$results | Export-Csv -LiteralPath $resultsPath -NoTypeInformation

$summary = $results |
    Where-Object Measured |
    Group-Object Version |
    ForEach-Object {
        $times = @($_.Group.Seconds | Sort-Object)
        $rates = @($_.Group.FilesPerSecond | Sort-Object)
        $middle = [math]::Floor($times.Count / 2)
        [pscustomobject]@{
            Version = $_.Name
            Runs = $_.Count
            MedianSeconds = $times[$middle]
            MinimumSeconds = $times[0]
            MaximumSeconds = $times[-1]
            MedianFilesPerSecond = $rates[$middle]
        }
    }

$summaryPath = Join-Path $resolvedOutput 'benchmark-summary.csv'
$summary | Export-Csv -LiteralPath $summaryPath -NoTypeInformation

Write-Host "Results: $resultsPath"
Write-Host "Summary: $summaryPath"
$summary | Format-Table -AutoSize

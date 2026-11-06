param (
    [string]$TestResultsDirectory = $(throw "Path to TestResults directory containing *.trx files is required."),
    [string]$WorkingDirectory = $(throw "Path to write retry test runs is required.")
)

# SUMMARY
# The `dotnet test` CLI will not automatically retry failed tests.
# This script inspects dotnet test result files (*.trx).
# Any failed tests will be retried up to a max value.

Write-Host "inputs:"
Write-Host "-TestResultsDirectory: $TestResultsDirectory"
Write-Host "-WorkingDirectory: $WorkingDirectory"
Write-Host ""

if (-not (Test-Path $TestResultsDirectory)) {
    Write-Error -Message "Test Results Directory does not exist." -ErrorAction Stop
}

[int]$maxRetries = 3;
[int]$secondsBetweenRetries = 5;

$logDirectoryRetries = Join-Path -Path $WorkingDirectory -ChildPath "RetryResults";

# TRX contents are treated as untrusted input: only test assemblies inside the working directory
# and identifier-shaped test names are ever passed to `dotnet test`.
$allowedRoot = [System.IO.Path]::GetFullPath($WorkingDirectory).TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
$pathComparison = if ($IsWindows -or $env:OS -eq 'Windows_NT') { [System.StringComparison]::OrdinalIgnoreCase } else { [System.StringComparison]::Ordinal }
$identifierPattern = '^[A-Za-z_][A-Za-z0-9_`+]*(\.[A-Za-z_][A-Za-z0-9_`+]*)*$'

function Get-ValidatedTestAssemblyPath([string]$codeBase) {
    if ([string]::IsNullOrWhiteSpace($codeBase) -or -not [System.IO.Path]::IsPathRooted($codeBase)) {
        return $null
    }

    $fullPath = [System.IO.Path]::GetFullPath($codeBase)
    if (-not $fullPath.StartsWith($allowedRoot, $pathComparison) -or
        -not $fullPath.EndsWith('.dll', [System.StringComparison]::OrdinalIgnoreCase) -or
        -not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
        return $null
    }

    $item = Get-Item -LiteralPath $fullPath -Force
    if ($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) {
        return $null
    }

    return $fullPath
}

$trxFiles = Get-ChildItem -Path $TestResultsDirectory\*.trx -Recurse -Force
Write-Host "TRX files found: "
foreach ($trx in $trxFiles) {
    Write-Host "- $trx"
}
Write-Host ""

[bool]$scriptResult = $true;
$RetrySummary = @();
$FailedAfterRetrySummary = @();

foreach ($trx in $trxFiles) {
    [xml]$testRunXml = Get-Content -Path $trx -ErrorAction Stop
    Write-Host ""
    Write-Host "Parsing TestRun '$trx' Outcome: '$($testRunXml.TestRun.ResultSummary.outcome)' Failed Count: '$($testRunXml.TestRun.ResultSummary.Counters.failed)'";

    if ($testRunXml.TestRun.ResultSummary.outcome -eq "Failed") {
        if (-not (Test-Path $logDirectoryRetries)) {
            New-Item -Path $logDirectoryRetries -ItemType directory -ErrorAction Stop | Out-Null
        }

        $results = $testRunXml.TestRun.Results.UnitTestResult
        $testDefinitions = $testRunXml.TestRun.TestDefinitions.UnitTest;

        foreach ($result in $results) {
            if ($result.outcome -eq "Failed") {
                $definition = $testDefinitions | Where-Object { $_.id -eq $result.testId }
                if ($null -eq $definition) {
                    Write-Error -Message "TEST DEFINITION NOT FOUND" -ErrorAction Stop
                }

                $className = [string]$definition.TestMethod.className
                $testName = [string]$definition.TestMethod.name
                $RetrySummary += "$className.$testName"

                Write-Host ""
                Write-Host "$($definition.TestMethod.codeBase) $className.$testName $($result.outcome)"

                $testAssembly = Get-ValidatedTestAssemblyPath ([string]$definition.TestMethod.codeBase)
                if ($null -eq $testAssembly -or $className -notmatch $identifierPattern -or $testName -notmatch $identifierPattern) {
                    Write-Host "Skipping retry: test definition failed validation (assembly must be an existing .dll under '$allowedRoot'; class and method names must be identifiers)."
                    $FailedAfterRetrySummary += "$className.$testName"
                    $scriptResult = $false
                    continue
                }

                [bool]$retryResult = $false;
                for ($i = 0; $i -lt $maxRetries -and $retryResult -eq $false; $i++) {
                    Start-Sleep -Seconds ([int]$secondsBetweenRetries * $i)

                    $logPath = Join-Path -Path $logDirectoryRetries -ChildPath "$className.$($testName)_$i.trx";
                    $dotnetArgs = @('test', $testAssembly, '--logger', "trx;LogFileName=$logPath", '--filter', "ClassName=$className&Name=$testName")
                    & dotnet @dotnetArgs | Out-Null

                    [xml]$retryXml = Get-Content -LiteralPath $logPath -ErrorAction Stop
                    $retryOutcome = $retryXml.TestRun.ResultSummary.outcome;
                    $retryResult = ($retryOutcome -ne "Failed");
                    Write-Host "Retry #$i Outcome: '$retryOutcome' Passed: $retryResult"
                }

                if ($retryResult -eq $false) {
                    $FailedAfterRetrySummary += "$className.$testName"
                }

                $scriptResult = $scriptResult -band $retryResult;
            }
        }
    }
}

Write-Host ""
Write-Host "========== RETRY SUMMARY =========="
Write-Host ""

Write-Host "The following tests were retried:"
foreach ($line in $RetrySummary) {
    Write-Host "- $line"
}

Write-Host ""

if ($scriptResult) {
    Write-Host "Retry complete. All tests pass."
}
else {
    Write-Host "The following tests failed after retry:"
    foreach ($line in $FailedAfterRetrySummary) {
        Write-Host "- $line";
    }
    Write-Host ""
    Write-Error -Message "Retry failed." -ErrorAction Stop
}

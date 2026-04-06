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

                $RetrySummary += "$($definition.TestMethod.className).$($definition.TestMethod.name)"

                Write-Host ""
                Write-Host "$($definition.TestMethod.codeBase) $($definition.TestMethod.className).$($definition.TestMethod.name) $($result.outcome)"

                [bool]$retryResult = $false;
                for ($i = 0; $i -lt $maxRetries -and $retryResult -eq $false; $i++) {
                    Start-Sleep -Seconds ([int]$secondsBetweenRetries * $i)

                    $logPath = "$logDirectoryRetries/$($definition.TestMethod.className).$($definition.TestMethod.name)_$i.trx";
                    dotnet test $($definition.TestMethod.codeBase) --logger "trx;LogFileName=$logPath" --filter "ClassName=$($definition.TestMethod.className)&Name=$($definition.TestMethod.name)" | Out-Null

                    [xml]$retryXml = Get-Content -Path $logPath -ErrorAction Stop
                    $retryOutcome = $retryXml.TestRun.ResultSummary.outcome;
                    $retryResult = ($retryOutcome -ne "Failed");
                    Write-Host "Retry #$i Outcome: '$retryOutcome' Passed: $retryResult"
                }

                if ($retryResult -eq $false) {
                    $FailedAfterRetrySummary += "$($definition.TestMethod.className).$($definition.TestMethod.name)"
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

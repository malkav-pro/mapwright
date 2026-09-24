[CmdletBinding()]
param(
    [switch]$SelfTest,
    [switch]$Full,
    [switch]$ReportOnly,
    [switch]$VerifyReport,
    [string]$RunId
)

$ErrorActionPreference = 'Stop'
if (@($SelfTest, $Full, $ReportOnly, $VerifyReport).Where({ $_ }).Count -ne 1) {
    throw 'Choose exactly one of -SelfTest, -Full, -ReportOnly or -VerifyReport.'
}
& (Join-Path $PSScriptRoot 'run-phase1-acceptance.ps1') -Phase11 -SelfTest:$SelfTest -Full:$Full -ReportOnly:$ReportOnly -VerifyReport:$VerifyReport -RunId $RunId
exit $LASTEXITCODE

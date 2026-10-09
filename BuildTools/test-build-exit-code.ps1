#!/usr/bin/env pwsh
# Run with pwsh -NoProfile -File BuildTools/test-build-exit-code.ps1.
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$buildScript = (Join-Path $PSScriptRoot '../build.ps1').Replace("'", "''")
$pwsh = (Get-Process -Id $PID).Path

foreach ($expectedExitCode in @(0, 1, 37)) {
    $command = @'
function dotnet {
    $expectedArgs = @('build', 'ILSpy.sln', '-c', 'Release', '-p:Platform=Any CPU', '--no-restore', '-t:Example')
    if (($args | ConvertTo-Json -Compress) -ne ($expectedArgs | ConvertTo-Json -Compress)) {
        throw "Unexpected dotnet arguments: $args"
    }
    $global:LASTEXITCODE = __EXIT_CODE__
}
& '__BUILD_SCRIPT__' -Configuration Release --no-restore '-t:Example'
if ($?) { exit 0 } else { exit $LASTEXITCODE }
'@
    $command = $command.Replace('__EXIT_CODE__', [string]$expectedExitCode).Replace('__BUILD_SCRIPT__', $buildScript)
    $output = & $pwsh -NoProfile -NonInteractive -Command $command 2>&1
    $actualExitCode = $LASTEXITCODE
    if ($actualExitCode -ne $expectedExitCode) {
        throw "Expected exit code $expectedExitCode, got $actualExitCode. $output"
    }
}

Write-Output 'Build exit-code and argument-forwarding checks passed.'
exit 0

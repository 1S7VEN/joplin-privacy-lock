$nativeTestDir = Join-Path $PSScriptRoot 'tests\native-credentials'
$nativeHelper = Join-Path $PSScriptRoot 'dist\LockHost.exe'
$nativeTestProcess = Start-Process -FilePath $nativeHelper -ArgumentList @('--data', ('"' + $nativeTestDir + '"'), '--action', 'self-test') -WindowStyle Hidden -Wait -PassThru
if ($nativeTestProcess.ExitCode -ne 0) { throw 'Native helper self-test failed' }
Get-Content -LiteralPath (Join-Path $nativeTestDir 'self-test-result.txt')

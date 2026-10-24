$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
Push-Location (Split-Path $PSScriptRoot -Parent)
try {
    foreach ($suite in @('MatchTest', 'RegressionTest')) {
        & $compiler /nologo /target:exe "/main:$($suite)Program" /unsafe+ /optimize+ /codepage:65001 `
            "/out:test\$suite.exe" /reference:System.dll /reference:System.Core.dll `
            /reference:System.Drawing.dll /reference:System.Windows.Forms.dll "test\$suite.cs" LongShot.cs
        if ($LASTEXITCODE -ne 0) { throw "$suite compilation failed" }
        & ".\test\$suite.exe"
        if ($LASTEXITCODE -ne 0) { throw "$suite failed" }
    }
} finally { Pop-Location }

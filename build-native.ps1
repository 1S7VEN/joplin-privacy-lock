$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
New-Item -ItemType Directory -Force (Join-Path $PSScriptRoot 'dist') | Out-Null
try {
    $nativeOutput = Join-Path $PSScriptRoot 'dist\LockHost.exe'
    $nativeSources = @('LockHost.cs','LockPolicy.cs','PolicyTests.cs') | ForEach-Object { Join-Path $PSScriptRoot ('src\' + $_) }
    & 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe' /nologo /target:winexe "/out:$nativeOutput" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Security.dll /reference:System.Web.Extensions.dll @nativeSources
    if ($LASTEXITCODE -ne 0) { throw 'Native build failed' }
    & node_modules/.bin/esbuild.cmd src/index-native.js --bundle --platform=node --format=iife --outfile=dist/index.js
    if ($LASTEXITCODE -ne 0) { throw 'Plugin build failed' }
    Copy-Item src/manifest.json dist/manifest.json -Force
    python package_jpl.py
    if ($LASTEXITCODE -ne 0) { throw 'Packaging failed' }
} finally { Pop-Location }

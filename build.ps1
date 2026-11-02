param([string]$GodotPath='')
$ErrorActionPreference='Stop'
$projectRoot=$PSScriptRoot
if([string]::IsNullOrWhiteSpace($GodotPath)) {
    $savedEngine=Join-Path $projectRoot 'godot-path.txt'
    if(Test-Path -LiteralPath $savedEngine) { $GodotPath=(Get-Content -LiteralPath $savedEngine -Raw).Trim() }
}
if([string]::IsNullOrWhiteSpace($GodotPath) -or -not (Test-Path -LiteralPath $GodotPath -PathType Leaf)) {
    $engineCommand=Get-Command godot,Godot_v4.7.1-stable_mono_win64.exe -ErrorAction SilentlyContinue | Select-Object -First 1
    if($engineCommand) { $GodotPath=$engineCommand.Source }
}
if([string]::IsNullOrWhiteSpace($GodotPath) -or -not (Test-Path -LiteralPath $GodotPath -PathType Leaf)) {
    throw 'Godot executable not found. Pass -GodotPath with the full path to Godot 4.7.1.'
}
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
. (Join-Path $projectRoot 'native\composition-references.ps1')
$nativeOutput=Join-Path $projectRoot 'native\bin'
New-Item -ItemType Directory -Path $nativeOutput -Force | Out-Null
$vswhere=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$vs=& $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if(-not $vs) { throw 'Visual C++ x64 build tools are required for the Figma glass effect.' }
$vcvars=Join-Path $vs 'VC\Auxiliary\Build\vcvars64.bat'
$compileScript=Join-Path $nativeOutput 'build-glass.cmd'
@"
@echo off
call "$vcvars" >nul
if errorlevel 1 exit /b 1
cl /nologo /LD /EHsc /O2 /MD ..\GlassEffects.cpp /FoGlassEffects.obj /link /OUT:GlassEffects.dll /IMPLIB:GlassEffects.lib runtimeobject.lib d2d1.lib d3d11.lib
if errorlevel 1 exit /b 1
cl /nologo /LD /EHsc /O2 /MD ..\DesktopShell.cpp /FoDesktopShell.obj /link /OUT:DesktopShell.dll /IMPLIB:DesktopShell.lib ole32.lib oleaut32.lib shell32.lib shlwapi.lib uuid.lib user32.lib oleacc.lib gdi32.lib advapi32.lib
"@ | Set-Content -LiteralPath $compileScript -Encoding ascii
Push-Location -LiteralPath $nativeOutput
try { & cmd.exe /d /c build-glass.cmd } finally { Pop-Location }
if($LASTEXITCODE -ne 0) { throw 'Native glass effects compilation failed' }
& (Join-Path $projectRoot 'native\build-icon.ps1')
$zoneSources=Get-ChildItem -LiteralPath (Join-Path $projectRoot 'native\DesktopZones') -Filter '*.cs' | ForEach-Object { $_.FullName }
$zoneSources+=Get-ChildItem -LiteralPath (Join-Path $projectRoot 'native\DesktopPartitions') -Filter '*.cs' | ForEach-Object { $_.FullName }
& $compiler /nologo /target:winexe /platform:x64 /optimize+ "/win32icon:$projectRoot\assets\udo.ico" /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll @compositionReferences "/out:$nativeOutput\DockBridge.exe" (Join-Path $projectRoot 'native\Branding.cs') (Join-Path $projectRoot 'native\DockBridge.cs') (Join-Path $projectRoot 'native\WindowPreviews.cs') (Join-Path $projectRoot 'native\ComposedGlass.cs') (Join-Path $projectRoot 'native\KeyVisualizer.cs') (Join-Path $projectRoot 'native\KeyStatistics.cs') (Join-Path $projectRoot 'native\TrayApps.cs') @zoneSources
if($LASTEXITCODE -ne 0) { throw 'Native bridge compilation failed' }
& $compiler /nologo /target:winexe /platform:x64 /optimize+ "/win32icon:$projectRoot\assets\udo.ico" /r:System.Windows.Forms.dll /r:System.Drawing.dll "/out:$projectRoot\UDo.exe" (Join-Path $projectRoot 'native\Branding.cs') (Join-Path $projectRoot 'native\Launcher.cs')
if($LASTEXITCODE -ne 0) { throw 'Launcher compilation failed' }
Set-Content -LiteralPath (Join-Path $projectRoot 'godot-path.txt') -Value $GodotPath -Encoding utf8
# Keep existing desktop shortcuts working, with the same updated icon/metadata.
if(Test-Path -LiteralPath (Join-Path $projectRoot 'LumaDock.exe')) {
    Copy-Item -LiteralPath (Join-Path $projectRoot 'UDo.exe') -Destination (Join-Path $projectRoot 'LumaDock.exe') -Force
}
Write-Output "Built: $projectRoot\UDo.exe"

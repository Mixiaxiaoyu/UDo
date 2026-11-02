# Build-time metadata only. Runtime APIs are provided by Windows 10/11.
$sdkWinmd = Get-ChildItem 'C:\Program Files (x86)\Windows Kits\10\UnionMetadata' -Filter Windows.winmd -Recurse | Where-Object { $_.FullName -notmatch '\\Facade\\' } | Sort-Object FullName -Descending | Select-Object -First 1
if (-not $sdkWinmd) { throw 'Windows SDK metadata is needed to build the clipped backdrop.' }
$frameworkDir = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$runtimeFacade = Join-Path $frameworkDir 'System.Runtime.dll'
$compositionReferences = @('/r:' + $sdkWinmd.FullName; '/r:' + (Join-Path $frameworkDir 'System.Runtime.WindowsRuntime.dll'); '/r:' + (Join-Path $frameworkDir 'System.Numerics.Vectors.dll'); '/r:System.Numerics.dll'; '/r:' + $runtimeFacade)

param([string]$UnityEditorPath = 'C:\Program Files\Unity\Hub\Editor\6000.4.10f1\Editor')
$ErrorActionPreference = 'Stop'
$taskRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$taskOutput = Join-Path $taskRoot 'Lumenfall/Temp/presentation-checks'
$taskDotnet = Join-Path $UnityEditorPath 'Data/NetCoreRuntime/dotnet.exe'
$taskCompiler = Join-Path $UnityEditorPath 'Data/DotNetSdkRoslyn/csc.dll'
$taskRuntime = Get-ChildItem (Join-Path $UnityEditorPath 'Data/NetCoreRuntime/shared/Microsoft.NETCore.App') -Directory |
    Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
if ($null -eq $taskRuntime -or !(Test-Path $taskDotnet) -or !(Test-Path $taskCompiler)) { throw 'Unity bundled compiler/runtime is unavailable.' }
New-Item -ItemType Directory -Force -Path $taskOutput | Out-Null
$taskLines = [System.Collections.Generic.List[string]]::new()
@('-nostdlib+','-target:exe','-langversion:9.0','-nologo',('-out:"'+(Join-Path $taskOutput 'WorldPresentationOfflineTests.dll')+'"')) |
    ForEach-Object { $taskLines.Add($_) }
@('System.Private.CoreLib.dll','System.Runtime.dll','System.Console.dll','System.Collections.dll','System.Globalization.dll','netstandard.dll') |
    ForEach-Object { $taskLines.Add('-r:"'+(Join-Path $taskRuntime.FullName $_)+'"') }
$taskLines.Add('"'+(Join-Path $taskRoot 'Lumenfall/Assets/_Project/Systems/DataDriven/WorldPresentationCatalog.cs')+'"')
$taskLines.Add('"'+(Join-Path $PSScriptRoot 'tests/WorldPresentationOfflineTests.cs')+'"')
$taskResponse = Join-Path $taskOutput 'presentation.rsp'
[IO.File]::WriteAllLines($taskResponse,$taskLines,[Text.UTF8Encoding]::new($false))
& $taskDotnet $taskCompiler ('@'+$taskResponse)
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$taskConfig = @{ runtimeOptions = @{ tfm = 'net8.0'; framework = @{ name = 'Microsoft.NETCore.App'; version = $taskRuntime.Name } } } | ConvertTo-Json -Depth 5
[IO.File]::WriteAllText((Join-Path $taskOutput 'WorldPresentationOfflineTests.runtimeconfig.json'),$taskConfig,[Text.UTF8Encoding]::new($false))
& $taskDotnet (Join-Path $taskOutput 'WorldPresentationOfflineTests.dll')
exit $LASTEXITCODE

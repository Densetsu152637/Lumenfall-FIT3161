param(
 [string]$ScopeRoot = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path,
 [string]$UnityEditorPath = 'C:/Program Files/Unity/Hub/Editor/6000.4.10f1/Editor'
)
$ErrorActionPreference='Stop'
$scope=$ScopeRoot
$out=Join-Path $scope 'Lumenfall/Temp/orio-compile'
$editor=$UnityEditorPath
$runtime=Get-ChildItem (Join-Path $editor 'Data/NetCoreRuntime/shared/Microsoft.NETCore.App') -Directory | Sort-Object Name -Descending | Select-Object -First 1 -ExpandProperty FullName
$lines=[System.Collections.Generic.List[string]]::new()
@('-nostdlib+','-target:exe','-langversion:9.0','-nologo',('-out:"'+$out+'/OrioOffline.dll"')) | ForEach-Object {$lines.Add($_)}
foreach($ref in Get-ChildItem $runtime -Filter '*.dll') {
 try { [void][Reflection.AssemblyName]::GetAssemblyName($ref.FullName); $lines.Add('-r:"'+$ref.FullName+'"') } catch {}
}
$core=Join-Path $editor 'Data/Managed/UnityEngine/UnityEngine.CoreModule.dll'
foreach($ref in @($core,"$out/Lumenfall.Runtime.dll","$out/Lumenfall.GameEvents.dll")) {$lines.Add('-r:"'+$ref+'"')}
$lines.Add('"'+(Join-Path $PSScriptRoot 'OrioOffline.cs')+'"')
[IO.File]::WriteAllLines("$out/offline.rsp",$lines,[Text.UTF8Encoding]::new($false))
& "$editor/Data/NetCoreRuntime/dotnet.exe" "$editor/Data/DotNetSdkRoslyn/csc.dll" "@$out/offline.rsp"
if($LASTEXITCODE -ne 0){throw 'Offline harness compile failed'}
Copy-Item -LiteralPath $core -Destination "$out/UnityEngine.CoreModule.dll"
$version=Split-Path $runtime -Leaf
$config=@{runtimeOptions=@{tfm='net8.0';framework=@{name='Microsoft.NETCore.App';version=$version}}} | ConvertTo-Json -Depth 5
[IO.File]::WriteAllText("$out/OrioOffline.runtimeconfig.json",$config)
& "$editor/Data/NetCoreRuntime/dotnet.exe" "$out/OrioOffline.dll" "$scope/Lumenfall/Assets/Resources/GameEvents/quest_npc_dialogue.json"
if($LASTEXITCODE -ne 0){throw 'Offline harness failed'}

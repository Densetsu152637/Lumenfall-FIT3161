param(
    [string]$ReferenceProject = 'D:\Git Repositories\Lumenfall-FIT3161\Lumenfall',
    [string]$UnityEditorPath = 'C:\Program Files\Unity\Hub\Editor\6000.4.10f1\Editor'
)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$project = Join-Path $root 'Lumenfall'
$dag = Join-Path $ReferenceProject 'Library/Bee/artifacts/1900b0aE.dag'
$outDir = Join-Path $project 'Temp/audio-validation'
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$dotnet = Join-Path $UnityEditorPath 'Data/NetCoreRuntime/dotnet.exe'
$csc = Join-Path $UnityEditorPath 'Data/DotNetSdkRoslyn/csc.dll'
$managed = Join-Path $UnityEditorPath 'Data/Managed/UnityEngine'
$references = [System.Collections.Generic.List[string]]::new()
# Imported first-party refs can hide source errors. Use only branch-compiled assemblies.
Get-ChildItem -LiteralPath $dag -File -Filter '*.ref.dll' | Where-Object Name -notmatch '^(Assembly-CSharp|Lumenfall\.)' | ForEach-Object { $references.Add('-r:"' + $_.FullName + '"') }
@('Core','Input','InputLegacy','Animation','IMGUI','Physics','Physics2D','UI','JSONSerialize','Audio','ImageConversion','TextRendering') | ForEach-Object { $references.Add('-r:"' + (Join-Path $managed ('UnityEngine.'+$_+'Module.dll')) + '"') }
$references.Add('-r:"' + (Join-Path $UnityEditorPath 'Data/NetStandard/ref/2.1.0/netstandard.dll') + '"')
$references.Add('-r:"' + (Join-Path $UnityEditorPath 'Data/NetStandard/compat/2.1.0/shims/netfx/mscorlib.dll') + '"')
function Invoke-Compile([string]$Name, [string[]]$Sources, [string[]]$ExtraRefs, [bool]$Exe = $false) {
    $response = [System.Collections.Generic.List[string]]::new()
    @('-noconfig','-nostdlib+','-langversion:9.0','-deterministic','-nologo','-define:UNITY_6000_4_10;UNITY_EDITOR;ENABLE_INPUT_SYSTEM',('-target:' + $(if ($Exe) {'exe'} else {'library'})),('-out:"' + (Join-Path $outDir ($Name+'.dll')) + '"')) | ForEach-Object { $response.Add($_) }
    $references | ForEach-Object { $response.Add($_) }
    $ExtraRefs | ForEach-Object { $response.Add('-r:"' + $_ + '"') }
    $Sources | ForEach-Object { $response.Add('"' + $_ + '"') }
    $responsePath = Join-Path $outDir ($Name+'.rsp')
    [IO.File]::WriteAllLines($responsePath, $response, [Text.UTF8Encoding]::new($false))
    & $dotnet $csc ('@' + $responsePath)
    if ($LASTEXITCODE -ne 0) { throw "$Name compile failed: $LASTEXITCODE" }
    Write-Output "$Name compiled from branch sources."
}
$eventSources = @(Get-ChildItem (Join-Path $project 'Assets/_Project/Systems/GameEvents') -Recurse -File -Filter '*.cs' | Where-Object FullName -notmatch '\\Tests\\|\\Editor\\' | ForEach-Object FullName)
Invoke-Compile 'Lumenfall.GameEvents' $eventSources @()
$eventsDll = Join-Path $outDir 'Lumenfall.GameEvents.dll'
$runtimeSources = @(Get-ChildItem (Join-Path $project 'Assets/_Project/Systems') -Recurse -File -Filter '*.cs' | Where-Object FullName -notmatch '\\GameEvents\\|\\Tests\\|\\Editor\\' | ForEach-Object FullName)
Invoke-Compile 'Lumenfall.Runtime' $runtimeSources @($eventsDll)
$runtimeDll = Join-Path $outDir 'Lumenfall.Runtime.dll'
$nunit = (Get-ChildItem (Join-Path $ReferenceProject 'Library/PackageCache') -Recurse -File -Filter 'nunit.framework.dll' | Where-Object FullName -match 'unity-custom' | Select-Object -First 1).FullName
if (!$nunit) { throw 'Read-only NUnit assembly unavailable.' }
$testSources = @(Get-ChildItem (Join-Path $project 'Assets/_Project/Systems/GameEvents/Tests/EditMode') -File -Filter '*.cs' | ForEach-Object FullName)
Invoke-Compile 'Lumenfall.GameEvents.Tests' $testSources @($eventsDll,$runtimeDll,$nunit)
# Execute deterministic tests only; JsonUtility/PlayerPrefs cases need the Unity engine.
$runner = Join-Path $outDir 'AudioBehaviorRunner.cs'
@"
using System;
using System.Reflection;
using Lumenfall.Tests.GameEvents;
class AudioBehaviorRunner
{
    static int Main()
    {
        int passed = 0;
        var fixture = new AudioFeedbackTests();
        foreach (var method in typeof(AudioFeedbackTests).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        {
            if (method.Name.StartsWith("Unity", StringComparison.Ordinal)) continue;
            try { method.Invoke(fixture, null); Console.WriteLine("PASS " + method.Name); passed++; }
            catch (Exception error) { Console.Error.WriteLine("FAIL " + method.Name + ": " + (error.InnerException ?? error)); return 1; }
        }
        Console.WriteLine(passed + " portable audio behavior tests passed; 2 engine tests compiled but not executed.");
        return 0;
    }
}
"@ | Set-Content -LiteralPath $runner
Invoke-Compile 'AudioBehaviorRunner' @($runner) @($eventsDll,$runtimeDll,$nunit,(Join-Path $outDir 'Lumenfall.GameEvents.Tests.dll')) $true
'{"runtimeOptions":{"tfm":"net8.0","framework":{"name":"Microsoft.NETCore.App","version":"8.0.16"}}}' | Set-Content -LiteralPath (Join-Path $outDir 'AudioBehaviorRunner.runtimeconfig.json')
Copy-Item -LiteralPath $nunit -Destination (Join-Path $outDir 'nunit.framework.dll')
Copy-Item -LiteralPath (Join-Path $managed 'UnityEngine.CoreModule.dll') -Destination $outDir
Copy-Item -LiteralPath (Join-Path $managed 'UnityEngine.AudioModule.dll') -Destination $outDir
& $dotnet (Join-Path $outDir 'AudioBehaviorRunner.dll')
if ($LASTEXITCODE -ne 0) { throw "Portable audio tests failed: $LASTEXITCODE" }

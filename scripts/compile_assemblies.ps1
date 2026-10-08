param(
    [Parameter(Mandatory = $true)][string]$ReferenceProject,
    [string]$UnityEditorPath = 'C:\Program Files\Unity\Hub\Editor\6000.4.10f1\Editor',
    [string]$ProjectPath,
    [switch]$TestsOnly,
    [switch]$PrepareOnly
)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$project = if ($ProjectPath) { (Resolve-Path -LiteralPath $ProjectPath).Path } else { Join-Path $root 'Lumenfall' }
$referenceRoot = (Resolve-Path -LiteralPath $ReferenceProject).Path
$referencePrefix = $referenceRoot.TrimEnd([char[]]'\/') + [IO.Path]::DirectorySeparatorChar
if ($project.Equals($referenceRoot, [StringComparison]::OrdinalIgnoreCase) -or $project.StartsWith($referencePrefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'Current-source project must be outside the read-only ReferenceProject.' }
$dagRoot = Join-Path $referenceRoot 'Library/Bee/artifacts'
$outDir = Join-Path $project 'Temp/codex-compile'
if (!(Test-Path -LiteralPath $dagRoot)) { throw "Read-only reference project has no imported Bee artifacts: $dagRoot" }
if (!$PrepareOnly) {
    $dotnet = Join-Path $UnityEditorPath 'Data/NetCoreRuntime/dotnet.exe'
    $csc = Join-Path $UnityEditorPath 'Data/DotNetSdkRoslyn/csc.dll'
    if (!(Test-Path -LiteralPath $dotnet) -or !(Test-Path -LiteralPath $csc)) { throw "Unity Roslyn compiler not found under $UnityEditorPath" }
}

# Current asmdefs own source membership; imported response files supply dependency references only.
$assemblies = @{}
$guidNames = @{}
foreach ($file in Get-ChildItem (Join-Path $project 'Assets') -Recurse -File -Filter '*.asmdef') {
    $definition = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json
    if ($definition.name -notlike 'Lumenfall.*') { continue }
    if ($definition.defineConstraints.Count -gt 0 -or $definition.excludePlatforms.Count -gt 0 -or @($definition.includePlatforms | Where-Object { $_ -and $_ -ne 'Editor' }).Count -gt 0) {
        throw "Unsupported offline asmdef constraints in $($file.FullName); do not silently skip this assembly."
    }
    $isTest = @($definition.optionalUnityReferences) -contains 'TestAssemblies'
    $assemblies[$definition.name] = @{ File = $file; Definition = $definition; IsTest = $isTest }
    $meta = $file.FullName + '.meta'
    if (Test-Path -LiteralPath $meta) {
        $match = [regex]::Match((Get-Content -LiteralPath $meta -Raw), '(?m)^guid:\s*(\w{32})')
        if ($match.Success) { $guidNames[$match.Groups[1].Value] = $definition.name }
    }
}
if ($assemblies.Count -eq 0) { throw 'No supported first-party asmdefs found.' }
$pending = @($assemblies.Keys | Where-Object { [bool]$assemblies[$_].IsTest -eq [bool]$TestsOnly } | Sort-Object)
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$completed = @{}
while ($pending.Count -gt 0) {
    $progress = $false
    foreach ($name in @($pending)) {
        $assembly = $assemblies[$name]
        $dependencies = @($assembly.Definition.references | ForEach-Object {
            if ($_ -like 'GUID:*' -and $guidNames.ContainsKey($_.Substring(5))) { $guidNames[$_.Substring(5)] } else { $_ }
        } | Where-Object { $_ -and $assemblies.ContainsKey($_) })
        if (@($dependencies | Where-Object { $pending -contains $_ -and !$completed.ContainsKey($_) }).Count -gt 0) { continue }
        $templates = @(Get-ChildItem -LiteralPath $dagRoot -Recurse -File -Filter ($name + '.rsp'))
        if ($templates.Count -ne 1) { throw "Expected exactly one imported reference template for $name; found $($templates.Count). Reimport reference project explicitly outside this script." }
        $lines = [System.Collections.Generic.List[string]]::new()
        $seen = @{}
        foreach ($line in Get-Content -LiteralPath $templates[0].FullName) {
            if ($line -match '^[-/]r:"?(.+?)"?$') {
                $ref = $Matches[1].Replace('\', '/')
                # Never compile against stale first-party imported code, including tests.
                if ([IO.Path]::GetFileName($ref) -match '^(Lumenfall\.|Assembly-CSharp)') { continue }
                if ($ref -match '^Library[/\\]') { $ref = Join-Path $referenceRoot $ref }
                elseif ($ref -match '^.*[/\\]Editor[/\\]Data[/\\](.+)$') { $ref = Join-Path $UnityEditorPath ('Data/' + $Matches[1]) }
                elseif (![IO.Path]::IsPathRooted($ref)) { $ref = Join-Path $referenceRoot $ref }
                $ref = [IO.Path]::GetFullPath($ref)
                if (!(Test-Path -LiteralPath $ref)) { throw "Missing imported dependency for ${name}: $ref" }
                if (!$seen.ContainsKey($ref)) { $lines.Add('-r:"' + $ref + '"'); $seen[$ref] = $true }
            }
            elseif ($line -match '^[-/](define:|langversion:|unsafe|nostdlib|noconfig|nullable:|nowarn:)') { $lines.Add($line) }
        }
        foreach ($dependency in $dependencies) {
            $output = Join-Path $outDir ($dependency + '.dll')
            if (!$PrepareOnly -and !(Test-Path -LiteralPath $output)) { throw "Compile current-source runtime dependency first: $dependency" }
            $lines.Add('-r:"' + $output + '"')
        }
        $lines.Add('-target:library')
        $lines.Add('-nologo')
        $lines.Add('-deterministic')
        $lines.Add('-out:"' + (Join-Path $outDir ($name + '.dll')) + '"')
        $sources = @(Get-ChildItem -LiteralPath $assembly.File.DirectoryName -Recurse -File -Filter '*.cs' | Where-Object {
            # Exclude child asmdefs from parent membership, preserving all sources in this asmdef.
            $directory = $_.Directory
            $belongs = $true
            while ($directory.FullName -ne $assembly.File.DirectoryName) {
                if (@(Get-ChildItem -LiteralPath $directory.FullName -File -Filter '*.asmdef').Count -gt 0) { $belongs = $false; break }
                $directory = $directory.Parent
            }
            $belongs -and ($_.FullName -notmatch '[/\\]Editor[/\\]' -or @($assembly.Definition.includePlatforms) -contains 'Editor')
        } | Sort-Object FullName)
        if ($sources.Count -eq 0) { throw "No current-worktree sources found for $name" }
        foreach ($source in $sources) { $lines.Add('"' + $source.FullName + '"') }
        $rsp = Join-Path $outDir ($name + '.rsp')
        [IO.File]::WriteAllLines($rsp, $lines, (New-Object Text.UTF8Encoding($false)))
        Write-Output "$name : $($sources.Count) current-source files; references from $referenceRoot; output $outDir"
        if (!$PrepareOnly) {
            & $dotnet $csc ('@' + $rsp)
            if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
        }
        $completed[$name] = $true
        $pending = @($pending | Where-Object { $_ -ne $name })
        $progress = $true
    }
    if (!$progress) { throw ('First-party asmdef reference cycle: ' + ($pending -join ', ')) }
}

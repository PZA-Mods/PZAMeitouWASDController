# PZA's WASD Control (Meitou): build the code mod and stage its install folder. Author: PZA. Licence: GPLv3 or later.
#
#   .\build.ps1            build Release, stage out\PZAWASDControl\ (PZAWASDControl.dll, meitou.json, PZAWASDControl.mod)
#   .\build.ps1 -Check     also run tools\check on the staged folder (Meitou 0.20.1's manifest rules, the settings validation, the three patches
#                          applied to the real MeitouClient.dll and the input rule through the patched InputBindings.Resolve), on a copy of
#                          the .NET 11 runtime inside the Meitou release. It never starts the game.
#   .\build.ps1 -Fuzz      also run the property fuzzer (tools\check\Fuzz.cs) on the mod's pure rules: dial aim, squad pages, the prompt
#                          strip's fitting, the safe picks, clear jobs, time under a dial on Meitou's real clock. -FuzzRuns N (default
#                          200000 per target), -FuzzSeed S (default: random, printed; pass it back to repeat a failure), -FuzzSeconds T
#                          (run each target for T seconds instead), -FuzzOnly <target>. Failures shrink and land in tests\fuzz\corpus.
#   -MeitouDir <dir>       the unpacked Meitou release (default: MeitouDir in mod\local.props, git-ignored)
#
# It installs nothing (see ..\README.md "Install"). out\ is git-ignored.
param(
    [switch]$Check,
    [switch]$Fuzz,
    [int]$FuzzRuns = 200000,
    [int]$FuzzSeed = -1,
    [double]$FuzzSeconds = 0,
    [string]$FuzzOnly = "",
    [string]$MeitouDir = ""
)
$ErrorActionPreference = "Stop"
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$localProps = Join-Path $here "local.props"
if ($MeitouDir -eq "" -and (Test-Path $localProps)) { $MeitouDir = ([xml](Get-Content $localProps -Raw)).Project.PropertyGroup.MeitouDir }
if (-not $MeitouDir) { throw "Set -MeitouDir <unpacked Meitou release> or create mod\local.props (see the comment in the csproj)" }
$MeitouDir = [System.IO.Path]::GetFullPath($MeitouDir)
if (-not (Test-Path (Join-Path $MeitouDir "MeitouClient.dll"))) { throw "MeitouClient.dll not found in $MeitouDir" }

& dotnet build (Join-Path $here "PZAWASDControl.csproj") -c Release -nologo "-p:MeitouDir=$MeitouDir"
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed ($LASTEXITCODE)" }

$bin = Get-ChildItem (Join-Path $here "bin\Release") -Directory | Sort-Object LastWriteTime -Descending | Select-Object -First 1
$stage = Join-Path $here "out\PZAWASDControl"
New-Item -ItemType Directory -Force $stage | Out-Null
Copy-Item (Join-Path $bin.FullName "PZAWASDControl.dll") $stage -Force
Copy-Item (Join-Path $here "meitou.json") $stage -Force

# The data mod Meitou lists the folder by (mods\<Name>\<Name>.mod): an FCS mod file with no records, author PZA (same layout as LEAFMeitou's).
function Write-FcsString([System.IO.BinaryWriter]$w, [string]$s) {
    $b = [System.Text.Encoding]::ASCII.GetBytes($s); $w.Write([int]$b.Length); $w.Write($b)
}
$fs = [System.IO.File]::Create((Join-Path $stage "PZAWASDControl.mod"))
$w = New-Object System.IO.BinaryWriter($fs)
try {
    $w.Write([int]16); $w.Write([int]1)
    Write-FcsString $w "PZA"
    Write-FcsString $w "PZA's WASD & Controller Support for Meitou (code mod pza.wasdcontrol): steer the selected character with W A S D; V toggles, X speed, G take control. Holds no records. Source: GPLv3."
    Write-FcsString $w ""; Write-FcsString $w ""
    $w.Write([int]0); $w.Write([int]0)
} finally { $w.Close() }

Write-Host "staged $stage :"
Get-ChildItem $stage | ForEach-Object { Write-Host ("  {0,-22} {1,8} bytes" -f $_.Name, $_.Length) }

if ($Check -or $Fuzz) {
    $rt = Join-Path $here "out\checkrt"
    New-Item -ItemType Directory -Force $rt | Out-Null
    # The runtime and Meitou assemblies the checker needs, copied from the release (nothing in the release is changed).
    $patterns = "System*.dll", "Microsoft.*.dll", "mscorlib.dll", "netstandard.dll", "WindowsBase.dll", "coreclr.dll", "clrjit.dll",
                "clrgc.dll", "hostfxr.dll", "hostpolicy.dll", "Meitou*.dll", "0Harmony.dll", "Bepu*.dll", "DotRecast*.dll", "Silk.NET*.dll", "MeitouClient.deps.json"
    foreach ($p in $patterns) { Copy-Item (Join-Path $MeitouDir $p) $rt -Force }
    $stagedDll = Join-Path $stage "PZAWASDControl.dll"
    & dotnet build (Join-Path $here "tools\check\check.csproj") -c Release -nologo "-p:MeitouDir=$MeitouDir" "-p:DcModDll=$stagedDll" -o (Join-Path $here "out\checkbin")
    if ($LASTEXITCODE -ne 0) { throw "check tool build failed ($LASTEXITCODE)" }
    Copy-Item (Join-Path $here "out\checkbin\check.dll") $rt -Force
    Copy-Item $stagedDll $rt -Force
    $cfg = '{ "runtimeOptions": { "tfm": "net11.0", "includedFrameworks": [ { "name": "Microsoft.NETCore.App", "version": "11.0.0" } ] } }'
    Set-Content -Path (Join-Path $rt "check.runtimeconfig.json") -Value $cfg -Encoding ascii
    Push-Location $rt
    try
    {
        if ($Check)
        {
            & dotnet exec --runtimeconfig check.runtimeconfig.json check.dll $stage "C:\Program Files (x86)\Steam\steamapps\common\Kenshi"
            if ($LASTEXITCODE -ne 0) { throw "check FAILED ($LASTEXITCODE)" }
        }
        if ($Fuzz)
        {
            $slowmo = [System.IO.Path]::GetFullPath((Join-Path $here "..\..\PZAMeitouSlowMo\mod\out\PZASlowMo\PZASlowMo.Meitou.dll"))
            $env:PZA_FUZZ_SLOWMO = if (Test-Path $slowmo) { $slowmo } else { "" }
            if ($FuzzSeed -lt 0) { $FuzzSeed = Get-Random -Maximum 2147483647 }
            $corpus = [System.IO.Path]::GetFullPath((Join-Path $here "..\tests\fuzz\corpus"))
            $fuzzArgs = @("fuzz", $FuzzRuns, $FuzzSeed, $FuzzSeconds.ToString([Globalization.CultureInfo]::InvariantCulture), $corpus)
            if ($FuzzOnly) { $fuzzArgs += $FuzzOnly }
            & dotnet exec --runtimeconfig check.runtimeconfig.json check.dll @fuzzArgs
            if ($LASTEXITCODE -ne 0) { throw "fuzz FAILED ($LASTEXITCODE), seed $FuzzSeed" }
        }
    }
    finally { Pop-Location }
}

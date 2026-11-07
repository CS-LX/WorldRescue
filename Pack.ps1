param([ValidateSet('Release','Debug')][string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
dotnet build "$PSScriptRoot/Mod/WorldRescue.csproj" -c $Configuration --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
$outputDir = Join-Path $PSScriptRoot 'dist'
New-Item -ItemType Directory -Force -Path $outputDir | Out-Null
$version = (Get-Content -Raw -LiteralPath "$PSScriptRoot/Mod/modinfo.json" | ConvertFrom-Json).Version
$package = Join-Path $outputDir "WorldRescue-$version.scmod"
$buildDir = Join-Path $PSScriptRoot "Mod/bin/$Configuration/net10.0"
Add-Type -AssemblyName System.IO.Compression
$stream = [IO.File]::Open($package, [IO.FileMode]::Create)
try {
    $zip = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create)
    try {
        # Explicit allow-list: never bundle host libraries, build caches or other mods.
        foreach ($name in @('WorldRescue.dll','WorldRescue.Core.dll','modinfo.json','icon.png')) {
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, (Join-Path $buildDir $name), $name) | Out-Null
        }
        $modRoot = Join-Path $PSScriptRoot 'Mod'
        foreach ($file in Get-ChildItem -LiteralPath @((Join-Path $modRoot 'Assets'), (Join-Path $modRoot 'ThirdParty')) -File -Recurse) {
            $entry = [IO.Path]::GetRelativePath($modRoot, $file.FullName).Replace('\','/')
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $file.FullName, $entry) | Out-Null
        }
    } finally { $zip.Dispose() }
} finally { $stream.Dispose() }
Write-Output "Built: $package"

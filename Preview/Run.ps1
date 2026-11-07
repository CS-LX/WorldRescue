param(
    [Parameter(Mandatory=$true)][string]$HostBin,
    [string]$Output = "$PSScriptRoot/../.cache/preview",
    [string]$WorldArchive,
    [switch]$IconOnly
)
$ErrorActionPreference = 'Stop'
dotnet build "$PSScriptRoot/WorldRescue.Preview.csproj" -c Test --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw 'Preview build failed.' }
$previewBin = Join-Path $PSScriptRoot 'bin/Test/net10.0'
foreach ($name in @('Content.zip','glfw3.dll','openal32.dll','wrap_oal.dll','openxr_loader.dll')) {
    $source = Join-Path $HostBin $name
    if (Test-Path -LiteralPath $source) { Copy-Item -LiteralPath $source -Destination (Join-Path $previewBin $name) }
}
$previewOutput = [IO.Path]::GetFullPath($Output)
New-Item -ItemType Directory -Force -Path $previewOutput | Out-Null
$arguments = '"{0}" "{1}"' -f [IO.Path]::GetFullPath("$PSScriptRoot/../Mod"), $previewOutput
if ($IconOnly) { $arguments += ' --icon-only' }
elseif ($WorldArchive) { $arguments += ' "{0}"' -f [IO.Path]::GetFullPath($WorldArchive) }
$process = Start-Process -FilePath (Join-Path $previewBin 'WorldRescue.Preview.exe') -ArgumentList $arguments -WorkingDirectory $previewBin -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $previewOutput 'run.log') -RedirectStandardError (Join-Path $previewOutput 'errors.log')
if (!$process.WaitForExit(45000)) { throw "Preview timed out. Process: $($process.Id)" }
Get-Content (Join-Path $previewOutput 'run.log')
Get-Content (Join-Path $previewOutput 'errors.log')
if ($process.ExitCode -ne 0) { throw 'Preview failed.' }

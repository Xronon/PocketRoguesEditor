# Builds the release archive into dist\:
#   PocketRoguesEditor-<version>.zip   one folder PocketRoguesEditor\ with the program, both
#                                      manuals (English and Russian) and the license
#
#   powershell -ExecutionPolicy Bypass -File package.ps1
#
# Run build.cmd first (and build-test.cmd + bin-test\SelfTest.exe to be sure). Only these files
# are packed: nothing the program creates while working (backups, edit log, settings) can get
# into the archive, even if bin\ has them.
# ASCII only: Windows PowerShell 5.1 reads scripts without a BOM in the system code page.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$exe = Join-Path $here 'bin\PocketRoguesEditor.exe'
if (-not (Test-Path -LiteralPath $exe)) { throw 'bin\PocketRoguesEditor.exe not found - run build.cmd first' }

# version - the one the program shows in its title (Program.Version)
$src = [System.IO.File]::ReadAllText((Join-Path $here 'src\Program.cs'))
$m = [regex]::Match($src, 'public const string Version = "([^"]+)"')
if (-not $m.Success) { throw 'version not found in src\Program.cs' }
$version = $m.Groups[1].Value

# the manuals are the .txt files in the repository root (English and Russian)
$manuals = @(Get-ChildItem -LiteralPath $here -Filter '*.txt' -File)
if ($manuals.Count -ne 2) { throw "expected 2 manuals (*.txt) in the repository root, found $($manuals.Count)" }

$dist = Join-Path $here 'dist'
if (-not (Test-Path -LiteralPath $dist)) { New-Item -ItemType Directory -Path $dist | Out-Null }
$out = Join-Path $dist "PocketRoguesEditor-$version.zip"
if (Test-Path -LiteralPath $out) { Remove-Item -LiteralPath $out -Force }

$level = [System.IO.Compression.CompressionLevel]::Optimal
$zip = [System.IO.Compression.ZipFile]::Open($out, [System.IO.Compression.ZipArchiveMode]::Create)
try {
    [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $exe, 'PocketRoguesEditor/PocketRoguesEditor.exe', $level) | Out-Null
    foreach ($f in $manuals) {
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $f.FullName, 'PocketRoguesEditor/' + $f.Name, $level) | Out-Null
    }
    [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, (Join-Path $here 'LICENSE'), 'PocketRoguesEditor/LICENSE.txt', $level) | Out-Null
} finally { $zip.Dispose() }
Write-Host "[OK] $out"

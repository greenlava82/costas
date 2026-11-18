# Fetches jt9 (WSJT-X) and rigctld (Hamlib) for Windows into third_party/.
# Requires 7-Zip (7z.exe) on PATH to unpack the WSJT-X NSIS installer.
#
# Every download is checked against a pinned SHA-256 before it is unpacked, because what it fetches ships inside the
# signed installer. A version with no pinned hash, or a file that does not match, stops the script. To move to a new
# version, download it, check it against what upstream publishes, and add its hash to $PinnedSha256 below (or pass
# -WsjtxSha256 / -HamlibSha256 to try one out).
param(
    # 3.0.1 is the newest Windows build on SourceForge (no 3.0.2 build is published there, checked 2026-10-04).
    [string]$WsjtxVersion = '3.0.1',
    [string]$HamlibVersion = '4.6.5',
    [string]$WsjtxSha256 = '',
    [string]$HamlibSha256 = ''
)
$ErrorActionPreference = 'Stop'

# File name -> SHA-256 (lower-case hex). How each was obtained:
#   wsjtx-3.0.1-win64.exe   computed from a download of the SourceForge URL below on 2026-11-18 (52,994,240 bytes);
#                           its MD5 (ee8f9ae3393226861779df37eaaa2126) matches the one SourceForge publishes for the file.
#   hamlib-w64-4.6.5.zip    computed from a download of the GitHub release asset below on 2026-11-18 (3,146,339 bytes).
$PinnedSha256 = @{
    'wsjtx-3.0.1-win64.exe' = '4e3c5006bf919553f79784c7a512c0a9706e78937f0b9ab037078d73d951dc0b'
    'hamlib-w64-4.6.5.zip'  = '2a7bfae280ec8aef1d42558b73eaef7f5f0d1dfa9fd8463c15adde5fdec63851'
}

# The hash $Name must have: $Override if given, else the pinned one. Called before downloading, so a version with no
# pinned hash fails straight away.
function Get-ExpectedSha256([string]$Name, [string]$Override) {
    $expected = if ($Override) { $Override } else { $PinnedSha256[$Name] }
    if ($expected -notmatch '^[0-9a-fA-F]{64}$') {
        throw "SHA-256 for $Name is not pinned in third_party/fetch.ps1. Download it, check it against what upstream publishes, and add it to `$PinnedSha256."
    }
    $expected
}

# Checks $Path against $Expected. A mismatch deletes the file, so the next run downloads it again instead of reusing a
# bad copy from the cache.
function Assert-Sha256([string]$Path, [string]$Expected) {
    $name = Split-Path $Path -Leaf
    $actual = (Get-FileHash -Algorithm SHA256 -Path $Path).Hash.ToLowerInvariant()
    if ($actual -ne $Expected.ToLowerInvariant()) {
        Remove-Item $Path -Force
        throw "SHA-256 mismatch for $name`: expected $Expected, got $actual. The file was deleted; do not ship it."
    }
    Write-Host "SHA-256 OK: $name"
}

$root = $PSScriptRoot
$tmp = Join-Path $root '.download'
New-Item -ItemType Directory -Force -Path $tmp | Out-Null

$wsjtxExe = Join-Path $tmp "wsjtx-$WsjtxVersion-win64.exe"
$wsjtxSha = Get-ExpectedSha256 (Split-Path $wsjtxExe -Leaf) $WsjtxSha256
if (-not (Test-Path $wsjtxExe)) {
    $url = "https://sourceforge.net/projects/wsjt/files/wsjtx-$WsjtxVersion/wsjtx-$WsjtxVersion-win64.exe/download"
    Write-Host "Downloading $url"
    Invoke-WebRequest -Uri $url -OutFile $wsjtxExe -UserAgent 'Wget'
}
# Checked every run, including a copy restored from the CI cache.
Assert-Sha256 $wsjtxExe $wsjtxSha
$wsjtxDir = Join-Path $root 'wsjtx'
Remove-Item -Recurse -Force $wsjtxDir -ErrorAction SilentlyContinue
& 7z x $wsjtxExe "-o$wsjtxDir" -y | Out-Null
if ($LASTEXITCODE -ne 0) { throw '7z failed to unpack the WSJT-X installer' }
if (-not (Test-Path (Join-Path $wsjtxDir 'bin/jt9.exe'))) { throw 'jt9.exe not found after unpacking' }

$hamZip = Join-Path $tmp "hamlib-w64-$HamlibVersion.zip"
$hamSha = Get-ExpectedSha256 (Split-Path $hamZip -Leaf) $HamlibSha256
if (-not (Test-Path $hamZip)) {
    $url = "https://github.com/Hamlib/Hamlib/releases/download/$HamlibVersion/hamlib-w64-$HamlibVersion.zip"
    Write-Host "Downloading $url"
    Invoke-WebRequest -Uri $url -OutFile $hamZip
}
Assert-Sha256 $hamZip $hamSha
$hamDir = Join-Path $root 'hamlib'
Remove-Item -Recurse -Force $hamDir -ErrorAction SilentlyContinue
Expand-Archive -Path $hamZip -DestinationPath $tmp -Force
Move-Item (Join-Path $tmp "hamlib-w64-$HamlibVersion") $hamDir

# cty.dat changes every few weeks, so it cannot be pinned. It is not shipped from here either: the app embeds the copy
# committed in src/Ft8Client.Data/Resources (and checks any newer download with CountryFile.Parse). This copy is only
# checked for being a country file rather than an error page.
$cty = Join-Path $tmp 'cty.dat'
Invoke-WebRequest -Uri 'https://www.country-files.com/cty/cty.dat' -OutFile $cty
$ctyText = Get-Content -Raw $cty
$entities = ([regex]::Matches($ctyText, '(?m)^[^\s:][^:]*:\s*\d+:\s*\d+:\s*[A-Z]{2}:')).Count
if ((Get-Item $cty).Length -lt 20KB -or (Get-Item $cty).Length -gt 5MB -or $ctyText -match '<' -or $entities -lt 300) {
    Remove-Item $cty
    throw "cty.dat download is not a country file ($entities entities)"
}
Move-Item $cty (Join-Path $root 'cty.dat') -Force
Write-Host "Done. jt9: $wsjtxDir\bin\jt9.exe  rigctld: $hamDir\bin\rigctld.exe"

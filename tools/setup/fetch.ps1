<#
.SYNOPSIS
    Downloads the pinned files listed in a manifest, verifies their hashes and extracts archives.

.EXAMPLE
    .\tools\setup\fetch.ps1 -Manifest tools\setup\phase0-downloads.json
    .\tools\setup\fetch.ps1 -Manifest tools\setup\phase0-downloads.json -Only 'Qwen3.5-4B'
#>
param(
    [Parameter(Mandatory)] [string] $Manifest,
    [string] $DataRoot = 'E:\AldrenData',
    [string[]] $Only = @()
)

$ErrorActionPreference = 'Stop'

function Test-Hash([string] $Path, $Entry) {
    if (-not (Test-Path $Path)) { return $false }
    if ($Entry.sha512) { return (Get-FileHash $Path -Algorithm SHA512).Hash -eq $Entry.sha512.ToUpper() }
    return (Get-FileHash $Path -Algorithm SHA256).Hash -eq $Entry.sha256.ToUpper()
}

$entries = Get-Content $Manifest -Raw | ConvertFrom-Json
if ($Only.Count -gt 0) {
    $entries = $entries | Where-Object { $name = $_.name; $Only | Where-Object { $name -like "*$_*" } }
}

foreach ($e in $entries) {
    $dest = Join-Path $DataRoot $e.dest
    New-Item -ItemType Directory -Force (Split-Path $dest) | Out-Null

    if (Test-Hash $dest $e) {
        Write-Output "cached    $($e.name)"
    }
    else {
        foreach ($attempt in 1..2) {
            # -C - resumes a partial file; a corrupt one fails the hash check and is fetched again.
            curl.exe -L --fail --silent --show-error --retry 3 -C - -o $dest $e.url
            if ($LASTEXITCODE -eq 0 -and (Test-Hash $dest $e)) { break }
            Remove-Item $dest -ErrorAction SilentlyContinue
            if ($attempt -eq 2) { throw "Download or hash check failed: $($e.name)" }
        }
        Write-Output "verified  $($e.name)"
    }

    if ($e.extractTo) {
        $target = Join-Path $DataRoot $e.extractTo
        New-Item -ItemType Directory -Force $target | Out-Null
        if ($dest -like '*.tar.bz2') {
            tar.exe -xjf $dest -C $target
            if ($LASTEXITCODE -ne 0) { throw "Extraction failed: $($e.name)" }
        }
        else {
            Expand-Archive -Path $dest -DestinationPath $target -Force
        }
        Write-Output "extracted $($e.name) -> $target"
    }
}

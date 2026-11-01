<#
.SYNOPSIS
    Tests for check-repo.ps1, run in a throwaway git repository under %TEMP%.
    Exit code 1 means a test failed.
#>
$ErrorActionPreference = 'Stop'
$script = Join-Path $PSScriptRoot 'check-repo.ps1'
$failures = 0

function New-TestRepo {
    $dir = Join-Path ([IO.Path]::GetTempPath()) ("check-repo-" + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory $dir | Out-Null
    git -C $dir init -q
    $dir
}

function Test-Case([string] $name, [int] $expected, [scriptblock] $stage) {
    $repo = New-TestRepo
    try {
        & $stage $repo
        & powershell -NoProfile -ExecutionPolicy Bypass -File $script -RepoRoot $repo | Out-Null
        $actual = $LASTEXITCODE
        if ($actual -eq $expected) { Write-Output "PASS $name" }
        else { Write-Output "FAIL $name (exit $actual, expected $expected)"; $script:failures++ }
    }
    finally { Remove-Item $repo -Recurse -Force }
}

Test-Case 'Ordinary_text_file_passes' 0 {
    param($r) Set-Content (Join-Path $r 'notes.md') 'hello'; git -C $r add notes.md
}
Test-Case 'Png_with_a_non_ascii_name_is_blocked' 1 {
    param($r) $f = Join-Path $r "Capture d'$([char]0xE9)cran.png"; [IO.File]::WriteAllBytes($f, [byte[]](1, 2, 3)); git -C $r add -A
}
Test-Case 'Npz_model_is_blocked' 1 {
    param($r) [IO.File]::WriteAllBytes((Join-Path $r 'aldren-wakeword.npz'), [byte[]](1, 2, 3)); git -C $r add -A
}
Test-Case 'Staged_size_counts_not_the_working_copy' 1 {
    param($r)
    $f = Join-Path $r 'big.txt'
    [IO.File]::WriteAllBytes($f, (New-Object byte[] (6MB)))
    git -C $r add big.txt
    Set-Content $f 'small now'
}
Test-Case 'Large_file_with_a_non_ascii_name_is_blocked' 1 {
    param($r) [IO.File]::WriteAllBytes((Join-Path $r "donn$([char]0xE9)es.txt"), (New-Object byte[] (6MB))); git -C $r add -A
}

if ($failures) { Write-Output "$failures failed"; exit 1 }
Write-Output 'All passed'

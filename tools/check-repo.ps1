<#
.SYNOPSIS
    Refuses a commit when staged files include personal data, models, databases or very large files.
    Run it before every commit; exit code 1 means "do not commit".
#>
param([string] $RepoRoot = (git rev-parse --show-toplevel))

$blockedTypes = '\.(wav|flac|mp3|png|jpg|jpeg|gguf|onnx|npy|db)$'
$maxBytes = 5MB

$staged = git -C $RepoRoot diff --cached --name-only --diff-filter=ACMR
$problems = foreach ($file in $staged) {
    $path = Join-Path $RepoRoot $file
    if ($file -match $blockedTypes) {
        "$file (blocked file type)"
    }
    elseif ((Test-Path $path) -and (Get-Item $path).Length -gt $maxBytes) {
        "$file (larger than 5 MB)"
    }
}

if ($problems) {
    Write-Output 'Refusing to commit:'
    $problems | ForEach-Object { Write-Output "  $_" }
    exit 1
}

Write-Output 'check-repo: OK'
exit 0

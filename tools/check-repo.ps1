<#
.SYNOPSIS
    Refuses a commit when staged files include personal data, models, databases or very large files.
    Run it before every commit; exit code 1 means "do not commit".
#>
param([string] $RepoRoot = (git rev-parse --show-toplevel))

$blockedTypes = '\.(wav|flac|mp3|m4a|ogg|png|jpg|jpeg|webp|bmp|gguf|onnx|npy|npz|db)$'
$maxBytes = 5MB

# Unquoted, NUL-separated UTF-8 names: with git's default quoting, a name like "Capture d'écran.png"
# arrives wrapped in quotes and escaped, and slips past the type match.
$previousEncoding = [Console]::OutputEncoding
[Console]::OutputEncoding = [Text.Encoding]::UTF8
try {
    $raw = git -C $RepoRoot -c core.quotePath=false diff --cached --name-only -z --diff-filter=ACMR | Out-String
    $staged = $raw.Split([char]0, [StringSplitOptions]::RemoveEmptyEntries) | ForEach-Object { $_.Trim("`r", "`n") } | Where-Object { $_ }
    $problems = foreach ($file in $staged) {
        if ($file -match $blockedTypes) {
            "$file (blocked file type)"
        }
        # The staged blob's size, not the working copy's: they can differ.
        elseif ([long](git -C $RepoRoot cat-file -s ":$file") -gt $maxBytes) {
            "$file (larger than 5 MB)"
        }
    }
}
finally { [Console]::OutputEncoding = $previousEncoding }

if ($problems) {
    Write-Output 'Refusing to commit:'
    $problems | ForEach-Object { Write-Output "  $_" }
    exit 1
}

Write-Output 'check-repo: OK'
exit 0

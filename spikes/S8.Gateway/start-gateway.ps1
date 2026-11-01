# Starts the FreeLLMAPI gateway for the S8 evaluation in the background, listening on 127.0.0.1 only; stop-gateway.ps1 stops it.
# The encryption key is generated once and kept DPAPI-protected for this Windows account in E:\AldrenData\gateway\key.dpapi. It is
# decrypted only to start the gateway and is never written to a file or a user variable.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Security

$root = 'E:\AldrenData\gateway'
$app = Join-Path $root 'freellmapi-0.16.14'
$keyFile = Join-Path $root 'key.dpapi'
$pidFile = Join-Path $root 'gateway.pid'
$portFile = Join-Path $root 'port.txt'
$log = Join-Path $root 'logs\gateway.log'
$node = (Get-Command node -ErrorAction Stop).Source
if (-not (Test-Path (Join-Path $app 'server\dist\index.js'))) { throw "Build the gateway first: npm ci, then npm run build, in $app" }

if ((Test-Path $pidFile) -and (Get-Process -Id (Get-Content $pidFile) -ErrorAction SilentlyContinue)) {
    Write-Host "The gateway is already running: http://127.0.0.1:$(Get-Content $portFile)"
    return
}

if (-not (Test-Path $keyFile)) {
    $bytes = New-Object byte[] 32
    [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
    $hex = -join ($bytes | ForEach-Object { $_.ToString('x2') })
    $sealed = [Security.Cryptography.ProtectedData]::Protect([Text.Encoding]::ASCII.GetBytes($hex), $null, 'CurrentUser')
    [IO.File]::WriteAllBytes($keyFile, $sealed)
    Write-Host "Generated the gateway's encryption key (DPAPI-protected: $keyFile)"
}

$probe = New-Object Net.Sockets.TcpListener([Net.IPAddress]::Loopback, 0)
$probe.Start(); $port = $probe.LocalEndpoint.Port; $probe.Stop()
$db = Join-Path $env:LOCALAPPDATA 'Aldren\gateway\freeapi.db'
New-Item -ItemType Directory -Force (Split-Path $db), (Split-Path $log) | Out-Null

$settings = [ordered]@{
    NODE_ENV                         = 'production'   # as in the project's Docker image: migrations run, error details stay hidden
    HOST                             = '127.0.0.1'    # the default is '::', every interface
    PORT                             = "$port"
    ALLOWED_HOSTS                    = 'localhost'
    FALLBACK_TIME_BUDGET_MS          = '8000'
    PROVIDER_STREAM_STALL_TIMEOUT_MS = '15000'
    REQUEST_ANALYTICS_LOG_CLIENT     = 'false'
    FREELLMAPI_UPDATE_CHECK          = 'off'
    FREEAPI_DB_PATH                  = $db
    FREEAPI_DB_DIR_HARDENING         = '1'            # the folder is this account's alone, so lock it and the SQLite sidecars down
    FREEAPI_ENV_PATH                 = (Join-Path $root 'no-such.env') # never pick up a stray .env from the checkout
    ENCRYPTION_KEY                   = [Text.Encoding]::ASCII.GetString(
        [Security.Cryptography.ProtectedData]::Unprotect([IO.File]::ReadAllBytes($keyFile), $null, 'CurrentUser'))
}

# Start-Process without redirection goes through ShellExecute, which hands the gateway no handles of this process (a plain
# Process.Start would pass on the caller's console or output pipe and keep it open). The child takes its environment from this
# process, so the settings are set only for the launch; cmd just appends the gateway's output to the log.
try {
    foreach ($name in $settings.Keys) { [Environment]::SetEnvironmentVariable($name, $settings[$name], 'Process') }
    $shell = Start-Process cmd.exe -ArgumentList "/d /c `"`"$node`" server\dist\index.js >> `"$log`" 2>&1`"" -WorkingDirectory $app `
        -WindowStyle Hidden -PassThru
}
finally {
    foreach ($name in $settings.Keys) { [Environment]::SetEnvironmentVariable($name, $null, 'Process') }
    $settings.Remove('ENCRYPTION_KEY')
}

$deadline = (Get-Date).AddSeconds(60)
$gateway = $null
do {
    Start-Sleep -Milliseconds 250
    if (-not $gateway) { $gateway = Get-CimInstance Win32_Process -Filter "ParentProcessId=$($shell.Id) AND Name='node.exe'" }
    try { $ready = (Invoke-WebRequest "http://127.0.0.1:$port/api/ping" -UseBasicParsing -TimeoutSec 2).StatusCode -eq 200 } catch { $ready = $false }
} until ($ready -or $shell.HasExited -or (Get-Date) -gt $deadline)

if (-not $ready) {
    Write-Host "The gateway didn't start. Last lines of $($log):" -ForegroundColor Red
    Get-Content $log -Tail 20
    if ($gateway) { Stop-Process -Id $gateway.ProcessId -Force -ErrorAction SilentlyContinue }
    exit 1
}

Set-Content -Path $pidFile -Value $gateway.ProcessId -Encoding ASCII
Set-Content -Path $portFile -Value $port -Encoding ASCII
Write-Host "Gateway running: http://127.0.0.1:$port (node pid $($gateway.ProcessId), log $log). stop-gateway.ps1 stops it." -ForegroundColor Green

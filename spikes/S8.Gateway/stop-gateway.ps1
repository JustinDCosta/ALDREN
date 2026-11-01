# Stops the gateway that start-gateway.ps1 started. Its data stays in %LOCALAPPDATA%\Aldren\gateway.
$pidFile = 'E:\AldrenData\gateway\gateway.pid'
$gateway = if (Test-Path $pidFile) { Get-Process -Id (Get-Content $pidFile) -ErrorAction SilentlyContinue }
if ($gateway -and $gateway.ProcessName -eq 'node') {
    Stop-Process -Id $gateway.Id
    Write-Host "Stopped the gateway (node pid $($gateway.Id))"
} else {
    Write-Host 'The gateway is not running'
}
Remove-Item $pidFile -ErrorAction SilentlyContinue

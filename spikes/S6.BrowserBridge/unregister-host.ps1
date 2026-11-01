# Removes what register-host.ps1 added: the com.aldren.bridge key and its host manifest. Other hosts under the same key are untouched.
$key = 'HKCU:\Software\Google\Chrome\NativeMessagingHosts\com.aldren.bridge'
if (Test-Path $key) {
    Remove-Item -Path $key -Recurse
    Write-Host 'Removed com.aldren.bridge from Chrome''s native-messaging hosts'
} else {
    Write-Host 'com.aldren.bridge was not registered'
}

$manifest = Join-Path $PSScriptRoot 'Host\bin\Release\net10.0-windows\win-x64\com.aldren.bridge.json'
if (Test-Path $manifest) { Remove-Item $manifest }

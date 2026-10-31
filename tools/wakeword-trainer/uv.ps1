# Runs uv for this project with everything on E:\AldrenData: package cache, managed Python and the virtual
# environment. Example: .\tools\wakeword-trainer\uv.ps1 run aldren-record --device "Maono"
$env:UV_CACHE_DIR = 'E:\AldrenData\cache\uv'
$env:UV_PYTHON_INSTALL_DIR = 'E:\AldrenData\tools\uv\python'
$env:UV_PROJECT_ENVIRONMENT = 'E:\AldrenData\venvs\wakeword-trainer'
$env:UV_PYTHON_PREFERENCE = 'only-managed'

Push-Location $PSScriptRoot
try {
    & 'E:\AldrenData\tools\uv\uv.exe' @args
}
finally {
    Pop-Location
}
exit $LASTEXITCODE

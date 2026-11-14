<#
.SYNOPSIS
    Helpers for checking ALDREN's windows by eye and by UI Automation, against a test instance with its own data folder.
    Dot-source it:  . tools\dev\ui.ps1
.NOTES
    The test instance runs with ALDREN_HOME (settings, database and logs under E:\AldrenData\dev\test-home), so nothing
    of yours is read or changed. Screenshots are cropped to ALDREN's own window. Buttons are pressed through UI
    Automation and the hotkeys by posting WM_HOTKEY to ALDREN's host window: no synthetic mouse or keyboard input.
#>
$ErrorActionPreference = 'Stop'
$script:AldrenDir = 'E:\AldrenData\dev\aldren'
$script:TestHome = 'E:\AldrenData\dev\test-home'

Add-Type -AssemblyName System.Drawing, UIAutomationClient, UIAutomationTypes
if (-not ('AldrenDev.Native' -as [type])) {
    Add-Type -Namespace AldrenDev -Name Native -MemberDefinition @'
[StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
[DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "FindWindowW")] public static extern IntPtr FindByTitle(IntPtr cls, string title);
[DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "FindWindowW")] public static extern IntPtr FindByClass(string cls, IntPtr title);
[DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hwnd, uint msg, UIntPtr w, IntPtr l);
[DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
[DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
[DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
[DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
[DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr hwnd);
[DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
[DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
[DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out RECT value, int size);
'@
}
# Per-monitor DPI aware (v2) for this thread, so window rectangles are in real pixels.
[void][AldrenDev.Native]::SetThreadDpiAwarenessContext([IntPtr](-4))

function Get-AldrenProcess {
    Get-Process Aldren.Core, Aldren.App, Aldren.BrowserHost -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -and $_.Path.StartsWith($script:AldrenDir, [StringComparison]::OrdinalIgnoreCase) }
}

function Stop-Aldren {
    Get-AldrenProcess | Stop-Process -Force -Confirm:$false
    Start-Sleep -Milliseconds 800
    Get-AldrenProcess | Stop-Process -Force -Confirm:$false
}

function Start-TestAldren {
    param([switch] $Cloud, [switch] $Speak, [string] $AppPath)
    if (Get-AldrenProcess) { throw 'ALDREN is already running from the dev folder; Stop-Aldren first.' }
    New-Item -ItemType Directory -Force "$script:TestHome\settings" | Out-Null
    # Shown to capture (the island is hidden from screenshots otherwise), silent, no greeting, no away mute.
    @"
{
  "schemaVersion": 4,
  "general": { "hotkey": "Ctrl+/", "showOnStream": true, "personality": "friendly" },
  "voice": { "wakeWord": false, "transcriptCheck": true, "speakReplies": $(if ($Speak) { 'true' } else { 'false' }), "pushToTalkHotkey": "Ctrl+*" },
  "presence": { "greeting": false, "briefingWithGreeting": false, "greetingAfterHours": 4, "awayMute": false, "awayMuteSkipHeadphones": true },
  "cloud": { "enabled": $(if ($Cloud) { 'true' } else { 'false' }), "quickFallback": false }
}
"@ | Set-Content "$script:TestHome\settings\settings.json" -Encoding utf8
    $env:ALDREN_HOME = $script:TestHome
    $arguments = if ($AppPath) { @('--app', $AppPath) } else { @() }
    if ($arguments.Count -gt 0) {
        Start-Process "$script:AldrenDir\Aldren.Core.exe" -ArgumentList $arguments -WorkingDirectory $script:AldrenDir
    } else {
        Start-Process "$script:AldrenDir\Aldren.Core.exe" -WorkingDirectory $script:AldrenDir
    }
    Remove-Item Env:\ALDREN_HOME
}

function Get-AldrenWindow {
    param([Parameter(Mandatory)] [string] $Title)
    $hwnd = [AldrenDev.Native]::FindByTitle([IntPtr]::Zero, $Title)
    if ($hwnd -eq [IntPtr]::Zero) { throw "No window titled '$Title'." }
    $hwnd
}

function Wait-AldrenWindow {
    param([Parameter(Mandatory)] [string] $Title, [int] $Seconds = 60)
    $until = (Get-Date).AddSeconds($Seconds)
    while ((Get-Date) -lt $until) {
        $hwnd = [AldrenDev.Native]::FindByTitle([IntPtr]::Zero, $Title)
        if ($hwnd -ne [IntPtr]::Zero) { return $hwnd }
        Start-Sleep -Milliseconds 300
    }
    throw "No window titled '$Title' after $Seconds s."
}

function Send-AldrenHotkey {
    # 1 = text box, 2 = push-to-talk, 3 = Escape while speaking
    param([int] $Id = 1)
    $hostWindow = [AldrenDev.Native]::FindByClass('Aldren.App.Host', [IntPtr]::Zero)
    if ($hostWindow -eq [IntPtr]::Zero) { throw 'The App is not running.' }
    [void][AldrenDev.Native]::PostMessage($hostWindow, 0x0312, [UIntPtr][uint32]$Id, [IntPtr]::Zero)
}

function Send-TrayCommand {
    # 1 = click on the icon (opens the conversation window)
    $hostWindow = [AldrenDev.Native]::FindByClass('Aldren.App.Host', [IntPtr]::Zero)
    [void][AldrenDev.Native]::PostMessage($hostWindow, 0x8001, [UIntPtr]::Zero, [IntPtr]0x400)
}

function Save-WindowShot {
    # -Window draws the window itself (PrintWindow), so nothing in front of it is captured; without it the screen region is copied (the island).
    param([Parameter(Mandatory)] [IntPtr] $Hwnd, [Parameter(Mandatory)] [string] $Path, [int[]] $Crop, [switch] $Window, [double] $Scale = 1)
    $r = New-Object AldrenDev.Native+RECT
    # The visible frame, without the invisible resize borders.
    if ([AldrenDev.Native]::DwmGetWindowAttribute($Hwnd, 9, [ref]$r, 16) -ne 0 -or ($r.Right - $r.Left) -le 0) {
        [void][AldrenDev.Native]::GetWindowRect($Hwnd, [ref]$r)
    }
    $x = $r.Left; $y = $r.Top; $w = $r.Right - $r.Left; $h = $r.Bottom - $r.Top
    if ($Crop) { $x += $Crop[0]; $y += $Crop[1]; $w = $Crop[2]; $h = $Crop[3] }
    $bitmap = New-Object System.Drawing.Bitmap $w, $h
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    if ($Window) {
        $full = New-Object AldrenDev.Native+RECT
        [void][AldrenDev.Native]::GetWindowRect($Hwnd, [ref]$full)
        $whole = New-Object System.Drawing.Bitmap ($full.Right - $full.Left), ($full.Bottom - $full.Top)
        $wg = [System.Drawing.Graphics]::FromImage($whole)
        $hdc = $wg.GetHdc()
        [void][AldrenDev.Native]::PrintWindow($Hwnd, $hdc, 2)
        $wg.ReleaseHdc($hdc)
        $wg.Dispose()
        $graphics.DrawImage($whole, $full.Left - $x, $full.Top - $y)
        $whole.Dispose()
    } else {
        $graphics.CopyFromScreen($x, $y, 0, 0, $bitmap.Size)
    }
    $graphics.Dispose()
    if ($Scale -ne 1) {
        $small = New-Object System.Drawing.Bitmap ([int]($w * $Scale)), ([int]($h * $Scale))
        $sg = [System.Drawing.Graphics]::FromImage($small)
        $sg.InterpolationMode = 'HighQualityBicubic'
        $sg.DrawImage($bitmap, 0, 0, $small.Width, $small.Height)
        $sg.Dispose()
        $bitmap.Dispose()
        $bitmap = $small
    }
    New-Item -ItemType Directory -Force (Split-Path -Parent $Path) | Out-Null
    $bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
    "{0}  {1}x{2} at ({3},{4}), {5} dpi" -f $Path, $w, $h, $x, $y, [AldrenDev.Native]::GetDpiForWindow($Hwnd)
}

function Get-UiaElement {
    param([Parameter(Mandatory)] [IntPtr] $Hwnd, [Parameter(Mandatory)] [string] $Name, [int] $Seconds = 5)
    $root = [System.Windows.Automation.AutomationElement]::FromHandle($Hwnd)
    $condition = New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::NameProperty), $Name
    $until = (Get-Date).AddSeconds($Seconds)
    do {
        $element = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
        if ($element) { return $element }
        Start-Sleep -Milliseconds 200
    } while ((Get-Date) -lt $until)
    throw "No element named '$Name'."
}

function Invoke-Uia {
    # Presses a button, selects a navigation item or flips a toggle, whichever the element supports.
    param([Parameter(Mandatory)] [IntPtr] $Hwnd, [Parameter(Mandatory)] [string] $Name)
    $element = Get-UiaElement $Hwnd $Name
    $pattern = $null
    if ($element.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern, [ref]$pattern)) { $pattern.Select(); return }
    if ($element.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$pattern)) { $pattern.Invoke(); return }
    if ($element.TryGetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern, [ref]$pattern)) { $pattern.Toggle(); return }
    throw "'$Name' can't be invoked, selected or toggled."
}

function Set-UiaValue {
    param([Parameter(Mandatory)] [IntPtr] $Hwnd, [Parameter(Mandatory)] [string] $Name, [Parameter(Mandatory)] [AllowEmptyString()] [string] $Value)
    $element = Get-UiaElement $Hwnd $Name
    $pattern = $null
    if (-not $element.TryGetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern, [ref]$pattern)) { throw "'$Name' has no value to set." }
    $pattern.SetValue($Value)
}

function Get-UiaText {
    # Names of the text elements in a window, for checking what a page shows without a screenshot.
    param([Parameter(Mandatory)] [IntPtr] $Hwnd, [int] $Max = 200)
    $root = [System.Windows.Automation.AutomationElement]::FromHandle($Hwnd)
    $all = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
    $all | Select-Object -First $Max | ForEach-Object {
        $type = $_.Current.ControlType.ProgrammaticName -replace 'ControlType\.', ''
        if ($_.Current.Name) { "{0,-12} {1}" -f $type, ($_.Current.Name -replace "`r?`n", ' / ') }
    }
}

function Read-TestLog {
    param([ValidateSet('core', 'app')] [string] $Name = 'core', [int] $Lines = 30)
    $log = Get-ChildItem "$script:TestHome\data\logs\$Name-*.log" -ErrorAction SilentlyContinue | Sort-Object LastWriteTime | Select-Object -Last 1
    if (-not $log) { return "No $Name log yet." }
    Get-Content $log.FullName -Tail $Lines | ForEach-Object {
        try { $e = $_ | ConvertFrom-Json } catch { return $_ }
        $m = $e.'@mt'
        foreach ($p in $e.PSObject.Properties) {
            if ($p.Name -notlike '@*') { $m = $m -replace ('\{' + $p.Name + '(:[^}]*)?\}'), "$($p.Value)".Replace('$', '$$') }
        }
        $x = if ($e.'@x') { ' | ' + (($e.'@x' -split "`n")[0..4] -join ' / ') } else { '' }
        "$($e.'@t'.Substring(11,8)) $($e.'@l') $m$x"
    }
}

function Send-AldrenCommand {
    # A tray menu command without the menu: 1 conversation, 2 text field, 3 Settings, 4 silent, 5-7 gaming auto/on/off, 8 end focus.
    param([Parameter(Mandatory)] [int] $Command)
    $hostWindow = [AldrenDev.Native]::FindByClass('Aldren.App.Host', [IntPtr]::Zero)
    if ($hostWindow -eq [IntPtr]::Zero) { throw 'The App is not running.' }
    [void][AldrenDev.Native]::PostMessage($hostWindow, 0x8002, [UIntPtr][uint32]$Command, [IntPtr]::Zero)
}

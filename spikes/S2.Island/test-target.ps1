# Stand-in "normal app" for the automated island checks: a borderless solid-colour window with a text box.
# The checklist positions it, types into it, clicks it and reads its text back. It counts mouse clicks in its title.
param([Parameter(Mandatory)][string]$Title)

Add-Type -AssemblyName System.Windows.Forms, System.Drawing

$form = [System.Windows.Forms.Form]::new()
$form.Text = $Title
$form.FormBorderStyle = 'None'
$form.StartPosition = 'Manual'
$form.BackColor = [System.Drawing.Color]::FromArgb(32, 96, 160)
$form.Bounds = [System.Drawing.Rectangle]::new(100, 100, 900, 500)

$box = [System.Windows.Forms.TextBox]::new()
$box.Multiline = $true
$box.Dock = 'Bottom'
$box.Height = 140
$box.Font = [System.Drawing.Font]::new('Segoe UI', 11)
$form.Controls.Add($box)

$script:clicks = 0
$form.Add_MouseDown({
        $script:clicks++
        $form.Text = "$Title clicks=$script:clicks"
    })

[System.Windows.Forms.Application]::Run($form)

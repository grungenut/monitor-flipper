# ============================================================
#  Monitor Flipper AI Launcher - PowerShell edition
#  Opens Claude Code (and Codex, if installed) in this folder.
#  Self-locating: safe to move or rename the project folder.
# ============================================================

$PROJECT   = Split-Path -Parent $MyInvocation.MyCommand.Path
$WT        = "$env:LOCALAPPDATA\Microsoft\WindowsApps\wt.exe"
$WT_TARGET = "flipper-ai"
$TEMP      = $env:TEMP

Write-Host ""
Write-Host " ============================================================"
Write-Host "  Monitor Flipper AI Launcher"
Write-Host "  Project: $PROJECT"
Write-Host " ============================================================"
Write-Host ""

# Check Node.js
Write-Host " Checking Node.js..."
if (-not (Get-Command node -ErrorAction SilentlyContinue)) {
    Write-Host "   [MISSING] Run: winget install OpenJS.NodeJS.LTS"
    Write-Host "             Then restart PowerShell and try again."
    Read-Host " Press Enter to exit"
    exit
}
Write-Host "   [OK]      $(node --version)"

# Check / install each tool
function Ensure-Tool($name, $package) {
    Write-Host " Checking $name..."
    if (-not (Get-Command $name -ErrorAction SilentlyContinue)) {
        Write-Host "   [MISSING] Installing $name..."
        npm install -g $package
        if (-not (Get-Command $name -ErrorAction SilentlyContinue)) {
            Write-Host "   [FAILED]  Could not install $name"
            return $false
        }
        Write-Host "   [OK]      $name installed"
    } else {
        Write-Host "   [OK]      $name ready"
    }
    return $true
}

$hasCodex  = Ensure-Tool "codex"  "@openai/codex@latest"
$hasClaude = Ensure-Tool "claude" "@anthropic-ai/claude-code@latest"

Write-Host ""
Write-Host " Launching AI tools..."
Write-Host ""

# Write a small launcher ps1 for each tool into TEMP
# (avoids quoting issues when passing commands through wt.exe)
$npmPath = "C:\Users\$env:USERNAME\AppData\Roaming\npm"

@"
Set-Location '$PROJECT'
& '$npmPath\codex.ps1' --yolo -a never
"@ | Set-Content "$TEMP\launch_codex.ps1"

@"
Set-Location '$PROJECT'
`$env:DISABLE_AUTOUPDATER = '1'
& '$npmPath\claude.ps1' --dangerously-skip-permissions
"@ | Set-Content "$TEMP\launch_claude.ps1"

function Launch($title, $script) {
    if (Test-Path $WT) {
        & $WT -w $WT_TARGET new-tab --title $title -- powershell -NoExit -File $script
    } else {
        Start-Process pwsh -ArgumentList "-NoExit", "-File", $script
    }
}

if ($hasCodex)  { Write-Host "   Launching Codex...";       Launch "Codex"  "$TEMP\launch_codex.ps1" }
if ($hasClaude) { Write-Host "   Launching Claude Code..."; Launch "Claude" "$TEMP\launch_claude.ps1" }

Write-Host ""
Write-Host " Done!"
Write-Host ""

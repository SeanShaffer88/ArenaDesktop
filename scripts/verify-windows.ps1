# Verify ArenaDesktop on a real Windows PC.
#
# Why this exists: every row of QUALITY.md that says "Partially verified" or
# "Unverified" is waiting on one thing — somebody sitting at a Windows machine
# and doing the work on RELEASE_CHECKLIST.md. This script does the parts a
# script can do, asks for a verdict on the parts it cannot, and writes the
# result as a Markdown file you can attach to a bug report or copy into
# QUALITY.md.
#
# It never touches the network beyond the app's own update check, never reads
# the Arena page, and never uploads anything.
#
# Usage:
#   pwsh -File scripts/verify-windows.ps1
#   pwsh -File scripts/verify-windows.ps1 -Exe publish/win-x64/ArenaDesktop.exe
#   pwsh -File scripts/verify-windows.ps1 -NonInteractive   # skeleton only
#
# Exit code: 0 when every check passed or was skipped, 1 when anything was
# marked a problem, 2 when the automated part failed.

[CmdletBinding()]
param(
    [string]$Exe = "",
    [string]$OutDir = "",
    [switch]$NonInteractive
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
if ([string]::IsNullOrWhiteSpace($Exe)) {
    $candidates = @(
        (Join-Path $repoRoot "publish/win-x64/ArenaDesktop.exe"),
        (Join-Path $repoRoot "src/ArenaDesktop/bin/Release/net10.0-windows/ArenaDesktop.exe")
    )
    $Exe = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
}
if ([string]::IsNullOrWhiteSpace($OutDir)) {
    $OutDir = Join-Path $repoRoot "docs/verification"
}

$stamp = Get-Date
$results = New-Object System.Collections.Generic.List[object]

function Add-Result {
    param([string]$Check, [string]$Verdict, [string]$Note = "")
    $results.Add([pscustomobject]@{ Check = $Check; Verdict = $Verdict; Note = $Note })
    Write-Host ("  {0,-52} {1}  {2}" -f $Check, $Verdict, $Note)
}

function Ask-Verdict {
    param([string]$Check)
    if ($NonInteractive) {
        Add-Result $Check "Not recorded" "non-interactive run"
        return
    }
    Write-Host ""
    Write-Host "$Check" -ForegroundColor Cyan
    $answer = Read-Host "  [p]ass / [r] problem / [s] skip / [Enter] skip"
    $note = ""
    switch ($answer.ToLowerInvariant()) {
        "p" { $note = Read-Host "  note (optional)"; Add-Result $Check "Pass" $note }
        "r" { $note = Read-Host "  what went wrong"; Add-Result $Check "Problem" $note }
        default { Add-Result $Check "Skipped" $note }
    }
}

Write-Host "ArenaDesktop verification run — $($stamp.ToString('yyyy-MM-dd HH:mm'))" -ForegroundColor Green
Write-Host ""

# ── automated ───────────────────────────────────────────────────────────────
if ([string]::IsNullOrWhiteSpace($Exe) -or -not (Test-Path $Exe)) {
    Write-Host "No ArenaDesktop.exe found — build or publish first:" -ForegroundColor Yellow
    Write-Host "  dotnet publish src/ArenaDesktop/ArenaDesktop.csproj -c Release -r win-x64 --self-contained true -o publish/win-x64"
    exit 2
}

Write-Host "Executable: $Exe"
$version = (Get-Item $Exe).VersionInfo.FileVersion
Add-Result "Binary present ($version)" "Pass" $Exe

Write-Host ""
Write-Host "Running the start-up smoke test (--smoke-test)…"
$process = Start-Process -FilePath $Exe -ArgumentList "--smoke-test" -PassThru -Wait
if ($process.ExitCode -eq 0) {
    Add-Result "Start-up smoke test (window + WebView2)" "Pass" ""
}
else {
    Add-Result "Start-up smoke test (window + WebView2)" "Problem" "exit code $($process.ExitCode) — see the log"
}

$logPath = Join-Path $env:APPDATA "ArenaDesktop\logs\arenadesktop.log"
$logTail = ""
if (Test-Path $logPath) {
    $logTail = (Get-Content $logPath -Tail 60) -join "`n"
    $errors = (Get-Content $logPath -Tail 400 | Select-String -Pattern "\[ERROR\]" -SimpleMatch)
    if ($errors) {
        Add-Result "No ERROR lines in the recent log" "Problem" "$($errors.Count) found"
    }
    else {
        Add-Result "No ERROR lines in the recent log" "Pass" ""
    }
}
else {
    Add-Result "Log file exists" "Problem" $logPath
}

# ── interactive: the checklist's judgement calls ────────────────────────────
Write-Host ""
Write-Host "The rest needs your eyes. Answer, or press Enter to skip a check." -ForegroundColor Yellow

Ask-Verdict "App opens to Arena, signed in, page renders"
Ask-Verdict "Three layouts: Standard / Compact Focus / Left Rail (Ctrl+Shift+K cycles, remembers)"
Ask-Verdict "Command palette (Ctrl+K): 50 commands, conditional rows appear correctly, Enter runs one"
Ask-Verdict "Prompt pad (Ctrl+Shift+P): copy & open, draft kept on close, Save to Battle Lab (Ctrl+S)"
Ask-Verdict "Battle Lab: prompts, blanks, run queue, journal entry, prompt kept from the pad appears"
Ask-Verdict "Downloads: progress + ETA, pause/resume/cancel, history entries open and delete"
Ask-Verdict "App lock: set, idle auto-lock, unlock; wrong password refused"
Ask-Verdict "Error report: force an error, box names the exception + frame, Copy details pastes a redacted report"
Ask-Verdict "Settings opens with a damaged data file (banner names the panel; Repair data files… backs it up)"
Ask-Verdict "In-app updater: check, download, hash verify, install, relaunch"
Ask-Verdict "Rollback to the previous in-app-updated version"

# ── write the report ────────────────────────────────────────────────────────
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$reportPath = Join-Path $OutDir ("verify-" + $stamp.ToString("yyyyMMdd-HHmmss") + ".md")

$pass = @($results | Where-Object Verdict -eq "Pass").Count
$problem = @($results | Where-Object Verdict -eq "Problem").Count
$skipped = @($results | Where-Object { $_.Verdict -in @("Skipped", "Not recorded") }).Count

$lines = @(
    "# ArenaDesktop verification run",
    "",
    "- When: $($stamp.ToString('yyyy-MM-dd HH:mm')) (local)",
    "- Machine: $([System.Environment]::MachineName) — $([System.Environment]::OSVersion.VersionString)",
    "- Architecture: $([System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture)",
    "- Binary: ``$Exe`` (file version $version)",
    "- Result: **$pass passed**, $problem with a problem, $skipped not recorded",
    "",
    "| Check | Verdict | Note |",
    "| --- | --- | --- |"
)
foreach ($r in $results) {
    $note = ($r.Note -replace "\|", "\|" -replace "`r?`n", " ").Trim()
    $lines += "| $($r.Check) | $($r.Verdict) | $note |"
}
$lines += ""
$lines += "This file is evidence, not a claim: it records what one person saw on one machine at one time. Attach it to a bug report, or cite it in QUALITY.md."
if ($logTail.Trim().Length -gt 0) {
    $lines += ""
    $lines += "## Log tail (last 60 lines, unredacted — read before sharing)"
    $lines += ""
    $lines += '```'
    $lines += $logTail
    $lines += '```'
}
Set-Content -Path $reportPath -Value ($lines -join "`n") -Encoding UTF8

Write-Host ""
Write-Host "Report written: $reportPath" -ForegroundColor Green

if ($problem -gt 0) { exit 1 }
exit 0

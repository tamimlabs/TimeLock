<#
    TimeLock uninstaller

    Removes the scheduled task, stops any running lock, and deletes the
    program folder.  Asks before deleting your saved lock state.
#>

$ErrorActionPreference = 'Stop'

$TaskName = 'TimeLock.Enforce'
$InstallDir = Join-Path $env:ProgramFiles 'TimeLock'
$DataDir = Join-Path $env:ProgramData 'TimeLock'

function Write-Step($m) { Write-Host "  -> $m" }
function Write-Ok($m)  { Write-Host "  [ok] $m" }
function Write-Bad($m) { Write-Host "  [!!] $m" -ForegroundColor Red }

# ----------------------------------------------------------------- elevate
$id = [Security.Principal.WindowsIdentity]::GetCurrent()
$isAdmin = (New-Object Security.Principal.WindowsPrincipal $id).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)

if (-not $isAdmin) {
    Write-Host ''
    Write-Host 'TimeLock removal needs administrator rights.' -ForegroundColor Yellow
    Start-Process -FilePath 'powershell.exe' -Verb RunAs -ArgumentList @(
        '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSCommandPath`""
    )
    exit
}

Write-Host ''
Write-Host '  TimeLock removal' -ForegroundColor Cyan
Write-Host '  -----------------' -ForegroundColor Cyan
Write-Host ''

# ------------------------------------------------------- end any active lock
Write-Step 'Ending any active lock'
Get-Process -Name 'TimeLock.Client', 'TimeLock.Enforce', 'TimeLock.App' -ErrorAction SilentlyContinue |
    Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 500

if (Test-Path -LiteralPath (Join-Path $DataDir 'state.json')) {
    try {
        [System.IO.File]::WriteAllText(
            (Join-Path $DataDir 'state.json'),
            '{"Enabled":false,"EndUtcTicks":0,"CreatedUtcTicks":0,"DurationSeconds":0,"Message":""}',
            (New-Object System.Text.UTF8Encoding $false))
        Write-Ok 'lock state cleared'
    }
    catch {
        Write-Bad "could not clear state: $($_.Exception.Message)"
    }
}

# ------------------------------------------------------------------- task
Write-Step "Removing scheduled task '$TaskName'"
try {
    Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false -ErrorAction Stop
    Write-Ok 'task removed'
}
catch {
    if ($_.Exception.Message -match 'cannot find') { Write-Ok 'task was not registered' }
    else { Write-Bad "could not remove task: $($_.Exception.Message)" }
}

# ------------------------------------------------------------------ files
Write-Step 'Deleting program files'
if (Test-Path -LiteralPath $InstallDir) {
    try {
        Remove-Item -LiteralPath $InstallDir -Recurse -Force
        Write-Ok "removed $InstallDir"
    }
    catch { Write-Bad "could not remove $InstallDir : $($_.Exception.Message)" }
}
else {
    Write-Ok 'nothing installed'
}

Write-Step 'Deleting shortcuts'
foreach ($link in @(
    (Join-Path $env:ProgramData 'Microsoft\Windows\Start Menu\Programs\TimeLock.lnk'),
    (Join-Path ([Environment]::GetFolderPath('Desktop')) 'TimeLock.lnk'))) {
    try { if (Test-Path -LiteralPath $link) { Remove-Item -LiteralPath $link -Force } }
    catch { }
}
Write-Ok 'shortcuts removed'

# ------------------------------------------------------------------- data
$answer = 'n'
if (Test-Path -LiteralPath $DataDir) {
    $answer = Read-Host '  Delete TimeLock settings and logs too? [y/N]'
}

if ($answer -match '^[Yy]') {
    Write-Step 'Deleting data directory'
    try {
        # Restore normal permissions first, otherwise the hardened ACL blocks us.
        $acl = Get-Acl -LiteralPath $DataDir
        $acl.SetAccessRuleProtection($false, $false)
        Set-Acl -LiteralPath $DataDir -AclObject $acl
        Remove-Item -LiteralPath $DataDir -Recurse -Force
        Write-Ok "removed $DataDir"
    }
    catch { Write-Bad "could not remove $DataDir : $($_.Exception.Message)" }
}
else {
    Write-Step 'Keeping settings and logs'
    Write-Host "       $DataDir"
}

Write-Host ''
Write-Host '  TimeLock has been removed.' -ForegroundColor Green
Write-Host ''

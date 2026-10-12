<#
    TimeLock installer

    - copies the three executables to  %ProgramFiles%\TimeLock
    - creates %ProgramData%\TimeLock  with a read-only state file for normal
      users and a writable expiry flag (the lock client runs unprivileged)
    - registers the SYSTEM scheduled task that enforces the lock at logon,
      at boot, and every minute afterwards
    - adds Start Menu + Desktop shortcuts

    Safe to run more than once.
#>

$ErrorActionPreference = 'Stop'

$TaskName = 'TimeLock.Enforce'
$InstallDir = Join-Path $env:ProgramFiles 'TimeLock'
$DataDir = Join-Path $env:ProgramData 'TimeLock'
$EnforcerExe = Join-Path $InstallDir 'TimeLock.Enforce.exe'
$AppExe = Join-Path $InstallDir 'TimeLock.App.exe'

function Write-Step($m)  { Write-Host "  -> $m" }
function Write-Ok($m)   { Write-Host "  [ok] $m" }
function Write-Bad($m)  { Write-Host "  [!!] $m" -ForegroundColor Red }

# The state file is parsed by DataContractJsonSerializer, which rejects a BOM.
function Write-Utf8NoBom($path, $text) {
    [System.IO.File]::WriteAllText($path, $text, (New-Object System.Text.UTF8Encoding $false))
}

# ----------------------------------------------------------------- elevate
$id = [Security.Principal.WindowsIdentity]::GetCurrent()
$isAdmin = (New-Object Security.Principal.WindowsPrincipal $id).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)

if (-not $isAdmin) {
    Write-Host ''
    Write-Host 'TimeLock setup needs administrator rights.' -ForegroundColor Yellow
    Write-Host 'Restarting with elevation...' -ForegroundColor Yellow
    Start-Process -FilePath 'powershell.exe' -Verb RunAs -ArgumentList @(
        '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSCommandPath`""
    )
    exit
}

Write-Host ''
Write-Host '  TimeLock setup' -ForegroundColor Cyan
Write-Host '  ---------------' -ForegroundColor Cyan
Write-Host ''

# ----------------------------------------------------------------- binaries
$root = Split-Path -Parent $PSScriptRoot
$bin = Join-Path $root 'bin'

if (-not (Test-Path -LiteralPath $bin)) {
    Write-Bad "Build output not found at $bin"
    Write-Host '     Run build.cmd first.'
    exit 1
}

$required = @('TimeLock.Client.exe', 'TimeLock.Enforce.exe', 'TimeLock.App.exe')
$missing = @()
foreach ($f in $required) {
    if (-not (Test-Path -LiteralPath (Join-Path $bin $f))) { $missing += $f }
}
if ($missing.Count -gt 0) {
    Write-Bad "Missing binaries: $($missing -join ', ')"
    Write-Host '     Run build.cmd first.'
    exit 1
}

Write-Step 'Installing to Program Files'
if (-not (Test-Path -LiteralPath $InstallDir)) {
    New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null
}

# Stop anything already running so files are not locked.
Get-Process -Name 'TimeLock.Client', 'TimeLock.Enforce', 'TimeLock.App' -ErrorAction SilentlyContinue |
    Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 600

foreach ($f in $required) {
    Copy-Item -LiteralPath (Join-Path $bin $f) -Destination $InstallDir -Force
}
Write-Ok "copied $($required.Count) files to $InstallDir"

# ------------------------------------------------------------------ data dir
Write-Step 'Preparing data directory'
if (-not (Test-Path -LiteralPath $DataDir)) {
    New-Item -ItemType Directory -Path $DataDir -Force | Out-Null
}

$stateFile = Join-Path $DataDir 'state.json'
$enforcerLog = Join-Path $DataDir 'enforcer.log'
$clientLog = Join-Path $DataDir 'client.log'

foreach ($f in @($stateFile, $enforcerLog, $clientLog)) {
    if (-not (Test-Path -LiteralPath $f)) { New-Item -ItemType File -Path $f -Force | Out-Null }
}

# Leftovers from an older layout.
Remove-Item -LiteralPath (Join-Path $DataDir 'expired.flag') -Force -ErrorAction SilentlyContinue

# A brand-new install must not come up locked.
# No BOM: DataContractJsonSerializer chokes on a leading byte-order mark.
Write-Utf8NoBom $stateFile ('{"Enabled":false,"EndUtcTicks":0,"CreatedUtcTicks":0,"DurationSeconds":0,"Message":""}')
Write-Ok "data directory ready at $DataDir"

# ------------------------------------------------------------------- ACLs
Write-Step 'Locking down file permissions'
# state.json is owned by SYSTEM/admins only: a normal process cannot cancel
# or extend a lock. The client can only drop expired.flag, which can only ever
# shorten a lock that has genuinely run out.
try {
    # Use SIDs, not BUILTIN\Administrators -- built-in group names are
    # localised and fail to resolve on non-English Windows installs.
    $sidAdmins = New-Object Security.Principal.SecurityIdentifier 'S-1-5-32-544' # Administrators
    $sidSystem = New-Object Security.Principal.SecurityIdentifier 'S-1-5-18'    # SYSTEM
    $sidUsers  = New-Object Security.Principal.SecurityIdentifier 'S-1-5-32-545' # Users

    $acl = Get-Acl -LiteralPath $DataDir
    $acl.SetAccessRuleProtection($true, $false)   # drop inherited rules
    $acl.SetOwner($sidAdmins)

    $inherit = 'ContainerInherit,ObjectInherit'
    $null = $acl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule(
        $sidAdmins, 'FullControl', $inherit, 'None', 'Allow')))
    $null = $acl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule(
        $sidSystem, 'FullControl', $inherit, 'None', 'Allow')))
    # Everyone else may read but not write: the lock client runs unprivileged
    # and must have no way to cancel or extend a lock.
    $null = $acl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule(
        $sidUsers, 'ReadAndExecute', $inherit, 'None', 'Allow')))

    Set-Acl -LiteralPath $DataDir -AclObject $acl
    Write-Ok 'data folder restricted to administrators + SYSTEM (others read-only)'
}
catch {
    Write-Bad "could not tighten folder ACL: $($_.Exception.Message)"
}

# ------------------------------------------------------------------- task
Write-Step "Registering scheduled task '$TaskName'"

try {
    $action = New-ScheduledTaskAction -Execute $EnforcerExe -WorkingDirectory $InstallDir

    # Runs at every logon (any user), shortly after boot, and then every
    # minute forever -- so a killed client always comes back.
    $triggers = @(
        (New-ScheduledTaskTrigger -AtLogOn),
        (New-ScheduledTaskTrigger -AtStartup),
        (New-ScheduledTaskTrigger -Once -At (Get-Date).AddMinutes(-1) `
            -RepetitionInterval (New-TimeSpan -Minutes 1) `
            -RepetitionDuration (New-TimeSpan -Days 3650))
    )

    $principal = New-ScheduledTaskPrincipal -UserId 'SYSTEM' `
        -LogonType ServiceAccount -RunLevel Highest

    $settings = New-ScheduledTaskSettingsSet `
        -AllowStartIfOnBatteries `
        -DontStopIfGoingOnBatteries `
        -StartWhenAvailable `
        -MultipleInstances IgnoreNew `
        -ExecutionTimeLimit ([TimeSpan]::Zero) `
        -RestartCount 5 -RestartInterval (New-TimeSpan -Minutes 1)

    Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false -ErrorAction SilentlyContinue

    Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $triggers `
        -Principal $principal -Settings $settings `
        -Description 'TimeLock session enforcement (runs as SYSTEM).' -Force | Out-Null

    Write-Ok 'task registered (SYSTEM, at logon / at boot / every minute)'
}
catch {
    Write-Bad "could not register the scheduled task: $($_.Exception.Message)"
    Write-Host '     Without this task the lock will NOT survive a restart.'
}

# ------------------------------------------------------------------ links
Write-Step 'Adding shortcuts'
$shell = New-Object -ComObject WScript.Shell

function New-Shortcut($target, $linkPath, $icon) {
    try {
        $s = $shell.CreateShortcut($linkPath)
        $s.TargetPath = $target
        $s.WorkingDirectory = (Split-Path -Parent $target)
        $s.IconLocation = "$target,0"
        $s.Save()
        Write-Ok "shortcut -> $(Split-Path -Leaf $linkPath)"
    }
    catch {
        Write-Bad "could not create $linkPath : $($_.Exception.Message)"
    }
}

$programs = Join-Path $env:ProgramData 'Microsoft\Windows\Start Menu\Programs'
New-Shortcut $AppExe (Join-Path $programs 'TimeLock.lnk') $null
New-Shortcut $AppExe (Join-Path ([Environment]::GetFolderPath('Desktop')) 'TimeLock.lnk') $null

# ----------------------------------------------------------------- finish
Write-Host ''
Write-Host '  Done.' -ForegroundColor Green
Write-Host ''
Write-Host '  Open TimeLock from the Start Menu, choose a duration, press Start lock.'
Write-Host ''
Write-Host '  Notes:' -ForegroundColor Yellow
Write-Host '   * The lock is enforced by a SYSTEM task and comes back after a restart.'
Write-Host '   * It does NOT block the Ctrl+Alt+Del secure screen, Safe Mode,'
Write-Host '     another Windows account, or someone holding the power button.'
Write-Host '   * To remove everything: install\Uninstall.ps1'
Write-Host ''

# Changelog

All notable changes to this project.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project follows [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- Initial release.
- `TimeLock.App.exe` — pick a duration, optional message, presets, live status.
- `TimeLock.Enforce.exe` — SYSTEM scheduled task (at logon, at boot, and every
  minute) that owns the timer and respawns the lock screen into every
  interactive session via `CreateProcessAsUser`.
- `TimeLock.Client.exe` — borderless fullscreen lock screen spanning all
  monitors, with low-level keyboard and mouse hooks.
- Lock anchored to an absolute UTC end time, so rebooting, sleeping or changing
  the clock cannot extend it.
- Shell suppression: `explorer.exe` is terminated during a lock and
  `Winlogon\AutoRestartShell` is set to `0`, closing the `Win+Ctrl+D` new-desktop
  and virtual-desktop-switching bypass. Both are restored on unlock.
- Hardened ACLs on `C:\ProgramData\TimeLock` so normal users cannot cancel or
  extend a lock.
- Fail-open design: a missing, empty or corrupt state file means *unlocked*.
- `install\Install.ps1` and `install\Uninstall.ps1`.
- **Branding.** A padlock icon generated entirely in code by
  `tools\make_logo.py` (Pillow) — no hand-drawn artwork in the repository. The
  palette is taken from the lock screen in `Client.cs`. Ships as a
  multi-resolution `.ico` (16–256px), embedded into all three executables by
  `build.cmd`, plus light/dark README lockups.
- GitHub Actions CI that builds with the in-box `csc.exe` and smoke-tests that
  the executables exit cleanly when no lock is active.

### Known limitations

Documented in full in the README. In short: `Ctrl+Alt+Del`, Safe Mode and a
forced power off cannot be blocked by any user-mode application, and an
administrator can always remove TimeLock. For an OS-enforced guarantee, use
Windows 11 Pro's Assigned Access.

### Fixed during development

Recorded because each one is a trap worth knowing about:

- `CreateProcessAsUser` failed with `ERROR_ACCESS_DENIED`, then hard-crashed the
  enforcer with an access violation. `lpCommandLine` is `LPWSTR` (mutable by the
  callee) but was being marshalled as a read-only string.
- A stale expiry-flag mechanism was dead code — the enforcer deleted the flag on
  every run, so the unprivileged client could never recreate it.
- The lock client crashed with `ObjectDisposedException` on exit when a queued
  timer tick touched the form after disposal.
- Duplicate enforcer instances could run concurrently; now a global mutex makes
  extras exit immediately.
- The `hours` label painted an opaque white rectangle over the `minutes` field,
  because `Label.BackColor` defaults to opaque white and the label was 200px
  wide.
- `build.cmd` printed "Access is denied" and swallowed its own success message:
  `echo [ok] Build complete -> %OUT%` was parsed as output redirection because
  of the `>`, so cmd tried to create a file at the output path.

### Verified

On Windows 11 Home (build 26220): lock screen spawns in ~4s; `Win+Ctrl+D`,
`Alt+Tab` and `Ctrl+Shift+Esc` are blocked; the lock ends within 3s of its
deadline; explorer, `AutoRestartShell` and the state file are all restored on
unlock; standard users cannot write `state.json`.

Reboot persistence is implemented by design but had not been exercised across a
real reboot cycle at the time of writing.

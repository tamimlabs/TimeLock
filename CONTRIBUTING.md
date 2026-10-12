# Contributing to TimeLock

Thanks for looking at this. It is a small project with some sharp edges, so
before you start: **please read the "What it does *not* stop" section of the
README.** A large fraction of feature requests are things Windows does not let
any user-mode application do.

## Build

```
build.cmd
```

That is the whole toolchain. It uses the C# compiler that ships inside Windows
(`C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe`), so there is
nothing to install — no Visual Studio, no .NET SDK, no NuGet, no `dotnet` CLI.
CI builds the same way, which is deliberate: if we ever add a dependency that
requires an SDK, the project's main selling point is gone.

Three executables come out of `bin\`:

| File | Runs as | Responsibility |
| --- | --- | --- |
| `TimeLock.App.exe` | the user, elevated | the window you set a duration in |
| `TimeLock.Enforce.exe` | `SYSTEM`, on a schedule | owns the timer, respawns the lock screen, suppresses the shell |
| `TimeLock.Client.exe` | the user session | the lock screen and input blocking |

### Batch files must have CRLF line endings

`build.cmd` is parsed by `cmd.exe`, which silently misbehaves on LF-only files —
it fails with a baffling `'/nologo' is not recognized as an internal or
external command` because variable expansion silently fails. `.gitattributes`
enforces this, but if you edit the file in an editor that strips CRs, check it
before opening a PR.

## Test on Windows

CI can compile, but nothing meaningful about this project can be tested without
a real interactive session. Please do these before opening a PR:

1. **Kill the lock screen mid-lock.** `taskkill /f /im TimeLock.Client.exe`.
   The enforcer must bring it back within about a minute.
2. **Kill the enforcer mid-lock** too. The one-minute trigger must restart it.
3. **Reboot during a lock.** The remaining time must be unchanged, not reset.
4. **Let a lock expire.** The desktop, taskbar and `Winlogon\AutoRestartShell`
   must all come back on their own.
5. **Try to escape.** `Win+Ctrl+D`, `Win+Ctrl+Left/Right`, `Alt+Tab`,
   `Ctrl+Shift+Esc`, taskbar right-click. None should work.

## Invariants

These are load-bearing. A change that breaks one of them is a bug even if it
looks like an improvement:

- **The lock must survive a reboot.** The countdown is an absolute UTC end
  time (`EndUtcTicks`) precisely so that restarting cannot pause or extend it.
- **The lock must always end.** Nothing may hold the machine past
  `EndUtcTicks`, even if every lock screen is dead.
- **The machine must always come back.** If `AutoRestartShell` was changed, it
  must be restored; if explorer is missing with no active lock, it must be
  relaunched.
- **Fail open, never closed.** A missing, empty or corrupt state file must mean
  *unlocked*. It is a focus tool, and it must never be able to brick a machine.
- **Normal users must not be able to write the state file.** The enforcer runs
  as `SYSTEM` precisely because the lock client does not.

## Style

Match what is already there. The code targets C# 5 and .NET Framework 4.x
because that is what `csc.exe` supports out of the box — no `?.`, no
`$"interpolation"`, no expression-bodied members.

Comments should explain **why**, especially in `Common.cs` where most of the
subtle reasoning lives.

## Reporting security issues

Please don't open a public issue for something that could be used to defeat the
lock on someone else's machine. See [SECURITY.md](SECURITY.md).

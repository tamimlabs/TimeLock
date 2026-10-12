# Security Policy

## Scope of this tool

TimeLock is a **self-imposed focus tool**. The person it locks is the person who
installed it, and that person is a Windows administrator.

This matters for how you should read security reports about it. The install
registers a scheduled task running as `SYSTEM`, so bugs in TimeLock *can* be a
genuine local privilege escalation vector. Those are in scope and worth fixing.

What is **not** a vulnerability, and is documented in the README:

- An administrator stopping or uninstalling TimeLock.
- Defeating a lock from the Windows Recovery Environment or another boot medium.
- `Ctrl+Alt+Del`, Safe Mode, or a forced power off.
- Using a second Windows account on the machine.

These are Windows platform limitations, not defects.

## Reporting a vulnerability

Please **do not open a public issue**.

Use GitHub's private reporting: *Security* → *Report a vulnerability* on this
repository. If that is unavailable, open a private advisory instead.

Please include:

- The Windows version and TimeLock version/commit.
- What you expected versus what happened.
- Reproduction steps, ideally on a throwaway VM.
- Whether it needs administrator rights (it normally does, since the task
  registers as `SYSTEM`).

### What is most useful to report

In rough priority order:

1. **The lock ending early**, or not at all when it should.
2. **A local privilege escalation** — anything letting a non-administrator
   write `state.json`, disable the scheduled task, or influence what runs as
   `SYSTEM`.
3. **The machine being left without a desktop** — explorer gone and
   `AutoRestartShell` still `0` after a lock ends.
4. **The lock not surviving a reboot.**
5. **Input reaching through the lock screen** in a way a person could actually
   exploit (not an elevated process injecting synthetic keystrokes; see the
   README).

## Out of scope

- Weaknesses in Windows itself.
- Defeating the lock as the administrator who installed it. If you want that
  guarantee, use Windows 11 Pro's Assigned Access, which the OS enforces.

## Supported versions

Only the latest commit on `main` is supported. This is a small project without
release branches; fix the tip and move on.

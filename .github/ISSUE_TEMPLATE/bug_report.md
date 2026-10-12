name: Bug report
description: Something in TimeLock does not work as documented
labels: ["bug"]
body:
  - type: markdown
    attributes:
      value: |
        Thanks for taking the time to report this.

        **Please read "What it does *not* stop" in the README first.** Several
        very common "bugs" are documented, intended limitations -- Ctrl+Alt+Del,
        Safe Mode, and holding the power button cannot be blocked by any
        user-mode application on Windows.

  - type: textarea
    id: what-happened
    attributes:
      label: What happened
      description: What did you see, and what did you expect instead?
    validations:
      required: true

  - type: textarea
    id: steps
    attributes:
      label: Steps to reproduce
      placeholder: |
        1. Installed with install\Install.ps1
        2. Opened TimeLock, set 5 minutes, pressed Start lock
        3. ...
    validations:
      required: true

  - type: dropdown
    id: area
    attributes:
      label: Which part is involved?
      options:
        - Install / uninstall
        - TimeLock window (setting a duration)
        - Lock screen not appearing
        - Lock screen input still getting through
        - Lock not surviving a restart
        - Lock not ending on time
        - Desktop / taskbar missing after unlocking
        - Something else
    validations:
      required: true

  - type: input
    id: windows
    attributes:
      label: Windows version and edition
      placeholder: "Windows 11 Home Single Language, build 26220"
    validations:
      required: true

  - type: input
    id: power
    attributes:
      label: Scaled display?
      description: TimeLock and Windows both use per-monitor DPI scaling.
    validations:
      required: false

  - type: textarea
    id: logs
    attributes:
      label: Relevant log output
      description: |
        `C:\ProgramData\TimeLock\enforcer.log` and
        `%LOCALAPPDATA%\TimeLock\client.log` are the two places worth looking.
        Also useful: `TimeLock.Enforce.exe --status` from an admin prompt.
      render: shell
    validations:
      required: false

  - type: checkboxes
    id: checks
    attributes:
      label: Before submitting
      options:
        - label: I searched existing issues for this
          required: true
        - label: This is not one of the documented limitations
          required: true

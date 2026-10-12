name: Pull request
description: Propose a change to TimeLock
labels: ["pull request"]
body:
  - type: markdown
    attributes:
      value: |
        Thanks for contributing.

  - type: textarea
    id: summary
    attributes:
      label: What does this change?
    validations:
      required: true

  - type: dropdown
    id: area
    attributes:
      label: Which part is involved?
      options:
        - Lock screen / input blocking
        - Enforcer (scheduled task, shell suppression)
        - TimeLock window
        - Installer / uninstaller
        - Build / CI
        - Docs
    validations:
      required: true

  - type: textarea
    id: testing
    attributes:
      label: How did you verify it?
      description: |
        On Windows, try to break it before submitting. Useful checks: kill
        `TimeLock.Client.exe` mid-lock and confirm it comes back; confirm the
        desktop and taskbar return after the timer expires; reboot during a
        lock and confirm the remaining time is unchanged.
    validations:
      required: true

  - type: checkboxes
    id: checks
    attributes:
      label: Checklist
      options:
        - label: build.cmd succeeds with no errors
          required: true
        - label: Tested on Windows (state which version)
          required: true
        - label: The lock still ends on time if every lock screen is killed
          required: true
        - label: The desktop and taskbar are restored after unlocking
          required: true

name: Feature request
description: Suggest an improvement to TimeLock
labels: ["enhancement"]
body:
  - type: textarea
    id: problem
    attributes:
      label: What problem would this solve?
      description: Describe the situation you are in rather than the solution you have in mind.
    validations:
      required: true

  - type: textarea
    id: proposal
    attributes:
      label: What would you like to happen?
    validations:
      required: true

  - type: dropdown
    id: scope
    attributes:
      label: Rough scope
      options:
        - Small -- contained within existing files
        - Medium -- new component or a new enforcement layer
        - Large -- changes the security model
        - Not sure
    validations:
      required: true

  - type: textarea
    id: constraints
    attributes:
      label: Anything that must stay true?
      description: |
        Worth stating up front, because these are the load-bearing properties:
        the lock must survive a reboot, must end on time even if every lock
        screen is killed, and must never leave the machine without a desktop.

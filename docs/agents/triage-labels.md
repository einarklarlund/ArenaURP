# Triage Labels

The `triage` skill speaks in five canonical triage roles. This file maps those roles to the actual GitHub label strings used in `einarklarlund/ArenaURP`. Defaults kept (label string = role name).

| Role (mattpocock/skills) | Label in our tracker | Meaning                                  |
| ------------------------ | -------------------- | ---------------------------------------- |
| `needs-triage`           | `needs-triage`       | Maintainer needs to evaluate this issue  |
| `needs-info`             | `needs-info`         | Waiting on reporter for more information |
| `ready-for-agent`        | `ready-for-agent`    | Fully specified, ready for an AFK agent  |
| `ready-for-human`        | `ready-for-human`    | Requires human implementation            |
| `wontfix`                | `wontfix`            | Will not be actioned                     |

When a skill mentions a role (e.g. "apply the AFK-ready triage label"), use the corresponding label string from this table.

These labels don't exist on the GitHub repo yet — `gh label create <name>` them (or let `gh issue edit --add-label` create-on-first-use if the repo allows it). Edit the right-hand column to match any different vocabulary you adopt.

# AI Communication — Monitor Flipper

Purpose: handoff notes between agents, and the two-file interface to Command Center.

**Claude works autonomously — no external verification gate.** Codex is **advisory
only and non-blocking**: it reviews after the fact when the owner asks for a second set
of eyes. The owner is the final approver.

## Files
| File | Direction | What it is |
|---|---|---|
| `TO_CLAUDE.md` | → Claude | Conventions + at most a task or two the roadmap/backlog can't express. Not the live work state. |
| `TO_CODEX.md` | → Codex | Advisory review requests. Optional. |
| `TO_COMMAND.md` | project → Command | **Status card.** Frontmatter + ≤10-line body. Regenerated wholesale at EVERY closeout (`closer.md` 6b). Command reads it, never writes it. Not a log. |
| `FROM_COMMAND.md` | Command → project | **Inbound pop-queue.** The ONLY channel for items born outside a project session. Startup lands each item canonically, then deletes it. |

There is no `FROM_CODEX.md`; review results come back in the Codex conversation
and are acted on by the writer.

## Queue hygiene
- `TO_CLAUDE.md`, `TO_CODEX.md`, `FROM_COMMAND.md` hold **active items only** —
  delete an item the moment it lands (they rot silently otherwise). If a queue is
  empty, leave an explicit "(none)" / "No active …" note, not history.
- Newest items at the top.
- `TO_COMMAND.md` is NOT a queue: overwritten wholesale at every closeout. Its
  Rules block, the 10-line body cap, and the no-personal-data / no-credentials rule stay intact.
- **Never process a `FROM_COMMAND` item blindly.** If it conflicts with `CLAUDE.md`
  or the canonical, leave it marked ⚠ with a one-line reason and raise it with the owner.

## Screenshots / images
If the owner refers to screenshots they didn't attach, check the project's `Pictures/`
folder first — the default drop location for local images used in review.

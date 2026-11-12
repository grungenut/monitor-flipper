# Monitor Flipper Documentation Index

Annotated map of the active `README/` docs. Each line says what the doc is **for**
so it can be found without opening it.

> Maintenance: when a doc is added, retired, or changes purpose, update its line
> here (`closer.md` step 5). Session summaries are **not** listed individually.

## Start Here (read in this order each session)
- `README/Planning/ROADMAP__FLIPPER.md` — phases, ordered; the live "where are we" anchor.
- `README/Sessions/CURRENT__FLIPPER.md` — running log; newest session on top; open threads.
- `README/Planning/BACKLOG__FLIPPER.md` — slim, open-only item list (numbers preserved).
- `README/AI_Communication/FROM_COMMAND.md` — inbound items from outside a session (usually empty).

Read when relevant:
- `README/Canonical/ARCHITECTURE__FLIPPER.md` — single source of truth: architecture, invariants, version history. Wins on any conflict.
- `README/Planning/DECISIONS__FLIPPER.md` — settled decisions + conventions; do not relitigate.

## AI Coordination (`AI_Communication/`)
- `README.md` — the handoff protocol and queue hygiene.
- `TO_CLAUDE.md` — parked implementation tasks for Claude (active only).
- `TO_CODEX.md` — advisory review requests for Codex (active only; non-blocking).
- `TO_COMMAND.md` — outward status card for Command Center: frontmatter + ≤10-line body, regenerated wholesale at every closeout (`closer.md` 6b). No personal data, no credentials.
- `FROM_COMMAND.md` — inbound pop-queue: the ONLY channel for items originating outside a project session; startup lands each item canonically, then deletes it.

## Planning (`Planning/`)
- `ROADMAP__FLIPPER.md` — phasing (also listed above).
- `BACKLOG__FLIPPER.md` — open-only backlog (also listed above).
- `BACKLOG_ARCHIVE__FLIPPER.md` — resolved items, moved here whole, newest first, forensic notes kept.
- `DECISIONS__FLIPPER.md` — decisions, conventions, pitfalls learned the hard way.
  **§0 is the drain rule** — read it before deciding this file has got too long.
- `DECISIONS_ARCHIVE__FLIPPER.md` — closed subjects, moved here whole and verbatim when
  the subject ends; still binding if it reopens.

## Sessions (`Sessions/`)
- `CURRENT__FLIPPER.md` — **always start here**; ~8 newest sessions, prepended (`closer.md` step 2).
- `CURRENT_ARCHIVE__FLIPPER.md` — older CURRENT sections, moved here verbatim; newest first.
- `YYYY_MM/SESSION_SUMMARY_YYYY_MM_DD[_x].md` — one file per session, dated, in a folder per month; same-day sessions get `_b`, `_c` …. To find recent work, open the newest month folder and sort descending.

## Canonical (`Canonical/`)
- `ARCHITECTURE__FLIPPER.md` — the architecture/invariants document (also listed above).

<!-- Add project sections as they appear: Implementation/, Analysis/, Testing/,
Bugfixes/, Archive/ (historical — not enumerated; search it directly). -->

# AGENTS.md — Monitor Flipper multi-agent protocol

Tool-neutral entry point. Claude Code reads `CLAUDE.md` automatically; every other
agent (Codex, etc.) starts here. **`CLAUDE.md` holds the full project rules — read
it first**, then this file for roles and output shape.

## Read Order (required)
1. `CLAUDE.md` (full rules + read order for the live work state)
2. `README/Canonical/ARCHITECTURE__FLIPPER.md`
3. `README/AI_Communication/README.md`
4. `README/Sessions/CURRENT__FLIPPER.md`
5. `closer.md` (at session end)

Role-specific handoff file: Codex → `README/AI_Communication/TO_CODEX.md`;
Claude → `README/AI_Communication/TO_CLAUDE.md`.

If a required file is missing, stop and report it under **Findings** — do not
recreate it (it may simply not have synced yet).

## Roles
1. **Claude Code = Writer.** Implements with minimal, in-place edits; updates the
   session docs; works autonomously — no external verification gate.
2. **Codex = Advisory Reviewer (on request).** Non-blocking. Reviews after the fact
   when the owner asks for a second look, typically on high-risk changes. Requests go
   in `TO_CODEX.md`. There is no `FROM_CODEX.md`.
3. **The owner = Final Approver.** Tests for real and reports back. Their sign-off is the
   one that counts.

## Non-Negotiables
See `CLAUDE.md` → Absolute Rules. If a task conflicts with one, do not implement
it — escalate under **Findings**.

## Change Rules
- Minimal diffs; strictly scoped to the request; no drive-by refactors.
- No new dependencies unless explicitly approved.
- Preserve behavior outside the target; prefer deterministic, idempotent behavior.
- If refactoring is unavoidable, isolate it and justify it in Findings.

## Required Output Format (reviews)
### 1) Verdict — `APPROVE` · `NEEDS FIXES` · `BLOCKED` (missing context / invariant conflict)
### 2) Findings — ordered by severity (Critical / High / Medium / Low), with file + function (+ line) references; call out regressions, invariant violations, drift from canonical, missing tests
### 3) Fix Plan — minimal exact edits
### 4) Tests — the smallest set that proves the task works and nothing regressed

## Standing instructions from the owner
If the owner says **how** they want something done — how to work, how to report — that is a
rule to be written down, not a task to be done and forgotten. `CLAUDE.md` →
**"Maintaining this file"** says which of the three layers it belongs in (personal /
project / decision) and how to record it.

## Communication artifacts
- `README/Sessions/CURRENT__FLIPPER.md` — current task + constraints
- `README/AI_Communication/TO_CLAUDE.md` / `TO_CODEX.md` — active handoffs only
- `README/AI_Communication/TO_COMMAND.md` / `FROM_COMMAND.md` — the Command Center
  interface (see its README); not for agent-to-agent handoff

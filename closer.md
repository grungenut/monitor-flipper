# Session Closeout — Monitor Flipper

Run this **only when the owner says to close the session.** Finishing a task is not the
trigger; if a closeout seems due, offer it in one line and wait. Do ALL steps.

0) **Version / integrity** (project-specific — edit this line):
- If app code changed, bump the version where this project keeps it, and run the
  project's own check (test suite, `check` command, syntax compile). Note the result.
- Docs-only session → skip the bump, still run any cheap check.

1) **Write the session summary:**
- Create `README/Sessions/YYYY_MM/SESSION_SUMMARY_YYYY_MM_DD.md` (folder per month;
  create it for the first session of a new month). Several sessions in one day:
  add `_b`, `_c` … — list the folder first and take the next free letter.
- Include: focus / what changed / files touched / tests run / issues & risks /
  decisions / next steps. Use `SESSION_SUMMARY_TEMPLATE.md` if it is still present.

2) **Update the current pointer — PREPEND, never overwrite:**
- `README/Sessions/CURRENT__FLIPPER.md` is a **running log**. Add a new dated section at the
  **top** (5–10 bullet highlights + a pointer to the summary file); older sections
  stay below. **Do NOT overwrite it.**
- Keep it to **roughly the 8 newest sessions.** When prepending pushes it past
  that, move the oldest section(s) **verbatim** to the top of
  `README/Sessions/CURRENT_ARCHIVE__FLIPPER.md`. Never summarize or drop them.

3) **Canonical, if needed:**
- Architecture, invariants, workflows, or critical behavior changed → update
  `README/Canonical/ARCHITECTURE__FLIPPER.md` (and its version/date line).
- Otherwise leave it alone and say "canonical unchanged".

4) **Planning docs, if any item moved:**
- `BACKLOG__FLIPPER.md`: add / update rows for anything added, started, or changed. When an
  item is **fully resolved** (not merely "built, live-test owed"), move its whole
  row into `BACKLOG_ARCHIVE__FLIPPER.md` (newest first), number and notes preserved.
- `ROADMAP__FLIPPER.md`: update whenever the phase picture or the "recommended next move"
  shifted. It is the session-start anchor — keep it true or it stops being useful.
- `DECISIONS__FLIPPER.md`: add any decision settled this session (one line + date + why).
  **Then drain it** (`DECISIONS__FLIPPER.md` §0): does anything settled today *contradict* a line already there — if so, edit
  that line in place with `(supersedes <date>)` rather than adding a second one. Has a
  decision become structural enough to graduate to `Canonical/ARCHITECTURE__FLIPPER.md`,
  leaving a pointer? Has a whole subject closed — feature shipped, subsystem retired — so
  its `###` section moves verbatim to `DECISIONS_ARCHIVE__FLIPPER.md`?
  **Never delete a decision to save space: the risk is staleness, not length, and a
  deleted decision is a relitigable one.**
- Nothing moved → leave all four alone.

4b) **Did the owner say how they want something done?**
- Not *what* to build — **how to work or how to report.** That is a rule, and a rule left
  in the conversation is gone at the next launch. Offer to record it; name the file.
- **Which layer** (`CLAUDE.md` → "Maintaining this file"): would it be just as true on a
  different project? → personal, `~/.claude/CLAUDE.md`. Wrong anywhere
  else? → this project's `CLAUDE.md`, in the section it belongs to. The answer to a
  question rather than an instruction? → `DECISIONS__FLIPPER.md`, where the drain can
  supersede it later.
- Then note in `DECISIONS__FLIPPER.md` **that** the rule was added and why they asked — the
  rule lives in `CLAUDE.md`, the reason it exists lives in DECISIONS.
- ⚠ **Never write to `~/.claude/` without asking first**, and say out loud
  that it applies to every project on this machine and **does not sync to the other one.**
- Nothing like that came up → skip this step; do not invent a rule to have one.

5) **Docs index:** a `README/` doc was added, retired, moved, or changed purpose →
   update its line in `README/INDEX__FLIPPER.md`. Session summaries are not listed
   individually.

6) **AI handoff, if relevant:** update `TO_CLAUDE.md` / `TO_CODEX.md` for the next
   agent. Active items only — delete anything that landed.

6b) **Regenerate the Command status card — UNCONDITIONAL:**
- Rewrite `README/AI_Communication/TO_COMMAND.md` at EVERY closeout, docs-only
  included: refresh the frontmatter (`status`, `updated`, `needs_attention`,
  `summary`, plus the project's version fields) and the status body (**10 lines
  or fewer**). Latest state wins — it is not a log, and it never enumerates the
  backlog (point at `BACKLOG__FLIPPER.md`). Keep its Rules block intact. **No personal data, no
  credentials.**
- Glance at `FROM_COMMAND.md`: anything this session landed should already be
  deleted; if one lingers, delete it now.

7) **Git checkpoint** (if the project is a repo):
- Default: **commit** at closeout — just do it. Stop to ask only if the owner should
  *defer* (half-finished, risky) or *push* (needs to reach GitHub / another
  machine now). Present the choice as numbered options.
- `git status` first; confirm no accidental deletions, only intended files changed.

8) **Final confirmation** — print:
```
[✓] Integrity / version: <result, or "docs only">
[✓] README/Sessions/YYYY_MM/SESSION_SUMMARY_<date>.md written
[✓] CURRENT__FLIPPER.md prepended (archived: <n sections / none>)
[✓] Canonical: updated / unchanged
[✓] Planning: BACKLOG / ARCHIVE / ROADMAP / DECISIONS — <which changed> (DECISIONS drained: yes/n-a)
[✓] Standing instructions: <rule recorded in CLAUDE.md / personal / none this session>
[✓] INDEX__FLIPPER.md: updated / unchanged
[✓] TO_COMMAND.md regenerated · FROM_COMMAND.md clean
[✓] Git: committed / pushed / deferred / not a repo
```

**Command Center:** write nothing in `00 - Command Center/` from here. If this
session produced something Command must know beyond what `TO_COMMAND.md` says
(the project moved, its purpose changed, a Command-level decision), tell the owner in
one line; they decide whether to also close a Command session.

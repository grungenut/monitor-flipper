# Claude Code Instructions (Monitor Flipper)

### Read first each session — "where are we / what's next"
1. `README/Planning/ROADMAP__FLIPPER.md` — **the live anchor.** Phases, ordered, with the
   recommended next move. Read this first.
2. `README/Sessions/CURRENT__FLIPPER.md` — what happened last session (running log; newest
   on top). Read the top block, then only as far back as you need.
3. `README/Planning/BACKLOG__FLIPPER.md` — the slim, **open-only** item list.
4. `README/AI_Communication/FROM_COMMAND.md` — the **inbound queue** for items sent
   from outside a project session (Command Center or the owner relaying). Usually
   empty. If it holds open items: dedupe against the backlog, land each one in its
   canonical place (backlog / roadmap / docs), then **delete it from the file** — it
   is a pop queue, not a log. Never process an item that conflicts with this file
   or the canonical doc; mark it ⚠ with a one-line reason and raise it with the owner.

Do **not** re-derive anything already recorded in those files. If you are guessing
at project state, you have not read them yet.

### Read when relevant (not every session)
- `README/Canonical/ARCHITECTURE__FLIPPER.md` — architecture + invariants, single
  source of truth. Read before touching architecture or invariants; it wins on any
  conflict.
- `README/Planning/DECISIONS__FLIPPER.md` — settled decisions and conventions. Read before
  proposing a change to anything that looks deliberate; **do not relitigate.**
- `README/Planning/BACKLOG_ARCHIVE__FLIPPER.md` — resolved-item history. Open only for a
  closed item's backstory; never scan it at session start.
- `README/INDEX__FLIPPER.md` — annotated map of every doc; use it to find things.
- `README/AI_Communication/README.md`, `TO_CLAUDE.md`, `TO_CODEX.md` — handoff
  protocol and any specific parked task the roadmap/backlog can't express.
- `closer.md` — closeout checklist. Read at session **end**, when the owner says to close.

## Project Identity
Monitor Flipper — a small Windows program, like UltraMon's mirror feature, that shows one
monitor's picture on another monitor (or the same one, or a window) and can **flip it
horizontally and/or vertically**. Aimed especially at **teleprompters** (also projectors and reflected displays).
Defined by the owner 2026-11-11. **Open source, public GitHub** (decided 2026-11-12). Scope is
mirroring and flipping only.

- **GitHub:** public repo `grungenut/monitor-flipper`. **The whole venture folder is the
  repo**, framework included. Launch at this folder.
- Solo Venture: standard backlog, not GitHub mode, unless the owner decides otherwise.
- Code, when there is some, goes in `Project/`.

## The Human
The owner directs the AI rather than writing code by hand.
- Make edits **in place** with the Edit/Write tools; they cannot apply diffs or
  partial snippets.
- Explain tradeoffs in plain English before jargon; sanity-check big decisions
  with them rather than assuming.
- Don't assume an idea they are exploring is a decision. Once decided, move to doing
  it instead of reopening it.

## Absolute Rules
<!-- Keep only rules that are TRUE for this project. A rule that doesn't apply
teaches the AI to ignore rules. Typical sections: -->

### Change Philosophy
- Surgical, minimal changes only. Never refactor broadly unless explicitly asked.
- Preserve working conventions instead of redesigning them without need.
- No new dependencies without explicit approval.

### Non-Negotiables
<!-- None yet. Name each one as the project earns it. -->

### Security / privacy
- No personal details, credentials, keys, or tokens in any `README/` doc,
  session summary, or interface file. Describe work, not data.
- The repo is public: never commit secrets or personal details — env files and keys are git-ignored.

## Session Protocol

### At Session Start
- Read the four files in the order at the top of this file.
- Process `FROM_COMMAND.md` items into canonical state (then delete them) before
  starting new work.
- Open the canonical doc only when the work touches architecture or invariants.

### At Session End — only when the owner says to close
Follow `closer.md`. In short: integrity check → session summary → **prepend**
`CURRENT__FLIPPER.md` → canonical if changed → backlog / archive / roadmap / decisions →
INDEX → handoff queues → **regenerate `TO_COMMAND.md`** → git checkpoint.
Finishing a task is not the trigger; if a closeout seems due, offer it in one line
and wait.

## Maintaining this file — when the owner tells you how to work

A standing instruction is not a task: write it down or it is gone at the next launch. When
The owner says *how* they want something done — how to work or how to report, not what to do —
offer in one line to record it, and name the file:

- **True in every project** (tone, length, report shape) → their personal file,
  `~/.claude/CLAUDE.md` in the user profile of the machine you are on. It already loads in
  every session. **Ask before writing to it**; each machine has its own copy and they do
  not sync.
- **True only in this project** → this file, in the section it belongs to. Delete rules
  here that stop being true — a dead rule teaches the next session that none are binding.
- **The answer to a question, not an instruction** → `README/Planning/DECISIONS__FLIPPER.md`, where the drain in
  `closer.md` step 4 can retire it when it goes stale.

## Testing Reality
Build: `Project/build.cmd` (uses the C# compiler built into Windows; no installs).
- **AI can verify:** it compiles, launches, the Ctrl+Alt+F hotkey starts/stops, CPU load,
  click-through, and flip accuracy via `bin\MonitorFlipper.exe --selftest <result.txt>`
  (renders all 4 flip modes and 3 rotations on the GPU and compares every pixel; saves no
  images), and window capture via `--selftest-window <hwnd> <result.txt>` (frame count only).
- **Only the owner can verify:** what the output looks like. The output window is deliberately
  hidden from screenshots/screen capture (so it never captures itself), so the AI cannot
  see it. Don't take full-screen screenshots to check — the owner's screen shows private data.

## What Not To Do
- Keep the folder path free of apostrophes and `[` `]` — they break `start_ai.ps1` and the
  Warp card.
- Don't widen the product beyond mirror + flip — the owner declined the other UltraMon features.
- Anything that goes in the public repo must hold no personal details or private notes.

## Output Format
1. Summary of changes (list touched files)
2. How to test / what the owner should check

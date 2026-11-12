# Monitor Flipper — Decisions, Conventions & Pitfalls

Durable reference. Settled decisions here are **not to be relitigated** — to change
one, raise it explicitly with the owner first. Add a line whenever a session settles
something (`closer.md` step 4): what was decided, the date, and **why** (the why is
what stops it being re-argued).

## 0. Keeping this file from rotting

This file is append-heavy by nature, so it needs draining or it becomes a wall nobody
reads. **Four drains, in order of how often they apply. None of them is "trim for length."**

1. **Supersede in place — never append a correction.** A decision that gets revised is
   **edited on its own line**, with `(supersedes YYYY-MM-DD)` and the new reason. Two
   lines about one question is how this file gets long *and* ambiguous at the same time.
2. **Graduate structural decisions to `Canonical/ARCHITECTURE__FLIPPER.md`.** Once a
   choice has stopped being "what we decided" and become "how this thing is built," it
   belongs in Canonical. Leave a one-line pointer here, not a copy.
3. **Archive by subject when the subject closes.** When a body of work ends — a feature
   ships, a subsystem is retired, a migration completes — move that whole `###` section
   **verbatim** into `DECISIONS_ARCHIVE__FLIPPER.md`, newest first. Move it whole; never
   summarize. Archived decisions remain binding if the subject ever reopens, which is
   exactly why they are moved rather than deleted.
4. **Read by section, not front to back.** Group decisions by subject precisely so a
   session working on one subsystem never reads another's rules. Prefer a new section over
   growing one list — **this file's usable length is its longest *section*, not its total.**

**Never delete a decision to save space.** A deleted decision is a relitigable decision,
and the cost of re-arguing a settled question is the whole reason this file exists.

**The real risk is staleness, not length.** A long file is mildly annoying; one stale rule
stated as binding is actively harmful, because it carries authority and is marked
do-not-relitigate, so a later session obeys it without re-checking. At closeout the
question is *"does anything settled today contradict a line already here"* — never *"is
this file getting long."*

## 1. Decisions (do not relitigate)

<!-- Group by subject with ### headings once there is more than one subject — that is
drain 4 doing its work. A single flat list is fine while the file is young. -->

- 2026-11-11 — **Draw on the GPU** (Windows Desktop Duplication + Direct3D 11), not GDI screen copies — *why:* the owner's call; GDI cost ~40% of a CPU core at 30 fps, GPU ~2% and syncs to the monitor's refresh.
- 2026-11-11 — **C# built with the compiler inside Windows, no installed SDKs or libraries**; Direct3D called through raw COM vtables (`Project/src/Gpu.cs`) — *why:* nothing to install; "no new dependencies" rule.
- 2026-11-12 — **Scope is mirroring and flipping only**; no other UltraMon features — *why:* the owner's call.
- 2026-11-12 — **Open source on a public GitHub**, marketed especially for **teleprompters** — *why:* the owner's call.
- 2026-11-12 — **MIT license; copyright "Monitor Flipper contributors"; commits under the GitHub private (noreply) email; no personal details in the repo** — *why:* owner's call when going public.
- 2026-11-12 — **Code signing via SignPath Foundation (free OSS)**, not a paid certificate — *why:* owner's call; Inno Setup does not avoid SmartScreen, only signing plus download reputation does.
- 2026-11-12 — Ships as **one standalone exe** (~64 KB, needs only the .NET 4.8 built into Windows 10/11) — *why:* no installer for users.
- 2026-11-11 — The output window is hidden from screen capture — *why:* so the mirror never copies itself (needed for same-monitor mode). Side effect: screenshots/screen sharing won't show it.

## 2. Conventions
- <naming, IDs, file placement, commit style, how the owner wants options presented>

## 3. Working method
- The owner directs; the AI edits in place; the owner verifies live and reports back.
- <anything specific to how this project is built — e.g. which model does what>

## 4. Pitfalls (learned the hard way)
- 2026-11-11 — A full-screen test screenshot caught private browser data. Rule: never take full-screen screenshots; verify with `MonitorFlipper.exe --selftest <file>` (pixel-by-pixel flip check, saves no images).
- GDI's mirrored StretchBlt needs start point x+w-1, not x+w (one-pixel shift otherwise). Moot now that drawing is on the GPU.

## 5. Folder map (canonical locations)
- <where the code, data, docs, and generated output live; what must never be edited>

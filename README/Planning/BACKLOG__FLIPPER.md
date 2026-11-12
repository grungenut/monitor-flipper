# Monitor Flipper Backlog (Active)

Living list of **open work only**. When an item is fully resolved, its whole row
**moves** to `BACKLOG_ARCHIVE__FLIPPER.md`, number and notes preserved (`closer.md` step 4).
Phasing and ordering live in `ROADMAP__FLIPPER.md`.

- **Last reconciled:** 2026-11-11
- **Item numbers are assigned once and never reused**, so references stay valid.
- New work from outside a session arrives via `AI_Communication/FROM_COMMAND.md`
  and is landed here (then deleted from the queue).

**Legend:** `OPEN` = not started / in progress · `NEEDS-TEST` = built, awaiting
The owner's live confirmation · `DEFERRED` = intentionally parked.

| # | Item | Status | Notes |
|---|---|---|---|
| 1 | v1 mirror + flip on a real second monitor | NEEDS-TEST | Built 2026-11-11, moved to GPU same day. Self-test: all 4 flip modes pixel-exact. Window and same-monitor (click-through) modes run. The owner checks picture, pointer, smoothness on a real 2nd screen. |
| 2 | GPU drawing (lower CPU, smoother) | NEEDS-TEST | Done 2026-11-11: ~2% of one core, synced to the monitor refresh. Confirmed with #1. |
| 3 | Tray icon (run in background) | NEEDS-TEST | Done 2026-11-11: minimize goes to tray; tray menu has show / start-stop / flip H / flip V / exit. |
| 4 | Start with Windows / start mirroring automatically | NEEDS-TEST | Done 2026-11-11: two checkboxes. Startup entry launches hidden in the tray. Tested tray start + auto-mirror; the Windows startup entry itself not yet exercised. |
| 5 | Rotated (portrait) source monitor | NEEDS-TEST | Done 2026-11-11: turned upright on the GPU. Self-test confirms the math matches Microsoft's Desktop Duplication sample; needs one real check on a turned monitor. |
| 6 | "Inverting" pointer pixels (text I-beam) | NEEDS-TEST | Done 2026-11-11: now truly inverts the picture underneath, like the real pointer. |
| 7 | Mirror to several monitors at once (UltraMon parity) | NEEDS-TEST | Done 2026-11-11: "Show on" is a tick list. Tested: same monitor + window together at 60 fps. |
| 8 | Mirror a single program window (UltraMon parity) | NEEDS-TEST | Done 2026-11-11 via Windows Graphics Capture; works when covered; includes pointer. Capture self-test passed; full live run not yet. |
| 10 | Go public: open-source GitHub repo + Release with the exe | OPEN | 2026-11-12: personal details scrubbed, history squashed to one commit under the GitHub private email, MIT license, README at repo root, GitHub Actions builds the exe and publishes a Release on a `v*` tag. |
| 11 | Code signing through SignPath Foundation (free for open source) | OPEN | Owner applies at signpath.org (needs their contact details). Code signing policy already in README. After approval, add the SignPath step to `.github/workflows/build.yml`. Signing alone does not remove the warning at once; reputation builds with downloads. |

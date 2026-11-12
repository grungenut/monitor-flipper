# Monitor Flipper Canonical Documentation
**Version:** 0.1.0 · **Date:** 2026-11-10 · **Status:** <one line: what is built / what stage>

_Single source of truth for architecture, invariants, and critical behavior. Read
before touching any of those; **this file wins on any conflict** with session notes,
backlog entries, or chat. Update it (and the version line) whenever architecture or
an invariant changes — `closer.md` step 3. Rename this file to
`ARCHITECTURE__FLIPPER.md`._

---

## 1. Project Overview
What it is, who uses it, what problem it solves, what it replaces. Three to six
sentences. (Developer context: the owner directs the AI; code is edited in place.)

## 2. Architecture
- Stack and where the code lives (`Project/`, `app/`, …)
- Components and how they talk (a short diagram or list)
- Data: what is stored where, formats, what is the source of truth for data
- External systems it connects to

## 3. Invariants — do not break
Numbered. Each is something that must stay true regardless of the task (security,
compliance, a frozen engine, an idempotency guarantee, a naming convention
everything depends on). If a task conflicts with one, stop and raise it.

1. …
2. …

## 4. Core Workflows
The handful of flows that define the product, each as a few steps. Enough that a
new session can reason about a change's blast radius.

## 5. Conventions
Naming, IDs, file layout rules, how versions are numbered, where generated output
goes. (Decisions and their *why* live in `Planning/DECISIONS__FLIPPER.md`; this is the
resulting rule.)

## 6. Environment & Operations
How to run it, how to test it, how it is deployed/installed, what the owner does vs
what the AI can do.

## 7. Version History
Newest first. One line per meaningful change to this document.

- 0.1.0 (2026-11-10) — first canonical written from the template.

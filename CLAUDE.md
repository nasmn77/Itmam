# CLAUDE.md — Itmam (إتمام)

Arabic as-you-type word prediction for the physical keyboard on Windows, shipped on the Microsoft Store as MSIX.
`PROMPT.md` is the full brief and the source of truth; this file is the short rule set. Read both at the start of a session.

## Current status
- **Current phase:** 0 — scaffolding (done, awaiting review).
- **Next phase:** 1 — Core (normalization, dictionary builder, lookup, ranking, word-buffer state machine, tests).

## Build & test
```
dotnet build Itmam.CI.slnf -c Release   # must be zero warnings (warnings are errors in Release)
dotnet test --solution Itmam.CI.slnf -c Release
```
Tests run on Microsoft.Testing.Platform (opted in via `global.json` → `test.runner`), not VSTest.
`Itmam.CI.slnf` excludes the MSIX packaging project (`.wapproj` builds only in Visual Studio / MSBuild with the MSIX tooling).

## Tech stack (fixed)
- .NET 10 SDK pinned by `global.json` (`10.0.100`, `latestFeature`). Microsoft packages on 10.x.
- Central Package Management (`Directory.Packages.props`), shared settings in `Directory.Build.props`.
- C# latest, nullable on, implicit usings on, warnings as errors in Release.
- `Itmam.Core` targets `net10.0` (no Windows APIs, 100% unit-testable). Everything else targets `net10.0-windows10.0.19041.0`, min OS 10.0.19041.0.
- WinForms, PerMonitorV2 DPI. P/Invoke via **CsWin32** (`NativeMethods.txt`), not hand-written `DllImport`.
- Tests: xUnit v3 + **Shouldly** (not FluentAssertions: v8+ is commercially licensed).
- Packaging: `.wapproj` → MSIX, `runFullTrust` only. Platforms: **x64 and ARM64 only**.

## Approved dependencies (ask before adding any other)
| Package | Use | License |
|---|---|---|
| Microsoft.Windows.CsWin32 | P/Invoke source generator (build-time) | MIT |
| Microsoft.Data.Sqlite 10.x (bundles SQLitePCLRaw/e_sqlite3) | user word store (Phase 5) | MIT / SQLite public domain |
| xunit.v3 | tests (Microsoft.Testing.Platform runner) | Apache-2.0 |
| Shouldly | test assertions | BSD-3-Clause |

## Decisions made
- User words: embedded SQLite at `%LOCALAPPDATA%\Itmam\user.db`.
- Dictionary sources: **FrequencyWords `ar_full` first (Phase 1)**; Arabic Wikipedia counts added later once everything works. CC-BY-SA on the derived dictionary file is accepted. Verify each license and tell the user **before** downloading.
- Code license: MIT.
- Terminals are excluded by default (user can re-enable in settings).
- Lookup structure: sorted normalized-key array + front coding + varint frequencies (compressed via BCL), not an object trie, to meet the memory target.
- The `.wapproj` is created in Phase 7: the MSIX tooling (DesktopBridge targets) is not installed in Visual Studio yet.

## Privacy rules (hard requirements)
- No network access at runtime: no `internetClient` capability, no telemetry, no update checks, no crash upload.
- Never log typed characters. Debug logs (Debug builds only) contain event types and timings only.
- Learn only word → count/last-used. Never sentences or word order. Never learn in password fields or excluded apps.
- Hook and storage code paths must be documented in `docs/ARCHITECTURE.md`.

## Engineering rules
- Low-level hook callback: filter, translate, enqueue to a `Channel<T>`, decide swallow/pass — nothing else, no allocations, < 1 ms.
- Ignore `LLKHF_INJECTED` events. `ToUnicodeEx` with flag `0x4`.
- Hook/worker exceptions must never crash the app; reinstall the hook if Windows removes it.
- Comments and docs in English; all user-facing strings in `.resx` (Arabic + English). Arabic UI name **إتمام**, English **Itmam**.
- Never fabricate APIs, package names or licenses; say when unsure and propose how to verify.
- Ask before: adding a dependency, choosing/downloading a word-list source, anything touching the network.

## Git rules
- Remote: https://github.com/nasmn77/Itmam (`origin`), default branch `main`.
- One branch per phase (`phase-1-core`, `phase-2-hook`, …), merged into `main` only after the user approves.
- Conventional Commits (`feat(core): …`, `test(core): …`, `fix(win32): …`), small and focused; never commit a broken build.
- **Ask before every `git push`**, tag, release or anything public. Never force-push `main`.
- Never commit certificates (`*.pfx`, private keys), Partner Center secrets, or user data (`user.db`, `user.json`).

## Phase workflow
Phases 0–7 are listed in `PROMPT.md` §14. After each phase: build, run tests, summarize the changes, update the "Current status" and "Decisions made" sections here, then **stop for review**.

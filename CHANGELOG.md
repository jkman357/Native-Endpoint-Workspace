# Changelog

All notable changes to Native Endpoint Workspace are recorded here. The project remains on the `v0.0.1` RC line until explicitly frozen.

## v0.0.1rc12 — Safety, Schema, Diagnostics & Testability Hardening

- changed Workspace shutdown to detach-only; external applications remain open and the Workspace no longer sends `WM_CLOSE`
- made strong process-start identity validation fail closed when bind-time/current process start time cannot be verified
- added destroy-observed tombstoning for the currently bound endpoint instance
- added per-binding runtime instance IDs while keeping all native identity runtime-only
- changed global shortcut registration to an all-or-nothing transaction with rollback to the previous working set
- separated `LayoutSchemaVersion` from application version and introduced schema version 1
- replaced raw `StartsWith("0.0.1")` compatibility with explicit schema validation and exact legacy `0.0.1` / `0.0.1rcN` matching
- added a .NET Framework 4.7.2 Per-Monitor DPI baseline (`app.manifest`, `App.config`) and a DPI-change final resync trigger
- separated async geometry state into last-requested and last-verified rectangles
- filtered hidden endpoints out of the visible Z-order group
- moved WinEvent object-type filtering ahead of managed-handle locking for location/destroy events
- extracted layout topology, layout schema validation, layout version policy, and endpoint identity policy from `MainWindow`
- added a dependency-free policy test project and `test.cmd`
- added privacy-bounded runtime logging with 5 MB rotation, five-file retention, slow-layout warnings, and optional DEBUG commit metrics
- separated `README.md` and `CHANGELOG.md`; README now documents only the current architecture/operation while this file owns release history
- corrected rc11 documentation drift around layout scheduling and lifecycle behavior

## v0.0.1rc11 — Interactive Layout Performance + UI Cleanup

- added throttled interactive layout commits and precise final commits
- separated geometry-dirty and Z-order-dirty work
- cached stable Cell screen rectangles to reduce unnecessary native operations
- renamed `Unbind` to `Detach`
- removed the redundant per-Cell Close button
- combined group minimize/restore into one stateful toolbar control
- compacted the toolbar to icon-oriented actions with tooltips
- moved manual endpoint resync to Advanced settings
- added Ctrl+S / Ctrl+O for Save/Load
- restricted global assignment shortcuts to Ctrl/Alt/Shift combinations and rejected bare F-keys/Win-key combinations

## v0.0.1rc10 — Z-order Stability Fix

- replaced repeated per-endpoint Workspace re-stacking with a single bottom-most managed endpoint anchor
- skipped Z-order changes when all managed endpoints were already above the Workspace
- made the periodic health path detection-only while layout/Z-order were healthy
- reduced mixed-app flicker involving Paint, Firefox, Explorer, and other endpoints

## v0.0.1rc09 — Static Review Cleanup

- clarified layout-commit queue semantics
- centralized application tuning/constants
- made invalid Cell topology fail fast
- prohibited bare global F1-F12 shortcuts
- prepared the codebase for a new full static review

## v0.0.1rc08 — Foreground Group + Size Accommodation

- hardened managed endpoint foreground/Z-order grouping
- added endpoint minimum-size accommodation and Workspace growth within monitor work area
- clarified group minimize/restore no-activation policy
- added validate-before-mutate layout loading and native-operation result reporting

## v0.0.1rc07 — Endpoint Identity & Native Operation Hardening

- strengthened runtime endpoint identity with HWND/PID/TID/process start/window class
- added endpoint destroy observation
- moved geometry requests toward non-blocking cross-process operation
- added bounded layout-correction retry/backoff
- filtered unmanaged WinEvent traffic before WPF Dispatcher work
- replaced ambiguous geometry sync boolean results with explicit result states

## v0.0.1rc06 — Deterministic Native Layout Commit

- replaced timer-based resync guessing with a single WPF-geometry-driven Native Layout Commit path
- recalculated all active Cell screen rectangles and reapplied endpoint geometry deterministically
- explicitly maintained the Workspace below the managed endpoint group
- established the first practical multi-endpoint resize/maximize/restore baseline

## v0.0.1rc05 — Endpoint Resync Experiment

- added automatic endpoint resync attempts after Workspace/Cell layout changes
- introduced move/resize interaction hooks and a manual `Resync Endpoints` recovery action
- retained the non-embedding architecture

## v0.0.1rc04 — Adaptive Split Layout + Endpoint Grouping

- changed Cell layout to constrained splitter-based allocation within fixed Workspace area
- added independent row proportions
- reduced layout-lock feedback-loop risk with suppression/coalescing
- introduced multi-endpoint Z-order grouping behavior

## v0.0.1rc03 — Tiled Layout + Layout Lock

- replaced free-form Cells with a tiled Grid/Splitter model
- added WinEvent-based endpoint snap-back/layout lock
- synchronized endpoints on Workspace move/resize/restore and splitter changes
- introduced graceful Workspace-to-endpoint lifecycle experimentation

## v0.0.1rc02 — Configurable Cell Count + Z-order Persistence

- changed default Cell count to 8
- allowed 4-12 Cells
- registered only shortcuts for active Cells
- added Workspace move/resize/activation endpoint resync and Z-order work
- persisted Cell count in layout data

## v0.0.1rc01 — Native Endpoint Palette POC

- established the independent native top-level window architecture on .NET Framework 4.7.2 / WPF
- added foreground-window assignment with Ctrl+Shift+F1-F12
- added runtime Cell-to-HWND binding and duplicate-handle prevention
- added Identify, per-Cell unbind/close POC actions, group minimize/restore, Save/Load, and shortcut settings
- used `SetWindowPos` for external window geometry without `SetParent`, focus manipulation, input injection, or DLL injection

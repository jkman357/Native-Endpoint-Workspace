# Changelog

All notable changes to Native Endpoint Workspace are recorded here. `v0.0.1` is the frozen trial baseline derived from the accepted `v0.0.1rc17` freeze candidate.

## v0.0.1 — Frozen Trial Baseline

- froze the accepted `v0.0.1rc17` source baseline as the formal `v0.0.1` trial release
- retained the 1-8 Cell adaptive topology, F1-F8 assignment/removal workflow, detach-only lifecycle, single-anchor Z-order model, throttled interactive layout, final geometry/repaint convergence handling, identity/schema/shortcut hardening, and build/test/runtime diagnostics
- changed release/version metadata from `v0.0.1rc17` to `v0.0.1` without intentional Native Endpoint behavior changes
- established `v0.0.1` as an immutable trial baseline; subsequent fixes should be developed on the next maintenance RC line rather than modifying this release in place

## v0.0.1rc17 — 1-8 Cell Topology + Resize/Repaint Convergence

- reduced the supported Cell range to 1-8 with default 8; F9-F12 topology and global assignment shortcuts are no longer part of the active product surface
- added 1-, 2-, and 3-Cell adaptive topologies and allowed direct Cell removal down to one remaining Cell
- hide `Detach` on empty Cells and show it only while an endpoint is bound
- preserved contiguous F1-FN identities and automatic adaptive reflow after arbitrary Cell removal
- added final-placement `SWP_NOCOPYBITS` so the system does not preserve/copy stale client pixels during the final native resize request
- replaced the top-level-only `InvalidateRect` hint with one HWND-scoped `RedrawWindow` invalidation covering the top-level window and its child hierarchy, without child-HWND enumeration, focus manipulation, or input injection
- added DEBUG requested-vs-observed rectangle diagnostics and a warning when geometry has not converged at verification time
- retained async endpoint geometry, requested/verified state separation, Z-order stabilization, and bounded correction/backoff
- updated README, tests, version metadata, and runtime source baseline to v0.0.1rc17

## v0.0.1rc16 — Direct Cell Removal + Adaptive Reflow

- made each `F1` through `F12` Cell badge a direct Cell-removal control
- allowed an arbitrary Cell to be removed while enforcing the existing minimum of 4 active Cells
- when removing a bound Cell, require confirmation, detach that endpoint, and leave the external application open
- shift later endpoint bindings down one Cell so active Cells remain contiguous `F1...FN` rather than leaving holes
- rebuild the adaptive topology immediately after removal so the remaining Cells automatically consume the available Workspace area
- keep the `Cells` selector for bulk count changes and for increasing the number of active Cells
- reset Cell-indexed geometry/correction caches after a topology mutation and schedule a fresh final native layout commit
- add characterization coverage for arbitrary Cell removal and endpoint-binding shift semantics
- retain rc15 endpoint repaint stabilization and Cell chrome cleanup
- advanced application/runtime/source baseline to v0.0.1rc16

## v0.0.1rc15 — Endpoint Repaint Stabilization + Cell Header Cleanup

- removed the fixed per-Cell `layout locked • drag splitters between Cells` footer to reclaim content space
- replaced redundant `Cell 1` / `Cell 2` header badges with compact `F1` through `F12` badges
- simplified unbound Cell headers to show `No endpoint` while retaining `#` Identify and global F-key assignment mapping
- added a bounded top-level client repaint hint after asynchronous endpoint geometry has been observed and verified at the desired rectangle
- kept repaint handling non-blocking and limited to the bound top-level HWND; no application-specific child HWND enumeration, focus manipulation, or input injection was added
- retained rc14 compile fixes, rc13 build/test diagnostics, and rc12 safety/schema/runtime-log hardening
- advanced application/runtime/source baseline to v0.0.1rc15

## v0.0.1rc14 — Compile-Fix Closure

- fixed `CS0165` in `ShortcutService` by giving rollback diagnostics a definite initial value before the short-circuit rollback path
- fixed `CS0136` in `EndpointLayoutLockService` by using distinct managed-window variable names for foreground and object WinEvent branches
- preserved the rc13 build/test diagnostics and the rc12 endpoint-management behavior without intentional runtime policy changes
- advanced application/runtime/source baseline to v0.0.1rc14

## v0.0.1rc13 — Build & Test Diagnostics Hardening

- added automatic detailed MSBuild logging to `logs\build.log` for every `build.cmd` invocation
- kept console build output concise while recording diagnostic verbosity in the file log
- ensured a build log is still created when MSBuild cannot be located
- added explicit build exit-code reporting and log-path output on PASS/FAIL
- added automatic `logs\test.log` output for `test.cmd`
- made `test.cmd` preserve build diagnostics and stop cleanly when the build stage fails
- documented build/test log locations and failure-reporting workflow in README
- advanced application/runtime/source baseline to v0.0.1rc13 without changing the rc12 endpoint-management architecture

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

# Native Endpoint Workspace

**Version:** v0.0.1rc09  
**Target:** Windows 10 / C# / WPF / .NET Framework 4.7.2

Native Endpoint Workspace is a Windows technical POC for arranging and managing independent native top-level application windows as adaptive tiled **Native Endpoints** without embedding or reparenting them.

Firefox, Windows Explorer, Notepad++, VS Code, Terminal and similar applications remain normal top-level windows. The Workspace manages their screen geometry, layout membership, group visibility/Z-order, and bound-window lifecycle policy.

## Security / privacy boundary

Native Endpoint Workspace does **not** store, persist, collect, or manage user account credentials or passwords. Browser/application login state remains owned by each external application or browser profile. Runtime HWND values and endpoint process identity are not persisted in layout files.

The implementation deliberately does **not** use:

- `SetParent`
- owner mutation
- `AttachThreadInput`
- `SetFocus` manipulation
- `SendInput`
- IME/HKL manipulation
- DLL injection
- `WriteProcessMemory`
- `TerminateProcess`

## rc09 focus — Static Review Cleanup (#8 / #9 / #11 / #12)

rc09 is an engineering-cleanup RC. It preserves the rc08 foreground-group, size-accommodation, transactional-load, and native-result behavior while closing the remaining low/medium static-review cleanup tranche before the next full review.

### Review #8 — layout commit semantic clarity

The old `authoritativeFinal` concept is no longer used. Queue callers now explicitly request only whether completion status should be surfaced; correctness is identical for every Native Layout Commit. Coalesced requests preserve a pending completion-status request rather than silently discarding it.

### Review #9 — centralized behavior constants

Application-specific tuning values and shortcut/version constants are centralized in `WorkspaceConstants`, including supported Cell limits, F-key range, hotkey ID base, layout minimums, correction/backoff timing, endpoint-health timing, Identify duration, and native text-buffer capacities. Named Win32 constants remain in `NativeMethods`.

### Review #11 — fail-fast Cell topology

`GetRowCellCounts()` now explicitly handles Cell counts 4 through 12 and throws `ArgumentOutOfRangeException` for unsupported values. It no longer silently converts an unexpected value into a 12-Cell topology.

### Review #12 — bare global F-key protection

Shortcut registration now requires at least one modifier (`Ctrl`, `Shift`, `Alt`, or `Win`). Bare global `F1` through `F12` registrations are rejected so the Workspace cannot silently intercept common application commands such as Help, Rename, Refresh, Full Screen, or developer shortcuts. Layout-file validation enforces the same rule transactionally.

## Adaptive tiled layout

- 4 through 12 Cells; default 8.
- Workspace owns a fixed total area.
- Cells cannot float outside the Workspace.
- Splitters redistribute the existing area: when one region grows, adjacent regions shrink.
- Each row has independent horizontal Cell proportions.
- Row heights can be redistributed independently.
- Save/Load persists row heights and per-row Cell width proportions.
- `Reset Tiling` restores equal proportions without dropping bindings.

Default 8-Cell topology:

```text
+--------+--------+--------+--------+
| Cell 1 | Cell 2 | Cell 3 | Cell 4 |
+--------+--------+--------+--------+
| Cell 5 | Cell 6 | Cell 7 | Cell 8 |
+--------+--------+--------+--------+
```

## Binding workflow

1. Start Native Endpoint Workspace.
2. Bring a target top-level application window to the foreground.
3. Press `Ctrl+Shift+F1` for Cell 1, `Ctrl+Shift+F2` for Cell 2, and so on.
4. The foreground endpoint identity is captured and the window is placed over that Cell's content rectangle.
5. Repeat for separate Firefox top-level windows, Explorer, Notepad++, VS Code, Terminal, etc.
6. Move/resize/maximize/restore the Workspace or drag Cell splitters; the current WPF Cell geometry is reapplied to bound endpoints.

Firefox tabs are not Native Endpoints. Multiple Firefox endpoints require separate Firefox top-level windows.

## Native Layout Commit

```text
WPF LayoutUpdated / SizeChanged / LocationChanged
Splitter drag / maximize / restore / explicit Resync
        |
        v
geometry fingerprint changed?
        | yes
        v
coalesced Render-priority commit
        |
        v
WorkspaceGrid.UpdateLayout()
        |
        v
validate endpoint runtime identity
        |
        v
apply Cell screen rectangles asynchronously
        |
        v
verify endpoint minimum-size acceptance
        |
        v
anchor Workspace beneath every healthy endpoint
```

`Resync Endpoints` invokes the same path manually. It does not rebind, restart, or reload applications.

## Layout Lock

A bound endpoint is expected to occupy its Cell rectangle. If a managed application is manually moved or resized, an out-of-context WinEvent location hook requests correction back to the Cell.

The hook is observation-only and uses `WINEVENT_OUTOFCONTEXT`; no code is injected into target applications.

If an endpoint repeatedly refuses the requested geometry, bounded retry/backoff temporarily suspends Layout Lock for that endpoint instead of fighting indefinitely.

## Lifecycle semantics

- **Unbind:** stop managing the endpoint; application remains open.
- **Cell Close:** unbind and send graceful `WM_CLOSE` only after critical endpoint identity revalidation.
- **Reduce Cell count:** endpoints in removed Cells are unbound only; applications remain open.
- **Load Layout:** proposed state is validated before mutation; a successful load releases current runtime bindings while applications remain open; failed commit attempts roll back the previous working state.
- **Close Workspace:** after confirmation, still-bound endpoints that pass critical identity revalidation receive graceful `WM_CLOSE`; stale/unverifiable endpoints are deliberately left open.

`WM_CLOSE` is preferred over process termination so applications retain their normal unsaved-data handling.

## Save / Load

Layout files persist:

- Cell count
- adaptive row heights
- each row's independent Cell-width proportions
- shortcut mappings

Runtime HWND/PID/TID/process-start identity is intentionally **not** persisted.

## Architecture

```text
WPF Workspace
    |
    +-- Adaptive tiled layout
    |       +-- GridSplitter redistribution
    |       +-- actual Cell screen rectangles
    |
    +-- EndpointRegistry
    |       +-- Cell ID <-> runtime NativeEndpoint identity
    |
    +-- Native Layout Commit
    |       +-- WPF geometry fingerprint
    |       +-- Render-priority coalesced commit
    |
    +-- Endpoint Layout Lock
    |       +-- managed-HWND callback filtering
    |       +-- location / foreground / destroy observation
    |       +-- bounded correction retry/backoff
    |
    +-- NativeWindowCoordinator
            +-- identity validation
            +-- async external geometry requests
            +-- no-activation group minimize/restore
            +-- critical WM_CLOSE revalidation
            +-- Workspace-below-all-endpoints Z-order anchoring
            +-- monitor work-area query for size accommodation
```

## Build

From a Windows machine with the .NET Framework 4.7.2 developer targeting pack/MSBuild available:

```bat
cd Native-Endpoint-Workspace
build.cmd
```

Run:

```bat
run.cmd
```

The POC starts on .NET Framework 4.7.2 for compatibility with the initial test machine. Native/Services boundaries remain separated so a later migration to modern .NET/WPF does not require changing the Native Endpoint concept.

## rc09 regression / review-closure test

1. Build and run on the Windows 10 / .NET Framework 4.7.2 test machine.
2. Re-run rc08 mixed-endpoint tests: Firefox, Notepad++, console/Terminal, and Calculator.
3. Resize/maximize/restore the Workspace and drag splitters; bound endpoints must preserve rc08 geometry/Z-order behavior.
4. In Shortcut Settings, clear all modifiers for an F-key and apply. Verify the shortcut is rejected and not registered globally.
5. Verify modified F-key shortcuts still register and bind the correct foreground endpoint.
6. Load a layout containing a bare F-key shortcut and verify transactional validation rejects it without destroying the current working state.
7. Exercise Cell counts 4 through 12 and verify each supported topology builds correctly.
8. Confirm unsupported Cell counts fail validation rather than silently becoming a 12-Cell layout.
9. Save a layout and verify the serialized version is `0.0.1rc09`; runtime HWND identity remains non-persistent.
10. Run a new full static Code Review and classify all original 12 findings as CLOSED, ACCEPTED RISK, or DEFERRED based on rc09 source.

## Current maturity

**POC / hardening stage.** rc09 preserves the working rc08 Native Endpoint layout model and closes the planned static-review cleanup tranche (#8, #9, #11, #12). A fresh full review is required before declaring the original 12 findings closed as a set; rc09 does not claim production readiness by itself.

## License

MIT License. See `LICENSE`.

Copyright © 2026.

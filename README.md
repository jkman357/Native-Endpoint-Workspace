# Native Endpoint Workspace

**Version:** v0.0.1rc07  
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

## rc07 focus — Endpoint Identity & Native Operation Hardening

rc06 established the first real-machine baseline where multiple Firefox windows could remain locked to tiled Cells while the Workspace was moved, resized, maximized, restored, or splitters were dragged.

rc07 keeps that layout model and hardens the native-window boundary after static review identified stale/reused HWND handling, potentially blocking cross-process calls, and unbounded correction loops as the main reliability risks.

### Stronger runtime endpoint identity

A bound endpoint now records:

```text
HWND
Process ID (PID)
Thread ID (TID)
Process start time (when accessible)
Window class name
Process name / title (display metadata)
```

Routine native operations validate the current HWND against PID/TID/window class. Destructive `WM_CLOSE` requests perform an additional process-start-time check when that information was captured at bind time.

`EVENT_OBJECT_DESTROY` is monitored so a destroyed managed window can be unbound immediately rather than leaving a stale HWND in the registry.

> HWND is still a Windows runtime resource, not a permanent object identifier. rc07 substantially reduces handle-reuse risk but does not claim that an HWND can be made globally permanent.

### Guarded / non-blocking cross-process window operations

External geometry and endpoint-raise operations use `SWP_ASYNCWINDOWPOS`; group minimize/restore uses `ShowWindowAsync`; close requests use `PostMessage(WM_CLOSE)`.

Before native operations, endpoint identity is revalidated. Hung endpoints are skipped for geometry/Z-order correction so an unresponsive external UI thread is not allowed to hold the WPF Dispatcher in a synchronous geometry/raise call.

The Workspace itself is still placed below the endpoint group with a normal same-process `SetWindowPos` operation. Endpoint windows remain normal, non-topmost top-level windows.

### Bounded Layout Lock correction

If an application repeatedly rejects the requested Cell geometry, rc07 no longer corrects indefinitely at full rate.

Per-endpoint state tracks correction bursts. After four correction attempts inside a short burst window, Layout Lock pauses corrections for three seconds and surfaces a status warning. This bounds the `SetWindowPos -> WinEvent -> correction` feedback path while keeping normal snap-back behavior.

### Early WinEvent filtering

The WinEvent service keeps a thread-safe snapshot of currently managed HWNDs. Unrelated system-wide location/destroy events are discarded in the callback before any WPF Dispatcher work is queued.

Foreground events are likewise passed through only for the Workspace or a currently managed endpoint.

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
raise healthy endpoint group without activation
        |
        v
anchor Workspace beneath endpoint group
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
- **Load Layout:** current runtime bindings are released; applications remain open.
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
            +-- async external geometry/Z-order requests
            +-- async minimize/restore
            +-- critical WM_CLOSE revalidation
            +-- Workspace-below-endpoint-group anchoring
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

## rc07 regression / hardening test

1. Bind at least three separate Firefox top-level windows plus one non-Firefox application.
2. Repeat Workspace move, continuous resize, maximize/restore, and Cell splitter drag. rc06 behavior must remain intact.
3. Click among bound endpoints; other bound Cells must remain visible.
4. Drag/resize a bound endpoint directly; normal snap-back should occur.
5. Repeatedly force one application to reject/move away from requested geometry and verify correction eventually enters temporary backoff instead of looping continuously.
6. Close one bound application externally. Its Cell should automatically unbind from the destroy event (with health-timer fallback still available).
7. Minimize/restore the group and verify the Workspace remains responsive if one endpoint is slow or hung.
8. Close a bound Cell and verify graceful close occurs only for a revalidated endpoint.
9. Close the Workspace and verify revalidated bound applications receive graceful close requests; stale/unverifiable endpoints must be left open.
10. Confirm `Resync Endpoints` preserves bindings and never restarts an application.

## Current maturity

**POC / hardening stage.** rc07 addresses the highest-priority static-review findings around endpoint identity, cross-process UI blocking risk, destroy detection, early event filtering, and correction-loop bounding. Transactional layout loading, richer native-operation result reporting, and explicit restore/focus policy remain future hardening work.

## License

MIT License. See `LICENSE`.

Copyright © 2026.

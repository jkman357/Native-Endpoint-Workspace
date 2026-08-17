# Native Endpoint Workspace

**Version:** v0.0.1rc08  
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

## rc08 focus — Foreground Group, Size Accommodation, and Review Medium Findings

rc08 preserves the rc06/rc07 real-machine tiled-window baseline and closes the next reliability tranche without adding unrelated features.

### Managed Endpoint Foreground Group

When any bound endpoint becomes foreground, the Workspace now re-establishes the **whole bound endpoint group above the opaque WPF Workspace** by moving only the Workspace behind each healthy endpoint. It no longer relies on a race between multiple asynchronous endpoint-raise requests. The user's clicked endpoint remains naturally active; the Workspace does not call `SetForegroundWindow`, `SetFocus`, or synthesize input.

### Endpoint Size Accommodation

Some applications, such as Windows Calculator modes, enforce a minimum top-level window size. After an asynchronous geometry request, rc08 verifies the settled window rectangle. If the endpoint accepted the Cell position but rejected the requested width/height, the Workspace treats the observed larger size as a minimum-size constraint instead of fighting it indefinitely.

The affected Cell/row receives a larger minimum allocation; neighboring Cells give up space first, and a normal-state Workspace may grow up to the current monitor work area when the existing canvas cannot satisfy the learned minimums. Unbinding the endpoint releases its learned Cell minimum.

### Review #5 — explicit no-activation group minimize/restore

Group lifecycle commands now use `ShowWindowAsync` with no-activation show states (`SW_SHOWMINNOACTIVE` / `SW_SHOWNOACTIVATE`). Geometry and Z-order maintenance continue to use no-activation policies. Natural user clicks are the activation mechanism.

### Review #6 — transactional layout loading

Layout files are deserialized and fully validated before active Workspace state is mutated. rc08 validates Cell range, adaptive row structure, finite positive layout weights, shortcut ranges, and duplicate active gestures. If commit of a validated layout throws, the previous Cell count, tiling, shortcuts, runtime bindings, and learned size constraints are restored on a best-effort rollback path.

### Review #7 — native-operation result reporting

Native layout commits now aggregate geometry outcomes, stale/hung/minimized skips, Win32 geometry failures, and Workspace Z-order anchor failures. Explicit Resync and failed background commits surface structured status instead of always claiming success.

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

## rc08 regression / hardening test

1. Bind four separate Firefox top-level windows plus Notepad++, Command Prompt/Terminal, and Windows Calculator.
2. Click repeatedly among Firefox, console, editor, and Calculator. No other bound endpoint should disappear behind the Workspace.
3. Move/resize/maximize/restore the Workspace and drag Cell splitters; all bound endpoints must follow the current Cell geometry.
4. Bind an application that rejects a Cell size. Verify the Cell/row allocation expands and, when necessary in normal state, the Workspace grows within the monitor work area instead of entering an endless correction fight.
5. Unbind that endpoint and verify its learned size constraint is released.
6. Minimize/Restore Group and verify endpoints do not steal activation sequentially.
7. Load a malformed/incompatible layout and verify the previously working layout/bindings remain intact.
8. Use `Resync Endpoints` and verify the status reports geometry/Z-order failures or stale/hung skips rather than unconditional success.
9. Re-run rc07 identity/destroy/backoff tests and rc06 resize/maximize regression tests.
10. Close the Workspace and verify only still-bound, strongly revalidated endpoints receive graceful `WM_CLOSE`.

## Current maturity

**POC / hardening stage.** rc08 preserves the working Native Endpoint layout model while addressing foreground-group Z-order, endpoint minimum-size accommodation, explicit no-activation group restore/minimize policy, transactional layout loading, and structured native layout result reporting. Remaining static-review cleanup items are intentionally deferred to the next RC rather than mixed into this reliability tranche.

## License

MIT License. See `LICENSE`.

Copyright © 2026.

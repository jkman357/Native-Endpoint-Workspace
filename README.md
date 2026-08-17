# Native Endpoint Workspace

**Version:** v0.0.1rc06  
**Target:** Windows 10 / C# / WPF / .NET Framework 4.7.2

Native Endpoint Workspace is a technical POC for arranging independent Windows native top-level application windows inside a workspace-like palette without reparenting them as child windows.

Firefox, Windows Explorer, Notepad++, VS Code, Terminal and similar applications are treated as **Native Endpoints** represented by runtime top-level HWNDs.

## Security / privacy boundary

Native Endpoint Workspace does **not** store, persist, collect, or manage user account credentials or passwords. Application/browser login state remains owned and managed by each external application or browser profile. Runtime HWND values are not persisted in layout files.

The implementation deliberately does **not** use `SetParent`, owner mutation, `AttachThreadInput`, `SetFocus` manipulation, `SendInput`, IME/HKL manipulation, or DLL injection.

## rc06 focus: deterministic Native Layout Commit

rc05 real-machine testing showed that timer-delayed resync still did not reliably follow Workspace move/resize/maximize or Cell splitter changes. The HWND bindings survived, but external applications could remain behind the opaque WPF Workspace until the bind hotkey was pressed again.

rc06 replaces timer-based layout guessing with one deterministic commit path:

```text
WPF LayoutUpdated / SizeChanged / LocationChanged
Splitter drag / maximize / restore / explicit Resync
        |
        v
geometry fingerprint changed?
        | yes
        v
coalesce one Render-priority commit
        |
        v
WorkspaceGrid.UpdateLayout()
        |
        v
read every bound Cell screen rectangle
        |
        v
SetWindowPos endpoint geometry (NOACTIVATE / NOZORDER)
        |
        v
rebuild endpoint Z-order group
        |
        v
explicitly place Workspace immediately below lowest endpoint
```

Important properties:

- No live/final resync timers are used for normal layout tracking.
- `LayoutUpdated` is guarded by a WPF geometry fingerprint, so unchanged layouts do not issue native window operations.
- All triggers converge on the same `CommitNativeLayout()` path.
- The Workspace is explicitly placed below the bound endpoint group; rc05 only raised endpoints and did not anchor the Workspace beneath them.
- Bound apps remain independent, normal, non-topmost top-level windows.
- `SWP_NOACTIVATE` is retained; keyboard focus is not forced.
- WinEvent location correction remains out-of-context and guarded against self-generated correction loops.
- **Resync Endpoints** invokes the same commit path manually and never rebinds/restarts an application.

## Adaptive tiled layout

- 4 through 12 Cells; default 8.
- Workspace owns a fixed total area.
- Cells cannot float outside the Workspace.
- Splitters redistribute the existing area: when one region grows, its adjacent region shrinks.
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
4. The HWND is registered and positioned over the Cell content rectangle.
5. Repeat for separate Firefox top-level windows, Explorer, Notepad++, VS Code, Terminal, etc.
6. Resize/move the Workspace or drag a Cell splitter. rc06 commits the latest WPF layout to all currently bound endpoints.

Firefox tabs are not Native Endpoints. Use separate Firefox top-level windows when multiple Firefox Endpoints are required.

## Z-order model

All endpoints remain independent top-level windows. They are not children of the WPF Workspace.

While the Workspace or one of its bound endpoints is the foreground group, rc06 reconstructs the normal non-topmost endpoint order and explicitly places the Workspace immediately below the lowest bound endpoint. `SWP_NOACTIVATE` is used; keyboard focus is not forced.

Switching to an unrelated application does not make the endpoint group globally Topmost.

## Layout Lock

A bound endpoint is expected to occupy its Cell rectangle. If a bound application is manually moved or resized, an out-of-context WinEvent location hook schedules correction back to the Cell.

The hook is observation-only and uses `WINEVENT_OUTOFCONTEXT`; no code is injected into target applications.

## Lifecycle semantics

- **Unbind:** stop managing the HWND; application remains open.
- **Cell Close:** send graceful `WM_CLOSE` to that bound window.
- **Reduce Cell count:** endpoints in removed Cells are unbound only; applications remain open.
- **Load Layout:** current runtime bindings are released; applications remain open.
- **Close Workspace:** after confirmation, every still-bound application window receives graceful `WM_CLOSE`.

`WM_CLOSE` is deliberately preferred over forceful process termination so applications can display their own unsaved-data prompts.

## Save / Load

Layout files persist:

- Cell count
- adaptive row heights
- each row's independent Cell-width proportions
- shortcut mappings

Runtime HWND values are intentionally not persisted.

## Architecture

```text
WPF Workspace
    |
    +-- Adaptive tiled layout
    |       +-- GridSplitter redistribution
    |       +-- actual Cell screen rectangles
    |
    +-- EndpointRegistry
    |       +-- Cell ID <-> runtime HWND
    |
    +-- Native Layout Commit
    |       +-- WPF geometry fingerprint
    |       +-- Render-priority coalesced commit
    |
    +-- Endpoint Layout Lock
    |       +-- out-of-context location observation
    |       +-- out-of-context foreground observation
    |
    +-- NativeWindowCoordinator
            +-- SetWindowPos geometry (SWP_NOZORDER)
            +-- normal Z-order group reconstruction (SWP_NOACTIVATE)
            +-- Workspace placed below endpoint group
            +-- ShowWindow / WM_CLOSE
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

The POC starts on .NET Framework 4.7.2 for compatibility with the current test machine. Native/Services boundaries remain separated so a later migration to modern .NET/WPF does not require changing the Native Endpoint model.

## rc06 real-machine acceptance test

1. Bind at least three separate Firefox top-level windows plus one other app (for example Notepad++).
2. Verify all bound endpoints are visible at the same time.
3. Resize the Workspace continuously from the lower-right corner. Bound endpoints should follow the Cell rectangles without pressing the bind hotkeys again.
4. Move the Workspace around the desktop. Bound endpoints should move with it; release must not require a bind hotkey.
5. Maximize and restore the Workspace; all bound endpoints should resync automatically.
6. Drag vertical/horizontal Cell splitters repeatedly. The affected Cells redistribute the fixed Workspace area and all bound endpoints follow.
7. Click among Cell 1/2/3/etc. Other bound endpoints should remain visible rather than falling behind the Workspace.
8. Attempt to drag/resize a bound endpoint directly; it should snap back without a high-frequency loop/hang.
9. If a visual desync is observed, click **Resync Endpoints**. All current bindings must be preserved and no application should restart.
10. Switch to an unrelated application and verify the Workspace group is not globally Topmost.
11. Close the Workspace and verify all still-bound applications receive graceful close requests.

## License

MIT License. See `LICENSE`.

Copyright © 2026.

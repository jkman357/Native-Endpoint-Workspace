# Native Endpoint Workspace

**Current version:** v0.0.2rc02  
**Target:** Windows 10 / C# / WPF / .NET Framework 4.7.2  
**Status:** Trial maintenance RC — bug/regression fixes only

Native Endpoint Workspace is a Windows workspace for arranging independent native top-level application windows as adaptive tiled **Native Endpoints**. External applications stay normal top-level windows; the Workspace manages their screen geometry, Cell membership, layout lock, group visibility, and Z-order without embedding or reparenting.

`v0.0.1` remains the immutable frozen trial baseline. `v0.0.2rc02` continues the maintenance line: rc01 runtime identity/group-visibility fixes are retained, while rc02 adds raw-before-merge layout validation and full work-area bounds convergence during endpoint minimum-size accommodation.

For release history, see [`CHANGELOG.md`](CHANGELOG.md).

## Core architecture

```text
WPF Workspace
    |
    +-- Adaptive tiled Cell layout (1-8 Cells, default 8)
    |       +-- independent row/Cell proportions
    |       +-- GridSplitter redistribution
    |       +-- endpoint minimum-size accommodation
    |
    +-- EndpointRegistry
    |       +-- runtime-only HWND/PID/TID/process metadata
    |       +-- process start time + destroy-observed tombstone
    |
    +-- Native Layout Commit
    |       +-- ~45 ms interactive coalescing
    |       +-- final precise commit after resize/splitter operations
    |       +-- requested vs verified geometry state
    |       +-- verified endpoint repaint/convergence handling
    |
    +-- Endpoint Layout Lock
    |       +-- out-of-context WinEvent observation
    |       +-- early managed-HWND filtering
    |       +-- bounded correction/backoff
    |
    +-- NativeWindowCoordinator
            +-- strong identity revalidation at health/mutation boundaries
            +-- asynchronous external geometry requests
            +-- single-anchor endpoint-group Z-order
            +-- no-activation group minimize/restore
            +-- full monitor work-area clamp during Workspace growth
```

## Non-embedding boundary

The application deliberately does **not** use:

- `SetParent`
- owner mutation
- `AttachThreadInput`
- `SetFocus` manipulation
- `SendInput`
- IME/HKL manipulation
- DLL injection
- `WriteProcessMemory`
- `TerminateProcess`

External apps remain independent native top-level windows.

## Binding workflow

1. Start Native Endpoint Workspace.
2. Bring the target application window to the foreground.
3. Press `Ctrl+Shift+F1` for Cell 1, `Ctrl+Shift+F2` for Cell 2, and so on through F8.
4. The foreground top-level window is described and must have a verifiable process start time before the binding is accepted.
5. The accepted endpoint follows its Cell screen rectangle under Adaptive Layout Lock.

Firefox tabs are not endpoints. Use separate Firefox top-level windows for separate Cells.

## Cell and lifecycle semantics

- **Detach:** releases the endpoint from its Cell and Layout Lock; the application remains open.
- **Application X:** close the application using its own native window controls. Destroy/stale handling clears the Cell automatically.
- **Remove a specific Cell:** click its `F#` badge. If an endpoint is bound, confirm the removal; that endpoint is detached and left open. Later Cells shift down to keep contiguous `F1...FN` identities and the layout reflows automatically.
- **Reduce Cell count:** the `Cells` selector supports bulk count changes; endpoints in removed trailing Cells are detached and applications remain open.
- **Load Layout:** runtime endpoint handles are not restored from disk.
- **Close Workspace:** all bindings are detached/stopped and **external applications remain open**. The Workspace does not send `WM_CLOSE` to external applications.

## Endpoint identity hardening

Runtime identity captures:

- HWND
- process ID
- thread ID
- process start time
- window class
- per-binding runtime instance ID

`v0.0.2rc01` closes the gap between the existing strong identity policy and runtime behavior:

- a new binding is rejected if bind-time process start time cannot be established;
- the periodic health revalidation path requires the strong process-start check;
- external-window native mutations (geometry, repaint, Z-order anchor use, minimize, restore) require the strong process-start check immediately before mutation;
- read-only probes may continue to use the lightweight HWND/PID/TID/class check to avoid adding repeated process queries to high-frequency layout inspection;
- strong validation fails closed when current process start time cannot be queried or no longer matches;
- an observed `EVENT_OBJECT_DESTROY` tombstones the bound endpoint instance before UI-thread cleanup.

Runtime HWND/PID/TID data is never persisted to layout files.

## Group visibility semantics

The toolbar group minimize/restore action now tracks exactly which endpoints it successfully asked to minimize.

- Endpoints already minimized before the toolbar operation are **not** added to the toolbar restore set.
- Restore acts only on the tracked set, so user-pre-minimized applications remain minimized.
- Failed restore requests remain tracked so the action can be retried while the endpoint remains bound.
- Detach/rebind/destroy removes the relevant handle from the toolbar restore set and refreshes the toolbar state.

Workspace minimize/restore remains a separate lifecycle path with its own tracking set.

## Adaptive layout

- 1 through 8 Cells; default 8.
- Cell headers use clickable `F1` through `F8` badges.
- Splitters redistribute a fixed Workspace layout area.
- Each row can have independent horizontal proportions; row heights can also be redistributed.
- Applications that reject an undersized rectangle can cause their Cell/row allocation to grow; the Workspace may grow, resize, and reposition as needed while remaining fully bounded by the current monitor work area.
- Removing an arbitrary Cell reindexes later Cells contiguously and immediately rebuilds the adaptive topology.
- `Reset Cell Layout` restores equal proportions without dropping bindings.

## Shortcut policy

Default endpoint assignment:

```text
Ctrl+Shift+F1 ... Ctrl+Shift+F8
```

Shortcut Settings supports Ctrl / Alt / Shift combinations. At least one supported modifier is required. Bare F1-F8 and Win-key global combinations are rejected.

Global shortcut registration is transactional: if Windows rejects any requested hotkey, the partially registered new set is removed and the previous working set is restored.

Workspace-local shortcuts:

```text
Ctrl+S  Save Layout
Ctrl+O  Load Layout
```

## Save / Load and schema version

Layout files persist `LayoutSchemaVersion`, application version for provenance, Cell count, adaptive row/Cell proportions, and shortcut mappings. Runtime endpoint handles are never persisted.

`LayoutSchemaVersion` remains independent from the application RC version; schema version 1 is unchanged in `v0.0.2rc02`. Load processing now follows a raw-validate → compatibility-merge → resolved-validate order. Valid older files may still omit shortcut entries and receive defaults, but malformed raw shortcut entries are rejected before merge. Failure-safe/atomic layout persistence remains deferred to a later maintenance RC.

## DPI baseline

The .NET Framework 4.7.2 WPF process is Per-Monitor DPI aware through `app.manifest` and `App.config`. `WM_DPICHANGED` schedules a final endpoint geometry commit after the WPF layout transition.

## Runtime diagnostics

Runtime diagnostics are written under:

```text
logs\NativeEndpointWorkspace.log
```

Default level is `INFO`. For extra layout diagnostics:

```bat
set NATIVE_ENDPOINT_WORKSPACE_LOG_LEVEL=DEBUG
run.cmd
```

Log policy:

- maximum active file size: 5 MB
- retention: 5 log files total
- logging failure must not stop the Workspace
- DEBUG mode records requested/observed endpoint rectangles and convergence results
- logs do not record webpage contents, typed input, clipboard contents, passwords/credentials, browser cookies/session data, full endpoint titles, or full Explorer/document paths derived from titles

## Security and privacy

Native Endpoint Workspace does **not** store, persist, collect, or manage user account credentials or passwords. Browser/application login credentials and session state remain owned by the browser or external application profile and are outside this application's storage.

Layout files contain Workspace geometry and shortcut configuration only; runtime native window identity is not persisted.

## Build

Requirements:

- Windows 10
- Visual Studio/MSBuild with the .NET Framework 4.7.2 Developer/Targeting Pack

```bat
build.cmd
```

Detailed diagnostics are written to `logs\build.log`.

## Test

```bat
test.cmd
```

Policy-test output is written to `logs\test.log`. Automated characterization includes strong endpoint identity fail-closed behavior, required runtime strong-check policy, toolbar restore-tracking policy, raw-before-merge layout validation, work-area bounds clamping (including negative-coordinate monitors), exact legacy layout-version boundaries, Cell topology, arbitrary Cell removal shifting, destroy tombstoning, hotkey rollback, and unsupported schema rejection.

Windows integration behavior (WinEvent, Z-order, DPI, mixed-app geometry, actual minimize/restore) still requires real Windows runtime regression testing.

## v0.0.2rc02 regression focus

1. Run `test.cmd`; confirm all policy tests PASS and preserve both `logs\build.log` and `logs\test.log`.
2. Load a valid older layout with missing shortcut entries; confirm defaults fill only the missing entries and the layout still loads transactionally.
3. Load layouts containing a null shortcut, CellId outside F1-F8, unsupported/bare shortcut, or duplicate shortcut CellId; confirm the raw file is rejected before the active Workspace changes.
4. Place the Workspace near the right/bottom edge, bind an app that rejects a small rectangle, and trigger minimum-size accommodation; confirm Workspace growth repositions as needed and remains fully inside the monitor work area.
5. Repeat the accommodation test on a secondary monitor, including one positioned left of the primary (negative X) when available.
6. Re-run rc01 group minimize/restore and strong endpoint-identity regression cases; confirm no maintenance regression.
7. Confirm closing the Workspace leaves all external applications open.

## Run

```bat
run.cmd
```

## Disclaimer

This repository is an engineering POC and is not a production-grade desktop window manager. Native top-level window orchestration depends on application and Windows behavior outside the Workspace process. Validate important workflows on the actual target environment before relying on it for long-running work.

## License

MIT License. See [`LICENSE`](LICENSE).

# Native Endpoint Workspace

**Current version:** v0.0.1rc12  
**Target:** Windows 10 / C# / WPF / .NET Framework 4.7.2  
**Status:** Technical POC / hardening line

Native Endpoint Workspace is a Windows workspace for arranging independent native top-level application windows as adaptive tiled **Native Endpoints**. External applications stay normal top-level windows; the Workspace manages their screen geometry, Cell membership, layout lock, group visibility, and Z-order without embedding or reparenting.

Typical endpoints include Firefox top-level windows, Windows Explorer, Notepad++, VS Code, Terminal/Command Prompt, Calculator, Paint, and other normal desktop applications.

For release history, see [`CHANGELOG.md`](CHANGELOG.md).

## Core architecture

```text
WPF Workspace
    |
    +-- Adaptive tiled Cell layout (4-12 Cells, default 8)
    |       +-- independent row/Cell proportions
    |       +-- GridSplitter redistribution
    |       +-- endpoint minimum-size accommodation
    |
    +-- EndpointRegistry
    |       +-- runtime-only HWND/PID/TID/process metadata
    |       +-- destroy-observed tombstone on the bound instance
    |
    +-- Native Layout Commit
    |       +-- ~45 ms interactive coalescing
    |       +-- final precise commit after resize/splitter operations
    |       +-- requested vs verified geometry state
    |
    +-- Endpoint Layout Lock
    |       +-- out-of-context WinEvent observation
    |       +-- early managed-HWND filtering
    |       +-- bounded correction/backoff
    |
    +-- NativeWindowCoordinator
            +-- identity validation
            +-- asynchronous external geometry requests
            +-- single-anchor endpoint-group Z-order
            +-- no-activation group minimize/restore
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
3. Press `Ctrl+Shift+F1` for Cell 1, `Ctrl+Shift+F2` for Cell 2, and so on.
4. The foreground top-level window is bound to that Cell and follows its screen rectangle.
5. Move/resize/maximize/restore the Workspace or drag Cell splitters; bound endpoints are resynchronized automatically.

Firefox tabs are not endpoints. Use separate Firefox top-level windows for separate Cells.

## Cell and lifecycle semantics

- **Detach:** releases the endpoint from its Cell and Layout Lock; the application remains open.
- **Application X:** close the application using its own native window controls. Destroy/stale handling clears the Cell automatically.
- **Reduce Cell count:** endpoints in removed Cells are detached; applications remain open.
- **Load Layout:** runtime endpoint handles are not restored from disk.
- **Close Workspace:** all bindings are detached/stopped and **external applications remain open**. The Workspace does not send `WM_CLOSE` to external applications.

The detach-only Workspace shutdown policy avoids treating a raw HWND as an infallible destructive-operation identity.

## Endpoint identity hardening

Runtime identity captures:

- HWND
- process ID
- thread ID
- process start time when available
- window class
- per-binding runtime instance ID

A strong identity check is fail-closed when bind-time/current process start time cannot be verified. An observed `EVENT_OBJECT_DESTROY` tombstones the currently bound endpoint instance before UI-thread cleanup, so that instance can no longer validate as current even if the numeric HWND value is later reused.

Runtime HWND/PID/TID data is never persisted to layout files.

## Adaptive layout

- 4 through 12 Cells; default 8.
- Workspace owns a fixed total layout area.
- Splitters redistribute area instead of allowing Cells to float outside the Workspace.
- Each row can have independent horizontal proportions.
- Row heights can be redistributed.
- Applications that reject an undersized rectangle can cause their Cell/row allocation to grow; the Workspace may grow within the monitor work area.
- `Reset Cell Layout` restores equal proportions without dropping bindings.

## Shortcut policy

Default endpoint assignment:

```text
Ctrl+Shift+F1 ... Ctrl+Shift+F12
```

Shortcut Settings supports Ctrl / Alt / Shift combinations. At least one supported modifier is required. Bare F1-F12 and Win-key global combinations are rejected.

Global shortcut registration is transactional: if Windows rejects any requested hotkey, the partially registered new set is removed and the previous working set is restored.

Workspace-local shortcuts:

```text
Ctrl+S  Save Layout
Ctrl+O  Load Layout
```

## Save / Load and schema version

Layout files persist:

- `LayoutSchemaVersion`
- application version for provenance
- Cell count
- adaptive row heights
- per-row Cell-width proportions
- shortcut mappings

`LayoutSchemaVersion` is independent from the application RC version. rc12 writes schema version `1`. Legacy rc01-rc11 files without the schema field are accepted only when their application version exactly matches the `0.0.1` / `0.0.1rcN` line.

Load validates structure before mutating the active Workspace. OS-level global hotkey registration is also included in the transaction; a partial registration failure restores the previous shortcut set and causes the load to fail/roll back.

## DPI baseline

rc12 explicitly opts the .NET Framework 4.7.2 WPF process into **Per-Monitor DPI awareness** using `app.manifest` (`true/pm` / `PerMonitor`) and enables WPF DPI-change handling through `App.config`.

`WM_DPICHANGED` schedules a final endpoint geometry commit after the WPF layout transition.

Required mixed-DPI regression cases before claiming multi-monitor hardening:

- 100% -> 150%
- 150% -> 100%
- 125% -> 200%
- maximize/restore on a secondary monitor
- move the Workspace between different-DPI monitors
- monitor resolution/DPI change or hot-plug
- endpoints with different DPI-awareness modes

## Runtime diagnostics

Runtime diagnostics are written under:

```text
logs\NativeEndpointWorkspace.log
```

Default level is `INFO`. To enable additional layout-commit diagnostics for a test session:

```bat
set NATIVE_ENDPOINT_WORKSPACE_LOG_LEVEL=DEBUG
run.cmd
```

Log policy:

- maximum active file size: 5 MB
- retention: 5 log files total (active + rotated backups)
- rotation is automatic
- logging failure must not stop the Workspace

Examples of recorded events:

- Workspace session start/exit
- endpoint bind/detach/destroy
- Cell ID, HWND, PID/TID, process name
- shortcut apply/rollback
- save/load success/failure
- DPI change
- slow Native Layout Commit warnings
- optional DEBUG layout commit duration/counts

The runtime log deliberately does **not** record:

- webpage contents
- typed user input
- clipboard contents
- passwords/credentials
- browser cookies/session data
- full endpoint window titles
- full Explorer paths or document names derived from window titles

## Security and privacy

Native Endpoint Workspace does **not** store, persist, collect, or manage user account credentials or passwords. Browser/application login credentials and session state remain owned by the browser or external application profile and are outside this application's storage.

Layout files contain Workspace geometry and shortcut configuration only; runtime native window identity is not persisted.

## Build

Requirements:

- Windows 10
- Visual Studio/MSBuild with the .NET Framework 4.7.2 Developer/Targeting Pack

From the repository root:

```bat
build.cmd
```

## Test

rc12 adds a dependency-free .NET Framework test executable for extracted pure policy/state logic:

```bat
test.cmd
```

Initial automated characterization covers:

- strong endpoint identity fail-closed behavior
- exact legacy layout-version boundary behavior
- Cell topology fail-fast behavior
- destroy tombstone behavior
- global-hotkey transaction rollback
- unsupported layout schema rejection

Windows integration behavior (WinEvent, Z-order, DPI, mixed-app geometry) still requires real Windows runtime regression testing.

## Run

```bat
run.cmd
```

## rc12 regression focus

1. Bind 4-8 mixed apps and repeat Workspace move/resize/maximize/restore and splitter drag.
2. Confirm `Detach` leaves the external app open and independent.
3. Close the Workspace with bound apps; confirm every external app remains open.
4. Force a shortcut conflict during Settings/Load and confirm the previous shortcut set still works afterward.
5. Move the Workspace between monitors with different Windows scaling values and compare Cell/endpoint pixel bounds.
6. Close/recreate an endpoint and confirm the destroyed binding does not resume management of a replacement window.
7. Inspect `logs\NativeEndpointWorkspace.log`; verify no endpoint titles, paths, typed text, credentials, or browser content are present.
8. Run `test.cmd` and confirm all policy tests PASS.

## Disclaimer

This repository is an engineering POC and is not a production-grade desktop window manager. Native top-level window orchestration depends on application and Windows behavior outside the Workspace process. Validate important workflows on the actual target environment before relying on it for long-running work.

## License

MIT License. See [`LICENSE`](LICENSE).

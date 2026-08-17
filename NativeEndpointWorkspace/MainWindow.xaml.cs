using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using NativeEndpointWorkspace.Core;
using NativeEndpointWorkspace.Native;
using NativeEndpointWorkspace.Services;
using NativeEndpointWorkspace.UI;

namespace NativeEndpointWorkspace
{
    public partial class MainWindow : Window
    {
        private const int MinCellCount = 4;
        private const int MaxCellCount = 12;
        private const int DefaultCellCount = 8;
        private const double SplitterSize = 6.0;

        private readonly NativeWindowCoordinator _windowCoordinator = new NativeWindowCoordinator();
        private readonly EndpointRegistry _registry = new EndpointRegistry();
        private readonly LayoutService _layoutService = new LayoutService();
        private readonly EndpointLayoutLockService _layoutLockService = new EndpointLayoutLockService();
        private readonly Dictionary<int, CellControl> _cells = new Dictionary<int, CellControl>();
        private readonly Dictionary<int, Grid> _rowGrids = new Dictionary<int, Grid>();
        private readonly Dictionary<int, List<int>> _rowCellIds = new Dictionary<int, List<int>>();
        private readonly Dictionary<IntPtr, DateTime> _locationCorrectionSuppressedUntil = new Dictionary<IntPtr, DateTime>();
        private readonly HashSet<IntPtr> _pendingLocationCorrections = new HashSet<IntPtr>();
        private readonly HashSet<IntPtr> _minimizedWithWorkspace = new HashSet<IntPtr>();
        private readonly DispatcherTimer _windowHealthTimer;

        private ShortcutService _shortcutService;
        private IList<ShortcutBinding> _shortcutBindings;
        private HwndSource _hwndSource;
        private IntPtr _workspaceHwnd;
        private bool _initialLayoutApplied;
        private bool _updatingCellCountUi;
        private bool _buildingGrid;
        private bool _groupNormalizeScheduled;
        private bool _syncingEndpoints;
        private bool _normalizingZOrder;
        private bool _nativeLayoutCommitQueued;
        private bool _committingNativeLayout;
        private string _lastObservedLayoutFingerprint = string.Empty;
        private bool _workspaceCloseAccepted;
        private int _cellCount = DefaultCellCount;
        private int _rowCount;

        public MainWindow()
        {
            InitializeComponent();

            for (int i = MinCellCount; i <= MaxCellCount; i++)
                CellCountComboBox.Items.Add(i);
            CellCountComboBox.SelectedItem = _cellCount;

            SourceInitialized += MainWindow_SourceInitialized;
            Loaded += MainWindow_Loaded;
            Activated += MainWindow_Activated;
            LocationChanged += MainWindow_LocationChanged;
            SizeChanged += MainWindow_SizeChanged;
            StateChanged += MainWindow_StateChanged;
            Closing += MainWindow_Closing;
            Closed += MainWindow_Closed;

            _layoutLockService.WindowLocationChanged += LayoutLockService_WindowLocationChanged;
            _layoutLockService.ForegroundChanged += LayoutLockService_ForegroundChanged;

            // Slow health fallback only. rc06 no longer depends on live/final resync timers.
            // Actual WPF geometry changes are detected from LayoutUpdated with a geometry
            // fingerprint, then coalesced into one Render-priority native layout commit.
            _windowHealthTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1250) };
            _windowHealthTimer.Tick += WindowHealthTimer_Tick;
            WorkspaceGrid.LayoutUpdated += WorkspaceGrid_LayoutUpdated;
        }

        private void MainWindow_SourceInitialized(object sender, EventArgs e)
        {
            var helper = new WindowInteropHelper(this);
            _workspaceHwnd = helper.Handle;
            _hwndSource = HwndSource.FromHwnd(_workspaceHwnd);
            if (_hwndSource != null)
                _hwndSource.AddHook(WndProc);

            _shortcutService = new ShortcutService(_workspaceHwnd);
            _shortcutBindings = _shortcutService.CreateDefaultBindings();
            string shortcutSummary = ApplyActiveShortcutBindings();
            StatusText.Text = shortcutSummary + " Press Ctrl+Shift+F1...F" + _cellCount + " while the target app is foreground.";
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            EnsureActiveCellControls();
            BuildAdaptiveLayout(null);
            _initialLayoutApplied = true;
            _windowHealthTimer.Start();

            bool hookStarted = _layoutLockService.Start();
            ScheduleGeometrySync();
            ScheduleEndpointGroupNormalize();

            StatusText.Text = hookStarted
                ? "Ready. Adaptive tiled layout + Endpoint Z-order Group active. Press Ctrl+Shift+F1...F" + _cellCount + "."
                : "Ready, but one or more WinEvent hooks could not start; periodic fallback remains active.";
        }

        private void EnsureActiveCellControls()
        {
            for (int i = 1; i <= _cellCount; i++)
            {
                if (_cells.ContainsKey(i))
                    continue;

                var cell = new CellControl(i);
                cell.UnbindRequested += Cell_UnbindRequested;
                cell.CloseRequested += Cell_CloseRequested;
                _cells.Add(i, cell);
            }

            foreach (int cellId in _cells.Keys.Where(x => x > _cellCount).ToArray())
            {
                CellControl cell = _cells[cellId];
                cell.UnbindRequested -= Cell_UnbindRequested;
                cell.CloseRequested -= Cell_CloseRequested;
                _cells.Remove(cellId);
            }
        }

        private static int[] GetRowCellCounts(int cellCount)
        {
            switch (cellCount)
            {
                case 4: return new[] { 2, 2 };
                case 5: return new[] { 3, 2 };
                case 6: return new[] { 3, 3 };
                case 7: return new[] { 4, 3 };
                case 8: return new[] { 4, 4 };
                case 9: return new[] { 3, 3, 3 };
                case 10: return new[] { 4, 3, 3 };
                case 11: return new[] { 4, 4, 3 };
                default: return new[] { 4, 4, 4 };
            }
        }

        private void BuildAdaptiveLayout(GridLayoutState requestedState)
        {
            _buildingGrid = true;
            try
            {
                EnsureActiveCellControls();
                foreach (Grid oldRowGrid in _rowGrids.Values)
                    oldRowGrid.Children.Clear();
                WorkspaceGrid.Children.Clear();
                WorkspaceGrid.ColumnDefinitions.Clear();
                WorkspaceGrid.RowDefinitions.Clear();
                _rowGrids.Clear();
                _rowCellIds.Clear();

                WorkspaceGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                int[] rowCellCounts = GetRowCellCounts(_cellCount);
                _rowCount = rowCellCounts.Length;
                List<AdaptiveRowLayoutState> savedRows = GetCompatibleSavedRows(requestedState, rowCellCounts);

                int nextCellId = 1;
                for (int logicalRow = 0; logicalRow < _rowCount; logicalRow++)
                {
                    double rowWeight = 1.0;
                    if (savedRows != null)
                        rowWeight = Math.Max(1.0, savedRows[logicalRow].HeightWeight);
                    else if (requestedState != null && requestedState.RowWeights != null && requestedState.RowWeights.Count == _rowCount)
                        rowWeight = Math.Max(1.0, requestedState.RowWeights[logicalRow]);

                    WorkspaceGrid.RowDefinitions.Add(new RowDefinition
                    {
                        Height = new GridLength(rowWeight, GridUnitType.Star),
                        MinHeight = 115
                    });

                    var rowGrid = new Grid
                    {
                        ClipToBounds = true,
                        Background = Brushes.Transparent
                    };
                    _rowGrids[logicalRow] = rowGrid;
                    _rowCellIds[logicalRow] = new List<int>();

                    int cellsInRow = rowCellCounts[logicalRow];
                    AdaptiveRowLayoutState savedRow = savedRows == null ? null : savedRows[logicalRow];

                    for (int column = 0; column < cellsInRow; column++)
                    {
                        double columnWeight = 1.0;
                        if (savedRow != null && savedRow.ColumnWeights.Count == cellsInRow)
                            columnWeight = Math.Max(1.0, savedRow.ColumnWeights[column]);
                        else if (requestedState != null && requestedState.ColumnWeights != null && requestedState.ColumnWeights.Count >= cellsInRow)
                            columnWeight = Math.Max(1.0, requestedState.ColumnWeights[column]);

                        rowGrid.ColumnDefinitions.Add(new ColumnDefinition
                        {
                            Width = new GridLength(columnWeight, GridUnitType.Star),
                            MinWidth = 155
                        });

                        int cellId = nextCellId++;
                        _rowCellIds[logicalRow].Add(cellId);
                        CellControl cell = _cells[cellId];
                        Grid.SetColumn(cell, column * 2);
                        Grid.SetRow(cell, 0);
                        cell.Margin = new Thickness(0);
                        rowGrid.Children.Add(cell);

                        if (column < cellsInRow - 1)
                        {
                            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(SplitterSize) });
                            GridSplitter splitter = CreateVerticalSplitter();
                            Grid.SetColumn(splitter, column * 2 + 1);
                            Grid.SetRow(splitter, 0);
                            Panel.SetZIndex(splitter, 10);
                            rowGrid.Children.Add(splitter);
                        }
                    }

                    Grid.SetRow(rowGrid, logicalRow * 2);
                    Grid.SetColumn(rowGrid, 0);
                    WorkspaceGrid.Children.Add(rowGrid);

                    if (logicalRow < _rowCount - 1)
                    {
                        WorkspaceGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(SplitterSize) });
                        GridSplitter horizontalSplitter = CreateHorizontalSplitter();
                        Grid.SetRow(horizontalSplitter, logicalRow * 2 + 1);
                        Grid.SetColumn(horizontalSplitter, 0);
                        Panel.SetZIndex(horizontalSplitter, 20);
                        WorkspaceGrid.Children.Add(horizontalSplitter);
                    }
                }
            }
            finally
            {
                _buildingGrid = false;
            }

            ScheduleGeometrySync();
            ScheduleEndpointGroupNormalize();
        }

        private List<AdaptiveRowLayoutState> GetCompatibleSavedRows(GridLayoutState requestedState, int[] rowCellCounts)
        {
            if (requestedState == null || requestedState.RowLayouts == null || requestedState.RowLayouts.Count != rowCellCounts.Length)
                return null;

            int nextCellId = 1;
            for (int row = 0; row < rowCellCounts.Length; row++)
            {
                AdaptiveRowLayoutState saved = requestedState.RowLayouts[row];
                if (saved == null || saved.CellIds == null || saved.ColumnWeights == null)
                    return null;
                if (saved.CellIds.Count != rowCellCounts[row] || saved.ColumnWeights.Count != rowCellCounts[row])
                    return null;

                for (int col = 0; col < rowCellCounts[row]; col++)
                {
                    if (saved.CellIds[col] != nextCellId++)
                        return null;
                }
            }

            return requestedState.RowLayouts;
        }

        private GridSplitter CreateVerticalSplitter()
        {
            var splitter = new GridSplitter
            {
                Width = SplitterSize,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Background = new SolidColorBrush(Color.FromRgb(58, 67, 81)),
                ResizeDirection = GridResizeDirection.Columns,
                ResizeBehavior = GridResizeBehavior.PreviousAndNext,
                ShowsPreview = false
            };
            splitter.DragDelta += GridSplitter_DragDelta;
            splitter.DragCompleted += GridSplitter_DragCompleted;
            return splitter;
        }

        private GridSplitter CreateHorizontalSplitter()
        {
            var splitter = new GridSplitter
            {
                Height = SplitterSize,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Background = new SolidColorBrush(Color.FromRgb(58, 67, 81)),
                ResizeDirection = GridResizeDirection.Rows,
                ResizeBehavior = GridResizeBehavior.PreviousAndNext,
                ShowsPreview = false
            };
            splitter.DragDelta += GridSplitter_DragDelta;
            splitter.DragCompleted += GridSplitter_DragCompleted;
            return splitter;
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == NativeMethods.WM_ENTERSIZEMOVE)
            {
                QueueNativeLayoutCommit(false);
            }
            else if (msg == NativeMethods.WM_EXITSIZEMOVE)
            {
                QueueNativeLayoutCommit(true);
            }

            if (msg == NativeMethods.WM_HOTKEY && _shortcutService != null)
            {
                int cellId = _shortcutService.CellIdFromHotKeyId(wParam.ToInt32());
                if (cellId > 0 && cellId <= _cellCount)
                {
                    BindForegroundToCell(cellId);
                    handled = true;
                }
            }
            return IntPtr.Zero;
        }

        private void BindForegroundToCell(int cellId)
        {
            CellControl targetCell;
            if (cellId < 1 || cellId > _cellCount || !_cells.TryGetValue(cellId, out targetCell))
            {
                StatusText.Text = "Cell " + cellId + " is not active.";
                return;
            }

            IntPtr target = _windowCoordinator.GetForegroundWindow();
            if (!_windowCoordinator.IsValidWindow(target))
            {
                StatusText.Text = "Cell " + cellId + ": no valid foreground window.";
                return;
            }

            if (_windowCoordinator.IsCurrentProcessWindow(target))
            {
                StatusText.Text = "Cell " + cellId + ": Native Endpoint Workspace windows cannot be bound as endpoints.";
                return;
            }

            NativeEndpoint existing = _registry.GetByHandle(target);
            if (existing != null)
            {
                StatusText.Text = existing.CellId == cellId
                    ? "Cell " + cellId + ": this HWND is already bound here."
                    : "Rejected: HWND is already bound to Cell " + existing.CellId + ".";
                return;
            }

            NativeEndpoint endpoint = _windowCoordinator.DescribeWindow(cellId, target);
            NativeEndpoint old = _registry.Bind(cellId, endpoint);
            targetCell.SetEndpoint(endpoint);
            RepositionCellEndpoint(cellId);
            RequestEndpointResync(true);

            if (old == null)
                StatusText.Text = "Bound " + endpoint.DisplayName + " to Cell " + cellId + ". Adaptive Layout Lock active.";
            else
                StatusText.Text = "Cell " + cellId + " rebound to " + endpoint.DisplayName + "; previous endpoint was unbound, not closed.";
        }

        private void Cell_UnbindRequested(object sender, EventArgs e)
        {
            var cell = sender as CellControl;
            if (cell == null) return;
            NativeEndpoint old = _registry.UnbindCell(cell.CellId);
            cell.SetEndpoint(null);
            StatusText.Text = old == null
                ? "Cell " + cell.CellId + " is already unbound."
                : "Unbound " + old.DisplayName + " from Cell " + cell.CellId + "; window remains open and Layout Lock is released.";
        }

        private void Cell_CloseRequested(object sender, EventArgs e)
        {
            var cell = sender as CellControl;
            if (cell == null) return;
            NativeEndpoint old = _registry.UnbindCell(cell.CellId);
            cell.SetEndpoint(null);
            if (old == null)
            {
                StatusText.Text = "Cell " + cell.CellId + " has no endpoint to close.";
                return;
            }

            _windowCoordinator.RequestClose(old.Handle);
            StatusText.Text = "WM_CLOSE requested for " + old.DisplayName + "; Cell " + cell.CellId + " unbound.";
        }

        private void RepositionCellEndpoint(int cellId)
        {
            NativeEndpoint endpoint = _registry.GetByCell(cellId);
            CellControl cell;
            if (endpoint == null || !_cells.TryGetValue(cellId, out cell)) return;
            if (!_windowCoordinator.IsValidWindow(endpoint.Handle)) return;
            if (WindowState == WindowState.Minimized) return;

            FrameworkElement host = cell.EndpointHostElement;
            if (!host.IsVisible || host.ActualWidth < 2 || host.ActualHeight < 2)
                return;

            Rect bounds = GetElementScreenBounds(host);
            _locationCorrectionSuppressedUntil[endpoint.Handle] = DateTime.UtcNow.AddMilliseconds(180);
            _windowCoordinator.SyncToRectangle(endpoint.Handle,
                (int)Math.Round(bounds.Left),
                (int)Math.Round(bounds.Top),
                (int)Math.Round(bounds.Width),
                (int)Math.Round(bounds.Height));
        }

        private void SyncAllEndpointGeometry()
        {
            if (!_initialLayoutApplied || _buildingGrid || WindowState == WindowState.Minimized || _syncingEndpoints)
                return;

            _syncingEndpoints = true;
            try
            {
                foreach (NativeEndpoint endpoint in _registry.All().Where(x => x.CellId <= _cellCount).OrderBy(x => x.CellId).ToArray())
                    RepositionCellEndpoint(endpoint.CellId);
            }
            finally
            {
                _syncingEndpoints = false;
            }
        }

        private void ScheduleGeometrySync()
        {
            QueueNativeLayoutCommit(false);
        }

        private void RequestEndpointResync(bool authoritativeFinal)
        {
            QueueNativeLayoutCommit(authoritativeFinal);
        }

        // rc06: do not infer layout completion from elapsed time. WPF tells us whenever its
        // arranged geometry changes. A compact geometry fingerprint prevents LayoutUpdated
        // from becoming a SetWindowPos feedback loop. External-window movement does not alter
        // this fingerprint, so our native commits cannot retrigger themselves indefinitely.
        private void WorkspaceGrid_LayoutUpdated(object sender, EventArgs e)
        {
            if (!_initialLayoutApplied || _buildingGrid || _committingNativeLayout || _workspaceCloseAccepted || WindowState == WindowState.Minimized)
                return;

            string fingerprint = CaptureLayoutFingerprint();
            if (string.Equals(fingerprint, _lastObservedLayoutFingerprint, StringComparison.Ordinal))
                return;

            _lastObservedLayoutFingerprint = fingerprint;
            QueueNativeLayoutCommit(false);
        }

        private string CaptureLayoutFingerprint()
        {
            var parts = new List<string>();
            parts.Add(Math.Round(Left, 1).ToString(System.Globalization.CultureInfo.InvariantCulture));
            parts.Add(Math.Round(Top, 1).ToString(System.Globalization.CultureInfo.InvariantCulture));
            parts.Add(Math.Round(ActualWidth, 1).ToString(System.Globalization.CultureInfo.InvariantCulture));
            parts.Add(Math.Round(ActualHeight, 1).ToString(System.Globalization.CultureInfo.InvariantCulture));
            parts.Add(((int)WindowState).ToString(System.Globalization.CultureInfo.InvariantCulture));

            foreach (KeyValuePair<int, CellControl> pair in _cells.OrderBy(x => x.Key))
            {
                if (pair.Key > _cellCount)
                    continue;

                FrameworkElement host = pair.Value.EndpointHostElement;
                if (host == null || !host.IsVisible)
                {
                    parts.Add(pair.Key + ":hidden");
                    continue;
                }

                try
                {
                    Point local = host.TransformToAncestor(WorkspaceGrid).Transform(new Point(0, 0));
                    parts.Add(pair.Key + ":" +
                        Math.Round(local.X, 1).ToString(System.Globalization.CultureInfo.InvariantCulture) + "," +
                        Math.Round(local.Y, 1).ToString(System.Globalization.CultureInfo.InvariantCulture) + "," +
                        Math.Round(host.ActualWidth, 1).ToString(System.Globalization.CultureInfo.InvariantCulture) + "," +
                        Math.Round(host.ActualHeight, 1).ToString(System.Globalization.CultureInfo.InvariantCulture));
                }
                catch (InvalidOperationException)
                {
                    parts.Add(pair.Key + ":pending");
                }
            }

            return string.Join("|", parts);
        }

        private void QueueNativeLayoutCommit(bool authoritativeFinal)
        {
            if (!_initialLayoutApplied || !IsLoaded || _buildingGrid || _workspaceCloseAccepted || WindowState == WindowState.Minimized)
                return;

            if (_nativeLayoutCommitQueued)
                return;

            _nativeLayoutCommitQueued = true;
            Dispatcher.BeginInvoke(new Action(delegate
            {
                _nativeLayoutCommitQueued = false;
                CommitNativeLayout(authoritativeFinal);
            }), DispatcherPriority.Render);
        }

        private void CommitNativeLayout(bool authoritativeFinal)
        {
            if (!_initialLayoutApplied || _buildingGrid || _workspaceCloseAccepted || WindowState == WindowState.Minimized || _committingNativeLayout)
                return;

            _committingNativeLayout = true;
            try
            {
                // Force the current WPF arrangement to be authoritative before screen-space
                // rectangles are read. This is a single commit path for workspace move/resize,
                // maximize/restore, splitter drag, explicit resync, and layout-lock correction.
                WorkspaceGrid.UpdateLayout();
                SyncAllEndpointGeometry();
                NormalizeEndpointZOrderGroup();
                _lastObservedLayoutFingerprint = CaptureLayoutFingerprint();
            }
            finally
            {
                _committingNativeLayout = false;
            }

            if (authoritativeFinal)
                StatusText.Text = "Native layout committed: bound endpoints reapplied to current Cell geometry and Workspace Z-order.";
        }

        private bool IsWorkspaceGroupForeground(IntPtr foreground)
        {
            if (foreground == IntPtr.Zero)
                return false;
            if (foreground == _workspaceHwnd)
                return true;
            return _registry.GetByHandle(foreground) != null;
        }

        private void NormalizeEndpointZOrderGroup()
        {
            if (!_initialLayoutApplied || _buildingGrid || WindowState == WindowState.Minimized || _normalizingZOrder)
                return;

            IntPtr foreground = _windowCoordinator.GetForegroundWindow();
            if (!IsWorkspaceGroupForeground(foreground))
                return;

            NativeEndpoint[] endpoints = _registry.All()
                .Where(x => x.CellId <= _cellCount && _windowCoordinator.IsValidWindow(x.Handle) && !_windowCoordinator.IsMinimized(x.Handle))
                .OrderBy(x => x.CellId)
                .ToArray();
            if (endpoints.Length == 0)
                return;

            _normalizingZOrder = true;
            try
            {
                // Build an explicit top-to-bottom endpoint group. The foreground endpoint, if
                // any, is first; all remaining endpoints follow in Cell order. We raise from
                // bottom to top so the final order is deterministic without activation.
                var desiredTopToBottom = new List<NativeEndpoint>();
                NativeEndpoint activeEndpoint = endpoints.FirstOrDefault(x => x.Handle == foreground);
                if (activeEndpoint != null)
                    desiredTopToBottom.Add(activeEndpoint);
                desiredTopToBottom.AddRange(endpoints.Where(x => activeEndpoint == null || x.Handle != activeEndpoint.Handle));

                for (int i = desiredTopToBottom.Count - 1; i >= 0; i--)
                    _windowCoordinator.RaiseWithoutActivate(desiredTopToBottom[i].Handle);

                // Critical rc06 invariant: the opaque WPF Workspace itself must be explicitly
                // placed immediately below the lowest bound endpoint. Merely raising endpoints
                // was not sufficient during native move/resize activation on the real machine.
                IntPtr lowestEndpoint = desiredTopToBottom[desiredTopToBottom.Count - 1].Handle;
                _windowCoordinator.PlaceBehindWithoutActivate(_workspaceHwnd, lowestEndpoint);
            }
            finally
            {
                _normalizingZOrder = false;
            }
        }

        private void ScheduleEndpointGroupNormalize()
        {
            if (!_initialLayoutApplied && !IsLoaded)
                return;
            if (_groupNormalizeScheduled)
                return;

            _groupNormalizeScheduled = true;
            Dispatcher.BeginInvoke(new Action(delegate
            {
                _groupNormalizeScheduled = false;
                NormalizeEndpointZOrderGroup();
            }), DispatcherPriority.Background);
        }

        private static Rect GetElementScreenBounds(FrameworkElement element)
        {
            Point topLeft = element.PointToScreen(new Point(0, 0));
            double width = Math.Max(1, element.ActualWidth);
            double height = Math.Max(1, element.ActualHeight);

            PresentationSource source = PresentationSource.FromVisual(element);
            if (source != null && source.CompositionTarget != null)
            {
                Matrix transform = source.CompositionTarget.TransformToDevice;
                width *= transform.M11;
                height *= transform.M22;
            }

            return new Rect(topLeft.X, topLeft.Y, width, height);
        }

        private void GridSplitter_DragDelta(object sender, DragDeltaEventArgs e)
        {
            QueueNativeLayoutCommit(false);
        }

        private void GridSplitter_DragCompleted(object sender, DragCompletedEventArgs e)
        {
            QueueNativeLayoutCommit(true);
        }

        private void LayoutLockService_WindowLocationChanged(IntPtr hwnd)
        {
            // WinEvent callbacks are not guaranteed to run on the WPF UI thread. Never touch
            // registry/WPF state directly from the callback thread.
            Dispatcher.BeginInvoke(new Action(delegate
            {
                if (_workspaceCloseAccepted || WindowState == WindowState.Minimized)
                    return;

                NativeEndpoint endpoint = _registry.GetByHandle(hwnd);
                if (endpoint == null)
                    return;

                DateTime suppressedUntil;
                if (_locationCorrectionSuppressedUntil.TryGetValue(hwnd, out suppressedUntil) && DateTime.UtcNow < suppressedUntil)
                    return;

                if (!_pendingLocationCorrections.Add(hwnd))
                    return;

                Dispatcher.BeginInvoke(new Action(delegate
                {
                    _pendingLocationCorrections.Remove(hwnd);
                    if (_workspaceCloseAccepted || _syncingEndpoints)
                        return;

                    NativeEndpoint current = _registry.GetByHandle(hwnd);
                    if (current != null)
                        RepositionCellEndpoint(current.CellId);
                }), DispatcherPriority.Background);
            }), DispatcherPriority.Background);
        }

        private void LayoutLockService_ForegroundChanged(IntPtr hwnd)
        {
            Dispatcher.BeginInvoke(new Action(delegate
            {
                if (_workspaceCloseAccepted)
                    return;
                if (hwnd == _workspaceHwnd || _registry.GetByHandle(hwnd) != null)
                    ScheduleEndpointGroupNormalize();
            }), DispatcherPriority.Background);
        }

        private void WindowHealthTimer_Tick(object sender, EventArgs e)
        {
            foreach (NativeEndpoint endpoint in _registry.All().ToArray())
            {
                if (_windowCoordinator.IsValidWindow(endpoint.Handle))
                    continue;

                _registry.UnbindCell(endpoint.CellId);
                _locationCorrectionSuppressedUntil.Remove(endpoint.Handle);
                _pendingLocationCorrections.Remove(endpoint.Handle);
                CellControl cell;
                if (_cells.TryGetValue(endpoint.CellId, out cell))
                    cell.SetEndpoint(null);
                StatusText.Text = "Cell " + endpoint.CellId + ": endpoint window disappeared and was automatically unbound.";
            }

            foreach (IntPtr stale in _locationCorrectionSuppressedUntil.Where(x => DateTime.UtcNow > x.Value.AddSeconds(2)).Select(x => x.Key).ToArray())
                _locationCorrectionSuppressedUntil.Remove(stale);

            // Low-frequency fallback for apps that do not emit location/layout events.
            // Uses the same single rc06 commit path; no alternate timer-specific behavior.
            if (IsWorkspaceGroupForeground(_windowCoordinator.GetForegroundWindow()))
                CommitNativeLayout(false);
        }

        private void Identify_Click(object sender, RoutedEventArgs e)
        {
            foreach (CellControl cell in _cells.Values.OrderBy(x => x.CellId))
            {
                Rect bounds = GetElementScreenBounds(cell);
                var overlay = new IdentifyOverlayWindow(cell.CellId, bounds);
                overlay.Show();
            }
            StatusText.Text = "Identify overlays shown for all " + _cellCount + " active Cells.";
        }

        private void MinimizeGroup_Click(object sender, RoutedEventArgs e)
        {
            foreach (NativeEndpoint endpoint in _registry.All().ToArray())
                _windowCoordinator.Minimize(endpoint.Handle);
            StatusText.Text = "Minimize requested for all bound endpoints.";
        }

        private void RestoreGroup_Click(object sender, RoutedEventArgs e)
        {
            foreach (NativeEndpoint endpoint in _registry.All().ToArray())
                _windowCoordinator.Restore(endpoint.Handle);
            RequestEndpointResync(true);
            StatusText.Text = "Restore requested for all bound endpoints.";
        }

        private void ResetTiling_Click(object sender, RoutedEventArgs e)
        {
            BuildAdaptiveLayout(null);
            StatusText.Text = "Adaptive tiled layout reset to equal row/Cell proportions; endpoint bindings were preserved.";
        }

        private void ResyncEndpoints_Click(object sender, RoutedEventArgs e)
        {
            CommitNativeLayout(true);
            StatusText.Text = "Native layout commit completed; bindings/apps were not restarted or reloaded.";
        }

        private void CellCountComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_updatingCellCountUi || CellCountComboBox.SelectedItem == null)
                return;

            int requested = (int)CellCountComboBox.SelectedItem;
            if (!_initialLayoutApplied)
            {
                _cellCount = requested;
                return;
            }

            if (requested == _cellCount)
                return;

            if (!TryChangeCellCount(requested))
            {
                _updatingCellCountUi = true;
                try { CellCountComboBox.SelectedItem = _cellCount; }
                finally { _updatingCellCountUi = false; }
            }
        }

        private bool TryChangeCellCount(int requested)
        {
            requested = Math.Max(MinCellCount, Math.Min(MaxCellCount, requested));
            if (requested == _cellCount)
                return true;

            NativeEndpoint[] removedEndpoints = _registry.All().Where(x => x.CellId > requested).OrderBy(x => x.CellId).ToArray();
            if (removedEndpoints.Length > 0)
            {
                MessageBoxResult result = MessageBox.Show(this,
                    "Reducing the Cell count from " + _cellCount + " to " + requested + " will unbind " +
                    removedEndpoints.Length + " endpoint(s) in removed Cells.\n\n" +
                    "The external applications will remain open and will not be closed.\n\nContinue?",
                    "Reduce Cell Count",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);
                if (result != MessageBoxResult.Yes)
                    return false;
            }

            foreach (NativeEndpoint endpoint in removedEndpoints)
            {
                _registry.UnbindCell(endpoint.CellId);
                _locationCorrectionSuppressedUntil.Remove(endpoint.Handle);
                _pendingLocationCorrections.Remove(endpoint.Handle);
                CellControl oldCell;
                if (_cells.TryGetValue(endpoint.CellId, out oldCell))
                    oldCell.SetEndpoint(null);
            }

            _cellCount = requested;
            EnsureActiveCellControls();
            BuildAdaptiveLayout(null);
            string shortcutSummary = ApplyActiveShortcutBindings();

            StatusText.Text = "Cell count changed to " + _cellCount + ". " + shortcutSummary +
                              (removedEndpoints.Length > 0 ? " Removed-Cell endpoints were unbound, not closed." : string.Empty);
            return true;
        }

        private string ApplyActiveShortcutBindings()
        {
            if (_shortcutService == null || _shortcutBindings == null)
                return "Shortcuts are not initialized.";

            IList<ShortcutBinding> active = _shortcutBindings.Where(x => x.CellId >= 1 && x.CellId <= _cellCount)
                                                              .OrderBy(x => x.CellId)
                                                              .ToList();
            string summary;
            _shortcutService.ApplyBindings(active, out summary);
            foreach (ShortcutBinding inactive in _shortcutBindings.Where(x => x.CellId > _cellCount))
                inactive.Status = "Inactive (Cell disabled)";
            return summary;
        }

        private GridLayoutState CaptureGridLayoutState()
        {
            var state = new GridLayoutState
            {
                Rows = _rowCount,
                Columns = _rowCellIds.Count == 0 ? 0 : _rowCellIds.Values.Max(x => x.Count)
            };

            for (int row = 0; row < _rowCount; row++)
            {
                RowDefinition rootRow = WorkspaceGrid.RowDefinitions[row * 2];
                state.RowWeights.Add(Math.Max(1.0, rootRow.ActualHeight));

                Grid rowGrid;
                List<int> cellIds;
                if (!_rowGrids.TryGetValue(row, out rowGrid) || !_rowCellIds.TryGetValue(row, out cellIds))
                    continue;

                var rowState = new AdaptiveRowLayoutState
                {
                    HeightWeight = Math.Max(1.0, rootRow.ActualHeight),
                    CellIds = new List<int>(cellIds)
                };

                for (int column = 0; column < cellIds.Count; column++)
                {
                    double width = Math.Max(1.0, rowGrid.ColumnDefinitions[column * 2].ActualWidth);
                    rowState.ColumnWeights.Add(width);
                    if (row == 0)
                        state.ColumnWeights.Add(width);
                }
                state.RowLayouts.Add(rowState);
            }

            return state;
        }

        private void SaveLayout_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog
            {
                Title = "Save Native Endpoint Workspace Layout",
                Filter = "Native Endpoint Workspace Layout (*.newlayout.xml)|*.newlayout.xml|XML files (*.xml)|*.xml|All files (*.*)|*.*",
                FileName = "workspace.newlayout.xml"
            };
            if (dialog.ShowDialog(this) != true) return;

            try
            {
                var state = new WorkspaceState
                {
                    Version = "0.0.1rc06",
                    CellCount = _cellCount,
                    Grid = CaptureGridLayoutState(),
                    Cells = new List<CellLayoutState>(),
                    Shortcuts = _shortcutBindings.Select(x => x.Clone()).OrderBy(x => x.CellId).ToList()
                };
                _layoutService.Save(dialog.FileName, state);
                StatusText.Text = "Adaptive tiled layout saved with " + _cellCount + " Cells. HWND bindings are runtime-only and were not persisted.";
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Save Layout Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadLayout_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Load Native Endpoint Workspace Layout",
                Filter = "Native Endpoint Workspace Layout (*.newlayout.xml)|*.newlayout.xml|XML files (*.xml)|*.xml|All files (*.*)|*.*"
            };
            if (dialog.ShowDialog(this) != true) return;

            try
            {
                WorkspaceState state = _layoutService.Load(dialog.FileName);

                // Loading a layout releases current bindings but does not close those apps.
                _registry.Clear();
                _locationCorrectionSuppressedUntil.Clear();
                _pendingLocationCorrections.Clear();
                foreach (CellControl cell in _cells.Values)
                    cell.SetEndpoint(null);

                _cellCount = ResolveLoadedCellCount(state);
                _updatingCellCountUi = true;
                try { CellCountComboBox.SelectedItem = _cellCount; }
                finally { _updatingCellCountUi = false; }

                EnsureActiveCellControls();
                BuildAdaptiveLayout(state == null ? null : state.Grid);

                _shortcutBindings = MergeLoadedShortcutBindings(state == null ? null : state.Shortcuts);
                string shortcutSummary = ApplyActiveShortcutBindings();
                StatusText.Text = "Layout loaded with " + _cellCount + " adaptive tiled Cells; endpoint HWNDs intentionally not restored. " + shortcutSummary;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Load Layout Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static int ResolveLoadedCellCount(WorkspaceState state)
        {
            if (state == null)
                return DefaultCellCount;

            if (state.CellCount >= MinCellCount && state.CellCount <= MaxCellCount)
                return state.CellCount;

            if (state.Cells != null && state.Cells.Count >= MinCellCount)
                return Math.Max(MinCellCount, Math.Min(MaxCellCount, state.Cells.Count));

            return DefaultCellCount;
        }

        private IList<ShortcutBinding> MergeLoadedShortcutBindings(IList<ShortcutBinding> loaded)
        {
            IList<ShortcutBinding> defaults = _shortcutService.CreateDefaultBindings();
            var byCell = defaults.ToDictionary(x => x.CellId, x => x);
            if (loaded != null)
            {
                foreach (ShortcutBinding binding in loaded.Where(x => x.CellId >= 1 && x.CellId <= MaxCellCount))
                    byCell[binding.CellId] = binding.Clone();
            }
            return byCell.Values.OrderBy(x => x.CellId).ToList();
        }

        private void ShortcutSettings_Click(object sender, RoutedEventArgs e)
        {
            if (_shortcutService == null) return;

            IList<ShortcutBinding> activeBindings = _shortcutBindings.Where(x => x.CellId <= _cellCount)
                                                                      .OrderBy(x => x.CellId)
                                                                      .Select(x => x.Clone())
                                                                      .ToList();
            var dialog = new ShortcutSettingsWindow(_shortcutService, activeBindings);
            dialog.ShowDialog();

            foreach (ShortcutBinding applied in dialog.AppliedBindings)
            {
                ShortcutBinding existing = _shortcutBindings.FirstOrDefault(x => x.CellId == applied.CellId);
                if (existing == null)
                {
                    _shortcutBindings.Add(applied.Clone());
                    continue;
                }

                existing.Control = applied.Control;
                existing.Shift = applied.Shift;
                existing.Alt = applied.Alt;
                existing.Win = applied.Win;
                existing.KeyCode = applied.KeyCode;
                existing.Status = applied.Status;
            }

            int conflicts = _shortcutBindings.Count(x => x.CellId <= _cellCount &&
                !string.Equals(x.Status, "Registered", StringComparison.OrdinalIgnoreCase));
            StatusText.Text = conflicts == 0
                ? "Shortcut settings applied for all " + _cellCount + " active Cells."
                : "Shortcut settings contain " + conflicts + " conflict(s)/inactive active-Cell shortcut(s).";
        }

        private void MainWindow_Activated(object sender, EventArgs e)
        {
            // Give the group an immediate chance to recover from the Workspace activation,
            // then run the settled authoritative pass shortly afterward.
            ScheduleEndpointGroupNormalize();
            RequestEndpointResync(true);
        }

        private void MainWindow_LocationChanged(object sender, EventArgs e)
        {
            RequestEndpointResync(false);
        }

        private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            RequestEndpointResync(false);
        }

        private void MainWindow_StateChanged(object sender, EventArgs e)
        {
            if (!_initialLayoutApplied)
                return;

            if (WindowState == WindowState.Minimized)
            {
                _minimizedWithWorkspace.Clear();
                foreach (NativeEndpoint endpoint in _registry.All().ToArray())
                {
                    if (!_windowCoordinator.IsMinimized(endpoint.Handle))
                    {
                        _minimizedWithWorkspace.Add(endpoint.Handle);
                        _windowCoordinator.Minimize(endpoint.Handle);
                    }
                }
                return;
            }

            foreach (IntPtr hwnd in _minimizedWithWorkspace.ToArray())
                _windowCoordinator.Restore(hwnd);
            _minimizedWithWorkspace.Clear();
            RequestEndpointResync(true);
        }

        private void MainWindow_Closing(object sender, CancelEventArgs e)
        {
            if (_workspaceCloseAccepted)
                return;

            NativeEndpoint[] endpoints = _registry.All().Where(x => _windowCoordinator.IsValidWindow(x.Handle)).ToArray();
            if (endpoints.Length == 0)
            {
                _workspaceCloseAccepted = true;
                return;
            }

            MessageBoxResult result = MessageBox.Show(this,
                "Close Native Endpoint Workspace?\n\n" +
                endpoints.Length + " currently bound application window(s) will also receive a graceful WM_CLOSE request.\n\n" +
                "Applications with unsaved data may show their own Save/Cancel prompt.",
                "Close Workspace and Bound Apps",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
            {
                e.Cancel = true;
                return;
            }

            _workspaceCloseAccepted = true;
            _windowHealthTimer.Stop();
            _layoutLockService.Dispose();

            foreach (NativeEndpoint endpoint in endpoints)
                _windowCoordinator.RequestClose(endpoint.Handle);

            _registry.Clear();
        }

        private void MainWindow_Closed(object sender, EventArgs e)
        {
            _windowHealthTimer.Stop();
            WorkspaceGrid.LayoutUpdated -= WorkspaceGrid_LayoutUpdated;
            _layoutLockService.WindowLocationChanged -= LayoutLockService_WindowLocationChanged;
            _layoutLockService.ForegroundChanged -= LayoutLockService_ForegroundChanged;
            _layoutLockService.Dispose();

            if (_shortcutService != null)
                _shortcutService.Dispose();
            if (_hwndSource != null)
                _hwndSource.RemoveHook(WndProc);

            _registry.Clear();
        }
    }
}

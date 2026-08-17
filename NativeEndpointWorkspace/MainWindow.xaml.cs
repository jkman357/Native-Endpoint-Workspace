using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
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
        private const double DefaultCellMinWidth = 155.0;
        private const double DefaultRowMinHeight = 115.0;
        private const double SizeVerificationPositionTolerance = 20.0;
        private const double SizeVerificationGrowthTolerance = 8.0;
        private static readonly TimeSpan LocationCorrectionSuppression = TimeSpan.FromMilliseconds(180);
        private static readonly TimeSpan EndpointHealthInterval = TimeSpan.FromMilliseconds(1250);
        private static readonly TimeSpan EndpointSizeVerificationDelay = TimeSpan.FromMilliseconds(220);

        private readonly NativeWindowCoordinator _windowCoordinator = new NativeWindowCoordinator();
        private readonly EndpointRegistry _registry = new EndpointRegistry();
        private readonly LayoutService _layoutService = new LayoutService();
        private readonly EndpointLayoutLockService _layoutLockService = new EndpointLayoutLockService();
        private readonly Dictionary<int, CellControl> _cells = new Dictionary<int, CellControl>();
        private readonly Dictionary<int, Grid> _rowGrids = new Dictionary<int, Grid>();
        private readonly Dictionary<int, List<int>> _rowCellIds = new Dictionary<int, List<int>>();
        private readonly Dictionary<int, ColumnDefinition> _cellColumns = new Dictionary<int, ColumnDefinition>();
        private readonly Dictionary<int, int> _cellLogicalRows = new Dictionary<int, int>();
        private readonly Dictionary<int, double> _cellMinimumHostWidths = new Dictionary<int, double>();
        private readonly Dictionary<int, double> _cellMinimumHostHeights = new Dictionary<int, double>();
        private readonly Dictionary<IntPtr, DispatcherTimer> _sizeVerificationTimers = new Dictionary<IntPtr, DispatcherTimer>();
        private readonly Dictionary<IntPtr, DateTime> _locationCorrectionSuppressedUntil = new Dictionary<IntPtr, DateTime>();
        private readonly Dictionary<IntPtr, EndpointCorrectionState> _correctionStates = new Dictionary<IntPtr, EndpointCorrectionState>();
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
        private bool _queuedShowCompletionStatus;
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
            _layoutLockService.WindowDestroyed += LayoutLockService_WindowDestroyed;

            // Slow health fallback only. rc08 keeps rc06's deterministic LayoutUpdated
            // commit path and adds identity/backoff hardening around native endpoints.
            _windowHealthTimer = new DispatcherTimer { Interval = EndpointHealthInterval };
            _windowHealthTimer.Tick += WindowHealthTimer_Tick;
            WorkspaceGrid.LayoutUpdated += WorkspaceGrid_LayoutUpdated;
        }

        private void MainWindow_SourceInitialized(object sender, EventArgs e)
        {
            var helper = new WindowInteropHelper(this);
            _workspaceHwnd = helper.Handle;
            _layoutLockService.SetWorkspaceHandle(_workspaceHwnd);
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

            RefreshManagedHandleSnapshot();
            bool hookStarted = _layoutLockService.Start();
            ApplyEndpointMinimumConstraints();
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
                _cellColumns.Clear();
                _cellLogicalRows.Clear();

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
                        MinHeight = DefaultRowMinHeight
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

                        var cellColumn = new ColumnDefinition
                        {
                            Width = new GridLength(columnWeight, GridUnitType.Star),
                            MinWidth = DefaultCellMinWidth
                        };
                        rowGrid.ColumnDefinitions.Add(cellColumn);

                        int cellId = nextCellId++;
                        _cellColumns[cellId] = cellColumn;
                        _cellLogicalRows[cellId] = logicalRow;
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

            ApplyEndpointMinimumConstraints();
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
            if (existing != null && !_windowCoordinator.IsEndpointIdentityCurrent(existing, false))
            {
                UnbindStaleEndpoint(existing, "stale handle detected during bind");
                existing = null;
            }

            if (existing != null)
            {
                StatusText.Text = existing.CellId == cellId
                    ? "Cell " + cellId + ": this endpoint is already bound here."
                    : "Rejected: endpoint is already bound to Cell " + existing.CellId + ".";
                return;
            }

            NativeEndpoint endpoint = _windowCoordinator.DescribeWindow(cellId, target);
            NativeEndpoint old = _registry.Bind(cellId, endpoint);
            if (old != null)
                ClearEndpointRuntimeState(old);
            RefreshManagedHandleSnapshot();
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
            if (old != null)
                ClearEndpointRuntimeState(old);
            RefreshManagedHandleSnapshot();
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
            if (old != null)
                ClearEndpointRuntimeState(old);
            RefreshManagedHandleSnapshot();
            cell.SetEndpoint(null);
            if (old == null)
            {
                StatusText.Text = "Cell " + cell.CellId + " has no endpoint to close.";
                return;
            }

            bool closePosted = _windowCoordinator.RequestClose(old);
            StatusText.Text = closePosted
                ? "WM_CLOSE requested for " + old.DisplayName + "; Cell " + cell.CellId + " unbound."
                : "Cell " + cell.CellId + " unbound, but close was skipped because endpoint identity could not be revalidated.";
        }

        private void RefreshManagedHandleSnapshot()
        {
            _layoutLockService.UpdateManagedHandles(_registry.All().Select(x => x.Handle));
        }

        private void ClearEndpointRuntimeState(NativeEndpoint endpoint)
        {
            if (endpoint == null)
                return;

            _locationCorrectionSuppressedUntil.Remove(endpoint.Handle);
            _pendingLocationCorrections.Remove(endpoint.Handle);
            _correctionStates.Remove(endpoint.Handle);
            _minimizedWithWorkspace.Remove(endpoint.Handle);

            DispatcherTimer sizeTimer;
            if (_sizeVerificationTimers.TryGetValue(endpoint.Handle, out sizeTimer))
            {
                sizeTimer.Stop();
                _sizeVerificationTimers.Remove(endpoint.Handle);
            }

            _cellMinimumHostWidths.Remove(endpoint.CellId);
            _cellMinimumHostHeights.Remove(endpoint.CellId);
            ApplyEndpointMinimumConstraints();
        }

        private void ScheduleEndpointSizeVerification(NativeEndpoint endpoint)
        {
            if (endpoint == null || _workspaceCloseAccepted)
                return;

            DispatcherTimer oldTimer;
            if (_sizeVerificationTimers.TryGetValue(endpoint.Handle, out oldTimer))
                oldTimer.Stop();

            var timer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = EndpointSizeVerificationDelay
            };
            timer.Tick += delegate
            {
                timer.Stop();
                _sizeVerificationTimers.Remove(endpoint.Handle);
                VerifyEndpointSizeAccommodation(endpoint);
            };
            _sizeVerificationTimers[endpoint.Handle] = timer;
            timer.Start();
        }

        private void VerifyEndpointSizeAccommodation(NativeEndpoint endpoint)
        {
            if (endpoint == null || _workspaceCloseAccepted || WindowState == WindowState.Minimized)
                return;

            NativeEndpoint current = _registry.GetByCell(endpoint.CellId);
            if (current == null || current.Handle != endpoint.Handle || !_windowCoordinator.IsEndpointIdentityCurrent(current, false))
                return;

            Rect desired;
            if (!TryGetDesiredCellScreenRect(endpoint.CellId, out desired))
                return;

            int actualX, actualY, actualWidth, actualHeight;
            if (!_windowCoordinator.TryGetWindowRectangle(current, out actualX, out actualY, out actualWidth, out actualHeight))
                return;

            bool positionAccepted = Math.Abs(actualX - desired.Left) <= SizeVerificationPositionTolerance &&
                                    Math.Abs(actualY - desired.Top) <= SizeVerificationPositionTolerance;
            bool widthRejected = actualWidth > desired.Width + SizeVerificationGrowthTolerance;
            bool heightRejected = actualHeight > desired.Height + SizeVerificationGrowthTolerance;
            if (!positionAccepted || (!widthRejected && !heightRejected))
                return;

            if (ApplyEndpointSizeAccommodation(endpoint.CellId,
                widthRejected ? actualWidth : (int)Math.Ceiling(desired.Width),
                heightRejected ? actualHeight : (int)Math.Ceiling(desired.Height)))
            {
                GetCorrectionState(endpoint.Handle).Reset();
                StatusText.Text = "Cell " + endpoint.CellId + ": endpoint minimum size detected; Workspace/Cell allocation adjusted.";
                QueueNativeLayoutCommit(false);
            }
        }

        private bool TryGetDesiredCellScreenRect(int cellId, out Rect bounds)
        {
            bounds = Rect.Empty;
            CellControl cell;
            if (!_cells.TryGetValue(cellId, out cell))
                return false;

            FrameworkElement host = cell.EndpointHostElement;
            if (host == null || !host.IsVisible || host.ActualWidth < 2 || host.ActualHeight < 2)
                return false;

            bounds = GetElementScreenBounds(host);
            return !bounds.IsEmpty && bounds.Width > 1 && bounds.Height > 1;
        }

        private bool ApplyEndpointSizeAccommodation(int cellId, int requiredHostWidthPixels, int requiredHostHeightPixels)
        {
            CellControl cell;
            if (!_cells.TryGetValue(cellId, out cell))
                return false;

            FrameworkElement host = cell.EndpointHostElement;
            double scaleX, scaleY;
            GetDeviceScale(host, out scaleX, out scaleY);

            double requiredHostWidthDip = requiredHostWidthPixels / scaleX;
            double requiredHostHeightDip = requiredHostHeightPixels / scaleY;
            double outerWidthOverhead = Math.Max(0, cell.ActualWidth - host.ActualWidth);
            double outerHeightOverhead = Math.Max(0, cell.ActualHeight - host.ActualHeight);
            double requestedCellWidth = requiredHostWidthDip + outerWidthOverhead;
            double requestedRowHeight = requiredHostHeightDip + outerHeightOverhead;

            double oldWidth;
            double oldHeight;
            _cellMinimumHostWidths.TryGetValue(cellId, out oldWidth);
            _cellMinimumHostHeights.TryGetValue(cellId, out oldHeight);

            bool changed = requestedCellWidth > oldWidth + 1 || requestedRowHeight > oldHeight + 1;
            if (!changed)
                return false;

            _cellMinimumHostWidths[cellId] = Math.Max(oldWidth, requestedCellWidth);
            _cellMinimumHostHeights[cellId] = Math.Max(oldHeight, requestedRowHeight);
            ApplyEndpointMinimumConstraints();
            EnsureWorkspaceCapacityForMinimums();
            return true;
        }

        private void ApplyEndpointMinimumConstraints()
        {
            foreach (KeyValuePair<int, ColumnDefinition> pair in _cellColumns)
            {
                double required;
                pair.Value.MinWidth = _cellMinimumHostWidths.TryGetValue(pair.Key, out required)
                    ? Math.Max(DefaultCellMinWidth, required)
                    : DefaultCellMinWidth;
            }

            for (int logicalRow = 0; logicalRow < _rowCount; logicalRow++)
            {
                int rowDefinitionIndex = logicalRow * 2;
                if (rowDefinitionIndex >= WorkspaceGrid.RowDefinitions.Count)
                    continue;

                double requiredRowHeight = DefaultRowMinHeight;
                List<int> cellIds;
                if (_rowCellIds.TryGetValue(logicalRow, out cellIds))
                {
                    foreach (int cellId in cellIds)
                    {
                        double required;
                        if (_cellMinimumHostHeights.TryGetValue(cellId, out required))
                            requiredRowHeight = Math.Max(requiredRowHeight, required);
                    }
                }
                WorkspaceGrid.RowDefinitions[rowDefinitionIndex].MinHeight = requiredRowHeight;
            }
        }

        private void EnsureWorkspaceCapacityForMinimums()
        {
            if (!IsLoaded || WindowState != WindowState.Normal)
                return;

            double requiredGridWidth = 0;
            foreach (KeyValuePair<int, Grid> rowPair in _rowGrids)
            {
                double rowWidth = 0;
                foreach (ColumnDefinition column in rowPair.Value.ColumnDefinitions)
                    rowWidth += column.Width.IsAbsolute ? column.Width.Value : column.MinWidth;
                requiredGridWidth = Math.Max(requiredGridWidth, rowWidth);
            }

            double requiredGridHeight = 0;
            foreach (RowDefinition row in WorkspaceGrid.RowDefinitions)
                requiredGridHeight += row.Height.IsAbsolute ? row.Height.Value : row.MinHeight;

            double chromeWidth = Math.Max(0, ActualWidth - WorkspaceGrid.ActualWidth);
            double chromeHeight = Math.Max(0, ActualHeight - WorkspaceGrid.ActualHeight);
            double targetWidth = Math.Max(Width, requiredGridWidth + chromeWidth);
            double targetHeight = Math.Max(Height, requiredGridHeight + chromeHeight);

            int workLeft, workTop, workRight, workBottom;
            if (_windowCoordinator.TryGetMonitorWorkArea(_workspaceHwnd, out workLeft, out workTop, out workRight, out workBottom))
            {
                double scaleX, scaleY;
                GetDeviceScale(WorkspaceGrid, out scaleX, out scaleY);
                double maxWidth = Math.Max(MinWidth, (workRight - workLeft) / scaleX);
                double maxHeight = Math.Max(MinHeight, (workBottom - workTop) / scaleY);
                targetWidth = Math.Min(targetWidth, maxWidth);
                targetHeight = Math.Min(targetHeight, maxHeight);
            }

            if (targetWidth > Width + 1)
                Width = targetWidth;
            if (targetHeight > Height + 1)
                Height = targetHeight;
        }

        private static void GetDeviceScale(Visual visual, out double scaleX, out double scaleY)
        {
            scaleX = 1.0;
            scaleY = 1.0;
            PresentationSource source = PresentationSource.FromVisual(visual);
            if (source != null && source.CompositionTarget != null)
            {
                Matrix transform = source.CompositionTarget.TransformToDevice;
                if (transform.M11 > 0) scaleX = transform.M11;
                if (transform.M22 > 0) scaleY = transform.M22;
            }
        }

        private void UnbindStaleEndpoint(NativeEndpoint endpoint, string reason)
        {
            if (endpoint == null)
                return;

            NativeEndpoint current = _registry.GetByCell(endpoint.CellId);
            if (current == null || current.Handle != endpoint.Handle)
                return;

            _registry.UnbindCell(endpoint.CellId);
            ClearEndpointRuntimeState(endpoint);
            RefreshManagedHandleSnapshot();

            CellControl cell;
            if (_cells.TryGetValue(endpoint.CellId, out cell))
                cell.SetEndpoint(null);

            StatusText.Text = "Cell " + endpoint.CellId + ": endpoint unbound (" + reason + ").";
        }

        private EndpointCorrectionState GetCorrectionState(IntPtr hwnd)
        {
            EndpointCorrectionState state;
            if (!_correctionStates.TryGetValue(hwnd, out state))
            {
                state = new EndpointCorrectionState();
                _correctionStates[hwnd] = state;
            }
            return state;
        }

        private GeometrySyncResult RepositionCellEndpoint(int cellId)
        {
            int ignoredError;
            return RepositionCellEndpoint(cellId, out ignoredError);
        }

        private GeometrySyncResult RepositionCellEndpoint(int cellId, out int nativeErrorCode)
        {
            nativeErrorCode = 0;
            NativeEndpoint endpoint = _registry.GetByCell(cellId);
            CellControl cell;
            if (endpoint == null || !_cells.TryGetValue(cellId, out cell)) return GeometrySyncResult.Failed;
            if (WindowState == WindowState.Minimized) return GeometrySyncResult.SkippedMinimized;

            EndpointIdentityStatus identity = _windowCoordinator.ValidateEndpointIdentity(endpoint, false);
            if (identity != EndpointIdentityStatus.Current)
                return GeometrySyncResult.StaleEndpoint;

            FrameworkElement host = cell.EndpointHostElement;
            if (!host.IsVisible || host.ActualWidth < 2 || host.ActualHeight < 2)
                return GeometrySyncResult.Failed;

            Rect bounds = GetElementScreenBounds(host);
            int x = (int)Math.Round(bounds.Left);
            int y = (int)Math.Round(bounds.Top);
            int width = (int)Math.Round(bounds.Width);
            int height = (int)Math.Round(bounds.Height);

            EndpointCorrectionState correctionState = GetCorrectionState(endpoint.Handle);
            if (correctionState.IsBackedOff(DateTime.UtcNow))
                return GeometrySyncResult.Failed;

            _locationCorrectionSuppressedUntil[endpoint.Handle] = DateTime.UtcNow.Add(LocationCorrectionSuppression);
            GeometrySyncResult result = _windowCoordinator.SyncToRectangle(endpoint, x, y, width, height, out nativeErrorCode);
            if (result == GeometrySyncResult.AlreadyCorrect)
                correctionState.Reset();
            else if (result == GeometrySyncResult.Applied)
                ScheduleEndpointSizeVerification(endpoint);
            return result;
        }

        private NativeLayoutCommitResult SyncAllEndpointGeometry()
        {
            var commitResult = new NativeLayoutCommitResult();
            if (!_initialLayoutApplied || _buildingGrid || WindowState == WindowState.Minimized || _syncingEndpoints)
                return commitResult;

            var staleEndpoints = new List<NativeEndpoint>();
            _syncingEndpoints = true;
            try
            {
                foreach (NativeEndpoint endpoint in _registry.All().Where(x => x.CellId <= _cellCount).OrderBy(x => x.CellId).ToArray())
                {
                    int nativeErrorCode;
                    GeometrySyncResult result = RepositionCellEndpoint(endpoint.CellId, out nativeErrorCode);
                    switch (result)
                    {
                        case GeometrySyncResult.Applied:
                            commitResult.AppliedGeometryCount++;
                            break;
                        case GeometrySyncResult.AlreadyCorrect:
                            commitResult.AlreadyCorrectCount++;
                            break;
                        case GeometrySyncResult.SkippedMinimized:
                            commitResult.SkippedMinimizedCount++;
                            break;
                        case GeometrySyncResult.HungEndpoint:
                            commitResult.HungEndpointCount++;
                            break;
                        case GeometrySyncResult.StaleEndpoint:
                            commitResult.StaleEndpointCount++;
                            staleEndpoints.Add(endpoint);
                            break;
                        default:
                            commitResult.GeometryFailureCount++;
                            commitResult.Failures.Add("Cell " + endpoint.CellId + " geometry failed" +
                                (nativeErrorCode != 0 ? " (Win32 " + nativeErrorCode + ")" : string.Empty));
                            break;
                    }
                }
            }
            finally
            {
                _syncingEndpoints = false;
            }

            foreach (NativeEndpoint stale in staleEndpoints)
                UnbindStaleEndpoint(stale, "identity validation failed");
            return commitResult;
        }

        private void ScheduleGeometrySync()
        {
            QueueNativeLayoutCommit(false);
        }

        private void RequestEndpointResync(bool showCompletionStatus)
        {
            QueueNativeLayoutCommit(showCompletionStatus);
        }

        // rc08 preserves rc06's deterministic WPF geometry-fingerprint commit path.
        // Endpoint identity validation and bounded correction backoff now guard the native side.
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

        private void QueueNativeLayoutCommit(bool showCompletionStatus)
        {
            if (!_initialLayoutApplied || !IsLoaded || _buildingGrid || _workspaceCloseAccepted || WindowState == WindowState.Minimized)
                return;

            _queuedShowCompletionStatus |= showCompletionStatus;
            if (_nativeLayoutCommitQueued)
                return;

            _nativeLayoutCommitQueued = true;
            Dispatcher.BeginInvoke(new Action(delegate
            {
                bool showStatus = _queuedShowCompletionStatus;
                _queuedShowCompletionStatus = false;
                _nativeLayoutCommitQueued = false;
                CommitNativeLayout(showStatus);
            }), DispatcherPriority.Render);
        }

        private void CommitNativeLayout(bool showCompletionStatus)
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
                NativeLayoutCommitResult result = SyncAllEndpointGeometry();
                NormalizeEndpointZOrderGroup(result);
                _lastObservedLayoutFingerprint = CaptureLayoutFingerprint();

                if (showCompletionStatus || result.HasFailures)
                    StatusText.Text = result.ToStatusText();
            }
            finally
            {
                _committingNativeLayout = false;
            }

        }

        private bool IsWorkspaceGroupForeground(IntPtr foreground)
        {
            if (foreground == IntPtr.Zero)
                return false;
            if (foreground == _workspaceHwnd)
                return true;

            NativeEndpoint endpoint = _registry.GetByHandle(foreground);
            return endpoint != null && _windowCoordinator.IsEndpointIdentityCurrent(endpoint, false);
        }

        private void NormalizeEndpointZOrderGroup()
        {
            NormalizeEndpointZOrderGroup(null);
        }

        private void NormalizeEndpointZOrderGroup(NativeLayoutCommitResult commitResult)
        {
            if (!_initialLayoutApplied || _buildingGrid || WindowState == WindowState.Minimized || _normalizingZOrder)
                return;

            IntPtr foreground = _windowCoordinator.GetForegroundWindow();
            if (!IsWorkspaceGroupForeground(foreground))
                return;

            NativeEndpoint[] endpoints = _registry.All()
                .Where(x => x.CellId <= _cellCount &&
                            _windowCoordinator.IsEndpointIdentityCurrent(x, false) &&
                            !_windowCoordinator.IsMinimized(x) &&
                            !_windowCoordinator.IsHung(x))
                .OrderBy(x => x.CellId)
                .ToArray();
            if (endpoints.Length == 0)
                return;

            _normalizingZOrder = true;
            try
            {
                // rc08 maintains the endpoint group by moving only our own opaque WPF
                // Workspace behind each valid endpoint. This avoids asynchronous cross-process
                // endpoint raises racing one another while preserving the user's naturally
                // activated endpoint at the front of the group.
                foreach (NativeEndpoint endpoint in endpoints)
                {
                    int nativeErrorCode;
                    if (!_windowCoordinator.PlaceWorkspaceBehindEndpoint(_workspaceHwnd, endpoint, out nativeErrorCode) && commitResult != null)
                    {
                        commitResult.ZOrderFailureCount++;
                        commitResult.Failures.Add("Cell " + endpoint.CellId + " Z-order anchor failed" +
                            (nativeErrorCode != 0 ? " (Win32 " + nativeErrorCode + ")" : string.Empty));
                    }
                }
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
            // EndpointLayoutLockService already filters unrelated HWNDs on the WinEvent
            // callback thread. Marshal only a managed endpoint correction to WPF.
            Dispatcher.BeginInvoke(new Action(delegate
            {
                if (_workspaceCloseAccepted || WindowState == WindowState.Minimized || _syncingEndpoints)
                    return;

                NativeEndpoint endpoint = _registry.GetByHandle(hwnd);
                if (endpoint == null)
                    return;

                DateTime nowUtc = DateTime.UtcNow;
                DateTime suppressedUntil;
                if (_locationCorrectionSuppressedUntil.TryGetValue(hwnd, out suppressedUntil) && nowUtc < suppressedUntil)
                    return;

                EndpointCorrectionState correctionState = GetCorrectionState(hwnd);
                if (correctionState.IsBackedOff(nowUtc))
                    return;

                if (!_pendingLocationCorrections.Add(hwnd))
                    return;

                try
                {
                    if (correctionState.RecordCorrectionAttempt(nowUtc))
                    {
                        StatusText.Text = "Cell " + endpoint.CellId + ": endpoint repeatedly rejected Layout Lock; correction paused briefly.";
                        return;
                    }

                    GeometrySyncResult result = RepositionCellEndpoint(endpoint.CellId);
                    if (result == GeometrySyncResult.StaleEndpoint)
                        UnbindStaleEndpoint(endpoint, "identity validation failed during Layout Lock");
                }
                finally
                {
                    _pendingLocationCorrections.Remove(hwnd);
                }
            }), DispatcherPriority.Background);
        }

        private void LayoutLockService_WindowDestroyed(IntPtr hwnd)
        {
            Dispatcher.BeginInvoke(new Action(delegate
            {
                if (_workspaceCloseAccepted)
                    return;

                NativeEndpoint endpoint = _registry.GetByHandle(hwnd);
                if (endpoint != null)
                    UnbindStaleEndpoint(endpoint, "window destroyed");
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
                EndpointIdentityStatus identity = _windowCoordinator.ValidateEndpointIdentity(endpoint, false);
                if (identity == EndpointIdentityStatus.Current)
                    continue;

                UnbindStaleEndpoint(endpoint, "identity status " + identity);
            }

            DateTime nowUtc = DateTime.UtcNow;
            foreach (IntPtr stale in _locationCorrectionSuppressedUntil.Where(x => nowUtc > x.Value.AddSeconds(2)).Select(x => x.Key).ToArray())
                _locationCorrectionSuppressedUntil.Remove(stale);

            // Low-frequency fallback for apps that do not emit location/layout events.
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
            int requested = 0;
            int failed = 0;
            foreach (NativeEndpoint endpoint in _registry.All().ToArray())
            {
                requested++;
                if (!_windowCoordinator.MinimizeWithoutActivate(endpoint))
                    failed++;
            }
            StatusText.Text = "No-activation minimize requested for " + requested + " endpoint(s); " + failed + " request(s) failed/skipped.";
        }

        private void RestoreGroup_Click(object sender, RoutedEventArgs e)
        {
            int requested = 0;
            int failed = 0;
            foreach (NativeEndpoint endpoint in _registry.All().ToArray())
            {
                requested++;
                if (!_windowCoordinator.RestoreWithoutActivate(endpoint))
                    failed++;
            }
            RequestEndpointResync(true);
            StatusText.Text = "No-activation restore requested for " + requested + " endpoint(s); " + failed + " request(s) failed/skipped.";
        }

        private void ResetTiling_Click(object sender, RoutedEventArgs e)
        {
            BuildAdaptiveLayout(null);
            StatusText.Text = "Adaptive tiled layout reset to equal row/Cell proportions; endpoint bindings were preserved.";
        }

        private void ResyncEndpoints_Click(object sender, RoutedEventArgs e)
        {
            CommitNativeLayout(true);
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
                ClearEndpointRuntimeState(endpoint);
                CellControl oldCell;
                if (_cells.TryGetValue(endpoint.CellId, out oldCell))
                    oldCell.SetEndpoint(null);
            }
            RefreshManagedHandleSnapshot();

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
                    Version = "0.0.1rc08",
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

            int oldCellCount = _cellCount;
            GridLayoutState oldGrid = CaptureGridLayoutState();
            IList<ShortcutBinding> oldShortcuts = _shortcutBindings.Select(x => x.Clone()).ToList();
            NativeEndpoint[] oldEndpoints = _registry.All().ToArray();
            var oldMinWidths = new Dictionary<int, double>(_cellMinimumHostWidths);
            var oldMinHeights = new Dictionary<int, double>(_cellMinimumHostHeights);

            try
            {
                WorkspaceState state = _layoutService.Load(dialog.FileName);
                int proposedCellCount = ResolveLoadedCellCount(state);
                IList<ShortcutBinding> proposedShortcuts = MergeLoadedShortcutBindings(state == null ? null : state.Shortcuts);

                // Review #6: validate the complete proposed state before mutating the active
                // Workspace. Malformed geometry/shortcuts cannot destroy the working layout.
                ValidateLoadedWorkspaceState(state, proposedCellCount, proposedShortcuts);

                foreach (NativeEndpoint endpoint in oldEndpoints)
                    ClearEndpointRuntimeState(endpoint);
                _registry.Clear();
                _locationCorrectionSuppressedUntil.Clear();
                _pendingLocationCorrections.Clear();
                _correctionStates.Clear();
                _minimizedWithWorkspace.Clear();
                _cellMinimumHostWidths.Clear();
                _cellMinimumHostHeights.Clear();
                RefreshManagedHandleSnapshot();
                foreach (CellControl cell in _cells.Values)
                    cell.SetEndpoint(null);

                _cellCount = proposedCellCount;
                _updatingCellCountUi = true;
                try { CellCountComboBox.SelectedItem = _cellCount; }
                finally { _updatingCellCountUi = false; }

                EnsureActiveCellControls();
                BuildAdaptiveLayout(state == null ? null : state.Grid);

                _shortcutBindings = proposedShortcuts;
                string shortcutSummary = ApplyActiveShortcutBindings();
                StatusText.Text = "Layout loaded transactionally with " + _cellCount +
                                  " adaptive tiled Cells; endpoint HWNDs intentionally not restored. " + shortcutSummary;
            }
            catch (Exception ex)
            {
                try
                {
                    RestoreWorkspaceAfterFailedLoad(oldCellCount, oldGrid, oldShortcuts, oldEndpoints, oldMinWidths, oldMinHeights);
                }
                catch
                {
                    // Preserve the original load exception for the user; rollback is best effort.
                }
                MessageBox.Show(this, ex.Message, "Load Layout Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void RestoreWorkspaceAfterFailedLoad(int cellCount, GridLayoutState grid, IList<ShortcutBinding> shortcuts,
            IEnumerable<NativeEndpoint> endpoints, IDictionary<int, double> minWidths, IDictionary<int, double> minHeights)
        {
            _registry.Clear();
            _cellCount = cellCount;
            _updatingCellCountUi = true;
            try { CellCountComboBox.SelectedItem = _cellCount; }
            finally { _updatingCellCountUi = false; }

            _cellMinimumHostWidths.Clear();
            foreach (KeyValuePair<int, double> pair in minWidths)
                _cellMinimumHostWidths[pair.Key] = pair.Value;
            _cellMinimumHostHeights.Clear();
            foreach (KeyValuePair<int, double> pair in minHeights)
                _cellMinimumHostHeights[pair.Key] = pair.Value;

            EnsureActiveCellControls();
            BuildAdaptiveLayout(grid);
            foreach (CellControl cell in _cells.Values)
                cell.SetEndpoint(null);

            foreach (NativeEndpoint endpoint in endpoints)
            {
                if (endpoint.CellId < 1 || endpoint.CellId > _cellCount)
                    continue;
                _registry.Bind(endpoint.CellId, endpoint);
                CellControl cell;
                if (_cells.TryGetValue(endpoint.CellId, out cell))
                    cell.SetEndpoint(endpoint);
            }
            RefreshManagedHandleSnapshot();

            _shortcutBindings = shortcuts.Select(x => x.Clone()).ToList();
            ApplyActiveShortcutBindings();
            QueueNativeLayoutCommit(false);
        }

        private static void ValidateLoadedWorkspaceState(WorkspaceState state, int proposedCellCount, IList<ShortcutBinding> proposedShortcuts)
        {
            if (state == null)
                throw new InvalidDataException("Layout file did not contain a WorkspaceState.");
            if (!string.IsNullOrWhiteSpace(state.Version) && !state.Version.StartsWith("0.0.1", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Layout version is not compatible with the v0.0.1 RC line.");
            if (state.CellCount != 0 && (state.CellCount < MinCellCount || state.CellCount > MaxCellCount))
                throw new InvalidDataException("Layout CellCount is outside the supported 4-12 range.");
            if (state.CellCount == 0 && (state.Cells == null || state.Cells.Count < MinCellCount || state.Cells.Count > MaxCellCount))
                throw new InvalidDataException("Legacy layout does not provide a valid 4-12 Cell count.");
            if (proposedCellCount < MinCellCount || proposedCellCount > MaxCellCount)
                throw new InvalidDataException("Resolved layout CellCount is outside the supported 4-12 range.");

            ValidateGridLayoutState(state.Grid, proposedCellCount);

            if (proposedShortcuts == null || proposedShortcuts.Count < proposedCellCount)
                throw new InvalidDataException("Layout shortcut configuration is incomplete.");

            foreach (ShortcutBinding binding in proposedShortcuts)
            {
                if (binding.CellId < 1 || binding.CellId > MaxCellCount)
                    throw new InvalidDataException("Layout contains an invalid shortcut CellId.");
                if (binding.KeyCode <= 0 || binding.KeyCode > 0xFF)
                    throw new InvalidDataException("Layout contains an invalid shortcut virtual-key code.");
            }

            string duplicate = proposedShortcuts.Where(x => x.CellId <= proposedCellCount)
                .GroupBy(x => x.ConflictKey)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .FirstOrDefault();
            if (!string.IsNullOrEmpty(duplicate))
                throw new InvalidDataException("Layout contains duplicate active shortcut gestures: " + duplicate + ".");
        }

        private static void ValidateGridLayoutState(GridLayoutState grid, int cellCount)
        {
            if (grid == null)
                return;

            int[] rowCounts = GetRowCellCounts(cellCount);
            if (grid.RowLayouts != null && grid.RowLayouts.Count > 0)
            {
                if (grid.RowLayouts.Count != rowCounts.Length)
                    throw new InvalidDataException("Layout row count is incompatible with CellCount.");

                int expectedCellId = 1;
                for (int row = 0; row < rowCounts.Length; row++)
                {
                    AdaptiveRowLayoutState rowState = grid.RowLayouts[row];
                    if (rowState == null || rowState.CellIds == null || rowState.ColumnWeights == null ||
                        rowState.CellIds.Count != rowCounts[row] || rowState.ColumnWeights.Count != rowCounts[row])
                        throw new InvalidDataException("Layout row geometry is incomplete or incompatible.");
                    ValidateFinitePositiveWeight(rowState.HeightWeight, "row height");
                    for (int column = 0; column < rowCounts[row]; column++)
                    {
                        if (rowState.CellIds[column] != expectedCellId++)
                            throw new InvalidDataException("Layout Cell ordering is invalid.");
                        ValidateFinitePositiveWeight(rowState.ColumnWeights[column], "column width");
                    }
                }
            }

            if (grid.RowWeights != null)
                foreach (double weight in grid.RowWeights) ValidateFinitePositiveWeight(weight, "legacy row weight");
            if (grid.ColumnWeights != null)
                foreach (double weight in grid.ColumnWeights) ValidateFinitePositiveWeight(weight, "legacy column weight");
        }

        private static void ValidateFinitePositiveWeight(double value, string name)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0 || value > 1000000)
                throw new InvalidDataException("Layout contains an invalid " + name + ".");
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
                foreach (ShortcutBinding binding in loaded.Where(x => x != null && x.CellId >= 1 && x.CellId <= MaxCellCount))
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
                    if (!_windowCoordinator.IsMinimized(endpoint))
                    {
                        _minimizedWithWorkspace.Add(endpoint.Handle);
                        _windowCoordinator.MinimizeWithoutActivate(endpoint);
                    }
                }
                return;
            }

            foreach (IntPtr hwnd in _minimizedWithWorkspace.ToArray())
            {
                NativeEndpoint endpoint = _registry.GetByHandle(hwnd);
                if (endpoint != null)
                    _windowCoordinator.RestoreWithoutActivate(endpoint);
            }
            _minimizedWithWorkspace.Clear();
            RequestEndpointResync(true);
        }

        private void StopAllSizeVerificationTimers()
        {
            foreach (DispatcherTimer timer in _sizeVerificationTimers.Values.ToArray())
                timer.Stop();
            _sizeVerificationTimers.Clear();
        }

        private void MainWindow_Closing(object sender, CancelEventArgs e)
        {
            if (_workspaceCloseAccepted)
                return;

            NativeEndpoint[] allEndpoints = _registry.All().ToArray();
            if (allEndpoints.Length == 0)
            {
                _workspaceCloseAccepted = true;
                return;
            }

            NativeEndpoint[] closableEndpoints = allEndpoints
                .Where(x => _windowCoordinator.IsEndpointIdentityCurrent(x, true))
                .ToArray();
            int skippedEndpointCount = allEndpoints.Length - closableEndpoints.Length;

            string closeMessage =
                "Close Native Endpoint Workspace?\n\n" +
                closableEndpoints.Length + " bound application window(s) passed identity validation and will receive a graceful WM_CLOSE request.";
            if (skippedEndpointCount > 0)
                closeMessage += "\n\n" + skippedEndpointCount + " stale/unverifiable endpoint(s) will be left open for safety.";
            closeMessage += "\n\nApplications with unsaved data may show their own Save/Cancel prompt.";

            MessageBoxResult result = MessageBox.Show(this,
                closeMessage,
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
            StopAllSizeVerificationTimers();
            _layoutLockService.Dispose();

            foreach (NativeEndpoint endpoint in closableEndpoints)
                _windowCoordinator.RequestClose(endpoint);

            _registry.Clear();
            RefreshManagedHandleSnapshot();
        }

        private void MainWindow_Closed(object sender, EventArgs e)
        {
            _windowHealthTimer.Stop();
            StopAllSizeVerificationTimers();
            WorkspaceGrid.LayoutUpdated -= WorkspaceGrid_LayoutUpdated;
            _layoutLockService.WindowLocationChanged -= LayoutLockService_WindowLocationChanged;
            _layoutLockService.ForegroundChanged -= LayoutLockService_ForegroundChanged;
            _layoutLockService.WindowDestroyed -= LayoutLockService_WindowDestroyed;
            _layoutLockService.Dispose();

            if (_shortcutService != null)
                _shortcutService.Dispose();
            if (_hwndSource != null)
                _hwndSource.RemoveHook(WndProc);

            _registry.Clear();
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using NativeEndpointWorkspace.Core;
using NativeEndpointWorkspace.Services;

namespace NativeEndpointWorkspace.UI
{
    public class ShortcutSettingsWindow : Window
    {
        private sealed class EditorRow
        {
            public ShortcutBinding Binding;
            public CheckBox Ctrl;
            public CheckBox Shift;
            public CheckBox Alt;
            public ComboBox Key;
            public TextBlock Status;
        }

        private readonly ShortcutService _service;
        private readonly List<EditorRow> _rows = new List<EditorRow>();
        private readonly TextBlock _summary;

        public IList<ShortcutBinding> AppliedBindings { get; private set; }

        public ShortcutSettingsWindow(ShortcutService service, IList<ShortcutBinding> bindings)
        {
            _service = service;
            AppliedBindings = bindings.Select(x => x.Clone()).ToList();

            Title = "Shortcut Settings";
            Width = 760;
            Height = 650;
            MinWidth = 650;
            MinHeight = 500;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;

            var root = new DockPanel { Margin = new Thickness(12) };
            Content = root;

            var explanation = new TextBlock
            {
                Text = "Each shortcut assigns the current foreground top-level window to its Cell. " +
                       "Conflicts are detected both inside this workspace and by Windows RegisterHotKey. " +
                       "Use one or more Ctrl / Alt / Shift modifiers. Bare F1-F8 and Win-key global hotkeys are rejected to reduce collisions with normal application and Windows shortcuts. " +
                       "Shortcut apply is all-or-nothing: if Windows rejects any requested hotkey, the previous working shortcut set is restored.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 10)
            };
            DockPanel.SetDock(explanation, Dock.Top);
            root.Children.Add(explanation);

            var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
            var apply = new Button { Content = "Apply / Validate", Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(4, 0, 0, 0) };
            apply.Click += Apply_Click;
            var close = new Button { Content = "Close", Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(4, 0, 0, 0) };
            close.Click += delegate { Close(); };
            footer.Children.Add(apply);
            footer.Children.Add(close);
            DockPanel.SetDock(footer, Dock.Bottom);
            root.Children.Add(footer);

            _summary = new TextBlock { Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap };
            DockPanel.SetDock(_summary, Dock.Bottom);
            root.Children.Add(_summary);

            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            root.Children.Add(scroll);
            var list = new StackPanel();
            scroll.Content = list;

            list.Children.Add(BuildHeader());

            foreach (var binding in AppliedBindings.OrderBy(x => x.CellId))
            {
                EditorRow row = BuildRow(binding);
                _rows.Add(row);
                list.Children.Add((UIElement)row.Status.Tag);
            }
        }

        private UIElement BuildHeader()
        {
            var grid = CreateGrid();
            grid.Margin = new Thickness(0, 0, 0, 3);
            AddText(grid, "Cell", 0, true);
            AddText(grid, "Ctrl", 1, true);
            AddText(grid, "Shift", 2, true);
            AddText(grid, "Alt", 3, true);
            AddText(grid, "Key", 4, true);
            AddText(grid, "Status", 5, true);
            return grid;
        }

        private EditorRow BuildRow(ShortcutBinding binding)
        {
            var grid = CreateGrid();
            grid.Margin = new Thickness(0, 2, 0, 2);

            AddText(grid, binding.CellId.ToString(), 0, false);
            var ctrl = AddCheck(grid, binding.Control, 1);
            var shift = AddCheck(grid, binding.Shift, 2);
            var alt = AddCheck(grid, binding.Alt, 3);
            var key = new ComboBox { Margin = new Thickness(3), MinWidth = 70 };
            for (int i = 1; i <= WorkspaceConstants.FunctionKeyCount; i++) key.Items.Add("F" + i);
            key.SelectedIndex = Math.Max(0, Math.Min(WorkspaceConstants.FunctionKeyCount - 1, binding.KeyCode - WorkspaceConstants.FunctionKeyFirstVirtualKey));
            Grid.SetColumn(key, 4);
            grid.Children.Add(key);

            var status = new TextBlock { Text = binding.Status, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(5, 0, 0, 0) };
            Grid.SetColumn(status, 5);
            grid.Children.Add(status);
            status.Tag = grid;

            return new EditorRow { Binding = binding, Ctrl = ctrl, Shift = shift, Alt = alt, Key = key, Status = status };
        }

        private void Apply_Click(object sender, RoutedEventArgs e)
        {
            var candidate = new List<ShortcutBinding>();
            foreach (var row in _rows)
            {
                ShortcutBinding binding = row.Binding.Clone();
                binding.Control = row.Ctrl.IsChecked == true;
                binding.Shift = row.Shift.IsChecked == true;
                binding.Alt = row.Alt.IsChecked == true;
                binding.Win = false;
                binding.KeyCode = WorkspaceConstants.FunctionKeyFirstVirtualKey + Math.Max(0, row.Key.SelectedIndex);
                candidate.Add(binding);
            }

            string summary;
            bool applied = _service.ApplyBindings(candidate, out summary);
            _summary.Text = summary;

            for (int i = 0; i < _rows.Count; i++)
            {
                _rows[i].Status.Text = candidate[i].Status;
                if (applied)
                    _rows[i].Binding = candidate[i].Clone();
            }

            if (applied)
                AppliedBindings = candidate.Select(x => x.Clone()).OrderBy(x => x.CellId).ToList();
        }

        private static Grid CreateGrid()
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(55) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(95) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            return grid;
        }

        private static void AddText(Grid grid, string text, int column, bool bold)
        {
            var block = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 2, 4, 2) };
            if (bold) block.FontWeight = FontWeights.Bold;
            Grid.SetColumn(block, column);
            grid.Children.Add(block);
        }

        private static CheckBox AddCheck(Grid grid, bool value, int column)
        {
            var box = new CheckBox { IsChecked = value, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(box, column);
            grid.Children.Add(box);
            return box;
        }
    }
}

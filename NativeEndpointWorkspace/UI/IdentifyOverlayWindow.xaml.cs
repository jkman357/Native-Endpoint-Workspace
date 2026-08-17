using System;
using System.Windows;
using System.Windows.Threading;
using NativeEndpointWorkspace.Core;

namespace NativeEndpointWorkspace.UI
{
    public partial class IdentifyOverlayWindow : Window
    {
        private readonly DispatcherTimer _timer;

        public IdentifyOverlayWindow(int cellId, Rect screenBounds)
        {
            InitializeComponent();
            NumberText.Text = cellId.ToString();
            Left = screenBounds.Left;
            Top = screenBounds.Top;
            Width = Math.Max(80, screenBounds.Width);
            Height = Math.Max(80, screenBounds.Height);

            _timer = new DispatcherTimer { Interval = WorkspaceConstants.IdentifyOverlayDuration };
            _timer.Tick += Timer_Tick;
            Loaded += delegate { _timer.Start(); };
        }

        private void Timer_Tick(object sender, EventArgs e)
        {
            _timer.Stop();
            Close();
        }
    }
}

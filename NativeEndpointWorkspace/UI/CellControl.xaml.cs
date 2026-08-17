using System;
using System.Windows;
using System.Windows.Controls;
using NativeEndpointWorkspace.Core;

namespace NativeEndpointWorkspace.UI
{
    public partial class CellControl : UserControl
    {
        public event EventHandler DetachRequested;
        public event EventHandler RemoveRequested;

        public int CellId { get; private set; }

        public FrameworkElement EndpointHostElement
        {
            get { return EndpointHost; }
        }

        public CellControl(int cellId)
        {
            InitializeComponent();
            CellId = cellId;
            CellBadgeButton.Content = "F" + cellId;
            EndpointText.Text = "No endpoint";
        }

        public void SetEndpoint(NativeEndpoint endpoint)
        {
            if (endpoint == null)
            {
                EmptyText.Visibility = Visibility.Visible;
                EndpointText.Text = "No endpoint";
                return;
            }

            EmptyText.Visibility = Visibility.Collapsed;
            EndpointText.Text = endpoint.DisplayName;
        }

        private void DetachButton_Click(object sender, RoutedEventArgs e)
        {
            if (DetachRequested != null) DetachRequested(this, EventArgs.Empty);
        }

        private void CellBadgeButton_Click(object sender, RoutedEventArgs e)
        {
            if (RemoveRequested != null) RemoveRequested(this, EventArgs.Empty);
        }
    }
}

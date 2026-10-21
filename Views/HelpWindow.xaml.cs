using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using WinDock.Native;

namespace WinDock.Views
{
    public partial class HelpWindow : Window
    {
        public HelpWindow()
        {
            InitializeComponent();
            Loaded += (s, e) =>
            {
                IntPtr hwnd = new WindowInteropHelper(this).Handle;
                WindowHelper.HideFromAltTab(hwnd);
            };
        }

        private void OnCloseClicked(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                Close();
            }
        }
    }
}

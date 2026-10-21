using System;
using System.IO;
using System.Windows;
using System.Windows.Input;

namespace WinDock.Views
{
    public partial class ZipNameDialog : Window
    {
        public string ZipFileName { get; private set; } = string.Empty;

        public ZipNameDialog(string defaultName = "")
        {
            InitializeComponent();

            string baseName = string.IsNullOrWhiteSpace(defaultName)
                ? $"WinDock_Archive_{DateTime.Now:yyyyMMdd_HHmm}"
                : defaultName;

            FileNameTextBox.Text = baseName;
            Loaded += (s, e) =>
            {
                FileNameTextBox.Focus();
                FileNameTextBox.SelectAll();
            };
        }

        private void OnTextBoxKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                Confirm();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                DialogResult = false;
                Close();
                e.Handled = true;
            }
        }

        private void OnConfirmClicked(object sender, RoutedEventArgs e)
        {
            Confirm();
        }

        private void OnCancelClicked(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void Confirm()
        {
            string name = FileNameTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                FileNameTextBox.Focus();
                return;
            }

            // Strip invalid filename chars
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c.ToString(), "");
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                name = "WinDock_Archive";
            }

            ZipFileName = name;
            DialogResult = true;
            Close();
        }
    }
}

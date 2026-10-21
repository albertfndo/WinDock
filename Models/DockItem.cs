using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace WinDock.Models
{
    public class DockItem : INotifyPropertyChanged
    {
        private string _title = string.Empty;
        private string _processName = string.Empty;
        private string? _launchPath;
        private IntPtr _windowHandle = IntPtr.Zero;
        private ImageSource? _icon;
        private bool _isPinned;
        private bool _isRunning;
        private bool _isActive;
        private int _windowCount;
        private double _scale = 1.0;
        private string? _badgeText;

        public string? BadgeText
        {
            get => _badgeText ?? (_windowCount > 1 ? _windowCount.ToString() : null);
            set => SetField(ref _badgeText, value);
        }

        public bool HasBadge => !string.IsNullOrEmpty(BadgeText);

        public string Title
        {
            get => _title;
            set => SetField(ref _title, value);
        }

        public string ProcessName
        {
            get => _processName;
            set => SetField(ref _processName, value);
        }

        public string? LaunchPath
        {
            get => _launchPath;
            set => SetField(ref _launchPath, value);
        }

        public IntPtr WindowHandle
        {
            get => _windowHandle;
            set => SetField(ref _windowHandle, value);
        }

        public ImageSource? Icon
        {
            get => _icon;
            set => SetField(ref _icon, value);
        }

        public bool IsPinned
        {
            get => _isPinned;
            set => SetField(ref _isPinned, value);
        }

        public bool IsRunning
        {
            get => _isRunning;
            set => SetField(ref _isRunning, value);
        }

        public bool IsActive
        {
            get => _isActive;
            set => SetField(ref _isActive, value);
        }

        public int WindowCount
        {
            get => _windowCount;
            set => SetField(ref _windowCount, value);
        }

        public double Scale
        {
            get => _scale;
            set => SetField(ref _scale, value);
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }
}

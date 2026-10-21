using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace WinDock.Models
{
    public enum SpotlightCategory
    {
        Application,
        RunningWindow,
        Calculation,
        SystemCommand,
        WebSearch
    }

    public class SpotlightItem : INotifyPropertyChanged
    {
        private string _title = string.Empty;
        private string _subtitle = string.Empty;
        private SpotlightCategory _category = SpotlightCategory.Application;
        private ImageSource? _icon;
        private Action? _executeAction;
        private bool _isSelected;

        public string Title
        {
            get => _title;
            set => SetField(ref _title, value);
        }

        public string Subtitle
        {
            get => _subtitle;
            set => SetField(ref _subtitle, value);
        }

        public SpotlightCategory Category
        {
            get => _category;
            set => SetField(ref _category, value);
        }

        public string CategoryDisplay => _category switch
        {
            SpotlightCategory.Application => "Application",
            SpotlightCategory.RunningWindow => "Open Window",
            SpotlightCategory.Calculation => "Calculation",
            SpotlightCategory.SystemCommand => "System",
            SpotlightCategory.WebSearch => "Web Search",
            _ => "Item"
        };

        public string CategoryBadgeColor => _category switch
        {
            SpotlightCategory.Application => "#3B82F6",    // Blue
            SpotlightCategory.RunningWindow => "#10B981",  // Green
            SpotlightCategory.Calculation => "#F59E0B",    // Amber
            SpotlightCategory.SystemCommand => "#EF4444",  // Red
            SpotlightCategory.WebSearch => "#8B5CF6",      // Purple
            _ => "#6B7280"
        };

        public ImageSource? Icon
        {
            get => _icon;
            set => SetField(ref _icon, value);
        }

        public Action? ExecuteAction
        {
            get => _executeAction;
            set => _executeAction = value;
        }

        public bool IsSelected
        {
            get => _isSelected;
            set => SetField(ref _isSelected, value);
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

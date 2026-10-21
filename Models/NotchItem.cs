using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace WinDock.Models
{
    public enum NotchItemType
    {
        File,
        Folder,
        Application,
        Url,
        Text
    }

    public class NotchItem : INotifyPropertyChanged
    {
        private Guid _id = Guid.NewGuid();
        private string _name = string.Empty;
        private string? _filePath;
        private string? _textContent;
        private NotchItemType _itemType = NotchItemType.File;
        private string _sizeFormatted = string.Empty;
        private ImageSource? _icon;
        private DateTime _droppedTime = DateTime.Now;

        public Guid Id => _id;

        public string Name
        {
            get => _name;
            set => SetField(ref _name, value);
        }

        public string? FilePath
        {
            get => _filePath;
            set => SetField(ref _filePath, value);
        }

        public string? TextContent
        {
            get => _textContent;
            set => SetField(ref _textContent, value);
        }

        public NotchItemType ItemType
        {
            get => _itemType;
            set => SetField(ref _itemType, value);
        }

        public string SizeFormatted
        {
            get => _sizeFormatted;
            set => SetField(ref _sizeFormatted, value);
        }

        public ImageSource? Icon
        {
            get => _icon;
            set => SetField(ref _icon, value);
        }

        public DateTime DroppedTime
        {
            get => _droppedTime;
            set => SetField(ref _droppedTime, value);
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

using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace WinDock.Models
{
    public class ClipboardItem : INotifyPropertyChanged
    {
        private string _id = Guid.NewGuid().ToString();
        private string _title = string.Empty;
        private string? _textContent;
        private ImageSource? _imagePreview;
        private bool _isImage;
        private DateTime _copiedAt = DateTime.Now;
        private string _metaInfo = string.Empty;

        public string Id
        {
            get => _id;
            set => SetField(ref _id, value);
        }

        public string Title
        {
            get => _title;
            set => SetField(ref _title, value);
        }

        public string? TextContent
        {
            get => _textContent;
            set => SetField(ref _textContent, value);
        }

        public ImageSource? ImagePreview
        {
            get => _imagePreview;
            set => SetField(ref _imagePreview, value);
        }

        public bool IsImage
        {
            get => _isImage;
            set => SetField(ref _isImage, value);
        }

        public DateTime CopiedAt
        {
            get => _copiedAt;
            set
            {
                if (SetField(ref _copiedAt, value))
                {
                    OnPropertyChanged(nameof(TimeFormatted));
                }
            }
        }

        public string TimeFormatted
        {
            get
            {
                var elapsed = DateTime.Now - CopiedAt;
                if (elapsed.TotalSeconds < 60) return "Just now";
                if (elapsed.TotalMinutes < 60) return $"{(int)elapsed.TotalMinutes}m ago";
                if (elapsed.TotalHours < 24) return $"{(int)elapsed.TotalHours}h ago";
                return CopiedAt.ToString("MMM d, HH:mm");
            }
        }

        public string MetaInfo
        {
            get => _metaInfo;
            set => SetField(ref _metaInfo, value);
        }

        public void RefreshTimeFormatted()
        {
            OnPropertyChanged(nameof(TimeFormatted));
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

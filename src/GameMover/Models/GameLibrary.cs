using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace GameMover.Models
{
    public sealed class GameLibrary : INotifyPropertyChanged
    {
        private string name = string.Empty;
        private string path = string.Empty;

        public string Name
        {
            get => name;
            set
            {
                name = value ?? string.Empty;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DisplayLabel));
            }
        }

        public string Path
        {
            get => path;
            set
            {
                path = value ?? string.Empty;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DisplayLabel));
            }
        }

        public string DisplayLabel =>
            string.IsNullOrWhiteSpace(Name) ? Path : Name + " — " + Path;

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using GameMover.Models;
using GameMover.Services;
using Playnite.SDK;
using Playnite.SDK.Data;

namespace GameMover
{
    public class Settings : ObservableObject
    {
        private ObservableCollection<GameLibrary> libraries = new ObservableCollection<GameLibrary>();

        public ObservableCollection<GameLibrary> Libraries
        {
            get => libraries;
            set => SetValue(ref libraries, value);
        }
    }

    public class SettingsViewModel : ObservableObject, ISettings
    {
        private readonly GameMoverPlugin plugin;
        private Settings? editingClone;
        private Settings settings = new Settings();
        private GameLibrary? selectedLibrary;

        public SettingsViewModel(GameMoverPlugin plugin)
        {
            this.plugin = plugin ?? throw new ArgumentNullException(nameof(plugin));
            var saved = plugin.LoadPluginSettings<Settings>();
            Settings = saved ?? new Settings();
            if (Settings.Libraries == null)
            {
                Settings.Libraries = new ObservableCollection<GameLibrary>();
            }

            AddLibraryCommand = new RelayCommand(AddLibrary);
            EditLibraryCommand = new RelayCommand(EditLibrary, () => SelectedLibrary != null);
            RemoveLibraryCommand = new RelayCommand(RemoveLibrary, () => SelectedLibrary != null);
        }

        public Settings Settings
        {
            get => settings;
            set
            {
                settings = value;
                OnPropertyChanged();
            }
        }

        public GameLibrary? SelectedLibrary
        {
            get => selectedLibrary;
            set
            {
                selectedLibrary = value;
                OnPropertyChanged();
            }
        }

        public ICommand AddLibraryCommand { get; }

        public ICommand EditLibraryCommand { get; }

        public ICommand RemoveLibraryCommand { get; }

        public void BeginEdit()
        {
            editingClone = Serialization.GetClone(Settings);
        }

        public void CancelEdit()
        {
            if (editingClone != null)
            {
                Settings = editingClone;
            }

            SelectedLibrary = null;
        }

        public void EndEdit()
        {
            plugin.SavePluginSettings(Settings);
            plugin.Log.Info("Saved " + (Settings.Libraries?.Count ?? 0) + " library folder(s).");
        }

        public bool VerifySettings(out List<string> errors)
        {
            errors = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (Settings.Libraries == null)
            {
                return true;
            }

            foreach (var library in Settings.Libraries)
            {
                if (library == null || string.IsNullOrWhiteSpace(library.Path))
                {
                    errors.Add("Every library needs a folder path.");
                    continue;
                }

                string normalized;
                if (!PathRewriteService.TryNormalizeAbsolute(library.Path, out normalized))
                {
                    errors.Add("Library folder must be an absolute path: " + library.Path);
                    continue;
                }

                if (!Directory.Exists(library.Path))
                {
                    errors.Add("Library folder does not exist: " + library.Path);
                }

                if (!seen.Add(normalized))
                {
                    errors.Add("Duplicate library folder: " + library.Path);
                }
            }

            return errors.Count == 0;
        }

        private void AddLibrary()
        {
            var selected = plugin.PlayniteApi.Dialogs.SelectFolder();
            if (string.IsNullOrWhiteSpace(selected))
            {
                return;
            }

            Settings.Libraries.Add(new GameLibrary
            {
                Path = Normalize(selected)
            });
        }

        private void EditLibrary()
        {
            if (SelectedLibrary == null)
            {
                return;
            }

            var selected = plugin.PlayniteApi.Dialogs.SelectFolder(SelectedLibrary.Path);
            if (string.IsNullOrWhiteSpace(selected))
            {
                return;
            }

            SelectedLibrary.Path = Normalize(selected);
        }

        private void RemoveLibrary()
        {
            if (SelectedLibrary == null)
            {
                return;
            }

            Settings.Libraries.Remove(SelectedLibrary);
            SelectedLibrary = null;
        }

        private static string Normalize(string path)
        {
            try
            {
                return Path.GetFullPath(path);
            }
            catch (Exception)
            {
                return path;
            }
        }
    }
}

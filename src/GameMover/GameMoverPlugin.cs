using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using GameMover.Models;
using GameMover.Services;
using Playnite.SDK;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;

namespace GameMover
{
    public class GameMoverPlugin : GenericPlugin
    {
        private readonly SettingsViewModel settings;
        private readonly PathRewriteService pathRewrite = new PathRewriteService();
        private readonly FileCopyService fileCopy;
        private readonly GameProcessService processes;
        private readonly GameMoveService mover;
        private readonly GameMetadataUpdater metadata;

        public GameMoverPlugin(IPlayniteAPI api) : base(api)
        {
            Log = new PlayniteMoveLog(LogManager.GetLogger());
            fileCopy = new FileCopyService(Log);
            processes = new GameProcessService(Log);
            mover = new GameMoveService(
                fileCopy,
                new FileCountAndSizeVerificationService(Log),
                pathRewrite,
                Log);
            metadata = new GameMetadataUpdater(api, pathRewrite, Log);
            settings = new SettingsViewModel(this);
            Properties = new GenericPluginProperties
            {
                HasSettings = true
            };
        }

        public override Guid Id { get; } = Guid.Parse("c4e8a1d2-7b36-4f90-9a55-1e6d3c8b47f2");

        internal IMoveLog Log { get; }

        public override IEnumerable<GameMenuItem> GetGameMenuItems(GetGameMenuItemsArgs args)
        {
            yield return new GameMenuItem
            {
                Description = "Move Game...",
                MenuSection = "GameMover",
                Action = action => OpenMoveDialog(action.Games, null)
            };

            var libraries = settings.Settings.Libraries;
            if (libraries != null)
            {
                foreach (var library in libraries.Where(item => item != null && !string.IsNullOrWhiteSpace(item.Path)).ToList())
                {
                    var path = library.Path;
                    var label = string.IsNullOrWhiteSpace(library.Name) ? library.Path : library.Name;
                    yield return new GameMenuItem
                    {
                        Description = "Move to " + label,
                        MenuSection = "GameMover",
                        Action = action => OpenMoveDialog(action.Games, path)
                    };
                }
            }

            yield return new GameMenuItem
            {
                Description = "Manage Libraries...",
                MenuSection = "GameMover",
                Action = _ => PlayniteApi.MainView.OpenPluginSettings(Id)
            };
        }

        public override ISettings GetSettings(bool firstRunSettings)
        {
            return settings;
        }

        public override System.Windows.Controls.UserControl GetSettingsView(bool firstRunSettings)
        {
            return new SettingsView();
        }

        private void OpenMoveDialog(IList<Game>? games, string? preselectedLibrary)
        {
            try
            {
                if (games == null || games.Count == 0)
                {
                    PlayniteApi.Dialogs.ShowMessage("Select a game to move.", "GameMover");
                    return;
                }

                if (games.Count != 1)
                {
                    PlayniteApi.Dialogs.ShowMessage("Select a single game. GameMover moves one game at a time.", "GameMover");
                    return;
                }

                var game = games[0];
                var refusal = LibraryOwnershipService.GetMoveRefusal(game, PlayniteApi.Addons.Plugins);
                if (refusal != null)
                {
                    Log.Warn(refusal);
                    PlayniteApi.Dialogs.ShowErrorMessage(refusal, "GameMover");
                    return;
                }

                if (string.IsNullOrWhiteSpace(game.InstallDirectory))
                {
                    PlayniteApi.Dialogs.ShowErrorMessage(
                        "This game has no install directory. Set the install folder in game details, then try again.",
                        "GameMover");
                    return;
                }

                var libraries = new ObservableCollection<GameLibrary>();
                if (settings.Settings.Libraries != null)
                {
                    foreach (var library in settings.Settings.Libraries)
                    {
                        if (library != null && !string.IsNullOrWhiteSpace(library.Path))
                        {
                            libraries.Add(new GameLibrary { Name = library.Name, Path = library.Path });
                        }
                    }
                }

                var gameId = game.Id;
                var source = game.InstallDirectory;
                var viewModel = new MoveGameViewModel(
                    new MoveRequest
                    {
                        GameId = gameId,
                        GameName = game.Name ?? "Game",
                        SourceDirectory = source
                    },
                    libraries,
                    preselectedLibrary,
                    mover,
                    fileCopy,
                    pathRewrite,
                    new PlayniteFolderPicker(PlayniteApi),
                    new PlayniteUserPrompt(PlayniteApi),
                    new WpfUiInvoker(PlayniteApi.MainView.UIDispatcher),
                    () => ReadRuntime(gameId, source),
                    (oldRoot, newRoot, totalBytes) =>
                    {
                        var warnings = 0;
                        PlayniteApi.MainView.UIDispatcher.Invoke(() =>
                        {
                            warnings = metadata.Update(gameId, oldRoot, newRoot, totalBytes);
                        });
                        return warnings;
                    },
                    Log);

                var window = PlayniteApi.Dialogs.CreateWindow(new WindowCreationOptions
                {
                    ShowMinimizeButton = false,
                    ShowMaximizeButton = false
                });
                window.Title = "Move Game";
                window.Width = 680;
                window.MinWidth = 480;
                window.SizeToContent = SizeToContent.Height;
                window.MaxHeight = 720;
                window.Content = new MoveGameView();
                window.DataContext = viewModel;
                window.Owner = PlayniteApi.Dialogs.GetCurrentAppWindow();
                window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                window.Closing += (_, closingArgs) =>
                {
                    if (viewModel.IsMoving)
                    {
                        viewModel.RequestCancel();
                        closingArgs.Cancel = true;
                    }
                };
                viewModel.CloseRequested += (_, _) => window.Close();
                Log.Info("Opened Move Game for '" + game.Name + "' at '" + source + "'.");
                window.ShowDialog();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to open Move Game.");
                PlayniteApi.Dialogs.ShowErrorMessage(ex.Message, "GameMover");
            }
        }

        private GameRuntimeState ReadRuntime(Guid gameId, string sourceDirectory)
        {
            var state = new GameRuntimeState();
            PlayniteApi.MainView.UIDispatcher.Invoke(() =>
            {
                var stored = PlayniteApi.Database.Games.Get(gameId);
                if (stored == null)
                {
                    return;
                }

                state.IsRunning = stored.IsRunning;
                state.IsLaunching = stored.IsLaunching;
                state.IsInstalling = stored.IsInstalling;
                state.IsUninstalling = stored.IsUninstalling;
            });

            string? process;
            if (processes.IsAssociatedProcessRunning(sourceDirectory, out process))
            {
                state.AssociatedProcess = process;
            }

            return state;
        }
    }

    internal sealed class WpfUiInvoker : IUiInvoker
    {
        private readonly Dispatcher dispatcher;

        public WpfUiInvoker(Dispatcher dispatcher)
        {
            this.dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        }

        public void Invoke(Action action)
        {
            if (dispatcher.CheckAccess())
            {
                action();
            }
            else
            {
                dispatcher.Invoke(action);
            }
        }
    }

    internal sealed class PlayniteFolderPicker : IFolderPicker
    {
        private readonly IPlayniteAPI api;

        public PlayniteFolderPicker(IPlayniteAPI api)
        {
            this.api = api;
        }

        public string? Pick(string? initialDirectory)
        {
            var selected = string.IsNullOrWhiteSpace(initialDirectory)
                ? api.Dialogs.SelectFolder()
                : api.Dialogs.SelectFolder(initialDirectory);
            return string.IsNullOrWhiteSpace(selected) ? null : selected;
        }
    }

    internal sealed class PlayniteUserPrompt : IUserPrompt
    {
        private readonly IPlayniteAPI api;

        public PlayniteUserPrompt(IPlayniteAPI api)
        {
            this.api = api;
        }

        public bool AskChooseAnotherFolder(string message)
        {
            var choose = new MessageBoxOption("Choose another folder", true, false);
            var cancel = new MessageBoxOption("Cancel", false, true);
            var result = api.Dialogs.ShowMessage(
                message,
                "GameMover",
                MessageBoxImage.Warning,
                new List<MessageBoxOption> { choose, cancel });
            return result != null && string.Equals(result.Title, choose.Title, StringComparison.Ordinal);
        }

        public bool AskDeletePartial(string destination)
        {
            var keep = new MessageBoxOption("Keep the copy", true, true);
            var delete = new MessageBoxOption("Delete the copy", false, false);
            var result = api.Dialogs.ShowMessage(
                "A copy was left at:\n" + destination + "\n\nThe original folder was not changed. Delete that copy?",
                "GameMover",
                MessageBoxImage.Warning,
                new List<MessageBoxOption> { keep, delete });
            return result != null && string.Equals(result.Title, delete.Title, StringComparison.Ordinal);
        }

        public void Info(string message)
        {
            api.Dialogs.ShowMessage(message, "GameMover");
        }

        public void Error(string message)
        {
            api.Dialogs.ShowErrorMessage(message, "GameMover");
        }
    }
}

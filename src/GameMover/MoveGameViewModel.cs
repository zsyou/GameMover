using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
using System.Windows.Input;
using GameMover.Models;
using GameMover.Services;
using Playnite.SDK;

namespace GameMover
{
    public interface IFolderPicker
    {
        string? Pick(string? initialDirectory);
    }

    public interface IUserPrompt
    {
        bool AskChooseAnotherFolder(string message);

        bool AskDeletePartial(string destination);

        void Info(string message);

        void Error(string message);
    }

    public interface IUiInvoker
    {
        void Invoke(Action action);
    }

    public sealed class MoveGameViewModel : ObservableObject
    {
        private readonly MoveRequest request;
        private readonly GameMoveService moveService;
        private readonly FileCopyService fileCopy;
        private readonly PathRewriteService paths;
        private readonly IFolderPicker folderPicker;
        private readonly IUserPrompt prompts;
        private readonly IUiInvoker ui;
        private readonly Func<GameRuntimeState> runtimeProbe;
        private readonly Func<string, string, long, int> updateMetadata;
        private readonly IMoveLog log;
        private CancellationTokenSource? cancellation;
        private GameLibrary? selectedLibrary;
        private string destinationPreview = string.Empty;
        private bool isMoving;
        private bool showProgress;
        private string stageText = string.Empty;
        private string currentFile = string.Empty;
        private string sizeText = string.Empty;
        private string statusMessage = string.Empty;
        private double progressPercent;
        private bool progressIndeterminate;
        private int pathWarnings;

        public MoveGameViewModel(
            MoveRequest request,
            ObservableCollection<GameLibrary> libraries,
            string? preselectedLibrary,
            GameMoveService moveService,
            FileCopyService fileCopy,
            PathRewriteService paths,
            IFolderPicker folderPicker,
            IUserPrompt prompts,
            IUiInvoker ui,
            Func<GameRuntimeState> runtimeProbe,
            Func<string, string, long, int> updateMetadata,
            IMoveLog log)
        {
            this.request = request ?? throw new ArgumentNullException(nameof(request));
            Libraries = libraries ?? new ObservableCollection<GameLibrary>();
            this.moveService = moveService ?? throw new ArgumentNullException(nameof(moveService));
            this.fileCopy = fileCopy ?? throw new ArgumentNullException(nameof(fileCopy));
            this.paths = paths ?? throw new ArgumentNullException(nameof(paths));
            this.folderPicker = folderPicker ?? throw new ArgumentNullException(nameof(folderPicker));
            this.prompts = prompts ?? throw new ArgumentNullException(nameof(prompts));
            this.ui = ui ?? throw new ArgumentNullException(nameof(ui));
            this.runtimeProbe = runtimeProbe ?? throw new ArgumentNullException(nameof(runtimeProbe));
            this.updateMetadata = updateMetadata ?? throw new ArgumentNullException(nameof(updateMetadata));
            this.log = log ?? NullMoveLog.Instance;

            StartCommand = new RelayCommand(Start, () => CanStart);
            CancelCommand = new RelayCommand(Cancel);
            BrowseCommand = new RelayCommand(Browse, () => IsIdle);

            if (!string.IsNullOrWhiteSpace(preselectedLibrary))
            {
                SelectLibraryPath(preselectedLibrary!);
            }
            else if (Libraries.Count == 1)
            {
                SelectedLibrary = Libraries[0];
            }

            RefreshDestination();
        }

        public event EventHandler? CloseRequested;

        public ObservableCollection<GameLibrary> Libraries { get; }

        public string GameName => request.GameName;

        public string SourceDirectory => request.SourceDirectory;

        public bool HasNoLibraries => Libraries.Count == 0;

        public bool IsMoving
        {
            get => isMoving;
            private set
            {
                isMoving = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsIdle));
                OnPropertyChanged(nameof(CanStart));
            }
        }

        public bool IsIdle => !IsMoving;

        public bool CanStart => !IsMoving && !string.IsNullOrWhiteSpace(DestinationPreview);

        public GameLibrary? SelectedLibrary
        {
            get => selectedLibrary;
            set
            {
                selectedLibrary = value;
                OnPropertyChanged();
                RefreshDestination();
            }
        }

        public string DestinationPreview
        {
            get => destinationPreview;
            private set
            {
                destinationPreview = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanStart));
            }
        }

        public bool ShowProgress
        {
            get => showProgress;
            private set
            {
                showProgress = value;
                OnPropertyChanged();
            }
        }

        public string StageText
        {
            get => stageText;
            private set
            {
                stageText = value;
                OnPropertyChanged();
            }
        }

        public string CurrentFile
        {
            get => currentFile;
            private set
            {
                currentFile = value;
                OnPropertyChanged();
            }
        }

        public string SizeText
        {
            get => sizeText;
            private set
            {
                sizeText = value;
                OnPropertyChanged();
            }
        }

        public string StatusMessage
        {
            get => statusMessage;
            private set
            {
                statusMessage = value;
                OnPropertyChanged();
            }
        }

        public double ProgressPercent
        {
            get => progressPercent;
            private set
            {
                progressPercent = value;
                OnPropertyChanged();
            }
        }

        public bool ProgressIndeterminate
        {
            get => progressIndeterminate;
            private set
            {
                progressIndeterminate = value;
                OnPropertyChanged();
            }
        }

        public ICommand StartCommand { get; }

        public ICommand CancelCommand { get; }

        public ICommand BrowseCommand { get; }

        public void RequestCancel()
        {
            cancellation?.Cancel();
        }

        private void Browse()
        {
            var initial = SelectedLibrary?.Path;
            var picked = folderPicker.Pick(initial);
            if (string.IsNullOrWhiteSpace(picked))
            {
                return;
            }

            SelectLibraryPath(picked!);
        }

        private void Cancel()
        {
            if (IsMoving)
            {
                RequestCancel();
                StatusMessage = "Cancelling…";
                return;
            }

            CloseRequested?.Invoke(this, EventArgs.Empty);
        }

        private async void Start()
        {
            if (!CanStart || SelectedLibrary == null)
            {
                return;
            }

            request.LibraryDirectory = SelectedLibrary.Path;
            string destination;
            string? error;
            if (!paths.TryGetDestination(request.SourceDirectory, request.LibraryDirectory, out destination, out error))
            {
                StatusMessage = error ?? "Choose a target library.";
                return;
            }

            if (Directory.Exists(destination) || File.Exists(destination))
            {
                HandleTargetExists();
                return;
            }

            IsMoving = true;
            ShowProgress = true;
            StatusMessage = string.Empty;
            pathWarnings = 0;
            cancellation = new CancellationTokenSource();
            var progress = new Progress<MoveProgress>(ReportProgress);
            try
            {
                var result = await System.Threading.Tasks.Task.Run(
                    () => moveService.ExecuteAsync(
                        request,
                        runtimeProbe,
                        updateMetadata,
                        progress,
                        cancellation.Token),
                    cancellation.Token).ConfigureAwait(true);

                ApplyResult(result);
            }
            catch (Exception ex)
            {
                log.Error(ex, "Move Game failed.");
                IsMoving = false;
                StatusMessage = ex.Message;
                prompts.Error(ex.Message);
            }
            finally
            {
                cancellation.Dispose();
                cancellation = null;
            }
        }

        private void ApplyResult(MoveResult result)
        {
            IsMoving = false;
            ProgressIndeterminate = false;
            StatusMessage = result.Message;
            if (result.PathWarnings > 0)
            {
                pathWarnings = result.PathWarnings;
            }

            if (result.Failure == MoveFailureKind.TargetExists)
            {
                HandleTargetExists();
                return;
            }

            if (result.DestinationCreated && !result.MetadataUpdated &&
                (result.Status == MoveStatus.Cancelled || result.Status == MoveStatus.Failed))
            {
                var partial = result.Destination;
                if (!string.IsNullOrWhiteSpace(partial) && prompts.AskDeletePartial(partial!))
                {
                    try
                    {
                        fileCopy.DeleteTree(partial!);
                        StatusMessage = result.Message + " The copied folder was deleted.";
                    }
                    catch (Exception ex)
                    {
                        log.Error(ex, "Could not delete the partial copy.");
                        prompts.Error("Could not delete the partial copy: " + ex.Message);
                    }
                }
            }

            if (result.Status == MoveStatus.Success)
            {
                var message = result.Message;
                if (pathWarnings > 0)
                {
                    message += " Some paths were left unchanged. See the Playnite log.";
                }

                prompts.Info(message);
                CloseRequested?.Invoke(this, EventArgs.Empty);
                return;
            }

            if (result.Status == MoveStatus.Cancelled)
            {
                CloseRequested?.Invoke(this, EventArgs.Empty);
                return;
            }

            if (result.MetadataUpdated)
            {
                prompts.Error(result.Message);
                CloseRequested?.Invoke(this, EventArgs.Empty);
            }
        }

        private void HandleTargetExists()
        {
            StatusMessage = "Target folder already exists.";
            if (!prompts.AskChooseAnotherFolder("Target folder already exists."))
            {
                return;
            }

            var picked = folderPicker.Pick(SelectedLibrary?.Path);
            if (!string.IsNullOrWhiteSpace(picked))
            {
                SelectLibraryPath(picked!);
            }
        }

        private void SelectLibraryPath(string path)
        {
            foreach (var library in Libraries)
            {
                if (string.Equals(library.Path, path, StringComparison.OrdinalIgnoreCase))
                {
                    SelectedLibrary = library;
                    return;
                }
            }

            var added = new GameLibrary { Path = path };
            Libraries.Add(added);
            SelectedLibrary = added;
            OnPropertyChanged(nameof(HasNoLibraries));
        }

        private void RefreshDestination()
        {
            string destination;
            string? error;
            if (SelectedLibrary == null ||
                !paths.TryGetDestination(request.SourceDirectory, SelectedLibrary.Path, out destination, out error))
            {
                DestinationPreview = string.Empty;
                return;
            }

            DestinationPreview = destination;
        }

        private void ReportProgress(MoveProgress progress)
        {
            ui.Invoke(() =>
            {
                StageText = progress.Stage;
                CurrentFile = progress.CurrentFile ?? string.Empty;
                SizeText = progress.FilesCopied + " / " + progress.TotalFiles + " files · " +
                           ByteSize.Format(progress.BytesCopied) + " / " + ByteSize.Format(progress.TotalBytes);
                if (progress.TotalBytes <= 0)
                {
                    ProgressIndeterminate = progress.Stage == MoveStage.Copying || progress.Stage == MoveStage.Preparing;
                    ProgressPercent = progress.Stage == MoveStage.Verifying || progress.Stage == MoveStage.Updating ? 100 : 0;
                }
                else
                {
                    ProgressIndeterminate = false;
                    ProgressPercent = Math.Min(100, progress.BytesCopied * 100d / progress.TotalBytes);
                }
            });
        }
    }
}

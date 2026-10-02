using GameMover.Models;
using GameMover.Services;

namespace GameMover.Tests
{
    public class GameMoveServiceTests : IDisposable
    {
        private readonly string root;
        private readonly PathRewriteService paths = new PathRewriteService();
        private readonly FileCopyService fileCopy;
        private readonly GameMoveService mover;

        public GameMoveServiceTests()
        {
            root = Path.Combine(Path.GetTempPath(), "gamemover-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            fileCopy = new FileCopyService(NullMoveLog.Instance);
            mover = new GameMoveService(
                fileCopy,
                new FileCountAndSizeVerificationService(NullMoveLog.Instance),
                paths,
                NullMoveLog.Instance,
                _ => long.MaxValue);
        }

        [Fact]
        public async Task AcceptanceMove_CopiesVerifiesUpdatesMetadataAndDeletesSource()
        {
            var source = Path.Combine(root, "TestGame");
            var library = Path.Combine(root, "EGames");
            Directory.CreateDirectory(Path.Combine(source, "Data"));
            Directory.CreateDirectory(library);
            File.WriteAllText(Path.Combine(source, "TestGame.exe"), "exe");
            File.WriteAllBytes(Path.Combine(source, "Data", "data.bin"), new byte[] { 1, 2, 3, 4 });

            string? updatedOld = null;
            string? updatedNew = null;
            long updatedBytes = -1;
            var result = await mover.ExecuteAsync(
                new MoveRequest
                {
                    GameId = Guid.NewGuid(),
                    GameName = "TestGame",
                    SourceDirectory = source,
                    LibraryDirectory = library
                },
                () => new GameRuntimeState(),
                (oldRoot, newRoot, totalBytes) =>
                {
                    updatedOld = oldRoot;
                    updatedNew = newRoot;
                    updatedBytes = totalBytes;
                    return 0;
                },
                null,
                CancellationToken.None);

            var destination = Path.Combine(library, "TestGame");
            Assert.Equal(MoveStatus.Success, result.Status);
            Assert.True(result.SourceDeleted);
            Assert.True(result.MetadataUpdated);
            Assert.False(Directory.Exists(source));
            Assert.True(File.Exists(Path.Combine(destination, "TestGame.exe")));
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, File.ReadAllBytes(Path.Combine(destination, "Data", "data.bin")));
            Assert.Equal(Path.GetFullPath(source), updatedOld);
            Assert.Equal(Path.GetFullPath(destination), updatedNew);
            Assert.Equal(new FileInfo(Path.Combine(destination, "TestGame.exe")).Length + 4, updatedBytes);
        }

        [Fact]
        public async Task CancelDuringCopy_KeepsSourceAndDoesNotUpdateMetadata()
        {
            var source = Path.Combine(root, "BigGame");
            var library = Path.Combine(root, "Library");
            Directory.CreateDirectory(source);
            Directory.CreateDirectory(library);
            File.WriteAllBytes(Path.Combine(source, "payload.bin"), new byte[1024 * 1024]);

            var cts = new CancellationTokenSource();
            var progress = new Progress<MoveProgress>(update =>
            {
                if (update.BytesCopied > 0)
                {
                    cts.Cancel();
                }
            });
            var metadataCalled = false;
            var result = await mover.ExecuteAsync(
                new MoveRequest
                {
                    GameName = "BigGame",
                    SourceDirectory = source,
                    LibraryDirectory = library
                },
                () => new GameRuntimeState(),
                (_, _, _) =>
                {
                    metadataCalled = true;
                    return 0;
                },
                progress,
                cts.Token);

            Assert.Equal(MoveStatus.Cancelled, result.Status);
            Assert.False(metadataCalled);
            Assert.True(File.Exists(Path.Combine(source, "payload.bin")));
            Assert.True(result.DestinationCreated);
        }

        [Fact]
        public async Task ExistingTarget_IsRefusedAndSourceStays()
        {
            var source = Path.Combine(root, "Game");
            var library = Path.Combine(root, "Lib");
            var destination = Path.Combine(library, "Game");
            Directory.CreateDirectory(source);
            Directory.CreateDirectory(destination);
            File.WriteAllText(Path.Combine(source, "game.exe"), "keep");
            File.WriteAllText(Path.Combine(destination, "already.txt"), "do-not-touch");

            var result = await mover.ExecuteAsync(
                new MoveRequest
                {
                    GameName = "Game",
                    SourceDirectory = source,
                    LibraryDirectory = library
                },
                () => new GameRuntimeState(),
                (_, _, _) => 0,
                null,
                CancellationToken.None);

            Assert.Equal(MoveStatus.Failed, result.Status);
            Assert.Equal(MoveFailureKind.TargetExists, result.Failure);
            Assert.Contains("Target folder already exists.", result.Message);
            Assert.Equal("keep", File.ReadAllText(Path.Combine(source, "game.exe")));
            Assert.Equal("do-not-touch", File.ReadAllText(Path.Combine(destination, "already.txt")));
        }

        [Fact]
        public async Task RunningGame_IsBlockedBeforeCopy()
        {
            var source = Path.Combine(root, "Running");
            var library = Path.Combine(root, "Shelf");
            Directory.CreateDirectory(source);
            Directory.CreateDirectory(library);
            File.WriteAllText(Path.Combine(source, "game.exe"), "x");

            var result = await mover.ExecuteAsync(
                new MoveRequest
                {
                    GameName = "Running",
                    SourceDirectory = source,
                    LibraryDirectory = library
                },
                () => new GameRuntimeState { IsRunning = true },
                (_, _, _) => throw new InvalidOperationException("metadata should not run"),
                null,
                CancellationToken.None);

            Assert.Equal(MoveFailureKind.GameRunning, result.Failure);
            Assert.False(Directory.Exists(Path.Combine(library, "Running")));
            Assert.True(File.Exists(Path.Combine(source, "game.exe")));
        }

        [Fact]
        public async Task NotEnoughSpace_IsBlockedBeforeCopy()
        {
            var source = Path.Combine(root, "Huge");
            var library = Path.Combine(root, "SmallDisk");
            Directory.CreateDirectory(source);
            Directory.CreateDirectory(library);
            File.WriteAllBytes(Path.Combine(source, "game.bin"), new byte[64]);
            var tight = new GameMoveService(fileCopy, new FileCountAndSizeVerificationService(), paths, NullMoveLog.Instance, _ => 8);

            var result = await tight.ExecuteAsync(
                new MoveRequest
                {
                    GameName = "Huge",
                    SourceDirectory = source,
                    LibraryDirectory = library
                },
                () => new GameRuntimeState(),
                (_, _, _) => 0,
                null,
                CancellationToken.None);

            Assert.Equal(MoveFailureKind.NotEnoughSpace, result.Failure);
            Assert.True(File.Exists(Path.Combine(source, "game.bin")));
            Assert.False(Directory.Exists(Path.Combine(library, "Huge")));
        }

        [Fact]
        public async Task VerificationMismatch_DoesNotUpdateMetadataOrDeleteSource()
        {
            var source = Path.Combine(root, "Mismatch");
            var library = Path.Combine(root, "MismatchLib");
            Directory.CreateDirectory(source);
            Directory.CreateDirectory(library);
            File.WriteAllText(Path.Combine(source, "game.exe"), "original");

            var tampering = new TamperingVerificationService();
            var service = new GameMoveService(fileCopy, tampering, paths, NullMoveLog.Instance, _ => long.MaxValue);
            var metadataCalled = false;
            var result = await service.ExecuteAsync(
                new MoveRequest
                {
                    GameName = "Mismatch",
                    SourceDirectory = source,
                    LibraryDirectory = library
                },
                () => new GameRuntimeState(),
                (_, _, _) =>
                {
                    metadataCalled = true;
                    return 0;
                },
                null,
                CancellationToken.None);

            Assert.Equal(MoveFailureKind.VerifyFailed, result.Failure);
            Assert.False(metadataCalled);
            Assert.True(File.Exists(Path.Combine(source, "game.exe")));
            Assert.True(result.DestinationCreated);
        }

        [Fact]
        public void DeleteTree_RefusesDriveRoot()
        {
            var rootPath = Path.GetPathRoot(root);
            Assert.False(string.IsNullOrEmpty(rootPath));
            Assert.Throws<InvalidOperationException>(() => fileCopy.DeleteTree(rootPath!));
        }

        public void Dispose()
        {
            if (Directory.Exists(root))
            {
                try
                {
                    fileCopy.DeleteTree(root);
                }
                catch (IOException)
                {
                    Directory.Delete(root, true);
                }
            }
        }

        private sealed class TamperingVerificationService : IVerificationService
        {
            public string Name => "tamper";

            public VerificationMode Mode => VerificationMode.FileCountAndSize;

            public Task<VerificationResult> VerifyAsync(string sourceDirectory, string destinationDirectory, CancellationToken cancellationToken)
            {
                return Task.FromResult(new VerificationResult
                {
                    Match = false,
                    Message = "forced mismatch"
                });
            }
        }
    }
}

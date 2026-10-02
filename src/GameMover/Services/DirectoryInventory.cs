using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace GameMover.Services
{
    public sealed class FileEntry
    {
        public FileEntry(string relativePath, long length)
        {
            RelativePath = relativePath;
            Length = length;
        }

        public string RelativePath { get; }

        public long Length { get; }
    }

    public sealed class DirectoryInventory
    {
        public DirectoryInventory(string root, IReadOnlyList<FileEntry> files, IReadOnlyList<string> directories, int skippedReparsePoints)
        {
            Root = root;
            Files = files;
            Directories = directories;
            SkippedReparsePoints = skippedReparsePoints;
            long total = 0;
            foreach (var file in files)
            {
                total += file.Length;
            }

            TotalBytes = total;
        }

        public string Root { get; }

        public IReadOnlyList<FileEntry> Files { get; }

        public IReadOnlyList<string> Directories { get; }

        public long TotalBytes { get; }

        public int SkippedReparsePoints { get; }
    }

    public static class DirectoryInventoryScanner
    {
        public static DirectoryInventory Scan(string root, IMoveLog log, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                throw new ArgumentException("Directory is empty.", nameof(root));
            }

            var fullRoot = Path.GetFullPath(root);
            if (!Directory.Exists(fullRoot))
            {
                throw new DirectoryNotFoundException("Directory does not exist: " + fullRoot);
            }

            var files = new List<FileEntry>();
            var directories = new List<string>();
            var skipped = 0;
            var pending = new Stack<string>();
            pending.Push(fullRoot);

            while (pending.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var current = pending.Pop();
                DirectoryInfo directory;
                try
                {
                    directory = new DirectoryInfo(current);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    log.Error(ex, "Cannot read directory " + current);
                    throw;
                }

                if (!PathsEqual(current, fullRoot) && IsReparsePoint(directory.Attributes))
                {
                    skipped++;
                    log.Warn("Skipped reparse point: " + current);
                    continue;
                }

                IEnumerable<DirectoryInfo> children;
                IEnumerable<FileInfo> fileInfos;
                try
                {
                    children = directory.EnumerateDirectories();
                    fileInfos = directory.EnumerateFiles();
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    log.Error(ex, "Cannot enumerate " + current);
                    throw;
                }

                foreach (var child in children)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (IsReparsePoint(child.Attributes))
                    {
                        skipped++;
                        log.Warn("Skipped reparse point: " + child.FullName);
                        continue;
                    }

                    directories.Add(ToRelative(fullRoot, child.FullName));
                    pending.Push(child.FullName);
                }

                foreach (var file in fileInfos)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (IsReparsePoint(file.Attributes))
                    {
                        skipped++;
                        log.Warn("Skipped reparse point: " + file.FullName);
                        continue;
                    }

                    files.Add(new FileEntry(ToRelative(fullRoot, file.FullName), file.Length));
                }
            }

            if (skipped > 0)
            {
                log.Warn("Skipped " + skipped + " reparse point(s) under " + fullRoot + ". Junctions and symlinks are not moved.");
            }

            return new DirectoryInventory(fullRoot, files, directories, skipped);
        }

        private static bool IsReparsePoint(FileAttributes attributes)
        {
            return (attributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint;
        }

        private static bool PathsEqual(string left, string right)
        {
            return string.Equals(
                Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
        }

        private static string ToRelative(string root, string fullPath)
        {
            var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var normalizedPath = Path.GetFullPath(fullPath);
            if (normalizedPath.Length < normalizedRoot.Length)
            {
                return normalizedPath;
            }

            return normalizedPath.Substring(normalizedRoot.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }
}

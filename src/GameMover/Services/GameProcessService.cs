using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace GameMover.Services
{
    public sealed class GameProcessService
    {
        private readonly Func<IEnumerable<string>> enumerateProcessPaths;
        private readonly IMoveLog log;

        public GameProcessService(IMoveLog log)
        {
            this.log = log ?? NullMoveLog.Instance;
            enumerateProcessPaths = ReadProcessPaths;
        }

        public GameProcessService(Func<IEnumerable<string>> enumerateProcessPaths)
            : this(enumerateProcessPaths, NullMoveLog.Instance)
        {
        }

        public GameProcessService(Func<IEnumerable<string>> enumerateProcessPaths, IMoveLog log)
        {
            this.enumerateProcessPaths = enumerateProcessPaths ?? throw new ArgumentNullException(nameof(enumerateProcessPaths));
            this.log = log ?? NullMoveLog.Instance;
        }

        public bool IsAssociatedProcessRunning(string? installDirectory, out string? description)
        {
            description = null;
            if (string.IsNullOrWhiteSpace(installDirectory))
            {
                return false;
            }

            var paths = enumerateProcessPaths();
            if (paths == null)
            {
                return false;
            }

            var matcher = new PathRewriteService();
            foreach (var processPath in paths)
            {
                if (string.IsNullOrWhiteSpace(processPath))
                {
                    continue;
                }

                if (!matcher.IsUnderRoot(processPath, installDirectory))
                {
                    continue;
                }

                description = processPath;
                log.Info("Found a running process inside the install folder: " + processPath);
                return true;
            }

            return false;
        }

        private IEnumerable<string> ReadProcessPaths()
        {
            var paths = new List<string>();
            var inaccessible = 0;
            foreach (var process in Process.GetProcesses())
            {
                try
                {
                    var fileName = process.MainModule?.FileName;
                    if (!string.IsNullOrWhiteSpace(fileName))
                    {
                        paths.Add(fileName!);
                    }
                }
                catch (Exception ex) when (
                    ex is InvalidOperationException ||
                    ex is System.ComponentModel.Win32Exception ||
                    ex is UnauthorizedAccessException ||
                    ex is NotSupportedException)
                {
                    inaccessible++;
                }
                finally
                {
                    process.Dispose();
                }
            }

            if (inaccessible > 0)
            {
                log.Warn("Could not read " + inaccessible + " process path(s). Those processes were skipped.");
            }

            return paths;
        }
    }
}

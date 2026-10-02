using System;
using Playnite.SDK;

namespace GameMover.Services
{
    public sealed class PlayniteMoveLog : IMoveLog
    {
        private readonly ILogger logger;

        public PlayniteMoveLog(ILogger logger)
        {
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public void Info(string message)
        {
            logger.Info(message);
        }

        public void Warn(string message)
        {
            logger.Warn(message);
        }

        public void Error(string message)
        {
            logger.Error(message);
        }

        public void Error(Exception exception, string message)
        {
            logger.Error(exception, message);
        }
    }
}

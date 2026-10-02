using System;

namespace GameMover.Services
{
    public interface IMoveLog
    {
        void Info(string message);

        void Warn(string message);

        void Error(string message);

        void Error(Exception exception, string message);
    }

    public sealed class NullMoveLog : IMoveLog
    {
        public static readonly NullMoveLog Instance = new NullMoveLog();

        public void Info(string message)
        {
        }

        public void Warn(string message)
        {
        }

        public void Error(string message)
        {
        }

        public void Error(Exception exception, string message)
        {
        }
    }
}

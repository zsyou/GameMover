using System;

namespace GameMover.Models
{
    public sealed class MoveRequest
    {
        public Guid GameId { get; set; }

        public string GameName { get; set; } = string.Empty;

        public string SourceDirectory { get; set; } = string.Empty;

        public string LibraryDirectory { get; set; } = string.Empty;
    }
}

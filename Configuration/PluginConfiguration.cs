using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.PlaylistCleaner.Configuration;

public sealed class PluginConfiguration : BasePluginConfiguration
{
    public Guid? SelectedPlaylistId { get; set; }

    public string SelectedPlaylistName { get; set; } = string.Empty;

    public bool AutomaticCleanupEnabled { get; set; } = true;

    public string ExecutionTime { get; set; } = "06:00";

    public bool MarkEpisodesWatched { get; set; } = true;

    public bool ClearPlaybackProgress { get; set; } = true;

    public string LastExecutionStatus { get; set; } = "Never run";

    public string? LastExecutionStartedUtc { get; set; }

    public string? LastExecutionCompletedUtc { get; set; }

    public string? LastSuccessfulExecutionUtc { get; set; }

    public int LastUsersProcessed { get; set; }

    public int LastEpisodesProcessed { get; set; }

    public int LastUniqueEpisodesFound { get; set; }

    public int LastErrors { get; set; }
}
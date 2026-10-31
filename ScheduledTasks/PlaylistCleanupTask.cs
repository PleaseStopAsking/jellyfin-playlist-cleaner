using System.Diagnostics;
using System.Globalization;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.PlaylistCleaner.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Playlists;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.PlaylistCleaner.ScheduledTasks;

public sealed class PlaylistCleanupTask : IScheduledTask
{
    private static readonly SemaphoreSlim ExecutionGate = new(1, 1);

    private readonly IPlaylistManager _playlistManager;
    private readonly ILibraryManager _libraryManager;
    private readonly IUserDataManager _userDataManager;
    private readonly IUserManager _userManager;
    private readonly ILogger<PlaylistCleanupTask> _logger;

    public PlaylistCleanupTask(
        IPlaylistManager playlistManager,
        ILibraryManager libraryManager,
        IUserDataManager userDataManager,
        IUserManager userManager,
        ILogger<PlaylistCleanupTask> logger)
    {
        _playlistManager = playlistManager;
        _libraryManager = libraryManager;
        _userDataManager = userDataManager;
        _userManager = userManager;
        _logger = logger;
    }

    public string Name => "Playlist cleanup";

    public string Key => "Jellyfin.Plugin.PlaylistCleaner.Cleanup";

    public string Description => "Marks the selected playlist's TV episodes and movies watched and clears resume positions for Jellyfin users.";

    public string Category => "Library";

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        var configuration = Plugin.Instance?.Configuration;
        if (configuration?.AutomaticCleanupEnabled != true)
        {
            return [];
        }

        if (!TimeSpan.TryParseExact(configuration.ExecutionTime, @"hh\:mm", CultureInfo.InvariantCulture, out var time))
        {
            time = new TimeSpan(6, 0, 0);
        }

        return
        [
            new TaskTriggerInfo
            {
                Type = TaskTriggerInfoType.DailyTrigger,
                TimeOfDayTicks = time.Ticks
            }
        ];
    }

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        if (!await ExecutionGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            _logger.LogWarning("Playlist cleanup is already running; ignoring duplicate execution.");
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        var startedUtc = DateTimeOffset.UtcNow;
        var usersProcessed = 0;
        var itemsProcessed = 0;
        var uniqueItemCount = 0;
        var errors = 0;

        try
        {
            UpdateExecutionStatus(configuration =>
            {
                configuration.LastExecutionStatus = "Running";
                configuration.LastExecutionStartedUtc = startedUtc.ToString("O", CultureInfo.InvariantCulture);
                configuration.LastExecutionCompletedUtc = null;
                configuration.LastUsersProcessed = 0;
                configuration.LastEpisodesProcessed = 0;
                configuration.LastUniqueEpisodesFound = 0;
                configuration.LastErrors = 0;
            });

            _logger.LogInformation("Playlist cleanup started.");

            var configuration = Plugin.Instance?.Configuration;
            if (configuration is null || configuration.SelectedPlaylistId is null)
            {
                _logger.LogInformation("No playlist is selected; cleanup was skipped.");
                CompleteRun("Skipped: no playlist selected", startedUtc, stopwatch.Elapsed, usersProcessed, itemsProcessed, uniqueItemCount, errors, false);
                return;
            }

            if (!configuration.MarkEpisodesWatched && !configuration.ClearPlaybackProgress)
            {
                _logger.LogInformation("Both playback operations are disabled; cleanup was skipped.");
                CompleteRun("Skipped: no operations enabled", startedUtc, stopwatch.Elapsed, usersProcessed, itemsProcessed, uniqueItemCount, errors, false);
                return;
            }

            var allUsers = _userManager.GetUsers().ToArray();
            var usersWithAccess = new List<User>();
            MediaBrowser.Controller.Playlists.Playlist? playlist = null;

            foreach (var user in allUsers)
            {
                try
                {
                    var accessiblePlaylist = _playlistManager.GetPlaylists(user.Id)
                        .FirstOrDefault(item => item.Id == configuration.SelectedPlaylistId.Value);
                    if (accessiblePlaylist is null)
                    {
                        continue;
                    }

                    playlist ??= accessiblePlaylist;
                    usersWithAccess.Add(user);
                }
                catch (Exception exception)
                {
                    errors++;
                    _logger.LogError(exception, "Failed to check playlist access for user {UserName}.", user.Username);
                }
            }

            if (playlist is null)
            {
                errors++;
                _logger.LogError("Selected playlist {PlaylistId} is missing or inaccessible to all users; no playback data was changed.", configuration.SelectedPlaylistId);
                CompleteRun("Failed: selected playlist missing or inaccessible", startedUtc, stopwatch.Elapsed, usersProcessed, itemsProcessed, uniqueItemCount, errors, false);
                return;
            }

            var mediaItemIds = playlist.GetManageableItems()
                .Select(entry => entry.Item2)
                .Where(item => item is Episode or Movie)
                .Select(item => item.Id)
                .Distinct()
                .ToArray();

            uniqueItemCount = mediaItemIds.Length;
            var users = usersWithAccess.ToArray();

            _logger.LogInformation("Playlist: {PlaylistName}", playlist.Name);
            _logger.LogInformation("Found {ItemCount} unique TV episodes and movies and {UserCount} users with playlist access.", mediaItemIds.Length, users.Length);

            var totalOperations = Math.Max(1, mediaItemIds.Length * users.Length);
            var completedOperations = 0;

            foreach (var user in users)
            {
                cancellationToken.ThrowIfCancellationRequested();
                usersProcessed++;
                try
                {
                    _logger.LogInformation("Processing user: {UserName}", user.Username);

                    foreach (var itemId in mediaItemIds)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        try
                        {
                            var accessibleItem = _libraryManager.GetItemById<BaseItem>(itemId, user);
                            if (accessibleItem is not null)
                            {
                                ProcessItem(user, accessibleItem, configuration, cancellationToken);
                                itemsProcessed++;
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception exception)
                        {
                            errors++;
                            _logger.LogError(exception, "Failed to update media item {ItemId} for user {UserName}.", itemId, user.Username);
                        }

                        completedOperations++;
                        progress.Report(completedOperations * 100d / totalOperations);
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    errors++;
                    _logger.LogError(exception, "Failed while processing user {UserName}; continuing with the next user.", user.Username);
                }
            }

            stopwatch.Stop();
            var status = errors == 0 ? "Completed" : "Completed with errors";
            CompleteRun(status, startedUtc, stopwatch.Elapsed, usersProcessed, itemsProcessed, uniqueItemCount, errors, errors == 0);
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            CompleteRun("Cancelled", startedUtc, stopwatch.Elapsed, usersProcessed, itemsProcessed, uniqueItemCount, errors, false);
            throw;
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            errors++;
            _logger.LogError(exception, "Playlist cleanup failed after {Duration}.", stopwatch.Elapsed);
            CompleteRun("Failed", startedUtc, stopwatch.Elapsed, usersProcessed, itemsProcessed, uniqueItemCount, errors, false);
            throw;
        }
        finally
        {
            ExecutionGate.Release();
        }
    }

    private void ProcessItem(User user, BaseItem item, PluginConfiguration configuration, CancellationToken cancellationToken)
    {
        if (configuration.MarkEpisodesWatched)
        {
            item.MarkPlayed(user, null, configuration.ClearPlaybackProgress);
            return;
        }

        if (!configuration.ClearPlaybackProgress)
        {
            return;
        }

        var userData = _userDataManager.GetUserData(user, item);
        if (userData is null || userData.PlaybackPositionTicks == 0)
        {
            return;
        }

        userData.PlaybackPositionTicks = 0;
        _userDataManager.SaveUserData(user, item, userData, UserDataSaveReason.UpdateUserData, cancellationToken);
    }

    private void CompleteRun(
        string status,
        DateTimeOffset startedUtc,
        TimeSpan duration,
        int usersProcessed,
        int itemsProcessed,
        int uniqueItemCount,
        int errors,
        bool successful)
    {
        var completedUtc = DateTimeOffset.UtcNow;
        UpdateExecutionStatus(configuration =>
        {
            configuration.LastExecutionStatus = status;
            configuration.LastExecutionStartedUtc = startedUtc.ToString("O", CultureInfo.InvariantCulture);
            configuration.LastExecutionCompletedUtc = completedUtc.ToString("O", CultureInfo.InvariantCulture);
            configuration.LastUsersProcessed = usersProcessed;
            configuration.LastEpisodesProcessed = itemsProcessed;
            configuration.LastUniqueEpisodesFound = uniqueItemCount;
            configuration.LastErrors = errors;
            if (successful)
            {
                configuration.LastSuccessfulExecutionUtc = completedUtc.ToString("O", CultureInfo.InvariantCulture);
            }
        });

        _logger.LogInformation(
            "Cleanup finished with status {Status} in {Duration}. Users processed: {UserCount}. Media items processed: {ItemCount}. Unique media items: {UniqueItemCount}. Errors: {ErrorCount}.",
            status,
            duration,
            usersProcessed,
            itemsProcessed,
            uniqueItemCount,
            errors);
    }

    private static void UpdateExecutionStatus(Action<PluginConfiguration> update)
    {
        var plugin = Plugin.Instance;
        if (plugin is null)
        {
            return;
        }

        var configuration = plugin.Configuration;
        update(configuration);
        plugin.UpdateConfiguration(configuration);
    }
}
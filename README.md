[![GitHub Release](https://img.shields.io/github/v/release/PleaseStopAsking/jellyfin-playlist-cleaner)](https://github.com/PleaseStopAsking/jellyfin-playlist-cleaner/releases)
[![GitHub checks](https://img.shields.io/github/actions/workflow/status/PleaseStopAsking/jellyfin-playlist-cleaner/release.yml)](https://github.com/PleaseStopAsking/jellyfin-playlist-cleaner/actions/workflows/release.yml)

# Jellyfin Playlist Cleaner

A Jellyfin plugin that marks TV episodes and movies from a selected playlist as watched and clears each user's resume position without changing shared media metadata.

For those who use a playlist to fall asleep to their favorite shows, you know the frustration of having to manually mark episodes as watched and clear resume positions the next day. This plugin automates that process, ensuring a seamless viewing experience.

## Installation

### Method 1: Plugin Repository (Recommended)

1. Open your Jellyfin Web Client and navigate to **Dashboard** > **Plugins** > **Repositories**.
2. Click **+ Add Repository**.
3. Fill in the details:
   - **Repository Name**: `Jellyfin Playlist Cleaner`
   - **Repository URL**:
     ```
     https://github.com/PleaseStopAsking/jellyfin-playlist-cleaner/releases/latest/download/manifest.json
     ```
4. Click **Save**.
5. Find **Jellyfin Playlist Cleaner**, and click **Install**.
6. **Restart** your Jellyfin server.

---

### Method 2: Manual Installation

1. Go to the [Releases](https://github.com/PleaseStopAsking/jellyfin-playlist-cleaner/releases) page.
2. Download the latest `PlaylistCleaner_{version}.zip` asset.
3. Extract the ZIP contents into your Jellyfin `plugins` directory inside a folder named `PlaylistCleaner`:

   | Platform | Typical Plugins Path |
   | :--- | :--- |
   | **Linux (bare-metal)** | `/var/lib/jellyfin/plugins/PlaylistCleaner/` |
   | **Docker** | `<config_dir>/plugins/PlaylistCleaner/` |
   | **Windows** | `%ProgramData%\Jellyfin\Server\plugins\PlaylistCleaner\` |
   | **macOS** | `~/.local/share/jellyfin/plugins/PlaylistCleaner/` |

   The target folder should contain:
   ```text
   PlaylistCleaner/
  ├── Jellyfin.Plugin.PlaylistCleaner.dll
   ├── PlaylistCleaner.pdb
   └── meta.json
   ```

4. **Restart** your Jellyfin server.

---

## Configuration

Configure the plugin via **Dashboard** > **Plugins** > **Playlist Cleaner**:

- **Selected playlist**:
  - Choose the playlist you want the plugin to clean up. The plugin will track the playlist by its ID, so renaming the playlist will not break the selection.

- **Enable automatic cleanup**:
  - `Yes` *(default)*: The plugin will automatically clean up playlists according to the scheduled task.
  - `No`: Automatic cleanup is disabled; you must run it manually from the plugin page.

- **Daily execution time**:
  - `Daily at 06:00` *(default)*: The plugin will run the automatic cleanup task daily at 06:00 server-local time.
  - `Custom`: Allows you to configure a custom schedule via Jellyfin's Scheduled Tasks dashboard.

- **Mark episodes and movies watched**:
  - `Yes` *(default)*: The plugin will mark affected episodes and movies as watched.
  - `No`: Items will not be marked as watched.

- **Clear playback progress**:
  - `Yes` *(default)*: The plugin will clear playback progress for the affected items.
  - `No`: Playback progress will not be cleared.
---

## Notes

- The plugin page includes an administrator-only **Execute now** action and displays the last result, timestamp, user/item counts, errors, and plugin version.
- The task checks each Jellyfin user's accessible playlists and only processes users who can access the selected playlist.
- Only direct playlist entries resolved as TV `Episode` or `Movie` items are processed. Other entry types are ignored and duplicate item IDs are processed once.
- Per-user/item failures are logged and do not stop the rest of the run. No direct database access or global item metadata updates are used.
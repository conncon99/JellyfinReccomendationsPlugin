# Jellyfin Recommendations Plugin

A Jellyfin server plugin concept that creates a cross-client recommendations library backed by Trakt and Seerr.

The plugin is designed around Jellyfin's server-side library model, so the same recommendations and actions work in Jellyfin Web, Android, Android TV, and Fire TV. It automatically creates separate **Recommended Movies** and **Recommended Series** home libraries.

Use Jellyfin's native card actions directly from either shelf:

- **Rate** a title to mark it watched and use the score in future recommendations.
- **Dislike** a title to mark it Not Interested and permanently hide it.
- **Mark Played** to mark it watched with a neutral three-star score.
- **Favorite** a title to request it in Seerr.

On Android TV and Fire TV, long-press Select on a card to open its actions. Because these are native Jellyfin actions rather than injected web controls, the workflow is consistent across clients.

## MVP Flow

1. Configure your Seerr URL, API key, recommendation library directory, and the Jellyfin libraries to use as watch-history sources.
2. Configure Trakt with a client ID and access token if you want personal watch-habit recommendations.
3. Optionally enable Jellyfin watch-history sync to populate your connected Trakt profile.
4. Run the `Refresh Jellyfin Recommendations` scheduled task.
5. The plugin samples recently watched movies and series, merges Trakt personal recommendations with Seerr/TMDb similar-title recommendations, and writes `.strm`, `.nfo`, and plugin metadata files into the recommendation library directory.
6. JellyRec creates and maintains the two recommendation libraries automatically.
7. Use Rate, Dislike, Mark Played, or Favorite directly from a recommendation card.

## Remote Jellyfin Testing

Jellyfin cannot install a plugin directly from source code in a git repository. It installs from a plugin repository manifest that points to a downloadable ZIP.

The easiest remote test loop is:

1. Push this project to GitHub.
2. Run `.\scripts\package-release.ps1 -RepositoryOwner <you> -RepositoryName <repo>`.
3. Commit and push the updated `manifest.json` and ZIP from `release/`.
4. In Jellyfin `Dashboard -> Plugins -> Repositories`, add:

   `https://raw.githubusercontent.com/<you>/<repo>/main/manifest.json`

   Do not use the normal GitHub page URL (`https://github.com/.../blob/main/manifest.json`). Jellyfin needs the raw JSON URL.

5. Install **Jellyfin Recommendations** from the plugin catalog and restart Jellyfin.

## Recommendation Strategy

The initial build treats Trakt as the primary recommendation provider because it can use real user watch habits. Seerr/TMDb recommendations remain enabled as a fallback and enrichment provider because they are simple, stable, and already connected to the request workflow.

The current ranker:

- pulls personal Trakt movie and show recommendations when Trakt credentials are configured;
- samples recently watched Jellyfin movies and series that have TMDb IDs;
- asks Seerr for similar recommendations for those watched seeds;
- excludes media already present in Jellyfin;
- deduplicates candidates by media type and TMDb ID;
- weights Trakt candidates above Seerr candidates by default.
- diversifies the final shelf across source titles, movies/shows, and release decades;
- permanently excludes titles marked **Not Interested** using Dislike or the administration page;
- uses manually watched star ratings as weighted seeds for subsequent refreshes;
- enriches Trakt results with TMDb artwork through Seerr and supplies a graceful image fallback.

Watch-history sync runs with each scheduled or manual recommendation refresh. The configuration page also provides **Resync All Watch History**, which safely submits the complete eligible history again and reports discovered, submitted, accepted, and skipped counts. Episodes can be matched using their TMDb/TVDB IDs or their parent series TMDb ID plus season and episode number.

Recommendations retain their original first-seen time across refreshes. By default they expire after seven days, are removed from the recommendation folder, and enter a seven-day cooldown before they may be recommended again. The lifetime is configurable under Advanced settings.

Future OAuth/device-code setup should replace manual Trakt access-token entry.

## Project Status

The plugin targets Jellyfin 10.11 and is built and packaged with .NET 9.

## References

- JellyBridge: https://github.com/kinggeorges12/JellyBridge
- Jellyfin plugin docs: https://jellyfin.org/docs/general/server/plugins/
- Seerr API docs: https://api-docs.overseerr.dev/
- Trakt API docs: https://trakt.docs.apiary.io/

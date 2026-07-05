# Jellyfin Recommendations Plugin

A Jellyfin server plugin concept that creates a cross-client recommendations library backed by Trakt and Seerr.

The plugin is designed around Jellyfin's server-side library model, so the same recommendation shelf can appear in Jellyfin Web, Android, Android TV, and Fire TV clients. Users request a recommendation by marking its placeholder item as a favorite in Jellyfin; the plugin then creates the corresponding Seerr request, which your existing Sonarr/Radarr setup can process.

## MVP Flow

1. Configure your Seerr URL, API key, recommendation library directory, and the Jellyfin libraries to use as watch-history sources.
2. Configure Trakt with a client ID and access token if you want personal watch-habit recommendations.
3. Run the `Refresh Jellyfin Recommendations` scheduled task.
4. The plugin samples recently watched movies and series, merges Trakt personal recommendations with Seerr/TMDb similar-title recommendations, and writes `.strm`, `.nfo`, and plugin metadata files into the recommendation library directory.
5. Add that directory to Jellyfin as a Movies/Shows library.
6. Favorite an item from that library to request it in Seerr.

## Remote Jellyfin Testing

Jellyfin cannot install a plugin directly from source code in a git repository. It installs from a plugin repository manifest that points to a downloadable ZIP.

The easiest remote test loop is:

1. Push this project to GitHub.
2. Run `.\scripts\package-release.ps1 -RepositoryOwner <you> -RepositoryName <repo>`.
3. Commit and push the updated `manifest.json` and ZIP from `release/`.
4. In Jellyfin `Dashboard -> Plugins -> Repositories`, add:

   `https://raw.githubusercontent.com/<you>/<repo>/main/manifest.json`

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

Future OAuth/device-code setup should replace manual Trakt access-token entry.

## Project Status

This repository contains the initial plugin scaffold and implementation slice. It follows the same cross-client pattern as JellyBridge but narrows the feature set to recommendations.

Build verification is pending because this machine currently has .NET runtimes but no .NET SDK installed.

## References

- JellyBridge: https://github.com/kinggeorges12/JellyBridge
- Jellyfin plugin docs: https://jellyfin.org/docs/general/server/plugins/
- Seerr API docs: https://api-docs.overseerr.dev/
- Trakt API docs: https://trakt.docs.apiary.io/

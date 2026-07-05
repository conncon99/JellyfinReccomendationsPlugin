(function () {
  const pluginId = "1d1af7ee-7c42-42a0-9f03-d202274e68d5";

  function byId(id) {
    return document.getElementById(id);
  }

  function readForm() {
    return {
      Enabled: byId("Enabled").checked,
      SeerrUrl: byId("SeerrUrl").value,
      ApiKey: byId("ApiKey").value,
      RecommendationLibraryPath: byId("RecommendationLibraryPath").value,
      SourceLibraryPaths: byId("SourceLibraryPaths").value,
      EnableTraktRecommendations: byId("EnableTraktRecommendations").checked,
      TraktClientId: byId("TraktClientId").value,
      TraktAccessToken: byId("TraktAccessToken").value,
      EnableSeerrRecommendations: byId("EnableSeerrRecommendations").checked,
      TraktWeight: Number(byId("TraktWeight").value || 3),
      SeerrWeight: Number(byId("SeerrWeight").value || 1),
      RecentlyWatchedLimit: Number(byId("RecentlyWatchedLimit").value || 20),
      RecommendationsPerSeed: Number(byId("RecommendationsPerSeed").value || 8),
      MaxRecommendations: Number(byId("MaxRecommendations").value || 100),
      RefreshIntervalHours: Number(byId("RefreshIntervalHours").value || 24),
      RequestOnlyFirstSeason: byId("RequestOnlyFirstSeason").checked,
      RemoveAfterRequest: byId("RemoveAfterRequest").checked
    };
  }

  function writeForm(config) {
    byId("Enabled").checked = !!config.Enabled;
    byId("SeerrUrl").value = config.SeerrUrl || "";
    byId("ApiKey").value = config.ApiKey || "";
    byId("RecommendationLibraryPath").value = config.RecommendationLibraryPath || "";
    byId("SourceLibraryPaths").value = config.SourceLibraryPaths || "";
    byId("EnableTraktRecommendations").checked = config.EnableTraktRecommendations !== false;
    byId("TraktClientId").value = config.TraktClientId || "";
    byId("TraktAccessToken").value = config.TraktAccessToken || "";
    byId("EnableSeerrRecommendations").checked = config.EnableSeerrRecommendations !== false;
    byId("TraktWeight").value = config.TraktWeight || 3;
    byId("SeerrWeight").value = config.SeerrWeight || 1;
    byId("RecentlyWatchedLimit").value = config.RecentlyWatchedLimit || 20;
    byId("RecommendationsPerSeed").value = config.RecommendationsPerSeed || 8;
    byId("MaxRecommendations").value = config.MaxRecommendations || 100;
    byId("RefreshIntervalHours").value = config.RefreshIntervalHours || 24;
    byId("RequestOnlyFirstSeason").checked = config.RequestOnlyFirstSeason !== false;
    byId("RemoveAfterRequest").checked = config.RemoveAfterRequest !== false;
  }

  document.querySelector("#JellyRecConfigPage").addEventListener("pageshow", function () {
    Dashboard.showLoadingMsg();
    ApiClient.getPluginConfiguration(pluginId).then(function (config) {
      writeForm(config);
      Dashboard.hideLoadingMsg();
    });
  });

  document.querySelector("#JellyRecConfigForm").addEventListener("submit", function (event) {
    event.preventDefault();
    Dashboard.showLoadingMsg();
    ApiClient.updatePluginConfiguration(pluginId, readForm()).then(function () {
      return ApiClient.ajax({
        type: "POST",
        url: ApiClient.getUrl("JellyRec/EnsureRecommendationFolder"),
        data: JSON.stringify(readForm()),
        contentType: "application/json"
      });
    }).then(function () {
      Dashboard.processPluginConfigurationUpdateResult();
    }).catch(function () {
      Dashboard.alert("Saved settings, but JellyRec could not create the recommendation folder. Check the path and Jellyfin permissions.");
      Dashboard.hideLoadingMsg();
    });
  });

  byId("TestConnectionButton").addEventListener("click", function () {
    Dashboard.showLoadingMsg();
    ApiClient.ajax({
      type: "POST",
      url: ApiClient.getUrl("JellyRec/TestConnection"),
      data: JSON.stringify(readForm()),
      contentType: "application/json"
    }).then(function () {
      Dashboard.alert("Connected to Seerr successfully.");
    }).catch(function () {
      Dashboard.alert("Could not connect to Seerr. Check the URL and API key.");
    }).finally(Dashboard.hideLoadingMsg);
  });

  byId("TestTraktButton").addEventListener("click", function () {
    Dashboard.showLoadingMsg();
    ApiClient.ajax({
      type: "POST",
      url: ApiClient.getUrl("JellyRec/TestTrakt"),
      data: JSON.stringify(readForm()),
      contentType: "application/json"
    }).then(function () {
      Dashboard.alert("Connected to Trakt successfully.");
    }).catch(function () {
      Dashboard.alert("Could not connect to Trakt. Check the client ID and access token.");
    }).finally(Dashboard.hideLoadingMsg);
  });

  byId("RefreshButton").addEventListener("click", function () {
    Dashboard.showLoadingMsg();
    ApiClient.ajax({
      type: "POST",
      url: ApiClient.getUrl("JellyRec/Refresh")
    }).then(function () {
      Dashboard.alert("Recommendation refresh started.");
    }).catch(function () {
      Dashboard.alert("Recommendation refresh failed. Check Jellyfin logs.");
    }).finally(Dashboard.hideLoadingMsg);
  });

  byId("EnsureFolderButton").addEventListener("click", function () {
    Dashboard.showLoadingMsg();
    ApiClient.ajax({
      type: "POST",
      url: ApiClient.getUrl("JellyRec/EnsureRecommendationFolder"),
      data: JSON.stringify(readForm()),
      contentType: "application/json"
    }).then(function (result) {
      if (result && result.path) {
        byId("RecommendationLibraryPath").value = result.path;
        Dashboard.alert("Recommendation folder is ready: " + result.path);
      } else {
        Dashboard.alert("Recommendation folder is ready.");
      }
    }).catch(function () {
      Dashboard.alert("Could not create the recommendation folder. Check the path and Jellyfin permissions.");
    }).finally(Dashboard.hideLoadingMsg);
  });
})();

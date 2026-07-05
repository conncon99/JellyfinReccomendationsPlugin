(function () {
  const pluginId = "1d1af7ee-7c42-42a0-9f03-d202274e68d5";
  const defaults = {
    Enabled: false,
    SeerrUrl: "http://seerr:5055",
    ApiKey: "",
    RecommendationLibraryPath: "",
    SourceLibraryPaths: "",
    EnableTraktRecommendations: true,
    TraktClientId: "",
    TraktAccessToken: "",
    EnableSeerrRecommendations: true,
    TraktWeight: 3,
    SeerrWeight: 1,
    RecentlyWatchedLimit: 20,
    RecommendationsPerSeed: 8,
    MaxRecommendations: 100,
    RefreshIntervalHours: 24,
    RequestOnlyFirstSeason: true,
    RemoveAfterRequest: true
  };

  function byId(id) {
    return document.getElementById(id);
  }

  function readForm() {
    return {
      Enabled: byId("Enabled").checked,
      SeerrUrl: byId("SeerrUrl").value || defaults.SeerrUrl,
      ApiKey: byId("ApiKey").value,
      RecommendationLibraryPath: byId("RecommendationLibraryPath").value,
      SourceLibraryPaths: byId("SourceLibraryPaths").value,
      EnableTraktRecommendations: byId("EnableTraktRecommendations").checked,
      TraktClientId: byId("TraktClientId").value,
      TraktAccessToken: byId("TraktAccessToken").value,
      EnableSeerrRecommendations: byId("EnableSeerrRecommendations").checked,
      TraktWeight: Number(byId("TraktWeight").value || defaults.TraktWeight),
      SeerrWeight: Number(byId("SeerrWeight").value || defaults.SeerrWeight),
      RecentlyWatchedLimit: Number(byId("RecentlyWatchedLimit").value || defaults.RecentlyWatchedLimit),
      RecommendationsPerSeed: Number(byId("RecommendationsPerSeed").value || defaults.RecommendationsPerSeed),
      MaxRecommendations: Number(byId("MaxRecommendations").value || defaults.MaxRecommendations),
      RefreshIntervalHours: Number(byId("RefreshIntervalHours").value || defaults.RefreshIntervalHours),
      RequestOnlyFirstSeason: byId("RequestOnlyFirstSeason").checked,
      RemoveAfterRequest: byId("RemoveAfterRequest").checked
    };
  }

  function writeForm(config) {
    config = Object.assign({}, defaults, config || {});
    byId("Enabled").checked = !!config.Enabled;
    byId("SeerrUrl").value = config.SeerrUrl || defaults.SeerrUrl;
    byId("ApiKey").value = config.ApiKey || "";
    byId("RecommendationLibraryPath").value = config.RecommendationLibraryPath || "";
    byId("SourceLibraryPaths").value = config.SourceLibraryPaths || "";
    byId("EnableTraktRecommendations").checked = config.EnableTraktRecommendations !== false;
    byId("TraktClientId").value = config.TraktClientId || "";
    byId("TraktAccessToken").value = config.TraktAccessToken || "";
    byId("EnableSeerrRecommendations").checked = config.EnableSeerrRecommendations !== false;
    byId("TraktWeight").value = config.TraktWeight || defaults.TraktWeight;
    byId("SeerrWeight").value = config.SeerrWeight || defaults.SeerrWeight;
    byId("RecentlyWatchedLimit").value = config.RecentlyWatchedLimit || defaults.RecentlyWatchedLimit;
    byId("RecommendationsPerSeed").value = config.RecommendationsPerSeed || defaults.RecommendationsPerSeed;
    byId("MaxRecommendations").value = config.MaxRecommendations || defaults.MaxRecommendations;
    byId("RefreshIntervalHours").value = config.RefreshIntervalHours || defaults.RefreshIntervalHours;
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

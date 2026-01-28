using System;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Velopack;
using Velopack.Sources;

namespace EasyChat.Services;

/// <summary>
/// Service to check for application updates using Velopack.
/// </summary>
public class UpdateCheckService
{
    private readonly ILogger<UpdateCheckService> _logger;

    public UpdateCheckService(ILogger<UpdateCheckService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Checks for a newer version on GitHub.
    /// </summary>
    /// <returns>UpdateInfo if a new version is available, null otherwise.</returns>
    public async Task<UpdateInfo?> CheckForUpdateAsync()
    {
        try
        {
            var updateManager = new UpdateManager(new GithubSource("https://github.com/SwaggyMacro/EasyChat", null, false));

            if (!updateManager.IsInstalled)
            {
                _logger.LogInformation("Velopack not installed. Skipping update check.");
                return null;
            }

            var newVersion = await updateManager.CheckForUpdatesAsync();
            if (newVersion == null)
            {
                 _logger.LogInformation("No updates available.");
                 return null;
            }

             _logger.LogInformation("New version available: {Version}", newVersion.TargetFullRelease.Version);
             return newVersion;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking for updates.");
            return null;
        }
    }

    public async Task DownloadAndRestartAsync(UpdateInfo newVersion, Action<int>? progress = null)
    {
        var updateManager = new UpdateManager(new GithubSource("https://github.com/SwaggyMacro/EasyChat", null, false));
        await updateManager.DownloadUpdatesAsync(newVersion, progress);
        updateManager.ApplyUpdatesAndRestart(newVersion);
    }
}

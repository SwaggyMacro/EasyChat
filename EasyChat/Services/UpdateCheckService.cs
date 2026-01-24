using System;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace EasyChat.Services;

/// <summary>
/// Represents information about a GitHub release.
/// </summary>
public record ReleaseInfo(string TagName, string HtmlUrl, string Body);

/// <summary>
/// Service to check for application updates from GitHub releases.
/// </summary>
public class UpdateCheckService
{
    private const string GitHubApiUrl = "https://api.github.com/repos/SwaggyMacro/EasyChat/releases/latest";
    private readonly ILogger<UpdateCheckService> _logger;
    private readonly HttpClient _httpClient;

    public UpdateCheckService(ILogger<UpdateCheckService> logger)
    {
        _logger = logger;
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "EasyChat-UpdateChecker");
    }

    /// <summary>
    /// Checks for a newer version on GitHub.
    /// </summary>
    /// <returns>ReleaseInfo if a new version is available, null otherwise.</returns>
    public async Task<ReleaseInfo?> CheckForUpdateAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync(GitHubApiUrl);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to fetch release info. Status: {StatusCode}", response.StatusCode);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var tagName = root.GetProperty("tag_name").GetString();
            var htmlUrl = root.GetProperty("html_url").GetString();
            var body = root.TryGetProperty("body", out var bodyElement) ? bodyElement.GetString() ?? "" : "";

            if (string.IsNullOrEmpty(tagName) || string.IsNullOrEmpty(htmlUrl))
            {
                _logger.LogWarning("Invalid release data received from GitHub.");
                return null;
            }

            var currentVersion = GetCurrentVersion();
            var latestVersion = ParseVersion(tagName);

            _logger.LogInformation("Current version: {Current}, Latest version: {Latest}", currentVersion, latestVersion);

            if (latestVersion > currentVersion)
            {
                _logger.LogInformation("New version available: {TagName}", tagName);
                return new ReleaseInfo(tagName, htmlUrl, body);
            }

            _logger.LogInformation("Application is up to date.");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking for updates.");
            return null;
        }
    }

    /// <summary>
    /// Gets the current application version from the assembly.
    /// </summary>
    private static Version GetCurrentVersion()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var version = assembly.GetName().Version;
        return version ?? new Version(0, 0, 0);
    }

    /// <summary>
    /// Parses a version string like "v0.0.2" or "0.0.2" into a Version object.
    /// </summary>
    private static Version ParseVersion(string versionString)
    {
        // Remove 'v' prefix if present
        var cleaned = versionString.TrimStart('v', 'V');
        
        if (Version.TryParse(cleaned, out var version))
        {
            return version;
        }

        return new Version(0, 0, 0);
    }
}

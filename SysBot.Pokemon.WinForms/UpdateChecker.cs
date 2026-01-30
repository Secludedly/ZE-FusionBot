using Newtonsoft.Json;
using SysBot.Pokemon.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace SysBot.Pokemon.WinForms
{
    public class UpdateChecker
    {
        private const string RepositoryOwner = "Secludedly";
        private const string RepositoryName = "ZE-FusionBot";

        // Reuse HttpClient for better performance and socket management
        private static readonly HttpClient _httpClient = new()
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        static UpdateChecker()
        {
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "ZE-FusionBot");
        }

        public static async Task<(bool UpdateAvailable, bool UpdateRequired, string NewVersion)> CheckForUpdatesAsync(bool forceShow = false, bool showDialog = true)
        {
            ReleaseInfo? latestRelease = await FetchLatestReleaseAsync();

            bool updateAvailable = latestRelease != null && latestRelease.TagName != TradeBot.Version;
            bool updateRequired = latestRelease?.Prerelease == false && IsUpdateRequired(latestRelease?.Body);
            string? newVersion = latestRelease?.TagName;

            // Only show dialog if explicitly requested via forceShow (manual check)
            if (forceShow && showDialog)
            {
                var updateForm = new UpdateForm(updateRequired, newVersion ?? "", updateAvailable);
                updateForm.ShowDialog();
            }

            return (updateAvailable, updateRequired, newVersion ?? string.Empty);
        }

        public static async Task<string> FetchChangelogAsync()
        {
            ReleaseInfo? latestRelease = await FetchLatestReleaseAsync();
            return latestRelease?.Body ?? "Failed to fetch the latest release information.";
        }

        public static async Task<string?> FetchDownloadUrlAsync()
        {
            ReleaseInfo? latestRelease = await FetchLatestReleaseAsync();
            if (latestRelease?.Assets == null)
                return null;

            return latestRelease.Assets
            .FirstOrDefault(a => a.Name?.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) == true)
            ?.BrowserDownloadUrl;
        }

        private static async Task<ReleaseInfo?> FetchLatestReleaseAsync()
        {
            try
            {
                string releasesUrl = $"https://api.github.com/repos/{RepositoryOwner}/{RepositoryName}/releases/latest";
                HttpResponseMessage response = await _httpClient.GetAsync(releasesUrl);

                if (!response.IsSuccessStatusCode)
                {
                    string errorContent = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"GitHub API Error: {response.StatusCode} - {errorContent}");

                    // Provide more helpful error messages
                    if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                    {
                        Console.WriteLine("GitHub API rate limit may have been exceeded. Try again later.");
                    }
                    else if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    {
                        Console.WriteLine($"Repository {RepositoryOwner}/{RepositoryName} or latest release not found.");
                    }

                    return null;
                }

                string jsonContent = await response.Content.ReadAsStringAsync();
                var releaseInfo = JsonConvert.DeserializeObject<ReleaseInfo>(jsonContent);

                if (releaseInfo != null)
                {
                    Console.WriteLine($"Successfully fetched release info: Version {releaseInfo.TagName}");
                }

                return releaseInfo;
            }
            catch (HttpRequestException ex)
            {
                Console.WriteLine($"Network error fetching release info: {ex.Message}");
                Console.WriteLine("Please check your internet connection and try again.");
                return null;
            }
            catch (TaskCanceledException ex)
            {
                Console.WriteLine($"Request timeout fetching release info: {ex.Message}");
                Console.WriteLine("The request took too long. Please try again.");
                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Unexpected error fetching release info: {ex.Message}");
                return null;
            }
        }

        private static bool IsUpdateRequired(string? changelogBody)
        {
            return !string.IsNullOrWhiteSpace(changelogBody) &&
                   changelogBody.Contains("Required = Yes", StringComparison.OrdinalIgnoreCase);
        }

        private class ReleaseInfo
        {
            [JsonProperty("tag_name")]
            public string? TagName { get; set; }

            [JsonProperty("prerelease")]
            public bool Prerelease { get; set; }

            [JsonProperty("assets")]
            public List<AssetInfo>? Assets { get; set; }

            [JsonProperty("body")]
            public string? Body { get; set; }
        }

        private class AssetInfo
        {
            [JsonProperty("name")]
            public string? Name { get; set; }

            [JsonProperty("browser_download_url")]
            public string? BrowserDownloadUrl { get; set; }
        }
    }
}

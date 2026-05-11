using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace Aerochat.Helpers
{
    public static class UpdateChecker
    {
        private const string ApiUrl = "https://api.github.com/repos/Maxokie/Updated-Aerochat/releases/latest";
        public const string ReleasesUrl = "https://github.com/Maxokie/Updated-Aerochat/releases/latest";

        public record UpdateInfo(string TagName, string ReleaseUrl);

        /// <summary>
        /// Checks GitHub for a newer release. Returns update info if one is available, or null if up to date or the check failed.
        /// </summary>
        public static async Task<UpdateInfo?> CheckAsync()
        {
            try
            {
                using var http = new HttpClient();
                http.DefaultRequestHeaders.Add("User-Agent", "Aerochat/" + AssemblyInfo.Version);
                http.Timeout = TimeSpan.FromSeconds(10);

                string json = await http.GetStringAsync(ApiUrl).ConfigureAwait(false);
                using JsonDocument doc = JsonDocument.Parse(json);
                JsonElement root = doc.RootElement;

                if (!root.TryGetProperty("tag_name", out JsonElement tagNameEl)) return null;
                string tagName = tagNameEl.GetString() ?? "";

                string releaseUrl = root.TryGetProperty("html_url", out JsonElement urlEl)
                    ? (urlEl.GetString() ?? ReleasesUrl)
                    : ReleasesUrl;

                string remoteVersionStr = tagName.TrimStart('v', 'V');
                string localVersionStr = AssemblyInfo.Version;

                if (!Version.TryParse(remoteVersionStr, out Version? remoteVersion) ||
                    !Version.TryParse(localVersionStr, out Version? localVersion))
                    return null;

                return remoteVersion > localVersion ? new UpdateInfo(tagName.TrimStart('v', 'V'), releaseUrl) : null;
            }
            catch
            {
                return null;
            }
        }
    }
}

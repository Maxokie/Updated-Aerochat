// This file is part of the DSharpPlus project.
//
// Copyright (c) 2015 Mike Santiago
// Copyright (c) 2016-2023 DSharpPlus Contributors
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

using System;
using System.Globalization;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace DSharpPlus.Net.Abstractions
{
    /// <summary>
    /// Represents data for identify payload's client properties.
    /// </summary>
    internal sealed class ClientProperties
    {
        private static volatile int _cachedBuildNumber = 0;

        /// <summary>
        /// Fetches the current Discord client build number from the web app and caches it.
        /// Falls back to the last known-good hardcoded value if the fetch fails.
        /// </summary>
        internal static async Task FetchBuildNumberAsync()
        {
            if (_cachedBuildNumber > 0)
                return;

            try
            {
                using var http = new HttpClient();
                http.DefaultRequestHeaders.Add("User-Agent",
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/133.0.0.0 Safari/537.36");
                http.Timeout = TimeSpan.FromSeconds(15);

                var loginHtml = await http.GetStringAsync("https://discord.com/login").ConfigureAwait(false);
                var scriptMatches = Regex.Matches(loginHtml, @"<script src=""(/assets/[^""]+\.js)"" defer></script>");

                for (int i = scriptMatches.Count - 1; i >= 0; i--)
                {
                    var scriptUrl = "https://discord.com" + scriptMatches[i].Groups[1].Value;
                    var scriptContent = await http.GetStringAsync(scriptUrl).ConfigureAwait(false);
                    var match = Regex.Match(scriptContent, @"buildNumber[""']?:\s*[""']?(\d{5,6})[""']?");
                    if (match.Success && int.TryParse(match.Groups[1].Value, out var buildNum) && buildNum > 100000)
                    {
                        _cachedBuildNumber = buildNum;
                        return;
                    }
                }
            }
            catch { }
        }

        [JsonProperty("os")]
        public string OperatingSystem
        {
            get
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    return "windows";
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                    return "linux";
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                    return "osx";

                var plat = RuntimeInformation.OSDescription.ToLowerInvariant();
                if (plat.Contains("freebsd"))
                    return "freebsd";
                else if (plat.Contains("openbsd"))
                    return "openbsd";
                else if (plat.Contains("netbsd"))
                    return "netbsd";
                else if (plat.Contains("dragonfly"))
                    return "dragonflybsd";
                else if (plat.Contains("miros bsd") || plat.Contains("mirbsd"))
                    return "miros bsd";
                else if (plat.Contains("desktopbsd"))
                    return "desktopbsd";
                else if (plat.Contains("darwin"))
                    return "osx";
                else return plat.Contains("unix") ? "unix" : "toaster (unknown)";
            }
        }

        [JsonProperty("browser")]
        public string Browser
            => "Chrome";
        [JsonProperty("device")]
        public string Device
            => "";
        [JsonProperty("system_locale")]
        public string SystemLocale
            => CultureInfo.CurrentCulture.Name;

        [JsonProperty("browser_user_agent")]
        public string BrowserUserAgent
            => Utilities.GetUserAgent();

        [JsonProperty("browser_version")]
        public string BrowserVersion
            => $"{70 + ((DateTime.Now.Year - 2020) * 12) + DateTime.Now.Month}.0.0.0";

        [JsonProperty("os_version")]
        public string OSVersion
            => Environment.OSVersion.Version.Major.ToString();

        [JsonProperty("referrer")]
        public string Referrer
            => "";

        [JsonProperty("referring_domain")]
        public string ReferringDomain
            => "";

        [JsonProperty("referrer_current")]
        public string ReferrerCurrent
            => "";

        [JsonProperty("referring_domain_current")]
        public string ReferringDomainCurrent
            => "";

        [JsonProperty("release_channel")]
        public string ReleaseChannel
            => "stable";

        [JsonProperty("client_build_number")]
        public int ClientBuildNumber
            => _cachedBuildNumber > 0 ? _cachedBuildNumber : 325421;

        [JsonProperty("client_event_source")]
        public object ClientEventSource
            => null;
    }
}

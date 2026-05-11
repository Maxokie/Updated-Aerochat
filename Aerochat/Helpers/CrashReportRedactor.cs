using System.Text.RegularExpressions;

namespace Aerochat.Helpers
{
    /// <summary>
    /// Removes likely secrets from exception text before showing or forwarding crash reports.
    /// </summary>
    internal static class CrashReportRedactor
    {
        private static readonly Regex AuthorizationHeaderLike = new(
            @"(?i)\b(authorization|proxy-authorization)\s*:\s*[^\r\n]+",
            RegexOptions.Compiled);

        private static readonly Regex AccessTokenQuery = new(
            @"(?i)([?&])(access_token|refresh_token|token)=[^&\s]+",
            RegexOptions.Compiled);

        /// <summary>
        /// Discord user tokens are typically long and contain dots, or start with <c>mfa.</c>.
        /// </summary>
        private static readonly Regex DiscordTokenLike = new(
            @"\b(mfa\.[A-Za-z0-9._-]{16,}|[A-Za-z0-9+/=_-]{20,}\.[A-Za-z0-9+/=_-]{4,}\.[A-Za-z0-9+/=_-]{16,})\b",
            RegexOptions.Compiled);

        public static string Sanitize(string? text)
        {
            if (string.IsNullOrEmpty(text))
                return text ?? string.Empty;

            string s = text;
            s = AuthorizationHeaderLike.Replace(s, "[redacted: authorization header]");
            s = AccessTokenQuery.Replace(s, m => m.Groups[1].Value + m.Groups[2].Value + "=[redacted]");
            s = DiscordTokenLike.Replace(s, "[redacted:token]");
            return s;
        }
    }
}

using System;

namespace Aerochat.Helpers
{
    /// <summary>
    /// Max wait for <see cref="DSharpPlus.DiscordClient.ConnectAsync"/> during token sign-in.
    /// Legacy Windows often needs far longer than modern OS builds for REST, TLS, and gateway.
    /// </summary>
    internal static class LoginConnectTimeout
    {
        public static int Seconds
        {
            get
            {
                var v = Environment.OSVersion.Version;
                if (v.Major == 6 && v.Minor <= 1)
                    return 120;
                return 30;
            }
        }
    }
}

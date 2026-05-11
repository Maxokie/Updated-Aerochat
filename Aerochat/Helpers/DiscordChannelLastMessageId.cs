using System.Reflection;
using DSharpPlus.Entities;

namespace Aerochat.Helpers
{
    /// <summary>
    /// Cached reflection for <see cref="DiscordChannel.LastMessageId"/> (internal setter); avoids per-message GetProperty lookups.
    /// </summary>
    internal static class DiscordChannelLastMessageId
    {
        private static readonly PropertyInfo? LastMessageIdProperty =
            typeof(DiscordChannel).GetProperty(nameof(DiscordChannel.LastMessageId), BindingFlags.Public | BindingFlags.Instance);

        public static void TrySet(DiscordChannel? channel, ulong messageId)
        {
            if (channel is null || LastMessageIdProperty is null) return;
            try
            {
                LastMessageIdProperty.SetValue(channel, messageId);
            }
            catch
            {
                // Parity with previous silent reflection failures.
            }
        }
    }
}

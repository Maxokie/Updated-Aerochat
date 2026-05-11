using DSharpPlus;
using DSharpPlus.Enums;

namespace Aerochat.Helpers
{
    public static class DiscordRelationshipHelper
    {
        public static bool IsCurrentUser(DiscordClient? client, ulong userId)
        {
            if (client?.CurrentUser is not { } me || userId == 0)
                return false;
            return userId == me.Id;
        }

        /// <summary>True when an Add-contact invite (parsed username + optional #disc) refers to the logged-in account.</summary>
        public static bool ParsedInviteTargetsCurrentUser(DiscordClient? client, string username, string? discriminator)
        {
            var me = client?.CurrentUser;
            if (me is null || string.IsNullOrWhiteSpace(username))
                return false;
            if (!string.Equals(username.Trim(), me.Username, StringComparison.OrdinalIgnoreCase))
                return false;
            return NormalizeDiscriminator(discriminator) == NormalizeDiscriminator(me.Discriminator);
        }

        private static string? NormalizeDiscriminator(string? d)
        {
            if (string.IsNullOrWhiteSpace(d)) return null;
            if (d == "0") return null;
            return d;
        }

        public static bool IsUserBlocked(DiscordClient? client, ulong userId)
        {
            if (client is null || userId == 0)
                return false;

            foreach (var r in client.Relationships.Values)
            {
                if (r.UserId == userId && r.RelationshipType == DiscordRelationshipType.Blocked)
                    return true;
            }

            return false;
        }
    }
}

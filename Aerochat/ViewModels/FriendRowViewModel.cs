using Aerochat.Controls;
using Aerochat.Enums;
using DSharpPlus.Entities;
using System;

namespace Aerochat.ViewModels
{
    /// <summary>One row in the friends list (Add contact window). Always uses &quot;small picture&quot; layout; main home list uses Options → Disposition instead.</summary>
    public class FriendRowViewModel : ViewModelBase
    {
        public ulong UserId { get; }

        public string DisplayName { get; }

        /// <summary>Display name (same binding as main contact list).</summary>
        public string Name => DisplayName;

        /// <summary>Avatar URL for compact status icon fallback (mirrors main list <c>Image</c>).</summary>
        public string Image => AvatarUrl;

        public bool IsGroupChat => false;

        /// <summary>Discord username (for search).</summary>
        public string Username { get; }

        public PresenceViewModel Presence { get; }

        public string AvatarUrl { get; }

        /// <summary>Custom activity or status text for subtitle.</summary>
        public string StatusLine =>
            string.IsNullOrWhiteSpace(Presence.Presence) ? Presence.Status : Presence.Presence!;

        public ContactListIconSize EffectiveIconSize => ContactListIconSize.Small;

        public double ContactRowHeight => ContactListRowLayout.RowHeight(ContactListIconSize.Small);

        public bool ContactRowUsesAvatarLayout => true;

        public ProfileFrameSize ContactAvatarFrameSize => ProfileFrameSize.Small;

        public FriendRowViewModel(DiscordUser user, PresenceViewModel presence)
        {
            UserId = user.Id;
            DisplayName = user.DisplayName ?? user.Username ?? "?";
            Username = user.Username ?? "";
            Presence = presence;
            AvatarUrl = user.AvatarUrl ?? user.DefaultAvatarUrl ?? "";
        }
    }
}

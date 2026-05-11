using DSharpPlus;
using DSharpPlus.Entities;
using System;
using System.Linq;

namespace Aerochat.ViewModels
{
    public class ChannelViewModel : ViewModelBase
    {
        private string _name;
        private string _topic;
        private ulong _id;
        private bool _canTalk;
        private bool _canManageMessages;
        private bool _canAddReactions;
        private bool _canAttachFiles;
        private GuildViewModel? _guild;

        public required string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        public required string Topic
        {
            get => _topic;
            set => SetProperty(ref _topic, value);
        }

        public required ulong Id
        {
            get => _id;
            set => SetProperty(ref _id, value);
        }

        public bool CanTalk
        {
            get => _canTalk;
            set => SetProperty(ref _canTalk, value);
        }

        public bool CanManageMessages
        {
            get => _canManageMessages;
            set => SetProperty(ref _canManageMessages, value);
        }

        public bool CanAddReactions
        {
            get => _canAddReactions;
            set => SetProperty(ref _canAddReactions, value);
        }

        public bool CanAttachFiles
        {
            get => _canAttachFiles;
            set => SetProperty(ref _canAttachFiles, value);
        }

        public GuildViewModel? Guild
        {
            get => _guild;
            set => SetProperty(ref _guild, value);
        }

        public static ChannelViewModel FromChannel(DiscordChannel channel)
        {
            if (channel is null)
                throw new ArgumentNullException(nameof(channel));

            return new ChannelViewModel
            {
                Name = channel is DiscordDmChannel dm
                    ? (channel.Name ?? string.Join(", ", (dm.Recipients ?? Array.Empty<DiscordUser>()).Select(x => x.DisplayName)))
                    : channel.Name,
                Topic = channel.Topic ?? "",
                Id = channel.Id,
                CanTalk = channel is not DiscordDmChannel dmChannel ? ((channel.PermissionsFor(channel.Guild.CurrentMember) & Permissions.SendMessages) == Permissions.SendMessages) : true,
                CanManageMessages = channel is not DiscordDmChannel ? ((channel.PermissionsFor(channel.Guild.CurrentMember) & Permissions.ManageMessages) == Permissions.ManageMessages) : false,
                CanAddReactions = channel is not DiscordDmChannel ? ((channel.PermissionsFor(channel.Guild.CurrentMember) & Permissions.AddReactions) == Permissions.AddReactions) : true,
                CanAttachFiles = channel is not DiscordDmChannel ? ((channel.PermissionsFor(channel.Guild.CurrentMember) & Permissions.AttachFiles) == Permissions.AttachFiles) : true,
                Guild = channel.Guild != null ? GuildViewModel.FromGuild(channel.Guild) : null
            };
        }
    }
}

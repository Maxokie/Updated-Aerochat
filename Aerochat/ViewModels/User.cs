using DSharpPlus.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;
using Aerochat.Theme;
using Aerochat.Hoarder;
using Aerochat.Helpers;

namespace Aerochat.ViewModels
{
    public class UserViewModel : ViewModelBase
    {
        private string _name;
        private string _avatar;
        private ulong _id;
        private string _username;
        private PresenceViewModel? _presence;
        private SceneViewModel? _scene;
        private string? _color = "#525252";
        private string? _image;
        private string? _bio;
        private bool _isSpeaking;

        public bool IsSpeaking
        {
            get => _isSpeaking;
            set => SetProperty(ref _isSpeaking, value);
        }

        private bool _isBlocked;

        /// <summary>True when this user has a blocked relationship with the current account.</summary>
        public bool IsBlocked
        {
            get => _isBlocked;
            set => SetProperty(ref _isBlocked, value);
        }

        public required string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }
        public required string Avatar
        {
            get => _avatar;
            set => SetProperty(ref _avatar, value);
        }
        public required ulong Id
        {
            get => _id;
            set => SetProperty(ref _id, value);
        }
        public required string Username
        {
            get => _username;
            set => SetProperty(ref _username, value);
        }

        public PresenceViewModel? Presence
        {
            get => _presence;
            set => SetProperty(ref _presence, value);
        }

        public SceneViewModel? Scene
        {
            get => _scene;
            set => SetProperty(ref _scene, value);
        }

        public string? Color
        {
            get => _color;
            set => SetProperty(ref _color, value);
        }

        public string? Image
        {
            get => _image;
            set => SetProperty(ref _image, value);
        }

        /// <summary>Discord profile bio ("About me"); filled when the profile API is fetched.</summary>
        public string? Bio
        {
            get => _bio;
            set => SetProperty(ref _bio, value);
        }

        public static UserViewModel FromUser(DiscordUser user)
        {
            return new UserViewModel
            {
                //Name = user.Username,
                Name = user.DisplayName,
                Avatar = user.AvatarUrl,
                Id = user.Id,
                Username = user.Username,
                Presence = user.Presence == null ? null : PresenceViewModel.FromPresence(user.Presence),
                Scene = SceneViewModel.FromUser(user),
                IsBlocked = DiscordRelationshipHelper.IsUserBlocked(Discord.Client, user.Id)
            };
        }

        public static UserViewModel FromMember(DiscordMember member)
        {
            // find the topmost role where Icon is not null
            var role = member.Roles.OrderByDescending(x => x.Position).FirstOrDefault(x => x.IconUrl != null);
            return new UserViewModel
            {
                Name = string.IsNullOrEmpty(member.Nickname) ? member.DisplayName : member.Nickname,
                Avatar = member.AvatarUrl,
                Id = member.Id,
                Username = member.Username,
                Presence = member.Presence == null ? null : PresenceViewModel.FromPresence(member.Presence),
                // convert member.Color to hex string
                Color = member.Color.ToString() == "#000000" ? "#525252" : member.Color.ToString(),
                Image = role?.IconUrl,
                IsBlocked = DiscordRelationshipHelper.IsUserBlocked(Discord.Client, member.Id)
            };
        }
    }
}

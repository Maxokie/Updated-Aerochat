using Aerochat.Windows;
using Aerochat.Controls;
using Aerochat.Enums;
using Aerochat.Localization;
using Aerochat.Settings;
using DSharpPlus.Entities;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aerochat.ViewModels
{
    public class HomeListViewCategory : ViewModelBase
    {
        private string _name;
        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        /// <summary>Stable keys: <c>Favorites</c>, <c>Conversations</c>, <c>Servers</c>; folder names use Discord names.</summary>
        public static string LocalizedCategoryName(string name) =>
            name switch
            {
                "Favorites" => LocalizationManager.Instance["HomeCategoryFavorites"],
                "Conversations" => LocalizationManager.Instance["HomeCategoryConversations"],
                "Servers" => LocalizationManager.Instance["HomeCategoryServers"],
                _ => name
            };

        /// <summary>Localized title for the category header (internal <see cref="Name"/> is unchanged for logic).</summary>
        [DependsOn(nameof(Name))]
        public string LocalizedTitle => LocalizedCategoryName(Name);

        /// <summary>Full label for the category row (e.g. tooltips / copies).</summary>
        private string _categoryHeaderText = "";
        public string CategoryHeaderText
        {
            get => string.IsNullOrEmpty(_categoryHeaderText) ? Name : _categoryHeaderText;
            set => SetProperty(ref _categoryHeaderText, value);
        }

        /// <summary>Parentheses segment only, e.g. <c> (1/3)</c> or <c> (12)</c>; empty for folder headers.</summary>
        private string _categoryHeaderCountSuffix = "";
        public string CategoryHeaderCountSuffix
        {
            get => _categoryHeaderCountSuffix;
            set => SetProperty(ref _categoryHeaderCountSuffix, value);
        }

        private bool _isVisibleProperty = false;
        public bool IsVisibleProperty
        {
            get => _isVisibleProperty;
            set => SetProperty(ref _isVisibleProperty, value);
        }

        private bool _collapsed = false;
        public bool Collapsed
        {
            get => _collapsed;
            set => SetProperty(ref _collapsed, value);
        }

        private bool _isSelected = false;
        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }

        public ObservableCollection<HomeListItemViewModel> Items { get; } = new();

        /// <summary>Updates count suffix and full header text for one category (main list and search-filtered clones).</summary>
        public static void ApplyHeaderCounts(HomeListViewCategory cat, int guildTotal, Func<HomeListItemViewModel, bool> rowIsActive)
        {
            string title = LocalizedCategoryName(cat.Name);
            if (cat.Name == "Favorites" || cat.Name == "Conversations")
            {
                int total = cat.Items.Count;
                int online = cat.Items.Count(rowIsActive);
                cat.CategoryHeaderCountSuffix = $" ({online}/{total})";
                cat.CategoryHeaderText = $"{title}{cat.CategoryHeaderCountSuffix}";
            }
            else if (cat.Name == "Servers")
            {
                cat.CategoryHeaderCountSuffix = $" ({guildTotal})";
                cat.CategoryHeaderText = $"{title}{cat.CategoryHeaderCountSuffix}";
            }
            else
            {
                cat.CategoryHeaderCountSuffix = "";
                cat.CategoryHeaderText = title;
            }
        }

        public class HomeListItemViewModel : ViewModelBase
        {
            private string _name;
            private string _image;
            private PresenceViewModel _presence;
            private Action _doubleClick;
            private bool _isSelected;
            private ulong _lastMsgId;
            private ulong _id;
            private bool _isGroupChat;
            private int _recipientCount;
            private string _avatarUrl = "";
            private bool _isGuildServerRow;
            private bool _guildHasUnread;

            public string Name
            {
                get => _name;
                set => SetProperty(ref _name, value);
            }

            public string Image
            {
                get => _image;
                set => SetProperty(ref _image, value);
            }

            public PresenceViewModel Presence
            {
                get => _presence;
                set
                {
                    if (_presence != null)
                        _presence.PropertyChanged -= Presence_PropertyChanged;
                    if (!SetProperty(ref _presence, value))
                    {
                        if (_presence != null)
                            _presence.PropertyChanged += Presence_PropertyChanged;
                        return;
                    }
                    if (_presence != null)
                        _presence.PropertyChanged += Presence_PropertyChanged;
                    OnPropertyChanged(nameof(ContactRowShowStatusDash));
                }
            }

            private void Presence_PropertyChanged(object? sender, PropertyChangedEventArgs e)
            {
                if (e.PropertyName is null or nameof(PresenceViewModel.Presence))
                    OnPropertyChanged(nameof(ContactRowShowStatusDash));
            }

            public Action DoubleClick
            {
                get => _doubleClick;
                set => SetProperty(ref _doubleClick, value);
            }

            public bool IsSelected
            {
                get => _isSelected;
                set => SetProperty(ref _isSelected, value);
            }

            public ulong LastMsgId
            {
                get => _lastMsgId;
                set => SetProperty(ref _lastMsgId, value);
            }

            public ulong Id
            {
                get => _id;
                set => SetProperty(ref _id, value);
            }

            public bool IsGroupChat
            {
                get => _isGroupChat;
                set => SetProperty(ref _isGroupChat, value);
            }

            public int RecipientCount
            {
                get => _recipientCount;
                set => SetProperty(ref _recipientCount, value);
            }

            public string AvatarUrl
            {
                get => _avatarUrl;
                set => SetProperty(ref _avatarUrl, value);
            }

            /// <summary>True for guild/server rows (home list uses this for compact-mode icons).</summary>
            public bool IsGuildServerRow
            {
                get => _isGuildServerRow;
                set => SetProperty(ref _isGuildServerRow, value);
            }

            /// <summary>Discord guild id for <see cref="IsGuildServerRow"/> rows; 0 for DMs or legacy rows.</summary>
            private ulong _guildId;
            public ulong GuildId
            {
                get => _guildId;
                set => SetProperty(ref _guildId, value);
            }

            /// <summary>Guild has unread messages in some text/announcement channel (compact list unread dot).</summary>
            public bool GuildHasUnread
            {
                get => _guildHasUnread;
                set => SetProperty(ref _guildHasUnread, value);
            }

            /// <summary>Discord login name for friend requests (e.g. group member list).</summary>
            private string _discordUsername = "";
            public string DiscordUsername
            {
                get => _discordUsername;
                set => SetProperty(ref _discordUsername, value);
            }

            /// <summary>Legacy discriminator; null/empty to omit (new username system).</summary>
            private string? _discordDiscriminator;
            public string? DiscordDiscriminator
            {
                get => _discordDiscriminator;
                set => SetProperty(ref _discordDiscriminator, value);
            }

            private bool _isBlocked;

            /// <summary>True for 1:1 DMs when the other user is blocked (conversation row dimmed).</summary>
            public bool IsBlocked
            {
                get => _isBlocked;
                set => SetProperty(ref _isBlocked, value);
            }

            public ObservableCollection<DiscordUser> Recipients { get; } = new();

            public ObservableCollection<UserViewModel> ConnectedUsers { get; } = new();

            private ContactListSectionKind _listSection = ContactListSectionKind.Conversations;

            /// <summary>Which home section this row is shown under (favorites vs others use different icon size settings).</summary>
            public ContactListSectionKind ListSection
            {
                get => _listSection;
                set
                {
                    if (SetProperty(ref _listSection, value))
                        InvalidateContactLayout();
                }
            }

            public ContactListIconSize EffectiveIconSize =>
                ListSection == ContactListSectionKind.Favorites
                    ? SettingsManager.Instance.ContactListIconSizeFavorites
                    : SettingsManager.Instance.ContactListIconSizeConversationsServers;

            public double ContactRowHeight => ContactListRowLayout.RowHeight(EffectiveIconSize);

            public bool ContactRowUsesAvatarLayout => EffectiveIconSize != ContactListIconSize.Tiny;

            /// <summary>Tiny (status-only) layout: show a separator between the name and the activity line.</summary>
            [DependsOn(nameof(IsGroupChat))]
            public bool ContactRowShowStatusDash =>
                !ContactRowUsesAvatarLayout &&
                (!string.IsNullOrWhiteSpace(Presence?.Presence) || IsGroupChat);

            public ProfileFrameSize ContactAvatarFrameSize => EffectiveIconSize switch
            {
                ContactListIconSize.Tiny => ProfileFrameSize.ExtraSmall,
                ContactListIconSize.Small => ProfileFrameSize.Small,
                ContactListIconSize.Medium => ProfileFrameSize.Medium,
                ContactListIconSize.Large => ProfileFrameSize.Large,
                _ => ProfileFrameSize.ExtraSmall
            };

            public void InvalidateContactLayout()
            {
                OnPropertyChanged(nameof(EffectiveIconSize));
                OnPropertyChanged(nameof(ContactRowHeight));
                OnPropertyChanged(nameof(ContactRowUsesAvatarLayout));
                OnPropertyChanged(nameof(ContactAvatarFrameSize));
                OnPropertyChanged(nameof(ContactRowShowStatusDash));
            }
        }
    }
}
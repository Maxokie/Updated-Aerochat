using Aerochat.Hoarder;
using Aerochat.Theme;
using DSharpPlus.Entities;
using DSharpPlus.Enums;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace Aerochat.ViewModels
{
    public class FriendsWindowViewModel : ViewModelBase
    {
        private string _searchText = "";
        private readonly List<FriendRowViewModel> _master = new();

        private bool _onlineSectionCollapsed;
        private bool _offlineSectionCollapsed;
        private string _onlineCategoryHeaderCountSuffix = " (0/0)";
        private string _offlineCategoryHeaderCountSuffix = " (0/0)";

        public SceneViewModel Scene => ThemeService.Instance.Scene;

        public ObservableCollection<FriendRowViewModel> OnlineFriends { get; } = new();
        public ObservableCollection<FriendRowViewModel> OfflineFriends { get; } = new();

        /// <summary>When true, the online friends list is hidden (Home tree expander semantics).</summary>
        public bool OnlineSectionCollapsed
        {
            get => _onlineSectionCollapsed;
            set => SetProperty(ref _onlineSectionCollapsed, value);
        }

        /// <summary>When true, the offline friends list is hidden.</summary>
        public bool OfflineSectionCollapsed
        {
            get => _offlineSectionCollapsed;
            set => SetProperty(ref _offlineSectionCollapsed, value);
        }

        /// <summary>Parentheses segment for the online header, e.g. <c> (4/5)</c>.</summary>
        public string OnlineCategoryHeaderCountSuffix
        {
            get => _onlineCategoryHeaderCountSuffix;
            private set => SetProperty(ref _onlineCategoryHeaderCountSuffix, value);
        }

        /// <summary>Parentheses segment for the offline header, e.g. <c> (10/12)</c>.</summary>
        public string OfflineCategoryHeaderCountSuffix
        {
            get => _offlineCategoryHeaderCountSuffix;
            private set => SetProperty(ref _offlineCategoryHeaderCountSuffix, value);
        }

        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value))
                    ApplyFilterAndSplit();
            }
        }

        public async Task ReloadAsync()
        {
            _master.Clear();
            var client = Discord.Client;
            if (client is null) return;

            foreach (var rel in client.Relationships.Values)
            {
                if (rel.RelationshipType != DiscordRelationshipType.Friend)
                    continue;

                DiscordUser? user = rel.User;
                if (user is null)
                {
                    try
                    {
                        var profile = await client.GetUserProfileAsync(rel.UserId, true).ConfigureAwait(true);
                        user = profile.User;
                    }
                    catch
                    {
                        continue;
                    }
                }

                if (user is null) continue;

                client.Presences.TryGetValue(user.Id, out DiscordPresence? presenceEntity);
                var presenceVm = presenceEntity is null
                    ? new PresenceViewModel { Presence = "", Status = "Offline", Type = "" }
                    : PresenceViewModel.FromPresence(presenceEntity);

                _master.Add(new FriendRowViewModel(user, presenceVm));
            }

            _master.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase));
            ApplyFilterAndSplit();
        }

        private static bool IsOnline(PresenceViewModel p)
        {
            return p.Status is not "Offline" and not "Invisible" and not "";
        }

        /// <summary>Matches Home contact list header numerator (Online / Idle / DND).</summary>
        private static bool IsActivePresenceStatus(string? status) =>
            status is "Online" or "Idle" or "DoNotDisturb";

        private void ApplyFilterAndSplit()
        {
            var q = SearchText.Trim();
            IEnumerable<FriendRowViewModel> src = _master;
            if (!string.IsNullOrEmpty(q))
            {
                string l = q.ToLowerInvariant();
                src = _master.Where(f =>
                    (f.DisplayName?.ToLowerInvariant().Contains(l) ?? false) ||
                    (f.Username?.ToLowerInvariant().Contains(l) ?? false));
            }

            var list = src.ToList();
            var online = list.Where(f => IsOnline(f.Presence)).ToList();
            var offline = list.Where(f => !IsOnline(f.Presence)).ToList();

            OnlineFriends.Clear();
            foreach (var x in online) OnlineFriends.Add(x);
            OfflineFriends.Clear();
            foreach (var x in offline) OfflineFriends.Add(x);

            UpdateSectionHeaderCounts(online, offline);
        }

        private void UpdateSectionHeaderCounts(
            List<FriendRowViewModel> online,
            List<FriendRowViewModel> offline)
        {
            int onlineTotal = online.Count;
            int onlineActive = online.Count(f => IsActivePresenceStatus(f.Presence?.Status));
            OnlineCategoryHeaderCountSuffix = onlineTotal == 0
                ? " (0/0)"
                : $" ({onlineActive}/{onlineTotal})";

            int offlineTotal = offline.Count;
            int offlineStrict = offline.Count(f => f.Presence?.Status == "Offline");
            OfflineCategoryHeaderCountSuffix = offlineTotal == 0
                ? " (0/0)"
                : $" ({offlineStrict}/{offlineTotal})";
        }
    }
}

using Aerochat.Helpers;
using Aerochat.Hoarder;
using Aerochat.Localization;
using Aerochat.ViewModels;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.Enums;
using DSharpPlus.EventArgs;
using DSharpPlus.Exceptions;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using System.Drawing;
using System.Windows.Input;
using static Aerochat.ViewModels.HomeListViewCategory;

namespace Aerochat.Windows
{
    public partial class FriendsWindow : Window
    {
        private readonly FriendsWindowViewModel _vm;
        private DiscordClient? _client;
        /// <summary>When true, <see cref="FriendsWindow_Closed"/> restores owner state but does not steal focus from a chat opened from this window.</summary>
        private bool _closingAfterOpeningDm;

        public FriendsWindow()
        {
            InitializeComponent();
            _vm = new FriendsWindowViewModel();
            DataContext = _vm;
            Loaded += FriendsWindow_Loaded;
            Closing += FriendsWindow_Closing;
            Closed += FriendsWindow_Closed;
        }

        private async void FriendsWindow_Loaded(object sender, RoutedEventArgs e)
        {
            _client = Discord.Client;
            if (_client is not null)
            {
                _client.PresenceUpdated += OnPresenceUpdated;
                _client.RelationshipAdded += OnRelationshipAdded;
                _client.RelationshipRemoved += OnRelationshipRemoved;
            }

            try
            {
                await _vm.ReloadAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                DiagnosticsLog.Swallowed("FriendsWindow.ReloadAsync", ex);
            }
        }

        private void FriendsOnlineSectionToggleCollapse(object sender, MouseButtonEventArgs e)
        {
            _vm.OnlineSectionCollapsed = !_vm.OnlineSectionCollapsed;
            e.Handled = true;
        }

        private void FriendsOfflineSectionToggleCollapse(object sender, MouseButtonEventArgs e)
        {
            _vm.OfflineSectionCollapsed = !_vm.OfflineSectionCollapsed;
            e.Handled = true;
        }

        private void FriendsWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_client is not null)
            {
                _client.PresenceUpdated -= OnPresenceUpdated;
                _client.RelationshipAdded -= OnRelationshipAdded;
                _client.RelationshipRemoved -= OnRelationshipRemoved;
            }
        }

        private void FriendsWindow_Closed(object? sender, EventArgs e)
        {
            if (Owner is not Window owner)
                return;

            owner.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (owner.WindowState == WindowState.Minimized)
                    owner.WindowState = WindowState.Normal;

                if (_closingAfterOpeningDm)
                {
                    _closingAfterOpeningDm = false;
                    return;
                }

                owner.Activate();
            }), DispatcherPriority.Background);
        }

        private Task OnPresenceUpdated(DiscordClient client, PresenceUpdateEventArgs e)
        {
            if (!client.Relationships.TryGetValue(e.User.Id, out var rel) || rel.RelationshipType != DiscordRelationshipType.Friend)
                return Task.CompletedTask;

            _ = Dispatcher.InvokeAsync(async () => await _vm.ReloadAsync());
            return Task.CompletedTask;
        }

        private Task OnRelationshipAdded(DiscordClient client, RelationshipAddEventArgs e)
        {
            _ = Dispatcher.InvokeAsync(async () => await _vm.ReloadAsync());
            return Task.CompletedTask;
        }

        private Task OnRelationshipRemoved(DiscordClient client, RelationshipRemoveEventArgs e)
        {
            _ = Dispatcher.InvokeAsync(async () => await _vm.ReloadAsync());
            return Task.CompletedTask;
        }

        private async void FriendRow_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.DataContext is not FriendRowViewModel row)
                return;

            var client = Discord.Client;
            if (client is null) return;

            DiscordDmChannel? dm = null;
            foreach (var c in client.PrivateChannels.Values)
            {
                if (c?.Recipients?.Count != 1) continue;
                if (c.Recipients[0].Id == row.UserId)
                {
                    dm = c;
                    break;
                }
            }

            try
            {
                if (dm is null)
                    dm = await client.CreateDmAsync(row.UserId).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                ShowError(loc["ChatToolbarErrorTitle"], ex.Message);
                return;
            }

            var recipient = (dm.Recipients ?? Array.Empty<DiscordUser>()).FirstOrDefault();
            if (recipient is null)
            {
                ShowError(loc["ChatToolbarErrorTitle"], loc["FriendsWindowOpenDmError"]);
                return;
            }

            var openingItem = new HomeListItemViewModel
            {
                Id = dm.Id,
                Name = row.DisplayName,
                Presence = row.Presence,
                LastMsgId = dm.LastMessageId ?? dm.Id,
                IsGroupChat = false,
                RecipientCount = 2,
                AvatarUrl = row.AvatarUrl,
                Image = row.AvatarUrl,
            };
            openingItem.Recipients.Add(recipient);

            Chat? existing = Application.Current.Windows.OfType<Chat>()
                .FirstOrDefault(x => x?.Channel?.Id == dm.Id);
            Chat chatWindow = existing is null
                ? new Chat(dm.Id, true, row.Presence, openingItem)
                : existing;

            chatWindow.Activate();
            if (chatWindow.WindowState == WindowState.Minimized)
                chatWindow.WindowState = WindowState.Normal;

            _closingAfterOpeningDm = true;
            Close();
        }

        private static LocalizationManager loc => LocalizationManager.Instance;

        private void ShowError(string title, string message)
        {
            var dialog = new Dialog(title, message, SystemIcons.Error) { Owner = this };
            dialog.ShowDialog();
        }

        private async void AddFriendButton_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new AddFriendDialog { Owner = this };
            if (dlg.ShowDialog() != true) return;

            string raw = dlg.UsernameInput;
            if (string.IsNullOrWhiteSpace(raw))
            {
                ShowError(loc["AddFriendDialogTitle"], loc["AddFriendErrorEmpty"]);
                return;
            }

            var (username, discriminator) = AddFriendDialog.ParseUsername(raw);
            if (string.IsNullOrWhiteSpace(username))
            {
                ShowError(loc["AddFriendDialogTitle"], loc["AddFriendErrorEmpty"]);
                return;
            }

            var client = Discord.Client;
            if (client is null) return;

            if (DiscordRelationshipHelper.ParsedInviteTargetsCurrentUser(client, username, discriminator))
            {
                ShowError(loc["AddFriendDialogTitle"], loc["FriendRequestCannotTargetSelf"]);
                return;
            }

            try
            {
                await client.SendFriendRequestAsync(username.Trim(), discriminator).ConfigureAwait(true);
                await _vm.ReloadAsync();
            }
            catch (BadRequestException ex)
            {
                // Discord returns 400 + captcha_key; we show WebView2Frame (unsupported), then retry fails — avoid a second generic "Bad request: 400" dialog.
                if (IsCaptchaRequiredFailure(ex))
                    return;

                ShowError(loc["AddFriendDialogTitle"], FormatFriendRequestBadRequest(ex));
            }
            catch (Exception ex)
            {
                if (ex.InnerException is BadRequestException bre && IsCaptchaRequiredFailure(bre))
                    return;

                ShowError(loc["AddFriendDialogTitle"], ex.Message);
            }
        }

        /// <summary>True when Discord required a captcha for this request (user may have already seen the captcha notice).</summary>
        private static bool IsCaptchaRequiredFailure(BadRequestException ex)
        {
            try
            {
                var body = ex.WebResponse?.Response;
                if (!string.IsNullOrEmpty(body) && body.Contains("captcha_key", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            catch { }

            if (!string.IsNullOrEmpty(ex.Errors) && ex.Errors.Contains("captcha", StringComparison.OrdinalIgnoreCase))
                return true;

            if (!string.IsNullOrEmpty(ex.JsonMessage) && ex.JsonMessage.Contains("captcha", StringComparison.OrdinalIgnoreCase))
                return true;

            return false;
        }

        private static string FormatFriendRequestBadRequest(BadRequestException ex)
        {
            if (!string.IsNullOrWhiteSpace(ex.JsonMessage))
                return ex.JsonMessage;
            if (!string.IsNullOrEmpty(ex.Errors))
                return ex.Message + "\n" + ex.Errors;
            return ex.Message;
        }
    }
}

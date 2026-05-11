using Aerochat.Enums;
using Aerochat.Helpers;
using Aerochat.Hoarder;
using Aerochat.Localization;
using Aerochat.Settings;
using Aerochat.ViewModels;
using DiscordProtos.DiscordUsers.V1;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Linq;
using Vanara.PInvoke;
using static Aerochat.ViewModels.HomeListViewCategory;
using Timer = System.Timers.Timer;

namespace Aerochat.Windows
{
    public partial class Home : Window
    {
        private Dictionary<ulong, Timer> _typingTimers = new();
        public HomeWindowViewModel ViewModel { get; } = new HomeWindowViewModel();
        private Timer _hoverTimer = new(50);
        private Timer _adTimer = new(20000);

        private static List<AdViewModel> _ads = new();

        private CancellationTokenSource? _homeDynamicRefreshCts;

        /// <summary>Coalesces rapid <see cref="PresenceUpdateEventArgs"/> bursts into a single <see cref="UpdateStatuses"/> pass.</summary>
        private DispatcherTimer? _updateStatusesDebounceTimer;

        /// <summary>Coalesces rapid <see cref="MessageCreateEventArgs"/> / read-receipt bursts into a single home list refresh.</summary>
        private DispatcherTimer? _updateUnreadDebounceTimer;

        public int AdIndex { get; set; } = 0;

        /// <summary>
        /// The base URL used for dynamic notices and news content.
        /// </summary>
        const string DYNAMIC_BASE_URL = "https://raw.githubusercontent.com/Maxokie/Updated-Aerochat/refs/heads/main/Dynamic/";

        /// <summary>
        /// The URL for remote dynamic news content shown along the bottom of the client.
        /// </summary>
        const string DYNAMIC_NEWS_URL    = DYNAMIC_BASE_URL + "news.json";

        /// <summary>
        /// The URL for remote notices shown along the top of the client until dismissal.
        /// </summary>
        const string DYNAMIC_NOTICES_URL = DYNAMIC_BASE_URL + "notices.json";

        const string AEROCHAT_HELP_WIKI_URL = "https://github.com/Maxokie/Updated-Aerochat/wiki/Frequently%E2%80%90asked-questions";

        private void LocalizationManager_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is not (null or "Item[]")) return;
            foreach (var cat in ViewModel.Categories)
                cat.InvokePropertyChanged(nameof(HomeListViewCategory.LocalizedTitle));
            _ = Dispatcher.BeginInvoke(new Action(() =>
            {
                if (Discord.Client?.CurrentUser is null || ViewModel.Categories.Count == 0)
                    return;
                RefreshCategoryHeaderCounts();
                ViewModel.UpdateFilteredCategories();
            }));
        }

        private void SettingsManager_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is null) return;
            if (e.PropertyName != nameof(SettingsManager.ContactListIconSizeFavorites)
                && e.PropertyName != nameof(SettingsManager.ContactListIconSizeConversationsServers)
                && e.PropertyName != nameof(SettingsManager.ContactListStatusOnlyServerOnlineIndicator)
                && e.PropertyName != nameof(SettingsManager.HomeShowFavorites)
                && e.PropertyName != nameof(SettingsManager.HomeShowConversations)
                && e.PropertyName != nameof(SettingsManager.HomeShowServers))
                return;

            _ = Dispatcher.BeginInvoke(new Action(() =>
            {
                InvalidateAllContactItemLayouts();
                ViewModel.UpdateFilteredCategories();
            }));
        }

        private void InvalidateAllContactItemLayouts()
        {
            foreach (var cat in ViewModel.Categories)
            {
                foreach (var item in cat.Items)
                    item.InvalidateContactLayout();
            }
        }

        public PresenceViewModel? FindPresenceForUserId(ulong userId)
        {
            foreach (var category in ViewModel.Categories)
            {
                foreach (var item in category.Items)
                {
                    if (item.Id == userId)
                    {
                        return item.Presence;
                    }
                }
            }

            return null;
        }

        public Home()
        {
            InitializeComponent();
            ViewModel.EvaluateRowForCategoryHeader = RowIsActiveForHeader;
            ViewModel.GetGuildTotalForHeader = () => Discord.Client?.Guilds.Count ?? 0;
            SettingsManager.Instance.PropertyChanged += SettingsManager_PropertyChanged;
            LocalizationManager.Instance.PropertyChanged += LocalizationManager_PropertyChanged;
            Closing += (_, _) =>
            {
                SettingsManager.Instance.PropertyChanged -= SettingsManager_PropertyChanged;
                SettingsManager.Instance.PropertyChanged -= OnSettingsChange;
                LocalizationManager.Instance.PropertyChanged -= LocalizationManager_PropertyChanged;
                try { _homeDynamicRefreshCts?.Cancel(); }
                catch (Exception ex) { DiagnosticsLog.Swallowed("Home.Closing: cancel dynamic refresh", ex); }
                _homeDynamicRefreshCts?.Dispose();
                _homeDynamicRefreshCts = null;
                _updateStatusesDebounceTimer?.Stop();
                _updateStatusesDebounceTimer = null;
                _updateUnreadDebounceTimer?.Stop();
                _updateUnreadDebounceTimer = null;
                newsTimer.Stop();
                newsTimer.Dispose();
                _adTimer.Stop();
                _adTimer.Dispose();
                _hoverTimer.Stop();
                _hoverTimer.Dispose();
                if (Discord.Client is not null)
                {
                    Discord.Client.PresenceUpdated -= InvokeUpdateStatuses;
                    Discord.Client.ChannelCreated -= ChannelCreatedEvent;
                    Discord.Client.ChannelDeleted -= ChannelDeletedEvent;
                    Discord.Client.DmChannelDeleted -= DmChannelDeletedEvent;
                    Discord.Client.VoiceStateUpdated -= VoiceStateUpdatedEvent;
                    Discord.Client.GuildUpdated -= Home_GuildUpdated;
                    Discord.Client.GuildCreated -= Home_GuildCreated;
                    Discord.Client.GuildAvailable -= Home_GuildCreated;
                    Discord.Client.GuildDeleted -= Home_GuildDeleted;
                    Discord.Client.RelationshipAdded -= Home_RelationshipAdded;
                    Discord.Client.RelationshipRemoved -= Home_RelationshipRemoved;
                    Discord.Client.MessageCreated -= Home_MessageCreated;
                }
                DiscordUserSettingsManager.Instance.UserSettingsUpdated -= OnUserSettingsUpdated;
            };
            Loaded += HomeListView_Loaded;
            Loaded += Home_InitializeAfterFirstLoad;
        }

        private async void Home_InitializeAfterFirstLoad(object sender, RoutedEventArgs e)
        {
            try
            {
                Loaded -= Home_InitializeAfterFirstLoad;

                if (Discord.Client?.CurrentUser is null)
                {
                    DiagnosticsLog.Swallowed("Home.Home_InitializeAfterFirstLoad", new InvalidOperationException("Discord client or current user is not available."));
                    return;
                }

                ViewModel.CurrentUser = UserViewModel.FromUser(Discord.Client.CurrentUser);

                PreloadedUserSettings? userSettings = DiscordUserSettingsManager.Instance.UserSettingsProto;
                if (userSettings is not null)
                    ViewModel.CurrentUser.Presence = PresenceViewModel.GetPresenceForCurrentUser(userSettings);

                ViewModel.Categories.Clear();
                DataContext = ViewModel;

                UpdateAdVisibility();
                UpdateNewsVisibility();

                await Client_Ready(Discord.Client, null);

                SettingsManager.Instance.PropertyChanged += OnSettingsChange;
            }
            catch (Exception ex)
            {
                DiagnosticsLog.Swallowed("Home.Home_InitializeAfterFirstLoad", ex);
            }
        }

        private void UpdateAdVisibility()
        {
            AdContainer.Visibility = SettingsManager.Instance.DisplayAds ? Visibility.Visible : Visibility.Collapsed;
        }

        private void UpdateNewsVisibility() =>
            NewsContainer.Visibility = SettingsManager.Instance.DisplayHomeNews ? Visibility.Visible : Visibility.Collapsed;

        private void OnSettingsChange(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SettingsManager.Instance.DisplayAds))
            {
                Dispatcher.BeginInvoke(UpdateAdVisibility);
            }
            else if (e.PropertyName == nameof(SettingsManager.Instance.DisplayHomeNews))
            {
                Dispatcher.BeginInvoke(UpdateNewsVisibility);
            }
        }

        /// <summary>Debounced refresh of server row unread indicators (call from gateway bursts).</summary>
        public void UpdateUnreadMessages()
        {
            if (Discord.Client?.CurrentUser is null || ViewModel.Categories.Count == 0)
                return;

            if (_updateUnreadDebounceTimer is null)
            {
                _updateUnreadDebounceTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
                {
                    Interval = TimeSpan.FromMilliseconds(80),
                };
                _updateUnreadDebounceTimer.Tick += (_, _) =>
                {
                    _updateUnreadDebounceTimer.Stop();
                    RunUpdateUnreadMessages();
                };
            }

            _updateUnreadDebounceTimer.Stop();
            _updateUnreadDebounceTimer.Start();
        }

        /// <summary>Immediate unread refresh (e.g. after building the server list).</summary>
        private void RunUpdateUnreadMessages()
        {
            if (Discord.Client?.CurrentUser is null) return;

            foreach (var category in ViewModel.Categories)
            {
                foreach (var item in category.Items)
                {
                    Discord.Client.TryGetCachedChannel(item.Id, out var c);
                    if (c is null || c is DiscordDmChannel) continue;
                    Discord.Client.TryGetCachedGuild(c.GuildId ?? 0, out var guild);
                    if (guild is null) continue;

                    bool hasUnread = false;
                    foreach (var channelId in guild.Channels)
                    {
                        bool found = SettingsManager.Instance.LastReadMessages.TryGetValue(channelId.Key, out var lastReadMessageId);
                        DateTime lastReadMessageTime;
                        if (found)
                        {
                            lastReadMessageTime = DateTimeOffset.FromUnixTimeMilliseconds(((long)(lastReadMessageId >> 22) + 1420070400000)).DateTime;
                        }
                        else
                        {
                            lastReadMessageTime = SettingsManager.Instance.ReadRecieptReference;
                        }
                        var channel = guild.Channels[channelId.Key];
                        var lastMessageId = channel.LastMessageId;
                        if ((channel.Type != ChannelType.Text && channel.Type != ChannelType.Announcement) || lastMessageId == null) continue;
                        var lastMessageTime = DateTimeOffset.FromUnixTimeMilliseconds(((long)(lastMessageId >> 22) + 1420070400000)).DateTime;
                        if (lastMessageTime > lastReadMessageTime)
                        {
                            hasUnread = true;
                            break;
                        }
                    }

                    item.GuildHasUnread = hasUnread;
                }
            }
        }

        private Random _random = new();
        Dictionary<int, int> adWeights = new();

        private int GetNextAdIndex()
        {
            if (_ads.Count == 0)
                return 0;

            int totalWeight = adWeights.Values.Sum();
            if (totalWeight <= 0)
                return 0;

            int randomWeight = _random.Next(totalWeight);

            int cumulativeWeight = 0;
            for (int i = 0; i < _ads.Count; i++)
            {
                cumulativeWeight += adWeights[i];
                if (randomWeight < cumulativeWeight)
                {
                    return i;
                }
            }
            return 0;
        }

        private async Task Client_Ready(DiscordClient sender, DSharpPlus.EventArgs.ReadyEventArgs args)
        {
            await Dispatcher.BeginInvoke(() =>
            {
                newsTimer.Elapsed += NewsTimer_Elapsed;
                newsTimer.Start();
                var assembly = Assembly.GetExecutingAssembly();
                string resourceName = "Aerochat.Ads.Ads.xml";
                using (Stream? stream = assembly.GetManifestResourceStream(resourceName))
                {
                    if (stream is not null)
                    {
                        using StreamReader reader = new(stream);
                        string result = reader.ReadToEnd();
                        XDocument doc = XDocument.Parse(result);
                        foreach (XElement adXml in doc.Root?.Elements() ?? [])
                        {
                            AdViewModel ad = AdViewModel.FromAd(adXml);
                            _ads.Add(ad);
                        }
                    }
                }

                if (_ads.Count > 0)
                {
                    Random random = new();
                    AdIndex = random.Next(_ads.Count);

                    for (int i = 0; i < _ads.Count; i++)
                    {
                        adWeights[i] = 1;
                    }

                    _adTimer.Elapsed += (s, e) =>
                    {
                        if (_ads.Count == 0)
                            return;

                        AdIndex = GetNextAdIndex();

                        for (int i = 0; i < _ads.Count; i++)
                        {
                            if (i == AdIndex)
                            {
                                adWeights[i] = 1;
                            }
                            else
                            {
                                adWeights[i]++;
                            }
                        }

                        AdViewModel adVm = _ads[AdIndex];
                        ViewModel.Ad = adVm;
                    };

                    ViewModel.Ad = _ads[AdIndex];
                    _adTimer.Start();
                }

                ViewModel.Categories.Insert(0, new HomeListViewCategory { Name = "Favorites" });
                ViewModel.Categories.Add(new HomeListViewCategory { Name = "Conversations" });
                ViewModel.Categories.Add(new HomeListViewCategory { Name = "Servers" });

                Discord.Client.PresenceUpdated += InvokeUpdateStatuses;
                Discord.Client.ChannelCreated += ChannelCreatedEvent;
                Discord.Client.ChannelDeleted += ChannelDeletedEvent;
                Discord.Client.DmChannelDeleted += DmChannelDeletedEvent;
                Discord.Client.VoiceStateUpdated += VoiceStateUpdatedEvent;
                Discord.Client.GuildUpdated += Home_GuildUpdated;
                Discord.Client.GuildCreated += Home_GuildCreated;
                Discord.Client.GuildAvailable += Home_GuildCreated;
                Discord.Client.GuildDeleted += Home_GuildDeleted;
                Discord.Client.RelationshipAdded += Home_RelationshipAdded;
                Discord.Client.RelationshipRemoved += Home_RelationshipRemoved;
                DiscordUserSettingsManager.Instance.UserSettingsUpdated += OnUserSettingsUpdated;

                UpdateStatuses();
                AddGuilds();
                RefreshFavoritesCategory();

                Discord.Client.MessageCreated += Home_MessageCreated;

                _hoverTimer.Elapsed += OnTimerEnd;
                _hoverTimer.AutoReset = false;

                Show();
                Focus();

                _homeDynamicRefreshCts?.Cancel();
                _homeDynamicRefreshCts?.Dispose();
                _homeDynamicRefreshCts = new CancellationTokenSource();
                var refreshToken = _homeDynamicRefreshCts.Token;
                _ = Task.Run(async () =>
                {
                    while (!refreshToken.IsCancellationRequested)
                    {
                        try { await CheckForUpdates(); }
                        catch (Exception ex) { DiagnosticsLog.Swallowed("Home: CheckForUpdates (background)", ex); }
                        try { await GetNewNews(); }
                        catch (Exception ex) { DiagnosticsLog.Swallowed("Home: GetNewNews (background)", ex); }
                        try { await GetNewNotices(); }
                        catch (Exception ex) { DiagnosticsLog.Swallowed("Home: GetNewNotices (background)", ex); }
                        try
                        {
                            await Task.Delay(60 * 5 * 1000, refreshToken);
                        }
                        catch (OperationCanceledException)
                        {
                            break;
                        }
                    }
                }, refreshToken);

#if AEROCHAT_RC && !DEVELOPER_PRERELEASE
                Dialog betaNoticeDlg = new(
                    LocalizationManager.Instance["HomeNoticeTitle"],
                    LocalizationManager.Instance["HomeBetaNoticeRC"],
                    SystemIcons.Information
                );
                betaNoticeDlg.Owner = null;
                betaNoticeDlg.ShowDialog();
#endif

#if RELEASE && !AEROCHAT_RC
                if (SettingsManager.Instance.ShowBetaWarning)
                {
                    Dialog betaNoticeDlg = new(
                        LocalizationManager.Instance["HomeNoticeTitle"],
                        LocalizationManager.Instance["HomeBetaNoticeDev"],
                        SystemIcons.Information
                    );
                    betaNoticeDlg.Owner = this;
                    betaNoticeDlg.ShowDialog();
                }
#endif

                OpenChatQueue.Instance.ExecuteQueue();
                OpenChatQueue.Instance.ExecuteOnAdd = true;
            });
        }

        private void OnUserSettingsUpdated(object? sender, DiscordUserSettingsUpdateEventArgs e)
        {
            if (ViewModel.CurrentUser is null)
                return;
            ViewModel.CurrentUser.Presence = PresenceViewModel.GetPresenceForCurrentUser(e.NewSettings);
        }

        private void Image_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_ads.Count == 0)
                return;

            AdIndex = GetNextAdIndex();

            for (int i = 0; i < _ads.Count; i++)
            {
                if (i == AdIndex)
                {
                    adWeights[i] = 1;
                }
                else
                {
                    adWeights[i]++;
                }
            }

            // Reset the ad switch timer so it isn't desynchronised by this forceful
            // change. This is particularly important for some animated ads, which
            // may not get to play their full contents if the timer length is severely
            // desynchronised from continous skipping.
            _adTimer.Stop();
            _adTimer.Start();

            ViewModel.Ad = _ads[AdIndex];
        }

        private void NewsTimer_Elapsed(object? sender, ElapsedEventArgs e)
        {
            // go to the next news item
            Dispatcher.BeginInvoke(() =>
            {
                try
                {
                    var index = ViewModel.News.IndexOf(ViewModel.CurrentNews);
                    if (index == ViewModel.News.Count - 1)
                    {
                        ViewModel.CurrentNews = ViewModel.News[0];
                    }
                    else
                    {
                        ViewModel.CurrentNews = ViewModel.News[index + 1];
                    }
                }
                catch
                {
                    ViewModel.CurrentNews ??= new NewsViewModel();
                    ViewModel.CurrentNews.Body = LocalizationManager.Instance["HomeFailedFetchNews"];
                }
            });
        }

        private Timer newsTimer = new(20000);

        public void SetNews(NewsViewModel news)
        {
            ViewModel.CurrentNews = news;
            var animation = new DoubleAnimation(0, 1, new Duration(TimeSpan.FromSeconds(0.5)));
            NewsText.BeginAnimation(OpacityProperty, animation);
            // reset the timer
            newsTimer.Stop();
            newsTimer.Start();
        }

        /// <summary>
        /// Gets the user agent we report when making a request to remote servers.
        /// </summary>
        private static string GetAerochatUserAgent()
        {
            return "Aerochat/" + Assembly.GetExecutingAssembly().GetName().Version!.ToString(3);
        }

        /// <summary>
        /// Retrieves new news headlines from the remote server. This is shown along the bottom of the home window
        /// at all times unless disabled in the user's appearance settings.
        /// </summary>
        public async Task GetNewNews()
        {
            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Add("User-Agent", GetAerochatUserAgent());
            try
            {
                using var response = await httpClient.GetAsync(DYNAMIC_NEWS_URL + "?breaker=" + DateTimeOffset.Now.ToUnixTimeMilliseconds());
                if (!response.IsSuccessStatusCode)
                    return;

                string body = await response.Content.ReadAsStringAsync();
                try
                {
                    using JsonDocument news = JsonDocument.Parse(body, new JsonDocumentOptions()
                    {
                        AllowTrailingCommas = true,
                        CommentHandling = JsonCommentHandling.Skip,
                    });
                    var newsList = new List<NewsViewModel>();
                    foreach (var n in news.RootElement.EnumerateArray())
                    {
                        newsList.Add(NewsViewModel.FromNews(n));
                    }

                    _ = Dispatcher.BeginInvoke(() =>
                    {
                        ViewModel.News.Clear();
                        foreach (var n in newsList)
                        {
                            ViewModel.News.Add(n);
                        }

                        ViewModel.CurrentNews = ViewModel.News.FirstOrDefault(x => x.Date == ViewModel.CurrentNews?.Date) ?? ViewModel.News.FirstOrDefault();
                    });
                }
                catch (JsonException ex)
                {
                    DiagnosticsLog.Swallowed("Home.GetNewNews: invalid JSON", ex);
                }
            }
            catch (HttpRequestException ex)
            {
                DiagnosticsLog.Swallowed("Home.GetNewNews: HTTP", ex);
            }
        }

        /// <summary>
        /// Retrieves new notices which are shown along the top of the server list periodically as they update.
        /// </summary>
        public async Task GetNewNotices()
        {
            var noticesList = new List<NoticeViewModel>();
            
            // Get the latest notices from the GitHub repo.
            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Add("User-Agent", GetAerochatUserAgent());
            try
            {
                using var response = await httpClient.GetAsync(DYNAMIC_NOTICES_URL + "?breaker=" + DateTimeOffset.Now.ToUnixTimeMilliseconds());
                if (!response.IsSuccessStatusCode)
                    return;

                string body = await response.Content.ReadAsStringAsync();
                try
                {
                    using JsonDocument notices = JsonDocument.Parse(body, new JsonDocumentOptions()
                    {
                        AllowTrailingCommas = true,
                        CommentHandling = JsonCommentHandling.Skip,
                    });
                    // this is an array so iterate through it
                    foreach (var notice in notices.RootElement.EnumerateArray())
                    {
                        var noticeViewModel = NoticeViewModel.FromNotice(notice);
                        if (!noticeViewModel.IsTargeted || SettingsManager.Instance.ViewedNotices.Contains(noticeViewModel.Date)) continue;
                        noticesList.Add(noticeViewModel);
                    }

                    _ = Dispatcher.BeginInvoke(() =>
                    {
                        ViewModel.Notices.Clear();
                        foreach (var n in noticesList)
                        {
                            ViewModel.Notices.Add(n);
                        }
                    });
                }
                catch (JsonException ex)
                {
                    DiagnosticsLog.Swallowed("Home.GetNewNotices: invalid JSON", ex);
                }
            }
            catch (HttpRequestException ex)
            {
                DiagnosticsLog.Swallowed("Home.GetNewNotices: HTTP", ex);
            }
        }
        private void CloseNoticeButton_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (ViewModel.Notices.Count == 0)
                return;
            var notice = ViewModel.Notices[0];
            SettingsManager.Instance.ViewedNotices.Add(notice.Date);
            ViewModel.Notices.Remove(notice);
            SettingsManager.Save();
        }

        private bool showingUpdate = false;

        public async Task CheckForUpdates()
        {
#if DEBUG || DEVELOPER_PRERELEASE
            return;
#endif
            if (showingUpdate)
                return;

            HttpClient httpClient = new();
            httpClient.DefaultRequestHeaders.Add("User-Agent", GetAerochatUserAgent());

            HttpResponseMessage response;
            try
            {
                response = await httpClient.GetAsync("https://api.github.com/repos/Maxokie/Updated-Aerochat/tags");
            }
            catch (Exception)
            {
                // Ignore networking exception.
                httpClient.Dispose();
                return;
            }

            if (!response.IsSuccessStatusCode)
            {
                // Ignore unsuccessful requests.
                response.Dispose();
                httpClient.Dispose();
                return;
            }

            string? latestTag;
            try
            {
                using (JsonDocument tags = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
                {
                    latestTag = tags.RootElement[0].GetProperty("name").GetString();
                }
            }
            catch (Exception)
            {
                httpClient.Dispose();
                return;
            }

            if (latestTag == null)
            {
                httpClient.Dispose();
                return;
            }

#if !AEROCHAT_RC
            var localVersion = Assembly.GetExecutingAssembly().GetName().Version;
#else
            var localVersion = Version.Parse(AssemblyInfo.RC_LAST_VERSION);
#endif
            if (localVersion == null)
            {
                httpClient.Dispose();
                return;
            }

            string latestTagVersion = latestTag.Split('-')[0];
            
            if (latestTagVersion.StartsWith('v'))
            {
                latestTagVersion = latestTagVersion.Remove(0, 1);
            }

            Version remoteVersion = new(latestTagVersion);
            if (localVersion.CompareTo(remoteVersion) < 0)
            {
                _ = Dispatcher.BeginInvoke(async () =>
                {
                    showingUpdate = true;
                    Dialog dialog = new(
                        "A new version is available", 
                        $"Version {remoteVersion} has been released, but you currently have {localVersion.ToString()}. Press Continue to update.", 
                        SystemIcons.Information
                    );
                    dialog.Owner = this;
                    dialog.ShowDialog();
                    Hide();

                    //close all windows other than this one
                    foreach (Window window in Application.Current.Windows)
                    {
                        if (window != this)
                            window.Close();
                    }
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            HttpResponseMessage releaseResponse;
                            try
                            {
                                releaseResponse = await httpClient.GetAsync($"https://api.github.com/repos/Maxokie/Updated-Aerochat/releases/tags/{latestTag}");
                            }
                            catch (Exception)
                            {
                                await ShowAutomaticUpdateDownloadFailureDialog(latestTag);
                                Dispatcher.BeginInvoke(Close);
                                return;
                            }

                            using (JsonDocument release = JsonDocument.Parse(await releaseResponse.Content.ReadAsStringAsync()))
                            {
                                string? assetUrl = null;
                                try
                                {
                                    var assets = release.RootElement.GetProperty("assets");

                                    if (assets.GetArrayLength() > 0)
                                    {
                                        assetUrl = assets[0].GetProperty("browser_download_url").GetString();
                                    }
                                }
                                catch (Exception)
                                {
                                    await ShowAutomaticUpdateDownloadFailureDialog(latestTag);
                                    Dispatcher.BeginInvoke(Close);
                                    return;
                                }

                                if (assetUrl == null)
                                {
                                    await ShowAutomaticUpdateDownloadFailureDialog(latestTag);
                                    Dispatcher.BeginInvoke(Close);
                                    return;
                                }

                                // get a temp folder
                                string tempFolder = Path.GetTempPath();
                                string tempSetupExePath = Path.Combine(tempFolder, "aerochat-setup.exe");

                                // download the asset to the temp folder
                                var asset = await httpClient.GetAsync(assetUrl);
                                byte[] assetBytes = await asset.Content.ReadAsByteArrayAsync();

                                try
                                {
                                    File.WriteAllBytes(tempSetupExePath, assetBytes);

                                    // ShellExecute will open the UAC prompt, rather than trying to open the application with the same
                                    // permissions as the current process and potentially failing.
                                    Shell32.ShellExecute(HWND.NULL, "open", tempSetupExePath, null, null, ShowWindowCommand.SW_SHOWNORMAL);
                                }
                                catch (Exception)
                                {
                                    await ShowAutomaticUpdateDownloadFailureDialog(latestTag);
                                    Dispatcher.BeginInvoke(Close);
                                    return;
                                }

                                Dispatcher.BeginInvoke(Close);
                            }
                        }
                        finally
                        {
                            httpClient.Dispose();
                        }
                    });
                });
            }
            else
            {
                httpClient.Dispose();
            }
        }

        private async Task ShowAutomaticUpdateDownloadFailureDialog(string latestTag)
        {
            // We couldn't fetch the release for whatever reason, so inform the user of the error and
            // open the release's link in the user's browser:
            await Dispatcher.InvokeAsync(() =>
            {
                Dialog failureDialog = new(
                    "Failed to download update",
                    "We failed to automatically download this update. We will open the download " +
                    "link in your browser instead.",
                    SystemIcons.Warning
                );
                failureDialog.Owner = this;
                failureDialog.ShowDialog();

                Shell32.ShellExecute(HWND.NULL, "open",
                    $"https://github.com/Maxokie/Updated-Aerochat/releases/tag/{latestTag}", null, null,
                    ShowWindowCommand.SW_SHOWNORMAL
                );
            });
        }

        private async Task VoiceStateUpdatedEvent(DiscordClient sender, DSharpPlus.EventArgs.VoiceStateUpdateEventArgs args)
        {
            if (args.Guild is null) return;
            var field = args.Guild.GetType().GetField("_voiceStates", BindingFlags.NonPublic | BindingFlags.Instance);
            if (field is null) return;
            var voiceStates = field.GetValue(args.Guild) as ConcurrentDictionary<ulong, DiscordVoiceState>;
            if (voiceStates is null) return;
            if (args.Channel is null)
            {
                voiceStates.TryRemove(args.User.Id, out _);
            }
            else
            {
                voiceStates[args.User.Id] = args.After;
            }
        }

        private async Task ChannelDeletedEvent(DiscordClient sender, DSharpPlus.EventArgs.ChannelDeleteEventArgs args)
        {
            if (args.Channel.GuildId is null) await Dispatcher.InvokeAsync(() => UpdateStatuses());
        }

        private Task DmChannelDeletedEvent(DiscordClient sender, DmChannelDeleteEventArgs args)
        {
            _ = Dispatcher.BeginInvoke(new Action(() =>
            {
                UpdateStatuses();
                bool dirty = SettingsManager.Instance.FavoriteConversationIds.Remove(args.Channel.Id);
                if (SettingsManager.Instance.RecentDMChats.Remove(args.Channel.Id))
                    dirty = true;
                if (SettingsManager.Instance.LastReadMessages.Remove(args.Channel.Id))
                    dirty = true;
                if (dirty)
                {
                    SettingsManager.Save();
                    RefreshFavoritesCategory();
                }
            }));
            return Task.CompletedTask;
        }

        private async Task ChannelCreatedEvent(DiscordClient sender, DSharpPlus.EventArgs.ChannelCreateEventArgs args)
        {
            if (args.Channel.GuildId is null) await Dispatcher.InvokeAsync(() => UpdateStatuses());
        }

        private async Task InvokeUpdateStatuses(DiscordClient sender, DSharpPlus.EventArgs.PresenceUpdateEventArgs args)
        {
            try
            {
                await Dispatcher.InvokeAsync(ScheduleUpdateStatuses);
            }
            catch (TaskCanceledException)
            {
                // Ignore.
            }
        }

        private void ScheduleUpdateStatuses()
        {
            if (_updateStatusesDebounceTimer is null)
            {
                _updateStatusesDebounceTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
                {
                    Interval = TimeSpan.FromMilliseconds(120),
                };
                _updateStatusesDebounceTimer.Tick += (_, _) =>
                {
                    _updateStatusesDebounceTimer.Stop();
                    UpdateStatuses();
                };
            }

            _updateStatusesDebounceTimer.Stop();
            _updateStatusesDebounceTimer.Start();
        }

        private Task Home_MessageCreated(DiscordClient s, MessageCreateEventArgs e)
        {
            if (e.Channel is DiscordDmChannel)
            {
                var dm = Discord.Client.PrivateChannels[e.Channel.Id];
                DiscordChannelLastMessageId.TrySet(dm, e.Message.Id);
                UpdateStatuses();
            }
            else
            {
                if (!Discord.Client.TryGetCachedGuild(e.Channel.GuildId ?? 0, out var guild)) return Task.CompletedTask;
                if (!Discord.Client.TryGetCachedChannel(e.Channel.Id, out var channel)) return Task.CompletedTask;
                if (channel.Type != ChannelType.Text && channel.Type != ChannelType.Announcement) return Task.CompletedTask;

                DiscordChannelLastMessageId.TrySet(guild.Channels[channel.Id], e.Message.Id);
            }

            UpdateUnreadMessages();
            return Task.CompletedTask;
        }

        /// <summary>
        /// Guild list rows use the first text channel id as <see cref="HomeListItemViewModel.Id"/>; refresh name/icon when Discord pushes GUILD_UPDATE.
        /// </summary>
        private Task Home_GuildUpdated(DiscordClient client, GuildUpdateEventArgs e)
        {
            var guild = e.GuildAfter ?? e.GuildBefore;
            if (guild is null) return Task.CompletedTask;

            _ = Dispatcher.BeginInvoke(new Action(() =>
            {
                if (Discord.Client?.CurrentUser is null) return;
                ApplyGuildAppearanceToHomeListItems(guild);
            }));
            return Task.CompletedTask;
        }

        private Task Home_GuildCreated(DiscordClient client, GuildCreateEventArgs e)
        {
            if (e.Guild is null) return Task.CompletedTask;
            _ = Dispatcher.BeginInvoke(new Action(() => TryAddGuildToSidebar(e.Guild)));
            return Task.CompletedTask;
        }

        private Task Home_GuildDeleted(DiscordClient client, GuildDeleteEventArgs e)
        {
            if (e.Unavailable || e.Guild is null) return Task.CompletedTask;
            var gid = e.Guild.Id;
            _ = Dispatcher.BeginInvoke(new Action(() => RemoveGuildFromSidebar(gid)));
            return Task.CompletedTask;
        }

        /// <summary>Adds a server row when the user joins a guild (or when <see cref="GuildAvailable"/> fires). Skips if already listed.</summary>
        private void TryAddGuildToSidebar(DiscordGuild guild)
        {
            if (Discord.Client?.CurrentUser is null || ViewModel.Categories.Count < 3) return;
            if (SidebarContainsGuild(guild.Id)) return;
            if (!TryGetFirstAccessibleTextChannelId(guild, out ulong channelItemId)) return;

            int categoryIndex = ResolveCategoryIndexForGuild(guild.Id);
            if (categoryIndex < 0 || categoryIndex >= ViewModel.Categories.Count)
                categoryIndex = Math.Min(2, ViewModel.Categories.Count - 1);

            CreateAndInsertGuild(guild.Name, channelItemId, categoryIndex, guild.IconUrl ?? "", guild.Id);
            RefreshCategoryHeaderCounts();
            ViewModel.UpdateFilteredCategories();
            RunUpdateUnreadMessages();
        }

        private void RemoveGuildFromSidebar(ulong serverGuildId)
        {
            if (Discord.Client?.CurrentUser is null) return;

            foreach (var cat in ViewModel.Categories)
            {
                for (int i = cat.Items.Count - 1; i >= 0; i--)
                {
                    var item = cat.Items[i];
                    if (!item.IsGuildServerRow) continue;
                    bool match = item.GuildId != 0
                        ? item.GuildId == serverGuildId
                        : Discord.Client.TryGetCachedChannel(item.Id, out var ch) && ch.GuildId == serverGuildId;
                    if (match)
                        cat.Items.RemoveAt(i);
                }
            }

            RefreshFavoritesCategory();
            RefreshCategoryHeaderCounts();
            ViewModel.UpdateFilteredCategories();
            RunUpdateUnreadMessages();
        }

        private bool SidebarContainsGuild(ulong serverGuildId)
        {
            if (Discord.Client is null) return false;
            foreach (var cat in ViewModel.Categories)
            {
                foreach (var item in cat.Items)
                {
                    if (!item.IsGuildServerRow) continue;
                    if (item.GuildId != 0)
                    {
                        if (item.GuildId == serverGuildId) return true;
                        continue;
                    }
                    if (Discord.Client.TryGetCachedChannel(item.Id, out var ch) && ch.GuildId == serverGuildId)
                        return true;
                }
            }
            return false;
        }

        private int ResolveCategoryIndexForGuild(ulong serverGuildId)
        {
            var folders = DiscordUserSettingsManager.Instance.UserSettingsProto?.GuildFolders?.Folders;
            if (folders is not null)
            {
                foreach (var folder in folders)
                {
                    if (!folder.GuildIds.Contains(serverGuildId)) continue;
                    if (string.IsNullOrEmpty(folder.Name))
                        return 2;
                    var cat = ViewModel.Categories.FirstOrDefault(c => c.Name == folder.Name);
                    if (cat is not null)
                        return ViewModel.Categories.IndexOf(cat);
                }
            }
            return 2;
        }

        private static bool TryGetFirstAccessibleTextChannelId(DiscordGuild guild, out ulong channelItemId)
        {
            channelItemId = 0;
            var channelsList = new List<DiscordChannel>();
            foreach (var c in guild.Channels.Values)
            {
                if ((c.PermissionsFor(guild.CurrentMember) & Permissions.AccessChannels) == Permissions.AccessChannels && c.Type == ChannelType.Text)
                    channelsList.Add(c);
            }
            if (channelsList.Count == 0) return false;
            channelsList.Sort((x, y) => x.Position.CompareTo(y.Position));
            channelItemId = channelsList[0].Id;
            return true;
        }

        private void ApplyGuildAppearanceToHomeListItems(DiscordGuild guild)
        {
            if (Discord.Client is null) return;

            string iconUrl = guild.IconUrl ?? "";
            string name = guild.Name;

            foreach (var cat in ViewModel.Categories)
            {
                foreach (var item in cat.Items)
                {
                    if (!Discord.Client.TryGetCachedChannel(item.Id, out var ch)) continue;
                    if (ch is DiscordDmChannel) continue;
                    if (ch.GuildId != guild.Id) continue;

                    // Do not replace a valid URL with empty when the gateway payload omitted icon (see UpdateCachedGuild merge).
                    if (!string.IsNullOrWhiteSpace(iconUrl))
                    {
                        if (item.AvatarUrl != iconUrl)
                            item.AvatarUrl = iconUrl;
                    }
                    else if (string.IsNullOrWhiteSpace(item.AvatarUrl))
                        item.AvatarUrl = "";

                    if (item.Name != name)
                        item.Name = name;
                }
            }
        }

        private void AddGuilds()
        {
            // get all guilds which aren't sorted (ie not in a folder)
            List<ulong> processedGuilds = new();

            if (DiscordUserSettingsManager.Instance.UserSettingsProto?.GuildFolders?.Folders is not null)
            {
                foreach (PreloadedUserSettings.Types.GuildFolder folder in DiscordUserSettingsManager.Instance.UserSettingsProto!.GuildFolders.Folders)
                {
                    var index = 2; // Servers category index (0=Favorites, 1=Conversations, 2=Servers)
                    if (!string.IsNullOrEmpty(folder.Name))
                    {
                        var category = new HomeListViewCategory
                        {
                            Name = folder.Name,
                        };
                        ViewModel.Categories.Add(category);
                        index = ViewModel.Categories.IndexOf(category);
                    }
                    foreach (var guildId in folder.GuildIds)
                    {
                        Discord.Client.TryGetCachedGuild(guildId, out var guild);
                        if (guild == null) continue;
                        var channels = guild.Channels.Values;
                        List<DiscordChannel> channelsList = new();
                        foreach (var c in channels)
                        {
                            if ((c.PermissionsFor(guild.CurrentMember) & Permissions.AccessChannels) == Permissions.AccessChannels && c.Type == ChannelType.Text)
                            {
                                channelsList.Add(c);
                            }
                        }

                        channelsList.Sort((x, y) => x.Position.CompareTo(y.Position));

                        if (channelsList.Count == 0) continue;

                        CreateAndInsertGuild(guild.Name, channelsList[0].Id, index, guild.IconUrl ?? "", guild.Id);
                        processedGuilds.Add(guildId);
                    }
                }
            }
            // for each item in uncategorizedGuilds, add it to the Servers folder [1]
            // sorted by join date
            var uncategorizedGuilds = Discord.Client.Guilds
                .Where(x => !processedGuilds.Contains(x.Key))
                .OrderBy(x => x.Value.JoinedAt)
                .Select(x => x.Value);
            foreach (var guild in uncategorizedGuilds)
            {
                var channels = guild.Channels.Values;
                List<DiscordChannel> channelsList = new();
                foreach (var c in channels)
                {
                    if ((c.PermissionsFor(guild.CurrentMember) & Permissions.AccessChannels) == Permissions.AccessChannels && c.Type == ChannelType.Text)
                    {
                        channelsList.Add(c);
                    }
                }

                channelsList.Sort((x, y) => x.Position.CompareTo(y.Position));

                CreateAndInsertGuild(guild.Name, channelsList.ElementAtOrDefault(0)?.Id ?? 0, 2, guild.IconUrl ?? "", guild.Id);
            }
            RunUpdateUnreadMessages();
        }

        private void CreateAndInsertGuild(string name, ulong channelItemId, int categoryIndex, string iconUrl, ulong serverGuildId)
        {
            var guildItem = new HomeListItemViewModel
            {
                Name = name,
                AvatarUrl = iconUrl,
                IsGuildServerRow = true,
                GuildId = serverGuildId,
                Presence = new PresenceViewModel
                {
                    Presence = "",
                    Status = "",
                    Type = "",
                },
                IsSelected = false,
                LastMsgId = 0,
                Id = channelItemId,
                ListSection = ContactListSectionKind.Servers,
            };

            ViewModel.Categories[categoryIndex].Items.Add(guildItem);
        }

        private static bool IsActivePresenceStatus(string? status) =>
            status is "Online" or "Idle" or "DoNotDisturb";

        /// <summary>
        /// A row counts toward the "online" numerator if the DM is Online/Idle/DND, or (group) any other member is.
        /// </summary>
        private bool RowIsActiveForHeader(HomeListItemViewModel item)
        {
            if (Discord.Client?.CurrentUser is null)
                return false;

            if (!item.IsGroupChat)
                return IsActivePresenceStatus(item.Presence?.Status);

            foreach (var u in item.Recipients)
            {
                if (u.Id == Discord.Client.CurrentUser.Id)
                    continue;
                if (Discord.Client.Presences.TryGetValue(u.Id, out var pr) && IsActivePresenceStatus(pr.Status.ToString()))
                    return true;
            }

            return false;
        }

        private void RefreshCategoryHeaderCounts()
        {
            if (Discord.Client?.CurrentUser is null || ViewModel.Categories.Count == 0)
                return;

            int guildTotal = Discord.Client.Guilds.Count;

            foreach (var cat in ViewModel.Categories)
            {
                HomeListViewCategory.ApplyHeaderCounts(cat, guildTotal, RowIsActiveForHeader);
            }
        }

        private void RefreshFavoritesCategory()
        {
            if (ViewModel.Categories.Count == 0) return;
            var favoritesCategory = ViewModel.Categories[0];
            if (favoritesCategory.Name != "Favorites")
            {
                RefreshCategoryHeaderCounts();
                return;
            }

            favoritesCategory.Items.Clear();

            // Conversations category is at index 1
            const int conversationsIndex = 1;
            if (ViewModel.Categories.Count > conversationsIndex)
            {
                foreach (var id in SettingsManager.Instance.FavoriteConversationIds.ToList())
                {
                    var source = ViewModel.Categories[conversationsIndex].Items.FirstOrDefault(i => i.Id == id);
                    if (source != null)
                    {
                        var fav = CloneListItem(source);
                        fav.ListSection = ContactListSectionKind.Favorites;
                        favoritesCategory.Items.Add(fav);
                    }
                }
            }

            // Servers and folders start at index 2
            for (var catIndex = 2; catIndex < ViewModel.Categories.Count; catIndex++)
            {
                foreach (var id in SettingsManager.Instance.FavoriteGuildIds.ToList())
                {
                    var source = ViewModel.Categories[catIndex].Items.FirstOrDefault(i => i.Id == id);
                    if (source != null)
                    {
                        var fav = CloneListItem(source);
                        fav.ListSection = ContactListSectionKind.Favorites;
                        favoritesCategory.Items.Add(fav);
                        break; // each guild appears in only one category
                    }
                }
            }

            RefreshCategoryHeaderCounts();
        }

        private static HomeListItemViewModel CloneListItem(HomeListItemViewModel source)
        {
            var clone = new HomeListItemViewModel
            {
                Id = source.Id,
                Name = source.Name,
                Image = source.Image,
                AvatarUrl = source.AvatarUrl,
                IsGuildServerRow = source.IsGuildServerRow,
                GuildId = source.GuildId,
                GuildHasUnread = source.GuildHasUnread,
                Presence = source.Presence,
                IsGroupChat = source.IsGroupChat,
                RecipientCount = source.RecipientCount,
                LastMsgId = source.LastMsgId,
                ListSection = source.ListSection,
                IsBlocked = source.IsBlocked,
            };
            foreach (var r in source.Recipients) clone.Recipients.Add(r);
            foreach (var u in source.ConnectedUsers) clone.ConnectedUsers.Add(u);
            return clone;
        }

        private Task Home_RelationshipAdded(DiscordClient client, RelationshipAddEventArgs e)
        {
            _ = Dispatcher.BeginInvoke(new Action(RefreshBlockedConversationRows));
            return Task.CompletedTask;
        }

        private Task Home_RelationshipRemoved(DiscordClient client, RelationshipRemoveEventArgs e)
        {
            _ = Dispatcher.BeginInvoke(new Action(RefreshBlockedConversationRows));
            return Task.CompletedTask;
        }

        private void RefreshBlockedConversationRows()
        {
            if (Discord.Client?.CurrentUser is null) return;
            ulong selfId = Discord.Client.CurrentUser.Id;
            foreach (var item in ViewModel.Categories[1].Items)
            {
                if (!item.IsGroupChat && item.Recipients.Count > 0)
                {
                    var other = item.Recipients.FirstOrDefault(r => r.Id != selfId);
                    item.IsBlocked = other is not null && DiscordRelationshipHelper.IsUserBlocked(Discord.Client, other.Id);
                }
                else
                    item.IsBlocked = false;
            }
        }

        private void UpdateStatuses()
        {
            // Update the UI with the sorted list
            _ = Dispatcher.BeginInvoke(() =>
            {
                var oldList = ViewModel.Categories[1].Items;
                var newList = new List<HomeListItemViewModel>();

                // Build the new list from the current private channels
                foreach (var c in Discord.Client.PrivateChannels)
                {
                    var dm = c.Value;
                    bool isGroupChat = dm?.Recipients?.Count > 1;
                    var recipient = dm?.Recipients?.FirstOrDefault();
                    if (recipient is null) continue;

                    // Create new item or reuse existing item's selection state
                    var existingItem = oldList.ToList().FirstOrDefault(v => v?.Id == dm?.Id);
                    var newItem = new HomeListItemViewModel
                    {
                        Name = isGroupChat ? string.IsNullOrEmpty(dm.Name) ? String.Join(", ", dm.Recipients.Select(r => r.DisplayName)) : dm.Name : recipient.DisplayName,
                        Presence = recipient.Presence == null ? new PresenceViewModel()
                        {
                            Presence = "",
                            Status = recipient.Presence?.Status.ToString() ?? "Offline",
                            Type = "",
                        } : PresenceViewModel.FromPresence(recipient.Presence),
                        LastMsgId = dm.LastMessageId ?? dm.Id,
                        Id = dm.Id,
                        IsSelected = existingItem?.IsSelected ?? false,
                        IsGroupChat = isGroupChat,
                        RecipientCount = dm.Recipients.Count + 1, // to account for ourselves, i think?
                        AvatarUrl = isGroupChat
                            ? (dm.IconUrl ?? recipient.AvatarUrl ?? recipient.DefaultAvatarUrl ?? "")
                            : (recipient.AvatarUrl ?? recipient.DefaultAvatarUrl ?? ""),
                        ListSection = ContactListSectionKind.Conversations,
                    };

                    if (dm?.Recipients is not null) foreach (DiscordUser user in dm.Recipients)
                        newItem.Recipients.Add(user);

                    ulong selfId = Discord.Client.CurrentUser.Id;
                    if (!isGroupChat)
                    {
                        var other = dm.Recipients.FirstOrDefault(r => r.Id != selfId);
                        newItem.IsBlocked = other is not null && DiscordRelationshipHelper.IsUserBlocked(Discord.Client, other.Id);
                    }
                    else
                        newItem.IsBlocked = false;

                    newList.Add(newItem);
                }

                // Sort the new list based on the last message time
                newList.Sort((x, y) =>
                {
                    long prevTimestamp = ((long)(x.LastMsgId >> 22)) + 1420070400000;
                    DateTime prevLastMessageTime = DateTimeOffset.FromUnixTimeMilliseconds(prevTimestamp).DateTime;

                    long nextTimestamp = ((long)(y.LastMsgId >> 22)) + 1420070400000;
                    DateTime nextLastMessageTime = DateTimeOffset.FromUnixTimeMilliseconds(nextTimestamp).DateTime;

                    return nextLastMessageTime.CompareTo(prevLastMessageTime);
                });

                var itemsToRemove = oldList.Where(oldItem => !newList.Any(newItem => newItem.Id == oldItem.Id)).ToList();
                foreach (var itemToRemove in itemsToRemove)
                {
                    oldList.Remove(itemToRemove); // Remove items that are no longer in the new list
                }

                // Update or add new items, maintaining the sorted order
                foreach (var newItem in newList)
                {
                    var existingItem = oldList.FirstOrDefault(v => v.Id == newItem.Id);
                    if (existingItem != null)
                    {
                        existingItem.Name = newItem.Name;
                        existingItem.LastMsgId = newItem.LastMsgId;
                        existingItem.IsSelected = newItem.IsSelected;
                        existingItem.Presence = newItem.Presence;
                        existingItem.AvatarUrl = newItem.AvatarUrl;
                        existingItem.ListSection = ContactListSectionKind.Conversations;
                        existingItem.IsBlocked = newItem.IsBlocked;
                    }
                    else
                    {
                        // Add new item to the old list in the correct sorted order
                        oldList.Add(newItem);
                    }
                }

                // if the resulting lists r the same, return to prevent flickers

                bool isSame = false;
                if (oldList.Count == newList.Count)
                {
                    isSame = true;
                    for (int i = 0; i < oldList.Count; i++)
                    {
                        if (oldList[i].Id != newList[i].Id)
                        {
                            isSame = false;
                            break;
                        }
                    }
                }

                if (isSame)
                {
                    // List unchanged; still refresh favorites so loaded-from-config favorites appear
                    RefreshFavoritesCategory();
                }
                else
                {
                    ViewModel.Categories[1].Items.Clear();
                    foreach (var item in newList)
                    {
                        ViewModel.Categories[1].Items.Add(item);
                    }
                    RefreshFavoritesCategory();
                }
            });
        }

        private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Tab)
                e.Handled = true;
            else if (e.Key == System.Windows.Input.Key.Escape)
            {
                if (SearchInput.IsFocused)
                {
                    SearchInput.Text = "";
                    SearchInput.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
                    e.Handled = true;
                }
                else if (PART_StatusInputBox.IsFocused)
                {
                    PART_StatusInputBox.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
                    e.Handled = true;
                }
            }
            else if (e.Key == Key.Enter)
            {
                if (PART_StatusInputBox.IsFocused)
                {
                    PART_StatusInputBox.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
                    e.Handled = true;
                }
            }
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            var app = (App)Application.Current;
            if (app.LoggingOut) return;
            foreach (Window window in Application.Current.Windows)
            {
                if (window != this)
                    window.Close();
            }
        }

        public void SetVisibleProperty(bool prop)
        {
            foreach (var item in ViewModel.Categories)
            {
                item.IsVisibleProperty = prop;
            }

            ViewModel.IsVisible = prop;
        }

        private void HomeListView_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                MouseEnter += (s, e) => SetVisibleProperty(true);
                MouseLeave += (s, e) =>
                {
                    if (!IsActive) SetVisibleProperty(false);
                };
                Activated += (s, e) => SetVisibleProperty(true);
                Deactivated += (s, e) => SetVisibleProperty(false);
            }
            catch (Exception)
            {

            }
        }

        private void ItemToggleCollapse(object sender, MouseButtonEventArgs e)
        {
            var item = (HomeListViewCategory)((FrameworkElement)sender).DataContext;
            item.Collapsed = !item.Collapsed;
        }

        private void ItemClick(object sender, MouseButtonEventArgs e)
        {
            // set all items to not selected
            foreach (var i in ViewModel.Categories)
            {
                i.IsSelected = false;
                foreach (var x in i.Items)
                {
                    x.IsSelected = false;
                }
            }
            // get the data context of the clicked item
            var item = (dynamic)((Grid)sender).DataContext;
            if (item is HomeListViewCategory)
            {
                item.IsSelected = true;
            }
            else if (item is HomeListItemViewModel)
            {
                item.IsSelected = true;
            }
        }

        private void ItemContextMenu_Opening(object sender, ContextMenuEventArgs e)
        {
            if (sender is not Button button || button.DataContext is not HomeListItemViewModel listItem)
                return;
            var contextMenu = button.ContextMenu;
            if (contextMenu?.Items.Count < 2) return;
            var favoriteItem = contextMenu.Items[0] as MenuItem;
            var unfavoriteItem = contextMenu.Items[1] as MenuItem;
            if (favoriteItem == null || unfavoriteItem == null) return;
            // Use Id comparison: Favorites category contains clones, so reference equality would be wrong
            var inFavorites = ViewModel.Categories.Count > 0 && ViewModel.Categories[0].Items.Any(i => i.Id == listItem.Id);
            var alreadyFavorited = SettingsManager.Instance.FavoriteConversationIds.Contains(listItem.Id)
                || SettingsManager.Instance.FavoriteGuildIds.Contains(listItem.Id);
            favoriteItem.Visibility = inFavorites || alreadyFavorited ? Visibility.Collapsed : Visibility.Visible;
            unfavoriteItem.Visibility = inFavorites || alreadyFavorited ? Visibility.Visible : Visibility.Collapsed;

            if (contextMenu.Items.Count > 2 && contextMenu.Items[2] is MenuItem closeItem)
            {
                var client = Discord.Client;
                bool isDm = client?.PrivateChannels.ContainsKey(listItem.Id) == true;
                closeItem.Visibility = isDm ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void FavoriteMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var item = GetListItemFromContextMenuSender(sender);
            if (item == null) return;
            // Category 0 = Favorites, 1 = Conversations, 2+ = Servers/folders
            var inConversations = ViewModel.Categories.Count > 1 && ViewModel.Categories[1].Items.Contains(item);
            if (inConversations)
            {
                if (!SettingsManager.Instance.FavoriteConversationIds.Contains(item.Id))
                {
                    SettingsManager.Instance.FavoriteConversationIds.Add(item.Id);
                    SettingsManager.Save();
                    RefreshFavoritesCategory();
                }
            }
            else
            {
                if (!SettingsManager.Instance.FavoriteGuildIds.Contains(item.Id))
                {
                    SettingsManager.Instance.FavoriteGuildIds.Add(item.Id);
                    SettingsManager.Save();
                    RefreshFavoritesCategory();
                }
            }
        }

        private void UnfavoriteMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var item = GetListItemFromContextMenuSender(sender);
            if (item == null) return;
            SettingsManager.Instance.FavoriteConversationIds.Remove(item.Id);
            SettingsManager.Instance.FavoriteGuildIds.Remove(item.Id);
            SettingsManager.Save();
            RefreshFavoritesCategory();
        }

        private async void CloseConversationMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var item = GetListItemFromContextMenuSender(sender);
            if (item is null) return;
            var client = Discord.Client;
            if (client is null) return;
            if (!client.PrivateChannels.TryGetValue(item.Id, out var dm))
                return;

            var loc = LocalizationManager.Instance;
            try
            {
                await dm.DeleteAsync().ConfigureAwait(true);

                bool settingsDirty = false;
                if (SettingsManager.Instance.FavoriteConversationIds.Remove(item.Id))
                    settingsDirty = true;
                if (SettingsManager.Instance.RecentDMChats.Remove(item.Id))
                    settingsDirty = true;
                if (SettingsManager.Instance.LastReadMessages.Remove(item.Id))
                    settingsDirty = true;
                if (settingsDirty)
                    SettingsManager.Save();

                RefreshFavoritesCategory();
                UpdateStatuses();
            }
            catch (Exception ex)
            {
                new Dialog(loc["HomeNoticeTitle"], ex.Message, SystemIcons.Error) { Owner = this }.ShowDialog();
            }
        }

        private static HomeListItemViewModel? GetListItemFromContextMenuSender(object sender)
        {
            if (sender is not MenuItem menuItem) return null;
            var contextMenu = menuItem.Parent as ContextMenu;
            var target = contextMenu?.PlacementTarget as Button;
            return target?.DataContext as HomeListItemViewModel;
        }

        private async void Button_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var item = (HomeListItemViewModel)((Button)sender).DataContext;
            // is a window already open for this item?
            Chat? chat = Application.Current.Windows.OfType<Chat>().FirstOrDefault(x => 
                x?.ViewModel?.Recipient?.Id == item.Id || 
                x?.Channel?.Id == item.Id || 
                (x?.Channel?.Guild?.Channels.Values?.Select(x => x.Id)?.Contains(item.Id) ?? false));
            if (chat is null)
            {
                // We send over the presence of the item in case this is a one-on-one DM, where the Discord
                // API doesn't initially report this state.
                new Chat(item.Id, true, item.Presence, item);
            }
            else
            {
                // move the chat to the center of this window
                var rect = chat.RestoreBounds;

                // Avoid infinity values to avoid an ArgumentException.
                if (rect.Width == double.NegativeInfinity ||
                    rect.Width == double.PositiveInfinity)
                {
                    rect = new Rect(0, 0, 100, 100);
                }

                if (rect.Height == double.NegativeInfinity ||
                    rect.Height == double.PositiveInfinity)
                {
                    rect = new Rect(0, 0, 100, 100);
                }

                chat.Left = Left + (Width - rect.Width) / 2;
                chat.Top = Top + (Height - rect.Height) / 2;
                await chat.ExecuteNudgePrettyPlease(chat.Left, chat.Top, 0.5, 15);
            }
        }

        private NonNativeTooltip? tooltip;
        private HomeListItemViewModel? _lastHoveredItem;
        private Button? _lastHoveredControl;

        private void MouseEnteredUser(object sender, MouseEventArgs e)
        {
            var item = (HomeListItemViewModel)((FrameworkElement)sender).DataContext;
            if (!item.IsSelected) return;
            if (_lastHoveredItem == item) return;
            _lastHoveredItem = item;
            // traverse the parents till we find a Button
            var frameworkElement = sender as FrameworkElement;
            while (frameworkElement != null && frameworkElement is not Button)
                frameworkElement = VisualTreeHelper.GetParent(frameworkElement) as FrameworkElement;
            if (frameworkElement is not Button btn)
                return;
            _lastHoveredControl = btn;

            _hoverTimer.Stop();
            _hoverTimer.Start();
            tooltip?.StopKillTimer();
        }

        private void MouseExitedUser(object sender, MouseEventArgs e)
        {
            var frameworkElement = sender as FrameworkElement;
            if (frameworkElement?.DataContext is HomeListItemViewModel item)
            {
                _hoverTimer.Stop();
                tooltip?.StartKillTimer();
            }
        }

        private void OnTimerEnd(object? sender, System.Timers.ElapsedEventArgs e)
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (_lastHoveredControl is null)
                    return;

                // if there's a tooltip already open, close it
                tooltip?.Close();
                tooltip = new(new()
                {
                    new()
                    {
                        Name = "Block this user",
                        Key = "block"
                    },

                    new()
                    {
                        Name = "Send an instant message",
                        Key = "msg"
                    }
                });

                tooltip.ItemClicked += (s, e) =>
                {
                    Debug.WriteLine(e.Item.Key);
                };

                tooltip.Closed += (s, e) =>
                {
                    tooltip = null;
                };

                // get the position of the 

                tooltip.Loaded += (s, e) =>
                {
                    var pos = _lastHoveredControl.PointToScreen(new System.Windows.Point(0, 0));
                    tooltip.Left = pos.X - tooltip.Width - 56;
                    tooltip.Top = pos.Y;
                };

                tooltip.Show();
            });
        }

        private void Image_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            try // OmegaAOL - wrap in try catch, issue #141
            {
                UriBuilder builder = new(ViewModel.Ad.Url);
                var segments = builder.Path.Split('/');
                if (builder.Host == "web.archive.org" && segments.Length > 2 && !segments[2].EndsWith("if_"))
                {
                    segments[2] += "if_";
                }
                builder.Path = string.Join("/", segments);
                var uri = builder.Uri;
                Process.Start(new ProcessStartInfo(uri.ToString()) { UseShellExecute = true });
            }
            catch (Exception ex) { Console.WriteLine("Ad link launch failed with: " + ex.Message); }
        }

        private void NameDropdown_Click(object sender, RoutedEventArgs e)
        {
            var contextMenu = ((Button)sender).ContextMenu;
            contextMenu.PlacementTarget = (Button)sender;
            contextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            contextMenu.IsOpen = true;
        }

        private async void Available_Click(object sender, RoutedEventArgs e)
        {
            await App.SetStatus(UserStatus.Online);
        }

        private async void Busy_Click(object sender, RoutedEventArgs e)
        {
            await App.SetStatus(UserStatus.DoNotDisturb);
        }

        private async void Away_Click(object sender, RoutedEventArgs e)
        {
            await App.SetStatus(UserStatus.Idle);
        }

        private async void AppearOffline_Click(object sender, RoutedEventArgs e)
        {
            await App.SetStatus(UserStatus.Invisible);
        }

        private void OptionsBtn_Click(object sender, RoutedEventArgs e)
        {
            var settings = new Settings();
            settings.Owner = this;
            settings.ShowDialog();
        }

        private void ChangeLayoutButton_Click(object sender, MouseButtonEventArgs e)
        {
            var settings = new Settings("Disposition");
            settings.Owner = this;
            settings.ShowDialog();
        }

        private void ShowUnimplementedDialog()
        {
            var loc = LocalizationManager.Instance;
            var dialog = new Dialog(
                loc["ChatToolbarErrorTitle"],
                loc["ChatToolbarErrorUnimplemented"],
                SystemIcons.Error
            );
            dialog.Owner = this;
            dialog.ShowDialog();
        }

        private void MailButton_Click(object sender, MouseButtonEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo("mailto:") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                DiagnosticsLog.Swallowed("Home.MailButton_Click: open default mail", ex);
            }
        }

        private void AddContactButton_Click(object sender, MouseButtonEventArgs e)
        {
            var friends = new FriendsWindow { Owner = this };
            friends.Show();
        }

        private void UnimplementedMenuItem_Click(object sender, RoutedEventArgs e)
        {
            ShowUnimplementedDialog();
        }

        private void ShowMenuButton_Click(object sender, MouseButtonEventArgs e)
        {
            var element = (FrameworkElement)sender;

            MenuItem MakeItem(string header, RoutedEventHandler? onClick = null, bool isEnabled = true)
            {
                var item = new MenuItem { Header = header, IsEnabled = isEnabled };
                if (onClick != null) item.Click += onClick;
                return item;
            }

            RoutedEventHandler unimplemented = (_, _) => ShowUnimplementedDialog();

            var menu = new ContextMenu();
            var loc = LocalizationManager.Instance;

            // File
            var fileMenu = MakeItem(loc["HomeMenuFile"]);
            fileMenu.Items.Add(MakeItem(loc["HomeMenuSendInstantMessage"], unimplemented));
            fileMenu.Items.Add(MakeItem(loc["HomeMenuOpenReceivedFiles"], unimplemented));
            fileMenu.Items.Add(new Separator());
            fileMenu.Items.Add(MakeItem(loc["SignOut"], (_, _) => _ = ((App)Application.Current).SignOut()));
            fileMenu.Items.Add(new Separator());
            fileMenu.Items.Add(MakeItem(loc["HomeMenuClose"], (_, _) => Application.Current.Shutdown()));
            menu.Items.Add(fileMenu);

            // Edit
            var editMenu = MakeItem(loc["HomeMenuEdit"]);
            editMenu.Items.Add(MakeItem(loc["HomeMenuFindContactOrPhone"], unimplemented));
            editMenu.Items.Add(new Separator());
            editMenu.Items.Add(MakeItem(loc["HomeMenuCopyMyContactAddress"], unimplemented));
            editMenu.Items.Add(MakeItem(loc["HomeMenuSelectAll"], unimplemented));
            menu.Items.Add(editMenu);

            // View
            var viewMenu = MakeItem(loc["HomeMenuView"]);
            var layoutItem = MakeItem(loc["HomeChangeContactListLayout"],
                (_, _) =>
                {
                    var s = new Settings("Disposition");
                    s.Owner = this;
                    s.ShowDialog();
                });
            viewMenu.Items.Add(layoutItem);
            viewMenu.Items.Add(new Separator());
            viewMenu.Items.Add(MakeItem(loc["HomeMenuSortByName"], unimplemented));
            viewMenu.Items.Add(MakeItem(loc["HomeMenuSortByStatus"], unimplemented));
            menu.Items.Add(viewMenu);

            // Actions
            var actionsMenu = MakeItem(loc["HomeMenuActions"]);
            actionsMenu.Items.Add(MakeItem(loc["HomeMenuSendInstantMessage"], unimplemented));
            actionsMenu.Items.Add(MakeItem(loc["HomeMenuSendFileOrPhoto"], unimplemented));
            actionsMenu.Items.Add(new Separator());
            actionsMenu.Items.Add(MakeItem(loc["HomeMenuViewProfile"], unimplemented));
            menu.Items.Add(actionsMenu);

            // Tools
            var toolsMenu = MakeItem(loc["HomeMenuTools"]);
            toolsMenu.Items.Add(MakeItem(loc["HomeMenuAudioVideoSetup"], unimplemented));
            toolsMenu.Items.Add(new Separator());
            toolsMenu.Items.Add(MakeItem(loc["HomeOptions"], (_, _) => { var s = new Settings(); s.Owner = this; s.ShowDialog(); }));
            menu.Items.Add(toolsMenu);

            // Help
            var helpMenu = MakeItem(loc["HomeMenuHelpMenu"]);
            helpMenu.Items.Add(MakeItem(loc["HomeMenuAerochatHelp"], (_, _) =>
                Process.Start(new ProcessStartInfo(AEROCHAT_HELP_WIKI_URL) { UseShellExecute = true })));
            helpMenu.Items.Add(new Separator());
            helpMenu.Items.Add(MakeItem(loc["HomeMenuAboutAerochat"], (s, _) => CreditsBtn_Click(s, new RoutedEventArgs())));
            menu.Items.Add(helpMenu);

            menu.PlacementTarget = element;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.IsOpen = true;
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            // get the data context of the clicked item
            var item = (HomeButtonViewModel)((Button)sender).DataContext;
            // get the one in ViewModel.Buttons
            var button = ViewModel.Buttons.FirstOrDefault(x => x == item);
            // run the click action
            button?.Click?.Invoke();
        }

        private async void SignOut_Click(object sender, RoutedEventArgs e)
        {
            var app = (App)Application.Current;
            await app.SignOut();
        }

        private void Grid_MouseEnter(object sender, MouseEventArgs e)
        {
            SceneTileImage.Image = new BitmapImage(new Uri("pack://application:,,,/Aerochat;component/Resources/Home/PageOpen.png"));
            SceneTileImage.Reset();
            Debug.WriteLine("Enter");
        }

        private void Grid_MouseLeave(object sender, MouseEventArgs e)
        {
            SceneTileImage.Image = new BitmapImage(new Uri("pack://application:,,,/Aerochat;component/Resources/Home/PageClose.png"));
            SceneTileImage.Reset();
            Debug.WriteLine("Exit");
        }

        private void SceneTileImage_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            new ChangeScene().ShowDialog();
        }

        private void CreditsBtn_Click(object sender, RoutedEventArgs e)
        {
            new About().ShowDialog();
        }

        private void PreviousNewsItem_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel.CurrentNews is null || ViewModel.News.Count == 0) return;
            var index = ViewModel.News.IndexOf(ViewModel.CurrentNews);
            SetNews(ViewModel.News[(index - 1 + ViewModel.News.Count) % ViewModel.News.Count]);
        }

        private void NextNewsItem_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel.CurrentNews is null || ViewModel.News.Count == 0) return;
            var index = ViewModel.News.IndexOf(ViewModel.CurrentNews);
            SetNews(ViewModel.News[(index + 1) % ViewModel.News.Count]);
        }

        private void StatusDropdown_Click(object sender, RoutedEventArgs e)
        {
            if (DiscordUserSettingsManager.Instance.UserSettingsProto?.Status.CustomStatus == null)
            {
                PART_StatusInputBox.Text = "";
            }
            else
            {
                PART_StatusInputBox.Text = PART_StatusStaticView.Text;
            }

            ViewModel.IsEditingStatus = true;

            PART_StatusInputBox.Focus();
        }

        private void PART_StatusInputBox_LostFocus(object sender, RoutedEventArgs e)
        {
            OnStatusInputBoxLostFocus();
        }

        private void OnStatusInputBoxLostFocus()
        {
            ViewModel.IsEditingStatus = false;

            if (PART_StatusInputBox.Text != PART_StatusStaticView.Text && DiscordUserSettingsManager.Instance.UserSettingsProto != null &&
                ViewModel.CurrentUser.Presence != null && !(ViewModel.CurrentUser.Presence.CustomStatus == null && PART_StatusInputBox.Text == ""))
            {
                // Update the text for the brief period before the remote text is updated.
                ViewModel.CurrentUser.Presence!.CustomStatus = PART_StatusInputBox.Text;

                if (PART_StatusInputBox.Text == "")
                {
                    // Revert to the placeholder text:
                    ViewModel.CurrentUser.Presence.CustomStatus = null;

                    DiscordUserSettingsManager.Instance.UserSettingsProto.Status.CustomStatus = null;
                }
                else
                {
                    string croppedText = PART_StatusInputBox.Text;

                    // Ensure that the text fits into the limit given by Discord.
                    if (croppedText.Length > 128)
                    {
                        croppedText = croppedText.Substring(0, 128);
                    }

                    if (DiscordUserSettingsManager.Instance.UserSettingsProto.Status.CustomStatus == null)
                    {
                        DiscordUserSettingsManager.Instance.UserSettingsProto.Status.CustomStatus = new();
                    }

                    DiscordUserSettingsManager.Instance.UserSettingsProto.Status.CustomStatus.Text = PART_StatusInputBox.Text;
                }

                _ = DiscordUserSettingsManager.Instance.UpdateRemote();
            }
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            IInputElement? focusedElement = Keyboard.FocusedElement;
            Keyboard.ClearFocus();

            if (focusedElement != null)
                focusedElement.RaiseEvent(new RoutedEventArgs(LostFocusEvent));
        }


        private void OnDoubleClickTreeViewExpander(object sender, MouseButtonEventArgs e)
        {
            // In order to avoid double actions from occurring when the expander button is clicked (which
            // takes action after a single input), we ignore clicks going to that area. As an easy hack,
            // we just hit test and ignore the action if the mouse is not over the expander button.
            FrameworkElement? expanderButton = ((FrameworkElement)sender).FindName("PART_ExpanderButton") as FrameworkElement;

            bool isInExpanderButton = false;

            if (expanderButton != null)
            {
                HitTestResult? hitTest = VisualTreeHelper.HitTest(this, e.GetPosition(this));

                if (hitTest?.VisualHit == expanderButton)
                {
                    isInExpanderButton = true;
                }
            }

            if (e.ClickCount == 2 && !e.Handled && !isInExpanderButton)
            {
                ItemToggleCollapse(sender, e);
            }
        }
    }
}

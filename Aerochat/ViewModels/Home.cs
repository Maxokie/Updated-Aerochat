using Aerochat.Settings;
using static Aerochat.ViewModels.HomeListViewCategory;
using Aerochat.Theme;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace Aerochat.ViewModels
{
    public class HomeWindowViewModel : ViewModelBase
    {
        private readonly DispatcherTimer _searchDebounceTimer;
        private const int DEBOUNCE_INTERVAL_MS = 150;

        public HomeWindowViewModel()
        {
            Notices.CollectionChanged += (_, _) => InvokePropertyChanged(nameof(CurrentNotice));
            // Initialize filtered categories
            FilteredCategories = new ObservableCollection<HomeListViewCategory>();
            Categories.CollectionChanged += (s, e) => UpdateFilteredCategories();

            // Initialize debounce timer
            _searchDebounceTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(DEBOUNCE_INTERVAL_MS)
            };
            _searchDebounceTimer.Tick += (s, e) =>
            {
                _searchDebounceTimer.Stop();
                UpdateFilteredCategories();
            };

        }

        private bool _showEyecandy = true;

        public bool ShowEyecandy
        {
            get => _showEyecandy;
            set => SetProperty(ref _showEyecandy, value);
        }

        private string _searchText = "";
        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value))
                {
                    // Reset and restart the debounce timer
                    _searchDebounceTimer.Stop();
                    _searchDebounceTimer.Start();
                }
            }
        }

        public ObservableCollection<HomeListViewCategory> FilteredCategories { get; }

        /// <summary>Set by the home window so search-filtered categories can show correct (online/total) counts.</summary>
        public Func<HomeListItemViewModel, bool>? EvaluateRowForCategoryHeader { get; set; }

        /// <summary>Total guilds the user is in (for the Servers header).</summary>
        public Func<int>? GetGuildTotalForHeader { get; set; }

        private static bool ShouldShowHomeCategory(HomeListViewCategory cat)
        {
            if (cat.Name == "Favorites") return SettingsManager.Instance.HomeShowFavorites;
            if (cat.Name == "Conversations") return SettingsManager.Instance.HomeShowConversations;
            return SettingsManager.Instance.HomeShowServers;
        }

        public void UpdateFilteredCategories()
        {
            if (string.IsNullOrWhiteSpace(SearchText))
            {
                // Fast path for empty search - just reference existing categories
                FilteredCategories.Clear();
                foreach (var category in Categories)
                {
                    if (!ShouldShowHomeCategory(category)) continue;
                    FilteredCategories.Add(category);
                }
                return;
            }

            var searchTerms = SearchText.ToLower().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (searchTerms.Length == 0)
            {
                return;
            }

            FilteredCategories.Clear();
            var reusableCategory = new HomeListViewCategory();

            foreach (var category in Categories)
            {
                if (!ShouldShowHomeCategory(category)) continue;
                // Reuse the same category object and just update its properties
                reusableCategory.Name = category.Name;
                reusableCategory.CategoryHeaderText = category.CategoryHeaderText;
                reusableCategory.CategoryHeaderCountSuffix = category.CategoryHeaderCountSuffix;
                reusableCategory.IsVisibleProperty = category.IsVisibleProperty;
                reusableCategory.Collapsed = category.Collapsed;
                reusableCategory.IsSelected = category.IsSelected;
                reusableCategory.Items.Clear();

                bool hasMatchingItems = false;
                foreach (var item in category.Items)
                {
                    if (MatchesSearchTerms(item, searchTerms))
                    {
                        reusableCategory.Items.Add(item);
                        hasMatchingItems = true;
                    }
                }

                if (hasMatchingItems)
                {
                    // Only create a new category if matches are found
                    var newCategory = new HomeListViewCategory
                    {
                        Name = reusableCategory.Name,
                        CategoryHeaderText = reusableCategory.CategoryHeaderText,
                        CategoryHeaderCountSuffix = reusableCategory.CategoryHeaderCountSuffix,
                        IsVisibleProperty = reusableCategory.IsVisibleProperty,
                        Collapsed = reusableCategory.Collapsed,
                        IsSelected = reusableCategory.IsSelected
                    };
                    foreach (var item in reusableCategory.Items)
                    {
                        newCategory.Items.Add(item);
                    }

                    if (EvaluateRowForCategoryHeader != null && GetGuildTotalForHeader != null)
                    {
                        HomeListViewCategory.ApplyHeaderCounts(newCategory, GetGuildTotalForHeader(), EvaluateRowForCategoryHeader);
                    }

                    FilteredCategories.Add(newCategory);
                }
            }
        }

        private static bool MatchesSearchTerms(HomeListItemViewModel item, string[] searchTerms)
        {
            var name = item.Name?.ToLower() ?? "";
            var status = item.Presence?.Status?.ToLower() ?? "";
            var presence = item.Presence?.Presence?.ToLower() ?? "";

            return searchTerms.All(term =>
                name.Contains(term) ||
                status.Contains(term) ||
                presence.Contains(term));
        }

        private UserViewModel _currentUser = new()
        {
            Avatar = "/Aerochat;component/Resources/Frames/PlaceholderPfp.png",
            Id = 0,
            Name = "nullptr",
            Username = "notnullptr"
        };

        public UserViewModel CurrentUser
        {
            get => _currentUser;
            set => SetProperty(ref _currentUser, value);
        }

        private bool _isEditingStatus = false;

        public bool IsEditingStatus
        {
            get => _isEditingStatus;
            set => SetProperty(ref _isEditingStatus, value);
        }

        public ObservableCollection<HomeListViewCategory> Categories { get; } = new();

        private AdViewModel _ad = new();
        public AdViewModel Ad
        {
            get => _ad;
            set => SetProperty(ref _ad, value);
        }

        public ObservableCollection<HomeButtonViewModel> Buttons { get; } = new();

        public ThemeService Theme { get; } = ThemeService.Instance;

        public ObservableCollection<NoticeViewModel> Notices { get; } = new();

        public NoticeViewModel? CurrentNotice => Notices.FirstOrDefault();

        private bool _isVisible = true;
        public bool IsVisible
        {
            get => _isVisible;
            set => SetProperty(ref _isVisible, value);
        }

        public ObservableCollection<NewsViewModel> News { get; } = new();
        private NewsViewModel? _currentNews;
        public NewsViewModel? CurrentNews
        {
            get => _currentNews;
            set => SetProperty(ref _currentNews, value);
        }
    }
}

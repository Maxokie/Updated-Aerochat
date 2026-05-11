using Aerochat.Controls;
using Aerochat.Localization;
using Aerochat.Settings;
using Aerochat.Theme;
using Aerochat.Voice;
using Aerochat.Windows;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Aerochat.ViewModels
{
    public enum TargetMessageMode
    {
        None,
        Edit,
        Reply,
    }

    public enum DrawingTool
    {
        Pen,      // ペン
        Kesigomu, // 消しゴム
    }

    public class ChatWindowViewModel : ViewModelBase
    {
        public List<ToolbarItem> ToolbarItems { get; set; } = new()
        {
            new(LocalizationManager.Instance["ChatToolbarPhotos"], (FrameworkElement itemElement) =>
            {
                Debug.WriteLine("Photos clicked");

                Chat? chat = Window.GetWindow(itemElement) as Chat;
                if (chat != null)
                {
                    chat.OpenAttachmentsFilePicker();
                }
            }, false, LocalizationManager.Instance["ChatToolbarPhotosTooltip"]),
            new(LocalizationManager.Instance["ChatToolbarFiles"], (FrameworkElement itemElement) =>
            {
                Debug.WriteLine("Files clicked");

                Chat? chat = Window.GetWindow(itemElement) as Chat;
                if (chat != null)
                {
                    chat.OpenAttachmentsFilePicker();
                }
            }, false, LocalizationManager.Instance["ChatToolbarFilesTooltip"]),
            new(LocalizationManager.Instance["ChatToolbarGames"], (FrameworkElement itemElement) =>
            {
                Debug.WriteLine("Games clicked");
                OnUmimplementedAction(itemElement);
            }, true),
            new(LocalizationManager.Instance["ChatToolbarActivities"], (FrameworkElement itemElement) =>
            {
                Debug.WriteLine("Activities clicked");
                OnUmimplementedAction(itemElement);
            }, true),
            new(LocalizationManager.Instance["ChatToolbarInvite"], (FrameworkElement itemElement) =>
            {
                Debug.WriteLine("Invite clicked");
                OnUmimplementedAction(itemElement);
            }, true),
            new(LocalizationManager.Instance["ChatToolbarBlock"], (FrameworkElement itemElement) =>
            {
                if (Window.GetWindow(itemElement) is Chat chat)
                    chat.ShowBlockToolbarMenu(itemElement);
            }, true, LocalizationManager.Instance["ChatToolbarBlockTooltip"])
        };

        /// <summary>
        /// Shows a dialog stating that the toolbar action is unimplemented.
        /// </summary>
        private static void OnUmimplementedAction(FrameworkElement itemElement)
        {
            var loc = LocalizationManager.Instance;
            Dialog dialog = new(
                loc["ChatToolbarErrorTitle"],
                loc["ChatToolbarErrorUnimplemented"],
                SystemIcons.Error
            );
            dialog.Owner = Window.GetWindow(itemElement);
            dialog.ShowDialog();
        }

        private ObservableCollection<MessageViewModel> _messages = new();

        /// <summary>Chat message list; replacing the collection swaps the instance (see <c>Chat</c> channel load).</summary>
        public ObservableCollection<MessageViewModel> Messages
        {
            get => _messages;
            set => SetProperty(ref _messages, value);
        }

        private string _callStatusText = "";
        public string CallStatusText
        {
            get => _callStatusText;
            set => SetProperty(ref _callStatusText, value);
        }

        private int _topHeight = 80;

        private bool _showEyecandy = true;

        public bool ShowEyecandy
        {
            get => _showEyecandy;
            set => SetProperty(ref _showEyecandy, value);
        }

        public int TopHeight
        {
            get => _topHeight;
            set => SetProperty(ref _topHeight, value);
        }

        private int _bottomHeight = 64;

        public int BottomHeight
        {
            get => _bottomHeight;
            set => SetProperty(ref _bottomHeight, value);
        }

        private string _adText = "AIM access for Escargot users only $5!";

        public string AdText
        {
            get => _adText;
            set => SetProperty(ref _adText, value);
        }

        private ChannelViewModel _channel = new()
        {
            Name = LocalizationManager.Instance["ChatLoadingChannel"],
            Id = 0,
            Topic = LocalizationManager.Instance["ChatLoadingChannelWait"],
        };
        public ChannelViewModel Channel
        {
            get => _channel;
            set => SetProperty(ref _channel, value);
        }

        private bool _isDM = false;
        public bool IsDM
        {
            get => _isDM;
            set => SetProperty(ref _isDM, value);
        }

        private UserViewModel? _recipient;

        public UserViewModel? Recipient
        {
            get => _recipient;
            set => SetProperty(ref _recipient, value);
        }

        private MessageViewModel? _lastReceivedMessage;

        public MessageViewModel? LastReceivedMessage
        {
            get => _lastReceivedMessage;
            set => SetProperty(ref _lastReceivedMessage, value);
        }

        private UserViewModel? _currentUser;

        public UserViewModel? CurrentUser
        {
            get => _currentUser;
            set => SetProperty(ref _currentUser, value);
        }

        private string _typingString = "";

        public string TypingString
        {
            get => _typingString;
            set => SetProperty(ref _typingString, value);
        }

        private int _topHeightMinus10 = 70;

        public int TopHeightMinus10
        {
            get => _topHeightMinus10;
            set => SetProperty(ref _topHeightMinus10, value);
        }

        public ThemeService Theme { get; } = ThemeService.Instance;

        public ObservableCollection<HomeListViewCategory> Categories { get; } = [];

        private bool _loading = false;

        public bool Loading
        {
            get => _loading;
            set => SetProperty(ref _loading, value);
        }

        private GuildViewModel? _guild;

        public GuildViewModel? Guild
        {
            get => _guild;
            set => SetProperty(ref _guild, value);
        }

        private bool _isGroupChat;
        public bool IsGroupChat
        {
            get => _isGroupChat;
            set => SetProperty(ref _isGroupChat, value);
        }

        public VoiceManager VoiceManager { get; } = VoiceManager.Instance;

        private MessageViewModel? _editingMessage;

        public MessageViewModel? TargetMessage
        {
            get => _editingMessage;
            set => SetProperty(ref _editingMessage, value);
        }

        private TargetMessageMode _messageTargetMode;

        public TargetMessageMode MessageTargetMode
        {
            get => _messageTargetMode;
            set => SetProperty(ref _messageTargetMode, value);
        }

        private bool _isShowingAttachmentEditor = false;
        public bool IsShowingAttachmentEditor
        {
            get => _isShowingAttachmentEditor;
            set => SetProperty(ref _isShowingAttachmentEditor, value);
        }

        private DrawingTool _drawingTool = DrawingTool.Pen;

        public DrawingTool DrawingTool
        {
            get => _drawingTool;
            set => SetProperty(ref _drawingTool, value);
        }


        private bool _undoEnabled = false;
        public bool UndoEnabled
        {
            get => _undoEnabled;
            set => SetProperty(ref _undoEnabled, value);
        }

        private bool _redoEnabled = false;
        public bool RedoEnabled
        {
            get => _redoEnabled;
            set => SetProperty(ref _redoEnabled, value);
        }

        private bool _blockedUserWarningDismissed;

        /// <summary>User closed the yellow "blocked user" banner for this session in this window.</summary>
        public bool BlockedUserWarningDismissed
        {
            get => _blockedUserWarningDismissed;
            set => SetProperty(ref _blockedUserWarningDismissed, value);
        }

        private bool _hasBlockedUserInConversation;

        /// <summary>1:1 DM with blocked recipient, or group DM with at least one blocked member.</summary>
        public bool HasBlockedUserInConversation
        {
            get => _hasBlockedUserInConversation;
            set => SetProperty(ref _hasBlockedUserInConversation, value);
        }

        [DependsOn(nameof(BlockedUserWarningDismissed))]
        [DependsOn(nameof(HasBlockedUserInConversation))]
        public bool ShowBlockedUserWarning => !BlockedUserWarningDismissed && HasBlockedUserInConversation;

        private bool _isServerMemberPanelOpen;

        /// <summary>Guild chat only: right-hand member list pane is expanded.</summary>
        public bool IsServerMemberPanelOpen
        {
            get => _isServerMemberPanelOpen;
            set => SetProperty(ref _isServerMemberPanelOpen, value);
        }

        /// <summary>Guild members (online) when the member pane is open.</summary>
        public ObservableCollection<UserViewModel> ServerMemberListOnline { get; } = new();

        /// <summary>Guild members (offline) when the member pane is open.</summary>
        public ObservableCollection<UserViewModel> ServerMemberListOffline { get; } = new();

        private string _serverMemberOnlineSectionLabel = "";

        /// <summary>Localized &quot;Online (n)&quot; heading for the member list.</summary>
        public string ServerMemberOnlineSectionLabel
        {
            get => _serverMemberOnlineSectionLabel;
            set => SetProperty(ref _serverMemberOnlineSectionLabel, value);
        }

        private string _serverMemberOfflineSectionLabel = "";

        /// <summary>Localized &quot;Offline (n)&quot; heading for the member list.</summary>
        public string ServerMemberOfflineSectionLabel
        {
            get => _serverMemberOfflineSectionLabel;
            set => SetProperty(ref _serverMemberOfflineSectionLabel, value);
        }
    }
}

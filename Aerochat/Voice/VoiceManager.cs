using Aerochat.ViewModels;
using DSharpPlus.Entities;
using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace Aerochat.Voice
{
    public enum DmCallState { None, Ringing, Connected }

    /// <summary>
    /// Placeholder for removed voice chat; keeps bindings and APIs stable without connecting audio.
    /// </summary>
    public readonly struct VoiceStateChanged
    {
        public uint SSRC { get; }
        public bool Speaking { get; }

        public VoiceStateChanged(uint ssrc, bool speaking)
        {
            SSRC = ssrc;
            Speaking = speaking;
        }
    }

    public class VoiceManager : ViewModelBase
    {
        public static VoiceManager Instance = new();

        public DiscordChannel? Channel => null;

        private ChannelViewModel? _channelVM;
        public ChannelViewModel? ChannelVM
        {
            get => _channelVM;
            set => SetProperty(ref _channelVM, value);
        }

        public event EventHandler<(ulong UserId, bool IsSpeaking)>? UserSpeakingChanged;
        public event EventHandler<bool>? ClientSpeakingChanged;

        private DmCallState _currentDmCallState = DmCallState.None;
        public DmCallState CurrentDmCallState
        {
            get => _currentDmCallState;
            set { _currentDmCallState = value; OnPropertyChanged(); }
        }

        private UserViewModel? _dmCallRecipient;
        public UserViewModel? DmCallRecipient
        {
            get => _dmCallRecipient;
            set { _dmCallRecipient = value; OnPropertyChanged(); }
        }

        private readonly ConcurrentDictionary<ulong, float> _userVolumes = new();
        private readonly ConcurrentDictionary<ulong, bool> _userMuted = new();

        public bool SelfMuted { get; set; }

        public bool SelfDeafened { get; set; }

        private float _clientTransmitVolume = 1.0f;
        public float ClientTransmitVolume
        {
            get => _clientTransmitVolume;
            set => _clientTransmitVolume = value;
        }

        public void SetUserVolume(ulong userId, float volume) => _userVolumes[userId] = volume;

        public float GetUserVolume(ulong userId)
            => _userVolumes.TryGetValue(userId, out var vol) ? vol : 1.0f;

        public void SetUserMuted(ulong userId, bool muted) => _userMuted[userId] = muted;

        public bool IsUserMuted(ulong userId)
            => _userMuted.TryGetValue(userId, out var m) && m;

        public Task LeaveVoiceChannel()
        {
            ChannelVM = null;
            CurrentDmCallState = DmCallState.None;
            DmCallRecipient = null;
            return Task.CompletedTask;
        }

        public Task JoinVoiceChannel(DiscordChannel channel, Action<VoiceStateChanged> onStateChange)
            => Task.CompletedTask;
    }
}

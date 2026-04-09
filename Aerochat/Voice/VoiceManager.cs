using Aerochat.Hoarder;
using Aerochat.ViewModels;
using Aerochat.Windows;
using Aerovoice.Clients;
using DSharpPlus.Entities;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Aerochat.Voice
{
    public enum DmCallState { None, Ringing, Connected }

    public class VoiceManager : ViewModelBase
    {
        public static VoiceManager Instance = new();
        private VoiceSocket? voiceSocket;
        public VoiceSocket? VoiceSocket
        {
            get => voiceSocket;
        }

        public DiscordChannel? Channel => voiceSocket?.Channel;

        private ChannelViewModel? _channelVM;
        public ChannelViewModel? ChannelVM
        {
            get => _channelVM;
            set => SetProperty(ref _channelVM, value);
        }

        public event EventHandler<(ulong UserId, bool IsSpeaking)> UserSpeakingChanged;
        public event EventHandler<bool> ClientSpeakingChanged;

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

        public bool SelfMuted
        {
            get => voiceSocket?.SelfMuted ?? false;
            set { if (voiceSocket != null) voiceSocket.SelfMuted = value; }
        }

        public bool SelfDeafened
        {
            get => voiceSocket?.SelfDeafened ?? false;
            set { if (voiceSocket != null) voiceSocket.SelfDeafened = value; }
        }

        private float _clientTransmitVolume = 1.0f;
        public float ClientTransmitVolume
        {
            get => _clientTransmitVolume;
            set => _clientTransmitVolume = value;
        }

        public void SetUserVolume(ulong userId, float volume)
        {
            _userVolumes[userId] = volume;
            ApplyVolumeForUser(userId);
        }

        public float GetUserVolume(ulong userId)
        {
            return _userVolumes.TryGetValue(userId, out var vol) ? vol : 1.0f;
        }

        public void SetUserMuted(ulong userId, bool muted)
        {
            _userMuted[userId] = muted;
            ApplyMuteForUser(userId);
        }

        public bool IsUserMuted(ulong userId)
        {
            return _userMuted.TryGetValue(userId, out var m) && m;
        }

        private IEnumerable<uint> GetSsrcsForUser(ulong userId)
        {
            if (voiceSocket == null) return Array.Empty<uint>();
            return voiceSocket.UserSSRCMap
                .Where(kvp => kvp.Value == userId)
                .Select(kvp => kvp.Key);
        }

        private void ApplyVolumeForUser(ulong userId)
        {
            // Volume control not yet supported with the native audio backend.
        }

        private void ApplyMuteForUser(ulong userId)
        {
            // Mute control not yet supported with the native audio backend.
        }

        public async Task LeaveVoiceChannel()
        {
            if (voiceSocket is null)
                return;
            UnsubscribeEvents();
            await voiceSocket.DisconnectAndDispose();
            voiceSocket = null;
            ChannelVM = null;
            CurrentDmCallState = DmCallState.None;
            DmCallRecipient = null;
        }

        public async Task JoinVoiceChannel(DiscordChannel channel, Action<VoiceStateChanged> onStateChange)
        {
            await LeaveVoiceChannel();
            voiceSocket = new(Discord.Client, (e) =>
            {
                onStateChange(e);
                if (voiceSocket?.UserSSRCMap.TryGetValue(e.SSRC, out ulong userId) == true)
                {
                    UserSpeakingChanged?.Invoke(this, (userId, e.Speaking));
                    if (userId == Discord.Client.CurrentUser.Id)
                        ClientSpeakingChanged?.Invoke(this, e.Speaking);
                }
            });
            await voiceSocket.ConnectAsync(channel);
            ChannelVM = ChannelViewModel.FromChannel(channel);
        }

        private void SubscribeEvents()
        {
            if (voiceSocket == null) return;
            voiceSocket.UserSpeakingChanged += OnUserSpeakingChanged;
            voiceSocket.ClientSpeakingChanged += OnClientSpeakingChanged;
        }

        private void UnsubscribeEvents()
        {
            if (voiceSocket == null) return;
            voiceSocket.UserSpeakingChanged -= OnUserSpeakingChanged;
            voiceSocket.ClientSpeakingChanged -= OnClientSpeakingChanged;
        }

        private void OnUserSpeakingChanged(object? sender, (ulong UserId, bool IsSpeaking) e)
        {
            if (voiceSocket == null) return;
            if (e.IsSpeaking)
            {
                ApplyMuteForUser(e.UserId);
                ApplyVolumeForUser(e.UserId);
            }
            UserSpeakingChanged?.Invoke(this, e);
        }

        private void OnClientSpeakingChanged(object? sender, bool isSpeaking)
        {
            ClientSpeakingChanged?.Invoke(this, isSpeaking);
        }
    }
}

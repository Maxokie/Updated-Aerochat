using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Websocket.Client;
using System.Text;
using System.Threading.Tasks;
using DSharpPlus;
using Newtonsoft.Json.Linq;
using System.Net;
using Sodium;
using Microsoft.VisualBasic;
using NAudio.Wave;
using System.Timers;
using DSharpPlus.Entities;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using OpusDotNet;
using Aerovoice.Players;
using Aerovoice.Decoders;
using Aerovoice.Crypts;
using Aerovoice.Logging;
using Aerovoice.Recorders;
using Aerovoice.Encoders;
using Aerovoice.Timestamp;
using static System.Runtime.InteropServices.JavaScript.JSType;
using System.Runtime.InteropServices;
using static Vanara.PInvoke.Kernel32;

namespace Aerovoice.Clients
{
    public class VoiceStateChanged
    {
        public uint SSRC;
        public bool Speaking;

        public VoiceStateChanged(uint SSRC, bool Speaking)
        {
            this.SSRC = SSRC;
            this.Speaking = Speaking;
        }
    }

    struct IPInfo
    {
        public string Address;
        public ushort Port;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void VoiceUserCallback(uint ssrc, bool speaking);

    partial class VoiceSession : IDisposable
    {
        unsafe struct RawIPInfo
        {
            public fixed byte IP[64];
            public ushort Port;
        }

        [LibraryImport("AerovoiceNative.dll")]
        [UnmanagedCallConv(CallConvs = new Type[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]
        private static partial IntPtr voice_session_new(uint ssrc, ulong channelId, [MarshalAs(UnmanagedType.LPUTF8Str)] string ip, ushort port, VoiceUserCallback onSpeaking);
        [LibraryImport("AerovoiceNative.dll")]
        [UnmanagedCallConv(CallConvs = new Type[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]
        private static partial void voice_session_free(IntPtr sessionHandle);
        [LibraryImport("AerovoiceNative.dll")]
        [UnmanagedCallConv(CallConvs = new Type[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]
        private static partial void voice_session_init_poll_thread(IntPtr sessionHandle);
        [LibraryImport("AerovoiceNative.dll")]
        [UnmanagedCallConv(CallConvs = new Type[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]
        private static partial IntPtr voice_session_discover_ip(IntPtr sessionHandle);
        [LibraryImport("AerovoiceNative.dll")]
        [UnmanagedCallConv(CallConvs = new Type[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]
        private unsafe static partial IntPtr voice_session_set_secret(IntPtr sessionHandle, byte* secret, uint secretLen);
        [LibraryImport("AerovoiceNative.dll")]
        [UnmanagedCallConv(CallConvs = new Type[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]
        [return: MarshalAs(UnmanagedType.LPUTF8Str)]
        private unsafe static partial string voice_session_select_cryptor(IntPtr sessionHandle, [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPUTF8Str, SizeParamIndex = 2)] string[] availableMethods, uint availableMethodsLen);

        private IntPtr _sessionHandle;

        public VoiceSession(uint ssrc, ulong channelId, string ip, ushort port, VoiceUserCallback onSpeaking)
        {
            AllocConsole();
            _sessionHandle = voice_session_new(ssrc, channelId, ip, port, onSpeaking);
            if (_sessionHandle == IntPtr.Zero)
            {
                throw new Exception("Failed to create voice session.");
            }
        }

        public void BeginPolling()
        {
            voice_session_init_poll_thread(_sessionHandle);
        }

        public IPInfo DiscoverIP()
        {
            unsafe
            {
                var result = *(RawIPInfo*)voice_session_discover_ip(_sessionHandle).ToPointer();
                byte[] managedIP = new byte[64];
                for (int i = 0; i < 64; i++)
                {
                    managedIP[i] = result.IP[i];
                }

                string ip = Encoding.UTF8.GetString(managedIP).Trim((char)0);
                ushort port = result.Port;
                return new IPInfo { Address = ip, Port = port };
            }
        }

        public void SetSecret(byte[] secret)
        {
            var len = (uint)secret.Length;
            unsafe
            {
                fixed (byte* secretPtr = &secret[0])
                {
                    voice_session_set_secret(_sessionHandle, secretPtr, len);
                }
            }
        }

        public string SelectCryptor(string[] availableCryptors)
        {
            return voice_session_select_cryptor(_sessionHandle, availableCryptors, (uint)availableCryptors.Length);
        }

        public void Dispose()
        {
            FreeConsole();
            if (_sessionHandle != IntPtr.Zero)
            {
                voice_session_free(_sessionHandle);
                _sessionHandle = IntPtr.Zero;
            }
        }
    }


    public class VoiceSocket
    {
        private WebsocketClient _socket;
        private DiscordClient _client;
        private JObject _ready;
        private bool _disposed = false;
        private VoiceSession? _session;

        public NAudioPlayer Player = new NAudioPlayer();
        public IDecoder Decoder = new OpusDotNetDecoder();
        public IEncoder Encoder = new ConcentusEncoder();
        public BaseRecorder Recorder = new NAudioRecorder();
        public string? ForceEncryptionName;

        private BaseCrypt cryptor;
        private byte[] _secretKey;
        private int _sequence;
        private string _sessionId;
        private string _voiceToken;
        private Uri _endpoint;
        public DiscordChannel Channel { get; private set; }
        private bool _connected = false;
        private RTPTimestamp _timestamp = new(3840);
        private uint _ssrc = 0;
        private VoiceUserCallback _cb;
        private Dictionary<uint, ulong> _userSsrcMap = [];
        public Dictionary<uint, ulong> UserSSRCMap { get { return _userSsrcMap; } }

        public bool Speaking { get; private set; } = false;
        public bool SelfMuted { get; set; } = false;
        public bool SelfDeafened { get; set; } = false;

        public readonly ConcurrentDictionary<uint, ulong> SsrcToUserId = new();
        private readonly ConcurrentDictionary<ulong, DateTime> _lastSpeakingTime = new();
        private const double SpeakingTimeoutMs = 300;
        private const double IncomingRmsThreshold = 300;

        public event EventHandler<(ulong UserId, bool IsSpeaking)> UserSpeakingChanged;
        public event EventHandler<bool> ClientSpeakingChanged;

        public ulong GetUserIdFromSsrc(uint ssrc)
        {
            return SsrcToUserId.TryGetValue(ssrc, out var userId) ? userId : 0;
        }

        System.Timers.Timer _timer;
        System.Timers.Timer _speakingDecayTimer;

        Action<VoiceStateChanged> _onStateChange;

        public VoiceSocket(DiscordClient client, Action<VoiceStateChanged> onStateChange)
        {
            _client = client;
            _timer = new();
            _timer.Interval = 16.666666666666668;
            _timer.AutoReset = true;
            _timer.Elapsed += (s, e) => _timestamp.Increment(3840);
            _timer.Start();

            _speakingDecayTimer = new();
            _speakingDecayTimer.Interval = 150;
            _speakingDecayTimer.AutoReset = true;
            _speakingDecayTimer.Elapsed += SpeakingDecayTimer_Elapsed;
            _speakingDecayTimer.Start();
        }

        private void SpeakingDecayTimer_Elapsed(object? sender, ElapsedEventArgs e)
        {
            var now = DateTime.UtcNow;
            foreach (var kvp in _lastSpeakingTime)
            {
                if ((now - kvp.Value).TotalMilliseconds > SpeakingTimeoutMs)
                {
                    if (_lastSpeakingTime.TryRemove(kvp.Key, out _))
                        UserSpeakingChanged?.Invoke(this, (kvp.Key, false));
                }
            }
        }

        public async Task SendMessage(JObject message)
        {
            _socket.Send(message.ToString());
        }

        public byte[] ConstructPortScanPacket(uint ssrc, string ip, ushort port)
        {
            // Allocate buffers for each part of the packet
            byte[] packetType = new byte[2];
            BitConverter.GetBytes((ushort)1).CopyTo(packetType, 0);
            Array.Reverse(packetType); // Ensure big-endian order

            byte[] packetLength = new byte[2];
            BitConverter.GetBytes((ushort)70).CopyTo(packetLength, 0);
            Array.Reverse(packetLength);

            byte[] ssrcBuf = new byte[4];
            BitConverter.GetBytes(ssrc).CopyTo(ssrcBuf, 0);
            Array.Reverse(ssrcBuf);

            byte[] address = new byte[64];
            byte[] ipBytes = Encoding.UTF8.GetBytes(ip);
            Array.Copy(ipBytes, address, ipBytes.Length);

            byte[] portBuffer = new byte[2];
            BitConverter.GetBytes(port).CopyTo(portBuffer, 0);
            Array.Reverse(portBuffer);

            byte[] packet = new byte[2 + 2 + 4 + 64 + 2];
            Array.Copy(packetType, 0, packet, 0, 2);
            Array.Copy(packetLength, 0, packet, 2, 2);
            Array.Copy(ssrcBuf, 0, packet, 4, 4);
            Array.Copy(address, 0, packet, 8, 64);
            Array.Copy(portBuffer, 0, packet, 72, 2);

            return packet;
        }

        private void OnSpeaking(uint ssrc, bool speaking)
        { }

        public async Task OnMessageReceived(JObject message)
        {
            Debug.WriteLine(message);
            // if message["seq"] exists, set _sequence to it
            if (message["seq"] != null)
            {
                _sequence = message["seq"]!.Value<int>();
            }
            int op = message["op"]!.Value<int>();
            switch (op)
            {
                case 2: // ready
                {
                    _ready = message["d"]!.Value<JObject>()!;
                    var ip = _ready["ip"]!.Value<string>()!;
                    var port = _ready["port"]!.Value<ushort>();
                    _ssrc = _ready["ssrc"]!.Value<uint>();
                    var modes = _ready["modes"]!.ToArray().Select(x => x.Value<string>()!);
                    _availableEncryptionModes = modes.ToList();
                    Logger.Log($"Attempting to open UDP connection to {ip}:{port}.");
                    Logger.Log($"Your SSRC is {_ssrc}.");
                    UdpClient = new(ip, port);
                    UdpClient.MessageReceived += (s, e) => Task.Run(() => UdpClient_MessageReceived(s, e));

                    var discoveryPacket = ConstructPortScanPacket(_ssrc, ip, port);

                    UdpClient.SendMessage(discoveryPacket);
                    break;
                }
                case 4: // session description
                {
                    var secretKey = message["d"]!["secret_key"]!.Value<JArray>()!.Select(x => (byte)x.Value<int>()).ToArray();
                    _secretKey = secretKey;
                    if (cryptor is null)
                    {
                        cryptor = GetPreferredEncryption();
                    }
                    break;
                }
                case 5: // speaking
                {
                    var d = message["d"]!;
                    var userId = d["user_id"]!.Value<ulong>();
                    var ssrc = d["ssrc"]!.Value<uint>();
                    SsrcToUserId[ssrc] = userId;
                    break;
                }
            }
        }

        private readonly SortedList<uint, byte[]> _packetBuffer = new();
        private readonly object _bufferLock = new();
        private uint _lastPlayedTimestamp = 0;
        private const int BUFFER_THRESHOLD = 5;

        private async Task UdpClient_MessageReceived(object? sender, byte[] e)
        {
            byte packetType = e[1];
            switch (packetType)
            {
                case 0x2: // ip discovery
                {
                    var address = Encoding.UTF8.GetString(e, 8, 64).TrimEnd('\0');
                    var port = (ushort)IPAddress.NetworkToHostOrder(BitConverter.ToInt16(e, 72));
                    Logger.Log($"IP discovery was successful, your port is {port}.");
                    if (cryptor is null)
                    {
                        cryptor = GetPreferredEncryption();
                    }
                    await SendMessage(JObject.FromObject(new
                    {
                        op = 1,
                        d = new
                        {
                            protocol = "udp",
                            data = new
                            {
                                address,
                                port,
                                mode = cryptor.PName
                            },
                            codecs = new[]
        {
                                new
                                {
                                    name = "opus",
                                    type = "audio",
                                    priority = 1000,
                                    payload_type = 120
                                }
                            }
                        }
                    }));
                    Recorder.Start();
                    break;
                }
                case 0x78:
                {
                    // TODO: thread manager where each user gets one thread
                    await Task.Run(() =>
                    {
                        if (cryptor is null || _secretKey is null) return;

                        var rtpTimestamp = BinaryPrimitives.ReadUInt32BigEndian(e.AsSpan(4));
                        lock (_bufferLock)
                        {
                            _packetBuffer[rtpTimestamp] = e;
                        }

                        TryProcessBufferedPackets();
                    });
                    break;
                }
            }
        }

        private void TryProcessBufferedPackets()
        {
            lock (_bufferLock)
            {
                if (_packetBuffer.Count < BUFFER_THRESHOLD)
                    return;

                foreach (var key in _packetBuffer.Keys.ToList())
                {
                    if (key > _lastPlayedTimestamp)
                    {
                        byte[] packet = _packetBuffer[key];
                        _packetBuffer.Remove(key);
                        _lastPlayedTimestamp = key;

                        ProcessPacket(packet);
                    }
                }
            }
        }

        private void ProcessPacket(byte[] e)
        {
            if (SelfDeafened) return;

            var ssrc = BinaryPrimitives.ReadUInt32BigEndian(e.AsSpan(8));

            if (Player.IsSsrcMuted(ssrc)) return;

            byte[] decryptedData = cryptor.Decrypt(e, _secretKey);
            if (decryptedData.Length == 0) return;
            ushort increment = BinaryPrimitives.ReadUInt16BigEndian(e.AsSpan(2));
            var decoded = Decoder.Decode(decryptedData, decryptedData.Length, out int decodedLength, ssrc, increment);

            var userId = GetUserIdFromSsrc(ssrc);
            if (userId != 0)
            {
                double rms = ComputeRms(decoded, decodedLength);
                if (rms >= IncomingRmsThreshold)
                {
                    bool wasNew = !_lastSpeakingTime.ContainsKey(userId);
                    _lastSpeakingTime[userId] = DateTime.UtcNow;
                    if (wasNew)
                        UserSpeakingChanged?.Invoke(this, (userId, true));
                }
            }

            Player.AddSamples(decoded, decodedLength, ssrc);
        }

        private static double ComputeRms(byte[] pcm, int length)
        {
            int samples = length / 2;
            double sum = 0;
            for (int i = 0; i < length - 1; i += 2)
            {
                short sample = (short)((pcm[i + 1] << 8) | pcm[i]);
                sum += sample * sample;
            }
            return Math.Sqrt(sum / samples);
        }

        public BaseCrypt GetPreferredEncryption()
        {
            var decryptors = typeof(BaseCrypt).Assembly.GetTypes().Where(x => x.Namespace == "Aerovoice.Crypts" && x.IsSubclassOf(typeof(BaseCrypt)) && _availableEncryptionModes.Contains((string)x.GetProperty("Name")!.GetValue(null)!));
            var priority = new[] { "aead_aes256_gcm_rtpsize", "aead_xchacha20_poly1305_rtpsize" };
            BaseCrypt? decryptor = null;
            if (ForceEncryptionName != null)
            {
                var forced = decryptors.FirstOrDefault(x => x.GetProperty("Name")!.GetValue(null)!.Equals(ForceEncryptionName));
                if (forced != null)
                {
                    decryptor = (BaseCrypt)Activator.CreateInstance(forced)!;
                } else
                {
                    Logger.Log($"\"{ForceEncryptionName}\" is not supported, falling back to default.");
                }
            }
            if (decryptor == null)
            {
                foreach (var p in priority)
                {
                    var d = decryptors.FirstOrDefault(x => x.GetProperty("Name")!.GetValue(null)!.Equals(p));
                    if (d != null && _availableEncryptionModes.Contains(p))
                    {
                        decryptor = (BaseCrypt)Activator.CreateInstance(d)!;
                        break;
                    }
            }
        }

        public async Task ConnectAsync(DiscordChannel channel)
        {
            if (_disposed) throw new InvalidOperationException("This voice socket has been disposed!");
            Channel = channel;
            await _client.UpdateVoiceStateAsync(Channel.Guild?.Id ?? Channel.Id, Channel.Id, false, false);
            _client.VoiceStateUpdated += _client_VoiceStateUpdated;
            _client.VoiceServerUpdated += _client_VoiceServerUpdated;
            Recorder.DataAvailable += Recorder_DataAvailable;
        }

        private short _udpSequence = (short)new Random().Next(0, short.MaxValue);

        private const int BufferDurationMs = 200;
        private const int ChunkDurationMs = 20;
        private const int SampleRate = 48000; // 48kHz
        private const int BytesPerSample = 2; // 16-bit PCM
        private const int Channels = 2; // Stereo
        private const int BufferSizeBytes = (SampleRate * Channels * BytesPerSample * BufferDurationMs) / 1000; // 38400 bytes
        private byte[] _circularBuffer = new byte[BufferSizeBytes];
        private int _bufferOffset = 0;
        private bool _bufferFilled = false;

        private async void Recorder_DataAvailable(object? sender, byte[] e)
        {
            await Task.Run(() =>
            {
                AddToCircularBuffer(e);

                var sampleIsSpeaking = IsSpeaking(_circularBuffer, _bufferFilled ? BufferSizeBytes : _bufferOffset);

                if (sampleIsSpeaking || SelfMuted)
                {
                    if (Speaking)
                    {
                        _ = SendMessage(JObject.FromObject(new
                        {
                            op = 5,
                            d = new
                            {
                                speaking = 0,
                                delay = 0,
                                ssrc = _ssrc
                            }
                        }));
                        Speaking = false;
                        ClientSpeakingChanged?.Invoke(this, false);
                    }
                    return;
                }

                if (!Speaking)
                {
                    _ = SendMessage(JObject.FromObject(new
                    {
                        op = 5,
                        d = new
                        {
                            speaking = 1 << 0,
                            delay = 0,
                            ssrc = _ssrc
                        }
                    }));
                    Speaking = true;
                    ClientSpeakingChanged?.Invoke(this, true);
                }

                if (cryptor is null) return;

                byte[] toEncode = e;
                float vol = Player.ClientTransmitVolume;
                if (Math.Abs(vol - 1.0f) > 0.01f)
                    toEncode = ScalePcm(e, vol);

                var opus = Encoder.Encode(toEncode);
                var header = new byte[12];
                header[0] = 0x80;
                header[1] = 0x78;
                BinaryPrimitives.WriteInt16BigEndian(header.AsSpan(2), _udpSequence++);
                BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), _timestamp.GetCurrentTimestamp());
                BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(8), _ssrc);
                var packet = new byte[header.Length + opus.Length];
                Array.Copy(header, 0, packet, 0, header.Length);
                Array.Copy(opus, 0, packet, header.Length, opus.Length);
                var encrypted = cryptor.Encrypt(packet, _secretKey);
                UdpClient.SendMessage(encrypted);
            });
        }

        private static byte[] ScalePcm(byte[] pcm, float volume)
        {
            var result = new byte[pcm.Length];
            for (int i = 0; i < pcm.Length - 1; i += 2)
            {
                short sample = (short)((pcm[i + 1] << 8) | pcm[i]);
                int scaled = (int)(sample * volume);
                scaled = Math.Clamp(scaled, short.MinValue, short.MaxValue);
                result[i] = (byte)(scaled & 0xFF);
                result[i + 1] = (byte)((scaled >> 8) & 0xFF);
            }
            return result;
        }

        private void AddToCircularBuffer(byte[] data)
        {
            int dataLength = data.Length;

            if (_bufferOffset + dataLength > BufferSizeBytes)
            {
                int overflow = _bufferOffset + dataLength - BufferSizeBytes;
                Array.Copy(data, 0, _circularBuffer, _bufferOffset, dataLength - overflow);
                Array.Copy(data, dataLength - overflow, _circularBuffer, 0, overflow);
                _bufferOffset = overflow;
                _bufferFilled = true;
            }
            else
            {
                Array.Copy(data, 0, _circularBuffer, _bufferOffset, dataLength);
                _bufferOffset += dataLength;
            }
        }

        private bool IsSpeaking(byte[] buffer, int length)
        {
            int samples = length / BytesPerSample; // Convert byte length to number of samples
            double sum = 0;

            for (int i = 0; i < length; i += 2)
            {
                short sample = (short)((buffer[i + 1] << 8) | buffer[i]); // Convert to 16-bit sample
                sum += sample * sample;
            }

        private async Task _client_VoiceStateUpdated(DiscordClient sender, DSharpPlus.EventArgs.VoiceStateUpdateEventArgs args)
        {
            _sessionId = args.SessionId;
            if (_voiceToken != null && _endpoint != null)
            {
                await BeginVoiceConnection();
            }
        }

        private async Task _client_VoiceServerUpdated(DiscordClient sender, DSharpPlus.EventArgs.VoiceServerUpdateEventArgs args)
        {
            _voiceToken = args.VoiceToken;
            _endpoint = new Uri($"wss://{args.Endpoint}/?v=8");
            if (_sessionId != null)
            {
                await BeginVoiceConnection();
            }
        }

        private async Task BeginVoiceConnection()
        {
            if (_connected) return;
            _connected = true;
            _socket = new WebsocketClient(_endpoint);
            await _socket.Start();
            System.Timers.Timer timer = new();
            timer.Interval = 13750;
            timer.AutoReset = true;
            timer.Elapsed += async (s, e) => await SendMessage(JObject.FromObject(new
            {
                op = 3,
                d = new
                {
                    // t should be the current unix time
                    t = DateTimeOffset.Now.ToUnixTimeMilliseconds(),
                    seq = _sequence,
                }
            }));
            timer.Start();
            Logger.Log("Connected to the voice gateway, attempting to connect to UDP server!");
            _socket.MessageReceived.Subscribe(x => OnMessageReceived(JObject.Parse(x.Text)));
            await SendMessage(JObject.FromObject(new
            {
                op = 0,
                d = new
                {
                    server_id = Channel.Guild?.Id.ToString() ?? "",
                    user_id = _client.CurrentUser.Id.ToString(),
                    session_id = _sessionId,
                    token = _voiceToken,
                }
            }));
        }

        public async Task DisconnectAndDispose()
        {
            if (!_connected) return;
            _connected = false;
            _disposed = true;
            await _client.UpdateVoiceStateAsync(Channel.GuildId, null, false, false);
            _socket?.Dispose();
            _timer.Dispose();
            _speakingDecayTimer.Dispose();
            Encoder?.Dispose();
        }
    }
}

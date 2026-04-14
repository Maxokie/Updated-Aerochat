// This file is part of the DSharpPlus project.
//
// Copyright (c) 2015 Mike Santiago
// Copyright (c) 2016-2023 DSharpPlus Contributors
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DSharpPlus.AsyncEvents;
using DSharpPlus.EventArgs;

namespace DSharpPlus.Net.WebSocket
{
    /// <summary>
    /// A fully-managed WebSocket client using TcpClient + SslStream (Schannel).
    /// Unlike <see cref="WebSocketClient"/> which uses ClientWebSocket/WinHTTP, this
    /// implementation explicitly specifies <see cref="SslProtocols.Tls12"/> to
    /// <see cref="SslStream.AuthenticateAsClientAsync"/>, enabling TLS 1.2 on
    /// Windows Vista/7 without any registry modifications or administrator rights.
    /// </summary>
    public class ManagedWebSocketClient : IWebSocketClient
    {
        private const int OutgoingChunkSize = 8192;

        /// <inheritdoc />
        public IWebProxy Proxy { get; }

        /// <inheritdoc />
        public IReadOnlyDictionary<string, string> DefaultHeaders { get; }
        private readonly Dictionary<string, string> _defaultHeaders;

        private Task _receiverTask;
        private CancellationTokenSource _receiverTokenSource;
        private CancellationToken _receiverToken;
        private readonly SemaphoreSlim _senderLock;

        private CancellationTokenSource _socketTokenSource;
        private CancellationToken _socketToken;

        private TcpClient _tcp;
        private SslStream _ssl;

        private volatile bool _isClientClose = false;
        private volatile bool _isConnected = false;
        private bool _isDisposed = false;

        private ManagedWebSocketClient(IWebProxy proxy)
        {
            this._connected = new AsyncEvent<ManagedWebSocketClient, SocketEventArgs>("WS_CONNECT", this.EventErrorHandler);
            this._disconnected = new AsyncEvent<ManagedWebSocketClient, SocketCloseEventArgs>("WS_DISCONNECT", this.EventErrorHandler);
            this._messageReceived = new AsyncEvent<ManagedWebSocketClient, SocketMessageEventArgs>("WS_MESSAGE", this.EventErrorHandler);
            this._exceptionThrown = new AsyncEvent<ManagedWebSocketClient, SocketErrorEventArgs>("WS_ERROR", null);

            this.Proxy = proxy;
            this._defaultHeaders = new Dictionary<string, string>();
            this.DefaultHeaders = new ReadOnlyDictionary<string, string>(this._defaultHeaders);

            this._receiverTokenSource = null;
            this._receiverToken = CancellationToken.None;
            this._senderLock = new SemaphoreSlim(1);

            this._socketTokenSource = null;
            this._socketToken = CancellationToken.None;
        }

        /// <inheritdoc />
        public async Task ConnectAsync(Uri uri)
        {
            try { await this.DisconnectAsync().ConfigureAwait(false); } catch { }

            await this._senderLock.WaitAsync().ConfigureAwait(false);
            try
            {
                this._receiverTokenSource?.Dispose();
                this._socketTokenSource?.Dispose();
                this._ssl?.Dispose();
                this._tcp?.Dispose();

                this._receiverTokenSource = new CancellationTokenSource();
                this._receiverToken = this._receiverTokenSource.Token;
                this._socketTokenSource = new CancellationTokenSource();
                this._socketToken = this._socketTokenSource.Token;

                this._isClientClose = false;
                this._isDisposed = false;

                var host = uri.Host;
                var port = uri.IsDefaultPort ? (uri.Scheme == "wss" ? 443 : 80) : uri.Port;

                // TCP connect with 30-second timeout
                this._tcp = new TcpClient();
                var connectTask = this._tcp.ConnectAsync(host, port);
                if (await Task.WhenAny(connectTask, Task.Delay(TimeSpan.FromSeconds(30))).ConfigureAwait(false) != connectTask)
                    throw new TimeoutException("TCP connection to Discord gateway timed out.");
                await connectTask.ConfigureAwait(false);

                // TLS 1.2 via SslStream/Schannel — no registry or admin rights needed.
                // SslProtocols.Tls through Tls12 are listed so the OS picks the best
                // mutually supported version; Vista SP2 with TLS patch negotiates 1.2,
                // Windows 7 SP1+ does so natively.
                this._ssl = new SslStream(this._tcp.GetStream(), false);
                var authTask = this._ssl.AuthenticateAsClientAsync(
                    host,
                    null,
                    SslProtocols.Tls12 | SslProtocols.Tls11 | SslProtocols.Tls,
                    checkCertificateRevocation: false);
                if (await Task.WhenAny(authTask, Task.Delay(TimeSpan.FromSeconds(30))).ConfigureAwait(false) != authTask)
                    throw new TimeoutException("TLS handshake with Discord gateway timed out.");
                await authTask.ConfigureAwait(false);

                // RFC 6455 HTTP/1.1 upgrade handshake
                var keyBytes = new byte[16];
                using (var rng = new RNGCryptoServiceProvider())
                    rng.GetBytes(keyBytes);
                var wsKey = Convert.ToBase64String(keyBytes);

                var path = string.IsNullOrEmpty(uri.PathAndQuery) ? "/" : uri.PathAndQuery;
                var request = new StringBuilder();
                request.Append($"GET {path} HTTP/1.1\r\n");
                request.Append($"Host: {host}\r\n");
                request.Append("Upgrade: websocket\r\n");
                request.Append("Connection: Upgrade\r\n");
                request.Append($"Sec-WebSocket-Key: {wsKey}\r\n");
                request.Append("Sec-WebSocket-Version: 13\r\n");
                foreach (var kv in this._defaultHeaders)
                    request.Append($"{kv.Key}: {kv.Value}\r\n");
                request.Append("\r\n");

                var requestBytes = Encoding.ASCII.GetBytes(request.ToString());
                await this._ssl.WriteAsync(requestBytes, 0, requestBytes.Length).ConfigureAwait(false);
                await this._ssl.FlushAsync().ConfigureAwait(false);

                // Read HTTP response headers (terminated by \r\n\r\n)
                var headerBuffer = new byte[4096];
                int totalRead = 0;
                while (totalRead < headerBuffer.Length)
                {
                    int n = await this._ssl.ReadAsync(headerBuffer, totalRead, headerBuffer.Length - totalRead).ConfigureAwait(false);
                    if (n == 0)
                        throw new EndOfStreamException("Connection closed during WebSocket handshake.");
                    totalRead += n;
                    if (Encoding.ASCII.GetString(headerBuffer, 0, totalRead).Contains("\r\n\r\n"))
                        break;
                }

                var responseText = Encoding.ASCII.GetString(headerBuffer, 0, totalRead);
                if (!responseText.StartsWith("HTTP/1.1 101"))
                    throw new Exception($"Unexpected WebSocket upgrade response: {responseText.Substring(0, Math.Min(responseText.Length, 100))}");

                this._receiverTask = Task.Run(this.ReceiverLoopAsync, this._receiverToken);
            }
            finally
            {
                this._senderLock.Release();
            }
        }

        /// <inheritdoc />
        public async Task DisconnectAsync(int code = 1000, string message = "")
        {
            await this._senderLock.WaitAsync().ConfigureAwait(false);
            try
            {
                this._isClientClose = true;

                if (this._ssl != null && this._isConnected)
                {
                    try
                    {
                        var closePayload = new byte[] { (byte)(code >> 8), (byte)(code & 0xFF) };
                        var frame = EncodeFrame(closePayload, 8);
                        await this._ssl.WriteAsync(frame, 0, frame.Length).ConfigureAwait(false);
                    }
                    catch { }
                }

                if (this._receiverTask != null)
                {
                    try { await this._receiverTask.ConfigureAwait(false); } catch { }
                }

                if (this._isConnected)
                    this._isConnected = false;

                if (!this._isDisposed)
                {
                    if (this._socketToken.CanBeCanceled)
                        this._socketTokenSource?.Cancel();
                    this._socketTokenSource?.Dispose();

                    if (this._receiverToken.CanBeCanceled)
                        this._receiverTokenSource?.Cancel();
                    this._receiverTokenSource?.Dispose();

                    this._ssl?.Dispose();
                    this._tcp?.Dispose();
                    this._isDisposed = true;
                }
            }
            catch { }
            finally
            {
                this._senderLock.Release();
            }
        }

        /// <inheritdoc />
        public async Task SendMessageAsync(string message)
        {
            if (this._ssl == null || !this._isConnected)
                return;

            var bytes = Utilities.UTF8.GetBytes(message);
            await this._senderLock.WaitAsync().ConfigureAwait(false);
            try
            {
                int offset = 0;
                while (offset < bytes.Length)
                {
                    int len = Math.Min(OutgoingChunkSize, bytes.Length - offset);
                    bool isFinal = (offset + len) >= bytes.Length;
                    int opcode = offset == 0 ? 1 : 0; // 1=text, 0=continuation

                    var chunk = new byte[len];
                    Array.Copy(bytes, offset, chunk, 0, len);
                    var frame = EncodeFrame(chunk, opcode, isFinal);
                    await this._ssl.WriteAsync(frame, 0, frame.Length).ConfigureAwait(false);
                    offset += len;
                }
                await this._ssl.FlushAsync().ConfigureAwait(false);
            }
            finally
            {
                this._senderLock.Release();
            }
        }

        /// <inheritdoc />
        public bool AddDefaultHeader(string name, string value)
        {
            this._defaultHeaders[name] = value;
            return true;
        }

        /// <inheritdoc />
        public bool RemoveDefaultHeader(string name)
            => this._defaultHeaders.Remove(name);

        /// <summary>
        /// Disposes of resources used by this WebSocket client instance.
        /// </summary>
        public void Dispose()
        {
            if (this._isDisposed)
                return;

            this._isDisposed = true;
            this.DisconnectAsync().ConfigureAwait(false).GetAwaiter().GetResult();
            this._receiverTokenSource?.Dispose();
            this._socketTokenSource?.Dispose();
        }

        // RFC 6455 §5.3: client-to-server frames must be masked with a random 4-byte key.
        private static byte[] EncodeFrame(byte[] payload, int opcode, bool fin = true)
        {
            var maskKey = new byte[4];
            using (var rng = new RNGCryptoServiceProvider())
                rng.GetBytes(maskKey);

            var masked = new byte[payload.Length];
            for (int i = 0; i < payload.Length; i++)
                masked[i] = (byte)(payload[i] ^ maskKey[i % 4]);

            using var ms = new MemoryStream();
            ms.WriteByte((byte)((fin ? 0x80 : 0x00) | (opcode & 0x0F)));

            // 0x80 = MASK bit, required for client→server
            if (payload.Length <= 125)
            {
                ms.WriteByte((byte)(0x80 | payload.Length));
            }
            else if (payload.Length <= 65535)
            {
                ms.WriteByte(0xFE); // 0x80 | 126
                ms.WriteByte((byte)(payload.Length >> 8));
                ms.WriteByte((byte)(payload.Length & 0xFF));
            }
            else
            {
                ms.WriteByte(0xFF); // 0x80 | 127
                ulong len = (ulong)payload.Length;
                for (int i = 7; i >= 0; i--)
                    ms.WriteByte((byte)((len >> (i * 8)) & 0xFF));
            }

            ms.Write(maskKey, 0, 4);
            ms.Write(masked, 0, masked.Length);
            return ms.ToArray();
        }

        internal async Task ReceiverLoopAsync()
        {
            await Task.Yield();
            var token = this._receiverToken;

            try
            {
                using var accumulated = new MemoryStream();
                int currentOpcode = -1;

                while (!token.IsCancellationRequested)
                {
                    // Minimum 2-byte WebSocket frame header
                    var header = await ReadBytesAsync(2, token).ConfigureAwait(false);
                    bool fin = (header[0] & 0x80) != 0;
                    int opcode = header[0] & 0x0F;
                    bool masked = (header[1] & 0x80) != 0;
                    long payloadLen = header[1] & 0x7F;

                    if (payloadLen == 126)
                    {
                        var ext = await ReadBytesAsync(2, token).ConfigureAwait(false);
                        payloadLen = (ext[0] << 8) | ext[1];
                    }
                    else if (payloadLen == 127)
                    {
                        var ext = await ReadBytesAsync(8, token).ConfigureAwait(false);
                        payloadLen = 0;
                        for (int i = 0; i < 8; i++)
                            payloadLen = (payloadLen << 8) | ext[i];
                    }

                    byte[] maskKeyBytes = null;
                    if (masked)
                        maskKeyBytes = await ReadBytesAsync(4, token).ConfigureAwait(false);

                    var payload = payloadLen > 0
                        ? await ReadBytesAsync((int)payloadLen, token).ConfigureAwait(false)
                        : new byte[0];

                    if (masked)
                        for (int i = 0; i < payload.Length; i++)
                            payload[i] ^= maskKeyBytes[i % 4];

                    // Control frames (opcode >= 8) are never fragmented per RFC 6455
                    if (opcode >= 8)
                    {
                        switch (opcode)
                        {
                            case 8: // Close
                            {
                                int closeCode = payload.Length >= 2 ? (payload[0] << 8) | payload[1] : 1000;
                                string closeMsg = payload.Length > 2 ? Encoding.UTF8.GetString(payload, 2, payload.Length - 2) : "";
                                if (!this._isClientClose)
                                {
                                    try
                                    {
                                        var echo = payload.Length >= 2 ? new[] { payload[0], payload[1] } : new byte[0];
                                        var echoFrame = EncodeFrame(echo, 8);
                                        await this._ssl.WriteAsync(echoFrame, 0, echoFrame.Length).ConfigureAwait(false);
                                    }
                                    catch { }
                                }
                                await this._disconnected.InvokeAsync(this, new SocketCloseEventArgs() { CloseCode = closeCode, CloseMessage = closeMsg }).ConfigureAwait(false);
                                return;
                            }
                            case 9: // Ping — respond with pong
                            {
                                var pong = EncodeFrame(payload, 10);
                                try { await this._ssl.WriteAsync(pong, 0, pong.Length).ConfigureAwait(false); } catch { }
                                break;
                            }
                            case 10: // Pong — ignore
                                break;
                        }
                        continue;
                    }

                    // Data frames — handle fragmentation (continuation frames have opcode 0)
                    if (opcode != 0)
                        currentOpcode = opcode;

                    accumulated.Write(payload, 0, payload.Length);

                    if (fin)
                    {
                        var messageBytes = accumulated.ToArray();
                        accumulated.SetLength(0);
                        accumulated.Position = 0;

                        if (!this._isConnected)
                        {
                            this._isConnected = true;
                            await this._connected.InvokeAsync(this, new SocketEventArgs()).ConfigureAwait(false);
                        }

                        if (currentOpcode == 2)
                            await this._messageReceived.InvokeAsync(this, new SocketBinaryMessageEventArgs(messageBytes)).ConfigureAwait(false);
                        else
                            await this._messageReceived.InvokeAsync(this, new SocketTextMessageEventArgs(Utilities.UTF8.GetString(messageBytes))).ConfigureAwait(false);
                    }
                }
            }
            catch (Exception ex)
            {
                await this._exceptionThrown.InvokeAsync(this, new SocketErrorEventArgs() { Exception = ex }).ConfigureAwait(false);
                await this._disconnected.InvokeAsync(this, new SocketCloseEventArgs() { CloseCode = -1, CloseMessage = "" }).ConfigureAwait(false);
            }

            // Fire-and-forget to avoid deadlock (DisconnectAsync waits for this task)
            _ = this.DisconnectAsync().ConfigureAwait(false);
        }

        private async Task<byte[]> ReadBytesAsync(int count, CancellationToken token)
        {
            var buffer = new byte[count];
            int totalRead = 0;
            while (totalRead < count)
            {
                token.ThrowIfCancellationRequested();
                int n = await this._ssl.ReadAsync(buffer, totalRead, count - totalRead, token).ConfigureAwait(false);
                if (n == 0)
                    throw new EndOfStreamException("WebSocket connection closed unexpectedly.");
                totalRead += n;
            }
            return buffer;
        }

        /// <summary>
        /// Creates a new instance of <see cref="ManagedWebSocketClient"/>.
        /// </summary>
        /// <param name="proxy">Proxy to use for this client instance.</param>
        /// <returns>An instance of <see cref="ManagedWebSocketClient"/>.</returns>
        public static IWebSocketClient CreateNew(IWebProxy proxy)
            => new ManagedWebSocketClient(proxy);

        #region Events
        /// <inheritdoc />
        public event AsyncEventHandler<IWebSocketClient, SocketEventArgs> Connected
        {
            add => this._connected.Register(value);
            remove => this._connected.Unregister(value);
        }
        private readonly AsyncEvent<ManagedWebSocketClient, SocketEventArgs> _connected;

        /// <inheritdoc />
        public event AsyncEventHandler<IWebSocketClient, SocketCloseEventArgs> Disconnected
        {
            add => this._disconnected.Register(value);
            remove => this._disconnected.Unregister(value);
        }
        private readonly AsyncEvent<ManagedWebSocketClient, SocketCloseEventArgs> _disconnected;

        /// <inheritdoc />
        public event AsyncEventHandler<IWebSocketClient, SocketMessageEventArgs> MessageReceived
        {
            add => this._messageReceived.Register(value);
            remove => this._messageReceived.Unregister(value);
        }
        private readonly AsyncEvent<ManagedWebSocketClient, SocketMessageEventArgs> _messageReceived;

        /// <inheritdoc />
        public event AsyncEventHandler<IWebSocketClient, SocketErrorEventArgs> ExceptionThrown
        {
            add => this._exceptionThrown.Register(value);
            remove => this._exceptionThrown.Unregister(value);
        }
        private readonly AsyncEvent<ManagedWebSocketClient, SocketErrorEventArgs> _exceptionThrown;

        private void EventErrorHandler<TArgs>(AsyncEvent<ManagedWebSocketClient, TArgs> asyncEvent, Exception ex, AsyncEventHandler<ManagedWebSocketClient, TArgs> handler, ManagedWebSocketClient sender, TArgs eventArgs)
            where TArgs : AsyncEventArgs
            => this._exceptionThrown.InvokeAsync(this, new SocketErrorEventArgs() { Exception = ex }).ConfigureAwait(false).GetAwaiter().GetResult();
        #endregion
    }
}

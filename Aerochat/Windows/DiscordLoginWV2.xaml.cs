using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace Aerochat.Windows
{
    /// <summary>
    /// Discord web login in WebView2; captures the user token from API requests (Authorization header) or localStorage fallback.
    /// </summary>
    public partial class DiscordLoginWV2 : Window
    {
        private readonly object _finishLock = new();
        private bool _coreReady;
        private WebView2? _webView;

        public string Token { get; private set; } = string.Empty;
        public bool Succeeded { get; private set; }

        public DiscordLoginWV2()
        {
            InitializeComponent();
            Loaded += DiscordLoginWV2_Loaded;
            Closed += (_, _) =>
            {
                try
                {
                    if (_webView?.CoreWebView2 != null)
                    {
                        _webView.CoreWebView2.WebResourceRequested -= CoreWebView2_WebResourceRequested;
                        _webView.CoreWebView2.NavigationCompleted -= CoreWebView2_NavigationCompleted;
                    }
                }
                catch
                {
                    // ignore
                }
            };
        }

        private async void DiscordLoginWV2_Loaded(object sender, RoutedEventArgs e)
        {
            if (_coreReady)
                return;

            _webView = new WebView2();
            WebViewHost.Child = _webView;

            try
            {
                await _webView.EnsureCoreWebView2Async();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    "Password login requires the Microsoft Edge WebView2 Runtime.\n\nInstall it from:\nhttps://go.microsoft.com/fwlink/p/?LinkId=2124703\n\n" + ex.Message,
                    "WebView2 required",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                Close();
                return;
            }

            _coreReady = true;
            var core = _webView.CoreWebView2;
            core.Settings.IsStatusBarEnabled = false;
            core.WebResourceRequested += CoreWebView2_WebResourceRequested;
            core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
            core.NavigationCompleted += CoreWebView2_NavigationCompleted;
            core.Navigate("https://discord.com/login");
        }

        private void CoreWebView2_WebResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
        {
            try
            {
                var headers = e.Request.Headers;
                string? auth = headers.GetHeader("Authorization");
                if (string.IsNullOrWhiteSpace(auth))
                    return;

                auth = auth.Trim();
                if (auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                    auth = auth.Substring(7).Trim();
                if (auth.StartsWith("Bot ", StringComparison.OrdinalIgnoreCase))
                    return;

                if (!IsPlausibleDiscordUserToken(auth))
                    return;

                TryFinish(auth);
            }
            catch
            {
                // ignore
            }
        }

        private async void CoreWebView2_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (Succeeded || !e.IsSuccess || _webView?.CoreWebView2 == null)
                return;
            try
            {
                var json = await _webView.CoreWebView2.ExecuteScriptAsync(
                    "(function(){ try { var t = localStorage.getItem('token'); return t ? t : ''; } catch(x) { return ''; } })();");
                string? token = null;
                try
                {
                    token = JsonSerializer.Deserialize<string>(json);
                }
                catch
                {
                    if (json.Length >= 2 && json[0] == '"' && json[^1] == '"')
                        token = json.Substring(1, json.Length - 2).Replace("\\\"", "\"").Replace("\\\\", "\\");
                }

                if (!string.IsNullOrEmpty(token))
                    TryFinish(token);
            }
            catch
            {
                // ignore
            }
        }

        private void TryFinish(string token)
        {
            lock (_finishLock)
            {
                if (Succeeded)
                    return;
                if (!IsPlausibleDiscordUserToken(token))
                    return;
                Token = token;
                Succeeded = true;
                Dispatcher.BeginInvoke(new Action(Close));
            }
        }

        private static bool IsPlausibleDiscordUserToken(string token)
        {
            if (string.IsNullOrWhiteSpace(token) || token.Length < 50)
                return false;
            return token.Contains(".", StringComparison.Ordinal) || token.StartsWith("mfa.", StringComparison.Ordinal);
        }
    }
}

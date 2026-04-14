using DSharpPlus.Entities;
using System.Windows;

namespace Aerochat.Windows
{
    public partial class WebView2Frame : Window
    {
        public DiscordCaptchaResponse CaptchaResponse;

        public WebView2Frame(DiscordCaptchaRequest captchaRequest)
        {
            InitializeComponent();
        }

        private void OnClose(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}

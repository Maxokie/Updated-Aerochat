using System.Windows;

namespace Aerochat.Windows
{
    /// <summary>
    /// Browser-based login stub — not supported on .NET Framework / Vista builds.
    /// </summary>
    public partial class DiscordLoginWV2 : Window
    {
        public string Token { get; set; } = string.Empty;
        public bool Succeeded { get; set; } = false;

        public DiscordLoginWV2()
        {
            InitializeComponent();
        }

        private void OnClose(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}

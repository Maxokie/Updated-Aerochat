using System;
using System.Linq;
using System.Windows;
namespace Aerochat.Windows
{
    public partial class AddFriendDialog : Window
    {
        public string UsernameInput => UsernameBox.Text.Trim();

        public AddFriendDialog()
        {
            InitializeComponent();
            Loaded += (_, _) => UsernameBox.Focus();
        }

        private void Send_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(UsernameBox.Text))
                return;
            DialogResult = true;
            Close();
        }

        /// <summary>Parses <c>name</c> or <c>name#1234</c> into username and optional discriminator.</summary>
        public static (string Username, string? Discriminator) ParseUsername(string raw)
        {
            raw = raw.Trim();
            if (string.IsNullOrEmpty(raw))
                return ("", null);

            int hash = raw.LastIndexOf('#');
            if (hash > 0 && hash < raw.Length - 1)
            {
                string disc = raw[(hash + 1)..];
                if (disc.Length > 0 && disc.Length <= 4 && disc.All(char.IsDigit))
                    return (raw[..hash].Trim(), disc);
            }

            return (raw, null);
        }
    }
}

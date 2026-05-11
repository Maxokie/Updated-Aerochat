using Aerochat.Helpers;
using Aerochat.Localization;
using System.Diagnostics;
using System.Windows;

namespace Aerochat.Windows
{
    public partial class UpdateAvailableDialog : Window
    {
        private readonly string _releaseUrl;

        public UpdateAvailableDialog(UpdateChecker.UpdateInfo info)
        {
            InitializeComponent();

            _releaseUrl = info.ReleaseUrl;

            var loc = LocalizationManager.Instance;
            PART_NewVersion.Text = string.Format(loc["UpdateAvailableNewVersion"], info.TagName);
            PART_CurrentVersion.Text = string.Format(loc["UpdateAvailableCurrentVersion"], AssemblyInfo.Version);
        }

        private void DownloadButton_Click(object sender, RoutedEventArgs e)
        {
            Process.Start(new ProcessStartInfo(_releaseUrl) { UseShellExecute = true });
            Close();
        }

        private void LaterButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}

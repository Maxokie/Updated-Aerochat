using Microsoft.Win32;
using System;
using System.Diagnostics;

namespace Aerochat.Helpers
{
    /// <summary>
    /// Registers or removes the current executable in HKCU Run for Windows logon startup.
    /// </summary>
    public static class WindowsStartupRegistration
    {
        private const string RunSubKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "Aerochat";

        public static void SetEnabled(bool enabled)
        {
            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunSubKey, writable: true);
                if (key is null) return;

                if (enabled)
                {
                    string? exePath = GetExecutablePath();
                    if (string.IsNullOrEmpty(exePath)) return;
                    key.SetValue(ValueName, exePath);
                }
                else if (key.GetValue(ValueName) != null)
                {
                    key.DeleteValue(ValueName);
                }
            }
            catch
            {
                // No permission or registry unavailable — ignore.
            }
        }

        private static string? GetExecutablePath()
        {
            try
            {
                using Process p = Process.GetCurrentProcess();
                string? path = p.MainModule?.FileName;
                if (string.IsNullOrEmpty(path)) return null;
                return path.Contains(" ") ? $"\"{path}\"" : path;
            }
            catch
            {
                return null;
            }
        }
    }
}

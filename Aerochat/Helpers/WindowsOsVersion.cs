using Microsoft.Win32;
using System;
using System.Runtime.InteropServices;

namespace Aerochat.Helpers
{
    /// <summary>OS checks for features that need Windows 10+ (e.g. WebView2 password login).</summary>
    public static class WindowsOsVersion
    {
        /// <summary>
        /// True on Windows 10, Windows 11, and Windows Server 2016+ (NT 10.0).
        /// False on Windows 8.1, 8, 7, Vista, and earlier.
        /// </summary>
        public static bool IsWindows10OrGreater()
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
                if (key?.GetValue("CurrentMajorVersionNumber") is int major)
                    return major >= 10;
            }
            catch
            {
                // ignore
            }

            try
            {
                var v = TryGetRtlVersion();
                if (v.HasValue)
                    return v.Value.dwMajorVersion >= 10;
            }
            catch
            {
                // ignore
            }

            return Environment.OSVersion.Version.Major >= 10;
        }

        [DllImport("ntdll.dll")]
        private static extern int RtlGetVersionNative(ref OsVersionInfoExW versionInfo);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct OsVersionInfoExW
        {
            public int dwOSVersionInfoSize;
            public int dwMajorVersion;
            public int dwMinorVersion;
            public int dwBuildNumber;
            public int dwPlatformId;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string szCSDVersion;
            public ushort wServicePackMajor;
            public ushort wServicePackMinor;
            public ushort wSuiteMask;
            public byte wProductType;
            public byte wReserved;
        }

        private static OsVersionInfoExW? TryGetRtlVersion()
        {
            var os = new OsVersionInfoExW
            {
                dwOSVersionInfoSize = Marshal.SizeOf(typeof(OsVersionInfoExW)),
                szCSDVersion = string.Empty
            };
            if (RtlGetVersionNative(ref os) != 0)
                return null;
            return os;
        }
    }
}

using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Net;
using System.Windows.Data;
using System.Windows;
using System.Windows.Media.Imaging;

namespace Aerochat.Helpers
{
    public class ImageConverter : IValueConverter
    {
        private const string DefaultAvatarPackUri =
            "pack://application:,,,/Aerochat;component/Resources/Frames/DefaultAvatar.png";

        /// <summary>Discord's CDN may return 403 when no browser-like User-Agent is sent (WPF's default request does not send one).</summary>
        private const string HttpUserAgent =
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";

        private static readonly ConcurrentDictionary<string, BitmapImage> HttpBitmapCache = new();

        private static BitmapImage? _defaultAvatar;
        private static BitmapImage DefaultAvatar
        {
            get
            {
                if (_defaultAvatar == null)
                {
                    _defaultAvatar = new BitmapImage(new Uri(DefaultAvatarPackUri));
                    _defaultAvatar.Freeze();
                }
                return _defaultAvatar;
            }
        }

        private static BitmapImage BitmapFromHttp(Uri uri)
        {
            string key = uri.AbsoluteUri;
            return HttpBitmapCache.GetOrAdd(key, static k =>
            {
                var requestUri = new Uri(k);
                var request = (HttpWebRequest)WebRequest.Create(requestUri);
                request.UserAgent = HttpUserAgent;
                request.Accept = "image/avif,image/webp,image/apng,image/*,*/*;q=0.8";
                request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
                request.Timeout = 20000;

                using var response = (HttpWebResponse)request.GetResponse();
                using var responseStream = response.GetResponseStream();
                if (responseStream is null)
                    throw new InvalidOperationException("Empty CDN response stream.");
                byte[] buffer;
                using (var ms = new MemoryStream())
                {
                    responseStream.CopyTo(ms);
                    buffer = ms.ToArray();
                }

                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.StreamSource = new MemoryStream(buffer);
                bmp.DecodePixelWidth = 128;
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                if (bmp.CanFreeze)
                    bmp.Freeze();
                return bmp;
            });
        }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is null)
                return DefaultAvatar;

            if (value is string uri)
            {
                if (string.IsNullOrWhiteSpace(uri))
                    return DefaultAvatar;
                try
                {
                    var absUri = new Uri(uri);
                    if (absUri.Scheme == Uri.UriSchemeHttp || absUri.Scheme == Uri.UriSchemeHttps)
                    {
                        try
                        {
                            return BitmapFromHttp(absUri);
                        }
                        catch
                        {
                            return DefaultAvatar;
                        }
                    }

                    return new BitmapImage(absUri);
                }
                catch
                {
                    return DefaultAvatar;
                }
            }

            return value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // According to https://msdn.microsoft.com/en-us/library/system.windows.data.ivalueconverter.convertback(v=vs.110).aspx#Anchor_1
            // (kudos Scott Chamberlain), if you do not support a conversion 
            // back you should return a Binding.DoNothing or a 
            // DependencyProperty.UnsetValue
            return Binding.DoNothing;
        }
    }
}
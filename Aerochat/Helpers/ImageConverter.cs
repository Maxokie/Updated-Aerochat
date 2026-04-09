using System.Globalization;
using System.Windows.Data;
using System.Windows;
using System.Windows.Media.Imaging;

namespace Aerochat.Helpers
{
    public class ImageConverter : IValueConverter
    {
        private const string DefaultAvatarPackUri =
            "pack://application:,,,/Aerochat;component/Resources/Frames/DefaultAvatar.png";

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
                    return new BitmapImage(new Uri(uri));
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
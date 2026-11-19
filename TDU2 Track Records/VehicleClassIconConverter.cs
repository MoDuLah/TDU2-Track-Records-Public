using System;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace TDU2_Track_Records
{
    public sealed class VehicleClassIconConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string code = (value as string)?.Trim().ToUpperInvariant() ?? string.Empty;
            if (!Regex.IsMatch(code, @"\A(?:A[1-7]|[BC][1-4]|MA[12])\z"))
                code = "SC";
            return new BitmapImage(new Uri("pack://application:,,,/Images/carClasses/" + code + ".png"));
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}

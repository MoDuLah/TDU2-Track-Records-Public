using System;
using System.Data;
using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace TDU2_Track_Records.Classes
{
    public class ByteArrayToImageSourceConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is DataRowView vehicle)
            {
                if (long.TryParse(vehicle["id"].ToString(), out long id) &&
                    ReadVehiclePhoto(id) is BitmapImage photo)
                    return photo;

                value = vehicle["_vehicle_image"];
            }

            if (value is byte[] imageBytes)
            {
                using (var stream = new MemoryStream(imageBytes))
                {
                    var image = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                    return image;
                }
            }
            return null;
        }

        internal static BitmapImage ReadVehiclePhoto(long id)
        {
            string folder = Path.GetDirectoryName(typeof(ByteArrayToImageSourceConverter).Assembly.Location);
            string file = Path.Combine(folder, "VehiclePhotos",
                id.ToString(CultureInfo.InvariantCulture) + ".png");
            if (!File.Exists(file))
                return null;
            try
            {
                using (var stream = File.OpenRead(file))
                {
                    var image = new BitmapImage();
                    image.BeginInit();
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    image.DecodePixelWidth = 1024;
                    image.StreamSource = stream;
                    image.EndInit();
                    image.Freeze();
                    return image;
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException ||
                                       ex is NotSupportedException || ex is FileFormatException)
            {
                return null;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TDU2_Track_Records
{
    public partial class PowerLapBoard : UserControl
    {
        internal PowerLapBoard(string trackName, string restriction, string classImagePath,
            bool rainy, bool? anticlockwise, IReadOnlyList<Lap> laps)
        {
            InitializeComponent();
            TrackTitle.Text = "@" + trackName;
            RestrictionText.Text = restriction;
            RestrictionBadge.Visibility = classImagePath == null ? Visibility.Visible : Visibility.Collapsed;
            RestrictionIcon.Visibility = classImagePath == null ? Visibility.Collapsed : Visibility.Visible;
            if (classImagePath != null)
            {
                RestrictionIcon.Source = LoadIcon(classImagePath);
                RestrictionIcon.ToolTip = restriction;
            }
            ConditionsIcon.Source = LoadIcon(rainy ? "Images/rain.png" : "Images/sun.png");
            ConditionsIcon.ToolTip = rainy ? "Rainy" : "Sunny";
            DirectionIcon.Visibility = anticlockwise.HasValue ? Visibility.Visible : Visibility.Collapsed;
            if (anticlockwise.HasValue)
            {
                DirectionIcon.Source = LoadIcon(anticlockwise.Value ? "Images/anticlockwise.png" : "Images/clockwise.png");
                DirectionIcon.ToolTip = anticlockwise.Value ? "Anticlockwise" : "Clockwise";
            }
            RecordRows.ItemsSource = laps.Select(lap => $"{lap.carName}  {lap.carClass}  {lap.LapTime}").ToList();
        }

        internal BitmapSource RenderBitmap()
        {
            Measure(new Size(Width, double.PositiveInfinity));
            Arrange(new Rect(DesiredSize));
            UpdateLayout();
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(ActualWidth), (int)Math.Ceiling(ActualHeight),
                96, 96, PixelFormats.Pbgra32);
            bitmap.Render(this);
            bitmap.Freeze();
            return bitmap;
        }

        private static BitmapImage LoadIcon(string imagePath)
        {
            return new BitmapImage(new Uri("pack://application:,,,/" + imagePath));
        }
    }
}

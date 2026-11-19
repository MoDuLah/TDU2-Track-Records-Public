using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using TDU2_Track_Records.Properties;

namespace TDU2_Track_Records
{
    public partial class powerlap : UserControl
    {
        private readonly string connectionString = Settings.Default.connectionString;
        private List<Lap> shownLaps;

        public powerlap()
        {
            InitializeComponent();
            // Keep selector styles stable when navigation detaches the page.
            ThemeManager.ApplyScopedControlStyles(this);
            List<ComboBoxItem> items = new List<ComboBoxItem>
            {
                new ComboBoxItem { ImagePath = "Images/carClasses/SC.png", Description = "All", Value = "All" },
                new ComboBoxItem { ImagePath = "Images/carClasses/A1.png", Description = "Asphalt 1", Value = "A1" },
                new ComboBoxItem { ImagePath = "Images/carClasses/A2.png", Description = "Asphalt 2", Value = "A2" },
                new ComboBoxItem { ImagePath = "Images/carClasses/A3.png", Description = "Asphalt 3", Value = "A3" },
                new ComboBoxItem { ImagePath = "Images/carClasses/A4.png", Description = "Asphalt 4", Value = "A4" },
                new ComboBoxItem { ImagePath = "Images/carClasses/A5.png", Description = "Asphalt 5", Value = "A5" },
                new ComboBoxItem { ImagePath = "Images/carClasses/A6.png", Description = "Asphalt 6", Value = "A6" },
                new ComboBoxItem { ImagePath = "Images/carClasses/A7.png", Description = "Asphalt 7", Value = "A7" },
                new ComboBoxItem { ImagePath = "Images/carClasses/B1.png", Description = "Rally 1", Value = "B1" },
                new ComboBoxItem { ImagePath = "Images/carClasses/B2.png", Description = "Rally 2", Value = "B2" },
                new ComboBoxItem { ImagePath = "Images/carClasses/B3.png", Description = "Rally 3", Value = "B3" },
                new ComboBoxItem { ImagePath = "Images/carClasses/B4.png", Description = "Rally 4", Value = "B4" },
                new ComboBoxItem { ImagePath = "Images/carClasses/C1.png", Description = "Classic 1", Value = "C1" },
                new ComboBoxItem { ImagePath = "Images/carClasses/C2.png", Description = "Classic 2", Value = "C2" },
                new ComboBoxItem { ImagePath = "Images/carClasses/C3.png", Description = "Classic 3", Value = "C3" },
                new ComboBoxItem { ImagePath = "Images/carClasses/C4.png", Description = "Classic 4", Value = "C4" },
                new ComboBoxItem { ImagePath = "Images/carClasses/MA1.png", Description = "Motorcycles 1", Value = "mA1" },
                new ComboBoxItem { ImagePath = "Images/carClasses/MA2.png", Description = "Motorcycles 2", Value = "mA2" }
            };
            ClassSelector.ItemsSource = items;
            ClassSelector.SelectedIndex = 0;
            IslandSelector.ItemsSource = new[] { "All", "Ibiza", "Hawaii", "Casino" };
            IslandSelector.SelectedIndex = 0;
            WeatherSelector.ItemsSource = new[] { "Sunny", "Rainy" };
            WeatherSelector.SelectedIndex = 0;
            DirectionSelector.ItemsSource = new[] { "Clockwise", "Anticlockwise" };
            DirectionSelector.SelectedIndex = 0;

            IslandSelector.SelectionChanged += (sender, args) => RefreshRecordedTracks();
            TrackSelector.SelectionChanged += (sender, args) => ResetResults();
            ClassSelector.SelectionChanged += (sender, args) => ResetResults();
            WeatherSelector.SelectionChanged += (sender, args) => ResetResults();
            DirectionSelector.SelectionChanged += (sender, args) => ResetResults();
            ShowLapsButton.Click += (sender, args) => LoadFastestLaps();
            ScreenshotButton.Click += SaveScreenshot;
            CopyScreenshotButton.Click += CopyScreenshot;
            Loaded += (sender, args) =>
            {
                bool reloadResults = shownLaps != null;
                RefreshRecordedTracks();
                if (reloadResults)
                    LoadFastestLaps();
            };
        }

        private void LoadFastestLaps()
        {
            if (!(TrackSelector.SelectedItem is DataRowView track))
                return;

            int trackId = Convert.ToInt32(track["id"]);
            string carClass = (ClassSelector.SelectedItem as ComboBoxItem)?.Value ?? "All";
            var lapDataAccess = new LapDataAccess(connectionString);
            List<Lap> laps = lapDataAccess.GetFastestLapForEachCar(trackId, WeatherSelector.SelectedIndex,
                DirectionSelector.SelectedIndex, carClass, Window.GetWindow(this));
            shownLaps = laps;
            BoardHost.Visibility = laps.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            BoardHost.Content = laps.Count > 0 ? CreateBoard(track) : null;
            ScreenshotButton.IsEnabled = laps.Count > 0;
            CopyScreenshotButton.IsEnabled = ScreenshotButton.IsEnabled;
            PowerLapStatus.Text = laps.Count == 0 ? "No lap times match these filters." : string.Empty;
        }

        private PowerLapBoard CreateBoard(DataRowView track)
        {
            string restriction = track["RestrictedClass"].ToString().Trim();
            string classImage = ClassSelector.Items.OfType<ComboBoxItem>()
                .FirstOrDefault(item => item.Value != "All" && item.Value.Equals(restriction, StringComparison.OrdinalIgnoreCase))
                ?.ImagePath;
            bool hasRestriction = restriction.Length > 0 && !restriction.Equals("All", StringComparison.OrdinalIgnoreCase);
            bool? anticlockwise = SupportsDirection(track) ? (bool?)(DirectionSelector.SelectedIndex == 1) : null;
            return new PowerLapBoard(track["Name"].ToString(), hasRestriction ? restriction : "Open class", classImage,
                WeatherSelector.SelectedIndex == 1, anticlockwise, shownLaps);
        }

        private void SaveScreenshot(object sender, RoutedEventArgs e)
        {
            if (!(TrackSelector.SelectedItem is DataRowView track) || shownLaps == null || shownLaps.Count == 0)
                return;

            string name = string.Join("_", track["Name"].ToString().Split(Path.GetInvalidFileNameChars()));
            var dialog = new SaveFileDialog
            {
                Title = "Save Power Laps Screenshot",
                FileName = "Power Laps - " + name,
                Filter = "PNG image (*.png)|*.png",
                DefaultExt = ".png"
            };
            var owner = Window.GetWindow(this);
            if (dialog.ShowDialog(owner) != true)
                return;

            try
            {
                var screenshot = new PngBitmapEncoder();
                screenshot.Frames.Add(BitmapFrame.Create(CreateBoard(track).RenderBitmap()));
                using (var file = File.Create(dialog.FileName))
                    screenshot.Save(file);
            }
            catch (Exception ex)
            {
                AppNotice.Show(owner, ex.Message, "Could not save screenshot", persistent: true);
            }
        }

        private void CopyScreenshot(object sender, RoutedEventArgs e)
        {
            if (!(TrackSelector.SelectedItem is DataRowView track) || shownLaps == null || shownLaps.Count == 0)
                return;

            var owner = Window.GetWindow(this);
            try
            {
                Clipboard.SetImage(CreateBoard(track).RenderBitmap());
                AppNotice.Show(owner, "Power Laps image copied to the clipboard.");
            }
            catch (Exception ex)
            {
                AppNotice.Show(owner, ex.Message, "Could not copy screenshot", persistent: true);
            }
        }

        private static bool SupportsDirection(DataRowView track)
        {
            return track != null && track["Laps"] != DBNull.Value &&
                   EventCatalog.SupportsOrientation(track["RaceType"].ToString(), Convert.ToInt32(track["Laps"]));
        }

        private void ResetResults()
        {
            var track = TrackSelector.SelectedItem as DataRowView;
            DirectionFilter.Visibility = SupportsDirection(track) ? Visibility.Visible : Visibility.Collapsed;
            ShowLapsButton.IsEnabled = track != null;
            shownLaps = null;
            BoardHost.Content = null;
            BoardHost.Visibility = Visibility.Collapsed;
            ScreenshotButton.IsEnabled = false;
            CopyScreenshotButton.IsEnabled = false;
            PowerLapStatus.Text = TrackSelector.Items.Count == 0
                ? "No timed results for this island selection."
                : "Choose your filters, then select Show Laps.";
        }

        private void RefreshRecordedTracks()
        {
            const string query = @"
                SELECT t.id, t.Name, t.RaceType, t.Laps, t.RestrictedClass
                FROM tracks t
                WHERE (@Island = 'All' OR COALESCE(NULLIF(TRIM(t.Island), ''), 'Hawaii') = @Island)
                  AND EXISTS (
                    SELECT 1 FROM records r
                    WHERE r.trackId = t.id AND " + LapDataAccess.TimedRecordFilter + @"
                )
                ORDER BY t.Name COLLATE NOCASE, t.id;";

            try
            {
                object selectedTrackId = TrackSelector.SelectedValue;
                using (var connection = new SQLiteConnection(connectionString))
                using (var command = new SQLiteCommand(query, connection))
                using (var adapter = new SQLiteDataAdapter(command))
                {
                    command.Parameters.AddWithValue("@Island", IslandSelector.SelectedItem ?? "All");
                    connection.Open();
                    var tracks = new DataTable();
                    adapter.Fill(tracks);
                    TrackSelector.ItemsSource = tracks.DefaultView;
                    TrackSelector.SelectedValue = selectedTrackId;
                    ResetResults();
                }
            }
            catch (Exception ex)
            {
                AppNotice.Show(Window.GetWindow(this), $"An error occurred while loading tracks:\n{ex.Message}",
                    "Power Laps", persistent: true);
            }
        }
    }

    public class ComboBoxItem
    {
        public string ImagePath { get; set; }
        public string Description { get; set; }
        public string Value { get; set; }
    }
    public class Lap
    {
        public string carName { get; set; }
        public string LapTime { get; set; }
        public string carClass { get; set; }
        public string WeatherConditions { get; set; }
        public string Orientation { get; set; }
 
    }

    public class LapDataAccess
    {
        private readonly string _connectionString;
        internal const string TimedRecordFilter = "TRIM(r.Total_Time) COLLATE NOCASE NOT IN ('', 'N/A')";

        public LapDataAccess(string connectionString)
        {
            _connectionString = connectionString;
        }

        public List<Lap> GetFastestLapForEachCar(int trackId, int conditions, int orientation, string carClass, Window owner = null)
        {
            List<Lap> laps = new List<Lap>();
            try
            {
                const string hasOrientation = "COALESCE(t.Laps, 0) > 1 AND t.RaceType IN ('Race SP', 'Race MP', 'Race RMP', 'Eliminator SP')";
                const string query = @"
                    SELECT r.carName, MIN(r.Total_Time) AS Fastest_Time, r.carClass, r.conditions,
                           CASE WHEN " + hasOrientation + @" THEN r.orientation END AS orientation
                    FROM records r
                    JOIN tracks t ON t.id = r.trackId
                    WHERE r.trackId = @TrackId AND r.conditions = @Conditions
                      AND (NOT (" + hasOrientation + @") OR r.orientation = @Orientation)
                      AND (@CarClass = 'All' OR r.carClass = @CarClass)
                      AND " + TimedRecordFilter + @"
                    GROUP BY r.carName, r.carClass, r.conditions
                    ORDER BY Fastest_Time ASC;";

                using (SQLiteConnection connection = new SQLiteConnection(_connectionString))
                {
                    connection.Open();
                    using (SQLiteCommand command = new SQLiteCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@TrackId", trackId);
                        command.Parameters.AddWithValue("@CarClass", carClass);
                        command.Parameters.AddWithValue("@Conditions", conditions);
                        command.Parameters.AddWithValue("@Orientation", orientation);

                        using (SQLiteDataReader reader = command.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                Lap lap = new Lap
                                {
                                    carName = reader["carName"].ToString(),
                                    carClass = reader["carClass"].ToString(),
                                    LapTime = reader["Fastest_Time"].ToString(),
                                    WeatherConditions = reader["conditions"].ToString(),
                                    Orientation = reader["orientation"].ToString()
                                };
                                laps.Add(lap);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AppNotice.Show(owner, $"An error occurred: {ex.Message}", "Power Laps", persistent: true);
            }

            return laps;
        }

    }
}

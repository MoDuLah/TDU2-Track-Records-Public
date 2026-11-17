using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TDU2_Track_Records.Properties;
using TDU2_Track_Records.Classes;

namespace TDU2_Track_Records
{
    internal sealed class GarageSearchResult
    {
        public int Number { get; set; }
        public int VehicleId { get; set; }
        public long GarageVehicleId { get; set; }
        public ImageSource Thumbnail { get; set; }
        public ImageSource UpgradeImage { get; set; }
        public string UpgradeSummary { get; set; }
        public string Brand { get; set; }
        public string CarName { get; set; }
        public string VehicleClass { get; set; }
        public string TopSpeed { get; set; }
        public string Acceleration { get; set; }
        public string Horsepower { get; set; }
        public string Garage { get; set; }
    }

    /// <summary>
    /// Interaction logic for houses.xaml
    /// </summary>
    public partial class houses : Window
    {
        readonly string connectionString = Settings.Default.connectionString;
        private readonly LocationMenu locationMenu;
        private string vehicleSearch = string.Empty;
        private bool suppressVehicleSearchChange;
        private const string OwnedHouseSource = @"
            FROM houses h LEFT JOIN save_profiles p ON p.is_active=1
            WHERE CASE WHEN p.profile_key IS NOT NULL THEN EXISTS (
                SELECT 1 FROM save_houses owned
                JOIN garage_house_mappings m ON m.house_hash=owned.house_hash
                WHERE owned.profile_key=p.profile_key AND m.house_id=h.id
            ) ELSE h.Owned=1 OR EXISTS (
                SELECT 1 FROM vehicles v WHERE CAST(v.id AS TEXT) IN (
                    h.Slot1_VehicleId, h.Slot2_VehicleId, h.Slot3_VehicleId, h.Slot4_VehicleId,
                    h.Slot5_VehicleId, h.Slot6_VehicleId, h.Slot7_VehicleId, h.Slot8_VehicleId)
            ) END";

        public houses()
        {
            InitializeComponent();
            locationMenu = new LocationMenu(LocationMenus, LocationSearchBox, LocationCountText,
                new List<LocationMenu.Location>(), "OWNED HOMES", ShowLocation);
            RefreshSaveOwnership();
        }


        private void ShowLocation(LocationMenu.Location location)
        {
            if (location != null && !string.IsNullOrWhiteSpace(vehicleSearch))
            {
                suppressVehicleSearchChange = true;
                vehicleSearch = string.Empty;
                VehicleSearchBox.Text = string.Empty;
                suppressVehicleSearchChange = false;
            }
            GarageSearchResultsPanel.Visibility = Visibility.Collapsed;
            ImagePreviewOverlay.Visibility = Visibility.Collapsed;
            bool hasLocation = location != null && LoadVehicles(location.Name);
            HouseName.Visibility = hasLocation ? Visibility.Visible : Visibility.Collapsed;
            VehiclesScrollViewer.Visibility = HouseName.Visibility;
            if (!hasLocation)
                VehiclesStackPanel.Children.Clear();
            VehiclesScrollViewer.ScrollToTop();
        }

        private void VehicleSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (suppressVehicleSearchChange)
                return;
            vehicleSearch = (VehicleSearchBox.Text ?? string.Empty).Trim();
            if (locationMenu == null)
                return;
            if (vehicleSearch.Length == 0)
            {
                GarageSearchResultsPanel.Visibility = Visibility.Collapsed;
                RefreshSaveOwnership();
                return;
            }

            locationMenu.ClearSelection();
            ShowGarageSearchResults();
        }

        private void ShowGarageSearchResults()
        {
            HouseName.Visibility = Visibility.Collapsed;
            VehiclesScrollViewer.Visibility = Visibility.Collapsed;
            GarageSearchResultsPanel.Visibility = Visibility.Visible;
            ImagePreviewOverlay.Visibility = Visibility.Collapsed;

            const string sql = @"
                SELECT g.id AS garage_vehicle_id, v.id AS vehicle_id,
                       v._brand_name, v._vehicle_name, v._vehiclecategory_name,
                       v._max_theorical_speed, v._acceleration_0_100_kph, v._acceleration_0_60_mph,
                       v._power_bhp, CAST(v._vehicle_image AS BLOB) AS vehicle_image,
                       g.acceleration_upgrade, g.top_speed_upgrade, g.braking_upgrade,
                       h.Name AS house_name, h.Island AS house_island
                FROM save_profiles p
                JOIN garage_vehicles g ON g.profile_key=p.profile_key AND g.is_present=1
                JOIN vehicles v ON v.id=g.vehicle_id
                LEFT JOIN garage_house_mappings m ON m.house_hash=g.house_hash
                LEFT JOIN houses h ON h.id=m.house_id
                WHERE p.is_active=1 AND (
                    v._vehicle_name LIKE @Pattern COLLATE NOCASE OR
                    v._brand_name LIKE @Pattern COLLATE NOCASE OR
                    v._modelfull_name LIKE @Pattern COLLATE NOCASE)
                ORDER BY v._brand_name COLLATE NOCASE, v._vehicle_name COLLATE NOCASE,
                         h.Island, h.Name, g.garage_slot;";
            var results = new List<GarageSearchResult>();
            using (var connection = new SQLiteConnection(connectionString))
            using (var command = new SQLiteCommand(sql, connection))
            {
                connection.Open();
                command.Parameters.AddWithValue("@Pattern", "%" + vehicleSearch + "%");
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        int vehicleId = Convert.ToInt32(reader["vehicle_id"]);
                        int accelerationLevel = ClampUpgradeLevel(ReadNullableInt(reader, "acceleration_upgrade") ?? 0);
                        int speedLevel = ClampUpgradeLevel(ReadNullableInt(reader, "top_speed_upgrade") ?? 0);
                        int brakingLevel = ClampUpgradeLevel(ReadNullableInt(reader, "braking_upgrade") ?? 0);
                        int displayedLevel = Math.Max(accelerationLevel, Math.Max(speedLevel, brakingLevel));
                        string summary = accelerationLevel == speedLevel && speedLevel == brakingLevel
                            ? $"Stage {displayedLevel}"
                            : $"A{accelerationLevel} / S{speedLevel} / B{brakingLevel}";
                        string house = reader["house_name"] == DBNull.Value ? "Unmapped garage" : reader["house_name"].ToString();
                        string island = reader["house_island"] == DBNull.Value ? string.Empty : reader["house_island"].ToString();
                        byte[] imageBytes = reader["vehicle_image"] == DBNull.Value ? null : (byte[])reader["vehicle_image"];
                        BitmapImage customPhoto = ByteArrayToImageSourceConverter.ReadVehiclePhoto(vehicleId);

                        results.Add(new GarageSearchResult
                        {
                            Number = results.Count + 1,
                            VehicleId = vehicleId,
                            GarageVehicleId = Convert.ToInt64(reader["garage_vehicle_id"]),
                            Thumbnail = customPhoto ?? LoadImage(imageBytes),
                            UpgradeImage = new BitmapImage(new Uri($"/Images/vehicleCard/Tune{displayedLevel}.png", UriKind.Relative)),
                            UpgradeSummary = summary,
                            Brand = reader["_brand_name"].ToString(),
                            CarName = reader["_vehicle_name"].ToString(),
                            VehicleClass = reader["_vehiclecategory_name"].ToString(),
                            TopSpeed = FormatSearchTopSpeed(reader["_max_theorical_speed"], speedLevel),
                            Acceleration = FormatSearchAcceleration(reader, accelerationLevel),
                            Horsepower = FormatSearchHorsepower(reader["_power_bhp"], speedLevel),
                            Garage = string.IsNullOrWhiteSpace(island) ? house : $"{house} ({island})"
                        });
                    }
                }
            }

            GarageSearchTable.ItemsSource = results;
            GarageSearchHeading.Text = $"GARAGE SEARCH RESULTS · “{vehicleSearch}”";
            GarageSearchEmptyText.Visibility = results.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            LocationCountText.Text = results.Count == 1 ? "1 MATCHING CAR" : $"{results.Count} MATCHING CARS";
        }

        private static int ClampUpgradeLevel(int level) => Math.Max(0, Math.Min(4, level));

        private static string FormatSearchTopSpeed(object value, int level)
        {
            if (value == DBNull.Value || !MeasurementUnits.TryParse(value.ToString(), out double metric))
                return "—";
            double upgraded = metric * new[] { 1.0, 1.02, 1.04, 1.06, 1.08 }[level];
            double displayed = MeasurementUnits.IsImperial
                ? MeasurementUnits.FromMetric(upgraded, MeasurementUnits.MilesPerKilometer)
                : upgraded;
            return $"{Math.Round(displayed, 0):0} {Settings.Default.speed}";
        }

        private static string FormatSearchAcceleration(SQLiteDataReader reader, int level)
        {
            string column = MeasurementUnits.IsImperial ? "_acceleration_0_60_mph" : "_acceleration_0_100_kph";
            object value = reader[column];
            if (value == DBNull.Value || !MeasurementUnits.TryParse(value.ToString(), out double seconds))
                return "—";
            return $"{Math.Round(seconds * new[] { 1.0, 0.98, 0.96, 0.94, 0.92 }[level], 2):0.##} s";
        }

        private static string FormatSearchHorsepower(object value, int level)
        {
            if (value == DBNull.Value || !MeasurementUnits.TryParse(value.ToString(), out double horsepower))
                return "—";
            return $"{Math.Round(horsepower * new[] { 1.0, 1.02, 1.04, 1.06, 1.08 }[level], 0):0} bhp";
        }

        private void GarageThumbnail_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var result = button?.Tag as GarageSearchResult;
            if (result == null)
                return;
            ExpandedVehicleImage.Source = result.Thumbnail;
            ExpandedVehicleCaption.Text = $"{result.Brand} · {result.CarName}";
            ImagePreviewOverlay.Visibility = Visibility.Visible;
        }

        private void ImagePreviewOverlay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            ImagePreviewOverlay.Visibility = Visibility.Collapsed;
        }

        private void ExpandedVehicleBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
        }

        internal void RefreshMeasurementUnits()
        {
            if (!string.IsNullOrWhiteSpace(vehicleSearch))
            {
                ShowGarageSearchResults();
                return;
            }
            if (locationMenu.Selected != null)
            {
                double offset = VehiclesScrollViewer.VerticalOffset;
                ShowLocation(locationMenu.Selected);
                VehiclesScrollViewer.ScrollToVerticalOffset(offset);
            }
        }

        internal void RefreshSaveOwnership()
        {
            var locations = ReadOwnedLocations();
            double offset = VehiclesScrollViewer.VerticalOffset;
            if (locationMenu.ReplaceLocations(locations))
                VehiclesScrollViewer.ScrollToVerticalOffset(offset);
        }

        private List<LocationMenu.Location> ReadOwnedLocations()
        {
            var locations = new List<LocationMenu.Location>();
            string query = "SELECT h.Name, h.Island " + OwnedHouseSource + @"
                AND (@Search='' OR
                    (p.profile_key IS NOT NULL AND EXISTS (
                        SELECT 1 FROM garage_vehicles g
                        JOIN garage_house_mappings m ON m.house_hash=g.house_hash
                        JOIN vehicles v ON v.id=g.vehicle_id
                        WHERE g.profile_key=p.profile_key AND g.is_present=1 AND m.house_id=h.id
                          AND (v._vehicle_name LIKE @Pattern COLLATE NOCASE
                               OR v._brand_name LIKE @Pattern COLLATE NOCASE
                               OR v._modelfull_name LIKE @Pattern COLLATE NOCASE)
                    )) OR
                    (p.profile_key IS NULL AND EXISTS (
                        SELECT 1 FROM vehicles v WHERE CAST(v.id AS TEXT) IN (
                            h.Slot1_VehicleId, h.Slot2_VehicleId, h.Slot3_VehicleId, h.Slot4_VehicleId,
                            h.Slot5_VehicleId, h.Slot6_VehicleId, h.Slot7_VehicleId, h.Slot8_VehicleId)
                          AND (v._vehicle_name LIKE @Pattern COLLATE NOCASE
                               OR v._brand_name LIKE @Pattern COLLATE NOCASE
                               OR v._modelfull_name LIKE @Pattern COLLATE NOCASE)
                    ))) ORDER BY h.id";
            using (var connection = new SQLiteConnection(connectionString))
            using (var command = new SQLiteCommand(query, connection))
            {
                connection.Open();
                command.Parameters.AddWithValue("@Search", vehicleSearch);
                command.Parameters.AddWithValue("@Pattern", "%" + vehicleSearch + "%");
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                        locations.Add(new LocationMenu.Location(reader["Name"].ToString(), reader["Island"].ToString(), false));
                }
            }
            return locations;
        }

        private bool LoadVehicles(string houseNameSelection)
        {
            VehiclesStackPanel.Children.Clear();
            int slotIndex = 0;
            string island = locationMenu.Selected.Island;

            using (SQLiteConnection conn = new SQLiteConnection(connectionString))
            {
                conn.Open();
                const string query = "SELECT h.*, p.profile_key " + OwnedHouseSource +
                                     " AND h.Island=@Island AND h.Name=@Name";
                using (SQLiteCommand cmd = new SQLiteCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@Name", houseNameSelection);
                    cmd.Parameters.AddWithValue("@Island", island);
                    using (SQLiteDataReader reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            // Set house details
                            HouseNameText.Text = reader["Name"].ToString();
                            HouseRating.Text = reader["Stars"].ToString();
                            HousePrice.Text = "$" + Convert.ToInt32(reader["Price"]).ToString("N0");
                            int houseMaxSlot = Convert.ToInt32(reader["Slots"]);
                            HouseMaxSlots.Text = houseMaxSlot.ToString();
                            HouseOwned.Text = "Yes";

                            // Load house image from file system
                            string houseName = reader["Name"].ToString().ToLower();
                            if (houseName.Contains("yacht"))
                            {
                                houseName = island == "Ibiza" ? "yachtibiza" : "yachthawaii";
                            }
                            BitmapImage imagePath = new BitmapImage(new Uri("/Images/houses/" + houseName + ".png", UriKind.Relative));
                            HouseImage.Source = imagePath;

                            if (reader["profile_key"] != DBNull.Value)
                            {
                                LoadSaveGarage(conn, reader["profile_key"].ToString(),
                                    Convert.ToInt32(reader["id"]), houseMaxSlot);
                                return true;
                            }

                            // Load vehicles in slots
                            for (int i = 1; i <= houseMaxSlot; i++)
                            {
                                string slotColumn = $"Slot{i}_VehicleId";
                                string vehicleId = reader[slotColumn]?.ToString();

                                if (!string.IsNullOrEmpty(vehicleId))
                                {
                                    var vehicle = GetVehicleDetails(vehicleId);
                                    if (vehicle != null)
                                        AddVehicleToUI(vehicle, slotIndex);
                                }
                                slotIndex++;
                            }
                            return true;
                        }
                    }
                }
            }
            return false;
        }

        private void LoadSaveGarage(SQLiteConnection connection, string profile, int houseId, int capacity)
        {
            const string sql = @"
                SELECT g.id, g.vehicle_id, g.garage_slot, g.odometer_metric, g.odometer_imperial,
                       g.acceleration_upgrade, g.top_speed_upgrade, g.braking_upgrade
                FROM garage_vehicles g JOIN garage_house_mappings m ON m.house_hash=g.house_hash
                WHERE g.profile_key=@Profile AND m.house_id=@House AND g.is_present=1
                ORDER BY g.garage_slot, g.garage_index;";
            var occupied = new HashSet<int>();
            using (var command = new SQLiteCommand(sql, connection))
            {
                command.Parameters.AddWithValue("@Profile", profile);
                command.Parameters.AddWithValue("@House", houseId);
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        int slot = Convert.ToInt32(reader["garage_slot"]) - 1;
                        occupied.Add(slot);
                        var vehicle = GetVehicleDetails(reader["vehicle_id"].ToString());
                        if (vehicle == null)
                        {
                            AddSaveSlotLabel(slot, $"SLOT {slot + 1:00} · VEHICLE NOT IN CATALOGUE");
                            continue;
                        }
                        vehicle.VehicleOdometerMetric = Convert.ToDouble(reader["odometer_metric"]);
                        vehicle.VehicleOdometerImperial = Convert.ToDouble(reader["odometer_imperial"]);
                        vehicle.GarageVehicleId = Convert.ToInt64(reader["id"]);
                        vehicle.AccelerationUpgrade = ReadNullableInt(reader, "acceleration_upgrade");
                        vehicle.TopSpeedUpgrade = ReadNullableInt(reader, "top_speed_upgrade");
                        vehicle.BrakingUpgrade = ReadNullableInt(reader, "braking_upgrade");
                        vehicle.VehicleUpgradeSummary = FormatUpgradeSummary(vehicle);
                        ApplyGarageUpgradeStats(vehicle);
                        if (!MatchesVehicleSearch(vehicle))
                            continue;
                        AddVehicleToUI(vehicle, slot);
                    }
                }
            }
            if (string.IsNullOrWhiteSpace(vehicleSearch))
                for (int slot = 0; slot < capacity; slot++)
                    if (!occupied.Contains(slot))
                        AddSaveSlotLabel(slot, $"SLOT {slot + 1:00} · EMPTY");
        }

        private static int? ReadNullableInt(SQLiteDataReader reader, string name)
        {
            return reader[name] == DBNull.Value ? (int?)null : Convert.ToInt32(reader[name]);
        }

        private static string FormatUpgradeSummary(VehicleDetails vehicle)
        {
            if (!vehicle.AccelerationUpgrade.HasValue || !vehicle.TopSpeedUpgrade.HasValue || !vehicle.BrakingUpgrade.HasValue)
                return "STAGE UNKNOWN";
            if (vehicle.AccelerationUpgrade == vehicle.TopSpeedUpgrade && vehicle.TopSpeedUpgrade == vehicle.BrakingUpgrade)
                return "STAGE " + vehicle.AccelerationUpgrade.Value;
            return $"TUNE A{vehicle.AccelerationUpgrade} / S{vehicle.TopSpeedUpgrade} / B{vehicle.BrakingUpgrade}";
        }

        private static void ApplyGarageUpgradeStats(VehicleDetails vehicle)
        {
            double[] accelerationFactors = { 1.0, 0.98, 0.96, 0.94, 0.92 };
            double[] maxSpeedFactors = { 1.0, 1.02, 1.04, 1.06, 1.08 };
            if (vehicle.AccelerationUpgrade.HasValue &&
                MeasurementUnits.TryParse(vehicle.Acceleration0To100Kph, out double acceleration))
            {
                int level = Math.Max(0, Math.Min(4, vehicle.AccelerationUpgrade.Value));
                vehicle.Acceleration0To100Kph = Math.Round(acceleration * accelerationFactors[level], 2).ToString("0.##");
            }
            if (vehicle.TopSpeedUpgrade.HasValue &&
                MeasurementUnits.TryParse(vehicle.VehicleTopSpeed, out double topSpeed))
            {
                int level = Math.Max(0, Math.Min(4, vehicle.TopSpeedUpgrade.Value));
                vehicle.VehicleTopSpeed = Math.Round(topSpeed * maxSpeedFactors[level], 0).ToString("0");
            }
        }

        private bool MatchesVehicleSearch(VehicleDetails vehicle)
        {
            if (string.IsNullOrWhiteSpace(vehicleSearch))
                return true;
            return (vehicle.VehicleName ?? string.Empty).IndexOf(vehicleSearch, StringComparison.OrdinalIgnoreCase) >= 0 ||
                   (vehicle.VehicleBrand ?? string.Empty).IndexOf(vehicleSearch, StringComparison.OrdinalIgnoreCase) >= 0 ||
                   (vehicle.VehicleModel ?? string.Empty).IndexOf(vehicleSearch, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void AddSaveSlotLabel(int slot, string label)
        {
            var slotLabel = new Border
            {
                Margin = new Thickness(6), Padding = new Thickness(10),
                Background = new SolidColorBrush(Color.FromRgb(26, 28, 32)),
                Child = new TextBlock { Text = label, Foreground = Brushes.LightGray, TextWrapping = TextWrapping.Wrap }
            };
            AddSlotCard(slotLabel, slot);
        }






        private VehicleDetails GetVehicleDetails(string vehicleId)
        {
            VehicleDetails vehicle = null;
            using (SQLiteConnection conn = new SQLiteConnection(connectionString))
            {
                conn.Open();
                string query = @"SELECT vehicles.*,
                                        CAST(_vehicle_image AS BLOB) AS _vehicle_image_bytes
                                 FROM vehicles
                                 WHERE Id = @Id";
                using (SQLiteCommand cmd = new SQLiteCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@Id", vehicleId);
                    using (SQLiteDataReader reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            vehicle = new VehicleDetails
                            {
                                id = Convert.ToInt32(reader["id"]),
                                VehicleName = reader["_vehicle_name"].ToString(),
                                VehicleBrand = reader["_brand_name"].ToString(),
                                VehicleModel = reader["_modelfull_name"].ToString(),
                                VehicleCategory = reader["_vehiclecategory_name"].ToString(),
                                VehicleLevel = reader["_upgrade_level"].ToString(),
                                IsOwned = Convert.ToBoolean(reader["_is_owned"].ToString()),
                                VehicleTopSpeed = reader["_max_theorical_speed"] == DBNull.Value ? string.Empty : reader["_max_theorical_speed"].ToString(),
                                Acceleration0To100Kph = reader["_acceleration_0_100_kph"] == DBNull.Value ? string.Empty : reader["_acceleration_0_100_kph"].ToString(),
                                VehicleOdometerMetric = reader["_odometer_metric"] == DBNull.Value ? 0d : Convert.ToDouble(reader["_odometer_metric"]),
                                VehicleOdometerImperial = reader["_odometer_imperial"] == DBNull.Value ? 0d : Convert.ToDouble(reader["_odometer_imperial"]),
                                VehicleImage = reader["_vehicle_image_bytes"] != DBNull.Value
                                    ? (byte[])reader["_vehicle_image_bytes"]
                                    : null

                            };
                        }
                    }
                }
            }
            return vehicle;
        }


        private void AddVehicleToUI(VehicleDetails vehicle, int slotIndex)
        {
            Border vehicleCard = new Border
            {
                Margin = new Thickness(6),
                Background = new SolidColorBrush(Color.FromRgb(26, 28, 32)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(59, 62, 69)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(2)
            };

            Grid card = new Grid();
            card.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            card.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            card.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            card.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            Grid header = new Grid { Background = new SolidColorBrush(Color.FromRgb(37, 39, 44)) };
            header.ColumnDefinitions.Add(new ColumnDefinition());
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.Children.Add(new TextBlock
            {
                Text = $"SLOT {slotIndex + 1:00}",
                Foreground = new SolidColorBrush(Color.FromRgb(255, 180, 170)),
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(10, 6, 10, 6)
            });
            Border tuneChip = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(229, 37, 33)),
                Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(5),
                Child = new TextBlock
                {
                    Text = string.IsNullOrWhiteSpace(vehicle.VehicleUpgradeSummary)
                        ? "STAGE " + vehicle.VehicleLevel
                        : vehicle.VehicleUpgradeSummary,
                    Foreground = Brushes.White, FontSize = 11, FontWeight = FontWeights.Bold
                }
            };
            Grid.SetColumn(tuneChip, 1);
            header.Children.Add(tuneChip);
            card.Children.Add(header);

            Grid identity = new Grid { Margin = new Thickness(10) };
            identity.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(145) });
            identity.ColumnDefinitions.Add(new ColumnDefinition());
            Border imageBorder = new Border { Height = 92, ClipToBounds = true, BorderBrush = new SolidColorBrush(Color.FromRgb(59, 62, 69)), BorderThickness = new Thickness(1) };
            BitmapImage photo = ByteArrayToImageSourceConverter.ReadVehiclePhoto(vehicle.id);
            imageBorder.Child = new Image
            {
                Source = photo ?? LoadImage(vehicle.VehicleImage),
                Stretch = photo != null ? Stretch.Uniform : Stretch.UniformToFill
            };
            identity.Children.Add(imageBorder);
            StackPanel identityText = new StackPanel { Margin = new Thickness(12, 3, 0, 0) };
            identityText.Children.Add(new TextBlock { Text = vehicle.VehicleName.ToUpperInvariant(), Foreground = Brushes.White, FontSize = 17, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap });
            identityText.Children.Add(new TextBlock { Text = "CLASS " + vehicle.VehicleCategory, Foreground = new SolidColorBrush(Color.FromRgb(255, 180, 170)), FontSize = 12, Margin = new Thickness(0, 4, 0, 0) });
            double odometer = MeasurementUnits.IsImperial ? vehicle.VehicleOdometerImperial : vehicle.VehicleOdometerMetric;
            identityText.Children.Add(new TextBlock { Text = $"ODO: {odometer:F2} {Settings.Default.distance}", Foreground = new SolidColorBrush(Color.FromRgb(210, 210, 215)), FontSize = 13, Margin = new Thickness(0, 4, 0, 0) });
            Grid.SetColumn(identityText, 1);
            identity.Children.Add(identityText);
            Grid.SetRow(identity, 1);
            card.Children.Add(identity);

            Grid metrics = new Grid { Margin = new Thickness(10, 0, 10, 8), Background = new SolidColorBrush(Color.FromRgb(12, 14, 18)) };
            metrics.ColumnDefinitions.Add(new ColumnDefinition()); metrics.ColumnDefinitions.Add(new ColumnDefinition()); metrics.ColumnDefinitions.Add(new ColumnDefinition());
            string topSpeed = MeasurementUnits.TryParse(vehicle.VehicleTopSpeed, out double speedKph)
                ? $"{MeasurementUnits.FromMetric(speedKph, MeasurementUnits.MilesPerKilometer):0.##} {Settings.Default.speed}"
                : "—";
            metrics.Children.Add(CreateHouseMetric("TOP SPEED", topSpeed, 0));
            metrics.Children.Add(CreateHouseMetric("0-100", string.IsNullOrWhiteSpace(vehicle.Acceleration0To100Kph) ? "—" : vehicle.Acceleration0To100Kph + " s", 1));
            metrics.Children.Add(CreateHouseMetric("CLASS", vehicle.VehicleCategory, 2));
            Grid.SetRow(metrics, 2); card.Children.Add(metrics);

            Grid actions = new Grid { Margin = new Thickness(10, 0, 10, 10) };
            actions.ColumnDefinitions.Add(new ColumnDefinition()); actions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Button viewDetailsButton = new Button { Content = "CARD / VIEW SPECS", Tag = vehicle, Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(6, 0, 0, 0) };
            viewDetailsButton.Click += ViewDetailsButton_Click;
            Grid.SetColumn(viewDetailsButton, 1); actions.Children.Add(viewDetailsButton);
            Grid.SetRow(actions, 3); card.Children.Add(actions);

            vehicleCard.Child = card;
            AddSlotCard(vehicleCard, slotIndex);
        }

        private void AddSlotCard(UIElement vehicleCard, int slotIndex)
        {
            Grid grid = VehiclesStackPanel;
            const int columns = 3;
            while (grid.ColumnDefinitions.Count < columns) grid.ColumnDefinitions.Add(new ColumnDefinition());
            int rows = (slotIndex + columns) / columns;
            while (grid.RowDefinitions.Count < rows) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetColumn(vehicleCard, slotIndex % columns);
            Grid.SetRow(vehicleCard, slotIndex / columns);
            grid.Children.Add(vehicleCard);
        }

        private static Border CreateHouseMetric(string label, string value, int column)
        {
            Border cell = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(59, 62, 69)),
                BorderThickness = new Thickness(column == 0 ? 0 : 1, 0, 0, 0),
                Padding = new Thickness(6, 5, 6, 5),
                Child = new StackPanel
                {
                    Children =
                    {
                        new TextBlock { Text = label, Foreground = new SolidColorBrush(Color.FromRgb(170, 174, 183)), FontSize = 9, HorizontalAlignment = HorizontalAlignment.Center },
                        new TextBlock { Text = value, Foreground = Brushes.White, FontSize = 13, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center }
                    }
                }
            };
            Grid.SetColumn(cell, column);
            return cell;
        }
        private void ViewDetailsButton_Click(object sender, RoutedEventArgs e)
        {
            Button button = sender as Button;
            if (button != null)
            {
                var vehicle = button.Tag as VehicleDetails;
                if (vehicle == null)
                {
                    AppNotice.Show(this, "Invalid Vehicle ID");
                    return;
                }
                else
                {
                    vehicleCard vehicleWindow = new vehicleCard(vehicle.id, vehicle.GarageVehicleId);
                    vehicleWindow.Show();
                }
            }
        }


        private BitmapImage LoadImage(byte[] imageData)
        {
            if (imageData == null || imageData.Length == 0)
            {
                // Return a placeholder image if imageData is null or empty
                var placeholderImage = new BitmapImage();
                placeholderImage.BeginInit();
                placeholderImage.UriSource = new Uri("pack://application:,,,/Images/houses/placeholder.png", UriKind.Absolute);
                placeholderImage.DecodePixelWidth = 300; // Set max width
                placeholderImage.DecodePixelHeight = 150; // Set max height
                placeholderImage.CacheOption = BitmapCacheOption.OnLoad;
                placeholderImage.EndInit();
                return placeholderImage;
            }

            using (var ms = new System.IO.MemoryStream(imageData))
            {
                BitmapImage img = new BitmapImage();
                img.BeginInit();
                img.StreamSource = ms;
                img.DecodePixelWidth = 300; // Set max width
                img.DecodePixelHeight = 150; // Set max height
                img.CacheOption = BitmapCacheOption.OnLoad;
                img.EndInit();
                return img;
            }
        }

        private void dealerships_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (Window.GetWindow(e.OriginalSource as DependencyObject) != this)
                return;

            if (e.ButtonState == MouseButtonState.Pressed)
            {
                this.DragMove();
            }
        }
        private void Settings_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var settingsWindow = new SettingsWindow();

            settingsWindow.Left = this.Left + this.Width + 10;
            settingsWindow.Top = this.Top;
            settingsWindow.ShowDialog();
        }

        private void Minimize_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            Window.GetWindow(this).WindowState = WindowState.Minimized;
        }

        private void Close_Button_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            Window.GetWindow(this)?.Close();
        }
    }
}

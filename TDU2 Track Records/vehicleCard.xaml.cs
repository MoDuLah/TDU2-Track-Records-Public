using System.Data.SQLite;
using System;
using System.Windows;
using System.Windows.Input;
using TDU2_Track_Records.Properties;
using System.IO;
using System.Windows.Media.Imaging;
using TDU2_Track_Records.Classes;

namespace TDU2_Track_Records
{
    /// <summary>
    /// Interaction logic for vehicleCard.xaml
    /// </summary>
    public partial class vehicleCard : Window
    {
        readonly string connectionString = Settings.Default.connectionString;
        public string distance, speed;
        readonly string SI = Settings.Default.system;
        double conversionSpeedFactor;
        double conversionWeightFactor;
        double conversionTorqueFactor;
        string conversionAccImage;
        //bool isCalculated = false;

        bool canUpgrade;
        private readonly int vehicleId;
        private readonly long? garageVehicleId;
        private int accelerationUpgrade;
        private int topSpeedUpgrade;
        private int brakingUpgrade;
        private bool upgradeLevelsCameFromSave;
        private bool previewChanged;

        public vehicleCard(int vehicleId, long? garageVehicleId = null)
        {
            InitializeComponent();
            this.vehicleId = vehicleId;
            this.garageVehicleId = garageVehicleId;
            ThemeManager.ApplyScopedControlStyles(this);
            Rect workArea = SystemParameters.WorkArea;
            MaxWidth = Math.Max(MinWidth, workArea.Width - 24);
            MaxHeight = Math.Max(MinHeight, workArea.Height - 48);
            Width = Math.Min(900, MaxWidth);
            Height = Math.Min(760, MaxHeight);
            conversionSpeedFactor = SI == "Imperial" ? 0.621371 : 1.0;
            conversionWeightFactor = SI == "Imperial" ? 2.20462 : 1.0;
            conversionTorqueFactor = SI == "Imperial" ? 0.737562 : 1.0;
            conversionAccImage = SI == "Imperial" ? "M" : "K";

            // Query the database to get all the details of the vehicle using the vehicle ID
            VehicleDetails vehicle = GetVehicleDetails(vehicleId);
            if (vehicle != null)
            {
                int catalogueLevel = ClampUpgradeLevel(ParseUpgradeLevel(vehicle.VehicleLevel));
                accelerationUpgrade = topSpeedUpgrade = brakingUpgrade = catalogueLevel;
                LoadGarageUpgradeLevels(vehicleId);
                this.DataContext = vehicle;
                var photo = ByteArrayToImageSourceConverter.ReadVehiclePhoto(vehicleId);
                if (photo != null)
                    VehicleHeaderPhoto.ImageSource = photo;
                UpdateStorageLocationVisibility(vehicle);
                VehiclerClass.Source = new BitmapImage(new Uri("/Images/carClasses/" + vehicle.VehicleCategory + ".png", UriKind.Relative));
                AccTimeImage.Source = new BitmapImage(new Uri($"/Images/vehicleCard/acc{conversionAccImage}ph.png", UriKind.Relative));
                SetEnginePositionVisibility(vehicle.EnginePosition);
                SetDriveTypeVisibility(vehicle.DriveName);
                SetDifficultyImages(vehicle.Difficulty);
                SetPaintStickerUpgradeVisibility(vehicle.CanPaint,vehicle.CanSticker,vehicle.CanUpgrade);
                UpdateVehicleDetails(vehicle.id);
            }
            else
            {
                AppNotice.Show(this, "No vehicle data found.");
            }
        }
        private VehicleDetails GetVehicleDetails(int vehicleId)
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
                                // Header banner
                                VehicleName = reader["_vehicle_name"].ToString(),
                                VehicleImage = reader["_vehicle_image_bytes"] != DBNull.Value
                                    ? (byte[])reader["_vehicle_image_bytes"]
                                    : null,
                                VehicleCategory = reader["_vehiclecategory_name"].ToString(),
                                VehicleLevel = reader["_upgrade_level"].ToString(),
                                // Statistics
                                StatAcc = reader["_stat_acc"].ToString(),
                                StatSpeed = reader["_stat_speed"].ToString(),
                                StatBrake = reader["_stat_break"].ToString(),
                                Difficulty = reader["_difficulty"].ToString(),
                                CanPaint = Convert.ToBoolean(reader["_vehicle_can_paint"]),
                                CanSticker = Convert.ToBoolean(reader["_vehicle_can_sticker"]),
                                CanUpgrade = Convert.ToBoolean(reader["_vehicle_can_upgrade"]),
                                // Chassis
                                EnginePosition = reader["_engine_position_name"].ToString(),
                                DriveName = reader["_drive_name"].ToString(),
                                Length = reader["_length"].ToString() + "mm",
                                Width = reader["_width"].ToString() + "mm",
                                VehicleFrontTires = reader["_tires_front"] != DBNull.Value ? reader["_tires_front"].ToString() : "N/A",
                                VehicleRearTires = reader["_tires_rear"] != DBNull.Value ? reader["_tires_rear"].ToString() : "N/A",
                                BrakesCharacteristicsFront = reader["_brakes_characteristics_front"].ToString() + " " + reader["_brakes_dim_front"].ToString() + "mm",
                                BrakesCharacteristicsRear = reader["_brakes_characteristics_rear"].ToString() + " " + reader["_brakes_dim_rear"].ToString() + "mm",
                                // Engine
                                Displacement = reader["_displacement"].ToString(),
                                EngineTypeName = reader["_engine_type_name"].ToString(),
                                TorqueNm = reader["_torque_nm"].ToString(),
                                TorqueRpm = reader["_torque_rpm"].ToString(),
                                PowerBhp = reader["_power_bhp"].ToString(),
                                PowerRpm = reader["_power_rpm"].ToString(),
                                // Performance
                                NbGears = reader["_nb_gears"].ToString() + " Gears",
                                GearboxName = reader["_gearbox_name"].ToString(),
                                MaxTheoreticalSpeed = reader["_max_theorical_speed"].ToString(),
                                Acceleration0To100Kph = reader["_acceleration_0_100_kph"].ToString(),
                                Acceleration0To60Mph = reader["_acceleration_0_60_mph"].ToString(),
                                Braking100To0 = reader["_braking_100_0"].ToString(),
                                // Information
                                Mass = reader["_mass"].ToString(),
                                PowerWeightRatio = reader["_masspower_ratio"].ToString(),
                                DealershipNameInIbiza = reader["_dealership_name_in_ibiza"].ToString(),
                                DealershipNameInHawaii = reader["_dealership_name_in_hawaii"].ToString(),
                                VehiclePrice = reader["_price"].ToString(),
                                IsOwned = Convert.ToBoolean(reader["_is_owned"].ToString()),
                                IsPurchasable = Convert.ToBoolean(reader["_is_purchasable"].ToString()),
                                IsReward = Convert.ToBoolean(reader["_is_reward"].ToString()),
                                HouseStoredNameIbiza = reader["_house_name_in_ibiza"].ToString(),
                                HouseStoredNameHawaii = reader["_house_name_in_hawaii"].ToString(),
                                VehicleUpgradeLevel = reader["_upgrade_level"].ToString()
                            };
                        }
                    }
                }
            }
            if (vehicle == null)
                return null;
            canUpgrade = vehicle.CanUpgrade;

            // Handle purchase visibility and upgrade availability
            if (!vehicle.IsPurchasable && !vehicle.IsReward)
            {
                CanPurchase.Visibility = Visibility.Collapsed;
            }
            else
            {
                CanPurchase.Visibility = Visibility.Visible;
            }

            // Handle Ibiza dealership visibility and text
            if (vehicle.DealershipNameInIbiza == "Rival")
            {
                DealerHawaii.Visibility = Visibility.Collapsed;
                DealerIbizaText.Text = "Can be won from: ";
            }
            else
            {
                DealerHawaii.Visibility = Visibility.Visible;
                DealerIbizaText.Text = "Dealership In Ibiza: ";
            }

            // Handle Hawaii dealership visibility and text with combined conditions
            if (vehicle.DealershipNameInHawaii == "Rival")
            {
                DealerIbiza.Visibility = Visibility.Collapsed;
                DealerHawaiiText.Text = "Can be won from: ";
            }
            else if (vehicle.DealershipNameInHawaii == "Casino")
            {
                DealerIbiza.Visibility = Visibility.Collapsed;
                DealerHawaiiText.Text = "Can be bought at: ";
            }
            else
            {
                DealerIbiza.Visibility = Visibility.Visible;
                DealerHawaiiText.Text = "Dealership In Hawaii: ";
            }

            return vehicle;
        }

        private void SetPaintStickerUpgradeVisibility(bool canPaints, bool canStickers, bool canUpgrades)
        {
            SetVisibility(CanPaint, CantPaint, canPaints);
            SetVisibility(CanSticker, CantSticker, canStickers);
            SetVisibility(CanUpgrade, CantUpgrade, canUpgrades);
            UpgradeStageArrow.Visibility = UpgradeIncreaseHint.Visibility = UpgradeDecreaseHint.Visibility =
                canUpgrades ? Visibility.Visible : Visibility.Collapsed;
        }
        private void SetVisibility(UIElement canElement, UIElement cantElement, bool condition)
        {
            cantElement.Visibility = condition ? Visibility.Hidden : Visibility.Visible;
            canElement.Visibility = condition ? Visibility.Visible : Visibility.Hidden;
        }
        private void SetEnginePositionVisibility(string enginePosition)
        {
            stackpanelFENG.Visibility = enginePosition == "Front" ? Visibility.Visible : Visibility.Hidden;
            stackpanelMENG.Visibility = enginePosition == "Central" ? Visibility.Visible : Visibility.Hidden;
            stackpanelRENG.Visibility = enginePosition == "Rear" ? Visibility.Visible : Visibility.Hidden;
        }
        private void SetDriveTypeVisibility(string driveType)
        {
            stackpanelFWD.Visibility = driveType == "FWD" ? Visibility.Visible : Visibility.Hidden;
            stackpanelAWD.Visibility = driveType == "AWD" ? Visibility.Visible : Visibility.Hidden;
            stackpanelRWD.Visibility = driveType == "RWD" ? Visibility.Visible : Visibility.Hidden;
        }
        private void SetDifficultyImages(string difficulty)
        {
            string[] wheelImages = new string[5];
            string onColor;

            if (difficulty == "1" || difficulty == "2")
            {
                onColor = "green";
            }
            else if (difficulty == "3")
            {
                onColor = "yellow";
            }
            else if (difficulty == "4" || difficulty == "5")
            {
                onColor = "red";
            }
            else
            {
                onColor = "off";
            }

            for (int i = 0; i < 5; i++)
            {
                wheelImages[i] = i < int.Parse(difficulty) ? $"/Images/vehicleCard/wheel_{onColor}.png" : "/Images/vehicleCard/wheel_off.png";
            }

            VehicleDifficulty1.Source = new BitmapImage(new Uri(wheelImages[0], UriKind.Relative));
            VehicleDifficulty2.Source = new BitmapImage(new Uri(wheelImages[1], UriKind.Relative));
            VehicleDifficulty3.Source = new BitmapImage(new Uri(wheelImages[2], UriKind.Relative));
            VehicleDifficulty4.Source = new BitmapImage(new Uri(wheelImages[3], UriKind.Relative));
            VehicleDifficulty5.Source = new BitmapImage(new Uri(wheelImages[4], UriKind.Relative));
        }

        private void Minimize_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            Window.GetWindow(this).WindowState = WindowState.Minimized;
        }

        private void Close_Button_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            Window.GetWindow(this)?.Close();
        }

        private void VehiclerLevel_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            if (canUpgrade == false) { return; }
            accelerationUpgrade = Math.Min(4, accelerationUpgrade + 1);
            topSpeedUpgrade = Math.Min(4, topSpeedUpgrade + 1);
            brakingUpgrade = Math.Min(4, brakingUpgrade + 1);
            previewChanged = true;
            UpdateVehicleDetails(vehicleId);
        }

        private void VehiclerLevel_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            if (canUpgrade == false) { return; }
            accelerationUpgrade = Math.Max(0, accelerationUpgrade - 1);
            topSpeedUpgrade = Math.Max(0, topSpeedUpgrade - 1);
            brakingUpgrade = Math.Max(0, brakingUpgrade - 1);
            previewChanged = true;
            UpdateVehicleDetails(vehicleId);
        }

        private void VehiclePhoto_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is VehicleDetails vehicle)
                VehicleImagePreview.Show(this, VehicleHeaderPhoto.ImageSource, vehicle.VehicleName);
        }

        private void LoadGarageUpgradeLevels(int expectedVehicleId)
        {
            if (!garageVehicleId.HasValue)
                return;
            using (var connection = new SQLiteConnection(connectionString))
            using (var command = new SQLiteCommand(@"
                SELECT acceleration_upgrade, top_speed_upgrade, braking_upgrade
                FROM garage_vehicles
                WHERE id=@GarageId AND vehicle_id=@VehicleId AND is_present=1
                  AND profile_key=COALESCE((SELECT profile_key FROM save_profiles WHERE is_active=1), '')", connection))
            {
                connection.Open();
                command.Parameters.AddWithValue("@GarageId", garageVehicleId.Value);
                command.Parameters.AddWithValue("@VehicleId", expectedVehicleId);
                using (var reader = command.ExecuteReader())
                {
                    if (!reader.Read() || reader.IsDBNull(0) || reader.IsDBNull(1) || reader.IsDBNull(2))
                        return;
                    accelerationUpgrade = ClampUpgradeLevel(reader.GetInt32(0));
                    topSpeedUpgrade = ClampUpgradeLevel(reader.GetInt32(1));
                    brakingUpgrade = ClampUpgradeLevel(reader.GetInt32(2));
                    upgradeLevelsCameFromSave = true;
                }
            }
        }

        private static int ParseUpgradeLevel(string value)
        {
            return int.TryParse(value, out int level) ? level : 0;
        }

        private static int ClampUpgradeLevel(int level) => Math.Max(0, Math.Min(4, level));

        private void UpdateVehicleDetails(int vehicleId)
        {

            VehicleDetails vehicle = GetVehicleDetails(vehicleId);
            if (vehicle != null)
            {
                // Upgrade level factors for each stat (from 0 to 4)
                double[] accelerationFactors = { 1.0, 0.98, 0.96, 0.94, 0.92 };
                double[] maxSpeedFactors = { 1.0, 1.02, 1.04, 1.06, 1.08 };
                double[] statAccFactors = { 1.0, 1.02, 1.04, 1.06, 1.08 };
                double[] statSpeedFactors = { 1.0, 1.02, 1.04, 1.06, 1.08 };
                double[] statBrakeFactors = { 1.0, 0.98, 0.96, 0.94, 0.92 };
                double[] powerFactors = { 1.0, 1.02, 1.04, 1.06, 1.08 };
                double[] massReductionFactors = { 1.0, 0.99, 0.98, 0.97, 0.96 };
                double[] priceIncreaseFactors = { 1.0, 1.13, 1.27, 1.40, 1.60 };

                try
                {
                    int displayedLevel = Math.Max(accelerationUpgrade, Math.Max(topSpeedUpgrade, brakingUpgrade));
                    vehicle.VehicleLevel = displayedLevel.ToString();
                    double basePower = Convert.ToDouble(vehicle.PowerBhp);
                    double baseAccel = SI == "Metric" ? Convert.ToDouble(vehicle.Acceleration0To100Kph) : Convert.ToDouble(vehicle.Acceleration0To60Mph);
                    double baseBraking100To0 = Convert.ToDouble(vehicle.Braking100To0);
                    double baseMaxSpeed = Convert.ToDouble(vehicle.MaxTheoreticalSpeed);
                    double baseWeight = Convert.ToDouble(vehicle.Mass);
                    //double baseStatAcc = Convert.ToDouble(vehicle.StatAcc);
                    //double baseStatSpeed = Convert.ToDouble(vehicle.StatSpeed);
                    //double baseStatBrake = Convert.ToDouble(vehicle.StatBrake);
                    double basePrice = Convert.ToDouble(vehicle.VehiclePrice);
                    double baseTorque = Convert.ToDouble(vehicle.TorqueNm);

                    // Calculate new stats based on upgrade level
                    double finalStatAcc = Math.Floor(100 - (((baseAccel / statAccFactors[accelerationUpgrade]) - 2.25) / 9.75 * 100));
                    double finalStatSpeed = Math.Floor(((baseMaxSpeed * statSpeedFactors[topSpeedUpgrade]) - 70) / 400 * 100);
                    double finalStatBrake = Math.Floor(100 - (((baseBraking100To0 * statBrakeFactors[brakingUpgrade]) - 90) / 80 * 100));
                    double finalAccel = Math.Round(baseAccel * accelerationFactors[accelerationUpgrade], 2);
                    double finalMaxSpeed = Math.Round((baseMaxSpeed * maxSpeedFactors[topSpeedUpgrade]) * conversionSpeedFactor, 0);
                    double finalWeight = Math.Round((baseWeight * massReductionFactors[displayedLevel]) * conversionWeightFactor, 0);
                    double finalPower = Math.Round(basePower * powerFactors[topSpeedUpgrade], 0);
                    double finalPrice = Math.Round(basePrice * priceIncreaseFactors[displayedLevel], 0);
                    double finalTorque = Math.Round(baseTorque * conversionTorqueFactor, 0);
                    double finalMassPower = Math.Round(finalWeight / finalPower, 3);

                    // Update vehicle stats
                    vehicle.Acceleration0To100Kph = finalAccel.ToString();
                    vehicle.TorqueNm = finalTorque.ToString();
                    vehicle.MaxTheoreticalSpeed = finalMaxSpeed.ToString();
                    vehicle.Mass = finalWeight.ToString();
                    vehicle.StatAcc = finalStatAcc.ToString();
                    vehicle.StatSpeed = finalStatSpeed.ToString();
                    vehicle.StatBrake = finalStatBrake.ToString();
                    vehicle.PowerBhp = finalPower.ToString();
                    vehicle.PowerWeightRatio = finalMassPower.ToString();
                    vehicle.VehiclePrice = "$" + finalPrice.ToString("N0");

                    // Update the UI
                    this.DataContext = vehicle;
                    UpdateStorageLocationVisibility(vehicle);
                    VehiclerLevel.Source = new BitmapImage(new Uri($"/Images/vehicleCard/Tune{vehicle.VehicleLevel}.png", UriKind.Relative));
                    string upgradeSource = upgradeLevelsCameFromSave && !previewChanged ? "SAVE" : "PREVIEW";
                    UpgradeBreakdownText.Text = accelerationUpgrade == topSpeedUpgrade && topSpeedUpgrade == brakingUpgrade
                        ? $"{upgradeSource}: STAGE {displayedLevel} / 4"
                        : $"{upgradeSource}: A {accelerationUpgrade} · S {topSpeedUpgrade} · B {brakingUpgrade}";
                }
                catch (FormatException ex)
                {
                    // Handle format exception (e.g., log error or show message)
                    AppNotice.Show(this, "Error parsing vehicle details: " + ex.Message);
                }
                catch (Exception ex)
                {
                    // Handle any other exception
                    AppNotice.Show(this, "An unexpected error occurred: " + ex.Message);
                }
            }
        }

        private void UpdateStorageLocationVisibility(VehicleDetails vehicle)
        {
            HouseIbiza.Visibility = string.IsNullOrWhiteSpace(vehicle?.HouseStoredNameIbiza)
                ? Visibility.Collapsed
                : Visibility.Visible;
            HouseHawaii.Visibility = string.IsNullOrWhiteSpace(vehicle?.HouseStoredNameHawaii)
                ? Visibility.Collapsed
                : Visibility.Visible;
        }
        public double ConvertToZeroToSixtyTime(double zeroToHundredKmhTime)
        {
            // Define the constants for the conversion
            const double scaleFactor = 0.9656; // 60 mph / 100 km/h
            const double exponent = 0.8; // Empirical constant for non-linear acceleration

            // Apply the conversion formula
            double zeroToSixtyMphTime = zeroToHundredKmhTime * Math.Pow(scaleFactor, exponent);

            return Math.Round(zeroToSixtyMphTime, 2); // Round to 2 decimal places
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                this.DragMove();
            }
        }
    }
}

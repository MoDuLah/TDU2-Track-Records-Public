using System;
using System.Data.SQLite;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using TDU2_Track_Records.Properties;
using System.Windows.Input;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TDU2_Track_Records.Classes;

namespace TDU2_Track_Records
{
    public partial class dealerships : Window
    {
        readonly string connectionString = Settings.Default.connectionString;
        public string distance, speed;
        readonly string SI = Settings.Default.system;
        public int SelectedMaxSlots = Settings.Default.dealershipSlots;
        public int dealershipColumn = Settings.Default.dealershipColumns;
        private int DealerCardColumns => Math.Max(1, dealershipColumn);
        private static double lastcardLeft;
        private static double lastcardTop;
        private readonly LocationMenu locationMenu;
        private bool updatingVehicleFilters;

        private sealed class DealerSlot
        {
            public VehicleDetails Vehicle { get; set; }
        }

        public dealerships()
        {
            InitializeComponent();
            var locations = LocationMenu.ReadLocations(connectionString,
                "SELECT Name, Island FROM dealerships ORDER BY id", separateCasinoShowrooms: true);
            locationMenu = new LocationMenu(LocationMenus, LocationSearchBox, LocationCountText,
                locations, "LOCATIONS", ShowLocation, includeCasino: true);
            locationMenu.SelectFirst();
        }



        private void ShowLocation(LocationMenu.Location location)
        {
            LoadVehicles(location.Name);
            VehiclesScrollViewer.Visibility = Visibility.Visible;
            VehiclesScrollViewer.ScrollToTop();
        }

        internal void RefreshSaveOwnership()
        {
            if (locationMenu.Selected == null)
                return;

            double offset = VehiclesScrollViewer.VerticalOffset;
            LoadVehicles(locationMenu.Selected.Name);
            VehiclesScrollViewer.ScrollToVerticalOffset(offset);
        }

        private void LoadVehicles(string dealershipName)
        {
            VehiclesStackPanel.Children.Clear(); // Clear existing UI elements before loading new ones
            VehiclesStackPanel.ColumnDefinitions.Clear();
            VehiclesStackPanel.RowDefinitions.Clear();
            for (int columnIndex = 0; columnIndex < DealerCardColumns; columnIndex++)
                VehiclesStackPanel.ColumnDefinitions.Add(new ColumnDefinition());
            int slotIndex = 0;
            string island = locationMenu.Selected.Island;

            using (SQLiteConnection conn = new SQLiteConnection(connectionString))
            {
                conn.Open();
                string query = "SELECT * FROM dealerships WHERE Island = @Island AND Name = @Name";
                using (SQLiteCommand cmd = new SQLiteCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@Name", dealershipName);
                    cmd.Parameters.AddWithValue("@Island", island);
                    using (SQLiteDataReader reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            Dealership.Visibility = Visibility.Visible;
                            DealershipTitleText.Text = dealershipName.ToUpperInvariant();
                            DealershipIslandText.Text = island.ToUpperInvariant();
                            DealershipSubtitleText.Text = $"Selected showroom on {island}. Browse the vehicles currently assigned to this location.";

                            int inStockCount = 0;
                            for (int stockIndex = 1; stockIndex <= SelectedMaxSlots; stockIndex++)
                            {
                                if (!string.IsNullOrWhiteSpace(reader[$"Slot{stockIndex}_VehicleId"]?.ToString()))
                                    inStockCount++;
                            }
                            DealershipStockText.Text = $"{inStockCount} CARS";

                            try
                            {
                                BitmapImage imagePath = new BitmapImage(new Uri($"/Images/dealerships/{island}/{dealershipName}.png", UriKind.Relative));
                                DealershipImage.Source = imagePath;
                            }
                            catch (Exception)
                            {
                                // Handle case where image is not found
                                BitmapImage fallbackImage = new BitmapImage(new Uri("/Images/dealerships/default.png", UriKind.Relative)); // Fallback image
                                DealershipImage.Source = fallbackImage;
                            }

                            var dealerSlots = new List<DealerSlot>();
                            // Read the showroom once, then apply the optional brand/model filters.
                            for (int i = 1; i <= SelectedMaxSlots; i++) // Increased from 8 to 20 slots
                            {
                                string slotColumn = $"Slot{i}_VehicleId";
                                string vehicleId = reader[slotColumn]?.ToString();
                                dealerSlots.Add(new DealerSlot
                                {
                                    Vehicle = string.IsNullOrEmpty(vehicleId) ? null : GetVehicleDetails(vehicleId)
                                });
                            }

                            UpdateVehicleFilters(dealerSlots);
                            bool filtering = IsVehicleFilterActive();
                            int visibleCount = 0;
                            foreach (DealerSlot dealerSlot in dealerSlots)
                            {
                                if (dealerSlot.Vehicle == null)
                                {
                                    continue;
                                }
                                if (!MatchesVehicleFilters(dealerSlot.Vehicle))
                                    continue;
                                AddVehicleToUI(dealerSlot.Vehicle, slotIndex++);
                                visibleCount++;
                            }
                            DealershipStockText.Text = filtering ? $"{visibleCount} / {inStockCount} CARS" : $"{inStockCount} CARS";
                        }
                    }
                }
            }
        }

        private bool IsVehicleFilterActive()
        {
            return BrandFilterComboBox.SelectedIndex > 0 || ModelFilterComboBox.SelectedIndex > 0;
        }

        private bool MatchesVehicleFilters(VehicleDetails vehicle)
        {
            string brand = BrandFilterComboBox.SelectedIndex > 0 ? BrandFilterComboBox.SelectedItem as string : null;
            string model = ModelFilterComboBox.SelectedIndex > 0 ? ModelFilterComboBox.SelectedItem as string : null;
            return (brand == null || string.Equals(vehicle.VehicleBrand, brand, StringComparison.OrdinalIgnoreCase)) &&
                   (model == null || string.Equals(vehicle.VehicleModel, model, StringComparison.OrdinalIgnoreCase));
        }

        private void UpdateVehicleFilters(IEnumerable<DealerSlot> dealerSlots)
        {
            string selectedBrand = BrandFilterComboBox.SelectedIndex > 0 ? BrandFilterComboBox.SelectedItem as string : null;
            string selectedModel = ModelFilterComboBox.SelectedIndex > 0 ? ModelFilterComboBox.SelectedItem as string : null;
            var vehicles = new List<VehicleDetails>();
            foreach (DealerSlot slot in dealerSlots)
                if (slot.Vehicle != null)
                    vehicles.Add(slot.Vehicle);

            var brands = new List<string> { "All brands" };
            brands.AddRange(vehicles.ConvertAll(vehicle => vehicle.VehicleBrand)
                .FindAll(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value));

            updatingVehicleFilters = true;
            BrandFilterComboBox.ItemsSource = brands;
            BrandFilterComboBox.SelectedItem = selectedBrand != null && brands.Contains(selectedBrand) ? selectedBrand : brands[0];

            string activeBrand = BrandFilterComboBox.SelectedIndex > 0 ? BrandFilterComboBox.SelectedItem as string : null;
            var models = new List<string> { "All models" };
            models.AddRange(vehicles
                .FindAll(vehicle => activeBrand == null || string.Equals(vehicle.VehicleBrand, activeBrand, StringComparison.OrdinalIgnoreCase))
                .ConvertAll(vehicle => vehicle.VehicleModel)
                .FindAll(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value));
            ModelFilterComboBox.ItemsSource = models;
            ModelFilterComboBox.SelectedItem = selectedModel != null && models.Contains(selectedModel) ? selectedModel : models[0];
            updatingVehicleFilters = false;
        }

        private void VehicleFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (updatingVehicleFilters || locationMenu?.Selected == null)
                return;
            LoadVehicles(locationMenu.Selected.Name);
        }





        private VehicleDetails GetVehicleDetails(string vehicleId)
        {
            VehicleDetails vehicle = null;
            using (SQLiteConnection conn = new SQLiteConnection(connectionString))
            {
                conn.Open();
                string query = @"SELECT vehicles.*, ownership.is_owned AS displayed_owned,
                                        CAST(_vehicle_image AS BLOB) AS _vehicle_image_bytes
                                 FROM vehicles
                                 JOIN displayed_vehicle_ownership ownership ON ownership.id=vehicles.id
                                 WHERE vehicles.id = @Id";
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
                                VehiclePrice = reader["_price"].ToString(),
                                VehicleCategory = reader["_vehiclecategory_name"].ToString(),
                                VehicleLevel = reader["_upgrade_level"].ToString(),
                                IsOwned = Convert.ToBoolean(reader["displayed_owned"].ToString()),
                                IsPurchasable = Convert.ToBoolean(reader["_is_purchasable"]),
                                IsReward = Convert.ToBoolean(reader["_is_reward"]),
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

            Grid cardLayout = new Grid();
            cardLayout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            cardLayout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(150) });
            cardLayout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            cardLayout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            Grid header = new Grid { Background = new SolidColorBrush(Color.FromRgb(37, 39, 44)) };
            header.ColumnDefinitions.Add(new ColumnDefinition());
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            TextBlock name = new TextBlock
            {
                Text = vehicle.VehicleName.ToUpperInvariant(),
                Foreground = Brushes.White,
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(12, 8, 8, 8)
            };
            header.Children.Add(name);
            Border classChip = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(229, 37, 33)),
                Margin = new Thickness(5),
                Padding = new Thickness(8, 3, 8, 3),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = vehicle.VehicleCategory, Foreground = Brushes.White, FontWeight = FontWeights.Bold }
            };
            Grid.SetColumn(classChip, 1);
            header.Children.Add(classChip);
            cardLayout.Children.Add(header);

            Grid imageSurface = new Grid { Background = Brushes.Black, Margin = new Thickness(10, 8, 10, 0), ClipToBounds = true };
            BitmapImage photo = ByteArrayToImageSourceConverter.ReadVehiclePhoto(vehicle.id);
            Image vehicleImage = new Image
            {
                Source = photo ?? LoadImage(vehicle.VehicleImage),
                Stretch = photo != null ? Stretch.Uniform : Stretch.UniformToFill
            };
            imageSurface.Children.Add(vehicleImage);
            if (decimal.TryParse(vehicle.VehiclePrice, out decimal price))
            {
                Border priceBadge = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(230, 12, 14, 18)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(229, 37, 33)),
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(8, 4, 8, 4),
                    Margin = new Thickness(8),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Child = new TextBlock { Text = "$" + price.ToString("N0"), Foreground = new SolidColorBrush(Color.FromRgb(255, 180, 170)), FontWeight = FontWeights.Bold }
                };
                imageSurface.Children.Add(priceBadge);
            }
            Grid.SetRow(imageSurface, 1);
            cardLayout.Children.Add(imageSurface);

            Grid metrics = new Grid { Margin = new Thickness(10, 8, 10, 8), Background = new SolidColorBrush(Color.FromRgb(12, 14, 18)) };
            metrics.ColumnDefinitions.Add(new ColumnDefinition());
            metrics.ColumnDefinitions.Add(new ColumnDefinition());
            metrics.ColumnDefinitions.Add(new ColumnDefinition());
            var priceText = new TextBlock { Text = decimal.TryParse(vehicle.VehiclePrice, out decimal parsedPrice) ? "$" + parsedPrice.ToString("N0") : "—" };
            var tuneText = new TextBlock { Text = "STAGE " + vehicle.VehicleLevel };
            var statusText = new TextBlock { Text = GetVehicleStatus(vehicle) };
            metrics.Children.Add(CreateDealerMetric("VALUE", priceText, 0));
            metrics.Children.Add(CreateDealerMetric("TUNE", tuneText, 1));
            metrics.Children.Add(CreateDealerMetric("STATUS", statusText, 2));
            Grid.SetRow(metrics, 2);
            cardLayout.Children.Add(metrics);

            Grid actions = new Grid { Margin = new Thickness(10, 0, 10, 10) };
            actions.ColumnDefinitions.Add(new ColumnDefinition());
            actions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Button viewDetailsButton = new Button
            {
                Content = "CARD / VIEW SPECS",
                FontSize = 14,
                Padding = new Thickness(12, 6, 12, 6),
                Tag = vehicle.id
            };
            viewDetailsButton.Click += ViewDetailsButton_Click;
            Grid.SetColumn(viewDetailsButton, 1);
            actions.Children.Add(viewDetailsButton);
            Grid.SetRow(actions, 3);
            cardLayout.Children.Add(actions);

            vehicleCard.Child = cardLayout;

            Grid grid = VehiclesStackPanel;
            int columns = DealerCardColumns;
            while (grid.ColumnDefinitions.Count < columns)
                grid.ColumnDefinitions.Add(new ColumnDefinition());
            int rows = (slotIndex + columns) / columns;
            while (grid.RowDefinitions.Count < rows)
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            Grid.SetColumn(vehicleCard, slotIndex % columns);
            Grid.SetRow(vehicleCard, slotIndex / columns);
            grid.Children.Add(vehicleCard);
        }

        private static string GetVehicleStatus(VehicleDetails vehicle)
        {
            if (vehicle.IsOwned)
                return "OWNED";
            return vehicle.IsPurchasable || vehicle.IsReward ? "AVAILABLE" : "UNAVAILABLE";
        }

        private static Border CreateDealerMetric(string label, TextBlock value, int column)
        {
            value.Foreground = Brushes.White;
            value.FontSize = 14;
            value.FontWeight = FontWeights.Bold;
            value.HorizontalAlignment = HorizontalAlignment.Center;

            Border cell = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(59, 62, 69)),
                BorderThickness = new Thickness(column == 0 ? 0 : 1, 0, 0, 0),
                Padding = new Thickness(8, 6, 8, 6),
                Child = new StackPanel
                {
                    Children =
                    {
                        new TextBlock { Text = label, Foreground = new SolidColorBrush(Color.FromRgb(170, 174, 183)), FontSize = 10, HorizontalAlignment = HorizontalAlignment.Center },
                        value
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
                if (button?.Tag == null || !int.TryParse(button.Tag.ToString(), out int vehicleId))
                {
                    AppNotice.Show(this, "Invalid Vehicle ID");
                    return;
                }
                else { 
                // Get the vehicle ID from the button's Tag
                vehicleId = Convert.ToInt32(button.Tag);
                    // Open the vehicleCard window and pass the vehicle ID
                    vehicleCard vehicleWindow = new vehicleCard(vehicleId);
                    // Get the screen dimensions
                    var screenWidth = SystemParameters.PrimaryScreenWidth;
                    var screenHeight = SystemParameters.PrimaryScreenHeight;

                    // Set the position to the stored values, or default to the main window position if not set
                    double targetLeft = lastcardLeft != 0 ? lastcardLeft : this.Left - 10;
                    double targetTop = lastcardTop != 0 ? lastcardTop : this.Top + (this.Height - vehicleWindow.Height) / 2;

                    // Adjust if the window would be positioned outside the screen bounds
                    if (targetLeft + vehicleWindow.Width > screenWidth)
                    {
                        targetLeft = screenWidth - vehicleWindow.Width;
                    }
                    if (targetTop + vehicleWindow.Height > screenHeight)
                    {
                        targetTop = screenHeight - vehicleWindow.Height;
                    }
                    if (targetLeft < 0)
                    {
                        targetLeft = 0;
                    }
                    if (targetTop < 0)
                    {
                        targetTop = 0;
                    }

                    // Set the window position
                    vehicleWindow.Left = targetLeft;
                    vehicleWindow.Top = targetTop;


                vehicleWindow.Closed += cardWindow_Closed;
                vehicleWindow.Show();
                }
            }
        }

        private void cardWindow_Closed(object sender, EventArgs e)
        {
            var cardWindow = sender as Window;
            // Refresh the UI after deletion
            if (cardWindow != null)
            {
                lastcardLeft = cardWindow.Left;
                lastcardTop = cardWindow.Top;
            }
            LoadVehicles(locationMenu.Selected.Name);
        }
        private BitmapImage LoadImage(byte[] imageData)
        {
            if (imageData == null || imageData.Length == 0)
            {
                // Return a placeholder image if imageData is null or empty
                var placeholderImage = new BitmapImage();
                placeholderImage.BeginInit();
                placeholderImage.UriSource = new Uri("pack://application:,,,/Images/vehicles/placeholder.png", UriKind.Absolute);
                placeholderImage.DecodePixelWidth = 300; // Set max width
                placeholderImage.DecodePixelHeight = 150; // Set max height
                placeholderImage.CacheOption = BitmapCacheOption.OnLoad;
                placeholderImage.EndInit();
                return placeholderImage;
            }

            using (var ms = new MemoryStream(imageData))
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

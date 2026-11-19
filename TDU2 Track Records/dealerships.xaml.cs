using System;
using System.Data.SQLite;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using TDU2_Track_Records.Properties;
using System.Windows.Input;
using System.IO;
using Path = System.Windows.Shapes.Path;
using System.Windows.Automation;
using System.Windows.Threading;
using TDU2_Track_Records.Classes;

namespace TDU2_Track_Records
{
    public partial class dealerships : Window
    {
        readonly string connectionString = Settings.Default.connectionString;
        public string distance, speed;
        readonly string SI = Settings.Default.system;
        public int dealershipColumn = Settings.Default.dealershipColumns;
        private static double lastcardLeft;
        private static double lastcardTop;
        private readonly LocationMenu locationMenu;
        private readonly DealershipStock stock;
        private readonly DispatcherTimer searchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };

        public dealerships()
        {
            InitializeComponent();
            stock = new DealershipStock(connectionString);
            searchTimer.Tick += (sender, args) =>
            {
                searchTimer.Stop();
                UpdateVehicleSearch();
            };
            var locations = LocationMenu.ReadLocations(connectionString,
                "SELECT Name, Island FROM dealerships ORDER BY id", separateCasinoShowrooms: true);
            locationMenu = new LocationMenu(LocationMenus, LocationSearchBox, LocationCountText,
                locations, "LOCATIONS", ShowLocation, includeCasino: true);
            VehiclesStackPanel.Columns = Math.Max(1, dealershipColumn);
            VehiclesScrollViewer.SizeChanged += (sender, args) =>
            {
                if (args.WidthChanged && args.NewSize.Width > 0)
                    VehiclesStackPanel.Columns = Math.Max(1, Math.Min(dealershipColumn, (int)(args.NewSize.Width / 280)));
            };
            locationMenu.SelectFirst();
        }



        private void ShowLocation(LocationMenu.Location location)
        {
            VehicleSearchBox.Clear();
            LoadVehicles(location.Name);
            SetSearchView(false);
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
            VehiclesStackPanel.Children.Clear();
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
                            DealershipTitleText.Text = dealershipName.ToUpperInvariant();
                            DealershipIslandText.Text = island.ToUpperInvariant();
                            DealershipSubtitleText.Text = $"Selected showroom on {island}. Browse the vehicles currently assigned to this location.";

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

                            int inStockCount = 0;
                            foreach (string slot in stock.SlotColumns)
                            {
                                string vehicleId = reader[slot].ToString();
                                if (string.IsNullOrWhiteSpace(vehicleId))
                                    continue;
                                inStockCount++;
                                VehicleDetails vehicle = GetVehicleDetails(vehicleId);
                                if (vehicle != null)
                                    AddVehicleToUI(vehicle);
                            }
                            DealershipStockText.Text = $"{inStockCount} CARS";
                        }
                    }
                }
            }
        }

        private void VehicleSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (stock == null)
                return;
            searchTimer.Stop();
            if (string.IsNullOrWhiteSpace(VehicleSearchBox.Text))
            {
                DealershipSearchTable.ItemsSource = null;
                SetSearchView(false);
            }
            else
            {
                searchTimer.Start();
            }
        }

        private void UpdateVehicleSearch()
        {
            string search = VehicleSearchBox.Text.Trim();
            if (search.Length == 0)
                return;
            try
            {
                var results = stock.Search(search);
                DealershipSearchTable.ItemsSource = results;
                VehicleSearchEmptyText.Visibility = results.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                VehicleSearchHeading.Text = $"{results.Count} {(results.Count == 1 ? "match" : "matches")} for “{search}”";
                SetSearchView(true);
            }
            catch (Exception ex)
            {
                AppNotice.Show(Window.GetWindow(VehicleSearchBox), ex.Message, "Dealership search failed", persistent: true);
            }
        }

        private void SetSearchView(bool searching)
        {
            VehicleSearchResultsPanel.Visibility = searching ? Visibility.Visible : Visibility.Collapsed;
            Dealership.Visibility = VehiclesScrollViewer.Visibility = !searching && locationMenu?.Selected != null
                ? Visibility.Visible : Visibility.Collapsed;
        }

        private void SearchDealer_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is DealershipSearchResult result)
                locationMenu.Select(result.DealerName, result.Island);
        }

        private void SearchThumbnail_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is DealershipSearchResult result)
                VehicleImagePreview.Show(Window.GetWindow((Button)sender), result.Thumbnail, result.CarName);
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

        private void AddVehicleToUI(VehicleDetails vehicle)
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
            cardLayout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
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

            var imageSurface = new Grid
            {
                Height = 180, Background = Brushes.Black, Margin = new Thickness(10, 8, 10, 0), ClipToBounds = true
            };
            ImageSource photo = ByteArrayToImageSourceConverter.ReadVehiclePhoto(vehicle.id) ?? LoadImage(vehicle.VehicleImage);
            double heightPerWidth = photo.Width > 0 ? photo.Height / photo.Width : 9.0 / 16;
            imageSurface.SizeChanged += (sender, args) =>
            {
                if (args.WidthChanged && args.NewSize.Width > 0)
                    imageSurface.Height = Math.Max(180, Math.Min(440, args.NewSize.Width * heightPerWidth));
            };
            imageSurface.Children.Add(VehicleImagePreview.CreateButton(photo, vehicle.VehicleName));
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
                    IsHitTestVisible = false,
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

            FrameworkElement paintPreview = FactoryPaintPreview.Create(vehicle.id);
            if (paintPreview != null)
            {
                Grid.SetRow(paintPreview, 3);
                cardLayout.Children.Add(paintPreview);
            }

            imageSurface.Children.Add(CreateSpecsButton(vehicle.id, vehicle.VehicleName));

            vehicleCard.Child = cardLayout;

            VehiclesStackPanel.Children.Add(vehicleCard);
        }

        private Button CreateSpecsButton(int vehicleId, string vehicleName)
        {
            var button = new Button
            {
                Tag = vehicleId,
                ToolTip = "View specs",
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(8),
                Padding = new Thickness(7),
                Cursor = Cursors.Hand,
                Content = new Path
                {
                    Data = Geometry.Parse("M2,1 H14 V17 H2 Z M5,5 H11 M5,9 H11 M5,13 H9"),
                    Stroke = Brushes.White, StrokeThickness = 1.5,
                    Width = 16, Height = 18, Stretch = Stretch.Uniform
                }
            };
            button.SetResourceReference(FrameworkElement.StyleProperty, "AppScopedButtonStyle");
            AutomationProperties.SetName(button, "View specs for " + vehicleName);
            button.Click += ViewDetailsButton_Click;
            return button;
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

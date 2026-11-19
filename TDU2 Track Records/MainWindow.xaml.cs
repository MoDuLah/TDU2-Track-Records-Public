using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Reflection;
using System.IO;
using System.Globalization;
using System.Collections;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using TDU2_Track_Records.Properties;
using System.Windows.Media.Imaging;
using System.Threading.Tasks;
using System.Windows.Threading;


namespace TDU2_Track_Records
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public int RecordsOn = 0;
        int isCalculated = 0;
        readonly string connectionString = Settings.Default.connectionString;
        private string distance => Settings.Default.distance;
        private string speed => Settings.Default.speed;
        private string SI => Settings.Default.system;
        private static readonly Regex _regex = new Regex("[^0-9.-]+"); //regex that matches disallowed text
        private const double onemile = MeasurementUnits.MilesPerKilometer;
        private double _trackLengthKm;
        private double _trackMinimumSpeedKph;
        private bool _liveSaveSyncInProgress;
        internal static readonly int[] SaveCheckIntervals = { 1, 2, 5, 10, 15, 30, 60 };
        internal static int SaveCheckIntervalSeconds =>
            SaveCheckIntervals.Contains(Settings.Default.saveCheckIntervalSeconds)
                ? Settings.Default.saveCheckIntervalSeconds : 2;
        private static readonly TimeSpan SaveStabilityDelay = TimeSpan.FromSeconds(2);
        private readonly DispatcherTimer _liveSaveTimer = new DispatcherTimer();
        private SaveProfileInfo _liveSaveProfile;
        private (DateTime Written, DateTime Created, long Size)? _syncedSave;
        private DateTime _retrySaveAfter;
        private bool _windowClosed;
        private string _loadedOdometerText = "";
        private bool _isWorkAreaMaximized;
        private Point? _titleBarPress;
        private Rect _normalWindowBounds;
        private ResizeMode _normalResizeMode = ResizeMode.CanResize;

        private const uint MonitorDefaultToNearest = 0x00000002;

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MonitorInfo
        {
            public int Size;
            public NativeRect Monitor;
            public NativeRect WorkArea;
            public uint Flags;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr windowHandle, uint flags);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetMonitorInfo(IntPtr monitorHandle, ref MonitorInfo monitorInfo);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr windowHandle, out NativeRect rectangle);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetClientRect(IntPtr windowHandle, out NativeRect rectangle);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ClientToScreen(IntPtr windowHandle, ref NativePoint point);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowPos(
            IntPtr windowHandle,
            IntPtr insertAfter,
            int x,
            int y,
            int width,
            int height,
            uint flags);

        private const uint SetWindowPosNoZOrder = 0x0004;
        private const uint SetWindowPosNoActivate = 0x0010;
        private bool _isRefreshingEventTypes;
        private bool _vehicleTooltipGeneratorHooked;
        private readonly Dictionary<string, EmbeddedViewState> _embeddedViews = new Dictionary<string, EmbeddedViewState>();
        private powerlap _powerLapsView;
        private string _activeEmbeddedViewIdentifier;
        private Border _activeNavigationButton;

        private sealed class EmbeddedViewState
        {
            public Window Window { get; set; }
            public FrameworkElement Container { get; set; }
        }

        public MainWindow()
        {

            InitializeComponent();
            AppNotice.Attach(this, NoticeHost, MainScaleViewbox);
            InitializeHudLayout();
            InitializeSaveProfiles();
            Loaded += MainWindow_Loaded;
            _liveSaveTimer.Tick += LiveSaveTimer_Tick;
            RefreshSaveCheckInterval();
            SizeChanged += MainWindow_SizeChanged;
            StateChanged += MainWindow_StateChanged;

            // Bind the TextBox to the TextBlock
            Binding binding = new Binding
            {
                Source = LapsTextBlock,
                Path = new PropertyPath("Text"),
                Mode = BindingMode.OneWay // TextBox will only receive text from the TextBlock
            };
            LapsTextBox.SetBinding(TextBox.TextProperty, binding);
            // bind ends here
            BindClassComboBox(combo_Class);
            RefreshRaceTypeComboBox();
            calc_Total_Odometer();
            HideBoxes();

        }

        private void InitializeHudLayout()
        {
            ApplyScopedControlStyles();
            EventGroupBox.Header = "EVENT SELECTION";
            VehicleGroupBox.Header = "VEHICLE SELECTION & GARAGE";
            RaceDataGroupBox.Header = "RACE DATA ENTRY";

            IslandSelectorGroup.Header = "TARGET ISLAND";
            ClassSelectorGroup.Header = "VEHICLE CLASS REQUIREMENT";
            EventTypeSelectorGroup.Header = "EVENT TYPE & DISCIPLINE";
            EventNameSelectorGroup.Header = "CIRCUIT / SPRINT NAME";
            VehicleNameSelectorGroup.Header = "ACTIVE CAR IN GARAGE";
            UpdateOdometerHeader();
            VehicleRecordGroup.Header = "VEHICLE RECORD PB";
            RacesRanGroup.Header = "RACES RAN";
            lbl_TotalOdometer.Header = "TOTAL DISTANCE COVERED";
            RaceTimeGroupBox.Header = "Total Race Time (MM:SS.MM)";

            PositionInGrid(IslandSelectorGroup, 0, 0, 2);
            PositionInGrid(ClassSelectorGroup, 1, 0, 2);
            PositionInGrid(EventTypeSelectorGroup, 0, 0, 2);
            PositionInGrid(EventNameSelectorGroup, 1, 0, 2);
            PositionInGrid(VehicleRecordGroup, 0, 2, 1);
            PositionInGrid(RacesRanGroup, 1, 0, 1);
            PositionInGrid(lbl_TotalOdometer, 1, 2, 1);

            ApplyEventDetailWidths();
            SetActiveNavigation(EventsNavButton);
            btn_reset.Content = "RESET FIELDS";
            btn_submit.Content = "SUBMIT RECORD";

            ShowAssemblyVersion();
            UpdateRaceTimePreview();
            UpdateHudStatus();
        }

        private void ApplyScopedControlStyles()
        {
            ThemeManager.ApplyScopedControlStyles(this);

            Style comboBoxItemStyle = Application.Current?.TryFindResource("AppScopedComboBoxItemStyle") as Style;
            if (combo_Vehicle != null && comboBoxItemStyle != null)
            {
                combo_Vehicle.ItemContainerStyle = comboBoxItemStyle;
                if (!_vehicleTooltipGeneratorHooked)
                {
                    combo_Vehicle.ItemContainerGenerator.StatusChanged += VehicleItemContainerGenerator_StatusChanged;
                    _vehicleTooltipGeneratorHooked = true;
                }
                ApplyVehicleItemTooltips();
            }
        }

        private void VehicleItemContainerGenerator_StatusChanged(object sender, EventArgs e)
        {
            ApplyVehicleItemTooltips();
        }

        private void ApplyVehicleItemTooltips()
        {
            if (combo_Vehicle?.ItemContainerGenerator.Status != GeneratorStatus.ContainersGenerated)
                return;

            for (int index = 0; index < combo_Vehicle.Items.Count; index++)
            {
                if (combo_Vehicle.ItemContainerGenerator.ContainerFromIndex(index) is System.Windows.Controls.ComboBoxItem item)
                {
                    BindingOperations.SetBinding(
                        item,
                        FrameworkElement.ToolTipProperty,
                        new Binding("_vehicle_location"));
                }
            }
        }

        private static void PositionInGrid(UIElement element, int row, int column, int columnSpan)
        {
            Grid.SetRow(element, row);
            Grid.SetColumn(element, column);
            Grid.SetColumnSpan(element, columnSpan);
        }

        private void ApplyEventDetailWidths()
        {
            IslandSelectorGroup.MinWidth = 0;
            ClassSelectorGroup.MinWidth = 0;
            EventTypeSelectorGroup.MinWidth = 0;
            combo_Class.MinWidth = 0;
            combo_Track.MinWidth = 0;

            InfoGroupBox.MinWidth = 0;
            OrientationGroupBox.MinWidth = 0;
            WeatherGroupBox.MinWidth = 0;
            LapLengthGroupBox.MinWidth = 0;
            LapsGroupBox.MinWidth = 0;
            RaceLengthGroupBox.MinWidth = 0;
            ClassRestictionGroupBox.MinWidth = 0;
            VehicleRestictionGroupBox.MinWidth = 0;
            MinimumSpeedGroupBox.MinWidth = 0;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateHudStatus();
            _liveSaveTimer.Start();
            _ = CheckLiveSaveAsync();
        }

        private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_isWorkAreaMaximized)
                ApplyResponsiveWindowLayout(true, e.NewSize);
            UpdateHudStatus();
        }

        private void MainWindow_StateChanged(object sender, EventArgs e)
        {
            UpdateMaximizeVisual();
            UpdateHudStatus();
        }


        private void UpdateMaximizeVisual()
        {
            if (Maximize_Button == null)
                return;

            bool isMaximized = _isWorkAreaMaximized ||
                               WindowState == WindowState.Maximized;

            // Tell the XAML image style which icon state to display.
            Maximize_Button.Tag = isMaximized;

            // Update tooltip.
            Maximize_Button.ToolTip = isMaximized
                ? "Restore"
                : "Maximize";

            // Adjust scaling.
            MainScaleViewbox.StretchDirection = isMaximized
                ? StretchDirection.Both
                : StretchDirection.DownOnly;

            // Update background.
            Background = isMaximized
                ? (TryFindResource("AppSectionBodyBrush") as Brush
                   ?? Brushes.Black)
                : Brushes.Transparent;

            // Preserve responsive layout behaviour.
            ApplyResponsiveWindowLayout(isMaximized, RenderSize);
        }


        private void ApplyResponsiveWindowLayout(bool isMaximized, Size viewport)
        {
            if (MainShellBorder == null || EventLayoutColumn == null ||
                VehicleLayoutColumn == null || RaceLayoutColumn == null ||
                MainContentRow == null || EmbeddedContentSurface == null)
                return;

            if (isMaximized)
            {
                double scale = Math.Max(1, Math.Min(1.8, Math.Min(viewport.Width / 1600, viewport.Height / 900)));
                MainShellBorder.Width = viewport.Width / scale;
                MainShellBorder.Height = viewport.Height / scale;
                EventLayoutColumn.Width = new GridLength(350, GridUnitType.Star);
                VehicleLayoutColumn.Width = new GridLength(470, GridUnitType.Star);
                RaceLayoutColumn.Width = new GridLength(390, GridUnitType.Star);
                MainContentRow.Height = new GridLength(1, GridUnitType.Star);
                EventGroupBox.MinHeight = 0;
                EmbeddedContentSurface.ClearValue(FrameworkElement.HeightProperty);
            }
            else
            {
                MainShellBorder.ClearValue(FrameworkElement.WidthProperty);
                MainShellBorder.ClearValue(FrameworkElement.HeightProperty);
                EventLayoutColumn.SetResourceReference(ColumnDefinition.WidthProperty, "AppEventColumnWidth");
                VehicleLayoutColumn.SetResourceReference(ColumnDefinition.WidthProperty, "AppVehicleColumnWidth");
                RaceLayoutColumn.SetResourceReference(ColumnDefinition.WidthProperty, "AppRaceColumnWidth");
                MainContentRow.Height = GridLength.Auto;
                EventGroupBox.ClearValue(MinHeightProperty);
                EmbeddedContentSurface.SetResourceReference(
                    FrameworkElement.HeightProperty,
                    "AppEmbeddedContentHeight");
            }

            foreach (EmbeddedViewState embeddedView in _embeddedViews.Values)
            {
                if (!(embeddedView.Container is Panel container))
                    continue;

                foreach (Viewbox viewbox in container.Children.OfType<Viewbox>())
                {
                    viewbox.StretchDirection = isMaximized
                        ? StretchDirection.Both
                        : StretchDirection.DownOnly;
                }
            }
        }

        private void UpdateHudStatus()
        {
            if (HudResolutionText == null)
                return;

            HudResolutionText.Text = $"{Math.Round(ActualWidth)}x{Math.Round(ActualHeight)}";
        }
        private void BindRaceTypeComboBox(ComboBox comboBox, string selectedIsland, string selectedClass)
        {
            List<ComboBoxItem> raceTypes = new List<ComboBoxItem>();

            using (SQLiteConnection connection = new SQLiteConnection(connectionString))
            {
                connection.Open();
                const string query = EventCatalog.EligibleTracks + @"
                    SELECT rt.type_Description, rt.type_Value, rt.type_Icon
                    FROM race_types rt
                    WHERE EXISTS (
                        SELECT 1 FROM eligible t
                        WHERE t.RaceMode = rt.type_Value
                    )
                    ORDER BY rt.id;";

                using (SQLiteCommand command = new SQLiteCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@Island", selectedIsland);
                    command.Parameters.AddWithValue("@SelectedClass", selectedClass ?? string.Empty);
                using (SQLiteDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string description = reader["type_Description"].ToString();
                        string value = reader["type_Value"].ToString();
                        string iconFilename = reader["type_Icon"].ToString();

                        // Construct the path for the icon image
                        string imagePath = Path.Combine("Images/ico/events", iconFilename);

                        // Add a new ComboBoxItem to the list
                        raceTypes.Add(new ComboBoxItem
                        {
                            ImagePath = imagePath,
                            Description = EventCatalog.RaceTypeLabel(value, description),
                            Value = value
                        });
                    }
                }
                }
            }

            comboBox.ItemsSource = raceTypes;
            comboBox.SelectedIndex = -1; // No race type selected initially
        }

        private void RefreshRaceTypeComboBox()
        {
            ComboBoxItem selectedClass = combo_Class.SelectedItem as ComboBoxItem;
            string selectedIsland = GetSelectedIsland();

            _isRefreshingEventTypes = true;
            try
            {
                combo_Type.SelectedIndex = -1;
                combo_Type.ItemsSource = null;
                combo_Track.SelectedIndex = -1;
                combo_Track.ItemsSource = null;
                combo_Track.IsEnabled = false;

                if (selectedClass == null || string.IsNullOrWhiteSpace(selectedIsland))
                {
                    combo_Type.IsEnabled = false;
                    return;
                }

                BindRaceTypeComboBox(combo_Type, selectedIsland, selectedClass.Value ?? string.Empty);
                combo_Type.IsEnabled = combo_Type.Items.Count > 0;
            }
            finally
            {
                _isRefreshingEventTypes = false;
            }
        }
        private void BindClassComboBox(ComboBox comboBox)
        {
            List<ComboBoxItem> availableClasses = new List<ComboBoxItem>();

            using (SQLiteConnection connection = new SQLiteConnection(connectionString))
            {
                connection.Open();
                string query = "SELECT category_description, category_value FROM vehicle_categories WHERE category_active = 'true' ;";

                using (SQLiteCommand command = new SQLiteCommand(query, connection))
                using (SQLiteDataReader reader = command.ExecuteReader())
                {
                    // Adding a default item "All" at the beginning
                    availableClasses.Add(new ComboBoxItem
                    {
                        ImagePath = "Images/carClasses/SC.png",
                        Description = "All",
                        Value = ""
                    });

                    while (reader.Read())
                    {
                        string description = reader["category_description"].ToString();
                        string value = reader["category_value"].ToString();

                        // Construct the path for the icon image
                        string imagePath = Path.Combine("Images/carClasses", value + ".png");

                        // Add a new ComboBoxItem to the list
                        availableClasses.Add(new ComboBoxItem
                        {
                            ImagePath = imagePath,
                            Description = description,
                            Value = value
                        });
                    }
                }
            }

            comboBox.ItemsSource = availableClasses;
            comboBox.SelectedIndex = -1; // No class selected initially
        }

        private void combo_Type_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isRefreshingEventTypes || _isResetting)
                return;

            isCalculated = 0;
            HideAll();
            ComboBoxItem selectedType = (ComboBoxItem)combo_Type.SelectedItem;
            ComboBoxItem selectedClass = (ComboBoxItem)combo_Class.SelectedItem;

            if (selectedType != null && selectedClass != null)
            {
                string classValue = selectedClass.Value.ToString();

                BindVehicleComboBox(combo_Vehicle, classValue);
                combo_Vehicle.SelectedIndex = -1;
                combo_Vehicle.IsEnabled = true;
                RefreshTrackComboBox();
            }
            else
            {
                combo_Vehicle.IsEnabled = false;
                combo_Track.IsEnabled = false;
            }
            if (combo_Track.SelectedIndex != -1)
            {
                combo_Track.SelectedIndex = -1; // Only reset if necessary
            }
            if (combo_Vehicle.SelectedIndex != -1)
            {
                combo_Vehicle.SelectedIndex = -1;
            }
        }
        private bool _isResetting = false; // Flag to prevent re-entrancy

        private void combo_Class_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isResetting)
                return; // Prevent re-entrancy if we're already resetting

            _isResetting = true;

            try
            {
                HideBoxes();
                //// Reset other controls


                cb_conditions.IsChecked = false;
                keep.IsChecked = false;
                ResetRaceFields();
                // Arrays of TextBoxes for laps and average lap speeds
                lap_Length.Text = "";
                LapLengthGroupBox.Visibility = Visibility.Collapsed;
                race_Length.Text = "";
                RaceLength_Distance_Unit.Text = "";
                RaceLengthGroupBox.Visibility = Visibility.Collapsed;
                txt_odometer.Text = "";
                txt_lrec.Text = "";
                txt_lrecCar.Text = "";
                txt_rRan.Text = "";
                txt_Progress_1.Text = "";
                txt_Progress_2.Text = "";
                txt_carPB.Text = "";
                average_LapTime.Text = "";
                AverageRace_Speed.Text = "";

                RefreshRaceTypeComboBox();

                ComboBoxItem selectedRaceType = (ComboBoxItem)combo_Type.SelectedItem;
                ComboBoxItem selectedClass = (ComboBoxItem)combo_Class.SelectedItem;

                if (selectedRaceType != null && selectedClass != null)
                {
                    string classValue = selectedClass.Value.ToString();

                    // Bind vehicles and tracks based on the selected class (or "All")
                    BindVehicleComboBox(combo_Vehicle, classValue);
                    RefreshTrackComboBox();

                    combo_Vehicle.IsEnabled = true;
                }
                else
                {
                    combo_Vehicle.IsEnabled = false;
                    combo_Track.IsEnabled = false;
                }
                if (combo_Track.SelectedIndex != -1)
                {
                    combo_Track.SelectedIndex = -1; // Only reset if necessary
                }
                if (combo_Vehicle.SelectedIndex != -1)
                {
                    combo_Vehicle.SelectedIndex = -1;
                }
            }
            finally
            {
                _isResetting = false; // Ensure the flag is reset
            }
        }
        private void combo_Island_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded)
                return;

            isCalculated = 0;
            HideAll();
            RefreshRaceTypeComboBox();
        }

        private string GetSelectedIsland()
        {
            return (combo_Island.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content?.ToString();
        }

        private void RefreshTrackComboBox()
        {
            ComboBoxItem selectedRaceType = combo_Type.SelectedItem as ComboBoxItem;
            ComboBoxItem selectedClass = combo_Class.SelectedItem as ComboBoxItem;
            string selectedIsland = GetSelectedIsland();

            combo_Track.SelectedIndex = -1;
            if (selectedRaceType == null || selectedClass == null || string.IsNullOrWhiteSpace(selectedIsland))
            {
                combo_Track.ItemsSource = null;
                combo_Track.IsEnabled = false;
                return;
            }

            BindTrackComboBox(combo_Track, selectedRaceType.Value, selectedClass.Value, selectedIsland);
            combo_Track.IsEnabled = true;
        }

        private void BindTrackComboBox(ComboBox comboBox, string selectedRaceType, string selectedClass, string selectedIsland)
        {
            ExecuteQuery(EventCatalog.SelectEvents, "tracks", dataSet =>
            {
                SetComboBoxSource(comboBox, dataSet, "DisplayName", "id");
            },
            ex => AppNotice.Show(this, $"An error occurred while loading tracks:\n{ex.Message}"),
            ("@RaceType", selectedRaceType),
            ("@Island", selectedIsland),
            ("@SelectedClass", selectedClass ?? string.Empty));
        }
        private void ShowAssemblyVersion()
        {
            try
            {
                // Get the assembly version
                Version version = Assembly.GetExecutingAssembly().GetName().Version;
                string fullVersion = $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}";

                // Set the TextBlock's text
                Currentversion.Text = $"TDU2 TRACK RECORDS PUBLIC v{fullVersion}";
                HudStatusVersion.Text = $"TDU2 Track Records v{fullVersion}";
            }
            catch (Exception ex)
            {
                // Handle any exception and display a fallback text
                Currentversion.Text = $"Error retrieving version: {ex.Message}";
                HudStatusVersion.Text = "TDU2 Track Records";
            }
        }
        private void BindVehicleComboBox(ComboBox comboBox, string selectedClass = null, string restrictedVehicleId = null)
        {
            const string query = @"
                SELECT v.id,
                       v._vehicle_name,
                       v._vehiclecategory_name,
                       CAST(v._vehicle_image AS BLOB) AS _vehicle_image,
                       g.id AS garage_vehicle_id,
                       g.instance_id AS garage_instance_id,
                       g.garage_index,
                       g.garage_slot,
                       g.garage_capacity,
                       g.odometer_metric AS garage_odometer_metric,
                       g.odometer_imperial AS garage_odometer_imperial,
                       v._vehicle_name AS _vehicle_display_name,
                       CASE WHEN h.id IS NOT NULL
                            THEN h.Island || ' — ' || h.Name || ', garage slot ' || g.garage_slot || ' of ' || g.garage_capacity
                            ELSE 'Garage name unresolved, local slot ' || g.garage_slot || ' of ' || g.garage_capacity
                       END AS _vehicle_location
                FROM garage_vehicles g
                JOIN save_profiles p ON p.profile_key=g.profile_key AND p.is_active=1
                JOIN vehicles v ON v.id=g.vehicle_id
                LEFT JOIN houses h ON h.id=g.house_id
                WHERE g.is_present=1
                  AND v._is_active='true'
                  AND (@SelectedClass='' OR v._vehiclecategory_name=@SelectedClass COLLATE NOCASE
                       OR (LENGTH(@SelectedClass)=1 AND v._vehiclecategory_name LIKE @SelectedClass || '%'))
                  AND (@RestrictedVehicle='' OR v.id=CAST(@RestrictedVehicle AS INTEGER))

                UNION ALL

                SELECT v.id,
                       v._vehicle_name,
                       v._vehiclecategory_name,
                       CAST(v._vehicle_image AS BLOB) AS _vehicle_image,
                       NULL AS garage_vehicle_id,
                       NULL AS garage_instance_id,
                       NULL AS garage_index,
                       NULL AS garage_slot,
                       NULL AS garage_capacity,
                       NULL AS garage_odometer_metric,
                       NULL AS garage_odometer_imperial,
                       v._vehicle_name AS _vehicle_display_name,
                       'Garage location not imported' AS _vehicle_location
                FROM vehicles v
                JOIN displayed_vehicle_ownership ownership ON ownership.id=v.id
                WHERE v._is_active='true'
                  AND ownership.is_owned='true'
                  AND (@SelectedClass='' OR v._vehiclecategory_name=@SelectedClass COLLATE NOCASE
                       OR (LENGTH(@SelectedClass)=1 AND v._vehiclecategory_name LIKE @SelectedClass || '%'))
                  AND (@RestrictedVehicle='' OR v.id=CAST(@RestrictedVehicle AS INTEGER))
                  AND NOT EXISTS (
                      SELECT 1
                      FROM garage_vehicles active_garage
                      JOIN save_profiles active_profile
                        ON active_profile.profile_key=active_garage.profile_key AND active_profile.is_active=1
                      WHERE active_garage.is_present=1
                  )
                ORDER BY _vehicle_display_name ASC;";

            ExecuteQuery(query, "vehicles", dataSet =>
            {
                SetComboBoxSource(comboBox, dataSet, "_vehicle_display_name", "id");
            },
            ex => AppNotice.Show(this, $"An error occurred while loading vehicles:\n{ex.Message}"),
            ("@SelectedClass", selectedClass ?? ""),
            ("@RestrictedVehicle", restrictedVehicleId ?? ""));
        }

        public void RefreshGarageVehicleChoices(bool preserveOdometerDraft = false)
        {
            var previousVehicle = GetSelectedVehicleRow();
            string draft = txt_odometer.Text;
            bool hasDraft = draft != _loadedOdometerText;
            ComboBoxItem selectedClass = combo_Class.SelectedItem as ComboBoxItem;
            if (combo_Track.SelectedItem is DataRowView track)
            {
                ApplyRestrictions(track, refreshVehicles: true);
            }
            else if (selectedClass != null)
            {
                BindVehicleComboBox(combo_Vehicle, selectedClass.Value);
                RestoreVehicleSelection(previousVehicle);
                combo_Vehicle.IsEnabled = combo_Vehicle.Items.Count > 0;
            }
            if (preserveOdometerDraft && hasDraft && SameVehicle(previousVehicle, GetSelectedVehicleRow()))
                txt_odometer.Text = draft;
            calc_Total_Odometer(focusField: !preserveOdometerDraft);
        }

        private void InitializeSaveProfiles()
        {
            try
            {
                List<SaveProfileInfo> profiles = SaveGameGarageImport.RefreshDiscoveredProfiles(connectionString);
                SaveProfileInfo activeProfile = profiles.FirstOrDefault(profile => profile.IsActive && profile.SourceAvailable);
                SetLiveSaveProfile(activeProfile ?? profiles.FirstOrDefault(profile => profile.IsActive));
                if (activeProfile != null)
                {
                    Settings.Default.liveSavePath = activeProfile.DataPath;
                    Settings.Default.Save();
                }
            }
            catch (Exception ex)
            {
                AppNotice.Show(this, $"Save profiles could not be discovered.\n\n{ex.Message}",
                    "Profile discovery failed");
            }
        }

        public void RefreshActiveSaveProfile()
        {
            SaveProfileInfo activeProfile = SaveGameGarageImport.GetProfiles(connectionString)
                .FirstOrDefault(profile => profile.IsActive);
            SetLiveSaveProfile(activeProfile);
            RefreshGarageVehicleChoices();
            RefreshSaveViews();
            _ = CheckLiveSaveAsync();
        }

        private void SetLiveSaveProfile(SaveProfileInfo profile)
        {
            _liveSaveProfile = profile;
            _syncedSave = null;
            _retrySaveAfter = DateTime.MinValue;
            ActiveProfileText.ToolTip = null;
            UpdateActiveProfileChrome(profile);
        }

        internal void RefreshSaveCheckInterval()
        {
            _liveSaveTimer.Interval = TimeSpan.FromSeconds(SaveCheckIntervalSeconds);
        }

        private void UpdateActiveProfileChrome(SaveProfileInfo profile)
        {
            if (ActiveProfileText == null)
                return;

            if (profile == null)
            {
                ActiveProfileText.Text = "No active save profile";
                return;
            }

            ActiveProfileText.Text = profile.SourceAvailable
                ? $"Profile: {profile.ProfileName} / Live Sync"
                : $"Profile: {profile.ProfileName} / Save Missing";
        }

        private async void LiveSaveTimer_Tick(object sender, EventArgs e) => await CheckLiveSaveAsync();

        private async Task CheckLiveSaveAsync()
        {
            var profile = _liveSaveProfile;
            if (_windowClosed || _liveSaveSyncInProgress || profile == null || DateTime.UtcNow < _retrySaveAfter)
                return;

            try
            {
                var file = new FileInfo(profile.DataPath);
                if (!file.Exists)
                {
                    _syncedSave = null;
                    profile.SourceAvailable = false;
                    UpdateActiveProfileChrome(profile);
                    return;
                }

                var stamp = (file.LastWriteTimeUtc, file.CreationTimeUtc, file.Length);
                if (_syncedSave == stamp)
                    return;

                _liveSaveSyncInProgress = true;
                await Task.Delay(SaveStabilityDelay);
                if (_windowClosed || !ReferenceEquals(profile, _liveSaveProfile))
                    return;
                file.Refresh();
                if (!file.Exists || stamp != (file.LastWriteTimeUtc, file.CreationTimeUtc, file.Length))
                    return;

                GarageImportResult result = await Task.Run(() =>
                    SaveGameGarageImport.NeedsSync(connectionString, profile.DataPath)
                        ? SaveGameGarageImport.ImportLiveData(connectionString, profile.DataPath,
                            activateProfile: false, requiredActiveProfile: profile.ProfileKey)
                        : null);
                if (_windowClosed || !ReferenceEquals(profile, _liveSaveProfile))
                    return;

                _syncedSave = stamp;
                profile.SourceAvailable = true;
                UpdateActiveProfileChrome(profile);
                ActiveProfileText.ToolTip = $"DATA checked at {DateTime.Now:T}";
                if (result?.GarageChanged == true)
                    RefreshGarageVehicleChoices(preserveOdometerDraft: true);
                if (result != null)
                    RefreshSaveViews();
            }
            catch (Exception ex)
            {
                if (!_windowClosed && ReferenceEquals(profile, _liveSaveProfile))
                {
                    _retrySaveAfter = DateTime.UtcNow.AddSeconds(10);
                    ActiveProfileText.Text = $"Profile: {profile.ProfileName} / Sync retry";
                    ActiveProfileText.ToolTip = ex.Message;
                }
            }
            finally
            {
                _liveSaveSyncInProgress = false;
                if (!ReferenceEquals(profile, _liveSaveProfile))
                    _ = CheckLiveSaveAsync();
            }
        }

        private void RefreshSaveViews()
        {
            if (_embeddedViews.TryGetValue("ObjectivesWindow", out EmbeddedViewState view) &&
                view.Window is ObjectivesWindow objectives)
                objectives.RefreshSaveProgress();
            if (_embeddedViews.TryGetValue("HousesWindow", out view) && view.Window is houses houseView)
                houseView.RefreshSaveOwnership();
            if (_embeddedViews.TryGetValue("DealershipsWindow", out view) && view.Window is dealerships dealerView)
                dealerView.RefreshSaveOwnership();
        }

        private DataRowView GetSelectedVehicleRow()
        {
            return combo_Vehicle.SelectedItem as DataRowView;
        }

        private string GetSelectedVehicleName()
        {
            DataRowView row = GetSelectedVehicleRow();
            return row == null ? combo_Vehicle.Text : Convert.ToString(row["_vehicle_name"], CultureInfo.InvariantCulture);
        }

        private long? GetSelectedGarageVehicleId()
        {
            DataRowView row = GetSelectedVehicleRow();
            if (row == null || !row.Row.Table.Columns.Contains("garage_vehicle_id") || row["garage_vehicle_id"] == DBNull.Value)
                return null;
            return Convert.ToInt64(row["garage_vehicle_id"], CultureInfo.InvariantCulture);
        }

        private void ExecuteQuery(string query, string tableName, Action<DataSet> onSuccess, Action<Exception> onError, params (string, object)[] parameters)
        {
            try
            {
                using (var dbConn = new SQLiteConnection(connectionString))
                using (var dbCmd = new SQLiteCommand(query, dbConn))
                using (var dbAdapter = new SQLiteDataAdapter(dbCmd))
                {
                    foreach (var param in parameters)
                    {
                        dbCmd.Parameters.AddWithValue(param.Item1, param.Item2);
                    }
                    dbConn.Open();
                    var dataSet = new DataSet();
                    dbAdapter.Fill(dataSet, tableName);
                    onSuccess(dataSet);
                }
            }
            catch (Exception ex)
            {
                onError(ex);
            }
        }
        private void ClearVehicleDetails()
        {
            txt_odometer.Text = "";
            _loadedOdometerText = "";
            txt_carPB.Text = "";
            txt_rRan.Text = "";
        }
        private void UpdateOdometerHeader()
        {
            Odometer.Header = MeasurementUnits.IsImperial ? "Odometer (Mi)" : "Odometer (KM)";
        }

        internal void RefreshMeasurementUnits()
        {
            bool imperial = MeasurementUnits.IsImperial;
            UpdateOdometerHeader();
            bool hasDraft = txt_odometer.Text != _loadedOdometerText;
            MeasurementUnits.ConvertDraft(txt_odometer, onemile, imperial);
            _loadedOdometerText = hasDraft
                ? MeasurementUnits.ConvertText(_loadedOdometerText, onemile, imperial)
                : txt_odometer.Text;
            if (IsSelectedSpeedTrap())
            {
                MeasurementUnits.ConvertDraft(PointsTextBlock, onemile, imperial);
                rdc.Text = speed;
                FetchAndDisplayVehicleData(refreshOdometer: false);
                if (combo_Track.SelectedItem != null)
                    LoadTrackRecord();
            }
            AverageRace_Speed.Text = MeasurementUnits.ConvertText(AverageRace_Speed.Text, onemile, imperial, "0.00");
            Speed_Text.SetCurrentValue(TextBlock.TextProperty, speed);
            if (combo_Track.SelectedIndex >= 0 && !string.IsNullOrEmpty(race_Length.Text))
            {
                double length = GetDisplayTrackLength();
                race_Length.Text = length.ToString(CultureInfo.CurrentCulture);
                RaceLength_Distance_Unit.SetCurrentValue(TextBlock.TextProperty, distance);
                Lap_Distance_Unit.SetCurrentValue(TextBlock.TextProperty, distance);
                if (int.TryParse(LapsTextBlock.Text, out int laps) && laps > 0)
                    lap_Length.Text = (length / laps).ToString("N2", CultureInfo.CurrentCulture);

                MinimumSpeedTextBlock.Text = GetDisplayMinimumSpeed();
            }
            calc_Total_Odometer(focusField: false);
        }

        private void calc_Total_Odometer(bool focusField = true)
        {
            string odo = SI == "Metric" ? "_odometer_metric" : "_odometer_imperial";
            string garageOdo = SI == "Metric" ? "odometer_metric" : "odometer_imperial";
            string query = $@"
                SELECT CASE
                    WHEN EXISTS (
                        SELECT 1 FROM garage_vehicles g
                        JOIN save_profiles p ON p.profile_key=g.profile_key AND p.is_active=1
                        WHERE g.is_present=1
                    )
                    THEN (
                        SELECT SUM(g.{garageOdo}) FROM garage_vehicles g
                        JOIN save_profiles p ON p.profile_key=g.profile_key AND p.is_active=1
                        WHERE g.is_present=1
                    )
                    ELSE (SELECT SUM({odo}) FROM vehicles)
                END;";

            try
            {
                using (var dbConn = new SQLiteConnection(connectionString))
                {
                    dbConn.Open();
                    using (var dbCmd = new SQLiteCommand(query, dbConn))
                    {
                        using (var reader = dbCmd.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                double value = reader.IsDBNull(0) ? 0 : Convert.ToDouble(reader[0]);
                                Total_Odometer.Text = value.ToString("F2", CultureInfo.CurrentCulture);
                                total_distance_unit.Visibility = Visibility.Visible;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AppNotice.Show(this, "Error: " + ex);
            }
            finally
            {
                if (focusField)
                {
                    if (keep.IsChecked == true)
                        txt_odometer.Focus();
                    else
                        combo_Track.Focus();
                }
            }
        }
        private void SetComboBoxSource(ComboBox comboBox, DataSet dataSet, string displayMemberPath, string selectedValuePath)
        {
            if (dataSet.Tables.Count > 0)
            {
                comboBox.ItemsSource = dataSet.Tables[0].DefaultView;
                comboBox.DisplayMemberPath = displayMemberPath;
                comboBox.SelectedValuePath = selectedValuePath;
            }
            else
            {
                AppNotice.Show(this, $"No data found in the {displayMemberPath} table.");
            }
        }
        private void HideBoxes()
        {
            WeatherGroupBox.Visibility = Visibility.Collapsed;
            InfoGroupBox.Visibility = Visibility.Collapsed;
            TrackProgressGroupBox.Visibility = Visibility.Collapsed;
            RaceTimeGroupBox.Visibility = Visibility.Collapsed;
            avgLapGroupBox.Visibility = Visibility.Collapsed;
            ClassRestictionGroupBox.Visibility = Visibility.Collapsed;
            VehicleRestictionGroupBox.Visibility = Visibility.Collapsed;
            OrientationGroupBox.Visibility = Visibility.Collapsed;
            LapLengthGroupBox.Visibility = Visibility.Collapsed;
            LapsGroupBox.Visibility = Visibility.Collapsed;
            RaceLengthGroupBox.Visibility = Visibility.Collapsed;
            TrackRecordGroupbox.Visibility = Visibility.Collapsed;
            PointsGroupBox.Visibility = Visibility.Collapsed;
            MinimumSpeedGroupBox.Visibility = Visibility.Collapsed;
            CheckPointsGroupBox.Visibility = Visibility.Collapsed;
        }

        // GotFocus event handler
        private void TextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            FextBox_GotFocus((TextBox)sender);
        }

        private void FextBox_GotFocus(TextBox textBox)
        {
            // Clear the "00" value when the text box gains focus
            if (textBox.Text == "00")
            {
                textBox.Text = "";
            }
        }

        // LostFocus event handler
        private void TextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            FextBox_LostFocus((TextBox)sender);
        }

        private void FextBox_LostFocus(TextBox textBox)
        {

            if (textBox.Name.EndsWith("_Sec"))
            {
                // Ensure seconds are not greater than 59
                if (int.TryParse(textBox.Text, out int seconds) && seconds > 59)
                {
                    textBox.Text = "59";
                }
            }

            // Ensure the value has at least two digits (for Min/Sec), or three digits (for Ms)
            if (textBox.Name.EndsWith("_Min") || textBox.Name.EndsWith("_Sec"))
            {
                if (textBox.Text.Length == 1)
                {
                    textBox.Text = "0" + textBox.Text;
                }
            }
            else if (textBox.Name.EndsWith("_Ms"))
            {
                if (textBox.Text.Length == 2)
                {
                    textBox.Text = "0" + textBox.Text;
                }
            }
        }


        private void MyTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateRaceTimePreview();

            // Auto-tab when maxlength is reached
            if (((TextBox)sender).MaxLength == ((TextBox)sender).Text.Length)
            {
                // move focus
                var ue = e.OriginalSource as FrameworkElement;
                e.Handled = true;
                ue.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            }
        }

        private void UpdateRaceTimePreview()
        {
            if (RaceTimePreview == null || Race_Min == null || Race_Sec == null || Race_Ms == null)
                return;

            RaceTimePreview.Text = $"{FormatTimePart(Race_Min.Text)}:{FormatTimePart(Race_Sec.Text)}.{FormatTimePart(Race_Ms.Text)}";
        }

        private static string FormatTimePart(string text)
        {
            if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int value) || value < 0)
                return "00";

            return value.ToString("D2", CultureInfo.InvariantCulture);
        }
        private void RefreshRaceAverageSpeed()
        {
            if (combo_Track.SelectedItem is DataRowView track && TryReadRaceTime(out decimal totalSeconds))
                AverageRace_Speed.Text = CalculateRaceAverageSpeed(track, totalSeconds).ToString();
        }

        private static double CalculateRaceAverageSpeed(DataRowView track, decimal totalSeconds)
        {
            double distanceKm = Convert.ToDouble(track["Length"] == DBNull.Value ? 0 : track["Length"]);
            return Math.Round(MeasurementUnits.FromMetric(distanceKm, onemile) * 3600 / (double)totalSeconds, 2);
        }

        // Handler to ensure only numbers are accepted
        private void PreviewNoDecInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !IsTextAllowed(e.Text);
        }

        private bool IsTextAllowed(string text)
        {
            // Check if the input is numeric
            return Regex.IsMatch(text, "^[0-9]+$");
        }

        private void ApplyRestrictions(DataRowView track, bool refreshVehicles)
        {
            string classRestriction = track["RestrictedClass"].ToString().Trim();
            string vehicleId = track["RestrictedCar"].ToString().Trim();
            bool restrictClass = classRestriction.Length != 0 &&
                                 !string.Equals(classRestriction, "ALL", StringComparison.OrdinalIgnoreCase);
            bool restrictVehicle = vehicleId.Length != 0;
            bool hasClassIcon = restrictClass && classRestriction.Length > 1;
            ClassRestictionGroupBox.Visibility = restrictClass ? Visibility.Visible : Visibility.Collapsed;
            ClassRestrictionTextBlock.Text = classRestriction;
            ClassRestrictionTextBlock.Visibility = hasClassIcon ? Visibility.Collapsed : Visibility.Visible;
            ClassRestrictionImage.Source = hasClassIcon
                ? new BitmapImage(new Uri($"Images/carClasses/{classRestriction}.png", UriKind.Relative))
                : null;
            VehicleRestictionGroupBox.Visibility = restrictVehicle ? Visibility.Visible : Visibility.Collapsed;
            VehicleRestrictionTextBlock.Text = track["RestrictedVehicleName"] == DBNull.Value
                ? $"Vehicle {vehicleId}"
                : track["RestrictedVehicleName"].ToString();

            if (!refreshVehicles)
                return;

            string selectedClass = (combo_Class.SelectedItem as ComboBoxItem)?.Value ?? "";
            var previousVehicle = GetSelectedVehicleRow();
            BindVehicleComboBox(combo_Vehicle, restrictClass && classRestriction.Length > selectedClass.Length
                ? classRestriction
                : selectedClass, vehicleId);
            RestoreVehicleSelection(previousVehicle);
            if (combo_Vehicle.SelectedItem == null && restrictVehicle && combo_Vehicle.Items.Count == 1)
                combo_Vehicle.SelectedIndex = 0;
            combo_Vehicle.IsEnabled = combo_Vehicle.Items.Count > 0;
        }

        private void RestoreVehicleSelection(DataRowView previousVehicle)
        {
            combo_Vehicle.SelectedItem = combo_Vehicle.Items.OfType<DataRowView>()
                .FirstOrDefault(vehicle => SameVehicle(previousVehicle, vehicle));
        }

        private static bool SameVehicle(DataRowView first, DataRowView second) =>
            first != null && second != null && Equals(first["id"], second["id"]) &&
            Equals(first["garage_vehicle_id"], second["garage_vehicle_id"]);

        private void DisplayTrackDetails(double length, int laps, double minSpeed, int checkpoints, int oriental, string raceType)
        {
            _trackLengthKm = length;
            _trackMinimumSpeedKph = minSpeed;
            length = GetDisplayTrackLength();
            race_Length.Text = length.ToString();
            MinimumSpeedTextBlock.Text = GetDisplayMinimumSpeed();
            CheckpointsTextBlock.Text = checkpoints.ToString();
            bool isCrc = EventCatalog.IsCrcChallenge(raceType);
            bool isSpeedTrap = EventCatalog.IsSpeedTrap(raceType);
            CheckPointsGroupBox.Header = isSpeedTrap ? "Cameras" : "Checkpoints";
            CheckPointsGroupBox.Visibility = isSpeedTrap || (isCrc && checkpoints > 0)
                ? Visibility.Visible : Visibility.Collapsed;

            bool hasOrientation = EventCatalog.SupportsOrientation(raceType, laps);
            OrientationGroupBox.Visibility = hasOrientation ? Visibility.Visible : Visibility.Collapsed;
            cb_orientation.IsChecked = hasOrientation && oriental == 1;

            if (isSpeedTrap || EventCatalog.IsScoreBasedRaceType(raceType))
            {
                bool hasDistance = isCrc && _trackLengthKm > 0;
                bool hasLaps = isCrc && laps > 0;
                bool hasLapLength = hasDistance && laps > 1;
                RaceLengthGroupBox.Header = "Total Distance";
                RaceLengthGroupBox.Visibility = hasDistance ? Visibility.Visible : Visibility.Collapsed;
                RaceLength_Distance_Unit.Visibility = hasDistance ? Visibility.Visible : Visibility.Collapsed;
                RaceLength_Distance_Unit.Text = distance;
                LapsTextBlock.Text = hasLaps ? laps.ToString() : "";
                LapsGroupBox.Visibility = hasLaps ? Visibility.Visible : Visibility.Collapsed;
                LapLengthGroupBox.Visibility = hasLapLength ? Visibility.Visible : Visibility.Collapsed;
                Lap_Distance_Unit.Visibility = LapLengthGroupBox.Visibility;
                Lap_Distance_Unit.Text = distance;
                lap_Length.Text = hasLapLength ? (length / laps).ToString("N2") : "";
                avgLapGroupBox.Visibility = Visibility.Collapsed;
                TrackRecordGroupbox.Visibility = isSpeedTrap ? Visibility.Visible : Visibility.Collapsed;
            }
            else if (laps > 0)
            {
                RaceLengthGroupBox.Header = "Race Length";
                DisplayLapsAndRaceLength(length, laps);
            }
            else
            {
                HideLapsAndRaceLength();
            }
        }

        private double GetDisplayTrackLength() => Math.Round(MeasurementUnits.FromMetric(_trackLengthKm, onemile), 2);

        private string GetDisplayMinimumSpeed() =>
            Math.Round(MeasurementUnits.FromMetric(_trackMinimumSpeedKph, onemile), 2).ToString("0.##") + speed;

        private void DisplayLapsAndRaceLength(double length, int laps)
        {
            LapsTextBlock.Text = laps.ToString();
            lap_Length.Text = (length / laps).ToString("N2");

            RaceLength_Distance_Unit.Visibility = string.IsNullOrEmpty(race_Length.Text) ? Visibility.Collapsed : Visibility.Visible;
            RaceLength_Distance_Unit.Text = distance;

            if (laps == 1)
            {
                lap_Length.Text = "N/A";
                LapsTextBlock.Text = "N/A";
                race_Length.Text = length.ToString();
                Lap_Distance_Unit.Visibility = Visibility.Collapsed;
                avgLapGroupBox.Visibility = Visibility.Collapsed;
            }
            else
            {
                Lap_Distance_Unit.Visibility = Visibility.Visible;
                Lap_Distance_Unit.Text = distance;
                avgLapGroupBox.Visibility = Visibility.Visible;
            }

            LapsGroupBox.Visibility = Visibility.Visible;
            RaceLengthGroupBox.Visibility = Visibility.Visible;
            TrackRecordGroupbox.Visibility = Visibility.Visible;
        }
        private void HideLapsAndRaceLength()
        {
            LapsGroupBox.Visibility = Visibility.Collapsed;
            RaceLengthGroupBox.Visibility = Visibility.Collapsed;
            TrackRecordGroupbox.Visibility = Visibility.Collapsed;
        }
        private bool TryReadRaceTime(out decimal totalSeconds)
        {
            totalSeconds = 0;
            if (!TryParseTimePart(Race_Min.Text, out int minutes) ||
                !TryParseTimePart(Race_Sec.Text, out int seconds) || seconds > 59 ||
                !TryParseTimePart(Race_Ms.Text, out int hundredths) || hundredths > 99)
                return false;

            totalSeconds = minutes * 60m + seconds + hundredths / 100m;
            return totalSeconds > 0;
        }

        private void calc_Average_Lap_Time()
        {
            int laps = GetSelectedTrackLaps();
            average_LapTime.Text = laps > 1 && TryReadRaceTime(out decimal seconds)
                ? EventCatalog.FormatRaceTime(seconds / laps) : "";
        }


        // Call this method to reset all controls
        private void ResetControls(DependencyObject parent)
        {
            // Iterate through all child elements
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);

                // Check the type of control and reset accordingly
                if (child is TextBox textBox)
                {

                    textBox.Text = string.Empty;
                }
                else if (child is ComboBox comboBox)
                {
                    comboBox.SelectedIndex = -1;
                }
                else if (child is CheckBox checkBox)
                {
                    checkBox.IsChecked = false;
                }
                else if (child is RadioButton radioButton)
                {
                    radioButton.IsChecked = false;
                }
                else if (child is ListBox listBox)
                {
                    listBox.SelectedIndex = -1;
                }
                else if (child is DatePicker datePicker)
                {
                    datePicker.SelectedDate = null;
                }
                else if (child is PasswordBox passwordBox)
                {
                    passwordBox.Clear();
                }
                //else if(child is TextBlock textBlock)
                //{
                //    textBlock.Text = string.Empty;
                //}
                // Add more control types as needed

                // Recurse into child elements
                ResetControls(child);
            }
        }
        private void Btn_reset_Click(object sender, RoutedEventArgs e)
        {


            //// Reset other controls
            combo_Track.SelectedIndex = -1;
            combo_Class.SelectedIndex = -1;
            cb_conditions.IsChecked = false;
            combo_Vehicle.SelectedIndex = -1;
            keep.IsChecked = false;

            ResetControls(this);
            ResetRaceFields();
            // Arrays of TextBoxes for laps and average lap speeds
            isCalculated = 0;
            combo_Type.SelectedIndex = -1;
            lap_Length.Text = "";
            LapLengthGroupBox.Visibility = Visibility.Collapsed;
            race_Length.Text = "";
            RaceLength_Distance_Unit.Text = "";
            RaceLengthGroupBox.Visibility = Visibility.Collapsed;
            txt_odometer.Text = "";
            txt_lrec.Text = "";
            txt_lrecCar.Text = "";
            txt_rRan.Text = "";
            txt_Progress_1.Text = "";
            txt_Progress_2.Text = "";
            txt_carPB.Text = "";
            average_LapTime.Text = "";
            AverageRace_Speed.Text = "";
        }
        private void Btn_submit_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateSelections())
            {
                AppNotice.Show(this, "Select an event, class, and vehicle before saving.", "Missing selection");
                return;
            }

            bool databaseSaved = false;
            try
            {
                var track = (DataRowView)combo_Track.SelectedItem;
                int trackId = GetSelectedTrackId();
                bool newRankedTrack = trackId < 0;
                string raceType = ((ComboBoxItem)combo_Type.SelectedItem).Value;
                string carClass = GetSelectedVehicleRow()["_vehiclecategory_name"].ToString();
                string vehicleName = GetSelectedVehicleName();
                long? garageVehicleId = GetSelectedGarageVehicleId();
                bool isSpeedTrap = EventCatalog.IsSpeedTrap(raceType);
                bool isScoreBased = EventCatalog.IsScoreBasedRaceType(raceType);
                bool usesTimeResult = !isSpeedTrap && !isScoreBased;

                if (!TryParseNumber(txt_odometer.Text, out double enteredOdometer) || enteredOdometer < 0)
                {
                    AppNotice.Show(this, "Enter a valid non-negative vehicle odometer before saving.", "Invalid odometer");
                    txt_odometer.Focus();
                    return;
                }

                double? score = null;
                double averageSpeed = 0;
                string totalTime = "N/A";
                string averageLap = "N/A";

                if (isSpeedTrap)
                {
                    if (!MeasurementUnits.TryParse(PointsTextBlock.Text, out averageSpeed) || averageSpeed < 0)
                    {
                        AppNotice.Show(this, "Enter a valid average speed before saving this event.", "Invalid average speed");
                        return;
                    }
                }
                else if (isScoreBased)
                {
                    if (!TryParseNumber(PointsTextBlock.Text, out double enteredScore))
                    {
                        AppNotice.Show(this, "Enter a valid points score before saving this event.", "Invalid score");
                        return;
                    }

                    score = enteredScore;
                }
                else
                {
                    if (!TryReadRaceTime(out decimal totalSeconds))
                    {
                        AppNotice.Show(this, "Enter a valid race time before saving.", "Invalid race time");
                        return;
                    }

                    averageSpeed = CalculateRaceAverageSpeed(track, totalSeconds);
                    totalTime = EventCatalog.FormatRaceTime(totalSeconds);
                    int laps = GetSelectedTrackLaps();
                    if (laps > 1)
                        averageLap = EventCatalog.FormatRaceTime(totalSeconds / laps);
                }

                double metricOdometer = Math.Round(MeasurementUnits.DraftToMetric(txt_odometer, enteredOdometer, onemile), 2);
                double imperialOdometer = Math.Round(SI == "Metric" ? enteredOdometer * onemile : enteredOdometer, 2);
                double metricAverageSpeed = SI == "Metric" ? averageSpeed : Math.Round(averageSpeed / onemile, 2);
                double imperialAverageSpeed = SI == "Metric" ? Math.Round(averageSpeed * onemile, 2) : averageSpeed;
                if (isSpeedTrap)
                {
                    double metric = MeasurementUnits.DraftToMetric(PointsTextBlock, averageSpeed, onemile);
                    metricAverageSpeed = Math.Round(metric, 2);
                    imperialAverageSpeed = Math.Round(metric * onemile, 2);
                }
                string odometerColumn = SI == "Metric" ? "_odometer_metric" : "_odometer_imperial";

                using (var dbConn = new SQLiteConnection(connectionString))
                {
                    dbConn.Open();
                    using (SQLiteTransaction transaction = dbConn.BeginTransaction())
                    {
                        if (newRankedTrack)
                            trackId = EventCatalog.GetOrCreateRankedTrack(dbConn, transaction, -trackId);

                        const string insertQuery = @"
                            INSERT INTO records
                                (trackId, carName, carClass, conditions, orientation, raceType, score,
                                 Total_Time, Average_Lap, Average_Speed_Metric, Average_Speed_Imperial,
                                 garageVehicleId, timestamp)
                            VALUES
                                (@TrackId, @CarName, @CarClass, @Conditions, @Orientation, @RaceType, @Score,
                                 @TotalTime, @AverageLap, @MetricAverageSpeed, @ImperialAverageSpeed,
                                 @GarageVehicleId, CURRENT_TIMESTAMP);";
                        using (var insertCommand = new SQLiteCommand(insertQuery, dbConn, transaction))
                        {
                            insertCommand.Parameters.AddWithValue("@TrackId", trackId);
                            insertCommand.Parameters.AddWithValue("@CarName", vehicleName);
                            insertCommand.Parameters.AddWithValue("@CarClass", carClass);
                            insertCommand.Parameters.AddWithValue("@Conditions", cb_conditions.IsChecked == true ? 1 : 0);
                            insertCommand.Parameters.AddWithValue("@Orientation", GetRecordOrientation() ?? 0);
                            insertCommand.Parameters.AddWithValue("@RaceType", raceType);
                            insertCommand.Parameters.AddWithValue("@Score", score.HasValue ? (object)score.Value : DBNull.Value);
                            insertCommand.Parameters.AddWithValue("@TotalTime", totalTime);
                            insertCommand.Parameters.AddWithValue("@AverageLap", averageLap);
                            insertCommand.Parameters.AddWithValue("@MetricAverageSpeed", metricAverageSpeed);
                            insertCommand.Parameters.AddWithValue("@ImperialAverageSpeed", imperialAverageSpeed);
                            insertCommand.Parameters.AddWithValue("@GarageVehicleId",
                                garageVehicleId.HasValue ? (object)garageVehicleId.Value : DBNull.Value);
                            insertCommand.ExecuteNonQuery();
                        }

                        using (var trackCommand = new SQLiteCommand(
                            "UPDATE tracks SET Runs = COALESCE(Runs, 0) + 1 WHERE id = @TrackId", dbConn, transaction))
                        {
                            trackCommand.Parameters.AddWithValue("@TrackId", trackId);
                            if (trackCommand.ExecuteNonQuery() != 1)
                                throw new InvalidOperationException("The selected event no longer exists in the database.");
                        }

                        if (usesTimeResult)
                            EventCatalog.RefreshTimedBests(dbConn, transaction, trackId, carClass);

                        if (garageVehicleId.HasValue)
                        {
                            const string garageQuery = @"
                                UPDATE garage_vehicles
                                SET odometer_metric=@MetricOdometer,
                                    odometer_imperial=@ImperialOdometer
                                WHERE id=@GarageVehicleId AND is_present=1;";
                            using (var garageCommand = new SQLiteCommand(garageQuery, dbConn, transaction))
                            {
                                garageCommand.Parameters.AddWithValue("@MetricOdometer", metricOdometer);
                                garageCommand.Parameters.AddWithValue("@ImperialOdometer", imperialOdometer);
                                garageCommand.Parameters.AddWithValue("@GarageVehicleId", garageVehicleId.Value);
                                if (garageCommand.ExecuteNonQuery() != 1)
                                    throw new InvalidOperationException("The selected garage car no longer exists in the active import.");
                            }

                            const string aggregateQuery = @"
                                UPDATE vehicles
                                SET _odometer_metric=(
                                        SELECT SUM(g.odometer_metric) FROM garage_vehicles g
                                        JOIN save_profiles p ON p.profile_key=g.profile_key AND p.is_active=1
                                        WHERE g.vehicle_id=vehicles.id AND g.is_present=1),
                                    _odometer_imperial=(
                                        SELECT SUM(g.odometer_imperial) FROM garage_vehicles g
                                        JOIN save_profiles p ON p.profile_key=g.profile_key AND p.is_active=1
                                        WHERE g.vehicle_id=vehicles.id AND g.is_present=1),
                                    _races_ran=COALESCE(_races_ran, 0) + 1
                                WHERE id=(SELECT vehicle_id FROM garage_vehicles WHERE id=@GarageVehicleId);";
                            using (var aggregateCommand = new SQLiteCommand(aggregateQuery, dbConn, transaction))
                            {
                                aggregateCommand.Parameters.AddWithValue("@GarageVehicleId", garageVehicleId.Value);
                                if (aggregateCommand.ExecuteNonQuery() != 1)
                                    throw new InvalidOperationException("The selected garage car has no matching catalogue vehicle.");
                            }
                        }
                        else
                        {
                            const string vehicleQuery = @"
                                UPDATE vehicles
                                SET _odometer_metric = @MetricOdometer,
                                    _odometer_imperial = @ImperialOdometer,
                                    _races_ran = COALESCE(_races_ran, 0) + 1
                                WHERE _vehicle_name = @VehicleName;";
                            using (var vehicleCommand = new SQLiteCommand(vehicleQuery, dbConn, transaction))
                            {
                                vehicleCommand.Parameters.AddWithValue("@MetricOdometer", metricOdometer);
                                vehicleCommand.Parameters.AddWithValue("@ImperialOdometer", imperialOdometer);
                                vehicleCommand.Parameters.AddWithValue("@VehicleName", vehicleName);
                                if (vehicleCommand.ExecuteNonQuery() != 1)
                                    throw new InvalidOperationException("The selected vehicle no longer exists in the database.");
                            }
                        }

                        transaction.Commit();
                        databaseSaved = true;
                    }
                }

                if (newRankedTrack)
                    track["id"] = trackId;

                FinishSavedRun(usesTimeResult);
            }
            catch (Exception ex)
            {
                string message = databaseSaved
                    ? $"The race was saved, but the form could not be refreshed.\n\n{ex.Message}"
                    : $"The race could not be saved. No database changes were made.\n\n{ex.Message}";
                AppNotice.Show(this, message, databaseSaved ? "Refresh failed" : "Save failed", persistent: true);
            }
        }

        private static bool TryParseNumber(string text, out double value)
        {
            NumberStyles styles = NumberStyles.Float | NumberStyles.AllowThousands;
            return double.TryParse(text, styles, CultureInfo.CurrentCulture, out value) ||
                   double.TryParse(text, styles, CultureInfo.InvariantCulture, out value);
        }

        private static bool TryParseTimePart(string text, out int value)
        {
            return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value) && value >= 0;
        }

        private bool ValidateSelections()
        {
            return combo_Track.SelectedItem is DataRowView &&
                   combo_Type.SelectedItem is ComboBoxItem &&
                   combo_Class.SelectedItem is ComboBoxItem &&
                   GetSelectedVehicleRow() != null;
        }

        private void ResetRaceFields()
        {
            Race_Min.Text = "";
            Race_Sec.Text = "";
            Race_Ms.Text = "";
            AverageRace_Speed.Text = "0.0";
            average_LapTime.Text = "";
            PointsTextBlock.Text = "";
        }
        private void ResetAllFields()
        {
            ResetRaceFields();
            Speed_Text.Text = speed;
            combo_Type.SelectedIndex = -1;
            combo_Track.SelectedIndex = -1;
            combo_Class.SelectedIndex = -1;
            LapLengthGroupBox.Visibility = Visibility.Collapsed;
            RaceLengthGroupBox.Visibility = Visibility.Collapsed;
            race_Length.Text = string.Empty;
            RaceLength_Distance_Unit.Text = string.Empty;
            lap_Length.Text = string.Empty;
            cb_conditions.IsChecked = false;
            combo_Vehicle.SelectedIndex = -1;
            txt_odometer.Text = string.Empty;
            txt_lrec.Text = string.Empty;
            txt_lrecCar.Text = string.Empty;
            txt_rRan.Text = string.Empty;
            txt_Progress_1.Text = string.Empty;
            txt_Progress_2.Text = string.Empty;
            txt_carPB.Text = string.Empty;
            keep.IsChecked = false;
        }

        private void FinishSavedRun(bool usesTimeResult)
        {
            bool retrySameCar = retry.IsChecked == true;
            bool advanceCar = !retrySameCar && keep.IsChecked == true;

            if (retrySameCar || advanceCar)
            {
                ResetRaceFields();
                if (advanceCar && combo_Vehicle.SelectedIndex + 1 < combo_Vehicle.Items.Count)
                    combo_Vehicle.SelectedIndex++;

                LoadTrackRecord();
                CheckProgress();
                FetchAndDisplayVehicleData();
            }
            else
            {
                ResetAllFields();
            }

            calc_Total_Odometer(focusField: !retrySameCar);
            if (retrySameCar)
                (usesTimeResult ? Race_Min : PointsTextBlock).Focus();
            isCalculated = 0;
        }

        private int GetSelectedTrackId()
        {
            return Convert.ToInt32(((DataRowView)combo_Track.SelectedItem)["id"], CultureInfo.InvariantCulture);
        }

        private void LoadTrackDetailsAndRecord(bool refreshVehicles)
        {
            if (!(combo_Track.SelectedItem is DataRowView track))
                return;

            ApplyRestrictions(track, refreshVehicles);
            DisplayTrackDetails(Convert.ToDouble(track["Length"] == DBNull.Value ? 0 : track["Length"]),
                Convert.ToInt32(track["Laps"] == DBNull.Value ? 0 : track["Laps"]),
                Convert.ToDouble(track["MinSpeed"] == DBNull.Value ? 0 : track["MinSpeed"]),
                Convert.ToInt32(track["Checkpoints"] == DBNull.Value ? 0 : track["Checkpoints"]),
                Convert.ToInt32(track["Orientation"] == DBNull.Value ? 0 : track["Orientation"]),
                ((ComboBoxItem)combo_Type.SelectedItem).Value);
            LoadTrackRecord();
        }

        private int GetSelectedTrackLaps()
        {
            var track = combo_Track.SelectedItem as DataRowView;
            return track != null && track["Laps"] != DBNull.Value ? Convert.ToInt32(track["Laps"]) : 0;
        }

        private int? GetRecordOrientation() =>
            EventCatalog.SupportsOrientation((combo_Type.SelectedItem as ComboBoxItem)?.Value, GetSelectedTrackLaps())
                ? cb_orientation.IsChecked == true ? 1 : 0
                : (int?)null;

        private void LoadTrackRecord()
        {
            string selectedClass = (combo_Class.SelectedItem as ComboBoxItem)?.Value ?? "";
            try
            {
                using (var connection = new SQLiteConnection(connectionString))
                {
                    connection.Open();
                    int trackId = GetSelectedTrackId();
                    bool speedTrap = IsSelectedSpeedTrap();
                    var best = speedTrap
                        ? EventCatalog.ReadSpeedTrapBest(connection, trackId, selectedClass)
                        : null;
                    var timed = speedTrap ? null : EventCatalog.ReadTimedBest(connection, trackId, selectedClass);
                    txt_lrec.Text = best?.Speed ?? timed?.Time ?? "No Record";
                    txt_lrecCar.Text = best?.Car ?? timed?.Car ?? "No Record";
                    int? weather = best?.Weather ?? timed?.Weather;
                    weatherS_lrec.Visibility = weather == 0 ? Visibility.Visible : Visibility.Collapsed;
                    weatherR_lrec.Visibility = weather == 1 ? Visibility.Visible : Visibility.Collapsed;
                    bool hasOrientation = GetRecordOrientation().HasValue;
                    orientationC_lrec.Visibility = hasOrientation && timed?.Orientation == 0 ? Visibility.Visible : Visibility.Collapsed;
                    orientationA_lrec.Visibility = hasOrientation && timed?.Orientation == 1 ? Visibility.Visible : Visibility.Collapsed;
                }
            }
            catch (SQLiteException ex)
            {
                AppNotice.Show(this, $"The event record could not be loaded.\n{ex.Message}", "Database error");
            }
        }

        private void Btn_submit_GotFocus(object sender, RoutedEventArgs e)
        {
            calc_Average_Lap_Time();
        }

        private void Combo_Vehicle_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.Source == combo_Vehicle && txt_odometer != null)
                FetchAndDisplayVehicleData();
        }


        private void Combo_Track_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            HideAll();
            if (combo_Track.SelectedItem == null)
                return;

            UpdateRaceTypeUI();
            FinalizeUI(refreshVehicles: true);
            InfoGroupBox.Visibility = Visibility.Visible;
            if (GetSelectedVehicleRow() != null)
                FetchAndDisplayVehicleData();
            else
                ClearVehicleDetails();
            isCalculated = 0;
        }

        // Hides all UI elements and resets their associated text fields.
        private void HideAll()
        {
            if (isCalculated == 0)
            {
                // Hide and reset all UI elements
                OrientationGroupBox.Visibility = Visibility.Collapsed;
                WeatherGroupBox.Visibility = Visibility.Collapsed;
                InfoGroupBox.Visibility = Visibility.Collapsed;
                TrackProgressGroupBox.Visibility = Visibility.Collapsed;
                txt_Progress_1.Text = string.Empty;
                txt_Progress_2.Text = string.Empty;
                ClassRestictionGroupBox.Visibility = Visibility.Collapsed;
                VehicleRestictionGroupBox.Visibility = Visibility.Collapsed;
                VehicleRestrictionTextBlock.Text = string.Empty;
                LapLengthGroupBox.Visibility = Visibility.Collapsed;
                lap_Length.Text = string.Empty;
                LapsGroupBox.Visibility = Visibility.Collapsed;
                LapsTextBlock.Text = string.Empty;
                RaceLengthGroupBox.Visibility = Visibility.Collapsed;
                race_Length.Text = string.Empty;
                RaceLength_Distance_Unit.Text = string.Empty;
                TrackRecordGroupbox.Visibility = Visibility.Collapsed;
                txt_lrecCar.Text = string.Empty;
                weatherS_lrec.Visibility = Visibility.Collapsed;
                weatherR_lrec.Visibility = Visibility.Collapsed;
                orientationC_lrec.Visibility = Visibility.Collapsed;
                orientationA_lrec.Visibility = Visibility.Collapsed;
                PointsGroupBox.Visibility = Visibility.Collapsed;
                PointsTextBlock.Text = string.Empty;
                MinimumSpeedGroupBox.Visibility = Visibility.Collapsed;
                MinimumSpeedTextBlock.Text = string.Empty;
                CheckPointsGroupBox.Visibility = Visibility.Collapsed;
                CheckpointsTextBlock.Text = string.Empty;
            }
        }

        // Updates the UI based on the selected race type.
        // Update UI elements related to the selected race type.
        private void UpdateRaceTypeUI()
        {
            if (isCalculated == 0) {
                //HideAll();
                if (combo_Type.SelectedItem is ComboBoxItem selectedItem)
                {
                    string selectedRaceType = selectedItem.Value;
                    // Determine event types
                    bool isRace = selectedRaceType == "Race SP" || selectedRaceType == "Race MP" || selectedRaceType == "Race RMP";
                    bool isEliminator = selectedRaceType == "Eliminator SP";
                    bool isTimeAttack = EventCatalog.IsTimeAttack(selectedRaceType);
                    bool isSpeedEvent = selectedRaceType == "Speed SP" || selectedRaceType == "Speed MP" || selectedRaceType == "Speed PC";
                    bool isSpeedTrap = EventCatalog.IsSpeedTrap(selectedRaceType);
                    bool isFYLEvent = selectedRaceType == "Follow The Leader MP";
                    bool isKYDEvent = selectedRaceType == "Keep Your Distance MP";

                    // Define visibility conditions
                    bool hasRestrictedClass = isRace || isEliminator || isTimeAttack || isSpeedEvent || isSpeedTrap;
                    bool hasRestrictedVehicle = isRace || isEliminator || isTimeAttack || isSpeedEvent || isSpeedTrap;
                    bool hasDistance = isRace || isEliminator || isTimeAttack;
                    bool hasLaps = isRace || isEliminator || isTimeAttack; // Laps for race-related types
                    bool hasTrackLapRecord = isRace || isEliminator || isTimeAttack;
                    bool hasNumericResult = isSpeedEvent || isSpeedTrap || isFYLEvent || isKYDEvent;
                    bool hasWeather = isRace || isEliminator || isTimeAttack || isSpeedEvent || isSpeedTrap;

                    // Update the headers and visibility of the UI elements based on the selected race type
                    TrackRecordGroupbox.Header = "Track Record";
                    TrackRecordGroupbox.Visibility = hasTrackLapRecord || isSpeedTrap ? Visibility.Visible : Visibility.Collapsed;
                    LapLengthGroupBox.Visibility = hasDistance ? Visibility.Visible : Visibility.Collapsed;
                    LapsGroupBox.Visibility = hasLaps ? Visibility.Visible : Visibility.Collapsed;
                    RaceLengthGroupBox.Visibility = hasDistance ? Visibility.Visible : Visibility.Collapsed;
                    PointsGroupBox.Visibility = hasNumericResult ? Visibility.Visible : Visibility.Collapsed;
                    PointsGroupBox.Header = isSpeedTrap ? "Avg. Speed" : "Points";
                    rdc.Text = isSpeedTrap ? speed : Settings.Default.Points;
                    PointsRecordDivider.Visibility = isSpeedTrap ? Visibility.Collapsed : Visibility.Visible;
                    PointsRecordTextBlock.Visibility = PointsRecordDivider.Visibility;
                    MinimumSpeedGroupBox.Visibility = isSpeedEvent ? Visibility.Visible : Visibility.Collapsed;
                    CheckPointsGroupBox.Visibility = isSpeedTrap ? Visibility.Visible : Visibility.Collapsed;
                    VehicleRestrictionTextBlock.Visibility = hasRestrictedVehicle ? Visibility.Visible : Visibility.Collapsed;
                    ClassRestictionGroupBox.Visibility = hasRestrictedVehicle ? Visibility.Visible : Visibility.Collapsed;
                    WeatherGroupBox.Visibility = hasWeather ? Visibility.Visible : Visibility.Collapsed;
                    RaceTimeGroupBox.Visibility = hasTrackLapRecord ? Visibility.Visible : Visibility.Collapsed;
                    isCalculated++;
                }
            }
        }

        // Final adjustments to the UI after updating track information.
        private void FinalizeUI(bool refreshVehicles = false)
        {
            LoadTrackDetailsAndRecord(refreshVehicles);
            CheckProgress();     // Checks the current progress or state of the race/track
        }


        private bool IsSelectedSpeedTrap() =>
            EventCatalog.IsSpeedTrap((combo_Type.SelectedItem as ComboBoxItem)?.Value);

        // Fetches and displays vehicle data based on the selected track and vehicle.
        private void FetchAndDisplayVehicleData(bool refreshOdometer = true)
        {
            DataRowView vehicle = GetSelectedVehicleRow();
            if (vehicle == null)
            {
                ClearVehicleDetails();
                return;
            }

            int conditions = cb_conditions.IsChecked == true ? 1 : 0;
            int? orientation = GetRecordOrientation();
            string vehicleName = GetSelectedVehicleName();
            long? garageVehicleId = GetSelectedGarageVehicleId();
            int? trackId = combo_Track.SelectedItem == null ? (int?)null : GetSelectedTrackId();
            string odoColumn = SI == "Metric" ? "_odometer_metric" : "_odometer_imperial";
            string garageOdoColumn = SI == "Metric" ? "odometer_metric" : "odometer_imperial";
            bool speedTrap = IsSelectedSpeedTrap();
            string personalBest = speedTrap
                ? $"MAX(CASE WHEN score IS NULL THEN {EventCatalog.AverageSpeedColumn} END)"
                : "MIN(Total_Time)";

            string query = $@"
                WITH vehicle_runs AS (
                    SELECT Total_Time, Average_Speed_Metric, Average_Speed_Imperial, score
                    FROM records
                    WHERE trackId=@TrackId AND conditions=@Conditions AND (@Orientation IS NULL OR orientation=@Orientation)
                      AND ((@GarageVehicleId IS NOT NULL AND garageVehicleId=@GarageVehicleId)
                           OR (garageVehicleId IS NULL AND carName=@VehicleName))
                )
                SELECT CASE WHEN @GarageVehicleId IS NULL
                            THEN (SELECT {odoColumn} FROM vehicles WHERE id=@VehicleId)
                            ELSE (SELECT {garageOdoColumn} FROM garage_vehicles WHERE id=@GarageVehicleId)
                       END, {personalBest}, COUNT(*), MAX(score)
                FROM vehicle_runs;";

            try
            {
                // Execute the query and fetch data from the database
                using (var dbConn = new SQLiteConnection(connectionString))
                {
                    dbConn.Open();
                    using (var dbCmd = new SQLiteCommand(query, dbConn))
                    {
                        // Adding parameters to the query
                        dbCmd.Parameters.AddWithValue("@VehicleId", vehicle["id"]);
                        dbCmd.Parameters.AddWithValue("@VehicleName", vehicleName);
                        dbCmd.Parameters.AddWithValue("@GarageVehicleId",
                            garageVehicleId.HasValue ? (object)garageVehicleId.Value : DBNull.Value);
                        dbCmd.Parameters.AddWithValue("@TrackId", trackId.HasValue ? (object)trackId.Value : DBNull.Value);
                        dbCmd.Parameters.AddWithValue("@Conditions", conditions);
                        dbCmd.Parameters.AddWithValue("@Orientation", orientation.HasValue ? (object)orientation.Value : DBNull.Value);

                        using (var reader = dbCmd.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                if (refreshOdometer)
                                {
                                    double value = reader.IsDBNull(0) ? 0 : Convert.ToDouble(reader[0], CultureInfo.InvariantCulture);
                                    txt_odometer.Text = value.ToString("F2", CultureInfo.CurrentCulture);
                                    _loadedOdometerText = txt_odometer.Text;
                                }

                                if (!trackId.HasValue)
                                    txt_carPB.Text = "";
                                else if (!reader.IsDBNull(1))
                                    txt_carPB.Text = speedTrap
                                        ? EventCatalog.FormatSpeedTrapResult(Convert.ToDouble(reader[1]))
                                        : reader.GetString(1);
                                else if (speedTrap && !reader.IsDBNull(3))
                                    txt_carPB.Text = EventCatalog.FormatSpeedTrapResult(Convert.ToDouble(reader[3]), legacy: true);
                                else
                                    txt_carPB.Text = "No Record";

                                txt_rRan.Text = trackId.HasValue ? reader.GetInt32(2).ToString() : "";
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Display an error message if the database query fails
                AppNotice.Show(this, $"Error: {ex.Message}");
            }
        }

        private void CheckProgress(SQLiteConnection dbConn = null)
        {
            if (combo_Track.SelectedIndex == -1) { return; }
            txt_Progress_1.Text = "";
            txt_Progress_2.Text = "";

            if (TrackProgressGroupBox.Visibility != Visibility.Visible)
            {
                TrackProgressGroupBox.Visibility = Visibility.Visible;
            }


            int trackId = GetSelectedTrackId();
            int conditions = cb_conditions.IsChecked == true ? 1 : 0;
            int? orientation = GetRecordOrientation();

            ComboBoxItem selectedClass = (ComboBoxItem)combo_Class.SelectedItem;
            string param = selectedClass.Value.ToString();

            double percentage;

            bool localConnection = false;
            if (dbConn == null)
            {
                dbConn = new SQLiteConnection(connectionString);
                dbConn.Open();
                localConnection = true;
            }

            try
            {
                string effectiveClass;
                string restrictedVehicleName;
                GetProgressRestriction((DataRowView)combo_Track.SelectedItem, param, out effectiveClass, out restrictedVehicleName);

                int recordsCount = GetRecordCount(dbConn, trackId, conditions, orientation, effectiveClass, restrictedVehicleName);
                int carsCount = GetCarsCount(dbConn, effectiveClass, restrictedVehicleName);

                percentage = carsCount == 0 ? 0 : (recordsCount / (double)carsCount) * 100;

                txt_Progress_1.Text = $"{recordsCount}/{carsCount}";
                txt_Progress_2.Text = recordsCount > 0 ? $"{Math.Round(percentage, 2)}%" : "0%";
            }
            catch (Exception ex)
            {
                AppNotice.Show(this, "Error: " + ex.ToString());
            }
            finally
            {
                if (localConnection && dbConn != null)
                {
                    dbConn.Close();
                }
            }
        }

        private static void GetProgressRestriction(DataRowView track, string selectedClass,
            out string effectiveClass, out string restrictedVehicleName)
        {
            effectiveClass = selectedClass ?? string.Empty;
            restrictedVehicleName = track["RestrictedVehicleName"].ToString().Trim();
            string trackClass = track["RestrictedClass"].ToString().Trim();
            if (trackClass.Length == 0 || string.Equals(trackClass, "ALL", StringComparison.OrdinalIgnoreCase))
                return;

            bool selectedFitsFamily = trackClass.Length == 1 &&
                                      effectiveClass.StartsWith(trackClass, StringComparison.OrdinalIgnoreCase);
            if (!selectedFitsFamily)
                effectiveClass = trackClass;
        }

        private int GetRecordCount(SQLiteConnection dbConn, int trackId, int conditions, int? orientation,
            string effectiveClass, string restrictedVehicleName)
        {
            const string query = @"
                SELECT COUNT(DISTINCT r.carName)
                FROM records r
                INNER JOIN vehicles v ON v._vehicle_name = r.carName
                JOIN displayed_vehicle_ownership ownership ON ownership.id=v.id
                WHERE r.trackId = @TrackId
                  AND r.conditions = @Conditions
                  AND (@Orientation IS NULL OR r.orientation = @Orientation)
                  AND v._is_active = 'true'
                  AND ownership.is_owned = 'true'
                  AND (@VehicleName = '' OR v._vehicle_name = @VehicleName)
                  AND (
                        @Class = ''
                        OR UPPER(v._vehiclecategory_name) = UPPER(@Class)
                        OR (LENGTH(@Class) = 1 AND UPPER(v._vehiclecategory_name) LIKE UPPER(@Class) || '%')
                      );";

            using (var cmd = new SQLiteCommand(query, dbConn))
            {
                cmd.Parameters.AddWithValue("@TrackId", trackId);
                cmd.Parameters.AddWithValue("@Conditions", conditions);
                cmd.Parameters.AddWithValue("@Orientation", orientation.HasValue ? (object)orientation.Value : DBNull.Value);
                cmd.Parameters.AddWithValue("@Class", effectiveClass ?? string.Empty);
                cmd.Parameters.AddWithValue("@VehicleName", restrictedVehicleName ?? string.Empty);
                return Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
            }
        }

        private int GetCarsCount(SQLiteConnection dbConn, string effectiveClass, string restrictedVehicleName)
        {
            const string query = @"
                SELECT COUNT(*)
                FROM vehicles v JOIN displayed_vehicle_ownership ownership ON ownership.id=v.id
                WHERE _is_active = 'true'
                  AND ownership.is_owned = 'true'
                  AND (@VehicleName = '' OR _vehicle_name = @VehicleName)
                  AND (
                        @Class = ''
                        OR UPPER(_vehiclecategory_name) = UPPER(@Class)
                        OR (LENGTH(@Class) = 1 AND UPPER(_vehiclecategory_name) LIKE UPPER(@Class) || '%')
                      );";

            using (var cmd = new SQLiteCommand(query, dbConn))
            {
                cmd.Parameters.AddWithValue("@Class", effectiveClass ?? string.Empty);
                cmd.Parameters.AddWithValue("@VehicleName", restrictedVehicleName ?? string.Empty);
                return Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
            }
        }


        // Select all text when the TextBox receives keyboard focus
        private void TextBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            (sender as TextBox).SelectAll();
        }

        // Select all text when the TextBox receives mouse capture
        private void TextBox_GotMouseCapture(object sender, MouseEventArgs e)
        {
            (sender as TextBox).SelectAll();
        }

        // Replace commas with dots when the TextBox loses focus (e.g., for numerical input)
        private void Txt_odometer_LostFocus(object sender, RoutedEventArgs e)
        {
            txt_odometer.Text = txt_odometer.Text.Replace(",", ".");
        }

        public int RemoveIndex { get; private set; }

        // Define regex patterns for allowed/disallowed text
        private static readonly Regex _regexNoDec = new Regex("[^0-9]+"); // Allows only numeric characters (no decimals)

        // Check if the input text is allowed without decimal points
        private static bool IsDecAllowed(string text)
        {
            return !_regexNoDec.IsMatch(text);
        }

        private void Border_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                this.DragMove();
            }
        }
        private void Btn_TrackInfo_Click(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            if (combo_Track.SelectedIndex == -1)
            {
                AppNotice.Show(this, "No Track Selected", "Error");
                return;
            }


            int trackId = GetSelectedTrackId();

            using (var dbConn = new SQLiteConnection(connectionString))
            {
                dbConn.Open();
                try
                {
                    // Query to fetch the track name and total runs for the selected track
                    string trackQuery = "SELECT Name, Runs FROM tracks WHERE Id = @TrackId";
                    string trackName = ((DataRowView)combo_Track.SelectedItem)["Name"].ToString();
                    int totalRuns = 0;

                    using (var dbCmd = new SQLiteCommand(trackQuery, dbConn))
                    {
                        dbCmd.Parameters.AddWithValue("@TrackId", trackId);
                        using (var reader = dbCmd.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                trackName = reader.GetString(0); // Track Name
                                totalRuns = reader.GetInt32(1);  // Total Runs
                            }
                        }
                    }

                    // Query to fetch the fastest times by car class
                    string recordsQuery = @"
                SELECT 
                    r.carClass, 
                    r.carName, 
                    MIN(r.Total_Time) AS Fastest_Time
                FROM 
                    records r
                WHERE 
                    r.trackId = @TrackId
                    AND r.carClass IN ('A1', 'A2', 'A3', 'A4', 'A5', 'A6', 'A7', 
                                       'B1', 'B2', 'B3', 'B4', 
                                       'C1', 'C2', 'C3', 'C4', 
                                       'mA1', 'mA2')
                GROUP BY 
                    r.carClass
                ORDER BY 
                    r.carClass, Fastest_Time";

                    using (var dbCmd = new SQLiteCommand(recordsQuery, dbConn))
                    {
                        dbCmd.Parameters.AddWithValue("@TrackId", trackId);

                        using (var reader = dbCmd.ExecuteReader())
                        {
                            // Prepare a StringBuilder to collect the track info and lap records
                            var sb = new StringBuilder();
                            sb.AppendLine($"Track Name: {trackName}")
                              .AppendLine($"Total Runs: {totalRuns}")
                              .AppendLine("-----------------");

                            // Loop through each record and append the car class and the fastest time
                            while (reader.Read())
                            {
                                int x = 0;
                                string carClass = reader.GetString(0); // Car class (A1, A2, etc.)
                                string carName = reader.GetString(1);  // Car name

                                // Fastest time is treated as a string in the format "mm:ss.ms"
                                string fastestTime = reader.IsDBNull(2) ? "No Record Yet" : reader.GetString(2);

                                if (x > 0) { sb.AppendLine("-----------------"); }
                                sb.AppendLine($"{carClass} - {carName} - {fastestTime}")
                                  .AppendLine("-----------------");
                                x++;
                            }

                            // Display the result in a message box
                            AppNotice.Show(this, sb.ToString(), "Track Records");
                        }
                    }
                }
                catch (Exception ex)
                {
                    // Error handling if the query fails
                    AppNotice.Show(this, "Error: " + ex.ToString());
                }
            }
        }

        private void VehiclePreview_Click(object sender, RoutedEventArgs e)
        {
            if (combo_Vehicle.SelectedItem is DataRowView vehicle)
                VehicleImagePreview.Show(this, VehiclePreviewImage.Source, vehicle["_vehicle_name"].ToString());
        }

        private void cb_conditions_StateChanged(object sender, RoutedEventArgs e)
        {
            if (cb_conditions.IsChecked == false)
            {
                cb_weather.IsChecked = false;
            }
            else
            {
                cb_weather.IsChecked = true;
            }
        }
        private void cb_orientation_StateChanged(object sender, RoutedEventArgs e)
        {
            if (cb_orientation.IsChecked == false)
            {
                txt_orientation.IsChecked = false;
            }
            else
            {
                txt_orientation.IsChecked = true;
            }
        }
        private void cb_weather_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (cb_conditions.IsChecked == false)
            {
                cb_conditions.IsChecked = true;
            }
            else
            {
                cb_conditions.IsChecked = false;
            }

        }
        private void txt_orientation_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            
            if (cb_orientation.IsChecked == false)
            {
                cb_orientation.IsChecked = true;
            }
            else
            {
                cb_orientation.IsChecked = false;
            }
        }
        private void txt_orientation_Click(object sender, RoutedEventArgs e)
        {
            if (cb_orientation.IsChecked == false)
            {

                cb_orientation.IsChecked = true;
            }
            else
            {
                cb_orientation.IsChecked = false;
            }
        }
        private void txt_odometer_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            var textBox = sender as TextBox;
            string newText = textBox.Text.Insert(textBox.CaretIndex, e.Text);
            e.Handled = !IsOdoTextAllowed(newText);
        }
        private void txt_odometer_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Space)
            {
                e.Handled = true;
            }
        }
        private void txt_odometer_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!(sender is TextBox textBox))
                return;

            if (textBox.Text.Contains('.'))
            {
                int dotIndex = textBox.Text.IndexOf('.');
                if (textBox.Text.Length - dotIndex - 1 > 2)
                {
                    textBox.Text = textBox.Text.Substring(0, dotIndex + 3);
                    textBox.CaretIndex = textBox.Text.Length;
                }
            }
        }

        private static bool IsOdoTextAllowed(string text)
        {
            // Check for multiple dots
            if (text.Count(c => c == '.') > 1)
            {
                return false;
            }

            // Allow up to two digits after the decimal point.
            int dotIndex = text.IndexOf('.');
            if (dotIndex != -1 && text.Length - dotIndex - 1 > 2)
            {
                return false;
            }

            // Allow only digits and dot
            return text.All(c => char.IsDigit(c) || c == '.');
        }
        private void RunRetention_Checked(object sender, RoutedEventArgs e)
        {
            CheckBox other = ReferenceEquals(sender, retry) ? keep : retry;
            other.IsChecked = false;
        }

        private void KeepChoices_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            keep.IsChecked = keep.IsChecked != true;
        }

        private void Retry_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            retry.IsChecked = retry.IsChecked != true;
        }

        private void cb_weather_Click(object sender, RoutedEventArgs e)
        {
            if (cb_conditions.IsChecked == false)
            {

                cb_conditions.IsChecked = true;
            }
            else
            {
                cb_conditions.IsChecked = false;
            }
        }

        void DataGrid_LoadingRow(object sender, DataGridRowEventArgs e)
        {
            e.Row.Header = (e.Row.GetIndex() + 1).ToString();
        }

        internal void ShowEventsView()
        {
            if (string.Equals(_activeEmbeddedViewIdentifier, "EventWindow", StringComparison.Ordinal))
                RefreshRaceTypeComboBox();

            EmbeddedContentHost.Content = null;
            EmbeddedContentSurface.Visibility = Visibility.Collapsed;
            EventGroupBox.Visibility = Visibility.Visible;
            VehicleGroupBox.Visibility = Visibility.Visible;
            RaceDataGroupBox.Visibility = Visibility.Visible;
            _activeEmbeddedViewIdentifier = null;
            SetActiveNavigation(EventsNavButton);
            UpdateHudStatus();
        }

        private void OpenEmbeddedWindow<T>(string windowIdentifier, Border navigationButton) where T : Window, new()
        {
            EmbeddedViewState state;
            if (!_embeddedViews.TryGetValue(windowIdentifier, out state))
            {
                Window childWindow = new T();
                HideEmbeddedWindowChrome(childWindow);

                FrameworkElement childContent = childWindow.Content as FrameworkElement;
                if (childContent == null)
                    return;

                childWindow.Content = null;

                Grid themedContainer = new Grid
                {
                    Background = Brushes.Transparent,
                    ClipToBounds = true
                };
                ThemeManager.ApplyScopedControlStyles(themedContainer);

                bool useResponsiveLayout =
                    childWindow is houses || childWindow is dealerships ||
                    childWindow is ObjectivesWindow || childWindow is SettingsWindow;

                if (useResponsiveLayout)
                {
                    childContent.ClearValue(FrameworkElement.WidthProperty);
                    childContent.ClearValue(FrameworkElement.HeightProperty);
                    childContent.HorizontalAlignment = HorizontalAlignment.Stretch;
                    childContent.VerticalAlignment = VerticalAlignment.Stretch;
                    themedContainer.Children.Add(childContent);
                }
                else
                {
                    PreserveEmbeddedContentSize(childWindow, childContent);
                    Viewbox scaleToAvailableSpace = new Viewbox
                    {
                        Stretch = Stretch.Uniform,
                        StretchDirection = _isWorkAreaMaximized
                            ? StretchDirection.Both
                            : StretchDirection.DownOnly,
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        VerticalAlignment = VerticalAlignment.Top,
                        Child = childContent
                    };
                    themedContainer.Children.Add(scaleToAvailableSpace);
                }

                state = new EmbeddedViewState
                {
                    Window = childWindow,
                    Container = themedContainer
                };
                _embeddedViews[windowIdentifier] = state;
            }

            ShowEmbeddedView(windowIdentifier, navigationButton, state.Container);
        }

        private void ShowEmbeddedView(string windowIdentifier, Border navigationButton, FrameworkElement content)
        {
            if (string.Equals(_activeEmbeddedViewIdentifier, "EventWindow", StringComparison.Ordinal) &&
                !string.Equals(windowIdentifier, "EventWindow", StringComparison.Ordinal))
            {
                RefreshRaceTypeComboBox();
            }

            EventGroupBox.Visibility = Visibility.Collapsed;
            VehicleGroupBox.Visibility = Visibility.Collapsed;
            RaceDataGroupBox.Visibility = Visibility.Collapsed;
            EmbeddedContentHost.Content = content;
            EmbeddedContentSurface.Visibility = Visibility.Visible;
            _activeEmbeddedViewIdentifier = windowIdentifier;
            SetActiveNavigation(navigationButton);
            UpdateHudStatus();
        }

        private static void HideEmbeddedWindowChrome(Window childWindow)
        {
            string[] chromeNames =
            {
                "Minimize_Button", "Close_Button", "MinimizeButton", "CloseButton", "Settings_Button"
            };

            foreach (string name in chromeNames)
            {
                if (childWindow.FindName(name) is UIElement element)
                    element.Visibility = Visibility.Collapsed;
            }
        }

        private static void PreserveEmbeddedContentSize(Window childWindow, FrameworkElement childContent)
        {
            if (!double.IsNaN(childWindow.Width) && !double.IsInfinity(childWindow.Width) && childWindow.Width > 0)
                childContent.Width = childWindow.Width;
            else if (childWindow.MinWidth > 0)
                childContent.MinWidth = Math.Max(childContent.MinWidth, childWindow.MinWidth);

            if (!double.IsNaN(childWindow.Height) && !double.IsInfinity(childWindow.Height) && childWindow.Height > 0)
                childContent.Height = childWindow.Height;
            else if (childWindow.MinHeight > 0)
                childContent.MinHeight = Math.Max(childContent.MinHeight, childWindow.MinHeight);
        }

        private void SetActiveNavigation(Border activeButton)
        {
            Border[] navigationButtons =
            {
                EventsNavButton, TracksNavButton,
                ObjectivesNavButton, HousesNavButton, DealershipNavButton, PowerLapsNavButton, SettingsNavButton
            };

            foreach (Border button in navigationButtons)
            {
                if (button != null)
                {
                    button.SetResourceReference(
                        FrameworkElement.StyleProperty,
                        ReferenceEquals(button, activeButton) ? "AppNavActiveButtonStyle" : "AppNavButtonStyle");
                }
            }

            _activeNavigationButton = activeButton;
        }

        private void Minimize_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            Window.GetWindow(this).WindowState = WindowState.Minimized;
        }

        private void Maximize_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            ToggleMaximize();
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            if (e.ClickCount == 2)
            {
                _titleBarPress = null;
                ToggleMaximize();
            }
            else if (!_isWorkAreaMaximized && WindowState == WindowState.Normal)
            {
                _titleBarPress = e.GetPosition(this);
            }
        }

        private void TitleBar_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) => _titleBarPress = null;

        private void TitleBar_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed)
                _titleBarPress = null;
            if (!_titleBarPress.HasValue)
                return;

            Vector movement = e.GetPosition(this) - _titleBarPress.Value;
            if (Math.Abs(movement.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(movement.Y) < SystemParameters.MinimumVerticalDragDistance)
                return;

            _titleBarPress = null;
            DragMove();
        }

        private void ToggleMaximize()
        {
            if (_isWorkAreaMaximized || WindowState == WindowState.Maximized)
            {
                RestoreFromWorkArea();
            }
            else
            {
                MaximizeToWorkArea();
            }
        }

        private void MaximizeToWorkArea()
        {
            _normalWindowBounds = new Rect(Left, Top, ActualWidth, ActualHeight);
            _normalResizeMode = ResizeMode;

            SizeToContent = SizeToContent.Manual;
            WindowState = WindowState.Normal;
            ResizeMode = ResizeMode.NoResize;
            _isWorkAreaMaximized = true;

            if (!FitClientAreaToCurrentMonitor())
            {
                Rect workArea = SystemParameters.WorkArea;
                Left = workArea.Left;
                Top = workArea.Top;
                Width = workArea.Width;
                Height = workArea.Height;
            }

            UpdateMaximizeVisual();
            UpdateHudStatus();
        }

        private void RestoreFromWorkArea()
        {
            _isWorkAreaMaximized = false;
            WindowState = WindowState.Normal;
            SizeToContent = SizeToContent.Manual;
            ResizeMode = _normalResizeMode;

            if (!_normalWindowBounds.IsEmpty &&
                _normalWindowBounds.Width > 0 && _normalWindowBounds.Height > 0)
            {
                Left = _normalWindowBounds.Left;
                Top = _normalWindowBounds.Top;
                Width = _normalWindowBounds.Width;
                Height = _normalWindowBounds.Height;
            }

            UpdateMaximizeVisual();
            UpdateHudStatus();
        }

        private bool FitClientAreaToCurrentMonitor()
        {
            IntPtr windowHandle = new WindowInteropHelper(this).Handle;
            IntPtr monitorHandle = MonitorFromWindow(windowHandle, MonitorDefaultToNearest);
            MonitorInfo monitorInfo = new MonitorInfo
            {
                Size = Marshal.SizeOf(typeof(MonitorInfo))
            };

            if (monitorHandle == IntPtr.Zero || !GetMonitorInfo(monitorHandle, ref monitorInfo))
                return false;

            if (!GetWindowRect(windowHandle, out NativeRect windowRectangle) ||
                !GetClientRect(windowHandle, out NativeRect clientRectangle))
                return false;

            NativePoint clientOrigin = new NativePoint();
            if (!ClientToScreen(windowHandle, ref clientOrigin))
                return false;

            int clientWidth = clientRectangle.Right - clientRectangle.Left;
            int clientHeight = clientRectangle.Bottom - clientRectangle.Top;
            int leftInset = clientOrigin.X - windowRectangle.Left;
            int topInset = clientOrigin.Y - windowRectangle.Top;
            int rightInset = windowRectangle.Right - (clientOrigin.X + clientWidth);
            int bottomInset = windowRectangle.Bottom - (clientOrigin.Y + clientHeight);

            // Keep the monitor work area and window frame in the same native coordinates.
            NativeRect targetWorkArea = monitorInfo.WorkArea;
            int outerLeft = targetWorkArea.Left - leftInset;
            int outerTop = targetWorkArea.Top - topInset;
            int outerWidth = targetWorkArea.Right - targetWorkArea.Left + leftInset + rightInset;
            int outerHeight = targetWorkArea.Bottom - targetWorkArea.Top + topInset + bottomInset;

            return SetWindowPos(
                windowHandle,
                IntPtr.Zero,
                outerLeft,
                outerTop,
                outerWidth,
                outerHeight,
                SetWindowPosNoZOrder | SetWindowPosNoActivate);
        }

        private void Close_Button_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            _windowClosed = true;
            _liveSaveTimer.Stop();
            base.OnClosed(e);

            // Embedded pages retain their original Window objects so their
            // content can be reused. They must not keep the process alive once
            // the active main window has been closed (including via Alt+F4).
            if (ReferenceEquals(Application.Current?.MainWindow, this))
                Application.Current.Shutdown();
        }

        private void PowerLaps_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            if (_powerLapsView == null)
                _powerLapsView = new powerlap();
            ShowEmbeddedView("PowerLapBoard", PowerLapsNavButton, _powerLapsView);
        }

        private void Events_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            ShowEventsView();
        }

        private void Objectives_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            OpenEmbeddedWindow<ObjectivesWindow>("ObjectivesWindow", ObjectivesNavButton);
        }

        private void Houses_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            OpenEmbeddedWindow<houses>("HousesWindow", HousesNavButton);
        }

        private void Dealership_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            OpenEmbeddedWindow<dealerships>("DealershipsWindow", DealershipNavButton);
        }
        // Usage for opening specific child windows
        private void tracks_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            OpenEmbeddedWindow<tracks>("EventWindow", TracksNavButton);
        }
        private void Settings_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            OpenEmbeddedWindow<SettingsWindow>("SettingsWindow", SettingsNavButton);
        }
        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed &&
                !_isWorkAreaMaximized && WindowState != WindowState.Maximized)
            {
                this.DragMove();
            }
        }
        private void Race_Ms_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            ResetAvgSpeedText(sender, e, AverageRace_Speed);
        }
        private void Lap1_Ms_PreviewLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            RefreshRaceAverageSpeed();
        }
        private void ResetAvgSpeedText(object sender, KeyboardFocusChangedEventArgs e, TextBlock avgSpeedTextBlock)
        {
            avgSpeedTextBlock.Text = "0.00";
            Speed_Text.Text = speed;
        }

        private void TextBlock_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            OpenLapInputWindow();
        }
        private void OpenLapInputWindow()
        {
            LapInputWindow lapInputWindow = new LapInputWindow { Owner = this };
            if (lapInputWindow.ShowDialog() == true)
            {
                int total = lapInputWindow.TotalHundredths;
                Race_Min.Text = (total / 6000).ToString("D2");
                Race_Sec.Text = (total / 100 % 60).ToString("D2");
                Race_Ms.Text = (total % 100).ToString("D2");
                RefreshRaceAverageSpeed();
                calc_Average_Lap_Time();
            }
        }
    }
}

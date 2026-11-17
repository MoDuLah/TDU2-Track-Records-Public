using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Data.SQLite;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using TDU2_Track_Records.Properties;

namespace TDU2_Track_Records
{
    public partial class tracks : Window
    {
        readonly string connectionString = Settings.Default.connectionString;
        private bool isReady;
        private bool loadingEvent;
        private bool IsAdding => OperationTabs.SelectedItem == AddEventTab;
        private bool IsDeleting => OperationTabs.SelectedItem == DeleteEventTab;

        public tracks()
        {
            InitializeComponent();
            Rect workArea = SystemParameters.WorkArea;
            Width = Math.Min(760, Math.Max(MinWidth, workArea.Width - 48));
            Height = Math.Min(700, Math.Max(MinHeight, workArea.Height - 48));
            MaxWidth = Width;
            MaxHeight = Height;
            Left = workArea.Left + Math.Max(0, (workArea.Width - Width) / 2);
            Top = workArea.Top + Math.Max(0, (workArea.Height - Height) / 2);
            OrientationGroupBox.Visibility = Visibility.Collapsed;
            LapsGroupBox.Visibility = Visibility.Collapsed;
            DistanceGroupBox.Visibility = Visibility.Collapsed;
            SpeedGroupBox.Visibility = Visibility.Collapsed;
            CheckpointsGroupBox.Visibility = Visibility.Collapsed;
            RestrictedClassGroupBox.Visibility = Visibility.Collapsed;
            RestrictedCarGroupBox.Visibility = Visibility.Collapsed;


            List<ComboBoxItem> items = new List<ComboBoxItem>
            {
                new ComboBoxItem { ImagePath = "Images/ico/events/ico_cup_race.png", Description = "Race Cup", Value = "Race SP" },
                new ComboBoxItem { ImagePath = "Images/ico/events/ico_cup_speed.png", Description = "Speed Cup", Value = "Speed SP" },
                new ComboBoxItem { ImagePath = "Images/ico/events/ico_cup_eliminator.png", Description = "Eliminator Cup", Value = "Eliminator SP" },
                new ComboBoxItem { ImagePath = "Images/ico/events/ico_cup_speedtrap.png", Description = "Speed Trap Cup", Value = "Speed Trap SP" },
                new ComboBoxItem { ImagePath = "Images/ico/events/ico_cup_timeattack.png", Description = "Time Attack Cup", Value = "Time Attack SP" },
                new ComboBoxItem { ImagePath = "Images/ico/events/ico_mp_race.png", Description = "Race Multiplayer", Value = "Race MP" },
                new ComboBoxItem { ImagePath = "Images/ico/events/ico_mp_race.png", Description = "Ranked Multiplayer Race", Value = "Race RMP" },
                new ComboBoxItem { ImagePath = "Images/ico/events/ico_mp_speed.png", Description = "Speed Multiplayer", Value = "Speed MP" },
                new ComboBoxItem { ImagePath = "Images/ico/events/ico_mp_ftl.png", Description = "Follow The Leader", Value = "Follow The Leader MP" },
                new ComboBoxItem { ImagePath = "Images/ico/events/ico_mp_kyd.png", Description = "Keep Your Distance", Value = "Keep Your Distance MP" },
                new ComboBoxItem { ImagePath = "Images/ico/events/ico_mp_speedtrap.png", Description = "Speed Trap Multiplayer", Value = "Speed Trap MP" },
                new ComboBoxItem { ImagePath = "Images/ico/events/ico_cup_timeattack.png", Description = EventCatalog.RaceTypeLabel("Time Attack PC", "Time Attack"), Value = "Time Attack PC" },
                new ComboBoxItem { ImagePath = "Images/ico/events/ico_cup_speed.png", Description = EventCatalog.RaceTypeLabel("Speed PC", "Speed"), Value = "Speed PC" },
                new ComboBoxItem { ImagePath = "Images/ico/events/ico_cup_speedtrap.png", Description = EventCatalog.RaceTypeLabel("Speed Trap PC", "Speed Trap"), Value = "Speed Trap PC" }
            };
            RaceTypeComboBox.ItemsSource = items;
            PopulateClasses();
            ShowLastEntry();
            isReady = true;
            UpdateOperation();
        }

        internal void RefreshMeasurementUnits()
        {
            bool imperial = MeasurementUnits.IsImperial;
            MeasurementUnits.ConvertDraft(TotalDistanceTextBox, MeasurementUnits.MilesPerKilometer, imperial);
            MeasurementUnits.ConvertDraft(MinSpeedTextBox, MeasurementUnits.MilesPerKilometer, imperial);
        }

        private void PopulateClasses()
        {
            List<ComboBoxItem> items = new List<ComboBoxItem>
            {
                new ComboBoxItem { ImagePath = "Images/carClasses/SC.png", Description = "None", Value = "" },
                new ComboBoxItem { ImagePath = "Images/carClasses/SC.png", Description = "Any Asphalt class", Value = "A" },
                new ComboBoxItem { ImagePath = "Images/carClasses/SC.png", Description = "Any Rally class", Value = "B" },
                new ComboBoxItem { ImagePath = "Images/carClasses/SC.png", Description = "Any Classic class", Value = "C" },
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

            RestrictedClassComboBox.ItemsSource = items;
            //RestrictedCarComboBox.ItemsSource = "";
        }

        private void ShowLastEntry()
        {
            try
            {
                // Define the query to get the last entry
                string query = "SELECT Island, Name, RaceType, Length, RestrictedClass FROM tracks ORDER BY id DESC LIMIT 1";

                // Execute the query and retrieve the data
                using (var conn = new SQLiteConnection(connectionString))
                {
                    conn.Open();
                    using (var cmd = new SQLiteCommand(query, conn))
                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read()) // If a record is found
                        {
                            // Retrieve the values from the last entry
                            string trackName = reader["Name"].ToString();
                            string island = reader["Island"].ToString();
                            string raceType = reader["RaceType"].ToString();
                            double length = reader["Length"] == DBNull.Value ? 0 : Convert.ToDouble(reader["Length"]);
                            string distance = $"{MeasurementUnits.FromMetric(length, MeasurementUnits.MilesPerKilometer):0.##} {Settings.Default.distance}";
                            string RestictClass = reader["RestrictedClass"].ToString(); 
                            // Update the TextBlock with the information
                            LastEntry.Text = $"Last Entry: {trackName},     Island: {island},     Class: {RestictClass},     Type: {EventCatalog.RaceTypeLabel(raceType, raceType)},     Total Distance: {distance}";
                        }
                        else
                        {
                            // If no record is found, display a message
                            LastEntry.Text = "No entries found.";
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Handle any errors that may have occurred
                AppNotice.Show(this, "An error occurred while retrieving the last entry: " + ex.Message, "Error", persistent: true);
            }
        }
        private void BindVehicleComboBox(string storedCar = "")
        {
            const string sql = @"
                SELECT id, _vehicle_name FROM (
                    SELECT '' AS id, 'None' AS _vehicle_name
                    UNION ALL
                    SELECT CAST(id AS TEXT), _vehicle_name
                    FROM vehicles
                    WHERE (_is_purchasable = 'true' AND
                           (@Class = '' OR _vehiclecategory_name = @Class COLLATE NOCASE
                            OR (LENGTH(@Class) = 1 AND _vehiclecategory_name LIKE @Class || '%')))
                          OR CAST(id AS TEXT) = @StoredCar
                    UNION ALL
                    SELECT @StoredCar, 'Vehicle #' || @StoredCar
                    WHERE @StoredCar <> '' AND NOT EXISTS (
                        SELECT 1 FROM vehicles WHERE CAST(id AS TEXT) = @StoredCar)
                )
                ORDER BY id <> '', _vehicle_name COLLATE NOCASE;";

            string selectedClass = (RestrictedClassComboBox.SelectedItem as ComboBoxItem)?.Value ?? "";
            string selectedCar = loadingEvent ? storedCar : RestrictedCarComboBox.SelectedValue?.ToString() ?? "";
            using (var connection = new SQLiteConnection(connectionString))
            using (var command = new SQLiteCommand(sql, connection))
            using (var adapter = new SQLiteDataAdapter(command))
            {
                command.Parameters.AddWithValue("@Class", selectedClass);
                command.Parameters.AddWithValue("@StoredCar", storedCar);
                var table = new DataTable();
                adapter.Fill(table);
                RestrictedCarComboBox.ItemsSource = table.DefaultView;
                RestrictedCarComboBox.SelectedValue = selectedCar;
                if (RestrictedCarComboBox.SelectedIndex < 0)
                    RestrictedCarComboBox.SelectedIndex = 0;
            }
        }

        private void OperationTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!isReady || e.Source != OperationTabs)
                return;

            LastEntry.Text = "";
            if (IsAdding)
            {
                ClearInputFields();
            }
            else if (EventPicker.ItemsSource == null || e.RemovedItems.Contains(AddEventTab))
            {
                ReloadEventPicker();
            }

            UpdateOperation();
        }

        private void ReloadEventPicker(int? selectedId = null)
        {
            const string sql = @"
                SELECT t.*, COALESCE(rt.type_Description, t.RaceType, '') AS Description,
                       (SELECT COUNT(*) FROM records r WHERE r.trackId = t.id) AS SavedRuns
                FROM tracks t
                LEFT JOIN race_types rt ON rt.type_Value = t.RaceType
                ORDER BY t.Name COLLATE NOCASE, t.Island, t.RaceType, t.id;";
            using (var connection = new SQLiteConnection(connectionString))
            using (var adapter = new SQLiteDataAdapter(sql, connection))
            {
                var table = new DataTable();
                adapter.Fill(table);
                table.Columns.Add("DisplayName", typeof(string));
                foreach (DataRow row in table.Rows)
                {
                    string type = EventCatalog.RaceTypeLabel(row["RaceType"].ToString(), row["Description"].ToString());
                    row["DisplayName"] = $"{row["Name"]}  ·  {GetEventIsland(row)}  ·  {type}  ·  #{row["id"]}";
                }

                EventPicker.ItemsSource = table.DefaultView.Cast<DataRowView>().ToList();
                ICollectionView view = CollectionViewSource.GetDefaultView(EventPicker.ItemsSource);
                view.Filter = item => ((DataRowView)item)["DisplayName"].ToString()
                    .IndexOf(EventFilterTextBox.Text.Trim(), StringComparison.OrdinalIgnoreCase) >= 0;
                EventPicker.SelectedValue = selectedId;
            }
        }

        private void EventFilterTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (EventPicker?.ItemsSource != null)
                CollectionViewSource.GetDefaultView(EventPicker.ItemsSource).Refresh();
        }

        private void EventPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (isReady)
                UpdateOperation();
        }

        private void UpdateOperation()
        {
            DataRowView selected = EventPicker.SelectedItem as DataRowView;
            EventPickerGroup.Visibility = IsAdding ? Visibility.Collapsed : Visibility.Visible;
            KeepOptions.Visibility = IsAdding ? Visibility.Visible : Visibility.Collapsed;
            EventEditor.Visibility = IsDeleting ? Visibility.Collapsed : Visibility.Visible;
            EventEditor.IsEnabled = IsAdding || selected != null;
            EventActionButton.Content = IsAdding ? "Add Event" : IsDeleting ? "Delete Event" : "Save Changes";
            EventActionButton.IsEnabled = IsAdding || selected != null;
            DeleteEventSummary.Visibility = IsDeleting ? Visibility.Visible : Visibility.Collapsed;
            EventRecordsGroup.Visibility = !IsAdding && !IsDeleting && selected != null
                ? Visibility.Visible : Visibility.Collapsed;

            if (IsAdding)
                return;
            if (IsDeleting)
            {
                DeleteEventSummary.Text = selected == null
                    ? "Select an event to see what will be deleted."
                    : $"{selected["DisplayName"]}\n\n{DeleteRunDescription(Convert.ToInt32(selected["SavedRuns"]))}";
            }
            else if (selected == null)
            {
                ClearInputFields();
            }
            else
            {
                LoadEvent(selected);
            }
        }

        private static string GetEventIsland(DataRow row)
        {
            string island = row["Island"].ToString().Trim();
            return island.Length == 0 ? "Hawaii" : island;
        }

        private void LoadEvent(DataRowView track)
        {
            loadingEvent = true;
            try
            {
                string raceType = track["RaceType"].ToString();
                RaceTypeComboBox.SelectedValue = raceType;
                IslandComboBox.SelectedItem = IslandComboBox.Items.OfType<System.Windows.Controls.ComboBoxItem>()
                    .FirstOrDefault(item => item.Content.ToString() == GetEventIsland(track.Row));
                TrackNameTextBox.Text = track["Name"].ToString();

                double length = track["Length"] == DBNull.Value ? 0 : Convert.ToDouble(track["Length"]);
                TotalDistanceTextBox.Text = length > 0 ? length.ToString("R", CultureInfo.CurrentCulture) : "";
                if (MeasurementUnits.IsImperial)
                    MeasurementUnits.ConvertDraft(TotalDistanceTextBox, MeasurementUnits.MilesPerKilometer, true);
                LapsTextBox.Text = track["Laps"].ToString() == "0" ? "" : track["Laps"].ToString();
                double minSpeed = track["MinSpeed"] == DBNull.Value ? 0 : Convert.ToDouble(track["MinSpeed"]);
                MinSpeedTextBox.Text = MeasurementUnits.FromMetric(minSpeed, MeasurementUnits.MilesPerKilometer).ToString("0.##");
                CheckpointsTextBox.Text = track["Checkpoints"].ToString();
                cb_orientation.IsChecked = OrientationGroupBox.Visibility == Visibility.Visible && track["Orientation"].ToString() == "1";

                string carClass = track["RestrictedClass"].ToString().Trim();
                if (string.Equals(carClass, "ALL", StringComparison.OrdinalIgnoreCase))
                    carClass = "";
                RestrictedClassComboBox.SelectedItem = RestrictedClassComboBox.Items.OfType<ComboBoxItem>()
                    .FirstOrDefault(item => string.Equals(item.Value, carClass, StringComparison.OrdinalIgnoreCase));
                if (RestrictedCarGroupBox.Visibility == Visibility.Visible)
                    BindVehicleComboBox(track["RestrictedCar"].ToString().Trim());

                var bests = EventCatalog.ReadEventBests(connectionString, Convert.ToInt32(track["id"]), raceType);
                string heading = EventCatalog.IsSpeedTrap(raceType) ? "Best average speed"
                    : EventCatalog.IsScoreBasedRaceType(raceType) ? "Overall high score" : "Overall lap / race record";
                OverallRecordText.Text = $"{heading}  ·  {bests.Overall ?? "No record yet"}";
                ClassRecordsList.ItemsSource = bests.ByClass.Count > 0
                    ? bests.ByClass
                    : new List<string> { "No class records yet." };
            }
            finally
            {
                loadingEvent = false;
            }
        }

        private static string DeleteRunDescription(int runCount)
        {
            if (runCount == 0)
                return "This event has no saved runs.";
            return runCount == 1
                ? "Deleting this event will permanently remove its 1 saved run."
                : $"Deleting this event will permanently remove its {runCount} saved runs.";
        }

        private async System.Threading.Tasks.Task ConfirmDelete(DataRowView track)
        {
            int runCount = Convert.ToInt32(track["SavedRuns"]);
            if (!await AppNotice.ConfirmAsync(this, $"Delete {track["DisplayName"]}?",
                DeleteRunDescription(runCount), "Delete Event"))
                return;

            bool deleted = EventCatalog.DeleteTrack(connectionString, Convert.ToInt32(track["id"]), runCount);
            ReloadEventPicker();
            LastEntry.Text = deleted ? $"Deleted {track["Name"]}." : "The event or its saved runs changed. Review the selection again.";
        }

        private async void EventActionButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var track = EventPicker.SelectedItem as DataRowView;
                if (IsDeleting)
                {
                    if (track != null)
                        await ConfirmDelete(track);
                }
                else if (IsAdding || track != null)
                {
                    SaveEvent(IsAdding ? null : track);
                }
            }
            catch (Exception ex) when (ex is SQLiteException || ex is InvalidOperationException)
            {
                AppNotice.Show(this, ex.Message, "Tracks & Events");
            }
        }

        private void SaveEvent(DataRowView original)
        {
            if (!(RaceTypeComboBox.SelectedItem is ComboBoxItem selectedType))
            {
                ShowValidationError("Select a race type.", RaceTypeComboBox);
                return;
            }

            string island = (IslandComboBox.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content?.ToString();
            if (string.IsNullOrWhiteSpace(island))
            {
                ShowValidationError("Select an island.", IslandComboBox);
                return;
            }

            string trackName = TrackNameTextBox.Text.Trim();
            if (trackName.Length == 0)
            {
                ShowValidationError("Enter an event name.", TrackNameTextBox);
                return;
            }

            string raceType = selectedType.Value;
            bool sameType = original != null && original["RaceType"].ToString() == raceType;
            bool optionalCourseDetails = raceType == "Speed PC" || raceType == "Speed Trap PC";
            double totalDistance = 0;
            if (DistanceGroupBox.Visibility == Visibility.Visible &&
                (!optionalCourseDetails || !string.IsNullOrWhiteSpace(TotalDistanceTextBox.Text)))
            {
                if (!MeasurementUnits.TryParse(TotalDistanceTextBox.Text, out double distance) || distance <= 0)
                {
                    ShowValidationError("Enter a total distance greater than zero.", TotalDistanceTextBox);
                    return;
                }

                totalDistance = MeasurementUnits.DraftToMetric(TotalDistanceTextBox, distance, MeasurementUnits.MilesPerKilometer);
            }

            int laps = 0;
            if (LapsGroupBox.Visibility == Visibility.Visible &&
                (!optionalCourseDetails || !string.IsNullOrWhiteSpace(LapsTextBox.Text)) &&
                (!int.TryParse(LapsTextBox.Text, out laps) || laps < 1))
            {
                ShowValidationError("Enter a whole number of laps greater than zero.", LapsTextBox);
                return;
            }

            int minSpeed = 0;
            if (SpeedGroupBox.Visibility == Visibility.Visible &&
                !MeasurementUnits.TryReadMetricInteger(MinSpeedTextBox.Text, MeasurementUnits.MilesPerKilometer, out minSpeed))
            {
                ShowValidationError("Enter a valid minimum speed.", MinSpeedTextBox);
                return;
            }

            int checkpoints = 0;
            if (CheckpointsGroupBox.Visibility == Visibility.Visible &&
                !string.IsNullOrWhiteSpace(CheckpointsTextBox.Text) &&
                (!int.TryParse(CheckpointsTextBox.Text, out checkpoints) || checkpoints < 0))
            {
                ShowValidationError("Enter a non-negative whole number for cameras or checkpoints.", CheckpointsTextBox);
                return;
            }

            int orientation = OrientationGroupBox.Visibility == Visibility.Visible && cb_orientation.IsChecked == true ? 1 : 0;
            string restrictedClass = RestrictedClassGroupBox.Visibility == Visibility.Visible
                ? (RestrictedClassComboBox.SelectedItem as ComboBoxItem)?.Value : null;
            string restrictedCar = RestrictedCarGroupBox.Visibility == Visibility.Visible
                ? (RestrictedCarComboBox.SelectedItem as DataRowView)?["id"].ToString() : null;

            if (sameType)
            {
                if (DistanceGroupBox.Visibility != Visibility.Visible)
                    totalDistance = original.Row.Field<double?>("Length") ?? 0;
                if (LapsGroupBox.Visibility != Visibility.Visible)
                    laps = Convert.ToInt32(original["Laps"] == DBNull.Value ? 0 : original["Laps"]);
            }

            int? trackId = original == null ? (int?)null : Convert.ToInt32(original["id"]);
            SaveTrackToDatabase(trackId, island, trackName, raceType, orientation, totalDistance, laps, minSpeed,
                checkpoints, restrictedClass, restrictedCar);

            if (trackId.HasValue)
            {
                EventFilterTextBox.Clear();
                ReloadEventPicker(trackId);
                LastEntry.Text = $"Saved changes to {trackName}.";
            }
            else
            {
                LastEntry.Text = $"Added {trackName} ({island}, {selectedType.Description})";
                ClearInputFields(KeepCheckBox.IsChecked == true);
                RaceTypeComboBox.Focus();
            }
        }

        private void SaveTrackToDatabase(int? trackId, string island, string trackName, string raceType,
            int orientation, double totalDistance, int laps, int minSpeed, int checkpoints,
            string restrictedClass, string restrictedCar)
        {
            const string insert = @"
                INSERT INTO tracks
                    (Island, Name, Length, RaceType, Orientation, Laps, MinSpeed, Checkpoints, RestrictedClass, RestrictedCar, Runs)
                VALUES
                    (@island, @name, @length, @type, @orientation, @laps, @minSpeed, @checkpoints, @restrictedClass, @restrictedCar, 0);";
            const string update = @"
                UPDATE tracks SET Island=@island, Name=@name, Length=@length, RaceType=@type, Orientation=@orientation,
                                  Laps=@laps, MinSpeed=@minSpeed, Checkpoints=@checkpoints,
                                  RestrictedClass=CASE WHEN RaceType='Race MP' AND @type='Race MP'
                                                       THEN RestrictedClass ELSE @restrictedClass END,
                                  RestrictedCar=CASE WHEN RaceType='Race MP' AND @type='Race MP'
                                                     THEN RestrictedCar ELSE @restrictedCar END
                WHERE id=@id;";

            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();
                using (var transaction = connection.BeginTransaction())
                using (var command = new SQLiteCommand(trackId.HasValue ? update : insert, connection, transaction))
                {
                    if (trackId.HasValue)
                    {
                        command.Parameters.Add("@id", DbType.Int32).Value = trackId.Value;
                        UpdateRecordTypes(connection, transaction, trackId.Value, raceType);
                    }

                    command.Parameters.Add("@island", DbType.String).Value = island;
                    command.Parameters.Add("@name", DbType.String).Value = trackName;
                    command.Parameters.Add("@type", DbType.String).Value = raceType;
                    command.Parameters.Add("@orientation", DbType.Int32).Value = EventCatalog.SupportsOrientation(raceType, laps) ? orientation : 0;
                    command.Parameters.Add("@length", DbType.Double).Value = totalDistance;
                    command.Parameters.Add("@laps", DbType.Int32).Value = laps;
                    command.Parameters.Add("@minSpeed", DbType.Int32).Value = minSpeed;
                    command.Parameters.Add("@checkpoints", DbType.Int32).Value = checkpoints;
                    command.Parameters.Add("@restrictedClass", DbType.String).Value = string.IsNullOrEmpty(restrictedClass) ? (object)DBNull.Value : restrictedClass;
                    command.Parameters.Add("@restrictedCar", DbType.String).Value = string.IsNullOrEmpty(restrictedCar) ? (object)DBNull.Value : restrictedCar;
                    if (command.ExecuteNonQuery() != 1)
                        throw new InvalidOperationException("The event could not be saved.");
                    transaction.Commit();
                }
            }
        }

        private static void UpdateRecordTypes(SQLiteConnection connection, SQLiteTransaction transaction, int trackId, string raceType)
        {
            using (var command = new SQLiteCommand("SELECT RaceType FROM tracks WHERE id = @Id;", connection, transaction))
            {
                command.Parameters.AddWithValue("@Id", trackId);
                object oldType = command.ExecuteScalar();
                if (oldType == null)
                    throw new InvalidOperationException("This event no longer exists.");
                string oldRaceType = oldType.ToString();
                if (oldRaceType == raceType)
                    return;

                if (EventCatalog.IsSpeedTrap(oldRaceType) != EventCatalog.IsSpeedTrap(raceType) ||
                    EventCatalog.IsScoreBasedRaceType(oldRaceType) != EventCatalog.IsScoreBasedRaceType(raceType))
                {
                    command.CommandText = "SELECT EXISTS (SELECT 1 FROM records WHERE trackId = @Id);";
                    if (Convert.ToInt32(command.ExecuteScalar()) != 0)
                        throw new InvalidOperationException("An event with saved runs cannot switch between time, points, and average speed results.");
                }

                command.CommandText = "UPDATE records SET raceType = @Type WHERE trackId = @Id;";
                command.Parameters.AddWithValue("@Type", raceType);
                command.ExecuteNonQuery();
            }
        }


        private void ShowValidationError(string message, Control controlToFocus)
        {
            AppNotice.Show(this, message, "Validation Error");
            controlToFocus.Focus();
        }



        /// <summary>
        /// Clears the input fields after successful addition.
        /// </summary>
        private void ClearInputFields(bool keepLocation = false)
        {

            TrackNameTextBox.Clear();
            TotalDistanceTextBox.Clear();
            LapsTextBox.Text = "1";
            MinSpeedTextBox.Text = "0";
            CheckpointsTextBox.Text = "0";
            RaceTypeComboBox.SelectedIndex = -1; // Reset selection
            cb_orientation.IsChecked = false; // Reset selection
            RestrictedCarComboBox.SelectedIndex = -1;
            if (!keepLocation)
            {
                IslandComboBox.SelectedIndex = -1;
                RestrictedClassComboBox.SelectedIndex = 0;
            }
            OrientationGroupBox.Visibility = Visibility.Collapsed;
            LapsGroupBox.Visibility = Visibility.Collapsed;
            DistanceGroupBox.Visibility = Visibility.Collapsed;
            SpeedGroupBox.Visibility = Visibility.Collapsed;
            CheckpointsGroupBox.Visibility = Visibility.Collapsed;
            RestrictedCarGroupBox.Visibility = Visibility.Collapsed;
            RestrictedClassGroupBox.Visibility = Visibility.Collapsed;
        }
        private void RaceTypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!(RaceTypeComboBox.SelectedItem is ComboBoxItem selectedType))
                return;

            string raceType = selectedType.Value;
            bool isTimeAttack = EventCatalog.IsTimeAttack(raceType);
            bool isTimed = raceType == "Race SP" || raceType == "Eliminator SP" ||
                           raceType == "Race MP" || raceType == "Race RMP" || isTimeAttack;
            bool isSpeed = raceType == "Speed SP" || raceType == "Speed MP" || raceType == "Speed PC";
            bool isSpeedTrap = EventCatalog.IsSpeedTrap(raceType);
            bool isCrc = EventCatalog.IsCrcChallenge(raceType);
            bool singlePlayer = raceType.EndsWith("SP", StringComparison.Ordinal);
            bool hasVehicleRestriction = raceType != "Race MP" &&
                                         (raceType.EndsWith("MP", StringComparison.Ordinal) || isCrc ||
                                          (!IsAdding && singlePlayer));

            DistanceGroupBox.Visibility = isTimed || isCrc ? Visibility.Visible : Visibility.Collapsed;
            DistanceGroupBox.Header = isTimed ? "Total Distance" : "Total Distance (optional)";
            LapsGroupBox.Visibility = DistanceGroupBox.Visibility;
            LapsGroupBox.Header = isTimed ? "Number of Laps" : "Number of Laps (optional)";
            LapsTextBox.Text = raceType == "Eliminator SP" ? "7" : isTimed ? "1" : "";
            LapsTextBox_TextChanged(LapsTextBox, null);
            SpeedGroupBox.Visibility = isSpeed ? Visibility.Visible : Visibility.Collapsed;
            CheckpointsGroupBox.Visibility = isSpeedTrap || isCrc ? Visibility.Visible : Visibility.Collapsed;
            CheckpointsGroupBox.Header = isSpeedTrap ? "Cameras" : "Checkpoints (optional)";
            RestrictedClassGroupBox.Visibility = hasVehicleRestriction || singlePlayer
                ? Visibility.Visible : Visibility.Collapsed;
            RestrictedCarGroupBox.Visibility = hasVehicleRestriction ? Visibility.Visible : Visibility.Collapsed;

            if (hasVehicleRestriction && !loadingEvent)
                BindVehicleComboBox();
        }
        private void LapsTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (OrientationGroupBox == null)
                return;

            string raceType = (RaceTypeComboBox?.SelectedItem as ComboBoxItem)?.Value;
            OrientationGroupBox.Visibility = int.TryParse(LapsTextBox.Text, out int laps) &&
                EventCatalog.SupportsOrientation(raceType, laps) ? Visibility.Visible : Visibility.Collapsed;
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

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(sender as DependencyObject) is MainWindow mainWindow)
            {
                mainWindow.ShowEventsView();
                return;
            }

            Close();
        }

        private void MinimizeButton_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            WindowState = WindowState.Minimized;
            e.Handled = true;
        }

        private void CloseButton_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            Close();
            e.Handled = true;
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            // Any cleanup or additional actions when the window is closed
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e is null)
            {
                throw new ArgumentNullException(nameof(e));
            }

            if (Window.GetWindow(e.OriginalSource as DependencyObject) != this)
                return;

            if (e.ButtonState == MouseButtonState.Pressed)
            {
                this.DragMove();
            }
        }

        private void RestrictedClassComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (isReady && !loadingEvent && RestrictedCarGroupBox.Visibility == Visibility.Visible)
                BindVehicleComboBox();
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter || IsDeleting || !EventActionButton.IsEnabled ||
                EventFilterTextBox.IsKeyboardFocusWithin || EventPicker.IsKeyboardFocusWithin)
                return;

            EventActionButton_Click(sender, e);
            e.Handled = true;
        }
    }
}

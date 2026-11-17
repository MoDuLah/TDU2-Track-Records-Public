using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace TDU2_Track_Records
{
    internal sealed class GarageBridgeSource
    {
        public string path { get; set; }
        public long size { get; set; }
        public string sha256 { get; set; }
    }

    internal sealed class GarageBridgeVehicle
    {
        public long instance_id { get; set; }
        public int vehicle_id { get; set; }
        public int garage_index { get; set; }
        public long house_hash { get; set; }
        public int garage_slot { get; set; }
        public int garage_capacity { get; set; }
        public double mileage_km { get; set; }
    }

    internal sealed class SaveHouseReference
    {
        public long hash { get; set; }
        public string island { get; set; }
        public string name { get; set; }
    }

    internal sealed class GarageBridgeSnapshot
    {
        public int format_version { get; set; }
        public string profile_key { get; set; }
        public string profile { get; set; }
        public string captured_utc { get; set; }
        public GarageBridgeSource source { get; set; }
        public List<GarageBridgeVehicle> vehicles { get; set; }
        public List<SaveObjectiveEntry> licences { get; set; }
        public List<SaveObjectiveEntry> contest_results { get; set; }
        public int? ranked_wins { get; set; }
        public long? inner_club_played { get; set; }
        public long? versus_club_played { get; set; }
        public List<long> house_hashes { get; set; }
        public List<SaveHouseReference> house_reference { get; set; }
        public List<int> event_xps { get; set; }
        public List<int> roads_unlocked { get; set; }
        public List<int> roads_total { get; set; }
    }

    internal sealed class GarageImportResult
    {
        public string ProfileKey { get; set; }
        public string Profile { get; set; }
        public int ImportedVehicles { get; set; }
        public int CatalogueMatches { get; set; }
        public int UnknownModels { get; set; }
        public string SourceHash { get; set; }
        public DateTime SourceModifiedLocal { get; set; }
        public bool GarageChanged { get; set; } = true;
    }

    internal sealed class SaveProfileInfo
    {
        public string ProfileKey { get; set; }
        public string ProfileName { get; set; }
        public bool IsActive { get; set; }
        public bool SourceAvailable { get; set; }
        public string DataPath { get; set; }
        public string DisplayName => SourceAvailable ? ProfileName : ProfileName + " (save missing)";
    }

    internal static class SaveGameGarageImport
    {
        private const double MilesPerKilometre = 0.621371192;

        public static void EnsureSchema(string connectionString)
        {
            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();
                const string schema = @"
                    CREATE TABLE IF NOT EXISTS save_profiles (
                        profile_key TEXT PRIMARY KEY,
                        profile_name TEXT NOT NULL,
                        is_active INTEGER NOT NULL DEFAULT 0,
                        source_sha256 TEXT NOT NULL,
                        source_size INTEGER NOT NULL,
                        captured_utc TEXT NOT NULL,
                        imported_utc TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                        source_available INTEGER NOT NULL DEFAULT 1,
                        last_seen_utc TEXT
                    );
                    CREATE TABLE IF NOT EXISTS garage_vehicles (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        profile_key TEXT NOT NULL,
                        instance_id INTEGER NOT NULL,
                        vehicle_id INTEGER NOT NULL,
                        garage_index INTEGER NOT NULL,
                        house_hash INTEGER,
                        house_id INTEGER,
                        garage_slot INTEGER,
                        garage_capacity INTEGER,
                        odometer_metric REAL NOT NULL,
                        odometer_imperial REAL NOT NULL,
                        is_present INTEGER NOT NULL DEFAULT 1,
                        imported_utc TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                        UNIQUE(profile_key, instance_id)
                    );
                    CREATE INDEX IF NOT EXISTS idx_garage_vehicles_active
                        ON garage_vehicles(profile_key, is_present, vehicle_id);
                    CREATE TABLE IF NOT EXISTS save_houses (
                        profile_key TEXT NOT NULL,
                        house_hash INTEGER NOT NULL,
                        PRIMARY KEY (profile_key, house_hash)
                    );
                    CREATE VIEW IF NOT EXISTS displayed_vehicle_ownership AS
                    SELECT v.id, p.profile_key, CASE WHEN p.profile_key IS NULL THEN v._is_owned
                        WHEN EXISTS (SELECT 1 FROM garage_vehicles g
                            WHERE g.profile_key=p.profile_key AND g.vehicle_id=v.id AND g.is_present=1)
                        THEN 'true' ELSE 'false' END AS is_owned
                    FROM vehicles v LEFT JOIN save_profiles p ON p.is_active=1;
                    CREATE TABLE IF NOT EXISTS garage_house_mappings (
                        house_hash INTEGER PRIMARY KEY,
                        house_id INTEGER NOT NULL,
                        evidence_count INTEGER NOT NULL DEFAULT 1,
                        learned_utc TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
                    );";
                using (var command = new SQLiteCommand(schema, connection))
                    command.ExecuteNonQuery();

                EnsureColumn(connection, "save_profiles", "source_available", "INTEGER NOT NULL DEFAULT 1");
                EnsureColumn(connection, "save_profiles", "last_seen_utc", "TEXT");
                EnsureColumn(connection, "save_profiles", "objective_source_sha256", "TEXT");
                SaveObjectiveSync.EnsureSchema(connection);
                EnsureColumn(connection, "garage_vehicles", "house_hash", "INTEGER");
                EnsureColumn(connection, "garage_vehicles", "house_id", "INTEGER");
                EnsureColumn(connection, "garage_vehicles", "garage_slot", "INTEGER");
                EnsureColumn(connection, "garage_vehicles", "garage_capacity", "INTEGER");

                bool hasGarageVehicleId = false;
                using (var command = new SQLiteCommand("PRAGMA table_info(records)", connection))
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        if (string.Equals(reader["name"].ToString(), "garageVehicleId", StringComparison.OrdinalIgnoreCase))
                        {
                            hasGarageVehicleId = true;
                            break;
                        }
                    }
                }
                if (!hasGarageVehicleId)
                {
                    using (var command = new SQLiteCommand("ALTER TABLE records ADD COLUMN garageVehicleId INTEGER", connection))
                        command.ExecuteNonQuery();
                }
            }
        }

        internal static bool EnsureColumn(SQLiteConnection connection, string table, string column, string declaration,
            SQLiteTransaction transaction = null)
        {
            bool exists = false;
            using (var command = new SQLiteCommand("PRAGMA table_info(" + table + ")", connection, transaction))
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    if (string.Equals(Convert.ToString(reader["name"], CultureInfo.InvariantCulture), column,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        exists = true;
                        break;
                    }
                }
            }
            if (!exists)
            {
                using (var command = new SQLiteCommand(
                    "ALTER TABLE " + table + " ADD COLUMN " + column + " " + declaration, connection, transaction))
                    command.ExecuteNonQuery();
            }
            return !exists;
        }

        public static string DefaultSaveRoot => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Eden Games", "Test Drive Unlimited 2", "savegame");

        public static List<SaveProfileInfo> RefreshDiscoveredProfiles(string connectionString)
        {
            EnsureSchema(connectionString);
            var discovered = new List<SaveProfileInfo>();
            if (Directory.Exists(DefaultSaveRoot))
            {
                foreach (string directory in Directory.GetDirectories(DefaultSaveRoot).OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
                {
                    string dataPath = Path.Combine(directory, "PLAYERSAVE", "DATA");
                    if (!File.Exists(dataPath))
                        continue;
                    discovered.Add(new SaveProfileInfo
                    {
                        ProfileKey = Path.GetFileName(directory),
                        ProfileName = Path.GetFileName(directory),
                        SourceAvailable = true,
                        DataPath = dataPath,
                    });
                }
            }

            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();
                using (var transaction = connection.BeginTransaction())
                {
                    using (var command = new SQLiteCommand("UPDATE save_profiles SET source_available=0", connection, transaction))
                        command.ExecuteNonQuery();

                    foreach (SaveProfileInfo profile in discovered)
                    {
                        const string sql = @"
                            INSERT OR IGNORE INTO save_profiles
                                (profile_key, profile_name, is_active, source_sha256, source_size,
                                 captured_utc, source_available, last_seen_utc)
                            VALUES (@Key, @Name, 0, '', 0, '', 1, CURRENT_TIMESTAMP);
                            UPDATE save_profiles
                            SET profile_name=@Name, source_available=1, last_seen_utc=CURRENT_TIMESTAMP
                            WHERE profile_key=@Key;";
                        using (var command = new SQLiteCommand(sql, connection, transaction))
                        {
                            command.Parameters.AddWithValue("@Key", profile.ProfileKey);
                            command.Parameters.AddWithValue("@Name", profile.ProfileName);
                            command.ExecuteNonQuery();
                        }
                    }

                    object active;
                    using (var command = new SQLiteCommand("SELECT profile_key FROM save_profiles WHERE is_active=1 LIMIT 1", connection, transaction))
                        active = command.ExecuteScalar();
                    if ((active == null || active == DBNull.Value) && discovered.Count > 0)
                    {
                        using (var command = new SQLiteCommand(
                            "UPDATE save_profiles SET is_active=CASE WHEN profile_key=@Key THEN 1 ELSE 0 END", connection, transaction))
                        {
                            command.Parameters.AddWithValue("@Key", discovered[0].ProfileKey);
                            command.ExecuteNonQuery();
                        }
                    }
                    transaction.Commit();
                }
            }
            return GetProfiles(connectionString);
        }

        public static List<SaveProfileInfo> GetProfiles(string connectionString)
        {
            EnsureSchema(connectionString);
            var profiles = new List<SaveProfileInfo>();
            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();
                using (var command = new SQLiteCommand(@"
                    SELECT profile_key, profile_name, is_active, source_available
                    FROM save_profiles
                    ORDER BY is_active DESC, profile_name COLLATE NOCASE", connection))
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string key = Convert.ToString(reader["profile_key"], CultureInfo.InvariantCulture);
                        bool available = Convert.ToInt32(reader["source_available"], CultureInfo.InvariantCulture) == 1;
                        string path = Path.Combine(DefaultSaveRoot, key, "PLAYERSAVE", "DATA");
                        profiles.Add(new SaveProfileInfo
                        {
                            ProfileKey = key,
                            ProfileName = Convert.ToString(reader["profile_name"], CultureInfo.InvariantCulture),
                            IsActive = Convert.ToInt32(reader["is_active"], CultureInfo.InvariantCulture) == 1,
                            SourceAvailable = available && File.Exists(path),
                            DataPath = path,
                        });
                    }
                }
            }
            return profiles;
        }

        public static void ActivateProfile(string connectionString, string profileKey)
        {
            EnsureSchema(connectionString);
            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();
                using (var command = new SQLiteCommand(
                    "UPDATE save_profiles SET is_active=CASE WHEN profile_key=@Key THEN 1 ELSE 0 END", connection))
                {
                    command.Parameters.AddWithValue("@Key", profileKey);
                    if (command.ExecuteNonQuery() == 0)
                        throw new InvalidOperationException("The selected Track Records profile no longer exists.");
                }
            }
        }

        public static GarageImportResult ImportLiveData(string connectionString, string dataPath, bool activateProfile = true,
            string requiredActiveProfile = null)
        {
            string selectedPath = Path.GetFullPath(dataPath);
            GarageBridgeSnapshot snapshot = ReadLiveSnapshot(selectedPath);
            ValidateSnapshot(snapshot, selectedPath);
            string initialHash = ComputeSha256(selectedPath);
            if (!string.Equals(initialHash, snapshot.source.sha256, StringComparison.OrdinalIgnoreCase))
                throw new IOException("DATA changed immediately after it was decoded.");

            EnsureSchema(connectionString);
            int knownModels = 0;
            int unknownModels = 0;
            var catalogueIds = new HashSet<int>();

            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();
                using (var transaction = connection.BeginTransaction())
                {
                    if (requiredActiveProfile != null)
                    {
                        using (var active = new SQLiteCommand(
                            "SELECT source_sha256 FROM save_profiles WHERE profile_key=@Key AND is_active=1", connection, transaction))
                        {
                            active.Parameters.AddWithValue("@Key", requiredActiveProfile);
                            object importedHash = active.ExecuteScalar();
                            if (importedHash == null || !string.Equals(snapshot.profile_key, requiredActiveProfile, StringComparison.Ordinal))
                                return null;
                            if (string.Equals(importedHash.ToString(), initialHash, StringComparison.OrdinalIgnoreCase))
                            {
                                ApplyOwnedHouses(connection, transaction, snapshot);
                                SaveObjectiveSync.Apply(connection, transaction, snapshot);
                                VerifyUnchanged(selectedPath, initialHash);
                                transaction.Commit();
                                return new GarageImportResult
                                {
                                    ProfileKey = snapshot.profile_key, Profile = snapshot.profile,
                                    SourceModifiedLocal = File.GetLastWriteTime(selectedPath), GarageChanged = false
                                };
                            }
                        }
                    }

                    if (activateProfile)
                    {
                        using (var command = new SQLiteCommand("UPDATE save_profiles SET is_active = 0", connection, transaction))
                            command.ExecuteNonQuery();
                    }

                    const string updateProfile = @"
                        UPDATE save_profiles
                        SET profile_name=@Name,
                            is_active=CASE WHEN @Activate=1 THEN 1 ELSE is_active END,
                            source_sha256=@Hash, source_size=@Size,
                            captured_utc=@Captured, imported_utc=CURRENT_TIMESTAMP,
                            source_available=1, last_seen_utc=CURRENT_TIMESTAMP
                        WHERE profile_key=@Key;";
                    int profileRows;
                    using (var command = new SQLiteCommand(updateProfile, connection, transaction))
                    {
                        AddProfileParameters(command, snapshot, activateProfile);
                        profileRows = command.ExecuteNonQuery();
                    }
                    if (profileRows == 0)
                    {
                        const string insertProfile = @"
                            INSERT INTO save_profiles
                                (profile_key, profile_name, is_active, source_sha256, source_size, captured_utc,
                                 source_available, last_seen_utc)
                            VALUES (@Key, @Name, @Activate, @Hash, @Size, @Captured, 1, CURRENT_TIMESTAMP);";
                        using (var command = new SQLiteCommand(insertProfile, connection, transaction))
                        {
                            AddProfileParameters(command, snapshot, activateProfile);
                            command.ExecuteNonQuery();
                        }
                    }

                    using (var command = new SQLiteCommand(
                        "UPDATE garage_vehicles SET is_present=0 WHERE profile_key=@ProfileKey", connection, transaction))
                    {
                        command.Parameters.AddWithValue("@ProfileKey", snapshot.profile_key);
                        command.ExecuteNonQuery();
                    }

                    foreach (GarageBridgeVehicle vehicle in snapshot.vehicles)
                    {
                        bool known;
                        using (var command = new SQLiteCommand("SELECT COUNT(*) FROM vehicles WHERE id=@VehicleId", connection, transaction))
                        {
                            command.Parameters.AddWithValue("@VehicleId", vehicle.vehicle_id);
                            known = Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture) == 1;
                        }
                        if (known)
                        {
                            knownModels++;
                            catalogueIds.Add(vehicle.vehicle_id);
                        }
                        else
                        {
                            unknownModels++;
                        }

                        const string updateVehicle = @"
                            UPDATE garage_vehicles
                            SET vehicle_id=@VehicleId, garage_index=@GarageIndex,
                                house_hash=@HouseHash, garage_slot=@GarageSlot,
                                garage_capacity=@GarageCapacity,
                                odometer_metric=@Metric, odometer_imperial=@Imperial,
                                is_present=1, imported_utc=CURRENT_TIMESTAMP
                            WHERE profile_key=@ProfileKey AND instance_id=@InstanceId;";
                        int updated;
                        using (var command = new SQLiteCommand(updateVehicle, connection, transaction))
                        {
                            AddVehicleParameters(command, snapshot.profile_key, vehicle);
                            updated = command.ExecuteNonQuery();
                        }
                        if (updated == 0)
                        {
                            const string insertVehicle = @"
                                INSERT INTO garage_vehicles
                                    (profile_key, instance_id, vehicle_id, garage_index,
                                     house_hash, garage_slot, garage_capacity,
                                     odometer_metric, odometer_imperial, is_present)
                                VALUES (@ProfileKey, @InstanceId, @VehicleId, @GarageIndex,
                                        @HouseHash, @GarageSlot, @GarageCapacity,
                                        @Metric, @Imperial, 1);";
                            using (var command = new SQLiteCommand(insertVehicle, connection, transaction))
                            {
                                AddVehicleParameters(command, snapshot.profile_key, vehicle);
                                command.ExecuteNonQuery();
                            }
                        }
                    }

                    ApplyOwnedHouses(connection, transaction, snapshot);
                    LearnAndApplyHouseMappings(connection, transaction, snapshot);

                    foreach (int vehicleId in catalogueIds)
                    {
                        const string updateCatalogue = @"
                            UPDATE vehicles
                            SET _is_owned='true',
                                _odometer_metric=(SELECT SUM(odometer_metric) FROM garage_vehicles
                                                  WHERE profile_key=@ProfileKey AND vehicle_id=@VehicleId AND is_present=1),
                                _odometer_imperial=(SELECT SUM(odometer_imperial) FROM garage_vehicles
                                                    WHERE profile_key=@ProfileKey AND vehicle_id=@VehicleId AND is_present=1)
                            WHERE id=@VehicleId;";
                        using (var command = new SQLiteCommand(updateCatalogue, connection, transaction))
                        {
                            command.Parameters.AddWithValue("@ProfileKey", snapshot.profile_key);
                            command.Parameters.AddWithValue("@VehicleId", vehicleId);
                            command.ExecuteNonQuery();
                        }
                    }

                    SaveObjectiveSync.Apply(connection, transaction, snapshot);
                    VerifyUnchanged(selectedPath, initialHash);
                    transaction.Commit();
                }
            }

            return new GarageImportResult
            {
                ProfileKey = snapshot.profile_key,
                Profile = snapshot.profile,
                ImportedVehicles = snapshot.vehicles.Count,
                CatalogueMatches = knownModels,
                UnknownModels = unknownModels,
                SourceHash = snapshot.source.sha256,
                SourceModifiedLocal = File.GetLastWriteTime(selectedPath),
            };
        }

        private static void VerifyUnchanged(string path, string expectedHash)
        {
            if (!string.Equals(ComputeSha256(path), expectedHash, StringComparison.OrdinalIgnoreCase))
                throw new IOException("DATA changed during the import. The database import was rolled back.");
        }

        public static bool NeedsSync(string connectionString, string dataPath)
        {
            if (string.IsNullOrWhiteSpace(dataPath) || !File.Exists(dataPath))
                return false;
            string currentHash = ComputeSha256(Path.GetFullPath(dataPath));
            DirectoryInfo profileDirectory = Directory.GetParent(Path.GetDirectoryName(Path.GetFullPath(dataPath)));
            string profileKey = profileDirectory?.Name ?? string.Empty;
            EnsureSchema(connectionString);
            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();
                using (var command = new SQLiteCommand(
                    @"SELECT source_sha256 FROM save_profiles
                      WHERE profile_key=@Key AND objective_source_sha256=source_sha256 COLLATE NOCASE
                        AND objective_mapping_version=@Version LIMIT 1", connection))
                {
                    command.Parameters.AddWithValue("@Key", profileKey);
                    command.Parameters.AddWithValue("@Version", SaveObjectiveSync.MappingVersion);
                    object importedHash = command.ExecuteScalar();
                    return importedHash == null || importedHash == DBNull.Value ||
                           !string.Equals(Convert.ToString(importedHash, CultureInfo.InvariantCulture), currentHash,
                               StringComparison.OrdinalIgnoreCase);
                }
            }
        }

        private static GarageBridgeSnapshot ReadLiveSnapshot(string dataPath)
        {
            string editorRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "TDU2 SaveGame Editor");
            string python = Path.Combine(editorRoot, ".venv", "Scripts", "python.exe");
            string bridge = Path.Combine(editorRoot, "tools", "track_records_bridge.py");
            if (!File.Exists(python) || !File.Exists(bridge))
                throw new FileNotFoundException("TDU2 SaveGame Editor and its .venv are required for direct live-save import.");

            var startInfo = new ProcessStartInfo
            {
                FileName = python,
                Arguments = Quote(bridge) + " " + Quote(dataPath),
                WorkingDirectory = editorRoot,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            using (var process = Process.Start(startInfo))
            {
                var outputTask = process.StandardOutput.ReadToEndAsync();
                var errorTask = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(30000))
                {
                    process.Kill();
                    throw new TimeoutException("The live save decoder timed out.");
                }
                string output = outputTask.GetAwaiter().GetResult();
                string error = errorTask.GetAwaiter().GetResult();
                if (process.ExitCode != 0)
                    throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? "The live save could not be decoded." : error.Trim());
                var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 512 };
                return serializer.Deserialize<GarageBridgeSnapshot>(output);
            }
        }

        private static void ValidateSnapshot(GarageBridgeSnapshot snapshot, string selectedPath)
        {
            if (snapshot == null || snapshot.format_version != 1 || snapshot.source == null || snapshot.vehicles == null)
                throw new InvalidDataException("The SaveGame Editor returned an unsupported import snapshot.");
            if (string.IsNullOrWhiteSpace(snapshot.profile_key) || string.IsNullOrWhiteSpace(snapshot.profile))
                throw new InvalidDataException("The live save has no usable profile identity.");
            if (!string.Equals(Path.GetFullPath(snapshot.source.path), selectedPath, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The decoded save path does not match the selected DATA file.");
            if (snapshot.source.size != new FileInfo(selectedPath).Length || snapshot.source.sha256?.Length != 64)
                throw new InvalidDataException("The live save source signature is invalid.");
            var ids = new HashSet<long>();
            foreach (GarageBridgeVehicle vehicle in snapshot.vehicles)
            {
                if (vehicle.instance_id < 0 || vehicle.vehicle_id <= 0 || vehicle.garage_index < 0 ||
                    vehicle.house_hash <= 0 || vehicle.garage_capacity <= 0 ||
                    vehicle.garage_slot <= 0 || vehicle.garage_slot > vehicle.garage_capacity ||
                    double.IsNaN(vehicle.mileage_km) || double.IsInfinity(vehicle.mileage_km) || vehicle.mileage_km < 0 ||
                    !ids.Add(vehicle.instance_id))
                    throw new InvalidDataException("The live save contains an invalid or duplicate garage instance.");
            }
        }

        private static void LearnAndApplyHouseMappings(SQLiteConnection connection, SQLiteTransaction transaction,
            GarageBridgeSnapshot snapshot)
        {
            foreach (IGrouping<long, GarageBridgeVehicle> group in snapshot.vehicles.GroupBy(vehicle => vehicle.house_hash))
            {
                long houseHash = group.Key;
                int? houseId = null;
                using (var command = new SQLiteCommand(
                    "SELECT house_id FROM garage_house_mappings WHERE house_hash=@Hash", connection, transaction))
                {
                    command.Parameters.AddWithValue("@Hash", houseHash);
                    object mapped = command.ExecuteScalar();
                    if (mapped != null && mapped != DBNull.Value)
                        houseId = Convert.ToInt32(mapped, CultureInfo.InvariantCulture);
                }

                if (!houseId.HasValue)
                {
                    var votes = new Dictionary<int, int>();
                    foreach (GarageBridgeVehicle vehicle in group)
                    {
                        const string candidateSql = @"
                            SELECT DISTINCT h.id
                            FROM vehicles v
                            JOIN houses h ON h.Slots=@Capacity AND (
                                (h.Island='Ibiza' AND h.Name=v._house_name_in_ibiza COLLATE NOCASE) OR
                                (h.Island='Hawaii' AND h.Name=v._house_name_in_hawaii COLLATE NOCASE))
                            WHERE v.id=@VehicleId;";
                        using (var command = new SQLiteCommand(candidateSql, connection, transaction))
                        {
                            command.Parameters.AddWithValue("@Capacity", vehicle.garage_capacity);
                            command.Parameters.AddWithValue("@VehicleId", vehicle.vehicle_id);
                            using (var reader = command.ExecuteReader())
                            {
                                while (reader.Read())
                                {
                                    int candidate = Convert.ToInt32(reader[0], CultureInfo.InvariantCulture);
                                    votes[candidate] = votes.TryGetValue(candidate, out int count) ? count + 1 : 1;
                                }
                            }
                        }
                    }

                    var ranked = votes.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key).ToList();
                    if (ranked.Count > 0 && (ranked.Count == 1 || ranked[0].Value > ranked[1].Value))
                    {
                        houseId = ranked[0].Key;
                        using (var command = new SQLiteCommand(@"
                            INSERT OR IGNORE INTO garage_house_mappings
                                (house_hash, house_id, evidence_count)
                            VALUES (@Hash, @HouseId, @Evidence);", connection, transaction))
                        {
                            command.Parameters.AddWithValue("@Hash", houseHash);
                            command.Parameters.AddWithValue("@HouseId", houseId.Value);
                            command.Parameters.AddWithValue("@Evidence", ranked[0].Value);
                            command.ExecuteNonQuery();
                        }
                    }
                }

                using (var command = new SQLiteCommand(@"
                    UPDATE garage_vehicles
                    SET house_id=@HouseId
                    WHERE house_hash=@Hash AND is_present=1;", connection, transaction))
                {
                    command.Parameters.AddWithValue("@HouseId", houseId.HasValue ? (object)houseId.Value : DBNull.Value);
                    command.Parameters.AddWithValue("@Hash", houseHash);
                    command.ExecuteNonQuery();
                }
            }
        }

        private static void ApplyOwnedHouses(SQLiteConnection connection, SQLiteTransaction transaction,
            GarageBridgeSnapshot snapshot)
        {
            if (snapshot.house_hashes == null || snapshot.house_reference == null)
                return;

            using (var command = new SQLiteCommand(@"
                INSERT INTO garage_house_mappings (house_hash, house_id, evidence_count)
                SELECT @Hash, id, 0 FROM houses WHERE Name=@Name AND Island=@Island
                ON CONFLICT(house_hash) DO UPDATE SET house_id=excluded.house_id, evidence_count=0;",
                connection, transaction))
            {
                command.Parameters.AddWithValue("@Hash", 0L);
                command.Parameters.AddWithValue("@Name", "");
                command.Parameters.AddWithValue("@Island", "");
                foreach (SaveHouseReference house in snapshot.house_reference)
                {
                    command.Parameters["@Hash"].Value = house.hash;
                    command.Parameters["@Island"].Value = house.island;
                    command.Parameters["@Name"].Value = house.name;
                    command.ExecuteNonQuery();
                }
            }

            using (var command = new SQLiteCommand("DELETE FROM save_houses WHERE profile_key=@Profile", connection, transaction))
            {
                command.Parameters.AddWithValue("@Profile", snapshot.profile_key);
                command.ExecuteNonQuery();
                command.CommandText = "INSERT INTO save_houses (profile_key, house_hash) VALUES (@Profile, @Hash)";
                command.Parameters.AddWithValue("@Hash", 0L);
                foreach (long hash in snapshot.house_hashes)
                {
                    command.Parameters["@Hash"].Value = hash;
                    command.ExecuteNonQuery();
                }
            }

            using (var command = new SQLiteCommand(@"
                UPDATE garage_vehicles SET house_id=(SELECT m.house_id FROM garage_house_mappings m
                                                    WHERE m.house_hash=garage_vehicles.house_hash)
                WHERE is_present=1 AND EXISTS (SELECT 1 FROM garage_house_mappings m
                                               WHERE m.house_hash=garage_vehicles.house_hash);", connection, transaction))
                command.ExecuteNonQuery();
        }

        private static void AddProfileParameters(SQLiteCommand command, GarageBridgeSnapshot snapshot, bool activateProfile)
        {
            command.Parameters.AddWithValue("@Key", snapshot.profile_key);
            command.Parameters.AddWithValue("@Name", snapshot.profile);
            command.Parameters.AddWithValue("@Hash", snapshot.source.sha256);
            command.Parameters.AddWithValue("@Size", snapshot.source.size);
            command.Parameters.AddWithValue("@Captured", snapshot.captured_utc ?? string.Empty);
            command.Parameters.AddWithValue("@Activate", activateProfile ? 1 : 0);
        }

        private static void AddVehicleParameters(SQLiteCommand command, string profileKey, GarageBridgeVehicle vehicle)
        {
            command.Parameters.AddWithValue("@ProfileKey", profileKey);
            command.Parameters.AddWithValue("@InstanceId", vehicle.instance_id);
            command.Parameters.AddWithValue("@VehicleId", vehicle.vehicle_id);
            command.Parameters.AddWithValue("@GarageIndex", vehicle.garage_index);
            command.Parameters.AddWithValue("@HouseHash", vehicle.house_hash);
            command.Parameters.AddWithValue("@GarageSlot", vehicle.garage_slot);
            command.Parameters.AddWithValue("@GarageCapacity", vehicle.garage_capacity);
            command.Parameters.AddWithValue("@Metric", Math.Round(vehicle.mileage_km, 2));
            command.Parameters.AddWithValue("@Imperial", Math.Round(vehicle.mileage_km * MilesPerKilometre, 2));
        }

        private static string ComputeSha256(string path)
        {
            using (var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var sha = SHA256.Create())
                return string.Concat(sha.ComputeHash(stream).Select(value => value.ToString("x2", CultureInfo.InvariantCulture)));
        }

        private static string Quote(string value)
        {
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }
    }
}

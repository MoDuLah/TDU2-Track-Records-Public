using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;

namespace TDU2_Track_Records
{
    internal static class EventCatalog
    {
        internal static bool IsCrcChallenge(string raceType) =>
            raceType.EndsWith(" PC", StringComparison.Ordinal);

        internal static bool IsTimeAttack(string raceType) =>
            raceType == "Time Attack SP" || raceType == "Time Attack PC";

        internal static bool SupportsOrientation(string raceType, int laps) =>
            laps > 1 && (raceType == "Race SP" || raceType == "Race MP" ||
                         raceType == "Race RMP" || raceType == "Eliminator SP");

        internal static string FormatRaceTime(decimal seconds)
        {
            long hundredths = (long)Math.Round(seconds * 100, MidpointRounding.AwayFromZero);
            return FormattableString.Invariant($"{hundredths / 6000:D2}:{hundredths / 100 % 60:D2}.{hundredths % 100:D2}");
        }

        internal static bool IsSpeedTrap(string raceType) =>
            raceType == "Speed Trap SP" || raceType == "Speed Trap MP" || raceType == "Speed Trap PC";

        internal static bool IsScoreBasedRaceType(string raceType) =>
            !IsSpeedTrap(raceType) && (raceType.IndexOf("Speed", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                      raceType.IndexOf("Trap", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                      raceType.IndexOf("Keep", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                      raceType.IndexOf("Follow", StringComparison.OrdinalIgnoreCase) >= 0);

        internal static string AverageSpeedColumn => MeasurementUnits.IsImperial
            ? "Average_Speed_Imperial" : "Average_Speed_Metric";

        internal static string FormatSpeedTrapResult(double value, bool legacy = false) =>
            value.ToString("F2", CultureInfo.CurrentCulture) +
            (legacy ? " (unit unrecorded)" : " " + Properties.Settings.Default.speed);

        internal static string RaceTypeLabel(string raceType, string description)
        {
            switch (raceType)
            {
                case "Time Attack PC": return "CRC Time Attack";
                case "Speed PC": return "CRC Speed";
                case "Speed Trap PC": return "CRC Speed Trap";
                default: return description;
            }
        }

        private const string SameRoute = @"
            candidate.Length IS source.Length AND candidate.Laps IS source.Laps
            AND candidate.Orientation IS source.Orientation";

        private const string SameRestrictions = @"
            TRIM(COALESCE(candidate.RestrictedClass, '')) =
                TRIM(COALESCE(source.RestrictedClass, '')) COLLATE NOCASE
            AND TRIM(COALESCE(candidate.RestrictedCar, '')) =
                TRIM(COALESCE(source.RestrictedCar, ''))";

        private const string SameCourse = SameRoute + @"
            AND candidate.MinSpeed IS source.MinSpeed AND candidate.Checkpoints IS source.Checkpoints
            AND " + SameRestrictions;

        private const string RankedRouteMatch = @"
            FROM tracks candidate
            WHERE candidate.RaceType = 'Race RMP'
              AND candidate.Name = source.Name COLLATE NOCASE
              AND COALESCE(NULLIF(TRIM(candidate.Island), ''), 'Hawaii') =
                  COALESCE(NULLIF(TRIM(source.Island), ''), 'Hawaii')
              AND candidate.Length IS source.Length AND candidate.Laps IS source.Laps
              AND (COALESCE(source.Laps, 0) <= 1 OR candidate.Orientation IS source.Orientation)";

        // Filter the actual race mode, including its own restrictions, before either dropdown uses it.
        internal const string EligibleTracks = @"
            WITH candidates AS (
                SELECT t.*, t.id AS EventId, t.RaceType AS RaceMode,
                       CASE WHEN t.RaceType <> 'Race MP' THEN t.RestrictedClass END AS ModeClass,
                       CASE WHEN t.RaceType <> 'Race MP' THEN t.RestrictedCar END AS ModeCar
                FROM tracks t
                UNION ALL
                SELECT source.*, -source.id, 'Race RMP', source.RestrictedClass, source.RestrictedCar
                FROM tracks source
                WHERE source.RaceType = 'Race MP'
                  AND NOT EXISTS (SELECT 1 " + RankedRouteMatch + @")
            ),
            eligible AS (
                SELECT t.*, v._vehicle_name AS RestrictedVehicleName
                FROM candidates t
                LEFT JOIN vehicles v ON v.id = CAST(t.ModeCar AS INTEGER)
                WHERE (TRIM(t.Island) = @Island
                       OR (@Island = 'Hawaii' AND TRIM(COALESCE(t.Island, '')) = ''))
                  AND (@SelectedClass = '' OR (
                      (UPPER(TRIM(COALESCE(t.ModeClass, ''))) IN ('', 'ALL')
                       OR TRIM(t.ModeClass) = @SelectedClass COLLATE NOCASE
                       OR (LENGTH(TRIM(t.ModeClass)) = 1
                           AND @SelectedClass LIKE TRIM(t.ModeClass) || '%'))
                      AND (TRIM(COALESCE(t.ModeCar, '')) = ''
                           OR v._vehiclecategory_name = @SelectedClass COLLATE NOCASE)
                  ))
            ) ";

        internal const string SelectEvents = EligibleTracks + @"
            SELECT EventId AS id, Name, Length, Laps, Orientation, MinSpeed, Checkpoints,
                   ModeClass AS RestrictedClass, ModeCar AS RestrictedCar, RestrictedVehicleName,
                   CASE WHEN @RaceType = 'Race RMP' AND RestrictedVehicleName IS NOT NULL
                        THEN Name || ' (' || RestrictedVehicleName || ')'
                        ELSE Name END AS DisplayName
            FROM eligible
            WHERE RaceMode = @RaceType
            ORDER BY DisplayName COLLATE NOCASE, id;";

        internal static int GetOrCreateRankedTrack(SQLiteConnection connection, SQLiteTransaction transaction, int sourceId)
        {
            const string sql = @"
                INSERT INTO tracks (Island, Name, Length, RaceType, Orientation, RestrictedClass,
                                    RestrictedCar, Laps, MinSpeed, Checkpoints, Runs)
                SELECT source.Island, source.Name, source.Length, 'Race RMP', source.Orientation,
                       source.RestrictedClass, source.RestrictedCar, source.Laps, source.MinSpeed, source.Checkpoints, 0
                FROM tracks source
                WHERE source.id = @Id AND source.RaceType = 'Race MP'
                  AND NOT EXISTS (SELECT 1 " + RankedRouteMatch + @");

                SELECT (SELECT candidate.id " + RankedRouteMatch + " AND " + SameRestrictions + @"
                        ORDER BY candidate.id LIMIT 1)
                FROM tracks source
                WHERE source.id = @Id AND source.RaceType = 'Race MP';";

            using (var command = new SQLiteCommand(sql, connection, transaction))
            {
                command.Parameters.AddWithValue("@Id", sourceId);
                object result = command.ExecuteScalar();
                if (result == null || result == DBNull.Value)
                    throw new InvalidOperationException("The ranked event changed. Reselect it before saving.");
                return Convert.ToInt32(result, CultureInfo.InvariantCulture);
            }
        }

        internal static bool DeleteTrack(string connectionString, int trackId, int confirmedRunCount)
        {
            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();
                using (var transaction = connection.BeginTransaction())
                using (var command = new SQLiteCommand(@"
                    SELECT (SELECT COUNT(*) FROM records WHERE trackId = @TrackId)
                    FROM tracks WHERE id = @TrackId;", connection, transaction))
                {
                    command.Parameters.AddWithValue("@TrackId", trackId);
                    object count = command.ExecuteScalar();
                    if (count == null || Convert.ToInt32(count) != confirmedRunCount)
                        return false;

                    command.CommandText = "DELETE FROM records WHERE trackId = @TrackId;";
                    command.ExecuteNonQuery();
                    command.CommandText = "DELETE FROM tracks WHERE id = @TrackId;";
                    if (command.ExecuteNonQuery() != 1)
                        return false;
                    transaction.Commit();
                    return true;
                }
            }
        }

        internal static void RepairLegacyData(string connectionString)
        {
            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();
                using (var transaction = connection.BeginTransaction())
                {
                    RepairIbizaCup(connection, transaction);
                    RebuildTimedBests(connection, transaction);
                    transaction.Commit();
                }
            }
        }

        private static void RepairIbizaCup(SQLiteConnection connection, SQLiteTransaction transaction)
        {
            // Apply the catalog correction once so later edits to these events survive a restart.
            using (var command = new SQLiteCommand(@"
                CREATE TABLE IF NOT EXISTS event_catalog_migrations (name TEXT PRIMARY KEY);
                INSERT OR IGNORE INTO event_catalog_migrations (name) VALUES ('ibiza-cup-island');",
                connection, transaction))
            {
                if (command.ExecuteNonQuery() == 0)
                    return;
            }

            // The original catalog placed these Ibiza Cup rounds on Hawaii.
            var rounds = new[]
            {
                (155, "Ibiza tour", "Ibiza tour", "Time Attack SP", "A6"),
                (156, "Sant Antoni fields", "Sant Antoni fields", "Speed SP", "A6"),
                (157, "Sant Miquel course", "Sant Miguel course", "Time Attack SP", "A7"),
                (158, "Sant Antoni loop", "Sant Antoni loop", "Eliminator SP", "A7"),
                (159, "Benirras track", "Benirras track", "Race SP", "B4"),
                (160, "Cala Salada", "Cala Salada", "Time Attack SP", "B4"),
                (161, "Sant Rafael", "Sant Rafael", "Speed Trap SP", "C4"),
                (162, "Cala Corral", "Cala Corral", "Race SP", "C4")
            };

            foreach (var (id, name, alternateName, raceType, carClass) in rounds)
            {
                const string findDuplicate = @"
                    SELECT candidate.id
                    FROM tracks source
                    LEFT JOIN tracks candidate ON candidate.Island = 'Ibiza'
                        AND candidate.Name COLLATE NOCASE IN (@Name, @AlternateName)
                        AND candidate.RaceType = source.RaceType AND " + SameCourse + @"
                    WHERE source.id = @Id AND source.Island = 'Hawaii'
                      AND source.Name = @Name COLLATE NOCASE
                      AND source.RaceType = @Type AND source.RestrictedClass = @Class
                    ORDER BY EXISTS (SELECT 1 FROM records WHERE trackId = candidate.id) DESC,
                             candidate.id LIMIT 1;";

                object duplicate;
                using (var command = new SQLiteCommand(findDuplicate, connection, transaction))
                {
                    command.Parameters.AddWithValue("@Id", id);
                    command.Parameters.AddWithValue("@Name", name);
                    command.Parameters.AddWithValue("@AlternateName", alternateName);
                    command.Parameters.AddWithValue("@Type", raceType);
                    command.Parameters.AddWithValue("@Class", carClass);
                    duplicate = command.ExecuteScalar();
                }
                if (duplicate == null)
                    continue;

                int retainedId = id;
                if (duplicate != DBNull.Value)
                {
                    int ibizaId = Convert.ToInt32(duplicate, CultureInfo.InvariantCulture);
                    const string merge = @"
                        UPDATE tracks
                        SET Island = 'Ibiza',
                            Runs = COALESCE(Runs, 0) +
                                (SELECT COALESCE(Runs, 0) FROM tracks WHERE id = @Discarded)
                        WHERE id = @Retained;
                        UPDATE records SET trackId = @Retained WHERE trackId = @Discarded;
                        DELETE FROM tracks WHERE id = @Discarded;";
                    using (var history = new SQLiteCommand(
                        "SELECT EXISTS (SELECT 1 FROM records WHERE trackId = @Id)", connection, transaction))
                    {
                        history.Parameters.AddWithValue("@Id", ibizaId);
                        if (Convert.ToInt32(history.ExecuteScalar(), CultureInfo.InvariantCulture) != 0)
                            retainedId = ibizaId;
                    }
                    using (var command = new SQLiteCommand(merge, connection, transaction))
                    {
                        command.Parameters.AddWithValue("@Retained", retainedId);
                        command.Parameters.AddWithValue("@Discarded", retainedId == id ? ibizaId : id);
                        command.ExecuteNonQuery();
                    }
                }
                else
                {
                    using (var command = new SQLiteCommand(
                        "UPDATE tracks SET Island = 'Ibiza' WHERE id = @Id", connection, transaction))
                    {
                        command.Parameters.AddWithValue("@Id", id);
                        command.ExecuteNonQuery();
                    }
                }

            }
        }

        private const string TimedBestOrder = @"
            CAST(r.Total_Time AS INTEGER),
            CAST(SUBSTR(r.Total_Time, INSTR(r.Total_Time, ':') + 1) AS REAL),
            r.id";

        internal static (string Overall, List<string> ByClass) ReadEventBests(
            string connectionString, int trackId, string raceType)
        {
            bool scored = IsScoreBasedRaceType(raceType);
            bool speedTrap = IsSpeedTrap(raceType);
            string result = scored ? "r.score" : "TRIM(r.Total_Time)";
            string filter = scored ? "r.score IS NOT NULL" : LapDataAccess.TimedRecordFilter;
            string order = scored ? "r.score DESC, r.id" : TimedBestOrder;
            if (speedTrap)
            {
                result = $"COALESCE(r.score, r.{AverageSpeedColumn})";
                filter = result + " IS NOT NULL";
                order = "r.score IS NOT NULL, r.Average_Speed_Metric DESC, r.score DESC, r.id";
            }

            string sql = $@"
                SELECT UPPER(TRIM(r.carClass)), r.carName, {result}, r.score
                FROM records r
                WHERE r.trackId = @TrackId AND {filter}
                ORDER BY {order};";

            string overall = null;
            var byClass = new SortedDictionary<string, string>();
            using (var connection = new SQLiteConnection(connectionString))
            using (var command = new SQLiteCommand(sql, connection))
            {
                connection.Open();
                command.Parameters.AddWithValue("@TrackId", trackId);
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string carClass = reader[0].ToString();
                        string value = reader[2].ToString();
                        if (speedTrap)
                            value = FormatSpeedTrapResult(Convert.ToDouble(reader[2]), legacy: !reader.IsDBNull(3));
                        else if (scored)
                            value = Convert.ToDouble(reader[2]).ToString("0.##", CultureInfo.CurrentCulture) + " pts";
                        string entry = $"{reader[1]}  ·  {value}";
                        if (overall == null)
                            overall = entry;
                        if (carClass.Length != 0 && !byClass.ContainsKey(carClass))
                            byClass.Add(carClass, $"{carClass}  ·  {entry}");
                    }
                }
            }
            return (overall, new List<string>(byClass.Values));
        }

        private static void RebuildTimedBests(SQLiteConnection connection, SQLiteTransaction transaction)
        {
            const string sql = @"
                SELECT r.trackId, UPPER(TRIM(r.carClass)), r.carName || ' - ' || TRIM(r.Total_Time)
                FROM records r
                JOIN tracks t ON t.id = r.trackId
                WHERE " + LapDataAccess.TimedRecordFilter + @"
                ORDER BY r.trackId, " + TimedBestOrder;
            var bests = new Dictionary<(int TrackId, string Column), string>();
            using (var command = new SQLiteCommand(sql, connection, transaction))
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    int trackId = reader.GetInt32(0);
                    string record = reader[2].ToString();
                    var overall = (trackId, "Track_Lap_Record");
                    if (!bests.ContainsKey(overall))
                        bests.Add(overall, record);

                    string column = GetClassRecordColumn(reader[1].ToString());
                    var perClass = (trackId, column);
                    if (column != null && !bests.ContainsKey(perClass))
                        bests.Add(perClass, record);
                }
            }
            foreach (var best in bests)
                WriteTimedBest(connection, transaction, best.Key.TrackId, best.Key.Column, best.Value);
        }

        internal static (string Car, string Speed, int Weather)? ReadSpeedTrapBest(
            SQLiteConnection connection, int trackId, string carClass)
        {
            string result = $"COALESCE(r.score, r.{AverageSpeedColumn})";
            string sql = $@"
                SELECT r.carName, {result}, COALESCE(r.conditions, 0), r.score
                FROM records r
                WHERE r.trackId = @TrackId AND (@Class = '' OR TRIM(r.carClass) = @Class COLLATE NOCASE)
                  AND {result} IS NOT NULL
                ORDER BY r.score IS NOT NULL, r.Average_Speed_Metric DESC, r.score DESC, r.id
                LIMIT 1;";
            using (var command = new SQLiteCommand(sql, connection))
            {
                command.Parameters.AddWithValue("@TrackId", trackId);
                command.Parameters.AddWithValue("@Class", carClass);
                using (var reader = command.ExecuteReader())
                {
                    if (!reader.Read())
                        return null;
                    return (reader[0].ToString(), FormatSpeedTrapResult(Convert.ToDouble(reader[1]), !reader.IsDBNull(3)),
                            Convert.ToInt32(reader[2]));
                }
            }
        }

        internal static (string Car, string Time, int Weather, int Orientation)? ReadTimedBest(
            SQLiteConnection connection, int trackId, string carClass = "", SQLiteTransaction transaction = null)
        {
            const string sql = @"
                SELECT r.carName, TRIM(r.Total_Time), COALESCE(r.conditions, 0), COALESCE(r.orientation, 0)
                FROM records r
                WHERE r.trackId = @TrackId AND (@Class = '' OR TRIM(r.carClass) = @Class COLLATE NOCASE)
                  AND " + LapDataAccess.TimedRecordFilter + @"
                ORDER BY " + TimedBestOrder + " LIMIT 1;";
            using (var command = new SQLiteCommand(sql, connection, transaction))
            {
                command.Parameters.AddWithValue("@TrackId", trackId);
                command.Parameters.AddWithValue("@Class", carClass);
                using (var reader = command.ExecuteReader())
                {
                    if (!reader.Read())
                        return null;
                    return (reader[0].ToString(), reader[1].ToString(),
                            Convert.ToInt32(reader[2]), Convert.ToInt32(reader[3]));
                }
            }
        }

        internal static void RefreshTimedBests(SQLiteConnection connection, SQLiteTransaction transaction,
            int trackId, string carClass)
        {
            var best = ReadTimedBest(connection, trackId, "", transaction);
            if (best.HasValue)
                WriteTimedBest(connection, transaction, trackId, "Track_Lap_Record", $"{best.Value.Car} - {best.Value.Time}");

            carClass = carClass.Trim().ToUpperInvariant();
            string column = GetClassRecordColumn(carClass);
            if (column == null)
                return;

            best = ReadTimedBest(connection, trackId, carClass, transaction);
            if (best.HasValue)
                WriteTimedBest(connection, transaction, trackId, column, $"{best.Value.Car} - {best.Value.Time}");
        }

        private static string GetClassRecordColumn(string normalized)
        {
            switch (normalized)
            {
                case "A1": case "A2": case "A3": case "A4": case "A5": case "A6": case "A7":
                case "B1": case "B2": case "B3": case "B4":
                case "C1": case "C2": case "C3": case "C4":
                case "MA1": case "MA2":
                    return "Record_" + normalized;
                default:
                    return null;
            }
        }

        private static void WriteTimedBest(SQLiteConnection connection, SQLiteTransaction transaction,
            int trackId, string column, string record)
        {
            using (var command = new SQLiteCommand(
                $"UPDATE tracks SET {column} = @Record WHERE id = @TrackId AND {column} IS NOT @Record",
                connection, transaction))
            {
                command.Parameters.AddWithValue("@Record", record);
                command.Parameters.AddWithValue("@TrackId", trackId);
                command.ExecuteNonQuery();
            }
        }
    }
}

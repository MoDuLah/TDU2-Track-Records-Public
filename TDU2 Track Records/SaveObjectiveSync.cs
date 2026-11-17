using System;
using System.Data.SQLite;
using System.Text.RegularExpressions;

namespace TDU2_Track_Records
{
    internal sealed class SaveObjectiveEntry
    {
        public string name { get; set; }
        public bool? passed { get; set; }
        public bool verified { get; set; }
        public int? recorded_rounds { get; set; }
        public int? round_count { get; set; }
    }

    internal static class SaveObjectiveSync
    {
        internal const int MappingVersion = 4;

        public static void EnsureSchema(SQLiteConnection connection)
        {
            const string createTable = @"
                CREATE TABLE IF NOT EXISTS save_objectives (
                    profile_key TEXT NOT NULL,
                    objective_id INTEGER NOT NULL,
                    earned INTEGER NOT NULL,
                    detail TEXT,
                    PRIMARY KEY (profile_key, objective_id)
                );
                CREATE TABLE IF NOT EXISTS profile_objective_checks (
                    profile_key TEXT NOT NULL,
                    objective_id INTEGER NOT NULL,
                    completed INTEGER NOT NULL,
                    PRIMARY KEY (profile_key, objective_id)
                );";
            const string createView = @"
                CREATE VIEW IF NOT EXISTS displayed_objectives AS
                SELECT o.id, o.ParentCategory, o.Subcategory, o.Description, o.Island, o.MaximumPoints,
                       CASE WHEN live.earned=1 THEN 'true'
                            WHEN manual.completed IS NOT NULL THEN CASE manual.completed WHEN 1 THEN 'true' ELSE 'false' END
                            WHEN p.profile_key IS NULL OR (p.uses_legacy_objectives=1 AND live.objective_id IS NULL)
                            THEN o.Status ELSE 'false' END AS Status,
                       CASE WHEN live.earned=1 THEN o.MaximumPoints
                            WHEN manual.completed=1 THEN o.MaximumPoints
                            WHEN live.points IS NOT NULL THEN live.points
                            WHEN manual.completed=0 THEN 0
                            WHEN p.profile_key IS NULL OR (p.uses_legacy_objectives=1 AND live.objective_id IS NULL)
                            THEN o.Points ELSE 0 END AS Points,
                       CASE WHEN live.earned=1 THEN 1 ELSE 0 END AS SaveVerified,
                       live.detail AS SaveDetail
                FROM objectives_list o
                LEFT JOIN save_profiles p ON p.is_active=1
                LEFT JOIN save_objectives live ON live.objective_id=o.id AND live.profile_key=p.profile_key
                LEFT JOIN profile_objective_checks manual ON manual.objective_id=o.id AND manual.profile_key=p.profile_key;";
            using (var transaction = connection.BeginTransaction())
            {
                using (var command = new SQLiteCommand(createTable, connection, transaction))
                    command.ExecuteNonQuery();

                bool needsViewUpdate = SaveGameGarageImport.EnsureColumn(
                    connection, "save_objectives", "points", "INTEGER", transaction);
                bool migrateLegacy = SaveGameGarageImport.EnsureColumn(
                    connection, "save_profiles", "uses_legacy_objectives", "INTEGER NOT NULL DEFAULT 0", transaction);
                if (migrateLegacy)
                {
                    // The original checklist predates profile support. Keep it with the first imported garage.
                    using (var command = new SQLiteCommand(@"
                        UPDATE save_profiles SET uses_legacy_objectives=1 WHERE profile_key=COALESCE(
                            (SELECT profile_key FROM garage_vehicles ORDER BY id LIMIT 1),
                            (SELECT profile_key FROM save_profiles WHERE is_active=1 LIMIT 1));", connection, transaction))
                        command.ExecuteNonQuery();
                }
                SaveGameGarageImport.EnsureColumn(
                    connection, "save_profiles", "objective_mapping_version", "INTEGER", transaction);
                using (var command = new SQLiteCommand(
                    (needsViewUpdate || migrateLegacy ? "DROP VIEW IF EXISTS displayed_objectives;" : "") + createView,
                    connection, transaction))
                    command.ExecuteNonQuery();
                transaction.Commit();
            }
        }

        public static void Apply(SQLiteConnection connection, SQLiteTransaction transaction, GarageBridgeSnapshot snapshot)
        {
            if (snapshot.licences == null || snapshot.contest_results == null)
                return;

            const string sql = @"
                INSERT INTO save_objectives (profile_key, objective_id, earned, detail, points)
                SELECT @Profile, MIN(id), @Passed, @Detail, MIN(MaximumPoints)*@Count/@Target FROM objectives_list
                WHERE ParentCategory=@Category AND Subcategory=@Group
                  AND TRIM(Description)=@Description AND (@Island IS NULL OR COALESCE(Island,'')=@Island)
                HAVING COUNT(*)=1
                ON CONFLICT(profile_key, objective_id) DO UPDATE
                SET earned=MAX(save_objectives.earned, excluded.earned), detail=excluded.detail, points=excluded.points;";
            using (var command = new SQLiteCommand(sql, connection, transaction))
            {
                command.Parameters.AddWithValue("@Profile", snapshot.profile_key);
                command.Parameters.AddWithValue("@Category", "");
                command.Parameters.AddWithValue("@Group", "");
                command.Parameters.AddWithValue("@Description", "");
                command.Parameters.AddWithValue("@Island", "");
                command.Parameters.AddWithValue("@Passed", 0);
                command.Parameters.AddWithValue("@Detail", DBNull.Value);
                command.Parameters.AddWithValue("@Count", DBNull.Value);
                command.Parameters.AddWithValue("@Target", 1);

                void Store(string group, string description, string island, bool? passed, string detail = null,
                    string category = "Competition", int? count = null, int target = 1)
                {
                    if (!passed.HasValue)
                        return;
                    command.Parameters["@Category"].Value = category;
                    command.Parameters["@Group"].Value = group;
                    command.Parameters["@Description"].Value = description;
                    command.Parameters["@Island"].Value = (object)island ?? DBNull.Value;
                    command.Parameters["@Passed"].Value = passed.Value ? 1 : 0;
                    command.Parameters["@Detail"].Value = (object)detail ?? DBNull.Value;
                    command.Parameters["@Count"].Value = (object)count ?? DBNull.Value;
                    command.Parameters["@Target"].Value = target;
                    command.ExecuteNonQuery();
                }

                void StoreCount(string category, string group, string description, string label, long? count, int target)
                {
                    if (!count.HasValue || count.Value < 0)
                        return;
                    int progress = (int)Math.Min(count.Value, target);
                    Store(group, description, "", progress == target, $"{label}: {progress}/{target}",
                        category, progress, target);
                }

                bool? HasAward(int index)
                {
                    var awards = snapshot.event_xps;
                    return awards != null && index < awards.Count && awards[index] >= 0
                        ? (bool?)(awards[index] > 0) : null;
                }

                foreach (var licence in snapshot.licences)
                {
                    string name;
                    string island;
                    switch (licence.name)
                    {
                        case "1:C4": name = "C4"; island = "Ibiza"; break;
                        case "1:B4": name = "B4"; island = "Ibiza"; break;
                        case "1:A7": name = "A7-A6"; island = "Ibiza"; break;
                        case "1:C3": name = "C3"; island = "Hawaii"; break;
                        case "1:B3": name = "B3"; island = "Hawaii"; break;
                        case "1:A5": name = "A5-A4"; island = "Hawaii"; break;
                        case "2:A3": name = "A3-A2"; island = "Hawaii"; break;
                        case "3:A1": name = "A1"; island = "Hawaii"; break;
                        default: continue;
                    }
                    Store("Racing School", $"Obtain {name} Licence on", island, licence.passed);
                }

                foreach (var contest in snapshot.contest_results)
                {
                    if (!contest.verified)
                        continue;
                    string detail = contest.passed == false && contest.round_count > 0 && contest.recorded_rounds.HasValue
                        ? $"Current save: {contest.recorded_rounds}/{contest.round_count} rounds with results" : null;
                    var championship = Regex.Match(contest.name ?? "", @"^([ABC][1-7]) Championship ([12])$");
                    if (championship.Success)
                    {
                        string high = championship.Groups[2].Value == "2" ? " High" : "";
                        Store("Championships", $"Win {championship.Groups[1].Value}{high} Championship on",
                            null, contest.passed, detail);
                    }
                    else if (contest.name == "Ibiza Cup" || contest.name == "Hawaii Cup 1" || contest.name == "Hawaii Cup 2")
                    {
                        int area = contest.name == "Hawaii Cup 2" ? 2 : 1;
                        string island = contest.name == "Ibiza Cup" ? "Ibiza" : "Hawaii";
                        int award = island == "Ibiza" ? 101 : area == 1 ? 107 : 110;
                        Store("Cups", $"Win Cup Area {area} on", island,
                            HasAward(award) == true ? true : contest.passed, detail);
                    }
                }

                StoreCount("Competition", "Multiplayer Challenges", "Win 50 ranked multi challenges",
                    "Ranked wins", snapshot.ranked_wins, 50);
                StoreCount("Social", "Club", "Play 50 challenges against members of your Club",
                    "Club challenges played", snapshot.inner_club_played, 50);
                StoreCount("Social", "Club", "Play 50 Versus Club challenges",
                    "Versus Club challenges played", snapshot.versus_club_played, 50);

                // Reward indices match DBRewardXP._hashcode in the game catalogue.
                foreach (var duel in new[] { (104, "Miami Harris", "Ibiza"), (160, "Ryan Wilder", "Ibiza"),
                    (161, "Chris Wilder", "Hawaii"), (162, "Tess Wintory", "Hawaii"), (163, "Stuart Wintory", "Hawaii") })
                    Store("Duel", $"Win duel against {duel.Item2}", duel.Item3, HasAward(duel.Item1));

                foreach (var threshold in new[] { (13, 1), (159, 3), (113, 6), (15, 10), (115, 15), (116, 20),
                    (117, 25), (118, 30), (119, 40), (178, 50) })
                    Store("Vehicles", threshold.Item2 == 1 ? "Own 1 vehicle" : $"Own {threshold.Item2} different vehicles",
                        "", HasAward(threshold.Item1), category: "Collection");

                foreach (var threshold in new[] { (114, 1), (0, 3), (1, 6), (14, 10), (16, 15) })
                    Store("Houses and furniture", threshold.Item2 == 1 ? "Own 1 house" : $"Own {threshold.Item2} houses",
                        "", HasAward(threshold.Item1), category: "Collection");

                Store("Club", "Become a Club Member", "", HasAward(11), category: "Social");
                Store("Club", "Reach the Club Level 2", "", HasAward(157), category: "Social");
                Store("Club", "Reach the Club Level 3", "", HasAward(156), category: "Social");

                if (snapshot.roads_unlocked?.Count == 6 && snapshot.roads_total?.Count == 6)
                {
                    // DBCruisingZones orders Ibiza areas 1–2 before Hawaii areas 1–4.
                    for (int area = 0; area < 6; area++)
                    {
                        int unlocked = snapshot.roads_unlocked[area];
                        int total = snapshot.roads_total[area];
                        if (total <= 0 || unlocked < 0 || unlocked > total)
                            continue;
                        foreach (int milestone in new[] { 10, 25, 50, 75, 100 })
                            Store("Roads", $"Discover {milestone}% of roads in area {(area < 2 ? area + 1 : area - 1)} on",
                                area < 2 ? "Ibiza" : "Hawaii", (long)unlocked * 100 >= (long)milestone * total,
                                $"{unlocked}/{total} roads discovered", "Discovery");
                    }
                }
            }

            using (var command = new SQLiteCommand(
                @"UPDATE save_profiles SET objective_source_sha256=@Hash, objective_mapping_version=@Version
                  WHERE profile_key=@Profile", connection, transaction))
            {
                command.Parameters.AddWithValue("@Profile", snapshot.profile_key);
                command.Parameters.AddWithValue("@Hash", snapshot.source.sha256);
                command.Parameters.AddWithValue("@Version", MappingVersion);
                command.ExecuteNonQuery();
            }
        }
    }
}

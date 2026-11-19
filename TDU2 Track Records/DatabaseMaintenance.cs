using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;

namespace TDU2_Track_Records
{
    internal static class DatabaseMaintenance
    {
        internal static string ResolvePath(string connectionString)
        {
            string path = new SQLiteConnectionStringBuilder(connectionString).DataSource;
            if (string.IsNullOrWhiteSpace(path) || path == ":memory:")
                throw new InvalidOperationException("The configured database is not a local file.");
            if (path.StartsWith("|DataDirectory|", StringComparison.OrdinalIgnoreCase))
                path = Path.Combine(AppData.DirectoryPath, path.Substring("|DataDirectory|".Length).TrimStart('\\', '/'));
            return Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, path));
        }

        internal static string Check(string path, string reference)
        {
            using (var current = Open(path, readOnly: true))
            {
                CheckIntegrity(current);
                if (!File.Exists(reference))
                    return "SQLite integrity check passed. The reference database is unavailable for schema comparison.";

                using (var expected = Open(reference, readOnly: true))
                {
                    var currentSchema = ReadSchema(current);
                    var expectedSchema = ReadSchema(expected);
                    var differences = new List<string>();
                    foreach (var table in expectedSchema)
                    {
                        if (!currentSchema.TryGetValue(table.Key, out var columns))
                        {
                            differences.Add(table.Key + " (missing table)");
                            continue;
                        }
                        foreach (var column in table.Value)
                        {
                            if (!columns.TryGetValue(column.Key, out var definition) || definition != column.Value)
                                differences.Add(table.Key + "." + column.Key);
                        }
                    }

                    return differences.Count == 0
                        ? "SQLite integrity check passed. The database has the tables and columns expected by this release."
                        : $"SQLite integrity check passed, but {differences.Count} required schema entries differ. " +
                          $"A data migration is recommended before using this release with this database. " +
                          $"Examples: {string.Join(", ", differences.Take(5))}.";
                }
            }
        }

        internal static string CreateBackup(string path)
        {
            string folder = Path.Combine(Path.GetDirectoryName(path), "backups");
            Directory.CreateDirectory(folder);
            string file = Path.Combine(folder, $"track-records-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.db");
            string temporary = file + ".partial";
            try
            {
                using (var source = Open(path, readOnly: true))
                using (var command = source.CreateCommand())
                {
                    command.CommandText = "VACUUM INTO @file";
                    command.Parameters.AddWithValue("@file", temporary);
                    command.ExecuteNonQuery();
                }
                using (var backup = Open(temporary, readOnly: true))
                    CheckIntegrity(backup);
                File.Move(temporary, file);
                return file;
            }
            finally
            {
                if (File.Exists(temporary))
                    File.Delete(temporary);
            }
        }

        internal static string Optimize(string path)
        {
            string backup = CreateBackup(path);
            using (var connection = Open(path, readOnly: false))
            {
                CheckIntegrity(connection);
                using (var command = new SQLiteCommand("ANALYZE;", connection))
                    command.ExecuteNonQuery();
            }
            return backup;
        }

        private static SQLiteConnection Open(string path, bool readOnly)
        {
            var settings = new SQLiteConnectionStringBuilder
            {
                DataSource = path,
                ReadOnly = readOnly,
                FailIfMissing = true,
                DefaultTimeout = 10
            };
            var connection = new SQLiteConnection(settings.ConnectionString);
            try
            {
                connection.Open();
                return connection;
            }
            catch
            {
                connection.Dispose();
                throw;
            }
        }

        private static void CheckIntegrity(SQLiteConnection connection)
        {
            using (var command = new SQLiteCommand("PRAGMA quick_check(1);", connection))
            {
                string result = Convert.ToString(command.ExecuteScalar());
                if (result != "ok")
                    throw new InvalidDataException("SQLite reported an integrity problem. Restore a verified backup or recover the database before migrating. " + result);
            }
        }

        private static Dictionary<string, Dictionary<string, string>> ReadSchema(SQLiteConnection connection)
        {
            const string sql = @"
                SELECT m.name, p.name, p.type, p.pk
                FROM sqlite_master m JOIN pragma_table_info(m.name) p
                WHERE m.type='table' AND substr(m.name, 1, 7) <> 'sqlite_';";
            var tables = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            using (var command = new SQLiteCommand(sql, connection))
            using (var rows = command.ExecuteReader())
            {
                while (rows.Read())
                {
                    string table = rows.GetString(0);
                    if (!tables.TryGetValue(table, out var columns))
                    {
                        columns = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        tables.Add(table, columns);
                    }
                    columns.Add(rows.GetString(1), Affinity(rows.GetString(2)) + ":" + rows.GetInt32(3));
                }
            }
            return tables;
        }

        private static string Affinity(string declaredType)
        {
            string type = declaredType.ToUpperInvariant();
            if (type.Contains("INT"))
                return "INTEGER";
            if (type.Contains("CHAR") || type.Contains("CLOB") || type.Contains("TEXT"))
                return "TEXT";
            if (type.Length == 0 || type.Contains("BLOB"))
                return "BLOB";
            return type.Contains("REAL") || type.Contains("FLOA") || type.Contains("DOUB") ? "REAL" : "NUMERIC";
        }
    }
}

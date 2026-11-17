using System;
using System.IO;

namespace TDU2_Track_Records
{
    internal static class AppData
    {
        private const string DataDirectoryOverride = "TDU2_TRACK_RECORDS_DATA_DIR";
        private const string DatabaseFileName = "database.db";
        private const string SeedFileName = "database.seed.db";

        internal static string DirectoryPath { get; private set; }
        internal static string DatabasePath => Path.Combine(DirectoryPath, DatabaseFileName);

        internal static void Initialize()
        {
            string configuredPath = Environment.GetEnvironmentVariable(DataDirectoryOverride);
            DirectoryPath = string.IsNullOrWhiteSpace(configuredPath)
                ? Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "MoDuLah", "TDU2 Track Records")
                : Path.GetFullPath(configuredPath);

            Directory.CreateDirectory(DirectoryPath);
            AppDomain.CurrentDomain.SetData("DataDirectory", DirectoryPath);

            if (File.Exists(DatabasePath))
            {
                if (new FileInfo(DatabasePath).Length == 0)
                    throw new InvalidDataException($"The Track Records database is empty: {DatabasePath}");
                return;
            }

            string seedPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, SeedFileName);
            if (!File.Exists(seedPath))
                throw new FileNotFoundException("The clean Track Records database seed is missing.", seedPath);

            File.Copy(seedPath, DatabasePath, false);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Media;
using TDU2_Track_Records.Classes;

namespace TDU2_Track_Records
{
    internal sealed class DealershipStock
    {
        private readonly string connectionString;
        private readonly string searchSql;
        internal IReadOnlyList<string> SlotColumns { get; }

        internal DealershipStock(string connectionString)
        {
            this.connectionString = connectionString;
            using (var connection = new SQLiteConnection(connectionString))
            using (var command = new SQLiteCommand("SELECT * FROM dealerships LIMIT 0", connection))
            {
                connection.Open();
                using (var reader = command.ExecuteReader())
                {
                    SlotColumns = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName)
                        .Where(name => Regex.IsMatch(name, @"\ASlot[1-9][0-9]*_VehicleId\z")).ToArray();
                }
            }

            string sources = string.Join(" UNION ALL ", SlotColumns.Select(slot =>
                $"SELECT Island, Name, {slot} AS VehicleId FROM dealerships WHERE {slot} IS NOT NULL AND {slot} <> ''"));
            searchSql = $@"
                WITH stock AS ({sources}), locations AS (
                    SELECT DISTINCT Island, Name, CAST(VehicleId AS INTEGER) AS VehicleId
                    FROM stock WHERE Island IN ('Ibiza', 'Hawaii', 'Casino') AND TRIM(Name) <> ''
                )
                SELECT v.id, v._vehicle_name, v._vehiclecategory_name, v._price,
                       CAST(v._vehicle_image AS BLOB) AS Photo, l.Name, l.Island
                FROM locations l JOIN vehicles v ON v.id = l.VehicleId
                WHERE INSTR(LOWER(v._vehicle_name), LOWER(@Search)) > 0
                   OR INSTR(LOWER(v._brand_name), LOWER(@Search)) > 0
                   OR INSTR(LOWER(v._modelfull_name), LOWER(@Search)) > 0
                ORDER BY v._vehicle_name COLLATE NOCASE, l.Island, l.Name COLLATE NOCASE;";
        }

        internal List<DealershipSearchResult> Search(string search)
        {
            var results = new List<DealershipSearchResult>();
            if (SlotColumns.Count == 0)
                return results;

            var converter = new ByteArrayToImageSourceConverter();
            using (var connection = new SQLiteConnection(connectionString))
            using (var command = new SQLiteCommand(searchSql, connection))
            {
                connection.Open();
                command.Parameters.AddWithValue("@Search", search);
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        int vehicleId = Convert.ToInt32(reader["id"]);
                        ImageSource photo = ByteArrayToImageSourceConverter.ReadVehiclePhoto(vehicleId) ??
                            converter.Convert(reader["Photo"], typeof(ImageSource), null, null) as ImageSource;
                        results.Add(new DealershipSearchResult
                        {
                            Thumbnail = photo,
                            CarName = reader["_vehicle_name"].ToString(),
                            VehicleClass = reader["_vehiclecategory_name"].ToString(),
                            Price = decimal.TryParse(reader["_price"].ToString(), out decimal price) ? "$" + price.ToString("N0") : "—",
                            DealerName = reader["Name"].ToString(),
                            Island = reader["Island"].ToString()
                        });
                    }
                }
            }
            return results;
        }
    }

    internal sealed class DealershipSearchResult
    {
        public ImageSource Thumbnail { get; set; }
        public string CarName { get; set; }
        public string VehicleClass { get; set; }
        public string Price { get; set; }
        public string DealerName { get; set; }
        public string Island { get; set; }
    }
}

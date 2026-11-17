using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace TDU2_Track_Records
{
    internal sealed class LocationMenu
    {
        internal sealed class Location
        {
            public string Name { get; }
            public string Island { get; }
            public string Region { get; }
            public string MenuLabel { get; }

            public Location(string name, string island, bool separateCasinoShowrooms)
            {
                Name = name;
                Island = island;
                bool isIslandCasino = separateCasinoShowrooms &&
                    string.Equals(name, "Casino", StringComparison.OrdinalIgnoreCase) && island != "Casino";
                Region = isIslandCasino ? "Casino" : island;
                MenuLabel = isIslandCasino ? $"{name} ({island})" : name;
            }
        }

        private List<Location> locations;
        private readonly Dictionary<string, MenuItem> regions = new Dictionary<string, MenuItem>();
        private readonly TextBox searchBox;
        private readonly TextBlock countText;
        private readonly string noun;
        private readonly Action<Location> showLocation;

        public Location Selected { get; private set; }

        public LocationMenu(Menu menu, TextBox searchBox, TextBlock countText, List<Location> locations,
            string noun, Action<Location> showLocation, bool includeCasino = false)
        {
            this.locations = locations;
            this.searchBox = searchBox;
            this.countText = countText;
            this.noun = noun;
            this.showLocation = showLocation;
            AddRegion(menu, "Ibiza", "IBIZA");
            AddRegion(menu, "Hawaii", "HAWAII / OAHU");
            if (includeCasino)
                AddRegion(menu, "Casino", "CASINO");
            PopulateMenus(searchBox.Text);
            searchBox.TextChanged += (sender, e) => PopulateMenus(searchBox.Text);
        }

        public static List<Location> ReadLocations(string connectionString, string query, bool separateCasinoShowrooms = false)
        {
            var locations = new List<Location>();
            using (var connection = new SQLiteConnection(connectionString))
            using (var command = new SQLiteCommand(query, connection))
            {
                connection.Open();
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string name = reader["Name"].ToString();
                        string island = reader["Island"].ToString();
                        if (!string.IsNullOrWhiteSpace(name) &&
                            (island == "Ibiza" || island == "Hawaii" || island == "Casino"))
                            locations.Add(new Location(name, island, separateCasinoShowrooms));
                    }
                }
            }
            return locations;
        }

        public void SelectFirst()
        {
            Location first = FirstLocation();
            if (first != null)
                Select(first);
        }

        public void ClearSelection()
        {
            Selected = null;
            foreach (var region in regions)
            {
                region.Value.Tag = false;
                foreach (MenuItem option in region.Value.Items)
                    option.IsChecked = false;
            }
            showLocation(null);
        }

        public bool ReplaceLocations(List<Location> newLocations)
        {
            Location previous = Selected;
            locations = newLocations;
            Location retained = previous == null ? null : locations.FirstOrDefault(location =>
                location.Name == previous.Name && location.Island == previous.Island);
            PopulateMenus(searchBox.Text);
            Select(retained ?? FirstLocation());
            return retained != null;
        }

        private Location FirstLocation() => locations.FirstOrDefault(location => location.Region == "Ibiza") ??
                                            locations.FirstOrDefault(location => regions.ContainsKey(location.Region));

        private void AddRegion(Menu menu, string region, string label)
        {
            var item = new MenuItem { Header = label };
            item.SetResourceReference(FrameworkElement.StyleProperty, "LocationMenuRegionStyle");
            regions.Add(region, item);
            menu.Items.Add(item);
        }

        private void PopulateMenus(string search)
        {
            string filter = search.Trim();
            foreach (var region in regions)
            {
                MenuItem header = region.Value;
                header.Items.Clear();
                foreach (Location location in locations.Where(location => location.Region == region.Key &&
                    location.MenuLabel.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    var option = new MenuItem
                    {
                        Header = location.MenuLabel,
                        Tag = location,
                        FocusVisualStyle = null,
                        IsChecked = location == Selected
                    };
                    option.Click += (sender, e) =>
                    {
                        header.IsSubmenuOpen = false;
                        Select(location);
                    };
                    header.Items.Add(option);
                }

                if (header.Items.Count == 0)
                {
                    header.Items.Add(new MenuItem
                    {
                        Header = filter.Length == 0 ? $"No {noun.ToLowerInvariant()} here" : "No matching locations",
                        IsEnabled = false
                    });
                }
            }
        }

        private void Select(Location location)
        {
            Selected = location;
            int count = location == null ? 0 : locations.Count(item => item.Region == location.Region);
            countText.Text = $"{count} {noun}";
            foreach (var region in regions)
            {
                region.Value.Tag = region.Key == location?.Region;
                foreach (MenuItem option in region.Value.Items)
                    option.IsChecked = location != null && ReferenceEquals(option.Tag, location);
            }
            showLocation(location);
        }
    }
}

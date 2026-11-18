using System;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace TDU2_Track_Records
{
    internal static class FactoryPaintPreview
    {
        private sealed class FactoryPaint
        {
            public int vehicle_id { get; set; }
            public int paint_index { get; set; }
            public string name { get; set; }
            public int price { get; set; }
            public string primary { get; set; }
            public string secondary { get; set; }
            public bool has_secondary { get; set; }
        }

        private static readonly Lazy<ILookup<int, FactoryPaint>> Catalogue =
            new Lazy<ILookup<int, FactoryPaint>>(LoadCatalogue);

        private static ILookup<int, FactoryPaint> LoadCatalogue()
        {
            using (Stream stream = typeof(FactoryPaintPreview).Assembly.GetManifestResourceStream(
                "TDU2_Track_Records.vehicle-paints.json"))
            {
                if (stream == null)
                    throw new InvalidDataException("The factory paint catalogue is missing.");

                using (var reader = new StreamReader(stream))
                {
                    var serializer = new JavaScriptSerializer { MaxJsonLength = 4 * 1024 * 1024 };
                    return serializer.Deserialize<FactoryPaint[]>(reader.ReadToEnd())
                        .ToLookup(paint => paint.vehicle_id);
                }
            }
        }

        internal static FrameworkElement Create(int vehicleId)
        {
            FactoryPaint[] paints = Catalogue.Value[vehicleId]
                .OrderBy(paint => paint.paint_index).ToArray();
            if (paints.Length == 0)
                return null;

            var panel = new StackPanel { Margin = new Thickness(10, 0, 10, 8) };
            panel.Children.Add(new TextBlock
            {
                Text = $"FACTORY PAINTS ({paints.Length})",
                Foreground = new SolidColorBrush(Color.FromRgb(170, 174, 183)),
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 4)
            });

            var swatches = new WrapPanel();
            foreach (FactoryPaint paint in paints)
            {
                var colours = new Grid();
                colours.ColumnDefinitions.Add(new ColumnDefinition());
                colours.Children.Add(CreateColour(paint.primary));
                if (paint.has_secondary)
                {
                    colours.ColumnDefinitions.Add(new ColumnDefinition());
                    Border secondary = CreateColour(paint.secondary);
                    Grid.SetColumn(secondary, 1);
                    colours.Children.Add(secondary);
                }

                string price = paint.price > 0 ? "+" + paint.price.ToString("C0",
                    System.Globalization.CultureInfo.GetCultureInfo("en-US"))
                    : paint.price == 0 ? "Included" : "Price unavailable";
                bool unresolved = paint.primary == null || (paint.has_secondary && paint.secondary == null);
                string description = paint.name + "\n" + price + "\n" +
                    (unresolved ? "Colour preview unavailable for this finish."
                        : "Base-colour preview. Metallic, pearl and texture effects vary in game.");
                var swatch = new Border
                {
                    Width = 28,
                    Height = 22,
                    Margin = new Thickness(0, 0, 5, 5),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(132, 136, 144)),
                    BorderThickness = new Thickness(1),
                    Child = colours,
                    ToolTip = description,
                    Focusable = true,
                    Cursor = System.Windows.Input.Cursors.Help
                };
                AutomationProperties.SetName(swatch, description);
                swatches.Children.Add(swatch);
            }
            panel.Children.Add(swatches);
            return panel;
        }

        private static Border CreateColour(string hex)
        {
            if (hex != null)
                return new Border { Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)) };

            return new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(39, 42, 48)),
                Child = new TextBlock
                {
                    Text = "?",
                    FontSize = 12,
                    Foreground = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
        }
    }
}


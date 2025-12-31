// -----------------------------------------------------------------------------
// World Economic Data Reporting Program
// -----------------------------------------------------------------------------
// Student(s): Omar Alkhamissi
// Date: July 17, 2025
// -----------------------------------------------------------------------------
// This C# Console App that loads an XML the data file global_economies.xml using the XML DOM and uses XPath queries
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.XPath;

namespace GlobalEconomics
{
    internal static class Program
    {
        // ----- Constants ----------------------------------------------------
        private const int MinYear = 1970;
        private const int MaxYear = 2021;
        private const int MaxSpan = 5;              // inclusive span (<=5 yrs)
        private const int MaxLineWidth = 100;       // per assignment

        private const string DataFileName = "global_economies.xml";   
        private const string YearFileName = "user_year_range.xml";     // persistent year settings

        // Default year range used on very first run 
        private const int DefaultStartYear = 2017;
        private const int DefaultEndYear = 2021;

        // These names correspond to attribute names in the data file.
        // labelXPath is used to fetch the human‑readable label text from the <labels> section.
        private sealed record MetricDef(string Category, string AttrName, string LabelXPath);

        // Populated after XML load.
        private static List<MetricDef> _metrics = new();

        // Region cache – list of (rid, rname)
        private sealed record RegionDef(string Rid, string Name);
        private static List<RegionDef> _regions = new();

        // DOM + XPath objects
        private static XmlDocument? _dataDoc;
        private static XPathNavigator? _dataNav;

        // Year range currently in effect
        private sealed record YearRange(int Start, int End)
        {
            public int Count => End - Start + 1;
        }
        private static YearRange _yearRange = new(DefaultStartYear, DefaultEndYear);

        // -----------------------------------------------------------------
        //  Main
        // -----------------------------------------------------------------
        private static void Main()
        {
            try
            {
                LoadDataFile();              // load DOM + build region + metric lists
                LoadYearSettings();           // load persisted years if available
                MainMenuLoop();               // interactive UI loop
            }
            catch (IOException ex)
            {
                Console.WriteLine($"IO ERROR: {ex.Message}");
            }
            catch (XmlException ex)
            {
                Console.WriteLine($"XML ERROR: {ex.Message}");
            }
            catch (XPathException ex)
            {
                Console.WriteLine($"XPATH ERROR: {ex.Message}");
            }
            catch (Exception ex)
            {
                // Safety net
                Console.WriteLine($"UNEXPECTED ERROR: {ex.Message}");
            }
        }

        // -----------------------------------------------------------------
        //  Data Load & Caches
        // -----------------------------------------------------------------
        private static void LoadDataFile()
        {
            string path = Path.Combine(AppContext.BaseDirectory, DataFileName);
            if (!File.Exists(path))
            {
                throw new IOException($"Required data file '{DataFileName}' not found in {AppContext.BaseDirectory}.");
            }

            _dataDoc = new XmlDocument();
            _dataDoc.Load(path);
            _dataNav = _dataDoc.CreateNavigator();

            BuildRegionList();
            BuildMetricList();
        }

        private static void BuildRegionList()
        {
            _regions.Clear();
            if (_dataNav == null) return;

            XPathNodeIterator it = _dataNav.Select("/global_economies/region");
            while (it.MoveNext())
            {
                string rid = it.Current.GetAttribute("rid", string.Empty);
                string name = it.Current.GetAttribute("rname", string.Empty);
                if (!string.IsNullOrWhiteSpace(name))
                {
                    _regions.Add(new RegionDef(rid, name));
                }
            }
        }

        private static void BuildMetricList()
        {
            _metrics.Clear();
            if (_dataNav == null) return;

            _metrics.Add(new MetricDef("inflation", "consumer_prices_percent", "/global_economies/labels/inflation/@consumer_prices_percent"));
            _metrics.Add(new MetricDef("inflation", "gdp_deflator_percent", "/global_economies/labels/inflation/@gdp_deflator_percent"));
            _metrics.Add(new MetricDef("interest_rates", "real", "/global_economies/labels/interest/@real"));
            _metrics.Add(new MetricDef("interest_rates", "lending", "/global_economies/labels/interest/@lending"));
            _metrics.Add(new MetricDef("interest_rates", "deposit", "/global_economies/labels/interest/@deposit"));
            _metrics.Add(new MetricDef("unemployment_rates", "national_estimate", "/global_economies/labels/unemployment/@national_estimate"));
            _metrics.Add(new MetricDef("unemployment_rates", "modeled_ILO_estimate", "/global_economies/labels/unemployment/@modeled_ILO_estimate"));
        }

        private static string GetMetricLabel(MetricDef m)
        {
            if (_dataNav == null) return m.AttrName;
            XPathNavigator? node = _dataNav.SelectSingleNode(m.LabelXPath);
            return node?.Value ?? m.AttrName;
        }

        // -----------------------------------------------------------------
        //  Year Range Persistence
        // -----------------------------------------------------------------
        private static void LoadYearSettings()
        {
            string path = Path.Combine(AppContext.BaseDirectory, YearFileName);
            if (!File.Exists(path))
            {
                _yearRange = new YearRange(DefaultStartYear, DefaultEndYear);
                return;
            }

            XmlDocument yrDoc = new();
            try
            {
                yrDoc.Load(path);
                XmlNode? sNode = yrDoc.SelectSingleNode("/user_year_range/start");
                XmlNode? eNode = yrDoc.SelectSingleNode("/user_year_range/end");
                if (sNode != null && eNode != null &&
                    int.TryParse(sNode.InnerText, out int s) &&
                    int.TryParse(eNode.InnerText, out int e) &&
                    ValidateYearRangeNoPrompt(s, e))
                {
                    _yearRange = new YearRange(s, e);
                }
                else
                {
                    _yearRange = new YearRange(DefaultStartYear, DefaultEndYear);
                }
            }
            catch (Exception)
            {
                // If file corrupt, fall back to defaults.
                _yearRange = new YearRange(DefaultStartYear, DefaultEndYear);
            }
        }

        private static void SaveYearSettings()
        {
            string path = Path.Combine(AppContext.BaseDirectory, YearFileName);

            XmlDocument doc = new();
            XmlElement root = doc.CreateElement("user_year_range");
            doc.AppendChild(root);

            XmlElement start = doc.CreateElement("start");
            start.InnerText = _yearRange.Start.ToString();
            root.AppendChild(start);

            XmlElement end = doc.CreateElement("end");
            end.InnerText = _yearRange.End.ToString();
            root.AppendChild(end);

            doc.Save(path);
        }

        // -----------------------------------------------------------------
        //  Menu Loop
        // -----------------------------------------------------------------
        private static void MainMenuLoop()
        {
            while (true)
            {
                PrintTitle();
                Console.WriteLine("'Y' to adjust the range of years (currently {0} to {1})", _yearRange.Start, _yearRange.End);
                Console.WriteLine("'R' to print a regional summary");
                Console.WriteLine("'M' to print a specific metric for all regions");
                Console.WriteLine("'X' to exit the program");
                Console.Write("Your selection: ");

                string? input = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(input))
                {
                    continue;
                }

                char c = char.ToUpperInvariant(input.Trim()[0]);
                Console.WriteLine();

                switch (c)
                {
                    case 'Y':
                        PromptForYearRange();
                        break;
                    case 'R':
                        DoRegionReport();
                        break;
                    case 'M':
                        DoMetricReport();
                        break;
                    case 'X':
                        Console.WriteLine("All done!");
                        return;
                    default:
                        // ignore invalid and continue loop
                        break;
                }

                Console.WriteLine();
            }
        }

        private static void PrintTitle()
        {
            Console.WriteLine("World Economic Data");
            Console.WriteLine("===================");
            Console.WriteLine();
        }

        // -----------------------------------------------------------------
        //  Year Range Prompt
        // -----------------------------------------------------------------
        private static void PromptForYearRange()
        {
            int s = PromptForInt($"Starting year ({MinYear} to {MaxYear}): ", MinYear, MaxYear);

            int maxAllowed = Math.Min(MaxYear, s + MaxSpan - 1);
            int e;
            while (true)
            {
                e = PromptForIntAllowMessage($"\nEnding year ({MinYear} to {MaxYear}): ");
                if (ValidateYearRangeNoPrompt(s, e))
                {
                    break;
                }
                else
                {
                    Console.WriteLine($"ERROR: Ending year must be an integer between {s} and {maxAllowed}.");
                }
            }

            _yearRange = new YearRange(s, e);
            try { SaveYearSettings(); } catch { /* ignore – handled on next load */ }
        }

        private static bool ValidateYearRangeNoPrompt(int s, int e)
        {
            if (s < MinYear || s > MaxYear) return false;
            if (e < s) return false;
            if (e > MaxYear) return false;
            if ((e - s + 1) > MaxSpan) return false;
            return true;
        }

        // -----------------------------------------------------------------
        //  Region Report
        // -----------------------------------------------------------------
        private static void DoRegionReport()
        {
            if (_regions.Count == 0)
            {
                Console.WriteLine("No regions loaded.");
                return;
            }

            // Menu
            Console.WriteLine("Select a region by number as shown below...\n");
            int digits = _regions.Count.ToString().Length;
            for (int i = 0; i < _regions.Count; i++)
            {
                Console.WriteLine($"{(i + 1).ToString().PadLeft(digits)}. {_regions[i].Name}");
            }
            Console.WriteLine();

            int choice = PromptForInt($"Enter a region #: ", 1, _regions.Count);
            RegionDef r = _regions[choice - 1];
            Console.WriteLine();

            PrintRegionReport(r);
        }

        private static void PrintRegionReport(RegionDef region)
        {
            Console.WriteLine($"Economic Information for {region.Name}");
            Console.WriteLine(new string('-', Math.Min(MaxLineWidth, 31 + region.Name.Length)));
            Console.WriteLine();

            // Header row
            // first col is metric label
            const int firstColWidth = 22;
            string header = PadLeftToWidth("Economic Metric", firstColWidth) + BuildYearHeader();
            Console.WriteLine(header);
            Console.WriteLine();

            foreach (MetricDef m in _metrics)
            {
                string label = GetMetricLabel(m);
                label = TrimToWidth(label, firstColWidth, leftJustify: false);
                string row = PadLeftToWidth(label, firstColWidth) + BuildDataCells(region, m);
                Console.WriteLine(row);
            }
        }

        // -----------------------------------------------------------------
        //  Metric Report
        // -----------------------------------------------------------------
        private static void DoMetricReport()
        {
            if (_metrics.Count == 0)
            {
                Console.WriteLine("No metrics loaded.");
                return;
            }

            Console.WriteLine("Select a metric by number as shown below...");
            for (int i = 0; i < _metrics.Count; i++)
            {
                Console.WriteLine($"  {i + 1}. {GetMetricLabel(_metrics[i])}");
            }
            Console.WriteLine();

            int choice = PromptForInt("Enter a metric #: ", 1, _metrics.Count);
            MetricDef m = _metrics[choice - 1];
            Console.WriteLine();

            PrintMetricReport(m);
        }

        private static void PrintMetricReport(MetricDef metric)
        {
            string label = GetMetricLabel(metric);
            Console.WriteLine($"{label} By Region");
            Console.WriteLine(new string('-', Math.Min(MaxLineWidth, label.Length + 11)));
            Console.WriteLine();

            const int firstColWidth = 45; // wide enough for most region names; truncated if needed
            string header = PadLeftToWidth("Region", firstColWidth) + BuildYearHeader();
            Console.WriteLine(header);
            Console.WriteLine();

            foreach (RegionDef r in _regions)
            {
                string rname = TrimToWidth(r.Name, firstColWidth, leftJustify: false);
                string row = PadLeftToWidth(rname, firstColWidth) + BuildDataCells(r, metric);
                Console.WriteLine(row);
            }
        }

        // -----------------------------------------------------------------
        //  Build Cells Helpers
        // -----------------------------------------------------------------
        private static string BuildYearHeader()
        {
            StringBuilder sb = new();
            for (int y = _yearRange.Start; y <= _yearRange.End; y++)
            {
                sb.Append(y.ToString().PadLeft(8));
            }
            return sb.ToString();
        }

        private static string BuildDataCells(RegionDef region, MetricDef metric)
        {
            StringBuilder sb = new();
            for (int y = _yearRange.Start; y <= _yearRange.End; y++)
            {
                string cell = GetValue(region, y, metric);
                sb.Append(cell.PadLeft(8));
            }
            return sb.ToString();
        }

        private static string GetValue(RegionDef region, int year, MetricDef metric)
        {
            if (_dataNav == null) return "-";

            // XPath
            string xpath = $"/global_economies/region[@rid='{EscapeForXPath(region.Rid)}']/year[@yid='{year}']/{metric.Category}/@{metric.AttrName}";
            XPathNavigator? node = _dataNav.SelectSingleNode(xpath);
            if (node == null) return "-";

            string raw = node.Value;
            if (string.IsNullOrWhiteSpace(raw)) return "-";

            if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
            {
                return d.ToString("0.00", CultureInfo.InvariantCulture);
            }
            return "-";
        }

        private static string EscapeForXPath(string s)
        {
            // rid values are alphanumeric
            return s.Replace("'", "&apos;");
        }

        // -----------------------------------------------------------------
        //  Input Helpers
        // -----------------------------------------------------------------
        private static int PromptForInt(string prompt, int min, int max)
        {
            while (true)
            {
                Console.Write(prompt);
                string? input = Console.ReadLine();
                if (int.TryParse(input, out int value) && value >= min && value <= max)
                {
                    return value;
                }
                Console.WriteLine($"ERROR: Please enter an integer between {min} and {max}.");
            }
        }

        // Same as PromptForInt but used when we need custom error message (year end)
        private static int PromptForIntAllowMessage(string prompt)
        {
            while (true)
            {
                Console.Write(prompt);
                string? input = Console.ReadLine();
                if (int.TryParse(input, out int value))
                {
                    return value;
                }
                Console.WriteLine("ERROR: Please enter an integer value.");
            }
        }

        // -----------------------------------------------------------------
        //  Formatting Helpers
        // -----------------------------------------------------------------
        private static string PadLeftToWidth(string text, int width)
        {
            if (text.Length >= width) return text;
            return new string(' ', width - text.Length) + text;
        }

        private static string TrimToWidth(string text, int width, bool leftJustify)
        {
            if (text.Length <= width) return text;

            if (width > 1)
            {
                return text.Substring(0, width - 1) + "…"; // ellipsis
            }
            return text.Substring(0, width);
        }
    }
}


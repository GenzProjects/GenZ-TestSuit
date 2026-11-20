// GenZ-TestSuit-v1.0.1
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using UnityEditor;
using UnityEngine.Networking;

namespace GenZ.TestSuit
{
    /// <summary>
    /// Downloads a Google Sheet as CSV and looks up one game's IDs by package name.
    /// The sheet must be viewable by "Anyone with the link" (or published to the web).
    /// Supports both layouts: one row per game (headers in a row) or one column per game (labels in a column).
    /// </summary>
    public class GenZSheetReader
    {
        public string error;                       // set when download/lookup failed
        public List<List<string>> grid;            // raw CSV cells
        public Dictionary<string, string> gameValues; // normalized header -> value, for the matched game
        public List<string> headers = new List<string>(); // original header texts (for the UI hint)
        /// <summary>Every (header, value) of the matched game in sheet order, repeated headers included (the games tab has two "Store Name" columns).</summary>
        public List<KeyValuePair<string, string>> allValues = new List<KeyValuePair<string, string>>();

        public static string ToCsvUrl(string url)
        {
            url = url.Trim();
            if (url.Contains("output=csv") || url.Contains("format=csv")) return url;

            var gid = Regex.Match(url, @"[#&?]gid=(\d+)");
            var gidPart = gid.Success ? "&gid=" + gid.Groups[1].Value : "";

            // Published link: /spreadsheets/d/e/<id>/pubhtml
            var pub = Regex.Match(url, @"/spreadsheets/d/e/([A-Za-z0-9_-]+)");
            if (pub.Success)
                return $"https://docs.google.com/spreadsheets/d/e/{pub.Groups[1].Value}/pub?output=csv{gidPart}";

            // Normal share / edit link: /spreadsheets/d/<id>/edit#gid=0
            var id = Regex.Match(url, @"/spreadsheets/d/([A-Za-z0-9_-]+)");
            if (id.Success)
                return $"https://docs.google.com/spreadsheets/d/{id.Groups[1].Value}/export?format=csv{gidPart}";

            return url;
        }

        public List<string> tabsUsed = new List<string>(); // the tabs read, for the UI ("Android Games ID's", …)
        /// <summary>Tabs marked <see cref="SheetTab.keyOnly"/>: searched only by <see cref="FindUniqueCell"/> (iOS reads the shared MAX SDK key from the Android MAX tab).</summary>
        public List<List<string>> keyGrid = new List<List<string>>();

        /// <summary>
        /// Blocking download with a cancelable progress bar. With <paramref name="tabs"/>, every listed tab of the
        /// spreadsheet is downloaded and stacked into one grid (an empty row between them), so each table keeps its own
        /// "Package Name" header and every lookup works across tabs. Tabs are matched by name (ignoring case, spaces and
        /// symbols); a tab the spreadsheet doesn't have is skipped. No tab found (or none listed) = the tab in the link.
        /// </summary>
        public bool Download(string sheetUrl, IList<SheetTab> tabs = null)
        {
            tabsUsed.Clear();
            keyGrid = new List<List<string>>();
            try
            {
                var wanted = (tabs ?? new SheetTab[0]).Where(t => t != null && (!string.IsNullOrWhiteSpace(t.name) || !string.IsNullOrWhiteSpace(t.gid))).ToList();
                var id = Regex.Match(sheetUrl ?? "", @"/spreadsheets/d/([A-Za-z0-9_-]+)");
                if (wanted.Count > 0 && id.Success && !sheetUrl.Contains("/d/e/"))
                {
                    var listed = ListTabs(id.Groups[1].Value); // name -> gid, or null if the tab list couldn't be read
                    if (error == "Sheet download canceled.") return false;
                    var parts = new List<List<List<string>>>();
                    var keyParts = new List<List<List<string>>>();
                    foreach (var t in wanted)
                    {
                        string gid = null;
                        if (listed != null)
                        {
                            var hit = listed.FirstOrDefault(kv => Normalize(kv.Key) == Normalize(t.name));
                            if (hit.Key != null) gid = hit.Value;
                        }
                        else gid = string.IsNullOrWhiteSpace(t.gid) ? null : t.gid.Trim(); // tab list unavailable: trust the stored gid
                        if (gid == null) continue;
                        var part = DownloadCsv($"https://docs.google.com/spreadsheets/d/{id.Groups[1].Value}/export?format=csv&gid={gid}", t.name);
                        if (part == null) return false;
                        (t.keyOnly ? keyParts : parts).Add(part);
                        tabsUsed.Add((string.IsNullOrWhiteSpace(t.name) ? "gid " + gid : t.name.Trim()) + (t.keyOnly ? " (SDK key only)" : ""));
                    }
                    if (parts.Count > 0)
                    {
                        grid = Stack(parts);
                        keyGrid = Stack(keyParts);
                        return true;
                    }
                }

                grid = DownloadCsv(ToCsvUrl(sheetUrl), "Google Sheet");
                return grid != null;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        /// <summary>The tabs' grids one under the other, with an empty row between them so the tables stay apart.</summary>
        public static List<List<string>> Stack(IEnumerable<List<List<string>>> parts)
        {
            var all = new List<List<string>>();
            foreach (var p in parts)
            {
                if (all.Count > 0) all.Add(new List<string>());
                all.AddRange(p);
            }
            return all;
        }

        List<List<string>> DownloadCsv(string csvUrl, string what)
        {
            var text = Fetch(csvUrl, $"Downloading {what}…");
            if (text == null) return null;
            if (text.TrimStart().StartsWith("<"))
            {
                error = $"Google returned a web page instead of CSV for {what}. Share the sheet as \"Anyone with the link: Viewer\" or publish it to the web.";
                return null;
            }
            return ParseCsv(text);
        }

        /// <summary>The spreadsheet's tabs (name → gid), read from its public HTML view. Null if that fails.</summary>
        Dictionary<string, string> ListTabs(string spreadsheetId)
        {
            var html = Fetch($"https://docs.google.com/spreadsheets/d/{spreadsheetId}/htmlview", "Reading the sheet's tabs…", quiet: true);
            return html == null ? null : ParseTabList(html);
        }

        /// <summary>Tab name → gid from the spreadsheet's HTML view ({name: "Android Games ID\x27s", pageUrl: "…gid=0"}). Null if none.</summary>
        public static Dictionary<string, string> ParseTabList(string html)
        {
            var tabs = new Dictionary<string, string>();
            foreach (Match m in Regex.Matches(html, @"\{name:\s*""((?:[^""\\]|\\.)*)"",\s*pageUrl:\s*""[^""]*?gid=(\d+)"))
            {
                var name = Regex.Replace(m.Groups[1].Value, @"\\x([0-9A-Fa-f]{2})", x => ((char)Convert.ToInt32(x.Groups[1].Value, 16)).ToString())
                                .Replace("\\/", "/").Replace("\\\"", "\"");
                if (!tabs.ContainsKey(name)) tabs[name] = m.Groups[2].Value;
            }
            return tabs.Count > 0 ? tabs : null;
        }

        string Fetch(string url, string info, bool quiet = false)
        {
            using (var req = UnityWebRequest.Get(url))
            {
                req.timeout = 30;
                var op = req.SendWebRequest();
                while (!op.isDone)
                {
                    if (EditorUtility.DisplayCancelableProgressBar("GenZ Test Suit", info, req.downloadProgress))
                    {
                        req.Abort();
                        error = "Sheet download canceled.";
                        return null;
                    }
                    Thread.Sleep(20);
                }
                if (req.result == UnityWebRequest.Result.Success) return req.downloadHandler.text;
                if (!quiet) error = $"Sheet download failed: {req.error} ({url})";
                return null;
            }
        }

        /// <summary>
        /// Finds every table that lists the package and merges their values into <see cref="gameValues"/>.
        /// The studio sheet has a games table and a MAX table (on two tabs, stacked by Download), each with its own Package Name column.
        /// </summary>
        public bool SelectGame(string packageName, string[] packageColumnNames)
        {
            var pkg = packageName.Trim();
            var pkgHeaders = new HashSet<string>(packageColumnNames.Select(Normalize));
            gameValues = new Dictionary<string, string>();
            headers.Clear();
            allValues.Clear();
            int tables = 0;

            for (int r = 0; r < grid.Count; r++)
            for (int c = 0; c < grid[r].Count; c++)
            {
                if (!string.Equals(grid[r][c].Trim(), pkg, StringComparison.OrdinalIgnoreCase)) continue;
                tables++;

                // Row layout: a row above has a "Package Name" header in this column.
                bool placed = false;
                for (int h = r - 1; h >= 0 && !placed; h--)
                    if (pkgHeaders.Contains(Normalize(Cell(h, c))))
                    {
                        var (from, to) = Block(i => Cell(h, i), c);
                        for (int i = from; i <= to; i++) Add(Cell(h, i), Cell(r, i));
                        placed = true;
                    }

                // Column layout: a column to the left has a "Package Name" label in this row.
                for (int h = c - 1; h >= 0 && !placed; h--)
                    if (pkgHeaders.Contains(Normalize(Cell(r, h))))
                    {
                        var (from, to) = Block(i => i < grid.Count ? Cell(i, h) : "", r);
                        for (int i = from; i <= to; i++) Add(Cell(i, h), Cell(i, c));
                        placed = true;
                    }

                // No header found: assume the first row holds the headers.
                if (!placed)
                    for (int i = 0; i < grid[0].Count; i++) Add(Cell(0, i), Cell(r, i));
            }

            if (tables > 0) return true;
            error = $"Package \"{pkg}\" was not found in the sheet.";
            return false;
        }

        static readonly Regex PackagePattern = new Regex(@"^[A-Za-z][A-Za-z0-9_]*(\.[A-Za-z0-9_]+)+$");

        /// <summary>
        /// Every game in the sheet: the package-name cells under each "Package Name" header (row layout), with the
        /// game's name from a name column in the same table when there is one. Distinct by package, in sheet order.
        /// </summary>
        public List<KeyValuePair<string, string>> ListGames(string[] packageColumnNames, string[] nameColumnNames)
        {
            var pkgHeaders = new HashSet<string>(packageColumnNames.Select(Normalize));
            var nameHeaders = new HashSet<string>(nameColumnNames.Select(Normalize));
            var games = new List<KeyValuePair<string, string>>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int h = 0; h < grid.Count; h++)
            for (int c = 0; c < grid[h].Count; c++)
            {
                if (!pkgHeaders.Contains(Normalize(Cell(h, c)))) continue;
                var (from, to) = Block(i => Cell(h, i), c);
                int nameCol = -1;
                for (int i = from; i <= to && nameCol < 0; i++)
                    if (nameHeaders.Contains(Normalize(Cell(h, i)))) nameCol = i;
                for (int r = h + 1; r < grid.Count; r++)
                {
                    var pkg = Cell(r, c).Trim();
                    if (pkgHeaders.Contains(Normalize(pkg))) break; // the next table starts
                    if (!PackagePattern.IsMatch(pkg)) continue;
                    if (!seen.Add(pkg))
                    {
                        // seen in the other table: keep the first, but take its name if the first had none
                        int k = games.FindIndex(g => string.Equals(g.Key, pkg, StringComparison.OrdinalIgnoreCase));
                        if (k >= 0 && string.IsNullOrEmpty(games[k].Value) && nameCol >= 0)
                            games[k] = new KeyValuePair<string, string>(games[k].Key, Cell(r, nameCol).Trim());
                        continue;
                    }
                    games.Add(new KeyValuePair<string, string>(pkg, nameCol >= 0 ? Cell(r, nameCol).Trim() : ""));
                }
            }
            return games;
        }

        /// <summary>Looks up a value by any of the given column names (exact match, ignoring case/spaces/symbols). Null when no column matches.</summary>
        public string Lookup(string[] columnNames, out string matchedColumn)
        {
            matchedColumn = null;
            if (gameValues == null || columnNames == null) return null;
            foreach (var name in columnNames)
                if (gameValues.TryGetValue(Normalize(name), out var v)) { matchedColumn = name; return v; }
            return null;
        }

        /// <summary>Every value under any of these column names, in sheet order (a header used twice gives two values).</summary>
        public List<KeyValuePair<string, string>> LookupAll(string[] columnNames)
        {
            var wanted = new HashSet<string>((columnNames ?? new string[0]).Select(Normalize));
            return allValues.Where(kv => wanted.Contains(Normalize(kv.Key))).ToList();
        }

        /// <summary>The single distinct cell anywhere in the sheet matching the pattern (e.g. the shared MAX SDK key), else null.</summary>
        public string FindUniqueCell(Regex pattern)
        {
            var hits = grid.Concat(keyGrid ?? new List<List<string>>()).SelectMany(row => row).Select(v => v.Trim()).Where(v => pattern.IsMatch(v)).Distinct().ToList();
            return hits.Count == 1 ? hits[0] : null;
        }

        // Contiguous non-empty header cells around index `at` — keeps neighbouring tables out.
        static (int from, int to) Block(Func<int, string> header, int at)
        {
            int from = at, to = at;
            while (from > 0 && header(from - 1).Trim().Length > 0) from--;
            while (header(to + 1).Trim().Length > 0) to++;
            return (from, to);
        }

        void Add(string header, string value)
        {
            var key = Normalize(header);
            if (key.Length > 0) allValues.Add(new KeyValuePair<string, string>(header.Trim(), value.Trim()));
            if (key.Length == 0 || gameValues.ContainsKey(key)) return; // first table wins on shared headers (Game Name…)
            headers.Add(header.Trim());
            gameValues[key] = value.Trim();
        }

        string Cell(int r, int c) => r < grid.Count ? Get(grid[r], c) : "";
        static string Get(List<string> list, int i) => i < list.Count ? list[i] : "";

        public static string Normalize(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length);
            foreach (var ch in s)
                if (char.IsLetterOrDigit(ch)) sb.Append(char.ToLowerInvariant(ch));
            return sb.ToString();
        }

        public static List<List<string>> ParseCsv(string text)
        {
            var rows = new List<List<string>>();
            var row = new List<string>();
            var cell = new StringBuilder();
            bool quoted = false;

            for (int i = 0; i < text.Length; i++)
            {
                char ch = text[i];
                if (quoted)
                {
                    if (ch == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; }
                        else quoted = false;
                    }
                    else cell.Append(ch);
                }
                else if (ch == '"') quoted = true;
                else if (ch == ',') { row.Add(cell.ToString()); cell.Clear(); }
                else if (ch == '\n' || ch == '\r')
                {
                    if (ch == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                    row.Add(cell.ToString()); cell.Clear();
                    rows.Add(row); row = new List<string>();
                }
                else cell.Append(ch);
            }
            if (cell.Length > 0 || row.Count > 0) { row.Add(cell.ToString()); rows.Add(row); }
            return rows;
        }
    }
}

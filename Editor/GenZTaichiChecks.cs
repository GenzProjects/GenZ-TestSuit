// GenZ-TestSuit-v1.0.1
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using static GenZ.TestSuit.GenZSourceModel;

namespace GenZ.TestSuit
{
    /// <summary>What the Editor runner read for the Taichi step (scene switches and the sheet value).</summary>
    public class TaichiInput
    {
        public bool sceneRead;               // the manager component was found in the SDK scene
        public bool? taichi1, taichi2;       // switch values; null = switch field not found
        public string taichi1Flag, taichi2Flag;
        public string where, file, note;     // "Splash › Ads (FirebaseManager)", scene path, why not read
        public bool sheetRead;
        public string sheetCount, sheetColumn; // Taichi 2.0 impression count from the sheet (null = no column)
        public string taichi1Default, taichi2Default;   // defaults set in the Inspector (RemoteConfigManager); null = not found
        public string defaultWhere, defaultFile;        // "Splash › ADs (RemoteConfigManager)", scene path
        public string defaultObjectPath, defaultComponent, taichi2DefaultProperty; // where Auto Apply writes the sheet's count
        // No Inspector field: the Taichi 2.0 count written in code (the key's Remote Config default, or the field's starting value).
        public string taichi2CodeFile;       // Assets/... ; null = not a plain number in code
        public int taichi2CodeLine;
        public string taichi2CodeValue;      // the number as written, e.g. "50L"
        public string taichi2CodeContext;    // its line up to the end of the number, exactly as in the file
    }

    /// <summary>
    /// Step 3 · Taichi: Taichi 1.0 adds up ad revenue and sends a Firebase event (custom_ad_impression) when it reaches
    /// the Remote Config threshold (custom_rev); Taichi 2.0 sends an event (count_ad_impression) every N impressions
    /// (custom_imp). Each version must be switched on (if required) and correctly wired: event with value + currency,
    /// called from every revenue handler, progress saved and reset, threshold from Remote Config, read safely.
    /// </summary>
    public static partial class GenZCodeChecks
    {
        static readonly Regex LogEventCall = new Regex(@"\bLogEvent\s*\(");

        public static void RunTaichi(TestReport report, CodeCheckSettings cs, TaichiSettings ts, TaichiInput input, GenZSourceModel model)
        {
            var c = MakeCtx(cs, model);
            var sw = report.Section("Taichi · Switches", TestStage.Taichi);
            Switch(sw, "Taichi 1.0", input.taichi1, input.taichi1Flag, ts.taichi1Flags, ts.requireTaichi1, input);
            Switch(sw, "Taichi 2.0", input.taichi2, input.taichi2Flag, ts.taichi2Flags, ts.requireTaichi2, input);
            if (input.taichi1 == false && input.taichi2 == false)
                sw.Add("At least one version on", CheckStatus.Fail, "both off", "No Taichi event is ever sent — switch Taichi 2.0 (or 1.0) on", input.file, 0);

            var handlers = RevenueHandlers(c).Where(h => h.handler != null).ToList();
            Version(report.Section($"Taichi 1.0 · revenue reaches \"{ts.taichi1Key}\" → {ts.taichi1Event}", TestStage.Taichi), c, cs, ts,
                "Taichi 1.0", input.taichi1, ts.requireTaichi1, ts.taichi1Event, ts.taichi1Key, ts.taichi1Flags, false, input, handlers);
            Version(report.Section($"Taichi 2.0 · every \"{ts.taichi2Key}\" impressions → {ts.taichi2Event}", TestStage.Taichi), c, cs, ts,
                "Taichi 2.0", input.taichi2, ts.requireTaichi2, ts.taichi2Event, ts.taichi2Key, ts.taichi2Flags, true, input, handlers);
        }

        static void Switch(ReportSection sec, string name, bool? on, string flag, string[] flags, bool required, TaichiInput input)
        {
            string label = $"{name} switched on";
            if (!input.sceneRead)
                sec.Add(label, CheckStatus.Warn, "", "Couldn't read the switch — " + (input.note ?? "the SDK scene wasn't read"));
            else if (on == null)
                sec.Add(label, CheckStatus.Warn, "", $"No switch field on {input.where} (tried {string.Join(", ", flags)}) — add its name to the Taichi settings");
            else if (on == true)
                sec.Add(label, CheckStatus.Pass, flag + " = true", "On · " + input.where);
            else
                sec.Add(label, required ? CheckStatus.Fail : CheckStatus.Info, flag + " = false",
                    required ? $"Off — {name} events are never sent. Turn {flag} on ({input.where})"
                             : $"Off (optional) — {name} events aren't sent · {input.where}");
        }

        static void Version(ReportSection sec, Ctx c, CodeCheckSettings cs, TaichiSettings ts, string name, bool? on, bool required,
            string evt, string key, string[] flags, bool countBased, TaichiInput input, List<(string label, MethodInfo handler, string missing, MethodInfo near)> handlers)
        {
            if (on == false && !required)
            {
                sec.Add($"{name} logic", CheckStatus.Info, "", "Switched off — logic not checked");
                return;
            }

            // 1. The method that sends the event.
            var m = c.adMethods.FirstOrDefault(x => x.CodeWithStrings.Contains("\"" + evt + "\"") && LogEventCall.IsMatch(x.Code));
            if (m == null)
            {
                sec.Add($"Sends \"{evt}\"", CheckStatus.Fail, "", $"No ad/analytics script sends the Firebase event \"{evt}\" — {name} isn't implemented");
                return;
            }
            int evtLine = m.File.LineOf(m.bodyStart + m.CodeWithStrings.IndexOf("\"" + evt + "\"", StringComparison.Ordinal));
            sec.Add($"Sends \"{evt}\"", CheckStatus.Pass, m.FullName, "FirebaseAnalytics.LogEvent(\"" + evt + "\")", m.File.path, evtLine);

            // 2. Value + currency, so Firebase / Google Ads can use it as a revenue event.
            bool value = Regex.IsMatch(m.CodeWithStrings, @"\bParameterValue\b|""value""");
            bool currency = Regex.IsMatch(m.CodeWithStrings, @"\bParameterCurrency\b|""currency""");
            sec.Add("Event has value + currency", value && currency ? CheckStatus.Pass : CheckStatus.Fail, m.FullName,
                value && currency ? "value and currency parameters are sent"
                    : "Missing " + string.Join(" and ", new[] { value ? null : "value", currency ? null : "currency" }.Where(x => x != null)) + " — the event can't be used for value-based bidding",
                m.File.path, evtLine);

            // 3. Gated by its switch.
            var flagRx = new Regex(@"\b(" + string.Join("|", flags.Select(Regex.Escape)) + @")\b");
            var gate = flagRx.Match(m.Code);
            sec.Add("Respects its switch", gate.Success ? CheckStatus.Pass : CheckStatus.Warn, gate.Success ? gate.Value : "",
                gate.Success ? $"Returns early when {gate.Value} is off" : "Doesn't check the switch — it runs even when switched off",
                m.File.path, gate.Success ? m.File.LineOf(m.bodyStart + gate.Index) : m.Line);

            // 4. Called from every format's revenue (paid) handler.
            if (handlers.Count == 0)
                sec.Add("Called on every ad revenue", CheckStatus.Warn, m.name + "()", "No revenue handlers found to check");
            else
            {
                var callRx = new Regex(@"\b" + Regex.Escape(m.name) + @"\s*\(");
                var missing = handlers.Where(h => !ReachesText(c, h.handler, callRx, 2)).Select(h => h.label).ToList();
                sec.Add("Called on every ad revenue", missing.Count == 0 ? CheckStatus.Pass : CheckStatus.Fail, m.name + "()",
                    missing.Count == 0 ? $"All {handlers.Count} revenue handlers call it (MAX and AdMob)"
                        : $"Not called from: {string.Join(", ", missing)} — that revenue never counts towards {name}",
                    m.File.path, m.Line);
            }

            // 5. Progress kept across sessions, and reset after the event.
            bool saved = Regex.IsMatch(m.Code, @"\bPlayerPrefs\s*\.\s*Set\w*\s*\(");
            sec.Add("Progress saved between sessions", saved ? CheckStatus.Pass : CheckStatus.Warn, "",
                saved ? "Accumulated " + (countBased ? "count and revenue are" : "revenue is") + " stored in PlayerPrefs"
                      : "Nothing is saved — progress is lost every time the app closes", m.File.path, m.Line);
            int logIdx = m.CodeWithStrings.IndexOf("\"" + evt + "\"", StringComparison.Ordinal);
            var after = logIdx >= 0 ? m.CodeWithStrings.Substring(logIdx) : "";
            bool reset = Regex.IsMatch(after, @"Set\w*\s*\([^;]*,\s*""?0(\.0)?""?\s*\)|\b\w+\s*=\s*0(\.0)?\s*;");
            sec.Add("Reset after sending", reset ? CheckStatus.Pass : CheckStatus.Warn, "",
                reset ? "Starts counting again from 0 after the event" : "Not reset after the event — it would fire on every ad from then on", m.File.path, m.Line);

            // 6. Threshold from Remote Config, read safely.
            RemoteThreshold(sec, c, cs, ts, name, m, key, countBased, input);
        }

        static void RemoteThreshold(ReportSection sec, Ctx c, CodeCheckSettings cs, TaichiSettings ts, string name, MethodInfo m, string key, bool countBased, TaichiInput input)
        {
            var rcClasses = RemoteConfigClasses(c.model, cs);
            var readRx = new Regex(@"\b(\w+)\s*=(?!=)[^;]*GetValue\s*\(\s*""" + Regex.Escape(key) + @"""\s*\)[^;]*;");
            MethodInfo rm = null; Match read = null;
            foreach (var x in rcClasses.SelectMany(k => k.methods))
            {
                var mm = readRx.Match(x.CodeWithStrings);
                if (mm.Success) { rm = x; read = mm; break; }
            }
            if (read == null)
            {
                sec.Add($"Threshold from Remote Config \"{key}\"", CheckStatus.Fail, "",
                    $"\"{key}\" is never read in {string.Join("/", cs.remoteConfigClasses)} — {name} always uses the code default");
                return;
            }

            // Follow one hop: e.g. revenue_maxValue_St = GetValue("custom_rev")…; then either
            // revenue_maxValue = double.Parse(revenue_maxValue_St);  or  double.TryParse(revenue_maxValue_St, …, out revenue_maxValue)
            var target = read.Groups[1].Value;
            var statements = new List<Match> { read };
            string used = ReferenceRegex(target).IsMatch(m.Code) ? target : null;
            if (used == null)
            {
                var t = Regex.Escape(target);
                var assign = new Regex(@"\b(\w+)\s*=(?!=)[^;]*\b" + t + @"\b[^;]*;").Match(rm.CodeWithStrings, read.Index + read.Length);
                var tryOut = new Regex(@"\bTryParse\s*\([^;]*\b" + t + @"\b[^;]*?\bout\s+(?:(?:var|double|float|int|long|decimal)\s+)?(\w+)[^;]*;").Match(rm.CodeWithStrings, read.Index + read.Length);
                var hop = !tryOut.Success || (assign.Success && assign.Index < tryOut.Index) ? assign : tryOut;
                if (hop.Success)
                {
                    statements.Add(hop);
                    if (ReferenceRegex(hop.Groups[1].Value).IsMatch(m.Code)) used = hop.Groups[1].Value;
                }
            }
            int readLine = rm.File.LineOf(rm.bodyStart + read.Index);
            sec.Add($"Threshold from Remote Config \"{key}\"", used != null ? CheckStatus.Pass : CheckStatus.Warn, used ?? target,
                used != null ? $"\"{key}\" → {used}, used by {m.FullName}" : $"\"{key}\" is read into {target}, but {m.FullName} doesn't use it",
                rm.File.path, readLine);

            // Read safely: Parse without InvariantCulture misreads "0.05" on comma-decimal phones; Parse also throws on a missing key.
            var text = string.Join(" ", statements.Select(s => s.Value));
            var parse = statements.FirstOrDefault(s => Regex.IsMatch(s.Value, @"\b(Try)?Parse\s*\("));
            if (parse != null)
            {
                bool invariant = text.Contains("InvariantCulture");
                bool tryParse = Regex.IsMatch(parse.Value, @"\bTryParse\s*\(");
                int line = rm.File.LineOf(rm.bodyStart + parse.Index);
                if (!invariant)
                    sec.Add("Read safely on every phone", CheckStatus.Fail, Short(parse.Value.Trim(), 60),
                        "Parsed without CultureInfo.InvariantCulture: on phones set to a comma-decimal language (German, French, Turkish…) \"0.05\" is read as 5 or throws, so the threshold is wrong. Use double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out …)",
                        rm.File.path, line);
                else if (!tryParse)
                    sec.Add("Read safely on every phone", CheckStatus.Warn, Short(parse.Value.Trim(), 60),
                        "Parse throws when the key is missing or not a number, which skips the rest of the method — use TryParse", rm.File.path, line);
                else
                    sec.Add("Read safely on every phone", CheckStatus.Pass, Short(parse.Value.Trim(), 60), "TryParse with InvariantCulture", rm.File.path, line);
            }

            // Missing key: LongValue / parsed value 0 → the event fires on every ad.
            var allRc = string.Concat(rcClasses.Select(k => k.file.text.Substring(k.bodyStart, k.bodyEnd - k.bodyStart)));
            var defMatch = Regex.Match(allRc, @"[{\[]\s*""" + Regex.Escape(key) + @"""\s*[,\]]\s*=?\s*([^,}\]]+)");
            bool guarded = Regex.IsMatch(text, @">\s*0") || (used != null && Regex.IsMatch(rm.Code, @"\b" + Regex.Escape(used) + @"\s*>\s*0"));
            // Or a fallback: if (!TryParse(…, out x) || x <= 0) x = 0.1;
            bool fallback = used != null && Regex.IsMatch(text, @"\b" + Regex.Escape(used) + @"\s*<=\s*0[^;]*\)\s*\{?\s*" + Regex.Escape(used) + @"\s*=\s*[\d.]+[fFdDmM]?\s*;");
            sec.Add($"Safe if \"{key}\" is missing", defMatch.Success || guarded || fallback ? CheckStatus.Pass : CheckStatus.Warn, key,
                defMatch.Success ? "Has a Remote Config default" : guarded ? "Only applied when > 0" : fallback ? "Falls back to a default when missing, unreadable or ≤ 0"
                    : countBased ? $"No Remote Config default and no > 0 check — if \"{key}\" is missing it becomes 0 and {ts.taichi2Event} fires on every impression"
                                 : $"No Remote Config default — if \"{key}\" is missing the threshold is 0 (or the read throws) and {ts.taichi1Event} fires on every ad",
                rm.File.path, readLine);

            // The default the developer set in the Inspector (RemoteConfigManager on the Ads object).
            var inspector = countBased ? input.taichi2Default : input.taichi1Default;
            if (inspector != null)
                sec.Add("Default in the Inspector", CheckStatus.Info, inspector,
                    $"{input.defaultWhere}: used until Remote Config loads and whenever \"{key}\" is missing", input.defaultFile, 0);

            // Taichi 2.0: the impression count the game starts with must equal the sheet's value for this game.
            if (!countBased || !input.sheetRead) return;
            var field = used ?? target;
            if (input.taichi2Default == null) FindCodeDefault(c, rcClasses, key, field, input);
            string code = defMatch.Success ? ResolveDefault(c.model, defMatch.Groups[1].Value) : null, where = "Remote Config default";
            int defLine = readLine; string defFile = rm.File.path;
            if (input.taichi2Default != null)
            {
                code = input.taichi2Default; where = $"Inspector default ({input.defaultWhere})"; defFile = input.defaultFile; defLine = 0;
            }
            if (code == null)
                foreach (var k in c.model.classes)
                {
                    var d = Regex.Match(k.Body, @"\b(?:int|long|float|double)\s+" + Regex.Escape(field) + @"\s*=\s*([^;]+);");
                    if (!d.Success) continue;
                    code = d.Groups[1].Value; where = $"{k.name}.{field} initial value"; defFile = k.file.path; defLine = k.file.LineOf(k.bodyStart + d.Index);
                    break;
                }
            if (input.sheetCount == null)
                sec.Add("Impression count = sheet", CheckStatus.Info, code?.Trim() ?? "", "The sheet has no Taichi 2.0 column (tried " + string.Join(", ", ts.taichi2SheetColumns) + ")");
            else if (input.sheetCount.Trim().Length == 0)
                sec.Add("Impression count = sheet", CheckStatus.Warn, code?.Trim() ?? "", $"The sheet's \"{input.sheetColumn}\" cell is empty for this game");
            else if (code == null)
                sec.Add("Impression count = sheet", CheckStatus.Warn, "", $"Couldn't find the default of {field} — the sheet says {input.sheetCount.Trim()}");
            else
            {
                bool same = double.TryParse(Num(code), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var a)
                         && double.TryParse(Num(input.sheetCount), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var b)
                         && Math.Abs(a - b) < 1e-9;
                sec.Add("Impression count = sheet", same ? CheckStatus.Pass : CheckStatus.Fail, Num(code),
                    same ? $"{where} {Num(code)} matches the sheet's \"{input.sheetColumn}\""
                         : $"{where} is {Num(code)}, the sheet's \"{input.sheetColumn}\" says {Num(input.sheetCount)} — used until Remote Config loads and whenever \"{key}\" is missing"
                           + (input.taichi2Default != null ? ". Auto Apply All IDs from Sheet sets it"
                              : input.taichi2CodeFile != null ? $". Auto Apply All IDs from Sheet writes it in {input.taichi2CodeFile}:{input.taichi2CodeLine}" : ""),
                    defFile, defLine);
            }
        }

        static readonly Regex NumberLiteral = new Regex(@"^-?\d+(\.\d+)?[lLuUfFdDmM]{0,2}$");

        /// <summary>
        /// Where the Taichi 2.0 count is written in code, found the same way the check reads it: the key's Remote Config
        /// default ({ "custom_imp", 50L }), the field that default names, or else the starting value of the field the
        /// key is read into. Only a plain number is recorded (an expression can't be rewritten safely).
        /// </summary>
        static void FindCodeDefault(Ctx c, List<ClassInfo> rcClasses, string key, string field, TaichiInput input)
        {
            SourceFile file = null;
            int start = -1, length = 0;
            string name = field;
            foreach (var k in rcClasses)
            {
                var body = k.file.text.Substring(k.bodyStart, k.bodyEnd - k.bodyStart);
                var d = Regex.Match(body, @"[{\[]\s*""" + Regex.Escape(key) + @"""\s*[,\]]\s*=?\s*([^,}\]]+)");
                if (!d.Success) continue;
                var expr = d.Groups[1].Value.TrimEnd();
                if (NumberLiteral.IsMatch(expr)) { file = k.file; start = k.bodyStart + d.Groups[1].Index; length = expr.Length; }
                else
                {
                    var id = Regex.Match(expr, @"^(?:\w+\.)?([A-Za-z_]\w*)$");
                    if (!id.Success) return; // an expression: leave it to the developer
                    name = id.Groups[1].Value;
                }
                break;
            }
            if (file == null)
                foreach (var k in c.model.classes)
                {
                    var d = Regex.Match(k.Body, @"\b(?:int|long|float|double)\s+" + Regex.Escape(name) + @"\s*=\s*([^;,]+)[;,]");
                    if (!d.Success) continue;
                    var expr = k.file.text.Substring(k.bodyStart + d.Groups[1].Index, d.Groups[1].Length).TrimEnd();
                    if (NumberLiteral.IsMatch(expr)) { file = k.file; start = k.bodyStart + d.Groups[1].Index; length = expr.Length; }
                    break;
                }
            if (file == null) return;

            int lineStart = file.text.LastIndexOf('\n', start) + 1;
            input.taichi2CodeFile = file.path;
            input.taichi2CodeLine = file.LineOf(start);
            input.taichi2CodeValue = file.text.Substring(start, length);
            input.taichi2CodeContext = file.text.Substring(lineStart, start + length - lineStart);
        }

        static string Num(string s) => Regex.Replace(s ?? "", @"[^\d.\-]", "");
    }
}

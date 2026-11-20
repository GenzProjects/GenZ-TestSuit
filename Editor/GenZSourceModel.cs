// GenZ-TestSuit-v1.0.1
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace GenZ.TestSuit
{
    /// <summary>
    /// A light, Roslyn-free model of the project's C# scripts: classes, methods and name references.
    /// Good enough for pattern checks on ad code (who calls what, what a method body contains).
    /// Comments are removed; string contents are masked for structure parsing but kept for Invoke("Name") lookups.
    /// </summary>
    public class GenZSourceModel
    {
        public class SourceFile
        {
            public string path;      // Assets/...
            public string text;      // comments blanked out (same length/lines as the original)
            public string masked;    // comments and string contents blanked out
            public int[] lineStarts;

            public int LineOf(int index)
            {
                int i = Array.BinarySearch(lineStarts, index);
                return (i >= 0 ? i : ~i - 1) + 1;
            }
        }

        public class ClassInfo
        {
            public string name;
            public SourceFile file;
            public int bodyStart, bodyEnd; // inside the braces
            public readonly List<MethodInfo> methods = new List<MethodInfo>();
            public string Body => file.masked.Substring(bodyStart, bodyEnd - bodyStart);
        }

        public class MethodInfo
        {
            public string name;
            public ClassInfo owner;
            public int headerStart, bodyStart, bodyEnd;
            public bool isPublic;

            public SourceFile File => owner.file;
            /// <summary>Body with strings masked — use for code patterns.</summary>
            public string Code => owner.file.masked.Substring(bodyStart, bodyEnd - bodyStart);
            /// <summary>Body with strings kept — use for Invoke("Name") / GetValue("key").</summary>
            public string CodeWithStrings => owner.file.text.Substring(bodyStart, bodyEnd - bodyStart);
            public int Line => owner.file.LineOf(headerStart);
            public string Where => $"{owner.file.path}:{Line}";
            public string FullName => owner.name + "." + name;
            public override string ToString() => FullName;
        }

        public readonly List<SourceFile> files = new List<SourceFile>();
        public readonly List<ClassInfo> classes = new List<ClassInfo>();
        public IEnumerable<MethodInfo> Methods => classes.SelectMany(c => c.methods);

        static readonly HashSet<string> NotMethods = new HashSet<string>
        {
            "if", "for", "foreach", "while", "switch", "catch", "using", "lock", "fixed", "return", "new", "else",
            "get", "set", "add", "remove", "nameof", "typeof", "sizeof", "default", "checked", "unchecked", "when", "base", "this"
        };

        static readonly Regex ClassRegex = new Regex(@"\b(class|struct)\s+([A-Za-z_]\w*)[^{;]*\{");
        static readonly Regex MethodRegex = new Regex(@"([A-Za-z_]\w*)\s*(<[^<>()]*>)?\s*\(([^;{}()]|\([^;{}()]*\))*\)\s*(where\s[^{;]*)?(\{|=>)");

        /// <summary>Loads every .cs under Assets except excluded folders, Editor folders and this tool.</summary>
        public static GenZSourceModel Load(IEnumerable<string> excludeFolders)
        {
            var model = new GenZSourceModel();
            var excludes = excludeFolders.Select(e => "/" + e.Trim('/').ToLowerInvariant() + "/").ToList();

            foreach (var full in Directory.GetFiles("Assets", "*.cs", SearchOption.AllDirectories))
            {
                var path = full.Replace('\\', '/');
                var lower = "/" + path.ToLowerInvariant();
                if (lower.Contains("/editor/") || excludes.Any(lower.Contains)) continue;
                model.Add(path, File.ReadAllText(full));
            }
            return model;
        }

        public void Add(string path, string source)
        {
            var f = new SourceFile { path = path };
            Strip(source, out f.text, out f.masked);
            var starts = new List<int> { 0 };
            for (int i = 0; i < source.Length; i++) if (source[i] == '\n') starts.Add(i + 1);
            f.lineStarts = starts.ToArray();
            files.Add(f);
            ParseClasses(f);
        }

        // ---------------------------------------------------------------- queries

        public ClassInfo Class(params string[] names) =>
            classes.FirstOrDefault(c => names.Any(n => string.Equals(c.name, n, StringComparison.OrdinalIgnoreCase)));

        public IEnumerable<ClassInfo> Classes(IEnumerable<string> names)
        {
            var set = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
            return classes.Where(c => set.Contains(c.name));
        }

        /// <summary>Methods whose body mentions the name as a call, delegate, nameof() or Invoke("name").</summary>
        public IEnumerable<MethodInfo> Referencing(MethodInfo target, IEnumerable<MethodInfo> scope = null)
        {
            var rx = ReferenceRegex(target.name);
            foreach (var m in scope ?? Methods)
                if (m != target && rx.IsMatch(m.CodeWithStrings)) yield return m;
        }

        // Any mention except being assigned to ("Name = x"): calls, method groups, nameof(Name), Invoke("Name").
        public static Regex ReferenceRegex(string name)
        {
            if (!RefCache.TryGetValue(name, out var rx))
                RefCache[name] = rx = new Regex(@"\b" + Regex.Escape(name) + @"\b(?!\s*=(?!=|>))", RegexOptions.Compiled);
            return rx;
        }
        static readonly Dictionary<string, Regex> RefCache = new Dictionary<string, Regex>();

        /// <summary>Methods of the given classes that the method references by name (one level).</summary>
        public IEnumerable<MethodInfo> Callees(MethodInfo m, IEnumerable<ClassInfo> scope)
        {
            var code = m.CodeWithStrings;
            foreach (var c in scope)
            foreach (var other in c.methods)
                if (other != m && ReferenceRegex(other.name).IsMatch(code))
                    yield return other;
        }

        /// <summary>True if the method body, or a method it references in scope (up to depth), matches the pattern.</summary>
        public bool Reaches(MethodInfo m, Regex pattern, IEnumerable<ClassInfo> scope, int depth, out MethodInfo where)
        {
            var seen = new HashSet<MethodInfo>();
            return Reaches(m, pattern, scope.ToList(), depth, seen, out where);
        }

        bool Reaches(MethodInfo m, Regex pattern, List<ClassInfo> scope, int depth, HashSet<MethodInfo> seen, out MethodInfo where)
        {
            where = null;
            if (!seen.Add(m)) return false;
            if (pattern.IsMatch(m.Code)) { where = m; return true; }
            if (depth <= 0) return false;
            foreach (var callee in Callees(m, scope))
                if (Reaches(callee, pattern, scope, depth - 1, seen, out where)) return true;
            return false;
        }

        // ---------------------------------------------------------------- parsing

        void ParseClasses(SourceFile f)
        {
            var depth = Depths(f.masked);
            foreach (Match cm in ClassRegex.Matches(f.masked))
            {
                int open = cm.Index + cm.Length - 1;
                int close = MatchBrace(f.masked, open);
                if (close < 0) continue;
                var ci = new ClassInfo { name = cm.Groups[2].Value, file = f, bodyStart = open + 1, bodyEnd = close };
                int inner = depth[open + 1];

                var body = f.masked.Substring(ci.bodyStart, ci.bodyEnd - ci.bodyStart);
                foreach (Match mm in MethodRegex.Matches(body))
                {
                    int headerAbs = ci.bodyStart + mm.Index;
                    if (depth[headerAbs] != inner) continue; // nested in another member or class
                    var name = mm.Groups[1].Value;
                    if (NotMethods.Contains(name)) continue;

                    // Skip constructor calls / expressions like "x = Foo(...) {": the token before the name must end a type or modifier.
                    var before = PrecedingToken(f.masked, headerAbs);
                    if (before == "=" || before == "." || before == "new" || before == "return") continue;

                    var arrow = mm.Groups[5].Value == "=>";
                    int bodyStart, bodyEnd;
                    if (arrow)
                    {
                        bodyStart = ci.bodyStart + mm.Index + mm.Length;
                        bodyEnd = f.masked.IndexOf(';', bodyStart);
                        if (bodyEnd < 0) continue;
                    }
                    else
                    {
                        int ob = ci.bodyStart + mm.Index + mm.Length - 1;
                        int cb = MatchBrace(f.masked, ob);
                        if (cb < 0) continue;
                        bodyStart = ob + 1;
                        bodyEnd = cb;
                    }

                    var lineStart = f.masked.LastIndexOfAny(new[] { ';', '}', '{', ']' }, Math.Max(0, headerAbs - 1)) + 1;
                    var modifiers = f.masked.Substring(lineStart, headerAbs - lineStart);
                    ci.methods.Add(new MethodInfo
                    {
                        name = name, owner = ci, headerStart = headerAbs, bodyStart = bodyStart, bodyEnd = bodyEnd,
                        isPublic = Regex.IsMatch(modifiers, @"\bpublic\b")
                    });
                }
                classes.Add(ci);
            }
        }

        static string PrecedingToken(string s, int index)
        {
            int i = index - 1;
            while (i >= 0 && char.IsWhiteSpace(s[i])) i--;
            if (i < 0) return "";
            if (!char.IsLetterOrDigit(s[i]) && s[i] != '_') return s[i].ToString();
            int end = i + 1;
            while (i >= 0 && (char.IsLetterOrDigit(s[i]) || s[i] == '_')) i--;
            return s.Substring(i + 1, end - i - 1);
        }

        public static int MatchBrace(string s, int open)
        {
            int d = 0;
            for (int i = open; i < s.Length; i++)
            {
                if (s[i] == '{') d++;
                else if (s[i] == '}' && --d == 0) return i;
            }
            return -1;
        }

        static int[] Depths(string s)
        {
            var depth = new int[s.Length + 1];
            int d = 0;
            for (int i = 0; i < s.Length; i++)
            {
                depth[i] = d;
                if (s[i] == '{') d++;
                else if (s[i] == '}') d--;
            }
            depth[s.Length] = d;
            return depth;
        }

        /// <summary>Blanks comments (both outputs) and string/char contents (masked only), preserving length and line breaks.</summary>
        static void Strip(string src, out string text, out string masked)
        {
            var t = new StringBuilder(src);
            var m = new StringBuilder(src);
            int i = 0, n = src.Length;
            void Blank(StringBuilder sb, int from, int to) { for (int k = from; k < to; k++) if (sb[k] != '\n' && sb[k] != '\r') sb[k] = ' '; }

            while (i < n)
            {
                char c = src[i];
                if (c == '/' && i + 1 < n && src[i + 1] == '/')
                {
                    int e = src.IndexOf('\n', i); if (e < 0) e = n;
                    Blank(t, i, e); Blank(m, i, e); i = e;
                }
                else if (c == '/' && i + 1 < n && src[i + 1] == '*')
                {
                    int e = src.IndexOf("*/", i + 2, StringComparison.Ordinal); e = e < 0 ? n : e + 2;
                    Blank(t, i, e); Blank(m, i, e); i = e;
                }
                else if (c == '"' || (c == '@' && i + 1 < n && src[i + 1] == '"') || (c == '$' && i + 1 < n && (src[i + 1] == '"' || src[i + 1] == '@')))
                {
                    bool verbatim = src.IndexOf('@', i, Math.Min(2, n - i)) >= 0;
                    int q = src.IndexOf('"', i);
                    int j = q + 1;
                    while (j < n)
                    {
                        if (verbatim && src[j] == '"' && j + 1 < n && src[j + 1] == '"') { j += 2; continue; }
                        if (!verbatim && src[j] == '\\') { j += 2; continue; }
                        if (src[j] == '"') break;
                        if (!verbatim && src[j] == '\n') break;
                        j++;
                    }
                    Blank(m, q + 1, Math.Min(j, n));
                    i = j + 1;
                }
                else if (c == '\'')
                {
                    int j = i + 1;
                    while (j < n && src[j] != '\'' && src[j] != '\n') { if (src[j] == '\\') j++; j++; }
                    Blank(m, i + 1, Math.Min(j, n));
                    i = j + 1;
                }
                else i++;
            }
            text = t.ToString();
            masked = m.ToString();
        }
    }
}

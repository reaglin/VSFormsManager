using System.Text.RegularExpressions;

namespace VSFormsManager.Services.Analysis
{
    /// <summary>
    /// Statically scans a form's source code to find other form classes that are
    /// instantiated from it.
    ///
    /// Detection patterns (all require the class name to be in
    /// <paramref name="knownFormClasses"/> to avoid false positives):
    ///
    ///   Direct:
    ///     new FormName()              — any instantiation of a known form
    ///     new FormName(args)
    ///
    ///   Confirmed show patterns:
    ///     new FormName().Show()
    ///     new FormName().ShowDialog()
    ///     formVar.Show()             — where var was declared as FormName
    ///     formVar.ShowDialog()
    ///
    /// Returns names of classes that could not be confirmed as forms
    /// (i.e. found via <c>new Unknown()</c> but not in knownFormClasses) in
    /// <see cref="UnresolvedCandidates"/> for the AI fallback.
    /// </summary>
    public class FormReferenceScanner
    {
        // Finds: new SomeIdentifier(
        private static readonly Regex NewInstanceRegex = new(
            @"\bnew\s+(\w+)\s*\(",
            RegexOptions.Compiled | RegexOptions.Multiline);

        // Finds: SomeVar.Show() or SomeVar.ShowDialog()
        private static readonly Regex ShowCallRegex = new(
            @"\b(\w+)\s*\.\s*(?:Show|ShowDialog)\s*\(",
            RegexOptions.Compiled | RegexOptions.Multiline);

        // Finds variable declarations: FormName varName = ... or var varName = new FormName(
        private static readonly Regex VarDeclRegex = new(
            @"\b(\w+)\s+(\w+)\s*=",
            RegexOptions.Compiled | RegexOptions.Multiline);

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>Form class names that were statically confirmed.</summary>
        public List<string> ResolvedFormClasses { get; } = new();

        /// <summary>
        /// Class names found via <c>new X()</c> that are NOT in knownFormClasses —
        /// passed to the AI fallback to decide if they are forms.
        /// Mutable so transitive dep-file scanning can append additional candidates.
        /// </summary>
        public List<string> UnresolvedCandidates { get; } = new();

        /// <summary>
        /// Scans <paramref name="sourceContent"/> and populates
        /// <see cref="ResolvedFormClasses"/> and <see cref="UnresolvedCandidates"/>.
        /// </summary>
        public void Scan(
            string sourceContent,
            IReadOnlyDictionary<string, string> knownFormClasses)
        {
            ResolvedFormClasses.Clear();
            UnresolvedCandidates.Clear();

            var resolvedSet   = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var unresolvedSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Build a local map: variable name → type name (for .Show() matching)
            var varTypeMap = BuildVarTypeMap(sourceContent);

            // ── Pass 1: new KnownForm(...) ────────────────────────────────────
            foreach (Match m in NewInstanceRegex.Matches(sourceContent))
            {
                var className = m.Groups[1].Value;

                // Skip language keywords that look like identifiers
                if (IsKeyword(className)) continue;

                if (knownFormClasses.ContainsKey(className))
                    resolvedSet.Add(className);
                else
                    unresolvedSet.Add(className);
            }

            // ── Pass 2: varName.Show() / varName.ShowDialog() ─────────────────
            foreach (Match m in ShowCallRegex.Matches(sourceContent))
            {
                var varName = m.Groups[1].Value;
                if (varTypeMap.TryGetValue(varName, out var typeName) &&
                    knownFormClasses.ContainsKey(typeName))
                {
                    resolvedSet.Add(typeName);
                }
            }

            // Exclude classes from unresolved that were already resolved
            unresolvedSet.ExceptWith(resolvedSet);

            // Remove common non-form names from unresolved to reduce AI noise
            unresolvedSet.ExceptWith(CommonNonFormClasses);

            ResolvedFormClasses.AddRange(resolvedSet);
            UnresolvedCandidates.AddRange(
                unresolvedSet.Where(c => LooksLikeFormCandidate(c)));
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        /// <summary>
        /// Builds a map of variable-name → declared-type from the source.
        /// Handles: <c>FormName varName =</c> but not <c>var varName =</c>
        /// (var requires type inference we skip for now).
        /// </summary>
        private static Dictionary<string, string> BuildVarTypeMap(string content)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (Match m in VarDeclRegex.Matches(content))
            {
                var typeName = m.Groups[1].Value;
                var varName  = m.Groups[2].Value;

                if (!IsKeyword(typeName) && !IsKeyword(varName))
                    map.TryAdd(varName, typeName);
            }

            return map;
        }

        /// <summary>
        /// Heuristic: a candidate looks like a form if it starts with a capital
        /// letter and contains more than 3 characters (filters out 'OK', 'ID', etc.)
        /// and follows common form naming conventions.
        /// </summary>
        private static bool LooksLikeFormCandidate(string name) =>
            name.Length > 3 &&
            char.IsUpper(name[0]) &&
            !name.All(char.IsUpper);   // skip ALL_CAPS constants

        private static readonly HashSet<string> CommonNonFormClasses =
            new(StringComparer.OrdinalIgnoreCase)
            {
                "Exception", "List", "Dictionary", "HashSet", "Task", "Thread",
                "Timer", "StringBuilder", "StreamReader", "StreamWriter",
                "FileStream", "MemoryStream", "HttpClient", "JsonSerializer",
                "CancellationTokenSource", "CancellationToken", "EventArgs",
                "EventHandler", "Action", "Func", "Predicate", "Tuple",
                "KeyValuePair", "ObservableCollection", "BindingList",
                "DataTable", "DataRow", "DataSet", "SqlConnection",
                "SqlCommand", "SqlDataReader", "SqlParameter",
                "BackgroundWorker", "ProgressBar", "OpenFileDialog",
                "SaveFileDialog", "FolderBrowserDialog", "ColorDialog",
                "FontDialog", "PrintDialog", "ImageList", "ContextMenuStrip",
                "ToolTip", "ErrorProvider", "NotifyIcon"
            };

        private static bool IsKeyword(string name) => CSharpKeywords.Contains(name);

        private static readonly HashSet<string> CSharpKeywords =
            new(StringComparer.Ordinal)
            {
                "abstract", "as", "base", "bool", "break", "byte", "case",
                "catch", "char", "checked", "class", "const", "continue",
                "decimal", "default", "delegate", "do", "double", "else",
                "enum", "event", "explicit", "extern", "false", "finally",
                "fixed", "float", "for", "foreach", "goto", "if", "implicit",
                "in", "int", "interface", "internal", "is", "lock", "long",
                "namespace", "new", "null", "object", "operator", "out",
                "override", "params", "private", "protected", "public",
                "readonly", "ref", "return", "sbyte", "sealed", "short",
                "sizeof", "stackalloc", "static", "string", "struct",
                "switch", "this", "throw", "true", "try", "typeof", "uint",
                "ulong", "unchecked", "unsafe", "ushort", "using", "virtual",
                "void", "volatile", "while", "var", "dynamic", "async",
                "await", "yield", "partial", "get", "set", "value", "add",
                "remove", "where", "select", "from", "group", "into",
                "orderby", "join", "let", "on", "equals", "by", "ascending",
                "descending", "global", "record", "init", "with", "and", "or",
                "not", "when", "nint", "nuint", "file", "required", "scoped"
            };
    }
}

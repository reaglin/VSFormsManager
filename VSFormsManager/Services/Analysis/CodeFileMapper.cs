using System.Text.RegularExpressions;

namespace VSFormsManager.Services.Analysis
{
    /// <summary>
    /// Maps the <c>using</c> directives in a form's source file to actual
    /// source files in the project that declare classes in those namespaces.
    ///
    /// For each project-specific namespace used by a form, this class finds
    /// every <c>.cs</c> file in the project that declares that namespace and
    /// returns those files as code dependencies to include in the scaffold.
    ///
    /// Framework namespaces (System.*, Microsoft.*, Windows.*) are skipped
    /// since those come from NuGet packages / the SDK, not project source files.
    /// </summary>
    public static class CodeFileMapper
    {
        private static readonly Regex UsingRegex = new(
            @"^\s*using\s+(?!static\b)(?![\w.]+\s*=)([\w.]+)\s*;",
            RegexOptions.Multiline | RegexOptions.Compiled);

        private static readonly Regex NamespaceRegex = new(
            @"^\s*namespace\s+([\w.]+)\s*[;{]",
            RegexOptions.Multiline | RegexOptions.Compiled);

        private static readonly string[] FrameworkPrefixes =
            { "System", "Microsoft", "Windows", "Newtonsoft" };

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Builds a map of namespace → file list for all .cs files under
        /// <paramref name="projectDirectory"/>.
        ///
        /// Call once per project and cache the result in
        /// <see cref="VSFormsManager.Models.ProjectAnalysis.NamespaceFileMap"/>.
        /// </summary>
        public static Dictionary<string, List<string>> BuildNamespaceMap(
            string projectDirectory)
        {
            var map = new Dictionary<string, List<string>>(
                StringComparer.OrdinalIgnoreCase);

            foreach (var file in Directory.GetFiles(
                projectDirectory, "*.cs", SearchOption.AllDirectories))
            {
                // Skip designer files — their namespace matches the main file
                if (file.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase))
                    continue;

                try
                {
                    var content = File.ReadAllText(file);
                    var m       = NamespaceRegex.Match(content);
                    if (!m.Success) continue;

                    var ns = m.Groups[1].Value.Trim();
                    if (!map.ContainsKey(ns))
                        map[ns] = new List<string>();

                    map[ns].Add(file);
                }
                catch { /* skip unreadable files */ }
            }

            return map;
        }

        /// <summary>
        /// Given the source content of a form file and the project's namespace
        /// map, returns all source files that should be included as code
        /// dependencies (i.e. files in project-specific namespaces used by this form).
        ///
        /// Excludes form files (those are handled separately as form nodes)
        /// and the form file itself.
        /// </summary>
        public static List<string> FindDependencyFiles(
            string                                  formSourceContent,
            string                                  formFilePath,
            Dictionary<string, List<string>>        namespaceMap,
            IReadOnlyDictionary<string, string>     knownFormFiles)
        {
            var result      = new List<string>();
            var seen        = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var formFileSet = new HashSet<string>(
                knownFormFiles.Values, StringComparer.OrdinalIgnoreCase);

            // Collect all project-specific usings
            var usings = UsingRegex.Matches(formSourceContent)
                .Cast<Match>()
                .Select(m => m.Groups[1].Value.Trim())
                .Where(ns => !IsFrameworkNamespace(ns))
                .Distinct(StringComparer.OrdinalIgnoreCase);

            foreach (var ns in usings)
            {
                if (!namespaceMap.TryGetValue(ns, out var files)) continue;

                foreach (var file in files)
                {
                    if (seen.Contains(file))                                    continue;
                    if (file.Equals(formFilePath, StringComparison.OrdinalIgnoreCase)) continue;
                    if (formFileSet.Contains(file))                             continue; // it's a form

                    seen.Add(file);
                    result.Add(file);
                }
            }

            return result;
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static bool IsFrameworkNamespace(string ns) =>
            FrameworkPrefixes.Any(p =>
                ns.StartsWith(p, StringComparison.OrdinalIgnoreCase));
    }
}

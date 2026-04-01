using System.Text.RegularExpressions;

namespace VSFormsManager.Services.Analysis
{
    /// <summary>
    /// Locates the application startup form by reading Program.cs (or equivalent
    /// entry-point file) in the source project.
    ///
    /// Detection patterns (tried in order):
    ///   1. <c>Application.Run(new FormName())</c>                — standard WinForms
    ///   2. <c>Application.Run(new FormName(args))</c>            — with constructor args
    ///   3. <c>new FormName().ShowDialog()</c>                    — dialog-only apps
    ///   4. Top-level statements: any <c>new FormName()</c> call  — minimal hosting
    ///   5. WPF: <c>StartupUri="FormName.xaml"</c> in App.xaml
    /// </summary>
    public static class ProgramCsAnalyzer
    {
        // WinForms: Application.Run(new FormName(...))
        private static readonly Regex AppRunRegex = new(
            @"Application\.Run\(\s*new\s+(\w+)\s*\(",
            RegexOptions.Compiled);

        // Dialog-only: new FormName().ShowDialog()
        private static readonly Regex ShowDialogRegex = new(
            @"new\s+(\w+)\s*\(\s*\)\s*\.ShowDialog",
            RegexOptions.Compiled);

        // Generic: any "new FormName()" in Program.cs
        private static readonly Regex AnyNewRegex = new(
            @"new\s+(\w+)\s*\(",
            RegexOptions.Compiled);

        // WPF App.xaml StartupUri
        private static readonly Regex WpfStartupRegex = new(
            @"StartupUri\s*=\s*""([^""]+)""",
            RegexOptions.Compiled);

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Searches the project directory for Program.cs (or App.xaml for WPF)
        /// and returns the startup form class name.
        ///
        /// Returns an empty string if not found.
        /// </summary>
        public static string FindStartupFormClass(
            string projectDirectory,
            IReadOnlyDictionary<string, string> knownFormClasses)
        {
            // ── WinForms: Program.cs ──────────────────────────────────────────
            var programCs = Path.Combine(projectDirectory, "Program.cs");
            if (File.Exists(programCs))
            {
                var content = File.ReadAllText(programCs);
                var result  = ScanProgramCs(content, knownFormClasses);
                if (!string.IsNullOrEmpty(result)) return result;
            }

            // ── WPF: App.xaml ─────────────────────────────────────────────────
            var appXaml = Path.Combine(projectDirectory, "App.xaml");
            if (File.Exists(appXaml))
            {
                var content = File.ReadAllText(appXaml);
                var m       = WpfStartupRegex.Match(content);
                if (m.Success)
                {
                    // StartupUri is e.g. "MainWindow.xaml" — extract class name
                    var xamlFile = m.Groups[1].Value;
                    var baseName = Path.GetFileNameWithoutExtension(
                        Path.GetFileNameWithoutExtension(xamlFile));

                    if (knownFormClasses.ContainsKey(baseName)) return baseName;
                }
            }

            // ── Fallback: search all .cs files for Application.Run ────────────
            foreach (var csFile in Directory.GetFiles(
                projectDirectory, "*.cs", SearchOption.TopDirectoryOnly))
            {
                var content = File.ReadAllText(csFile);
                var result  = ScanProgramCs(content, knownFormClasses);
                if (!string.IsNullOrEmpty(result)) return result;
            }

            return string.Empty;
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static string ScanProgramCs(
            string content,
            IReadOnlyDictionary<string, string> knownFormClasses)
        {
            // Pattern 1: Application.Run(new FormName(...))
            var m = AppRunRegex.Match(content);
            if (m.Success && knownFormClasses.ContainsKey(m.Groups[1].Value))
                return m.Groups[1].Value;

            // Pattern 2: new FormName().ShowDialog()
            m = ShowDialogRegex.Match(content);
            if (m.Success && knownFormClasses.ContainsKey(m.Groups[1].Value))
                return m.Groups[1].Value;

            // Pattern 3: any new KnownFormClass() in the file
            foreach (Match match in AnyNewRegex.Matches(content))
            {
                var candidate = match.Groups[1].Value;
                if (knownFormClasses.ContainsKey(candidate))
                    return candidate;
            }

            return string.Empty;
        }
    }
}

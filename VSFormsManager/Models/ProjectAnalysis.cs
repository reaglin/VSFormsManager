using VSFormsManager.Models;

namespace VSFormsManager.Models
{
    /// <summary>
    /// Top-level result of analysing a Visual Studio project.
    /// Produced by
    /// <see cref="VSFormsManager.Services.Analysis.ProjectAnalysisService.AnalyzeProjectAsync"/>.
    ///
    /// The root of the dependency tree is <see cref="StartupNode"/>.
    /// All known form class names in the project are in <see cref="KnownFormClasses"/>
    /// and are used by the static scanner to distinguish form instantiations from
    /// regular object creation.
    /// </summary>
    public class ProjectAnalysis
    {
        // ── Source project ────────────────────────────────────────────────────

        /// <summary>Absolute path to the source .csproj file.</summary>
        public string CsprojPath { get; set; } = string.Empty;

        /// <summary>Absolute path to the source project directory.</summary>
        public string ProjectDirectory => Path.GetDirectoryName(CsprojPath) ?? string.Empty;

        /// <summary>Display name (file name without extension).</summary>
        public string ProjectName => Path.GetFileNameWithoutExtension(CsprojPath);

        // ── Project metadata (read from .csproj) ──────────────────────────────

        public string TargetFramework   { get; set; } = string.Empty;
        public string OutputType        { get; set; } = string.Empty;
        public bool   UseWindowsForms   { get; set; }
        public string RootNamespace     { get; set; } = string.Empty;
        public int    TotalFormCount    { get; set; }

        // ── Startup form ──────────────────────────────────────────────────────

        /// <summary>Class name of the startup form (e.g. <c>frmMain</c>).</summary>
        public string StartupFormClass { get; set; } = string.Empty;

        /// <summary>
        /// The root of the lazy dependency tree.
        /// Initially contains only the startup form node with no children.
        /// Children are populated on demand as the user expands nodes.
        /// </summary>
        public DependencyNode? StartupNode { get; set; }

        // ── Project-wide indexes (used by the scanner) ────────────────────────

        /// <summary>
        /// All form class names found in the project.
        /// Maps class name → absolute file path.
        /// Used to distinguish form instantiations from other object creation.
        /// </summary>
        public Dictionary<string, string> KnownFormClasses { get; set; }
            = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// All source files in the project mapped by their declared namespace.
        /// Maps namespace → list of absolute file paths in that namespace.
        /// Used by <see cref="VSFormsManager.Services.Analysis.CodeFileMapper"/>.
        /// </summary>
        public Dictionary<string, List<string>> NamespaceFileMap { get; set; }
            = new(StringComparer.OrdinalIgnoreCase);

        // ── Analysis state ────────────────────────────────────────────────────

        /// <summary>True when the initial analysis (startup form) completed successfully.</summary>
        public bool IsReady { get; set; }

        /// <summary>Error message if analysis failed.</summary>
        public string? ErrorMessage { get; set; }

        /// <summary>Nodes already in the tree, keyed by FilePath.
        /// Used to detect circular form references (Form1 → Form2 → Form1).
        /// </summary>
        public HashSet<string> VisitedFormPaths { get; set; }
            = new(StringComparer.OrdinalIgnoreCase);
    }
}

using VSFormsManager.Models;

namespace VSFormsManager.Models
{
    /// <summary>
    /// A single node in the project dependency tree built by
    /// <see cref="VSFormsManager.Services.Analysis.ProjectAnalysisService"/>.
    ///
    /// The tree is built lazily: a Form node's children are only populated when
    /// the user expands it in the UI (via
    /// <see cref="VSFormsManager.Services.Analysis.ProjectAnalysisService.AnalyzeFormNodeAsync"/>).
    ///
    /// Node relationships:
    ///   Form node  →  children are Controls, Referenced Forms, and Code Files
    ///   Control    →  leaf (no children)
    ///   CodeFile   →  leaf (no children)
    /// </summary>
    public class DependencyNode
    {
        // ── Identity ──────────────────────────────────────────────────────────

        /// <summary>Display name: class name for forms, field name for controls,
        /// relative path for code files.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>What kind of dependency this node represents.</summary>
        public DependencyNodeType NodeType { get; set; }

        /// <summary>Absolute path to the primary source file for this node.
        /// Empty for nodes that could not be resolved to a file.</summary>
        public string FilePath { get; set; } = string.Empty;

        /// <summary>For Form nodes: the fully-qualified class name (namespace + class).</summary>
        public string FullClassName { get; set; } = string.Empty;

        /// <summary>True when this is the application startup form.</summary>
        public bool IsStartupForm { get; set; }

        // ── Tree state ────────────────────────────────────────────────────────

        /// <summary>
        /// Whether this node's children have been populated yet.
        /// False until the user expands the node (lazy loading).
        /// Always true for leaf nodes (controls, code files).
        /// </summary>
        public bool IsAnalyzed { get; set; }

        /// <summary>
        /// Children of this node.
        /// For a Form: contains sub-groups of Controls, ReferencedForms, CodeFiles.
        /// </summary>
        public List<DependencyNode> Children { get; set; } = new();

        // ── User decision ─────────────────────────────────────────────────────

        /// <summary>Whether this node (and its subtree) should be included in the output.
        /// Defaults to true. Set false when the user unchecks it.</summary>
        public bool IsIncluded { get; set; } = true;

        /// <summary>
        /// Nodes from other parts of the tree that depend on this node being included.
        /// Used to generate warnings when the user tries to exclude a shared dependency.
        /// </summary>
        public List<DependencyNode> Dependents { get; set; } = new();

        // ── Resolution ────────────────────────────────────────────────────────

        /// <summary>How this node's form reference was detected.</summary>
        public DetectionMethod DetectedBy { get; set; } = DetectionMethod.NotApplicable;

        /// <summary>True when the file exists on disk.</summary>
        public bool FileExists => string.IsNullOrEmpty(FilePath) || File.Exists(FilePath);

        // ── Helpers ───────────────────────────────────────────────────────────

        /// <summary>True when this node is a Form that can be expanded further.</summary>
        public bool IsExpandable =>
            NodeType == DependencyNodeType.Form && !IsAnalyzed;

        public override string ToString() => Name;
    }

    // ── Enumerations ──────────────────────────────────────────────────────────

    public enum DependencyNodeType
    {
        /// <summary>A Windows Forms / WPF form class.</summary>
        Form,

        /// <summary>A UI control instance declared on a parent form.</summary>
        Control,

        /// <summary>A non-form source file (service, model, helper) referenced via using.</summary>
        CodeFile,

        /// <summary>A grouping header node ("Controls", "Forms", "Code Files") — not a real file.</summary>
        Group
    }

    public enum DetectionMethod
    {
        /// <summary>Not applicable (controls, code files, groups).</summary>
        NotApplicable,

        /// <summary>Found via static regex analysis (new FormX() pattern).</summary>
        StaticAnalysis,

        /// <summary>Found via AI analysis as fallback.</summary>
        AiFallback,

        /// <summary>The startup form — found from Program.cs.</summary>
        ProgramCs
    }
}

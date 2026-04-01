using VSFormsManager.Models;

namespace VSFormsManager.Services.Scaffolding
{
    /// <summary>
    /// Walks the <see cref="DependencyNode"/> tree that the user has configured
    /// and produces the flat list of <see cref="ScaffoldFile"/> objects consumed
    /// by <see cref="SolutionScaffolder"/>.
    ///
    /// Steps:
    ///   1. Recursively collect all included Form and CodeFile nodes.
    ///   2. For each Form, also collect companion files (.Designer.cs / .xaml.cs).
    ///   3. Apply <see cref="ExclusionCommentRewriter"/> to each included file,
    ///      passing the names of all excluded forms/classes.
    ///   4. Generate a Program.cs using the startup form.
    ///   5. Return a deduplicated, sorted list of ScaffoldFiles.
    /// </summary>
    public static class DependencyTreeScaffolder
    {
        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Produces the complete list of files to scaffold from the inclusion tree.
        /// </summary>
        public static List<ScaffoldFile> CollectFiles(
            DependencyNode     startupNode,
            string             projectDirectory,
            string             startupFormName)
        {
            var files       = new Dictionary<string, ScaffoldFile>(
                                  StringComparer.OrdinalIgnoreCase);
            var excluded    = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // ── Pass 1: collect all included/excluded nodes ────────────────────
            CollectNode(startupNode, projectDirectory, files, excluded);

            // ── Pass 2: apply exclusion rewriter to all included files ─────────
            foreach (var sf in files.Values.Where(f => !string.IsNullOrEmpty(f.SourcePath)))
            {
                sf.Content = ExclusionCommentRewriter.Rewrite(sf.Content, excluded);
            }

            // ── Pass 3: generate Program.cs ────────────────────────────────────
            var programCs = GenerateProgramCs(startupFormName);
            files["Program.cs"] = new ScaffoldFile
            {
                RelativePath = "Program.cs",
                Content      = programCs,
                SourcePath   = string.Empty,
                Category     = ScaffoldFileCategory.Infrastructure,
                IsIncluded   = true
            };

            return files.Values
                .OrderBy(f => (int)f.Category)
                .ThenBy(f => f.RelativePath)
                .ToList();
        }

        // ── Recursive collection ──────────────────────────────────────────────

        private static void CollectNode(
            DependencyNode                   node,
            string                           projectDirectory,
            Dictionary<string, ScaffoldFile> files,
            HashSet<string>                  excluded)
        {
            switch (node.NodeType)
            {
                case DependencyNodeType.Form:
                    if (node.IsIncluded)
                        AddFormFiles(node, projectDirectory, files);
                    else
                        excluded.Add(node.FullClassName);   // track for comment rewriter
                    break;

                case DependencyNodeType.CodeFile:
                    if (node.IsIncluded)
                        AddCodeFile(node, projectDirectory, files);
                    break;

                case DependencyNodeType.Control:
                    if (!node.IsIncluded)
                        excluded.Add(node.Name);
                    break;
            }

            // Recurse into children regardless (need to track excluded nodes)
            foreach (var child in node.Children)
                CollectNode(child, projectDirectory, files, excluded);
        }

        private static void AddFormFiles(
            DependencyNode                   node,
            string                           projectDirectory,
            Dictionary<string, ScaffoldFile> files)
        {
            AddSingleFile(node.FilePath, projectDirectory, files,
                          ScaffoldFileCategory.Form);

            // Companion files
            var ext = Path.GetExtension(node.FilePath).ToLowerInvariant();

            if (ext == ".cs")
            {
                // .Designer.cs
                var designer = Path.ChangeExtension(node.FilePath, null) + ".Designer.cs";
                if (File.Exists(designer))
                    AddSingleFile(designer, projectDirectory, files,
                                  ScaffoldFileCategory.Form);
            }
            else if (ext == ".xaml")
            {
                // .xaml.cs code-behind
                var codeBehind = node.FilePath + ".cs";
                if (File.Exists(codeBehind))
                    AddSingleFile(codeBehind, projectDirectory, files,
                                  ScaffoldFileCategory.Form);
            }
        }

        private static void AddCodeFile(
            DependencyNode                   node,
            string                           projectDirectory,
            Dictionary<string, ScaffoldFile> files)
        {
            // Determine category from path
            var category = node.FilePath.Contains(
                               Path.DirectorySeparatorChar + "Models" +
                               Path.DirectorySeparatorChar,
                               StringComparison.OrdinalIgnoreCase)
                         ? ScaffoldFileCategory.Model
                         : ScaffoldFileCategory.Service;

            AddSingleFile(node.FilePath, projectDirectory, files, category);
        }

        private static void AddSingleFile(
            string                           absolutePath,
            string                           projectDirectory,
            Dictionary<string, ScaffoldFile> files,
            ScaffoldFileCategory             category)
        {
            if (!File.Exists(absolutePath) || files.ContainsKey(absolutePath))
                return;

            var relative = MakeRelative(absolutePath, projectDirectory);
            var content  = File.ReadAllText(absolutePath);

            files[absolutePath] = new ScaffoldFile
            {
                RelativePath = relative,
                Content      = content,
                SourcePath   = absolutePath,
                Category     = category,
                IsIncluded   = true
            };
        }

        // ── Program.cs generation ─────────────────────────────────────────────

        private static string GenerateProgramCs(string startupFormName)
        {
            var nl = Environment.NewLine;
            return
                "// Auto-generated by VSFormsManager" + nl +
                "namespace __NAMESPACE__" + nl +
                "{" + nl +
                "    internal static class Program" + nl +
                "    {" + nl +
                "        [STAThread]" + nl +
                "        static void Main()" + nl +
                "        {" + nl +
                "            ApplicationConfiguration.Initialize();" + nl +
                $"            Application.Run(new {startupFormName}());" + nl +
                "        }" + nl +
                "    }" + nl +
                "}";
            // Note: __NAMESPACE__ is replaced by SolutionScaffolder.RewriteNamespace
            // when the namespace rewrite pass runs over all files.
        }

        // ── Path helper ───────────────────────────────────────────────────────

        private static string MakeRelative(string absolutePath, string projectRoot)
        {
            var root = projectRoot.TrimEnd(
                Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;

            if (absolutePath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                return absolutePath[root.Length..];

            return Path.GetFileName(absolutePath);
        }
    }
}

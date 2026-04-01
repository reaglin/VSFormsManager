using System.Text.RegularExpressions;
using VSFormsManager.Models;
using VSFormsManager.Services.Parsing;

namespace VSFormsManager.Services.Analysis
{
    /// <summary>
    /// Orchestrates the two-phase project analysis:
    ///
    ///   Phase 1 — <see cref="AnalyzeProjectAsync"/>
    ///     Reads the .csproj, builds the known-form index and namespace map,
    ///     finds the startup form, and returns a <see cref="ProjectAnalysis"/>
    ///     whose tree contains only the startup form node (no children yet).
    ///
    ///   Phase 2 — <see cref="AnalyzeFormNodeAsync"/>
    ///     Called lazily when the user expands a form node in the UI.
    ///     Parses the form, finds its controls, referenced forms, and code
    ///     dependencies, then attaches them as children of the node.
    ///     Uses static analysis first; falls back to AI for unresolved candidates.
    /// </summary>
    public class ProjectAnalysisService
    {
        private readonly AppSettings          _settings;
        private readonly AiFormReferenceAnalyzer _aiAnalyzer;

        // Detects: class ClassName : ... Form   (Form subclasses only)
        // Note: does NOT match UserControl — those are embedded controls, not opened windows.
        private static readonly Regex FormOnlyRegex = new(
            @"\bclass\s+(\w+)\s*(?:<[^>]+>)?\s*:.*?\bForm\b",
            RegexOptions.Compiled | RegexOptions.Singleline);

        // Detects: class ClassName : ... UserControl  (for exclusion from form index)
        private static readonly Regex UserControlRegex = new(
            @"\bclass\s+(\w+)\s*(?:<[^>]+>)?\s*:.*?\bUserControl\b",
            RegexOptions.Compiled | RegexOptions.Singleline);

        public ProjectAnalysisService(AppSettings settings)
        {
            _settings   = settings;
            _aiAnalyzer = new AiFormReferenceAnalyzer(settings);
        }

        // ── Phase 1 — Project-level analysis ─────────────────────────────────

        /// <summary>
        /// Reads the project, builds indexes, locates the startup form, and
        /// returns a <see cref="ProjectAnalysis"/> ready for Phase 2.
        /// </summary>
        public async Task<ProjectAnalysis> AnalyzeProjectAsync(
            string            csprojPath,
            IProgress<string>? progress,
            CancellationToken  ct)
        {
            var analysis = new ProjectAnalysis { CsprojPath = csprojPath };

            try
            {
                // ── Read .csproj ──────────────────────────────────────────────
                progress?.Report("Reading project file…");
                var csprojInfo = Scaffolding.CsprojReader.Read(csprojPath);
                analysis.TargetFramework = csprojInfo.TargetFramework;
                analysis.OutputType      = csprojInfo.OutputType;
                analysis.UseWindowsForms = csprojInfo.UseWindowsForms;
                analysis.RootNamespace   = csprojInfo.RootNamespace;

                ct.ThrowIfCancellationRequested();

                // ── Index all form classes ────────────────────────────────────
                progress?.Report("Indexing form classes…");
                analysis.KnownFormClasses = BuildFormClassIndex(
                    analysis.ProjectDirectory);
                analysis.TotalFormCount = analysis.KnownFormClasses.Count;

                ct.ThrowIfCancellationRequested();

                // ── Build namespace → file map ────────────────────────────────
                progress?.Report("Building namespace map…");
                analysis.NamespaceFileMap = CodeFileMapper.BuildNamespaceMap(
                    analysis.ProjectDirectory);

                ct.ThrowIfCancellationRequested();

                // ── Find startup form ─────────────────────────────────────────
                progress?.Report("Locating startup form…");
                analysis.StartupFormClass = ProgramCsAnalyzer.FindStartupFormClass(
                    analysis.ProjectDirectory, analysis.KnownFormClasses);

                if (string.IsNullOrEmpty(analysis.StartupFormClass))
                {
                    analysis.ErrorMessage =
                        "Could not determine the startup form from Program.cs.\r\n" +
                        "Please check the project type and entry point.";
                    return analysis;
                }

                // ── Create startup node (no children yet) ─────────────────────
                var startupFilePath = analysis.KnownFormClasses[analysis.StartupFormClass];
                analysis.StartupNode = new DependencyNode
                {
                    Name          = analysis.StartupFormClass,
                    FullClassName = analysis.StartupFormClass,
                    NodeType      = DependencyNodeType.Form,
                    FilePath      = startupFilePath,
                    IsStartupForm = true,
                    IsAnalyzed    = false,
                    DetectedBy    = DetectionMethod.ProgramCs
                };
                analysis.VisitedFormPaths.Add(startupFilePath);

                analysis.IsReady = true;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                analysis.ErrorMessage =
                    $"Analysis failed: {ex.GetType().Name} — {ex.Message}";
            }

            return analysis;
        }

        // ── Phase 2 — Form node expansion ────────────────────────────────────

        /// <summary>
        /// Expands <paramref name="formNode"/> by parsing its source file and
        /// attaching child nodes for:
        ///   • Controls (from designer or field declarations)
        ///   • Forms opened from this form (static + AI)
        ///   • Code dependencies (services, models, etc.)
        ///
        /// Marks the node as <see cref="DependencyNode.IsAnalyzed"/> = true.
        /// Circular references are detected via <paramref name="analysis"/>.VisitedFormPaths.
        /// </summary>
        public async Task AnalyzeFormNodeAsync(
            DependencyNode    formNode,
            ProjectAnalysis   analysis,
            IProgress<string>? progress,
            CancellationToken  ct)
        {
            if (formNode.IsAnalyzed) return;

            progress?.Report($"Analysing {formNode.Name}…");

            try
            {
                // ── Parse form ────────────────────────────────────────────────
                var (parser, primaryPath) = FormParserFactory.GetParser(formNode.FilePath);
                var record                = parser.Parse(primaryPath);

                // Read the full source content (primary + designer for control scan)
                var sourceContent = File.ReadAllText(primaryPath);
                string designerContent = string.Empty;
                var designerPath = Path.ChangeExtension(primaryPath, null) + ".Designer.cs";
                if (File.Exists(designerPath))
                    designerContent = File.ReadAllText(designerPath);

                ct.ThrowIfCancellationRequested();

                // ── Group 1: Controls ─────────────────────────────────────────
                if (record.Controls.Count > 0)
                {
                    var controlGroup = new DependencyNode
                    {
                        Name       = $"Controls ({record.Controls.Count})",
                        NodeType   = DependencyNodeType.Group,
                        IsAnalyzed = true
                    };

                    foreach (var ctrl in record.Controls)
                        controlGroup.Children.Add(new DependencyNode
                        {
                            Name       = ctrl.Name,
                            NodeType   = DependencyNodeType.Control,
                            FilePath   = string.Empty,
                            IsAnalyzed = true,
                            FullClassName = ctrl.ControlType
                        });

                    formNode.Children.Add(controlGroup);
                }

                // ── Group 2: Code Dependencies ────────────────────────────────
                // Collect dep files BEFORE scanning for form refs so we can
                // also scan them transitively (e.g. AppSession.OpenAiSettings
                // lives in AppSession.cs, not in the form file itself).
                var depFiles = CodeFileMapper.FindDependencyFiles(
                    sourceContent,
                    primaryPath,
                    analysis.NamespaceFileMap,
                    analysis.KnownFormClasses);

                ct.ThrowIfCancellationRequested();

                // ── Same-namespace peer files ─────────────────────────────────
                // Files in the same namespace as the form don't need a using directive,
                // so they never appear in CodeFileMapper results.
                // Example: AppSession.cs is in VSFormsManager alongside frmMain,
                // but frmMain has no "using VSFormsManager;" — it's already in it.
                var formNamespace = Parsing.ParserBase.ExtractNamespace(sourceContent);
                if (!string.IsNullOrEmpty(formNamespace) &&
                    analysis.NamespaceFileMap.TryGetValue(
                        formNamespace, out var peerFiles))
                {
                    var formFilePaths = new HashSet<string>(
                        analysis.KnownFormClasses.Values,
                        StringComparer.OrdinalIgnoreCase);

                    foreach (var peerFile in peerFiles)
                    {
                        if (peerFile.Equals(primaryPath,
                                StringComparison.OrdinalIgnoreCase)) continue;
                        if (formFilePaths.Contains(peerFile))     continue;
                        if (!depFiles.Contains(peerFile))
                            depFiles.Add(peerFile);
                    }
                }

                // ── Group 3: Referenced Forms ─────────────────────────────────
                // Pass 1 — direct scan of form file (+ designer)
                var scanner = new FormReferenceScanner();
                var scanContent = sourceContent +
                    (string.IsNullOrEmpty(designerContent) ? "" : "\r\n" + designerContent);
                scanner.Scan(scanContent, analysis.KnownFormClasses);

                // Pass 2 — transitive scan: scan each dep file for form refs
                // This catches patterns like:
                //   frmMain  →  AppSession.OpenAiSettings(this)
                //   AppSession.cs  →  new frmAiSettings(Settings)
                var transitiveRefs =
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    // key = form class name, value = "via FileName.cs"

                foreach (var depFile in depFiles)
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        var depContent  = File.ReadAllText(depFile);
                        var depScanner  = new FormReferenceScanner();
                        depScanner.Scan(depContent, analysis.KnownFormClasses);

                        var via = Path.GetFileName(depFile);
                        foreach (var fc in depScanner.ResolvedFormClasses)
                            transitiveRefs.TryAdd(fc, via);

                        // Merge unresolved candidates for AI fallback
                        scanner.UnresolvedCandidates.AddRange(
                            depScanner.UnresolvedCandidates
                                .Where(c => !scanner.ResolvedFormClasses
                                                    .Contains(c,
                                                        StringComparer.OrdinalIgnoreCase)));
                    }
                    catch { /* skip unreadable dep files */ }
                }

                ct.ThrowIfCancellationRequested();

                // AI fallback for anything still unresolved
                List<string> aiResolved = new();
                if (scanner.UnresolvedCandidates.Count > 0 &&
                    AiProviderRouter.TaskHasKey(AiTask.FormAnalysis, _settings))
                {
                    progress?.Report(
                        $"  AI resolving {scanner.UnresolvedCandidates.Count} unresolved references…");
                    aiResolved = await _aiAnalyzer.ResolveAsync(
                        sourceContent,
                        scanner.UnresolvedCandidates,
                        analysis.KnownFormClasses.Keys,
                        ct);
                }

                // Merge: direct + transitive + AI, deduplicated
                var allFormRefs = scanner.ResolvedFormClasses
                    .Concat(transitiveRefs.Keys)
                    .Concat(aiResolved)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (allFormRefs.Count > 0)
                {
                    var formGroup = new DependencyNode
                    {
                        Name       = $"Opens Forms ({allFormRefs.Count})",
                        NodeType   = DependencyNodeType.Group,
                        IsAnalyzed = true
                    };

                    foreach (var formClass in allFormRefs)
                    {
                        if (!analysis.KnownFormClasses.TryGetValue(
                            formClass, out var formPath)) continue;

                        var isCircular = analysis.VisitedFormPaths.Contains(formPath);

                        // Build display name — include via-file hint for transitive refs
                        string displayName = formClass;
                        if (transitiveRefs.TryGetValue(formClass, out var viaFile))
                            displayName += $"  (via {viaFile})";
                        if (isCircular)
                            displayName += "  (↩ circular)";

                        DetectionMethod detectedBy;
                        if (aiResolved.Contains(formClass, StringComparer.OrdinalIgnoreCase))
                            detectedBy = DetectionMethod.AiFallback;
                        else if (transitiveRefs.ContainsKey(formClass))
                            detectedBy = DetectionMethod.StaticAnalysis;
                        else
                            detectedBy = DetectionMethod.StaticAnalysis;

                        var childFormNode = new DependencyNode
                        {
                            Name          = displayName,
                            FullClassName = formClass,
                            NodeType      = DependencyNodeType.Form,
                            FilePath      = formPath,
                            IsAnalyzed    = isCircular,
                            DetectedBy    = detectedBy
                        };

                        if (!isCircular)
                            analysis.VisitedFormPaths.Add(formPath);

                        formGroup.Children.Add(childFormNode);
                    }

                    formNode.Children.Add(formGroup);
                }

                ct.ThrowIfCancellationRequested();

                // ── Code Dependencies group ───────────────────────────────────
                if (depFiles.Count > 0)
                {
                    var codeGroup = new DependencyNode
                    {
                        Name       = $"Code Dependencies ({depFiles.Count})",
                        NodeType   = DependencyNodeType.Group,
                        IsAnalyzed = true
                    };

                    foreach (var depFile in depFiles)
                    {
                        codeGroup.Children.Add(new DependencyNode
                        {
                            Name       = Path.GetFileName(depFile),
                            NodeType   = DependencyNodeType.CodeFile,
                            FilePath   = depFile,
                            IsAnalyzed = true
                        });
                    }

                    formNode.Children.Add(codeGroup);
                }
            }
            catch (Exception ex)
            {
                // Don't fail the whole tree — add an error node
                formNode.Children.Add(new DependencyNode
                {
                    Name       = $"⚠ Analysis error: {ex.Message}",
                    NodeType   = DependencyNodeType.Group,
                    IsAnalyzed = true
                });
            }

            formNode.IsAnalyzed = true;
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        /// <summary>
        /// Scans all .cs files in <paramref name="projectDirectory"/> to find
        /// classes inheriting from <c>Form</c> (not UserControl — those are
        /// embedded controls and should not appear as "opened forms").
        /// Returns a map of class name → absolute file path.
        /// </summary>
        private static Dictionary<string, string> BuildFormClassIndex(
            string projectDirectory)
        {
            var index = new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);

            foreach (var file in Directory.GetFiles(
                projectDirectory, "*.cs", SearchOption.AllDirectories))
            {
                if (file.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase))
                    continue;

                try
                {
                    var content = File.ReadAllText(file);

                    // Only include Form subclasses — UserControls are embedded
                    // controls (e.g. FormDetailPanel) and must not appear as
                    // "forms opened" by another form.
                    foreach (Match m in FormOnlyRegex.Matches(content))
                    {
                        var className = m.Groups[1].Value;

                        // Double-check: skip if the same class also inherits UserControl
                        // (handles pathological cases where both appear in one file)
                        if (UserControlRegex.IsMatch(content) &&
                            Regex.IsMatch(content,
                                $@"\bclass\s+{Regex.Escape(className)}\b.*?\bUserControl\b",
                                RegexOptions.Singleline))
                            continue;

                        index.TryAdd(className, file);
                    }
                }
                catch { /* skip unreadable files */ }
            }

            return index;
        }
    }
}

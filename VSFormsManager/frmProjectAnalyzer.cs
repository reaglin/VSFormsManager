using VSFormsManager.Models;
using VSFormsManager.Services;
using VSFormsManager.Services.Analysis;
using VSFormsManager.Services.Scaffolding;

namespace VSFormsManager
{
    /// <summary>
    /// Four-step wizard that creates a new solution from a subset of a source project,
    /// driven by an interactive dependency tree.
    ///
    /// Step 1 — Select Project
    ///   Browse to a .csproj file. The project is analysed immediately: form classes
    ///   are indexed, the startup form is located, and a summary is shown.
    ///
    /// Step 2 — Dependency Tree
    ///   A TreeView rooted at the startup form. Expanding a form node lazily
    ///   analyses that form's controls, opened forms, and code dependencies.
    ///   Each node has a checkbox — unchecking excludes it (and its subtree).
    ///
    /// Step 3 — Configure Output
    ///   Solution name, output folder, root namespace.
    ///   NuGet packages are listed for opt-out.
    ///
    /// Step 4 — Generate
    ///   Progress log, "Open in Explorer" on success.
    /// </summary>
    public partial class frmProjectAnalyzer : Form
    {
        // ── State ─────────────────────────────────────────────────────────────
        private ProjectAnalysis?       _analysis;
        private ProjectAnalysisService _analysisService;
        private int                    _currentStep = 1;
        private string                 _finalSlnPath = string.Empty;

        // ── Shared nav controls ───────────────────────────────────────────────
        private Button btnBack  = null!;
        private Button btnNext  = null!;
        private Label  lblStep  = null!;

        // ── Step panels ───────────────────────────────────────────────────────
        private Panel pnlStep1 = null!;
        private Panel pnlStep2 = null!;
        private Panel pnlStep3 = null!;
        private Panel pnlStep4 = null!;

        // ── Step 1 controls ───────────────────────────────────────────────────
        private TextBox txtCsproj     = null!;
        private Label   lblProjInfo   = null!;
        private Label   lblAnalysing  = null!;

        // ── Step 2 controls ───────────────────────────────────────────────────
        private TreeView tvDeps       = null!;
        private Label    lblTreeInfo  = null!;
        private Label    lblExpand    = null!;

        // ── Step 3 controls ───────────────────────────────────────────────────
        private TextBox  txtSolName   = null!;
        private TextBox  txtOutDir    = null!;
        private TextBox  txtNamespace = null!;
        private ListView lvPackages   = null!;

        // ── Step 4 controls ───────────────────────────────────────────────────
        private RichTextBox rtbLog        = null!;
        private Button      btnOpenFolder = null!;

        // ── Constructor ───────────────────────────────────────────────────────
        public frmProjectAnalyzer(AppSettings settings)
        {
            _analysisService = new ProjectAnalysisService(settings);
            AutoScaleMode    = AutoScaleMode.Font;
            BuildUi();
            GoToStep(1);
        }

        // ═════════════════════════════════════════════════════════════════════
        // UI CONSTRUCTION
        // ═════════════════════════════════════════════════════════════════════

        private void BuildUi()
        {
            Text          = "New Project from Source";
            Size          = new Size(900, 680);
            MinimumSize   = new Size(760, 580);
            StartPosition = FormStartPosition.CenterScreen;
            Font          = new Font("Segoe UI", 9f);

            // ── Header ────────────────────────────────────────────────────────
            var pnlHeader = new Panel
            {
                Dock = DockStyle.Top, Height = 52,
                BackColor = Color.FromArgb(0, 100, 180)
            };
            var lblTitle = new Label
            {
                Text = "New Project from Source", ForeColor = Color.White,
                Font = new Font("Segoe UI", 13f, FontStyle.Bold),
                AutoSize = true, Location = new Point(18, 10)
            };
            lblStep = new Label
            {
                ForeColor = Color.FromArgb(180, 220, 255),
                Font = new Font("Segoe UI", 9f), AutoSize = true,
                Location = new Point(18, 33)
            };
            pnlHeader.Controls.AddRange(new Control[] { lblTitle, lblStep });

            // ── Nav bar ───────────────────────────────────────────────────────
            var pnlNav = new Panel
            {
                Dock = DockStyle.Bottom, Height = 52,
                BackColor = SystemColors.ControlLight, Padding = new Padding(12, 10, 12, 0)
            };
            var navSep = new Panel
            {
                Dock = DockStyle.Bottom, Height = 1,
                BackColor = SystemColors.ControlDark
            };

            btnBack = new Button { Text = "← Back",  Size = new Size(96, 30), Enabled = false };
            btnNext = new Button
            {
                Text = "Next →", Size = new Size(96, 30),
                Font = new Font("Segoe UI", 9f, FontStyle.Bold)
            };
            var btnCancel = new Button
            {
                Text = "Cancel", Size = new Size(80, 30),
                DialogResult = DialogResult.Cancel
            };
            CancelButton = btnCancel;

            btnBack.Click += (_, _) => GoToStep(_currentStep - 1);
            btnNext.Click += BtnNext_Click;

            pnlNav.Resize += (_, _) =>
            {
                btnCancel.Location = new Point(pnlNav.ClientSize.Width - btnCancel.Width - 8, 10);
                btnNext.Location   = new Point(btnCancel.Left - btnNext.Width - 6, 10);
                btnBack.Location   = new Point(btnNext.Left - btnBack.Width - 6, 10);
            };
            pnlNav.Controls.AddRange(new Control[] { btnBack, btnNext, btnCancel });

            // ── Content ───────────────────────────────────────────────────────
            var pnlContent = new Panel { Dock = DockStyle.Fill };

            pnlStep1 = BuildStep1();
            pnlStep2 = BuildStep2();
            pnlStep3 = BuildStep3();
            pnlStep4 = BuildStep4();

            foreach (var p in new[] { pnlStep1, pnlStep2, pnlStep3, pnlStep4 })
            {
                p.Dock = DockStyle.Fill; p.Visible = false;
                p.Padding = new Padding(20, 14, 20, 6);
                pnlContent.Controls.Add(p);
            }

            Controls.Add(pnlContent);
            Controls.Add(navSep);
            Controls.Add(pnlNav);
            Controls.Add(pnlHeader);
        }

        // ── Step 1 ────────────────────────────────────────────────────────────

        private Panel BuildStep1()
        {
            var pnl = new Panel();

            var lbl = new Label
            {
                Text = "Select the source project (.csproj) to analyse.",
                AutoSize = true, Location = new Point(0, 0),
                Font = new Font("Segoe UI", 9.5f)
            };

            var lblCsproj = MakeLabel("Project file:", 0, 34, 100);
            txtCsproj = new TextBox
            {
                Location = new Point(106, 32), Size = new Size(520, 23),
                ReadOnly = true, BackColor = SystemColors.ControlLight,
                Font = new Font("Segoe UI", 9f),
                Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top
            };
            var btnBrowse = new Button
            {
                Text = "Browse…", Size = new Size(80, 25),
                Anchor = AnchorStyles.Right | AnchorStyles.Top
            };
            btnBrowse.Click += BtnBrowseCsproj_Click;

            lblAnalysing = new Label
            {
                Text = string.Empty, Location = new Point(0, 66),
                AutoSize = true, ForeColor = Color.FromArgb(0, 100, 180),
                Font = new Font("Segoe UI", 9f, FontStyle.Italic)
            };

            lblProjInfo = new Label
            {
                Text = string.Empty, Location = new Point(0, 90),
                AutoSize = false, Height = 140,
                Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top,
                Font = new Font("Segoe UI", 9f), ForeColor = Color.FromArgb(40, 40, 40)
            };

            pnl.Controls.AddRange(new Control[]
                { lbl, lblCsproj, txtCsproj, btnBrowse, lblAnalysing, lblProjInfo });

            pnl.Resize += (_, _) =>
            {
                int w = pnl.ClientSize.Width - 40;
                txtCsproj.Width = w - 106 - btnBrowse.Width - 4;
                btnBrowse.Location = new Point(106 + txtCsproj.Width + 4, 32);
                lblProjInfo.Width = w;
            };

            return pnl;
        }

        // ── Step 2 ────────────────────────────────────────────────────────────

        private Panel BuildStep2()
        {
            var pnl = new Panel();

            lblTreeInfo = new Label
            {
                Text = "Expand each form to see its dependencies. " +
                       "Uncheck nodes to exclude them from the new project.",
                AutoSize = false, Height = 22,
                Dock = DockStyle.Top,
                Font = new Font("Segoe UI", 9f)
            };

            lblExpand = new Label
            {
                Text = "⚡ Expanding a form analyses it — this may call the AI for unresolved references.",
                Dock = DockStyle.Top, Height = 20,
                ForeColor = Color.FromArgb(0, 100, 180),
                Font = new Font("Segoe UI", 8.5f, FontStyle.Italic)
            };

            tvDeps = new TreeView
            {
                Dock = DockStyle.Fill,
                CheckBoxes = true,
                HideSelection = false,
                ShowLines = true, ShowPlusMinus = true,
                Font = new Font("Segoe UI", 9f),
                ItemHeight = 22, Indent = 20
            };
            tvDeps.AfterCheck  += TvDeps_AfterCheck;
            tvDeps.BeforeExpand += TvDeps_BeforeExpand;

            pnl.Controls.Add(tvDeps);
            pnl.Controls.Add(lblExpand);
            pnl.Controls.Add(lblTreeInfo);

            return pnl;
        }

        // ── Step 3 ────────────────────────────────────────────────────────────

        private Panel BuildStep3()
        {
            var pnl = new Panel();
            int lw  = 120;
            int y   = 6;
            int rh  = 32;

            var lbl = new Label
            {
                Text = "Configure the output project.",
                AutoSize = true, Location = new Point(0, y),
                Font = new Font("Segoe UI", 9.5f)
            };
            y += 28;

            // Solution name
            pnl.Controls.AddRange(AddRow("Solution Name:", lw, y, out txtSolName));
            txtSolName.TextChanged += (_, _) =>
            {
                txtNamespace.Text = SanitizeIdentifier(txtSolName.Text);
            };
            y += rh;

            // Output folder
            var lblOut = MakeLabel("Output Folder:", 0, y, lw);
            txtOutDir = new TextBox { Location = new Point(lw + 4, y), Size = new Size(300, 23) };
            txtOutDir.Text = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            txtOutDir.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
            var btnOut = new Button
            {
                Text = "Browse…", Size = new Size(80, 23),
                Anchor = AnchorStyles.Right | AnchorStyles.Top
            };
            btnOut.Click += BtnBrowseOutDir_Click;
            pnl.Controls.AddRange(new Control[] { lblOut, txtOutDir, btnOut });
            y += rh;

            // Root namespace
            pnl.Controls.AddRange(AddRow("Root Namespace:", lw, y, out txtNamespace));
            y += rh + 10;

            // NuGet packages
            var lblPkg = new Label
            {
                Text = "NuGet Packages (uncheck to exclude):",
                Location = new Point(0, y), AutoSize = true,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold)
            };
            pnl.Controls.Add(lblPkg);
            y += 24;

            lvPackages = new ListView
            {
                Location = new Point(0, y), CheckBoxes = true,
                View = View.Details, FullRowSelect = true, GridLines = true,
                Font = new Font("Segoe UI", 9f),
                Anchor = AnchorStyles.Left | AnchorStyles.Right |
                         AnchorStyles.Top  | AnchorStyles.Bottom
            };
            lvPackages.Columns.Add("Package", 250);
            lvPackages.Columns.Add("Version", 110);
            pnl.Controls.AddRange(new Control[] { lbl, lvPackages });

            pnl.Resize += (_, _) =>
            {
                int w = pnl.ClientSize.Width - 40;
                txtOutDir.Width     = w - lw - 4 - btnOut.Width - 4;
                btnOut.Location     = new Point(lw + 4 + txtOutDir.Width + 4, btnOut.Top);
                lvPackages.Size     = new Size(w, pnl.ClientSize.Height - lvPackages.Top - 8);
            };

            return pnl;
        }

        // ── Step 4 ────────────────────────────────────────────────────────────

        private Panel BuildStep4()
        {
            var pnl = new Panel();

            var lbl = new Label
            {
                Text = "Generating project…", Name = "lblStep4Title",
                AutoSize = true, Location = new Point(0, 0),
                Font = new Font("Segoe UI", 9.5f)
            };

            rtbLog = new RichTextBox
            {
                ReadOnly = true, BackColor = Color.FromArgb(30, 30, 30),
                ForeColor = Color.FromArgb(200, 230, 200),
                Font = new Font("Consolas", 9f), BorderStyle = BorderStyle.None,
                Anchor = AnchorStyles.Left | AnchorStyles.Right |
                         AnchorStyles.Top  | AnchorStyles.Bottom,
                Location = new Point(0, 26)
            };

            btnOpenFolder = new Button
            {
                Text = "📂  Open Solution Folder", Size = new Size(200, 30),
                Visible = false, Anchor = AnchorStyles.Left | AnchorStyles.Bottom,
                Font = new Font("Segoe UI", 9.5f)
            };
            btnOpenFolder.Click += (_, _) =>
            {
                if (!string.IsNullOrEmpty(_finalSlnPath))
                    System.Diagnostics.Process.Start(
                        "explorer.exe", Path.GetDirectoryName(_finalSlnPath)!);
            };

            pnl.Controls.AddRange(new Control[] { lbl, rtbLog, btnOpenFolder });
            pnl.Resize += (_, _) =>
            {
                rtbLog.Size = new Size(pnl.ClientSize.Width - 40,
                                       pnl.ClientSize.Height - 68);
                btnOpenFolder.Location = new Point(0, rtbLog.Bottom + 8);
            };
            return pnl;
        }

        // ═════════════════════════════════════════════════════════════════════
        // NAVIGATION
        // ═════════════════════════════════════════════════════════════════════

        private void GoToStep(int step)
        {
            _currentStep = step;
            pnlStep1.Visible = step == 1;
            pnlStep2.Visible = step == 2;
            pnlStep3.Visible = step == 3;
            pnlStep4.Visible = step == 4;

            btnBack.Enabled = step > 1 && step < 4;
            btnNext.Enabled = step < 4;
            btnNext.Text    = step == 3 ? "Generate  ▶" : "Next  →";

            string[] titles =
            {
                "Step 1 of 4 — Select Source Project",
                "Step 2 of 4 — Dependency Tree",
                "Step 3 of 4 — Configure Output",
                "Step 4 of 4 — Generating"
            };
            lblStep.Text = titles[step - 1];

            if (step == 2) PopulateStep2();
            if (step == 3) PopulateStep3();
        }

        private async void BtnNext_Click(object? sender, EventArgs e)
        {
            switch (_currentStep)
            {
                case 1:
                    if (_analysis == null || !_analysis.IsReady)
                    {
                        Warn("Please browse to a project file and wait for analysis to complete.");
                        return;
                    }
                    GoToStep(2);
                    break;

                case 2:
                    GoToStep(3);
                    break;

                case 3:
                    if (!ValidateStep3()) return;
                    GoToStep(4);
                    await RunGenerateAsync();
                    break;
            }
        }

        // ═════════════════════════════════════════════════════════════════════
        // STEP 1 — browse + analyse
        // ═════════════════════════════════════════════════════════════════════

        private async void BtnBrowseCsproj_Click(object? sender, EventArgs e)
        {
            using var dlg = new OpenFileDialog
            {
                Title  = "Select a Visual Studio C# Project File",
                Filter = "C# Project Files (*.csproj)|*.csproj|All Files (*.*)|*.*",
                CheckFileExists = true
            };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;

            txtCsproj.Text   = dlg.FileName;
            lblProjInfo.Text = string.Empty;
            _analysis        = null;
            btnNext.Enabled  = false;

            lblAnalysing.Text = "Analysing project…";
            await AnalyseProjectAsync(dlg.FileName);
        }

        private async Task AnalyseProjectAsync(string csprojPath)
        {
            var progress = new Progress<string>(msg => lblAnalysing.Text = msg);

            _analysis = await _analysisService.AnalyzeProjectAsync(
                csprojPath, progress, CancellationToken.None);

            lblAnalysing.Text = string.Empty;

            if (!_analysis.IsReady)
            {
                lblProjInfo.ForeColor = Color.Firebrick;
                lblProjInfo.Text      = $"⚠  {_analysis.ErrorMessage}";
                btnNext.Enabled       = false;
                return;
            }

            lblProjInfo.ForeColor = Color.FromArgb(40, 40, 40);
            lblProjInfo.Text =
                $"Project:        {_analysis.ProjectName}\r\n" +
                $"Framework:      {_analysis.TargetFramework}\r\n" +
                $"Output type:    {_analysis.OutputType}\r\n" +
                $"Startup form:   {_analysis.StartupFormClass}\r\n" +
                $"Forms found:    {_analysis.TotalFormCount}\r\n" +
                $"Root namespace: {_analysis.RootNamespace}";

            btnNext.Enabled = true;
        }

        // ═════════════════════════════════════════════════════════════════════
        // STEP 2 — dependency tree
        // ═════════════════════════════════════════════════════════════════════

        private void PopulateStep2()
        {
            if (_analysis?.StartupNode == null) return;

            tvDeps.BeginUpdate();
            tvDeps.Nodes.Clear();

            var rootNode = CreateTreeNode(_analysis.StartupNode);
            tvDeps.Nodes.Add(rootNode);

            // Add a dummy child so the expand arrow shows
            if (!_analysis.StartupNode.IsAnalyzed)
                rootNode.Nodes.Add(new TreeNode("Loading…") { ForeColor = Color.DimGray });

            tvDeps.EndUpdate();
            rootNode.Expand();
        }

        private TreeNode CreateTreeNode(DependencyNode dn)
        {
            var icon = dn.NodeType switch
            {
                DependencyNodeType.Form     => dn.IsStartupForm ? "🏠 " : "📋 ",
                DependencyNodeType.Control  => "🔲 ",
                DependencyNodeType.CodeFile => "📄 ",
                DependencyNodeType.Group    => "📁 ",
                _                           => ""
            };

            var badge = dn.DetectedBy == DetectionMethod.AiFallback ? " [AI]" : "";

            var tn = new TreeNode($"{icon}{dn.Name}{badge}")
            {
                Tag     = dn,
                Checked = dn.IsIncluded
            };

            if (dn.NodeType == DependencyNodeType.Group ||
                dn.NodeType == DependencyNodeType.Control ||
                dn.NodeType == DependencyNodeType.CodeFile)
            {
                foreach (var child in dn.Children)
                    tn.Nodes.Add(CreateTreeNode(child));
            }
            else if (dn.NodeType == DependencyNodeType.Form && dn.IsAnalyzed)
            {
                foreach (var child in dn.Children)
                    tn.Nodes.Add(CreateTreeNode(child));
            }
            else if (dn.NodeType == DependencyNodeType.Form && !dn.IsAnalyzed)
            {
                // Placeholder to show expand arrow
                tn.Nodes.Add(new TreeNode("Loading…") { ForeColor = Color.DimGray });
            }

            if (!dn.FileExists)
            {
                tn.ForeColor   = Color.Gray;
                tn.ToolTipText = "File not found on disk.";
            }

            return tn;
        }

        private async void TvDeps_BeforeExpand(object? sender, TreeViewCancelEventArgs e)
        {
            if (e.Node?.Tag is not DependencyNode dn) return;
            if (dn.NodeType != DependencyNodeType.Form || dn.IsAnalyzed) return;

            e.Node.Nodes.Clear();

            var progress = new Progress<string>(msg => lblTreeInfo.Text = msg);

            await _analysisService.AnalyzeFormNodeAsync(
                dn, _analysis!, progress, CancellationToken.None);

            lblTreeInfo.Text =
                "Expand each form to see its dependencies. " +
                "Uncheck nodes to exclude them from the new project.";

            // Rebuild this node's children in the TreeView
            tvDeps.BeginUpdate();
            foreach (var child in dn.Children)
                e.Node.Nodes.Add(CreateTreeNode(child));
            tvDeps.EndUpdate();
        }

        private bool _updatingChecks = false;

        private void TvDeps_AfterCheck(object? sender, TreeViewEventArgs e)
        {
            if (_updatingChecks || e.Node == null) return;
            _updatingChecks = true;

            try
            {
                var dn = e.Node.Tag as DependencyNode;

                // Propagate to tree node children
                SetSubtreeChecked(e.Node, e.Node.Checked);

                // Sync DependencyNode
                if (dn != null) SyncNodeChecked(dn, e.Node.Checked);

                // Warn if unchecking something other nodes depend on
                if (!e.Node.Checked && dn != null && dn.Dependents.Count > 0)
                {
                    var dependentNames = string.Join(", ",
                        dn.Dependents.Select(d => d.Name));
                    MessageBox.Show(
                        $"'{dn.Name}' is referenced by: {dependentNames}\r\n\r\n" +
                        "Those references will be commented out in the generated code.",
                        "Dependency Warning",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }
            }
            finally { _updatingChecks = false; }
        }

        private static void SetSubtreeChecked(TreeNode node, bool @checked)
        {
            foreach (TreeNode child in node.Nodes)
            {
                child.Checked = @checked;
                SetSubtreeChecked(child, @checked);
            }
        }

        private static void SyncNodeChecked(DependencyNode dn, bool @checked)
        {
            dn.IsIncluded = @checked;
            foreach (var child in dn.Children)
                SyncNodeChecked(child, @checked);
        }

        // ═════════════════════════════════════════════════════════════════════
        // STEP 3 — configure output
        // ═════════════════════════════════════════════════════════════════════

        private void PopulateStep3()
        {
            if (_analysis == null) return;

            // Suggest solution name from project name
            if (string.IsNullOrEmpty(txtSolName.Text))
            {
                txtSolName.Text  = _analysis.ProjectName + "_Subset";
                txtNamespace.Text = SanitizeIdentifier(_analysis.ProjectName + "_Subset");
            }

            // Read packages from .csproj
            lvPackages.Items.Clear();
            var csprojInfo = CsprojReader.Read(_analysis.CsprojPath);
            foreach (var pkg in csprojInfo.PackageReferences)
            {
                var item = new ListViewItem(pkg.PackageId)
                    { Checked = true, Tag = pkg };
                item.SubItems.Add(pkg.Version);
                lvPackages.Items.Add(item);
            }
        }

        private void BtnBrowseOutDir_Click(object? sender, EventArgs e)
        {
            using var dlg = new FolderBrowserDialog
            {
                Description           = "Select output parent folder",
                SelectedPath          = txtOutDir.Text,
                UseDescriptionForTitle = true
            };
            if (dlg.ShowDialog(this) == DialogResult.OK)
                txtOutDir.Text = dlg.SelectedPath;
        }

        private bool ValidateStep3()
        {
            if (string.IsNullOrWhiteSpace(txtSolName.Text))
            { Warn("Enter a solution name."); return false; }
            if (!IsValidIdentifier(txtSolName.Text.Trim()))
            { Warn("Solution name must be a valid C# identifier."); return false; }
            if (string.IsNullOrWhiteSpace(txtOutDir.Text))
            { Warn("Choose an output folder."); return false; }
            return true;
        }

        // ═════════════════════════════════════════════════════════════════════
        // STEP 4 — generate
        // ═════════════════════════════════════════════════════════════════════

        private async Task RunGenerateAsync()
        {
            if (_analysis?.StartupNode == null) return;

            btnNext.Enabled = false;
            btnBack.Enabled = false;
            rtbLog.Clear();

            void Log(string msg)
            {
                rtbLog.AppendText(msg + Environment.NewLine);
                rtbLog.ScrollToCaret();
            }

            Log($"Collecting files from dependency tree…");

            // Sync package choices
            var csprojInfo = CsprojReader.Read(_analysis.CsprojPath);
            foreach (ListViewItem item in lvPackages.Items)
            {
                if (item.Tag is Models.PackageReferenceEntry pkg)
                    pkg.IsIncluded = item.Checked;
            }

            // Collect files from tree
            var scaffoldFiles = DependencyTreeScaffolder.CollectFiles(
                _analysis.StartupNode,
                _analysis.ProjectDirectory,
                _analysis.StartupFormClass);

            Log($"  {scaffoldFiles.Count} files collected.");

            // Build config
            var config = new Models.ProjectScaffoldConfig
            {
                SolutionName          = txtSolName.Text.Trim(),
                OutputParentDirectory = txtOutDir.Text.Trim(),
                RootNamespace         = txtNamespace.Text.Trim(),
                SourceRootNamespace   = _analysis.RootNamespace,
                SourceProjectFilePath = _analysis.CsprojPath,
                TargetFramework       = _analysis.TargetFramework,
                OutputType            = _analysis.OutputType,
                UseWindowsForms       = _analysis.UseWindowsForms,
                PackageReferences     = csprojInfo.PackageReferences,
                RewriteNamespaces     = true,
                StartupForm           = null
            };

            // Rewrite __NAMESPACE__ placeholder in Program.cs
            var programEntry = scaffoldFiles.FirstOrDefault(
                f => f.RelativePath == "Program.cs");
            if (programEntry != null)
                programEntry.Content = programEntry.Content.Replace(
                    "__NAMESPACE__", config.RootNamespace);

            Log($"Scaffolding solution '{config.SolutionName}'…");

            var progress = new Progress<string>(msg => Log($"  {msg}"));

            var result = await SolutionScaffolder.ScaffoldAsync(
                config, scaffoldFiles, csprojInfo, progress, CancellationToken.None);

            if (result.Success)
            {
                _finalSlnPath = result.SolutionPath;
                Log(string.Empty);
                Log($"✓  Solution created: {result.SolutionPath}");
                Log($"   {result.WrittenPaths.Count} files written.");
                if (result.SkippedPaths.Count > 0)
                    Log($"   {result.SkippedPaths.Count} files skipped.");

                var title = pnlStep4.Controls.OfType<Label>()
                    .FirstOrDefault(l => l.Name == "lblStep4Title");
                if (title != null) title.Text = "✓  Project generated successfully!";

                btnOpenFolder.Visible = true;
            }
            else
            {
                Log($"✗  FAILED: {result.ErrorMessage}");
                btnBack.Enabled = true;
            }
        }

        // ═════════════════════════════════════════════════════════════════════
        // HELPERS
        // ═════════════════════════════════════════════════════════════════════

        private Control[] AddRow(string label, int lw, int y, out TextBox txt)
        {
            var lbl = MakeLabel(label, 0, y, lw);
            txt     = new TextBox
            {
                Location = new Point(lw + 4, y), Size = new Size(320, 23),
                Font = new Font("Segoe UI", 9f)
            };
            return new Control[] { lbl, txt };
        }

        private static Label MakeLabel(string text, int x, int y, int w) =>
            new Label
            {
                Text = text, Location = new Point(x, y + 3),
                Size = new Size(w, 22), TextAlign = ContentAlignment.MiddleRight
            };

        private static string SanitizeIdentifier(string raw) =>
            string.Concat(raw.Where(c => char.IsLetterOrDigit(c) || c == '_'));

        private static bool IsValidIdentifier(string s) =>
            !string.IsNullOrEmpty(s) &&
            s.All(c => char.IsLetterOrDigit(c) || c == '_') &&
            !char.IsDigit(s[0]);

        private void Warn(string msg) =>
            MessageBox.Show(msg, "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }
}

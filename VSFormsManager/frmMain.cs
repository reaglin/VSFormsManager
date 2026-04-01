using VSFormsManager.Controls;

namespace VSFormsManager
{
    /// <summary>
    /// Application main window.
    ///
    /// Layout (top to bottom):
    ///   MenuStrip      — File, Tools, Help
    ///   Toolbar panel  — Browse for Form, Save Form As
    ///   FormDetailPanel — fills the rest; shows the currently selected form
    ///   StatusStrip    — current form path / ready message
    ///
    /// Workflow:
    ///   1. File → Browse for Form  opens <see cref="frmGetForm"/> modally.
    ///   2. User selects a form and clicks "Use This Form" → sets
    ///      <see cref="AppSession.CurrentForm"/> and closes the browser.
    ///   3. Main window refreshes the detail panel from <see cref="AppSession.CurrentForm"/>.
    ///   4. "Save Form As" becomes enabled; clicking it opens <see cref="frmSaveAs"/>.
    /// </summary>
    public partial class frmMain : Form
    {
        // ── Controls ──────────────────────────────────────────────────────────

        private FormDetailPanel _detailPanel  = null!;
        private Button          btnBrowse     = null!;
        private Button          btnSaveAs     = null!;
        private ToolStripStatusLabel lblStatus = null!;

        // ── Constructor ───────────────────────────────────────────────────────

        public frmMain()
        {
            InitializeComponent();
            BuildUi();
            RefreshDetailPanel();
        }

        // ═════════════════════════════════════════════════════════════════════
        // UI CONSTRUCTION
        // ═════════════════════════════════════════════════════════════════════

        private void BuildUi()
        {
            Text            = "VS Forms Manager";
            Size            = new Size(1060, 740);
            MinimumSize     = new Size(800, 560);
            StartPosition   = FormStartPosition.CenterScreen;
            Font            = new Font("Segoe UI", 9f);

            // ── Menu ──────────────────────────────────────────────────────────
            var menu = new MenuStrip();

            var miFile  = new ToolStripMenuItem("&File");
            var miBrowse = new ToolStripMenuItem("&Browse for Form…", null, MiBrowse_Click)
                           { ShortcutKeys = Keys.Control | Keys.O };
            var miSaveAs = new ToolStripMenuItem("Save Form &As…", null, MiSaveAs_Click)
                           { ShortcutKeys = Keys.Control | Keys.S, Enabled = false };
            var miNewProject = new ToolStripMenuItem(
                "&New Project from Library…", null, MiNewProject_Click)
                { ShortcutKeys = Keys.Control | Keys.Shift | Keys.N };
            var miExit = new ToolStripMenuItem("E&xit", null, (_, _) => Close());

            miFile.DropDownItems.AddRange(new ToolStripItem[]
            {
                miBrowse, miSaveAs,
                new ToolStripSeparator(),
                miNewProject,
                new ToolStripSeparator(),
                miExit
            });

            var miTools    = new ToolStripMenuItem("&Tools");
            var miAiConfig = new ToolStripMenuItem("&AI Provider Settings…",
                                 null, (_, _) => AppSession.OpenAiSettings(this));
            miTools.DropDownItems.Add(miAiConfig);

            var miHelp  = new ToolStripMenuItem("&Help");
            var miAbout = new ToolStripMenuItem("&About VS Forms Manager…",
                              null, MiAbout_Click);
            miHelp.DropDownItems.Add(miAbout);

            menu.Items.AddRange(new ToolStripItem[] { miFile, miTools, miHelp });
            MainMenuStrip = menu;

            // ── Toolbar ───────────────────────────────────────────────────────
            var pnlToolbar = new Panel
            {
                Dock      = DockStyle.Top,
                Height    = 46,
                BackColor = SystemColors.ControlLight,
                Padding   = new Padding(8, 8, 8, 0)
            };

            btnBrowse = new Button
            {
                Text     = "\uD83D\uDCC2  Browse for Form\u2026",
                Size     = new Size(174, 30),
                Location = new Point(8, 8),
                Font     = new Font("Segoe UI", 9.5f)
            };
            btnBrowse.Click += MiBrowse_Click;

            btnSaveAs = new Button
            {
                Text      = "\uD83D\uDCBE  Save Form As\u2026",
                Size      = new Size(164, 30),
                Location  = new Point(190, 8),
                Font      = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                BackColor = Color.FromArgb(0, 100, 180),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Enabled   = false
            };
            btnSaveAs.FlatAppearance.BorderColor = Color.FromArgb(0, 80, 160);
            btnSaveAs.Click += MiSaveAs_Click;

            var btnNewProject = new Button
            {
                Text      = "🗂  New Project…",
                Size      = new Size(148, 30),
                Location  = new Point(362, 8),
                Font      = new Font("Segoe UI", 9.5f),
                BackColor = Color.FromArgb(60, 120, 60),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            btnNewProject.FlatAppearance.BorderColor = Color.FromArgb(40, 100, 40);
            btnNewProject.Click += MiNewProject_Click;

            pnlToolbar.Controls.AddRange(new Control[]
                { btnBrowse, btnSaveAs, btnNewProject });

            var toolSep = new Panel
                { Dock = DockStyle.Top, Height = 1, BackColor = SystemColors.ControlDark };

            // ── Detail panel ──────────────────────────────────────────────────
            var pnlContent = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0) };

            var header = new Label
            {
                Text      = "Current Form",
                Dock      = DockStyle.Top,
                Height    = 28,
                Padding   = new Padding(10, 6, 0, 0),
                Font      = new Font("Segoe UI", 9f, FontStyle.Bold),
                BackColor = Color.FromArgb(230, 236, 245),
                ForeColor = Color.FromArgb(30, 60, 120),
                AutoSize  = false
            };

            _detailPanel = new FormDetailPanel { Dock = DockStyle.Fill };

            pnlContent.Controls.Add(_detailPanel);
            pnlContent.Controls.Add(header);

            // ── Status strip ──────────────────────────────────────────────────
            var status = new StatusStrip();
            lblStatus = new ToolStripStatusLabel("Ready")
            {
                Spring    = true,
                TextAlign = ContentAlignment.MiddleLeft
            };
            status.Items.Add(lblStatus);

            // Add in reverse dock order
            Controls.Add(pnlContent);
            Controls.Add(toolSep);
            Controls.Add(pnlToolbar);
            Controls.Add(menu);
            Controls.Add(status);

            // Keep menu's File→Save As in sync with toolbar button enabled state
            btnSaveAs.EnabledChanged += (_, _) => miSaveAs.Enabled = btnSaveAs.Enabled;
        }

        // ═════════════════════════════════════════════════════════════════════
        // DATA BINDING
        // ═════════════════════════════════════════════════════════════════════

        private void RefreshDetailPanel()
        {
            var form = AppSession.CurrentForm;
            _detailPanel.LoadRecord(form);

            btnSaveAs.Enabled = form != null;

            lblStatus.Text = form != null
                ? $"Working form: {form.FormName}  |  {form.FormFilePath}"
                : "Ready — use File → Browse for Form to select a form.";
        }

        // ═════════════════════════════════════════════════════════════════════
        // EVENT HANDLERS
        // ═════════════════════════════════════════════════════════════════════

        private void MiBrowse_Click(object? sender, EventArgs e)
        {
            if (AppSession.OpenFormBrowser(this))
                RefreshDetailPanel();   // user clicked "Use This Form"
        }

        private void MiSaveAs_Click(object? sender, EventArgs e)
        {
            var current = AppSession.CurrentForm;
            if (current == null)
            {
                MessageBox.Show(
                    "No form is selected. Use Browse for Form first.",
                    "No Form Selected",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            if (!current.FileExists)
            {
                MessageBox.Show(
                    $"The source file no longer exists:\r\n{current.FormFilePath}\r\n\r\n" +
                    "Please re-browse to the form at its new location.",
                    "File Not Found",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            using var dlg = new frmSaveAs(current, AppSession.Settings);
            dlg.ShowDialog(this);
        }

        private void MiNewProject_Click(object? sender, EventArgs e)
        {
            using var dlg = new frmProjectAnalyzer(AppSession.Settings);
            dlg.ShowDialog(this);
        }

        private void MiAbout_Click(object? sender, EventArgs e)
        {
            MessageBox.Show(
                "VS Forms Manager\r\n\r\n" +
                "Read, browse, and convert Visual Studio form files\r\n" +
                "between WinForms (Designer), WinForms (Code-Only), and XAML formats.\r\n\r\n" +
                "Conversion is powered by AI — configure providers in Tools → AI Provider Settings.",
                "About VS Forms Manager",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
    }
}

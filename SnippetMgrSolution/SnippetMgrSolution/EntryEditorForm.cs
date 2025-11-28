using SnippetMgr.Models;
using System.Data;
using System.Drawing.Text;
using System.Text;

namespace SnippetMgr
{
    public class EntryEditorForm : Form
    {
        public EntryConfig Entry { get; private set; }
        private string _currentType;

        // --- KONTROLKI UI ---
        private TextBox _txtTitle;
        private TextBox _txtDesc;
        private TextBox _txtShortcut;
        private CheckBox _chkPinned;

        // Sekcja Uruchamiania
        private GroupBox _grpPath;
        private TextBox _txtPath;
        private Button _btnBrowse;
        private TextBox _txtArgs;
        private CheckBox _chkAdmin;

        // Sekcja Treści (Rich Editor)
        private GroupBox _grpContent;
        private ToolStrip _toolbar;
        private ToolStripComboBox _cmbFontName;
        private ToolStripComboBox _cmbFontSize;
        private RichTextBox _rtbContent;
        private TextBox _txtTags;

        private Button _btnSave;
        private Button _btnCancel;

        public EntryEditorForm(EntryConfig entry = null, string defaultType = "snippet")
        {
            InitializeComponent();

            if (entry != null)
            {
                Entry = entry;
                if (!string.IsNullOrEmpty(Entry.AppPath)) _currentType = "app";
                else if (!string.IsNullOrEmpty(Entry.FolderPath)) _currentType = "folder";
                else if (Entry.TagsObject != null || IsRtf(Entry.NoteText) || defaultType == "note") _currentType = "note";
                else _currentType = "snippet";

                LoadData();
                this.Text = $"Edytuj: {GetTypeName(_currentType)}";
            }
            else
            {
                Entry = new EntryConfig();
                _currentType = defaultType;
                this.Text = $"Dodaj: {GetTypeName(_currentType)}";
            }

            UpdateVisibility();
        }

        private string GetTypeName(string type) => type switch { "app" => "Aplikację", "folder" => "Folder", "note" => "Notatkę", _ => "Snippet" };
        private bool IsRtf(string text) => !string.IsNullOrEmpty(text) && text.Trim().StartsWith(@"{\rtf");

        private void InitializeComponent()
        {
            this.Size = new Size(900, 800); // Powiększone okno dla paska narzędzi
            this.StartPosition = FormStartPosition.CenterParent;
            this.Font = new Font("Segoe UI", 9.5f);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;

            var mainTable = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(10), RowCount = 4 };
            mainTable.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            mainTable.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            mainTable.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            mainTable.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            // 1. INFO
            var grpMain = new GroupBox { Text = "Informacje podstawowe", Dock = DockStyle.Top, Height = 160 };
            var tableInfo = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 4, Padding = new Padding(5) };
            tableInfo.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            tableInfo.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            _txtTitle = new TextBox { Dock = DockStyle.Fill };
            _txtDesc = new TextBox { Dock = DockStyle.Fill };
            _txtShortcut = new TextBox { Width = 150 };
            _chkPinned = new CheckBox { Text = "Przypięte na górze", AutoSize = true };

            AddRow(tableInfo, "Nazwa:", _txtTitle, 0);
            AddRow(tableInfo, "Opis:", _txtDesc, 1);
            AddRow(tableInfo, "Skrót:", _txtShortcut, 2);
            tableInfo.Controls.Add(_chkPinned, 1, 3);
            grpMain.Controls.Add(tableInfo);

            // 2. PATH
            _grpPath = new GroupBox { Text = "Uruchamianie", Dock = DockStyle.Top, Height = 140, Visible = false };
            var tablePath = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 3, Padding = new Padding(5) };
            tablePath.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            tablePath.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            tablePath.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            _txtPath = new TextBox { Dock = DockStyle.Fill };
            _btnBrowse = new Button { Text = "...", Width = 40 }; _btnBrowse.Click += (s, e) => BrowseFile();
            _txtArgs = new TextBox { Dock = DockStyle.Fill };
            _chkAdmin = new CheckBox { Text = "Jako Admin", AutoSize = true };

            tablePath.Controls.Add(new Label { Text = "Ścieżka:", AutoSize = true, Anchor = AnchorStyles.Right }, 0, 0);
            tablePath.Controls.Add(_txtPath, 1, 0); tablePath.Controls.Add(_btnBrowse, 2, 0);
            tablePath.Controls.Add(new Label { Text = "Argumenty:", AutoSize = true, Anchor = AnchorStyles.Right }, 0, 1);
            tablePath.Controls.Add(_txtArgs, 1, 1); tablePath.Controls.Add(_chkAdmin, 1, 2);
            _grpPath.Controls.Add(tablePath);

            // 3. RICH EDITOR
            _grpContent = new GroupBox { Text = "Treść", Dock = DockStyle.Fill, Visible = false };

            _toolbar = new ToolStrip { Padding = new Padding(5), BackColor = Color.WhiteSmoke, RenderMode = ToolStripRenderMode.System };

            // Fonty
            _cmbFontName = new ToolStripComboBox { Width = 120, DropDownStyle = ComboBoxStyle.DropDownList };
            InstalledFontCollection fonts = new InstalledFontCollection();
            foreach (FontFamily font in fonts.Families) _cmbFontName.Items.Add(font.Name);
            _cmbFontName.SelectedItem = "Segoe UI";
            _cmbFontName.SelectedIndexChanged += (s, e) => ChangeFontFamily();

            _cmbFontSize = new ToolStripComboBox { Width = 50, DropDownStyle = ComboBoxStyle.DropDownList };
            _cmbFontSize.Items.AddRange(new object[] { "8", "9", "10", "11", "12", "14", "16", "18", "20", "24", "36", "48", "72" });
            _cmbFontSize.SelectedItem = "10";
            _cmbFontSize.SelectedIndexChanged += (s, e) => ChangeFontSize();

            // Style podstawowe
            var btnBold = CreateTsBtn("B", FontStyle.Bold, (s, e) => ToggleStyle(FontStyle.Bold));
            var btnItalic = CreateTsBtn("I", FontStyle.Italic, (s, e) => ToggleStyle(FontStyle.Italic));
            var btnUnder = CreateTsBtn("U", FontStyle.Underline, (s, e) => ToggleStyle(FontStyle.Underline));
            var btnStrike = CreateTsBtn("S", FontStyle.Strikeout, (s, e) => ToggleStyle(FontStyle.Strikeout));

            // Kolory
            var btnColor = new ToolStripButton("A") { ForeColor = Color.Red, Font = new Font("Segoe UI", 9, FontStyle.Bold), ToolTipText = "Kolor tekstu" };
            btnColor.Click += (s, e) => ChangeColor(false);
            var btnBack = new ToolStripButton("HL") { BackColor = Color.Yellow, ToolTipText = "Zakreślacz (Tło)" };
            btnBack.Click += (s, e) => ChangeColor(true);

            // --- NOWE FUNKCJONALNOŚCI ---
            var btnBullet = new ToolStripButton("•≡") { ToolTipText = "Lista punktowana" };
            btnBullet.Click += (s, e) => { _rtbContent.SelectionBullet = !_rtbContent.SelectionBullet; };

            var btnIndent = new ToolStripButton("→") { ToolTipText = "Zwiększ wcięcie" };
            btnIndent.Click += (s, e) => { _rtbContent.SelectionIndent += 20; };

            var btnOutdent = new ToolStripButton("←") { ToolTipText = "Zmniejsz wcięcie" };
            btnOutdent.Click += (s, e) => { _rtbContent.SelectionIndent = Math.Max(0, _rtbContent.SelectionIndent - 20); };

            var btnLink = new ToolStripButton("🔗") { ToolTipText = "Wstaw Link z nazwą" };
            btnLink.Click += (s, e) => InsertHyperlink();

            var btnTable = new ToolStripButton("▦") { ToolTipText = "Wstaw Tabelę (ASCII)" };
            btnTable.Click += (s, e) => InsertAsciiTable();

            var btnDate = new ToolStripButton("🕒") { ToolTipText = "Wstaw datę" };
            btnDate.Click += (s, e) => _rtbContent.AppendText(DateTime.Now.ToString("yyyy-MM-dd HH:mm") + " ");

            var btnClear = new ToolStripButton("🧹") { ToolTipText = "Wyczyść formatowanie" };
            btnClear.Click += (s, e) => { _rtbContent.SelectionFont = new Font("Segoe UI", 10); _rtbContent.SelectionColor = Color.Black; _rtbContent.SelectionBackColor = Color.White; _rtbContent.SelectionBullet = false; };

            _toolbar.Items.AddRange(new ToolStripItem[] {
                _cmbFontName, _cmbFontSize, new ToolStripSeparator(),
                btnBold, btnItalic, btnUnder, btnStrike, new ToolStripSeparator(),
                btnColor, btnBack, new ToolStripSeparator(),
                btnBullet, btnIndent, btnOutdent, new ToolStripSeparator(),
                btnLink, btnTable, btnDate, btnClear
            });

            var pnlEditor = new Panel { Dock = DockStyle.Fill, Padding = new Padding(5) };
            _rtbContent = new RichTextBox { Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle, Font = new Font("Segoe UI", 10), HideSelection = false, DetectUrls = true };
            // Obsługa kliknięcia w link
            _rtbContent.LinkClicked += (s, e) => { try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(e.LinkText) { UseShellExecute = true }); } catch { MessageBox.Show("Nieprawidłowy link."); } };

            _txtTags = new TextBox { PlaceholderText = "Tagi (oddzielone przecinkiem)", Dock = DockStyle.Bottom, Margin = new Padding(0, 5, 0, 0) };

            pnlEditor.Controls.Add(_rtbContent);
            pnlEditor.Controls.Add(_txtTags);
            pnlEditor.Controls.Add(_toolbar);

            _grpContent.Controls.Add(pnlEditor);

            // 4. BUTTONS
            var flowBtns = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, Height = 40 };
            _btnSave = new Button { Text = "Zapisz", DialogResult = DialogResult.OK, BackColor = Color.LightGreen, Height = 30, Width = 80 };
            _btnCancel = new Button { Text = "Anuluj", DialogResult = DialogResult.Cancel, Height = 30, Width = 80 };
            _btnSave.Click += (s, e) => SaveData();
            flowBtns.Controls.AddRange(new Control[] { _btnCancel, _btnSave });

            mainTable.Controls.Add(grpMain, 0, 0);
            mainTable.Controls.Add(_grpPath, 0, 1);
            mainTable.Controls.Add(_grpContent, 0, 2);
            mainTable.Controls.Add(flowBtns, 0, 3);

            this.Controls.Add(mainTable);
        }

        // --- HELPERS ---
        private void AddRow(TableLayoutPanel p, string lbl, Control c, int row)
        {
            p.Controls.Add(new Label { Text = lbl, AutoSize = true, Anchor = AnchorStyles.Right }, 0, row);
            p.Controls.Add(c, 1, row);
        }
        private ToolStripButton CreateTsBtn(string txt, FontStyle style, EventHandler action)
        {
            var btn = new ToolStripButton(txt);
            btn.Font = new Font("Times New Roman", 10, style);
            btn.Click += action;
            return btn;
        }

        // --- LOGIKA FORMATOWANIA ---
        private void ToggleStyle(FontStyle style) { if (_rtbContent.SelectionFont == null) return; FontStyle newStyle = _rtbContent.SelectionFont.Style ^ style; _rtbContent.SelectionFont = new Font(_rtbContent.SelectionFont, newStyle); }
        private void ChangeFontFamily() { if (_rtbContent.SelectionFont == null) return; _rtbContent.SelectionFont = new Font(_cmbFontName.Text, _rtbContent.SelectionFont.Size, _rtbContent.SelectionFont.Style); }
        private void ChangeFontSize() { if (float.TryParse(_cmbFontSize.Text, out float size) && _rtbContent.SelectionFont != null) _rtbContent.SelectionFont = new Font(_rtbContent.SelectionFont.FontFamily, size, _rtbContent.SelectionFont.Style); }
        private void ChangeColor(bool background) { using (var cd = new ColorDialog()) { if (cd.ShowDialog() == DialogResult.OK) { if (background) _rtbContent.SelectionBackColor = cd.Color; else _rtbContent.SelectionColor = cd.Color; } } }

        // --- ZAAWANSOWANE FUNKCJE (Tabela, Link) ---

        private void InsertHyperlink()
        {
            // Prosty input dialog
            using (var form = new Form())
            {
                form.Text = "Wstaw Link"; form.Size = new Size(300, 200); form.StartPosition = FormStartPosition.CenterParent; form.FormBorderStyle = FormBorderStyle.FixedDialog; form.MinimizeBox = false; form.MaximizeBox = false;
                var lbl1 = new Label { Text = "Tekst wyświetlany:", Top = 10, Left = 10, AutoSize = true };
                var txtText = new TextBox { Top = 30, Left = 10, Width = 260 };
                var lbl2 = new Label { Text = "Adres URL:", Top = 60, Left = 10, AutoSize = true };
                var txtUrl = new TextBox { Top = 80, Left = 10, Width = 260, Text = "http://" };
                var btnOk = new Button { Text = "OK", Top = 120, Left = 190, DialogResult = DialogResult.OK };
                form.Controls.AddRange(new Control[] { lbl1, txtText, lbl2, txtUrl, btnOk });

                if (form.ShowDialog() == DialogResult.OK)
                {
                    InsertRtfHyperlink(txtText.Text, txtUrl.Text);
                }
            }
        }

        // "Czarna magia" - wstrzykiwanie surowego kodu RTF, aby stworzyć prawdziwy link
        private void InsertRtfHyperlink(string text, string url)
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.Append(@"{\rtf1\ansi\ansicpg1252\deff0\nouicompat\deflang1033");
                sb.Append(@"{\fonttbl{\f0\fnil\fcharset0 Segoe UI;}}");
                sb.Append(@"\viewkind4\uc1\pard\f0\fs20");
                sb.Append(@"\field{\*\fldinst{HYPERLINK """ + url + @"""}}{\fldrslt{" + text + @"}}}");
                sb.Append(@"\par}"); // Nowa linia po linku

                _rtbContent.SelectedRtf = sb.ToString();
            }
            catch { _rtbContent.AppendText($"{text} ({url})"); } // Fallback
        }

        private void InsertAsciiTable()
        {
            // Prosty generator tabel tekstowych (idealny dla programistów)
            using (var form = new Form())
            {
                form.Text = "Tabela ASCII"; form.Size = new Size(250, 150); form.StartPosition = FormStartPosition.CenterParent;
                var numCols = new NumericUpDown { Minimum = 1, Maximum = 10, Value = 3, Top = 20, Left = 80, Width = 50 };
                var numRows = new NumericUpDown { Minimum = 1, Maximum = 20, Value = 3, Top = 50, Left = 80, Width = 50 };
                var btn = new Button { Text = "Wstaw", DialogResult = DialogResult.OK, Top = 80, Left = 80 };

                form.Controls.Add(new Label { Text = "Kolumny:", Top = 22, Left = 10 }); form.Controls.Add(numCols);
                form.Controls.Add(new Label { Text = "Wiersze:", Top = 52, Left = 10 }); form.Controls.Add(numRows);
                form.Controls.Add(btn);

                if (form.ShowDialog() == DialogResult.OK)
                {
                    StringBuilder sb = new StringBuilder();
                    string line = "+";
                    for (int i = 0; i < numCols.Value; i++) line += "----------+";
                    sb.AppendLine(line);

                    for (int r = 0; r < numRows.Value; r++)
                    {
                        string row = "|";
                        for (int c = 0; c < numCols.Value; c++) row += "          |";
                        sb.AppendLine(row);
                        sb.AppendLine(line);
                    }

                    // Zmieniamy czcionkę na monospaced dla tabeli, żeby się nie rozjechała
                    int start = _rtbContent.SelectionStart;
                    _rtbContent.SelectedText = sb.ToString();
                    _rtbContent.Select(start, sb.Length);
                    _rtbContent.SelectionFont = new Font("Consolas", 10);
                    _rtbContent.SelectionLength = 0;
                }
            }
        }

        // --- RESZTA LOGIKI (BEZ ZMIAN) ---
        private void BrowseFile()
        {
            if (_currentType == "folder") { using (var fbd = new FolderBrowserDialog()) { if (fbd.ShowDialog() == DialogResult.OK) _txtPath.Text = fbd.SelectedPath; } }
            else { using (var ofd = new OpenFileDialog { Filter = "Pliki|*.*" }) { if (ofd.ShowDialog() == DialogResult.OK) _txtPath.Text = ofd.FileName; } }
        }

        private void UpdateVisibility()
        {
            string t = _currentType?.ToLower();
            bool isApp = t == "app";
            bool isFolder = t == "folder";
            bool isNote = t == "note";
            bool isSnippet = t == "snippet" || t == "text";

            _grpPath.Visible = isApp || isFolder;
            _grpContent.Visible = isSnippet || isNote;
            _toolbar.Visible = isNote;
            _txtTags.Visible = isNote;

            if (isFolder) _grpPath.Text = "Wybierz Folder";
            if (isApp) _grpPath.Text = "Wybierz Aplikację";
        }

        private void LoadData()
        {
            _txtTitle.Text = Entry.Title ?? Entry.AppName;
            _txtDesc.Text = Entry.Description;
            _txtShortcut.Text = Entry.Shortcut;
            _chkPinned.Checked = Entry.Pinned ?? false;

            _txtPath.Text = Entry.AppPath ?? Entry.FolderPath;
            _txtArgs.Text = Entry.AppArgs;
            _chkAdmin.Checked = Entry.RunAsAdmin ?? false;

            string content = Entry.NoteText ?? Entry.PasteText;
            if (IsRtf(content)) _rtbContent.Rtf = content;
            else _rtbContent.Text = content;

            _txtTags.Text = Entry.GetTagsString();
        }

        private void SaveData()
        {
            Entry.Title = _txtTitle.Text;
            Entry.Description = _txtDesc.Text;
            Entry.Shortcut = _txtShortcut.Text;
            Entry.Pinned = _chkPinned.Checked;

            string t = _currentType?.ToLower();

            if (t == "app") { Entry.AppPath = _txtPath.Text; Entry.AppName = _txtTitle.Text; Entry.AppArgs = _txtArgs.Text; Entry.RunAsAdmin = _chkAdmin.Checked; Entry.PasteText = null; Entry.NoteText = null; }
            else if (t == "folder") { Entry.FolderPath = _txtPath.Text; Entry.AppPath = null; Entry.PasteText = null; }
            else
            {
                if (t == "note")
                {
                    Entry.NoteText = _rtbContent.Rtf;
                    Entry.PasteText = _rtbContent.Text;
                    string[] tagList = _txtTags.Text.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).ToArray();
                    Entry.TagsObject = tagList;
                }
                else
                {
                    Entry.PasteText = _rtbContent.Text;
                    Entry.NoteText = null;
                }
                Entry.AppPath = null; Entry.FolderPath = null;
            }
        }
    }
}
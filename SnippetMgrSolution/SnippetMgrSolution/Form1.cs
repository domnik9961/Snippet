using Microsoft.Extensions.Logging;
using SnippetMgr.Controls;
using SnippetMgr.Models;
using SnippetMgr.Services;
using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SnippetMgr
{
    public partial class Form1 : Form
    {
        // --- HOTKEY (Win+Y / Alt+Space) ---
        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private const int HOTKEY_ID = 9000;
        private const uint MOD_ALT = 0x0001;
        private const uint MOD_WIN = 0x0008;
        private const uint VK_Y = 0x59;
        private const uint VK_SPACE = 0x20;

        // --- KOLORY / STYLE ---
        private readonly Color C_Background = Color.FromArgb(245, 246, 250);
        private readonly Color C_DarkSide = Color.FromArgb(32, 34, 37);
        private readonly Color C_Accent = Color.FromArgb(0, 122, 204);

        private readonly Font _fontMain = new Font("Segoe UI", 10F);
        private readonly Font _fontCode = new Font("Consolas", 10.5F);

        // --- SERWISY ---
        private readonly IConfigService _configService;
        private readonly ICommandService _commandService;
        private readonly ISqlService _sqlService;
        private readonly ILogger<Form1> _logger;

        // --- KONFIG ---
        private RootConfig _config = new();

        // --- GŁÓWNE KONTROLKI ---
        private TabControl _tabControl = null!;
        private TextBox _txtSearch = null!;
        private NotifyIcon _trayIcon = null!;
        private ToolStripStatusLabel _statusLabel = null!;
        private ToolTip _toolTip = null!;

        // --- SQL LAB KONTROLKI ---
        private TreeView _treeTables = null!;
        private TextBox _txtTableSearch = null!;
        private TextBox _txtSelectedTable = null!;
        private SqlEditor _sqlEditor = null!;
        private TextBox _txtSrv = null!;
        private ComboBox _cmbDb = null!;
        private Button _btnConnect = null!;

        // Pola generatora (kluczowe dla "Generuj z Tabeli")
        private TextBox _txtWhere = null!;
        private ComboBox _cmbTables = null!;

        // Kontrolki eksportu i opcji
        private TextBox _txtExpQuery = null!;
        private Button _btnExpPreview = null!;
        private Button _btnExpFile = null!;
        private CheckBox _chkT = null!; // #temp
        private CheckBox _chkD = null!; // INSERT
        private CheckBox _chkTr = null!; // Tran
        private RichTextBox _rtbScript = null!;

        // Zakładki wyników
        private TabPage _tabScript = null!;
        private TabPage _tabResultGrids = null!;
        private TabPage _tabMessages = null!;
        private TabPage _tabLinear = null!;
        private TabControl _resultTabs = null!;
        private TabControl _gridTabs = null!;
        private TextBox _txtMessages = null!;
        private RichTextBox _rtbLinear = null!;

        // Przyciski akcji
        private Button _btnGenTable = null!;    // Generator (z tabeli)
        private Button _btnRunQuery = null!;    // JEDEN przycisk w "Własny SQL"

        public Form1(IConfigService configService, ICommandService commandService, ISqlService sqlService, ILogger<Form1> logger)
        {
            _configService = configService;
            _commandService = commandService;
            _sqlService = sqlService;
            _logger = logger;

            InitializeModernUI();
            SetupTrayIcon();
        }

        // --- LIFECYCLE ---
        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);

            try
            {
                _config = _configService.LoadConfig() ?? new RootConfig();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading config");
                _config = new RootConfig();
            }

            ApplyConfig();

            try
            {
                RegisterHotKey(Handle, HOTKEY_ID, MOD_WIN, VK_Y);
                RegisterHotKey(Handle, HOTKEY_ID, MOD_ALT, VK_SPACE);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error registering hotkeys");
            }

            SetStatus("Gotowy.");
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            try { UnregisterHotKey(Handle, HOTKEY_ID); } catch { }

            if (_trayIcon != null)
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
            }

            base.OnFormClosed(e);
        }

        // --- TRAY + HOTKEY ---
        private void SetupTrayIcon()
        {
            _trayIcon = new NotifyIcon
            {
                Icon = SystemIcons.Application,
                Visible = true,
                Text = "SnippetMgr"
            };

            var menu = new ContextMenuStrip();
            menu.Items.Add("Pokaż / Ukryj", null, (s, e) => ToggleWindow());
            menu.Items.Add("Zakończ", null, (s, e) => Close());
            _trayIcon.ContextMenuStrip = menu;

            _trayIcon.DoubleClick += (s, e) => ToggleWindow();
        }

        protected override void WndProc(ref Message m)
        {
            const int WM_HOTKEY = 0x0312;

            if (m.Msg == WM_HOTKEY && m.WParam.ToInt32() == HOTKEY_ID)
            {
                ToggleWindow();
            }

            base.WndProc(ref m);
        }

        private void ToggleWindow()
        {
            if (Visible)
            {
                Hide();
            }
            else
            {
                _commandService.SaveLastWindowHandle();

                Show();
                if (WindowState == FormWindowState.Minimized)
                    WindowState = FormWindowState.Normal;

                Activate();
                BringToFront();

                if (_tabControl.TabCount > 1)
                    _tabControl.SelectedIndex = 1;

                _txtSearch.Text = string.Empty;
                _txtSearch.Focus();
            }
        }

        // --- UI INIT ---
        private void InitializeModernUI()
        {
            Text = "SnippetMgr Enterprise";
            StartPosition = FormStartPosition.CenterScreen;
            Width = 1200;
            Height = 800;
            BackColor = C_Background;
            Font = _fontMain;

            _toolTip = new ToolTip();

            // Status
            var statusStrip = new StatusStrip();
            _statusLabel = new ToolStripStatusLabel("Inicjalizacja...");
            statusStrip.Items.Add(_statusLabel);
            Controls.Add(statusStrip);

            // Bottom (search + config)
            var bottomPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 50,
                BackColor = Color.White,
                Padding = new Padding(10)
            };
            bottomPanel.Paint += (s, e) =>
            {
                e.Graphics.DrawLine(Pens.LightGray, 0, 0, bottomPanel.Width, 0);
            };

            _txtSearch = new TextBox
            {
                Width = 400,
                PlaceholderText = "🔍 Szukaj...",
                Font = new Font("Segoe UI", 11F),
                BorderStyle = BorderStyle.FixedSingle
            };
            _txtSearch.TextChanged += (s, e) => FilterGrid(_txtSearch.Text);

            var btnCfg = new Button
            {
                Text = "⚙ Config",
                AutoSize = true,
                Dock = DockStyle.Right,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.White
            };
            btnCfg.FlatAppearance.BorderSize = 0;
            btnCfg.Click += (s, e) =>
            {
                try
                {
                    var path = _configService.GetConfigPath();
                    if (!System.IO.File.Exists(path))
                        System.IO.File.WriteAllText(path, "{}");

                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("notepad.exe", $"\"{path}\"")
                    {
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Nie udało się otworzyć konfiguracji: " + ex.Message);
                }
            };

            bottomPanel.Controls.Add(_txtSearch);
            bottomPanel.Controls.Add(btnCfg);
            Controls.Add(bottomPanel);

            // Tabs container
            var tabsContainer = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(0, 10, 0, 0)
            };

            _tabControl = new TabControl
            {
                Dock = DockStyle.Fill,
                Font = _fontMain,
                Alignment = TabAlignment.Top,
                ItemSize = new Size(140, 35),
                SizeMode = TabSizeMode.Fixed,
                Padding = new Point(15, 5)
            };
            _tabControl.SelectedIndexChanged += (s, e) =>
            {
                FilterGrid(_txtSearch.Text);
                _txtSearch.Focus();
            };

            tabsContainer.Controls.Add(_tabControl);
            Controls.Add(tabsContainer);

            bottomPanel.BringToFront();
        }

        // --- KONFIG + ZAKŁADKI ---
        private void ApplyConfig()
        {
            _tabControl.TabPages.Clear();

            if (_config.Config?.Sql != null)
            {
                _sqlService.Configure(
                    _config.Config.Sql.Server,
                    _config.Config.Sql.UseWindowsAuth,
                    _config.Config.Sql.User,
                    _config.Config.Sql.Password
                );
            }

            BuildSqlGeneratorTab();

            int i = 2;
            if (_config.SqlSnippets != null && _config.SqlSnippets.Count > 0)
                BuildSmartTab($"{i++}. Snippety", _config.SqlSnippets, "snippet");
            if (_config.Apps != null && _config.Apps.Count > 0)
                BuildSmartTab($"{i++}. Aplikacje", _config.Apps, "app");
            if (_config.Folders != null && _config.Folders.Count > 0)
                BuildSmartTab($"{i++}. Foldery", _config.Folders, "folder");
            if (_config.Notes != null && _config.Notes.Count > 0)
                BuildSmartTab($"{i++}. Notatki", _config.Notes, "note");
            if (_config.Tabs != null)
            {
                foreach (var tab in _config.Tabs)
                    BuildSmartTab($"{i++}. {tab.Name}", tab.Entries, "snippet");
            }
        }

        // --- 1. SQL LAB ---
        private void BuildSqlGeneratorTab()
        {
            var page = new TabPage("1. SQL Lab")
            {
                BackColor = C_Background
            };

            var mainSplit = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                SplitterDistance = 650,
                FixedPanel = FixedPanel.Panel1
            };

            // LEWA STRONA – połączenie i tabele
            var sidePanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = C_DarkSide,
                Padding = new Padding(12)
            };

            var lblConn = new Label
            {
                Text = "POŁĄCZENIE SQL",
                ForeColor = Color.White,
                Dock = DockStyle.Top,
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                Height = 24
            };
            sidePanel.Controls.Add(lblConn);

            var connLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                ColumnCount = 1,
                RowCount = 6,
                AutoSize = true,
                Padding = new Padding(0, 8, 0, 8)
            };

            _txtSrv = new TextBox
            {
                Dock = DockStyle.Top,
                BorderStyle = BorderStyle.FixedSingle,
                Text = _config.Config?.Sql?.Server ?? "."
            };

            _cmbDb = new ComboBox
            {
                Dock = DockStyle.Top,
                DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Flat
            };

            _btnConnect = new Button
            {
                Text = "Połącz z serwerem",
                Dock = DockStyle.Top,
                FlatStyle = FlatStyle.Flat,
                BackColor = C_Accent,
                ForeColor = Color.White,
                Height = 32
            };
            _btnConnect.FlatAppearance.BorderSize = 0;

            _txtTableSearch = new TextBox
            {
                Dock = DockStyle.Top,
                BorderStyle = BorderStyle.FixedSingle,
                PlaceholderText = "Szukaj tabel..."
            };

            _treeTables = new TreeView
            {
                Dock = DockStyle.Fill,
                BackColor = C_DarkSide,
                ForeColor = Color.WhiteSmoke,
                BorderStyle = BorderStyle.None,
                HideSelection = false
            };

            connLayout.Controls.Add(new Label { Text = "Serwer:", ForeColor = Color.WhiteSmoke, AutoSize = true }, 0, 0);
            connLayout.Controls.Add(_txtSrv, 0, 1);
            connLayout.Controls.Add(new Label { Text = "Baza danych:", ForeColor = Color.WhiteSmoke, AutoSize = true, Margin = new Padding(0, 6, 0, 0) }, 0, 2);
            connLayout.Controls.Add(_cmbDb, 0, 3);
            connLayout.Controls.Add(_btnConnect, 0, 4);
            connLayout.Controls.Add(_txtTableSearch, 0, 5);

            sidePanel.Controls.Add(_treeTables);
            sidePanel.Controls.Add(connLayout);

            mainSplit.Panel1.Controls.Add(sidePanel);

            // PRAWA STRONA – akcje + wyniki
            var rightPanel = new Panel
            {
                Dock = DockStyle.Fill
            };

            var workSplit = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                SplitterWidth = 6
            };

            page.Resize += (s, e) =>
            {
                if (workSplit.Height > 0)
                    workSplit.SplitterDistance = (int)(workSplit.Height * 0.66);
            };

            // GÓRA: zakładki akcji
            var actionTabs = new TabControl
            {
                Dock = DockStyle.Fill
            };

            // GENERATOR
            var tGen = new TabPage("Generator");

            var genOuter = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Padding = new Padding(18)
            };
            genOuter.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            genOuter.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            genOuter.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            // KARTA 1 – wybór tabeli
            var pnlSource = new Panel
            {
                Dock = DockStyle.Top,
                Height = 130,
                BackColor = Color.White,
                Padding = new Padding(12),
                Margin = new Padding(0, 0, 0, 10),
                BorderStyle = BorderStyle.FixedSingle
            };

            var lblStep1 = new Label
            {
                Text = "Krok 1. Wybierz tabelę",
                Dock = DockStyle.Top,
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                Height = 22
            };

            var lblTable = new Label
            {
                Text = "Tabela (schema.nazwa):",
                Dock = DockStyle.Top,
                Font = new Font("Segoe UI", 8.5F),
                Margin = new Padding(0, 6, 0, 0),
                Height = 18
            };

            _cmbTables = new ComboBox
            {
                Dock = DockStyle.Top,
                DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Flat,
                Margin = new Padding(0, 2, 0, 0)
            };

            var lblSelected = new Label
            {
                Text = "Zaznaczona tabela:",
                Dock = DockStyle.Top,
                Font = new Font("Segoe UI", 8.5F),
                Margin = new Padding(0, 8, 0, 0),
                Height = 18
            };

            _txtSelectedTable = new TextBox
            {
                Dock = DockStyle.Top,
                ReadOnly = true,
                BackColor = Color.FromArgb(245, 246, 250),
                BorderStyle = BorderStyle.FixedSingle,
                Margin = new Padding(0, 2, 0, 0)
            };

            pnlSource.Controls.Add(_txtSelectedTable);
            pnlSource.Controls.Add(lblSelected);
            pnlSource.Controls.Add(_cmbTables);
            pnlSource.Controls.Add(lblTable);
            pnlSource.Controls.Add(lblStep1);

            // KARTA 2 – filtr WHERE
            var pnlFilter = new Panel
            {
                Dock = DockStyle.Top,
                Height = 110,
                BackColor = Color.White,
                Padding = new Padding(12),
                Margin = new Padding(0, 0, 0, 10),
                BorderStyle = BorderStyle.FixedSingle
            };

            var lblStep2 = new Label
            {
                Text = "Krok 2. Opcjonalny filtr (WHERE)",
                Dock = DockStyle.Top,
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                Height = 22
            };

            var lblHint = new Label
            {
                Text = "Przykład: IsActive = 1 AND CreatedDate > '2024-01-01'",
                Dock = DockStyle.Top,
                Font = new Font("Segoe UI", 8F),
                ForeColor = Color.Gray,
                Height = 18,
                Margin = new Padding(0, 4, 0, 4)
            };

            _txtWhere = new TextBox
            {
                Dock = DockStyle.Top,
                BorderStyle = BorderStyle.FixedSingle,
                PlaceholderText = "Warunek bez słowa WHERE (lub pełny fragment WHERE ...)",
                Margin = new Padding(0, 2, 0, 0)
            };

            pnlFilter.Controls.Add(_txtWhere);
            pnlFilter.Controls.Add(lblHint);
            pnlFilter.Controls.Add(lblStep2);

            // KARTA 3 – przycisk generowania
            var pnlGenerate = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                Padding = new Padding(0, 8, 0, 0)
            };

            _btnGenTable = new Button
            {
                Text = "⚡ Generuj skrypt + podgląd",
                Dock = DockStyle.Right,
                Width = 260,
                Height = 44,
                BackColor = C_Accent,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI Semibold", 10.5F)
            };
            _btnGenTable.FlatAppearance.BorderSize = 0;

            pnlGenerate.Controls.Add(_btnGenTable);

            genOuter.Controls.Add(pnlSource, 0, 0);
            genOuter.Controls.Add(pnlFilter, 0, 1);
            genOuter.Controls.Add(pnlGenerate, 0, 2);

            tGen.Controls.Add(genOuter);

            // WŁASNY SQL
            var tSql = new TabPage("Własny SQL");
            _sqlEditor = new SqlEditor { Dock = DockStyle.Fill };
            if (_config.SqlSnippets != null)
                _sqlEditor.SetSnippets(_config.SqlSnippets);

            var sqlBottom = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 45,
                Padding = new Padding(5)
            };

            _btnRunQuery = new Button
            {
                Text = "URUCHOM + GENERUJ (F5)",
                Dock = DockStyle.Right,
                Width = 190,
                BackColor = C_Accent,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            _btnRunQuery.FlatAppearance.BorderSize = 0;

            sqlBottom.Controls.Add(_btnRunQuery);

            tSql.Controls.Add(_sqlEditor);
            tSql.Controls.Add(sqlBottom);

            // EKSPORT LINIOWY
            var tExp = new TabPage("Eksport Liniowy");
            var expLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Padding = new Padding(20)
            };
            expLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            expLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            expLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            _txtExpQuery = new TextBox
            {
                Multiline = true,
                Dock = DockStyle.Fill,
                ScrollBars = ScrollBars.Vertical,
                Font = _fontCode,
                PlaceholderText = "SELECT..."
            };

            _btnExpPreview = new Button
            {
                Text = "Podgląd",
                BackColor = C_Accent,
                ForeColor = Color.White,
                Dock = DockStyle.Top,
                Height = 35,
                FlatStyle = FlatStyle.Flat
            };
            _btnExpPreview.FlatAppearance.BorderSize = 0;

            _btnExpFile = new Button
            {
                Text = "Do pliku",
                BackColor = Color.SeaGreen,
                ForeColor = Color.White,
                Dock = DockStyle.Top,
                Height = 35,
                FlatStyle = FlatStyle.Flat
            };
            _btnExpFile.FlatAppearance.BorderSize = 0;

            expLayout.Controls.Add(_txtExpQuery, 0, 0);
            expLayout.Controls.Add(_btnExpPreview, 0, 1);
            expLayout.Controls.Add(_btnExpFile, 0, 2);
            tExp.Controls.Add(expLayout);

            actionTabs.TabPages.Add(tGen);
            actionTabs.TabPages.Add(tSql);
            actionTabs.TabPages.Add(tExp);

            var optionsPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 30,
                BackColor = Color.White,
                Padding = new Padding(12, 4, 0, 4)
            };
            _chkT = new CheckBox { Text = "#temp", Checked = true, AutoSize = true };
            _chkD = new CheckBox { Text = "INSERT", AutoSize = true, Margin = new Padding(12, 3, 0, 0) };
            _chkTr = new CheckBox { Text = "Tran", AutoSize = true, Margin = new Padding(12, 3, 0, 0) };
            optionsPanel.Controls.Add(_chkT);
            optionsPanel.Controls.Add(_chkD);
            optionsPanel.Controls.Add(_chkTr);

            workSplit.Panel1.Controls.Add(actionTabs);
            workSplit.Panel1.Controls.Add(optionsPanel);

            // DÓŁ: wyniki
            _resultTabs = new TabControl { Dock = DockStyle.Fill };

            // SKRYPT
            _tabScript = new TabPage("Skrypt");
            _rtbScript = new RichTextBox
            {
                Dock = DockStyle.Fill,
                Font = _fontCode,
                AcceptsTab = true,
                WordWrap = false
            };
            _rtbScript.TextChanged += (s, e) =>
            {
                SqlSyntaxHighlighter.Highlight(_rtbScript);
            };
            _tabScript.Controls.Add(_rtbScript);

            // WYNIKI – wiele gridów
            _tabResultGrids = new TabPage("Wyniki");
            _gridTabs = new TabControl
            {
                Dock = DockStyle.Fill,
                Alignment = TabAlignment.Top,
                Multiline = true
            };
            _tabResultGrids.Controls.Add(_gridTabs);

            // KOMUNIKATY
            _tabMessages = new TabPage("Komunikaty");
            _txtMessages = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                ReadOnly = true,
                Font = _fontCode
            };
            _tabMessages.Controls.Add(_txtMessages);

            // LINIOWY
            _tabLinear = new TabPage("Liniowy");
            _rtbLinear = new RichTextBox
            {
                Dock = DockStyle.Fill,
                Font = _fontCode,
                ReadOnly = true,
                BackColor = Color.WhiteSmoke
            };
            _tabLinear.Controls.Add(_rtbLinear);

            _resultTabs.TabPages.Add(_tabScript);
            _resultTabs.TabPages.Add(_tabResultGrids);
            _resultTabs.TabPages.Add(_tabMessages);
            _resultTabs.TabPages.Add(_tabLinear);

            workSplit.Panel2.Controls.Add(_resultTabs);

            rightPanel.Controls.Add(workSplit);
            mainSplit.Panel2.Controls.Add(rightPanel);

            page.Controls.Add(mainSplit);
            _tabControl.TabPages.Add(page);

            // --- LOGIKA / EVENTY SQL LAB ---

            _btnConnect.Click += async (s, e) =>
            {
                try
                {
                    _btnConnect.Enabled = false;
                    _btnConnect.Text = "Łączenie...";

                    var sqlCfg = _config.Config.Sql ?? new SqlConfig();
                    sqlCfg.Server = _txtSrv.Text;
                    _config.Config.Sql = sqlCfg;

                    bool useWinAuth = sqlCfg.UseWindowsAuth;
                    if (!useWinAuth && string.IsNullOrWhiteSpace(sqlCfg.User))
                    {
                        useWinAuth = true;
                    }

                    string? login = useWinAuth ? null : sqlCfg.User;
                    string? password = useWinAuth ? null : sqlCfg.Password;

                    _sqlService.Configure(sqlCfg.Server, useWinAuth, login, password);

                    var dbs = await _sqlService.GetDatabasesAsync();
                    _cmbDb.Items.Clear();
                    foreach (var d in dbs)
                        _cmbDb.Items.Add(d);

                    if (!string.IsNullOrWhiteSpace(sqlCfg.DefaultDatabase) &&
                        _cmbDb.Items.Contains(sqlCfg.DefaultDatabase))
                    {
                        _cmbDb.SelectedItem = sqlCfg.DefaultDatabase;
                    }
                    else if (_cmbDb.Items.Count > 0)
                    {
                        _cmbDb.SelectedIndex = 0;
                    }

                    _btnConnect.Text = "Połączono";
                    _txtSrv.BackColor = Color.Honeydew;

                    _configService.SaveConfig(_config);
                }
                catch (Exception ex)
                {
                    _btnConnect.Text = "BŁĄD";
                    _txtSrv.BackColor = Color.MistyRose;

                    MessageBox.Show(
                        "Błąd połączenia (logowanie do SQL Server):\r\n\r\n" + ex.Message,
                        "Połączenie SQL",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }
                finally
                {
                    _btnConnect.Enabled = true;
                }
            };

            _cmbDb.SelectedIndexChanged += async (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(_cmbDb.Text))
                    return;

                try
                {
                    _treeTables.Nodes.Clear();
                    var tables = await _sqlService.GetTablesAsync(_cmbDb.Text);
                    _treeTables.BeginUpdate();
                    foreach (var t in tables)
                    {
                        var node = new TreeNode(t);
                        node.Tag = "table";
                        node.Nodes.Add("."); // lazy-load placeholder
                        _treeTables.Nodes.Add(node);
                    }
                    _treeTables.EndUpdate();

                    var ac = new AutoCompleteStringCollection();
                    ac.AddRange(tables.ToArray());
                    _txtTableSearch.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
                    _txtTableSearch.AutoCompleteSource = AutoCompleteSource.CustomSource;
                    _txtTableSearch.AutoCompleteCustomSource = ac;

                    _sqlEditor.SetDatabaseObjects(tables);
                    _cmbTables.Items.Clear();
                    foreach (var t in tables)
                        _cmbTables.Items.Add(t);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Błąd pobierania tabel: " + ex.Message);
                }
            };

            // Obsługa nawiasów i poprawne parsowanie nazwy tabeli przy rozwijaniu drzewa
            _treeTables.BeforeExpand += async (s, e) =>
            {
                if (e.Node.Nodes.Count == 1 && e.Node.Nodes[0].Text == ".")
                {
                    try
                    {
                        e.Node.Nodes.Clear();
                        var cleanName = e.Node.Text.Replace("[", "").Replace("]", "");
                        var parts = cleanName.Split('.');

                        if (parts.Length == 2)
                        {
                            var cols = await _sqlService.GetColumnsAsync(_cmbDb.Text, parts[0], parts[1]);
                            foreach (var c in cols)
                            {
                                var node = e.Node.Nodes.Add(c);
                                node.ForeColor = Color.Gray;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("Błąd pobierania kolumn: " + ex.Message);
                    }
                }
            };

            _treeTables.NodeMouseDoubleClick += (s, e) =>
            {
                if (e.Node.Parent == null)
                {
                    _txtSelectedTable.Text = e.Node.Text;
                    _cmbTables.Text = e.Node.Text;
                }
            };

            string WrapTran(string sql)
            {
                if (_chkTr.Checked)
                    return "BEGIN TRAN;\r\n" + sql + "\r\nCOMMIT;";
                return sql;
            }

            // --- WSPÓLNA METODA DLA GENEROWANIA Z TABELI ---
            async Task GenerateTableScriptAsync()
            {
                if (string.IsNullOrWhiteSpace(_cmbDb.Text) || string.IsNullOrWhiteSpace(_cmbTables.Text))
                {
                    MessageBox.Show("Wybierz tabelę z listy (lub w panelu bocznym).", "Brak tabeli", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                try
                {
                    var cleanName = _cmbTables.Text.Replace("[", "").Replace("]", "");
                    var parts = cleanName.Split('.');

                    if (parts.Length != 2)
                    {
                        MessageBox.Show("Niepoprawny format nazwy tabeli. Oczekiwano: Schema.Table", "Błąd", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }

                    var schema = parts[0];
                    var table = parts[1];

                    var whereText = _txtWhere.Text?.Trim() ?? string.Empty;

                    var create = await _sqlService.GenerateCreateTableAsync(_cmbDb.Text, schema, table, _chkT.Checked);
                    var insert = _chkD.Checked
                        ? await _sqlService.GenerateInsertAsync(_cmbDb.Text, schema, table, whereText, _chkT.Checked)
                        : string.Empty;

                    var script = create + Environment.NewLine + insert;
                    _rtbScript.Text = WrapTran(script);
                    SqlSyntaxHighlighter.Highlight(_rtbScript);
                    _resultTabs.SelectedTab = _tabScript;

                    var sb = new StringBuilder();
                    sb.Append($"SELECT TOP 100 * FROM [{schema}].[{table}]");

                    if (!string.IsNullOrWhiteSpace(whereText))
                    {
                        if (whereText.StartsWith("WHERE ", StringComparison.OrdinalIgnoreCase))
                            sb.Append(" ").Append(whereText);
                        else
                            sb.Append(" WHERE ").Append(whereText);
                    }

                    await RunQueriesAsync(_cmbDb.Text, sb.ToString());
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Błąd generowania skryptu: " + ex.Message);
                }
            }

            // 1. GENERATOR – skrypt + SELECT TOP 100
            _btnGenTable.Click += async (s, e) => await GenerateTableScriptAsync();

            // 2. WŁASNY SQL – JEDEN PRZYCISK: URUCHOM + (JEŚLI SELECT) GENERUJ CREATE TABLE #TMP + INSERT
            _btnRunQuery.Click += async (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(_cmbDb.Text))
                {
                    MessageBox.Show("Najpierw wybierz bazę danych.", "Brak bazy",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                try
                {
                    SetStatus("Wykonywanie zapytania i generowanie skryptu...");

                    var rawQuery = !string.IsNullOrWhiteSpace(_sqlEditor.InnerEditor.SelectedText)
                        ? _sqlEditor.InnerEditor.SelectedText
                        : _sqlEditor.Text;

                    if (string.IsNullOrWhiteSpace(rawQuery))
                    {
                        MessageBox.Show("Brak zapytania w edytorze.", "SQL",
                            MessageBoxButtons.OK, MessageBoxIcon.Information);
                        SetStatus("Gotowy.");
                        return;
                    }

                    // 1) rozbij na instrukcje i znajdź pierwszy SELECT
                    var statements = SplitSqlStatements(rawQuery);
                    var firstSelect = statements
                        .FirstOrDefault(st => st.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase));

                    // 2) jeśli jest SELECT → generujemy CREATE TABLE #TmpResult(...) + INSERT
                    if (!string.IsNullOrWhiteSpace(firstSelect))
                    {
                        var previewRes = await _sqlService.ExecuteQueryAsync(_cmbDb.Text, firstSelect);

                        if (previewRes.Data != null && previewRes.Data.Columns.Count > 0)
                        {
                            var dt = previewRes.Data;
                            string tempName = _chkT.Checked ? "#TmpResult" : "TmpResult";

                            var sbCreate = new StringBuilder();
                            sbCreate.AppendLine($"CREATE TABLE {tempName} (");

                            for (int i = 0; i < dt.Columns.Count; i++)
                            {
                                var col = dt.Columns[i];
                                string sqlType = MapDotNetTypeToSql(col.DataType);
                                string nullable = col.AllowDBNull ? "NULL" : "NOT NULL";

                                sbCreate.Append($"    [{col.ColumnName}] {sqlType} {nullable}");
                                if (i < dt.Columns.Count - 1)
                                    sbCreate.Append(",");
                                sbCreate.AppendLine();
                            }

                            sbCreate.AppendLine(");");

                            var sbScript = new StringBuilder();
                            sbScript.AppendLine(sbCreate.ToString());

                            if (_chkD.Checked && dt.Rows.Count > 0)
                            {
                                // INSERT z wartościami
                                var colNames = dt.Columns.Cast<DataColumn>()
                                    .Select(c => $"[{c.ColumnName}]")
                                    .ToArray();
                                string colList = string.Join(", ", colNames);

                                foreach (DataRow row in dt.Rows)
                                {
                                    var values = new List<string>();
                                    foreach (DataColumn c in dt.Columns)
                                    {
                                        values.Add(FormatSqlLiteral(row[c], c.DataType));
                                    }

                                    sbScript.Append("INSERT INTO ").Append(tempName)
                                            .Append(" (").Append(colList).Append(") VALUES (")
                                            .Append(string.Join(", ", values)).AppendLine(");");
                                }
                            }

                            _rtbScript.Text = WrapTran(sbScript.ToString());
                            SqlSyntaxHighlighter.Highlight(_rtbScript);
                            _resultTabs.SelectedTab = _tabScript;
                        }
                    }

                    // 3) zawsze wykonaj cały SQL (wszystkie zapytania)
                    await RunQueriesAsync(_cmbDb.Text, rawQuery);

                    SetStatus("Gotowe.");
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, "Błąd zapytania",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    SetStatus("Błąd");
                }
            };

            _btnExpPreview.Click += async (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(_cmbDb.Text))
                    return;

                try
                {
                    var txt = await _sqlService.ExportFixedLineAsync(_cmbDb.Text, _txtExpQuery.Text);
                    _rtbLinear.Text = txt;
                    _resultTabs.SelectedTab = _tabLinear;
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Błąd eksportu: " + ex.Message);
                }
            };

            _btnExpFile.Click += async (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(_cmbDb.Text))
                    return;

                using var dlg = new SaveFileDialog
                {
                    Filter = "Text files|*.txt|All files|*.*",
                    DefaultExt = "txt"
                };

                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        var n = await _sqlService.ExportFixedLineToFileAsync(_cmbDb.Text, _txtExpQuery.Text, dlg.FileName);
                        MessageBox.Show($"Zapisano {n} linii do pliku.");
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("Błąd eksportu: " + ex.Message);
                    }
                }
            };

            // DRAG & DROP + rozwijane pole zaznaczonej tabeli
            SetupTreeToEditorDragDrop();
        }

        // --- Multi-query: rozbijanie na instrukcje i wyświetlanie wielu gridów ---
        private async Task RunQueriesAsync(string db, string sql)
        {
            var statements = SplitSqlStatements(sql);
            if (statements.Count == 0)
                return;

            var results = new List<QueryResult>();
            foreach (var stmt in statements)
            {
                var res = await _sqlService.ExecuteQueryAsync(db, stmt);
                results.Add(res);
            }

            ShowSqlResults(results);
        }

        private List<string> SplitSqlStatements(string sql)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(sql))
                return result;

            // 1. proste rozbicie po ';' (poza stringami)
            var tmp = new List<string>();
            var sb = new StringBuilder();
            bool inString = false;

            for (int i = 0; i < sql.Length; i++)
            {
                char c = sql[i];

                if (c == '\'')
                {
                    sb.Append(c);
                    inString = !inString;
                }
                else if (!inString && c == ';')
                {
                    var part = sb.ToString().Trim();
                    if (part.Length > 0)
                        tmp.Add(part);
                    sb.Clear();
                }
                else
                {
                    sb.Append(c);
                }
            }

            var tail = sb.ToString().Trim();
            if (tail.Length > 0)
                tmp.Add(tail);

            // 2. rozbicie po liniach GO
            foreach (var block in tmp)
            {
                var lines = block.Replace("\r", "").Split('\n');
                var buf = new StringBuilder();

                foreach (var line in lines)
                {
                    if (line.Trim().Equals("GO", StringComparison.OrdinalIgnoreCase))
                    {
                        var stmt = buf.ToString().Trim();
                        if (stmt.Length > 0)
                            result.Add(stmt);
                        buf.Clear();
                    }
                    else
                    {
                        buf.AppendLine(line);
                    }
                }

                var last = buf.ToString().Trim();
                if (last.Length > 0)
                    result.Add(last);
            }

            if (result.Count == 0)
                result.Add(sql.Trim());

            return result;
        }

        private void ShowSqlResults(List<QueryResult> results)
        {
            _txtMessages.Clear();
            _rtbLinear.Clear();
            _gridTabs.TabPages.Clear();

            var allMessages = new List<string>();
            string? firstPreview = null;
            int gridIndex = 1;

            foreach (var res in results)
            {
                if (res.Messages != null && res.Messages.Count > 0)
                    allMessages.AddRange(res.Messages);

                if (firstPreview == null && !string.IsNullOrEmpty(res.FixedWidthPreview))
                    firstPreview = res.FixedWidthPreview;

                if (res.Data != null && res.Data.Rows.Count > 0)
                {
                    var tp = new TabPage($"Grid {gridIndex++}");
                    var grid = CreateResultGrid();
                    grid.DataSource = res.Data;
                    tp.Controls.Add(grid);
                    _gridTabs.TabPages.Add(tp);
                }
            }

            _txtMessages.Text = string.Join(Environment.NewLine, allMessages);

            if (firstPreview != null)
                _rtbLinear.Text = firstPreview;

            if (_gridTabs.TabPages.Count > 0)
                _resultTabs.SelectedTab = _tabResultGrids;
            else if (allMessages.Count > 0)
                _resultTabs.SelectedTab = _tabMessages;
        }

        private DataGridView CreateResultGrid()
        {
            var g = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AutoGenerateColumns = true,
                ScrollBars = ScrollBars.Both
            };

            StyleModernGrid(g);

            g.DataError += (s, e) =>
            {
                MessageBox.Show("Błąd DataGridView: " + e.Exception?.Message,
                    "DataGridView.DataError",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                e.ThrowException = false;
            };

            return g;
        }

        private void StyleModernGrid(DataGridView g)
        {
            g.BackgroundColor = Color.White;
            g.BorderStyle = BorderStyle.None;
            g.EnableHeadersVisualStyles = false;
            g.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(245, 245, 245);
            g.ColumnHeadersDefaultCellStyle.ForeColor = Color.Black;
            g.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            g.RowHeadersVisible = false;
            g.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            g.MultiSelect = false;
            g.AllowUserToAddRows = false;
            g.AllowUserToDeleteRows = false;
            g.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells;
        }

        // Mapowanie typów .NET → SQL
        private string MapDotNetTypeToSql(Type t)
        {
            if (t == typeof(string) || t == typeof(char))
                return "NVARCHAR(MAX)";
            if (t == typeof(int))
                return "INT";
            if (t == typeof(long))
                return "BIGINT";
            if (t == typeof(short))
                return "SMALLINT";
            if (t == typeof(byte))
                return "TINYINT";
            if (t == typeof(bool))
                return "BIT";
            if (t == typeof(DateTime))
                return "DATETIME2";
            if (t == typeof(decimal))
                return "DECIMAL(18,4)";
            if (t == typeof(double))
                return "FLOAT";
            if (t == typeof(float))
                return "REAL";
            if (t == typeof(Guid))
                return "UNIQUEIDENTIFIER";
            if (t == typeof(byte[]))
                return "VARBINARY(MAX)";

            // fallback
            return "NVARCHAR(MAX)";
        }

        // Formatowanie wartości na literały SQL
        private string FormatSqlLiteral(object? value, Type type)
        {
            if (value == null || value == DBNull.Value)
                return "NULL";

            if (type == typeof(string) || type == typeof(char))
            {
                var s = value.ToString() ?? string.Empty;
                s = s.Replace("'", "''");
                return "N'" + s + "'";
            }

            if (type == typeof(DateTime))
            {
                var dt = (DateTime)value;
                return "'" + dt.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture) + "'";
            }

            if (type == typeof(bool))
            {
                return (bool)value ? "1" : "0";
            }

            if (type == typeof(Guid))
            {
                return "'" + ((Guid)value).ToString("D") + "'";
            }

            if (type == typeof(byte[]))
            {
                var bytes = (byte[])value;
                if (bytes.Length == 0) return "0x";
                return "0x" + BitConverter.ToString(bytes).Replace("-", "");
            }

            if (type.IsEnum)
            {
                return Convert.ToInt64(value).ToString(CultureInfo.InvariantCulture);
            }

            if (type == typeof(decimal) ||
                type == typeof(double) ||
                type == typeof(float))
            {
                return Convert.ToString(value, CultureInfo.InvariantCulture) ?? "NULL";
            }

            if (type == typeof(byte) || type == typeof(short) || type == typeof(int) ||
                type == typeof(long) || type == typeof(sbyte) || type == typeof(ushort) ||
                type == typeof(uint) || type == typeof(ulong))
            {
                return Convert.ToString(value, CultureInfo.InvariantCulture) ?? "NULL";
            }

            // Fallback – traktujemy jak tekst
            var fallback = value.ToString() ?? string.Empty;
            fallback = fallback.Replace("'", "''");
            return "N'" + fallback + "'";
        }

        // Ładniejszy grid dla zakładek Snippety / Aplikacje / Foldery / Notatki
        private void StyleSmartGrid(DataGridView g)
        {
            g.BackgroundColor = Color.White;
            g.BorderStyle = BorderStyle.None;
            g.EnableHeadersVisualStyles = false;
            g.ColumnHeadersDefaultCellStyle.BackColor = Color.WhiteSmoke;
            g.ColumnHeadersDefaultCellStyle.ForeColor = Color.Black;
            g.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            g.RowHeadersVisible = false;
            g.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            g.MultiSelect = false;
            g.AllowUserToAddRows = false;
            g.AllowUserToDeleteRows = false;
            g.AutoGenerateColumns = false;
            g.DefaultCellStyle.SelectionBackColor = C_Accent;
            g.DefaultCellStyle.SelectionForeColor = Color.White;
            g.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(248, 249, 252);
        }

        // DRAG & DROP z drzewa do edytora + „rozwijane” pole Zaznaczona tabela
        private void SetupTreeToEditorDragDrop()
        {
            if (_treeTables == null || _sqlEditor?.InnerEditor == null)
                return;

            _treeTables.ItemDrag += (s, e) =>
            {
                if (e.Item is TreeNode node && !string.IsNullOrWhiteSpace(node.Text))
                {
                    _treeTables.DoDragDrop(node.Text, DragDropEffects.Copy);
                }
            };

            if (_sqlEditor.InnerEditor is Control editorControl)
            {
                editorControl.AllowDrop = true;

                editorControl.DragEnter += (s, e) =>
                {
                    if (e.Data != null && e.Data.GetDataPresent(DataFormats.Text))
                        e.Effect = DragDropEffects.Copy;
                    else
                        e.Effect = DragDropEffects.None;
                };

                editorControl.DragDrop += (s, e) =>
                {
                    var txt = e.Data?.GetData(DataFormats.Text) as string;
                    if (string.IsNullOrEmpty(txt)) return;

                    if (editorControl is TextBoxBase tb)
                    {
                        int pos = tb.SelectionStart;
                        string current = tb.Text ?? string.Empty;
                        tb.Text = current.Insert(pos, txt);
                        tb.SelectionStart = pos + txt.Length;
                        tb.Focus();
                    }
                };
            }

            var menu = new ContextMenuStrip();
            var miInsertName = new ToolStripMenuItem("Wstaw nazwę tabeli do edytora");
            miInsertName.Click += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(_txtSelectedTable.Text)) return;
                InsertTextIntoEditor(_txtSelectedTable.Text);
            };
            var miInsertSelect = new ToolStripMenuItem("Wstaw SELECT * FROM tabela");
            miInsertSelect.Click += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(_txtSelectedTable.Text)) return;
                InsertTextIntoEditor($"SELECT TOP 100 * FROM {_txtSelectedTable.Text}");
            };
            menu.Items.Add(miInsertName);
            menu.Items.Add(miInsertSelect);

            _txtSelectedTable.ReadOnly = true;
            _txtSelectedTable.Cursor = Cursors.Hand;
            _txtSelectedTable.Click += (s, e) =>
            {
                menu.Show(_txtSelectedTable, 0, _txtSelectedTable.Height);
            };
        }

        private void InsertTextIntoEditor(string text)
        {
            if (_sqlEditor?.InnerEditor is TextBoxBase tb && !string.IsNullOrEmpty(text))
            {
                int pos = tb.SelectionStart;
                string current = tb.Text ?? string.Empty;
                tb.Text = current.Insert(pos, text);
                tb.SelectionStart = pos + text.Length;
                tb.Focus();
            }
        }

        // ZAKŁADKI SMART (SNIPPETY / APP / FOLDERY / NOTATKI)
        private void BuildSmartTab(string title, List<EntryConfig> entries, string type)
        {
            var page = new TabPage(title)
            {
                BackColor = C_Background
            };

            var split = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                SplitterDistance = 450
            };

            // LEWA STRONA – lista wpisów
            var grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                AutoGenerateColumns = false
            };
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "Nazwa",
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
            });
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "Opis",
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
            });
            StyleSmartGrid(grid);

            var ts = new ToolStrip
            {
                GripStyle = ToolStripGripStyle.Hidden,
                Dock = DockStyle.Top,
                BackColor = Color.WhiteSmoke
            };
            var btnAdd = new ToolStripButton("➕ Dodaj");
            var btnEdit = new ToolStripButton("✏ Edytuj");
            var btnDel = new ToolStripButton("❌ Usuń");

            foreach (var b in new[] { btnAdd, btnEdit, btnDel })
            {
                b.DisplayStyle = ToolStripItemDisplayStyle.Text;
                b.Font = new Font("Segoe UI", 9F, FontStyle.Regular);
            }

            ts.Items.Add(btnAdd);
            ts.Items.Add(btnEdit);
            ts.Items.Add(new ToolStripSeparator());
            ts.Items.Add(btnDel);

            var headerPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 42,
                BackColor = Color.White
            };
            var lblHeader = new Label
            {
                Text = title,
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI Semibold", 10.5F),
                Padding = new Padding(10, 10, 0, 0)
            };
            headerPanel.Controls.Add(lblHeader);

            var leftPanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };
            leftPanel.Controls.Add(grid);
            leftPanel.Controls.Add(ts);
            leftPanel.Controls.Add(headerPanel);

            // PRAWA STRONA – podgląd
            var txtPreview = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                Font = _fontCode,
                BackColor = Color.White
            };

            var previewCard = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = C_Background,
                Padding = new Padding(10)
            };

            var previewInner = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.White,
                Padding = new Padding(8),
                BorderStyle = BorderStyle.FixedSingle
            };

            var previewHeader = new Label
            {
                Text = "Podgląd",
                Dock = DockStyle.Top,
                Font = new Font("Segoe UI Semibold", 10F),
                Height = 24
            };

            previewInner.Controls.Add(txtPreview);
            previewInner.Controls.Add(previewHeader);
            previewCard.Controls.Add(previewInner);

            split.Panel1.Controls.Add(leftPanel);
            split.Panel2.Controls.Add(previewCard);

            page.Controls.Add(split);
            _tabControl.TabPages.Add(page);

            void RefreshGrid()
            {
                grid.Rows.Clear();
                foreach (var e in entries)
                {
                    int idx = grid.Rows.Add(e.GetDisplayName(), e.Description);
                    grid.Rows[idx].Tag = e;
                }
            }

            RefreshGrid();
            page.Tag = (Action)RefreshGrid;

            grid.SelectionChanged += (s, e) =>
            {
                if (grid.SelectedRows.Count > 0 && grid.SelectedRows[0].Tag is EntryConfig en)
                {
                    txtPreview.Text = en.GetContentText();
                    SqlSyntaxHighlighter.Highlight(txtPreview);
                }
            };

            grid.CellDoubleClick += (s, e) =>
            {
                if (e.RowIndex >= 0 && grid.Rows[e.RowIndex].Tag is EntryConfig en)
                {
                    ExecuteAndClose(en);
                }
            };

            btnAdd.Click += (s, e) =>
            {
                var cfg = new EntryConfig();
                using var f = new EntryEditorForm(cfg, type);
                if (f.ShowDialog() == DialogResult.OK)
                {
                    entries.Add(f.Entry);
                    SaveAndRefresh(page);
                }
            };

            btnEdit.Click += (s, e) =>
            {
                if (grid.SelectedRows.Count == 0)
                    return;

                if (grid.SelectedRows[0].Tag is EntryConfig en)
                {
                    using var f = new EntryEditorForm(en, type);
                    if (f.ShowDialog() == DialogResult.OK)
                    {
                        SaveAndRefresh(page);
                    }
                }
            };

            btnDel.Click += (s, e) =>
            {
                if (grid.SelectedRows.Count == 0)
                    return;

                if (grid.SelectedRows[0].Tag is EntryConfig en)
                {
                    if (MessageBox.Show("Usunąć wpis?", "Potwierdzenie",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                    {
                        entries.Remove(en);
                        SaveAndRefresh(page);
                    }
                }
            };
        }

        private void SaveAndRefresh(TabPage page)
        {
            _configService.SaveConfig(_config);
            if (page.Tag is Action refresh)
                refresh();
            FilterGrid(_txtSearch.Text);
        }

        private void ExecuteAndClose(EntryConfig entry)
        {
            try
            {
                _commandService.ExecuteEntry(entry);
                Hide();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Błąd", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // FILTROWANIE (pole „Szukaj” na dole)
        private void FilterGrid(string text)
        {
            if (_tabControl.SelectedTab == null)
                return;

            var page = _tabControl.SelectedTab;

            // SQL Lab nie filtrujemy
            if (page.Text.StartsWith("1. SQL"))
                return;

            var split = page.Controls.OfType<SplitContainer>().FirstOrDefault();
            if (split == null)
                return;

            var grid = split.Panel1.Controls.OfType<Panel>()
                .SelectMany(p => p.Controls.OfType<DataGridView>())
                .FirstOrDefault();

            if (grid == null)
                return;

            grid.Rows.Clear();

            List<EntryConfig>? src = null;

            if (page.Text.Contains("Snippety"))
                src = _config.SqlSnippets;
            else if (page.Text.Contains("Aplikacje"))
                src = _config.Apps;
            else if (page.Text.Contains("Foldery"))
                src = _config.Folders;
            else if (page.Text.Contains("Notatki"))
                src = _config.Notes;
            else if (_config.Tabs != null)
            {
                foreach (var t in _config.Tabs)
                {
                    if (page.Text.EndsWith(t.Name ?? string.Empty))
                    {
                        src = t.Entries;
                        break;
                    }
                }
            }

            if (src == null)
                return;

            var q = (text ?? string.Empty).Trim().ToLowerInvariant();

            foreach (var e in src)
            {
                var combo = ((e.GetDisplayName() ?? string.Empty) + " " + (e.Description ?? string.Empty)).ToLowerInvariant();
                if (string.IsNullOrEmpty(q) || combo.Contains(q))
                {
                    int i = grid.Rows.Add(e.GetDisplayName(), e.Description);
                    grid.Rows[i].Tag = e;
                }
            }
        }

        // ENTER w polu szukania = uruchom pierwszy wynik
        // F5 = uruchom SQL + generuj skrypt (Własny SQL)
        // CTRL+1..CTRL+9 = przełącz zakładki
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            // Ctrl+1..Ctrl+9 – przełącz zakładki
            if ((keyData & Keys.Control) == Keys.Control)
            {
                int index = -1;
                switch (keyData)
                {
                    case Keys.Control | Keys.D1:
                    case Keys.Control | Keys.NumPad1:
                        index = 0; break;
                    case Keys.Control | Keys.D2:
                    case Keys.Control | Keys.NumPad2:
                        index = 1; break;
                    case Keys.Control | Keys.D3:
                    case Keys.Control | Keys.NumPad3:
                        index = 2; break;
                    case Keys.Control | Keys.D4:
                    case Keys.Control | Keys.NumPad4:
                        index = 3; break;
                    case Keys.Control | Keys.D5:
                    case Keys.Control | Keys.NumPad5:
                        index = 4; break;
                    case Keys.Control | Keys.D6:
                    case Keys.Control | Keys.NumPad6:
                        index = 5; break;
                    case Keys.Control | Keys.D7:
                    case Keys.Control | Keys.NumPad7:
                        index = 6; break;
                    case Keys.Control | Keys.D8:
                    case Keys.Control | Keys.NumPad8:
                        index = 7; break;
                    case Keys.Control | Keys.D9:
                    case Keys.Control | Keys.NumPad9:
                        index = 8; break;
                }

                if (index >= 0 && index < _tabControl.TabCount)
                {
                    _tabControl.SelectedIndex = index;
                    return true;
                }
            }

            if (keyData == Keys.Enter && _txtSearch != null && _txtSearch.Focused)
            {
                if (_tabControl.SelectedTab != null)
                {
                    var page = _tabControl.SelectedTab;
                    var split = page.Controls.OfType<SplitContainer>().FirstOrDefault();
                    if (split != null)
                    {
                        var grid = split.Panel1.Controls.OfType<Panel>()
                            .SelectMany(p => p.Controls.OfType<DataGridView>())
                            .FirstOrDefault();

                        if (grid != null && grid.Rows.Count > 0)
                        {
                            if (grid.SelectedRows.Count == 0)
                                grid.Rows[0].Selected = true;

                            if (grid.SelectedRows[0].Tag is EntryConfig en)
                            {
                                ExecuteAndClose(en);
                                return true;
                            }
                        }
                    }
                }
            }

            // F5 = uruchom SQL z "1. SQL..." → Własny SQL
            if (keyData == Keys.F5)
            {
                if (_tabControl.SelectedTab != null && _tabControl.SelectedTab.Text.StartsWith("1. SQL"))
                {
                    _btnRunQuery?.PerformClick();
                    return true;
                }
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void SetStatus(string text)
        {
            if (_statusLabel == null)
                return;

            if (InvokeRequired)
            {
                BeginInvoke(new Action<string>(SetStatus), text);
                return;
            }

            _statusLabel.Text = text;
        }
    }
}

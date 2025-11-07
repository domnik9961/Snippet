using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using System.Drawing;
using Microsoft.Data.SqlClient;

namespace SnippetMgr
{
    public partial class Form1 : Form
    {
        // ====== Globalny skrót Win+Y ======
        private const int WM_HOTKEY = 0x0312;
        private const int HOTKEY_ID_WIN_Y = 1;
        private const uint MOD_WIN = 0x0008;

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        private IntPtr _lastActiveWindow = IntPtr.Zero;

        // ====== Snippet manager ======
        private const int MaxTabs = 9;
        private readonly Dictionary<TabPage, TabBinding> _tabBindings = new Dictionary<TabPage, TabBinding>();

        private readonly string _templatesPath;

        // ====== konfiguracja (z sekcji "config") ======
        private string _currentConfigName;
        private bool _autoPasteOnSnippetSelect = true;
        private bool _rememberLastTab = false;
        private bool _clearSearchOnTabChange = true;
        private bool _focusSearchOnTabChange = true;

        // ====== SQL Script zakładka ======
        private TabPage _sqlPage;
        private string _sqlBaseConnectionString;

        private System.Windows.Forms.TextBox _sqlServer;
        private System.Windows.Forms.RadioButton _sqlRbWindows;
        private System.Windows.Forms.RadioButton _sqlRbSqlAuth;
        private System.Windows.Forms.TextBox _sqlUser;
        private System.Windows.Forms.TextBox _sqlPassword;
        private System.Windows.Forms.ComboBox _sqlDatabases;
        private System.Windows.Forms.ComboBox _sqlTables;
        private System.Windows.Forms.TextBox _sqlWhere;
        private System.Windows.Forms.CheckBox _sqlIncludeData;
        private System.Windows.Forms.Button _sqlBtnConnect;
        private System.Windows.Forms.Button _sqlBtnGenerate;
        private System.Windows.Forms.Button _sqlBtnCopy;
        private System.Windows.Forms.Button _sqlBtnSave;

        // skrypt
        private System.Windows.Forms.RichTextBox _sqlScript;

        // tempdb object_id
        private System.Windows.Forms.TextBox _sqlTempObjectId;
        private System.Windows.Forms.Button _sqlBtnScriptTemp;

        // NOWE: pełny SELECT -> INSERT
        private System.Windows.Forms.TextBox _sqlFullSelect;
        private System.Windows.Forms.Button _sqlBtnFromSelect;

        // flag do kolorowania
        private bool _isHighlightingSql = false;

        public Form1()
        {
            InitializeComponent();

            _templatesPath = Path.Combine(AppContext.BaseDirectory, "Data", "templates.json");

            this.Load += Form1_Load;

            // Skróty i ENTER na poziomie formularza
            this.KeyPreview = true;
            this.KeyDown += Form1_KeyDown;

            textBox1.TextChanged += (s, e) => ApplyFilterToActiveTab();
            tabControl1.SelectedIndexChanged += TabControl1_SelectedIndexChanged;

            zarzadzajToolStripMenuItem.Click += ZarzadzajToolStripMenuItem_Click;

            SetStatus("Start", "Gotowy");
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            try
            {
                UnregisterHotKey(this.Handle, HOTKEY_ID_WIN_Y);
            }
            catch { }
            base.OnFormClosed(e);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY && m.WParam.ToInt32() == HOTKEY_ID_WIN_Y)
            {
                if (this.WindowState == FormWindowState.Minimized || !this.Visible)
                {
                    _lastActiveWindow = GetForegroundWindow();

                    this.Show();
                    this.WindowState = FormWindowState.Normal;
                    this.Activate();
                    ActivateMainTabAndSearch();
                }
                else
                {
                    this.WindowState = FormWindowState.Minimized;
                }
            }

            base.WndProc(ref m);
        }

        private void ActivateMainTabAndSearch()
        {
            try
            {
                if (tabControl1.TabPages.Count > 1)
                {
                    tabControl1.SelectedIndex = 1;
                }

                if (textBox1 != null)
                {
                    textBox1.Text = string.Empty;
                    textBox1.Focus();
                }

                ApplyFilterToActiveTab();
            }
            catch
            {
            }
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            try
            {
                bool hotkeyOk = RegisterHotKey(this.Handle, HOTKEY_ID_WIN_Y, MOD_WIN, (uint)Keys.Y);
                if (!hotkeyOk)
                {
                    SetStatus("Hotkey", "Win+Y zajęty (niezarejestrowany)");
                }
                else
                {
                    SetStatus("Hotkey", "Win+Y aktywny");
                }

                EnsureDataDir();

                if (File.Exists(_templatesPath))
                {
                    string json = File.ReadAllText(_templatesPath, Encoding.UTF8);
                    CreateTabsFromJson(json);
                }
                else
                {
                    EnsureSqlTabCreated();
                }

                ApplyFilterToActiveTab();
                SetStatus("OK", "JSON + SQL gotowe");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Błąd startu aplikacji:\n" + ex.Message, "Błąd",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                SetStatus("Błąd", "Start aplikacji niepełny");
            }
        }

        private void EnsureDataDir()
        {
            string dir = Path.Combine(AppContext.BaseDirectory, "Data");
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);
        }

        // ====== Skróty klawiaturowe + ENTER ======
        private void Form1_KeyDown(object sender, KeyEventArgs e)
        {
            // ENTER – uruchom zaznaczony wpis
            if (!e.Control && !e.Alt && !e.Shift && e.KeyCode == Keys.Enter)
            {
                TabPage page = tabControl1.SelectedTab;
                if (page != null && _tabBindings.TryGetValue(page, out TabBinding binding) && binding.Grid != null)
                {
                    var grid = binding.Grid;
                    int rowIndex = -1;
                    int colIndex = 0;

                    if (grid.CurrentCell != null)
                    {
                        rowIndex = grid.CurrentCell.RowIndex;
                        colIndex = grid.CurrentCell.ColumnIndex;
                    }
                    else if (grid.SelectedRows.Count > 0)
                    {
                        rowIndex = grid.SelectedRows[0].Index;
                    }

                    if (rowIndex >= 0 && rowIndex < grid.Rows.Count)
                    {
                        e.Handled = true;
                        e.SuppressKeyPress = true;
                        var args = new DataGridViewCellEventArgs(colIndex, rowIndex);
                        Grid_CellDoubleClick(grid, args);
                        return;
                    }
                }
            }

            // Ctrl+0..9 – przełączanie zakładek
            if (e.Control && !e.Alt && !e.Shift)
            {
                int? num = null;
                switch (e.KeyCode)
                {
                    case Keys.D0:
                    case Keys.NumPad0: num = 0; break;
                    case Keys.D1:
                    case Keys.NumPad1: num = 1; break;
                    case Keys.D2:
                    case Keys.NumPad2: num = 2; break;
                    case Keys.D3:
                    case Keys.NumPad3: num = 3; break;
                    case Keys.D4:
                    case Keys.NumPad4: num = 4; break;
                    case Keys.D5:
                    case Keys.NumPad5: num = 5; break;
                    case Keys.D6:
                    case Keys.NumPad6: num = 6; break;
                    case Keys.D7:
                    case Keys.NumPad7: num = 7; break;
                    case Keys.D8:
                    case Keys.NumPad8: num = 8; break;
                    case Keys.D9:
                    case Keys.NumPad9: num = 9; break;
                }

                if (num.HasValue)
                {
                    e.Handled = true;
                    e.SuppressKeyPress = true;

                    string prefix = num.Value.ToString() + ".";
                    foreach (TabPage page in tabControl1.TabPages)
                    {
                        if (page.Text.StartsWith(prefix, StringComparison.Ordinal))
                        {
                            tabControl1.SelectedTab = page;
                            ApplyFilterToActiveTab();
                            return;
                        }
                    }
                }
            }
        }

        private void TabControl1_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_clearSearchOnTabChange && textBox1 != null)
            {
                textBox1.Text = string.Empty;
            }

            if (_focusSearchOnTabChange && textBox1 != null)
            {
                textBox1.Focus();
            }

            ApplyFilterToActiveTab();
        }

        // ====== Config z templates.json (sekcja "config") ======
        private void LoadConfigFromRoot(JsonElement root)
        {
            try
            {
                if (root.TryGetProperty("configName", out JsonElement nameEl) &&
                    nameEl.ValueKind == JsonValueKind.String)
                {
                    _currentConfigName = nameEl.GetString();
                }

                if (!root.TryGetProperty("config", out JsonElement cfgEl) ||
                    cfgEl.ValueKind != JsonValueKind.Object)
                {
                    if (!string.IsNullOrEmpty(_currentConfigName))
                        SetStatus("Config", "Profil: " + _currentConfigName + " (brak sekcji config)");
                    return;
                }

                // --- config.sql ---
                if (cfgEl.TryGetProperty("sql", out JsonElement sqlEl) &&
                    sqlEl.ValueKind == JsonValueKind.Object)
                {
                    string server = GetString(sqlEl, "server");
                    bool? useWin = GetBoolNullable(sqlEl, "useWindowsAuth");
                    string user = GetString(sqlEl, "user");
                    string password = GetString(sqlEl, "password");
                    string defaultDb = GetString(sqlEl, "defaultDatabase");

                    if (_sqlServer != null && !string.IsNullOrWhiteSpace(server))
                        _sqlServer.Text = server;

                    if (useWin.HasValue)
                    {
                        if (useWin.Value)
                        {
                            if (_sqlRbWindows != null) _sqlRbWindows.Checked = true;
                        }
                        else
                        {
                            if (_sqlRbSqlAuth != null) _sqlRbSqlAuth.Checked = true;
                            if (_sqlUser != null) _sqlUser.Text = user ?? string.Empty;
                            if (_sqlPassword != null) _sqlPassword.Text = password ?? string.Empty;
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(server))
                    {
                        SqlConnect_Click(this, EventArgs.Empty);

                        if (!string.IsNullOrWhiteSpace(defaultDb) &&
                            _sqlDatabases != null &&
                            _sqlDatabases.Items.Count > 0)
                        {
                            for (int i = 0; i < _sqlDatabases.Items.Count; i++)
                            {
                                string itemDb = _sqlDatabases.Items[i].ToString();
                                if (string.Equals(itemDb, defaultDb, StringComparison.OrdinalIgnoreCase))
                                {
                                    _sqlDatabases.SelectedIndex = i;
                                    SetStatus("SQL", "Domyślna baza: " + defaultDb);
                                    break;
                                }
                            }
                        }
                    }
                }

                // --- config.ui ---
                if (cfgEl.TryGetProperty("ui", out JsonElement uiEl) &&
                    uiEl.ValueKind == JsonValueKind.Object)
                {
                    bool? alwaysOnTop = GetBoolNullable(uiEl, "alwaysOnTop");
                    bool? startMinimized = GetBoolNullable(uiEl, "startMinimized");
                    string hotkeyText = GetString(uiEl, "hotkey");

                    if (alwaysOnTop.HasValue)
                        this.TopMost = alwaysOnTop.Value;

                    if (startMinimized.HasValue && startMinimized.Value)
                        this.WindowState = FormWindowState.Minimized;
                }

                // --- config.behavior ---
                if (cfgEl.TryGetProperty("behavior", out JsonElement behEl) &&
                    behEl.ValueKind == JsonValueKind.Object)
                {
                    bool? autoPaste = GetBoolNullable(behEl, "autoPasteOnSnippetSelect");
                    bool? rememberTab = GetBoolNullable(behEl, "rememberLastTab");
                    bool? clearSearch = GetBoolNullable(behEl, "clearSearchOnTabChange");
                    bool? focusSearch = GetBoolNullable(behEl, "focusSearchOnTabChange");

                    if (autoPaste.HasValue)
                        _autoPasteOnSnippetSelect = autoPaste.Value;
                    if (rememberTab.HasValue)
                        _rememberLastTab = rememberTab.Value;
                    if (clearSearch.HasValue)
                        _clearSearchOnTabChange = clearSearch.Value;
                    if (focusSearch.HasValue)
                        _focusSearchOnTabChange = focusSearch.Value;
                }

                if (!string.IsNullOrEmpty(_currentConfigName))
                {
                    SetStatus("Config", "Profil: " + _currentConfigName);
                }
            }
            catch (Exception ex)
            {
                SetStatus("Config", "Błąd config: " + ex.Message);
            }
        }

        private static string GetString(JsonElement obj, string propertyName)
        {
            if (obj.TryGetProperty(propertyName, out JsonElement el) &&
                el.ValueKind == JsonValueKind.String)
            {
                return el.GetString();
            }
            return null;
        }

        private static bool? GetBoolNullable(JsonElement obj, string propertyName)
        {
            if (obj.TryGetProperty(propertyName, out JsonElement el) &&
                (el.ValueKind == JsonValueKind.True || el.ValueKind == JsonValueKind.False))
            {
                return el.GetBoolean();
            }
            return null;
        }

        // ====== Tworzenie zakładek z JSON (tabs) + config ======
        private void CreateTabsFromJson(string json)
        {
            EnsureSqlTabCreated();

            using (JsonDocument doc = JsonDocument.Parse(json))
            {
                JsonElement root = doc.RootElement;

                LoadConfigFromRoot(root);

                if (!root.TryGetProperty("tabs", out JsonElement tabsElement) ||
                    tabsElement.ValueKind != JsonValueKind.Array)
                {
                    throw new JsonException("Plik JSON nie zawiera prawidłowego pola 'tabs'.");
                }

                tabControl1.TabPages.Clear();
                _tabBindings.Clear();

                int totalTabs = tabsElement.GetArrayLength();
                int added = 0;

                if (totalTabs > MaxTabs)
                {
                    MessageBox.Show(
                        "Plik JSON zawiera " + totalTabs + " zakładek.\n" +
                        "Maksymalnie można utworzyć " + MaxTabs + " zakładek.\n" +
                        "Zostaną wczytane tylko pierwsze " + MaxTabs + ".",
                        "Limit zakładek",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }

                foreach (JsonElement tabEl in tabsElement.EnumerateArray())
                {
                    if (added >= MaxTabs)
                        break;

                    if (!tabEl.TryGetProperty("tab_name", out JsonElement nameEl) ||
                        nameEl.ValueKind != JsonValueKind.String)
                    {
                        continue;
                    }

                    string tabName = nameEl.GetString() ?? "Bez nazwy";

                    TabPage page = new TabPage((added + 1).ToString() + ". " + tabName);

                    DataGridView grid = new DataGridView();
                    grid.Dock = DockStyle.Fill;
                    grid.ReadOnly = true;
                    grid.AllowUserToAddRows = false;
                    grid.AllowUserToDeleteRows = false;
                    grid.AllowUserToOrderColumns = true;
                    grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
                    grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
                    grid.MultiSelect = false;

                    DataTable table = new DataTable();
                    table.CaseSensitive = false;

                    table.Columns.Add("Shortcut", typeof(string));
                    table.Columns.Add("Description", typeof(string));
                    table.Columns.Add("Text", typeof(string));

                    table.Columns.Add("Type", typeof(string));
                    table.Columns.Add("AppName", typeof(string));
                    table.Columns.Add("AppPath", typeof(string));
                    table.Columns.Add("AppArgs", typeof(string));
                    table.Columns.Add("WorkingDir", typeof(string));
                    table.Columns.Add("RunAsAdmin", typeof(bool));
                    table.Columns.Add("FolderPath", typeof(string));

                    if (tabEl.TryGetProperty("entries", out JsonElement entriesEl) &&
                        entriesEl.ValueKind == JsonValueKind.Array)
                    {
                        foreach (JsonElement entryEl in entriesEl.EnumerateArray())
                        {
                            string shortcut = "";
                            string description = "";
                            string text = "";

                            string type = "text";
                            string appName = "";
                            string appPath = "";
                            string appArgs = "";
                            string workingDir = "";
                            bool runAsAdmin = false;
                            string folderPath = "";

                            if (entryEl.TryGetProperty("shortcut", out JsonElement sEl) &&
                                sEl.ValueKind == JsonValueKind.String)
                            {
                                shortcut = sEl.GetString() ?? "";
                            }

                            if (entryEl.TryGetProperty("description", out JsonElement dEl) &&
                                dEl.ValueKind == JsonValueKind.String)
                            {
                                description = dEl.GetString() ?? "";
                            }

                            if (entryEl.TryGetProperty("paste_text", out JsonElement pEl) &&
                                pEl.ValueKind == JsonValueKind.String)
                            {
                                text = pEl.GetString() ?? "";
                            }

                            if (entryEl.TryGetProperty("type", out JsonElement tEl) &&
                                tEl.ValueKind == JsonValueKind.String)
                            {
                                type = (tEl.GetString() ?? "text").ToLowerInvariant();
                            }

                            if (type == "app")
                            {
                                if (entryEl.TryGetProperty("app_name", out JsonElement anEl) &&
                                    anEl.ValueKind == JsonValueKind.String)
                                {
                                    appName = anEl.GetString() ?? "";
                                }

                                if (entryEl.TryGetProperty("app_path", out JsonElement apEl) &&
                                    apEl.ValueKind == JsonValueKind.String)
                                {
                                    appPath = apEl.GetString() ?? "";
                                }

                                if (entryEl.TryGetProperty("app_args", out JsonElement aaEl) &&
                                    aaEl.ValueKind == JsonValueKind.String)
                                {
                                    appArgs = aaEl.GetString() ?? "";
                                }

                                if (entryEl.TryGetProperty("working_dir", out JsonElement wdEl) &&
                                    wdEl.ValueKind == JsonValueKind.String)
                                {
                                    workingDir = wdEl.GetString() ?? "";
                                }

                                if (entryEl.TryGetProperty("run_as_admin", out JsonElement raEl) &&
                                    (raEl.ValueKind == JsonValueKind.True || raEl.ValueKind == JsonValueKind.False))
                                {
                                    runAsAdmin = raEl.GetBoolean();
                                }

                                if (string.IsNullOrWhiteSpace(text))
                                {
                                    text = appPath;
                                }
                            }
                            else if (type == "folder")
                            {
                                if (entryEl.TryGetProperty("folder_path", out JsonElement fpEl) &&
                                    fpEl.ValueKind == JsonValueKind.String)
                                {
                                    folderPath = fpEl.GetString() ?? "";
                                }

                                if (string.IsNullOrWhiteSpace(text))
                                {
                                    text = folderPath;
                                }
                            }

                            table.Rows.Add(shortcut, description, text, type, appName, appPath, appArgs, workingDir, runAsAdmin, folderPath);
                        }
                    }

                    grid.DataSource = table.DefaultView;
                    grid.CellDoubleClick += Grid_CellDoubleClick;

                    page.Controls.Add(grid);
                    tabControl1.TabPages.Add(page);
                    _tabBindings[page] = new TabBinding { Table = table, Grid = grid };

                    added++;
                }

                if (_sqlPage != null && !tabControl1.TabPages.Contains(_sqlPage))
                {
                    tabControl1.TabPages.Insert(0, _sqlPage);
                }
            }
        }

        // ====== Double-click / ENTER: text vs app vs folder ======
        private void Grid_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            var grid = sender as DataGridView;
            if (grid == null || e.RowIndex < 0 || e.RowIndex >= grid.Rows.Count)
                return;

            var rowView = grid.Rows[e.RowIndex].DataBoundItem as DataRowView;
            if (rowView == null)
                return;

            string type;
            try
            {
                type = (rowView["Type"] as string ?? "text").Trim().ToLowerInvariant();
            }
            catch
            {
                type = "text";
            }

            // folder
            if (type == "folder")
            {
                string folderPath = "";
                try { folderPath = rowView["FolderPath"] as string ?? ""; } catch { folderPath = ""; }

                if (string.IsNullOrWhiteSpace(folderPath))
                {
                    MessageBox.Show("Brak ścieżki folderu (FolderPath) dla tego wpisu.", "Błąd",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                if (!Directory.Exists(folderPath))
                {
                    var res = MessageBox.Show(
                        "Folder nie istnieje:\n" + folderPath + "\n\nCzy mimo to spróbować go otworzyć?",
                        "Folder",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Warning);

                    if (res != DialogResult.Yes)
                        return;
                }

                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = folderPath,
                        UseShellExecute = true
                    };

                    Process.Start(psi);
                    SetStatus("Folder", "Otworzono: " + folderPath);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Nie udało się otworzyć folderu:\n" + ex.Message, "Błąd",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    SetStatus("Folder", "Błąd otwierania folderu");
                }

                return;
            }

            // app
            if (type == "app")
            {
                string appPath = "";
                string appArgs = "";
                string workingDir = "";
                bool runAsAdmin = false;

                try { appPath = rowView["AppPath"] as string ?? ""; } catch { }
                try { appArgs = rowView["AppArgs"] as string ?? ""; } catch { }
                try { workingDir = rowView["WorkingDir"] as string ?? ""; } catch { }

                try
                {
                    if (!Convert.IsDBNull(rowView["RunAsAdmin"]))
                        runAsAdmin = Convert.ToBoolean(rowView["RunAsAdmin"]);
                }
                catch
                {
                    runAsAdmin = false;
                }

                if (string.IsNullOrWhiteSpace(appPath))
                {
                    MessageBox.Show("Brak ścieżki aplikacji (AppPath) dla tego wpisu.", "Błąd",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = appPath,
                        Arguments = appArgs,
                        UseShellExecute = true
                    };

                    if (!string.IsNullOrWhiteSpace(workingDir))
                        psi.WorkingDirectory = workingDir;

                    if (runAsAdmin)
                        psi.Verb = "runas";

                    Process.Start(psi);
                    SetStatus("App", "Uruchomiono: " + appPath);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Nie udało się uruchomić aplikacji:\n" + ex.Message, "Błąd",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    SetStatus("App", "Błąd uruchamiania aplikacji");
                }

                return;
            }

            // text
            string text = "";
            try
            {
                text = rowView["Text"] as string ?? "";
            }
            catch
            {
                text = "";
            }

            if (string.IsNullOrWhiteSpace(text))
                return;

            bool copied = false;
            try
            {
                Clipboard.SetText(text);
                copied = true;
            }
            catch
            {
                MessageBox.Show("Nie udało się skopiować do schowka.", "Błąd schowka",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            bool pasted = false;

            try
            {
                if (_autoPasteOnSnippetSelect &&
                    _lastActiveWindow != IntPtr.Zero && _lastActiveWindow != this.Handle)
                {
                    SetForegroundWindow(_lastActiveWindow);
                    Thread.Sleep(50);
                    SendKeys.SendWait("^v");
                    pasted = true;
                }
            }
            catch
            {
            }

            if (pasted)
            {
                SetStatus("Wklej", "Tekst wklejony do poprzedniego okna");
            }
            else if (copied)
            {
                SetStatus("Kopiuj", "Tekst skopiowany do schowka");
            }
        }

        private void ApplyFilterToActiveTab()
        {
            TabPage page = tabControl1.SelectedTab;
            if (page == null)
                return;

            if (!_tabBindings.TryGetValue(page, out TabBinding binding))
                return;

            string filterText = textBox1.Text == null ? "" : textBox1.Text.Trim();
            DataView view = binding.Table.DefaultView;

            if (string.IsNullOrWhiteSpace(filterText))
            {
                view.RowFilter = "";
                return;
            }

            string esc = filterText.Replace("'", "''");
            view.RowFilter =
                "Shortcut LIKE '%" + esc + "%' OR " +
                "Description LIKE '%" + esc + "%' OR " +
                "Text LIKE '%" + esc + "%' OR " +
                "FolderPath LIKE '%" + esc + "%'";
        }

        private void SetStatus(string left, string right)
        {
            toolStripStatusLabel1.Text = left;
            toolStripStatusLabel2.Text = right;
        }

        private void ZarzadzajToolStripMenuItem_Click(object sender, EventArgs e)
        {
            try
            {
                EnsureDataDir();

                using (var dlg = new ManageJsonEditorForm(_templatesPath))
                {
                    dlg.ShowDialog(this);

                    if (dlg.JsonChanged && File.Exists(_templatesPath))
                    {
                        string json = File.ReadAllText(_templatesPath, Encoding.UTF8);
                        CreateTabsFromJson(json);
                        ApplyFilterToActiveTab();
                        SetStatus("JSON", "Zaktualizowano z pliku");
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Nie udało się odświeżyć zakładek z JSON:\n" + ex.Message, "Błąd",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ============================
        //  ZAKŁADKA "0. SQL Script"
        // ============================
        private void EnsureSqlTabCreated()
        {
            if (_sqlPage == null)
            {
                _sqlPage = new TabPage("0. SQL Script");
                tabControl1.TabPages.Insert(0, _sqlPage);

                var panel = new System.Windows.Forms.Panel();
                panel.Dock = DockStyle.Fill;
                _sqlPage.Controls.Add(panel);

                var gbConn = new System.Windows.Forms.GroupBox();
                gbConn.Text = "Połączenie z SQL Server";
                gbConn.Left = 8;
                gbConn.Top = 8;
                gbConn.Width = 520;
                gbConn.Height = 110;
                gbConn.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
                panel.Controls.Add(gbConn);

                var lblServer = new System.Windows.Forms.Label();
                lblServer.Text = "Serwer:";
                lblServer.Left = 10;
                lblServer.Top = 25;
                lblServer.AutoSize = true;

                _sqlServer = new System.Windows.Forms.TextBox();
                _sqlServer.Left = 70;
                _sqlServer.Top = 22;
                _sqlServer.Width = 340;
                _sqlServer.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

                _sqlRbWindows = new System.Windows.Forms.RadioButton();
                _sqlRbWindows.Text = "Windows";
                _sqlRbWindows.Left = 10;
                _sqlRbWindows.Top = 55;
                _sqlRbWindows.AutoSize = true;
                _sqlRbWindows.Checked = true;

                _sqlRbSqlAuth = new System.Windows.Forms.RadioButton();
                _sqlRbSqlAuth.Text = "SQL Server";
                _sqlRbSqlAuth.Left = 100;
                _sqlRbSqlAuth.Top = 55;
                _sqlRbSqlAuth.AutoSize = true;

                var lblUser = new System.Windows.Forms.Label();
                lblUser.Text = "Login:";
                lblUser.Left = 200;
                lblUser.Top = 55;
                lblUser.AutoSize = true;

                _sqlUser = new System.Windows.Forms.TextBox();
                _sqlUser.Left = 200;
                _sqlUser.Top = 72;
                _sqlUser.Width = 120;
                _sqlUser.Enabled = false;

                var lblPass = new System.Windows.Forms.Label();
                lblPass.Text = "Hasło:";
                lblPass.Left = 330;
                lblPass.Top = 55;
                lblPass.AutoSize = true;

                _sqlPassword = new System.Windows.Forms.TextBox();
                _sqlPassword.Left = 330;
                _sqlPassword.Top = 72;
                _sqlPassword.Width = 120;
                _sqlPassword.Enabled = false;
                _sqlPassword.UseSystemPasswordChar = true;

                _sqlRbWindows.CheckedChanged += (s, e) => UpdateSqlAuthControls();
                _sqlRbSqlAuth.CheckedChanged += (s, e) => UpdateSqlAuthControls();

                gbConn.Controls.Add(lblServer);
                gbConn.Controls.Add(_sqlServer);
                gbConn.Controls.Add(_sqlRbWindows);
                gbConn.Controls.Add(_sqlRbSqlAuth);
                gbConn.Controls.Add(lblUser);
                gbConn.Controls.Add(_sqlUser);
                gbConn.Controls.Add(lblPass);
                gbConn.Controls.Add(_sqlPassword);

                _sqlBtnConnect = new System.Windows.Forms.Button();
                _sqlBtnConnect.Text = "Połącz";
                _sqlBtnConnect.Left = gbConn.Right + 8;
                _sqlBtnConnect.Top = gbConn.Top + 20;
                _sqlBtnConnect.Width = 90;
                _sqlBtnConnect.Anchor = AnchorStyles.Top | AnchorStyles.Right;
                _sqlBtnConnect.Click += SqlConnect_Click;
                panel.Controls.Add(_sqlBtnConnect);

                var lblDb = new System.Windows.Forms.Label();
                lblDb.Text = "Baza:";
                lblDb.Left = 10;
                lblDb.Top = gbConn.Bottom + 12;
                lblDb.AutoSize = true;

                _sqlDatabases = new System.Windows.Forms.ComboBox();
                _sqlDatabases.Left = 70;
                _sqlDatabases.Top = gbConn.Bottom + 8;
                _sqlDatabases.Width = 220;
                _sqlDatabases.DropDownStyle = ComboBoxStyle.DropDownList;
                _sqlDatabases.SelectedIndexChanged += SqlDatabases_SelectedIndexChanged;

                var lblTable = new System.Windows.Forms.Label();
                lblTable.Text = "Tabela:";
                lblTable.Left = 300;
                lblTable.Top = gbConn.Bottom + 12;
                lblTable.AutoSize = true;

                _sqlTables = new System.Windows.Forms.ComboBox();
                _sqlTables.Left = 360;
                _sqlTables.Top = gbConn.Bottom + 8;
                _sqlTables.Width = 220;
                _sqlTables.DropDownStyle = ComboBoxStyle.DropDownList;

                panel.Controls.Add(lblDb);
                panel.Controls.Add(_sqlDatabases);
                panel.Controls.Add(lblTable);
                panel.Controls.Add(_sqlTables);

                var lblWhere = new System.Windows.Forms.Label();
                lblWhere.Text = "WHERE (opcjonalne):";
                lblWhere.Left = 10;
                lblWhere.Top = _sqlDatabases.Bottom + 12;
                lblWhere.AutoSize = true;

                _sqlWhere = new System.Windows.Forms.TextBox();
                _sqlWhere.Left = 150;
                _sqlWhere.Top = _sqlDatabases.Bottom + 8;
                _sqlWhere.Width = 430;
                _sqlWhere.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

                _sqlIncludeData = new System.Windows.Forms.CheckBox();
                _sqlIncludeData.Text = "Dołącz dane (INSERT)";
                _sqlIncludeData.Left = 10;
                _sqlIncludeData.Top = _sqlWhere.Bottom + 8;
                _sqlIncludeData.AutoSize = true;
                _sqlIncludeData.Checked = true;

                // Object ID (tempdb)
                var lblTempObj = new System.Windows.Forms.Label();
                lblTempObj.Text = "Object ID (tempdb):";
                lblTempObj.Left = 200;
                lblTempObj.Top = _sqlWhere.Bottom + 8;
                lblTempObj.AutoSize = true;

                _sqlTempObjectId = new System.Windows.Forms.TextBox();
                _sqlTempObjectId.Left = 320;
                _sqlTempObjectId.Top = _sqlWhere.Bottom + 6;
                _sqlTempObjectId.Width = 120;

                _sqlBtnScriptTemp = new System.Windows.Forms.Button();
                _sqlBtnScriptTemp.Text = "Script tempdb";
                _sqlBtnScriptTemp.Left = 450;
                _sqlBtnScriptTemp.Top = _sqlWhere.Bottom + 4;
                _sqlBtnScriptTemp.Width = 120;
                _sqlBtnScriptTemp.Click += SqlScriptTempObject_Click;

                panel.Controls.Add(lblWhere);
                panel.Controls.Add(_sqlWhere);
                panel.Controls.Add(_sqlIncludeData);
                panel.Controls.Add(lblTempObj);
                panel.Controls.Add(_sqlTempObjectId);
                panel.Controls.Add(_sqlBtnScriptTemp);

                // NOWE: pełny SELECT
                var lblFullSelect = new System.Windows.Forms.Label();
                lblFullSelect.Text = "Pełny SELECT (do INSERT):";
                lblFullSelect.Left = 10;
                lblFullSelect.Top = _sqlIncludeData.Bottom + 8;
                lblFullSelect.AutoSize = true;

                _sqlFullSelect = new System.Windows.Forms.TextBox();
                _sqlFullSelect.Left = 150;
                _sqlFullSelect.Top = _sqlIncludeData.Bottom + 4;
                _sqlFullSelect.Width = 430;
                _sqlFullSelect.Height = 60;
                _sqlFullSelect.Multiline = true;
                _sqlFullSelect.ScrollBars = ScrollBars.Vertical;
                _sqlFullSelect.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

                _sqlBtnFromSelect = new System.Windows.Forms.Button();
                _sqlBtnFromSelect.Text = "Generuj z SELECT";
                _sqlBtnFromSelect.Left = 10;
                _sqlBtnFromSelect.Top = _sqlFullSelect.Bottom + 4;
                _sqlBtnFromSelect.Width = 130;
                _sqlBtnFromSelect.Click += SqlGenerateFromSelect_Click;

                panel.Controls.Add(lblFullSelect);
                panel.Controls.Add(_sqlFullSelect);
                panel.Controls.Add(_sqlBtnFromSelect);

                int buttonsTop = _sqlBtnFromSelect.Bottom + 8;

                _sqlBtnGenerate = new System.Windows.Forms.Button();
                _sqlBtnGenerate.Text = "Generuj skrypt";
                _sqlBtnGenerate.Left = 200;
                _sqlBtnGenerate.Top = buttonsTop;
                _sqlBtnGenerate.Width = 120;
                _sqlBtnGenerate.Click += SqlGenerate_Click;

                _sqlBtnCopy = new System.Windows.Forms.Button();
                _sqlBtnCopy.Text = "Kopiuj";
                _sqlBtnCopy.Left = 330;
                _sqlBtnCopy.Top = buttonsTop;
                _sqlBtnCopy.Width = 80;
                _sqlBtnCopy.Click += (s, e) =>
                {
                    if (_sqlScript != null && !string.IsNullOrEmpty(_sqlScript.Text))
                    {
                        Clipboard.SetText(_sqlScript.Text);
                        SetStatus("SQL", "Skrypt skopiowany do schowka");
                    }
                };

                _sqlBtnSave = new System.Windows.Forms.Button();
                _sqlBtnSave.Text = "Zapisz do pliku";
                _sqlBtnSave.Left = 420;
                _sqlBtnSave.Top = buttonsTop;
                _sqlBtnSave.Width = 120;
                _sqlBtnSave.Click += SqlSaveToFile_Click;

                panel.Controls.Add(_sqlBtnGenerate);
                panel.Controls.Add(_sqlBtnCopy);
                panel.Controls.Add(_sqlBtnSave);

                _sqlScript = new System.Windows.Forms.RichTextBox();
                _sqlScript.Multiline = true;
                _sqlScript.ScrollBars = RichTextBoxScrollBars.Both;
                _sqlScript.AcceptsTab = true;
                _sqlScript.Left = 8;
                _sqlScript.Top = _sqlBtnSave.Bottom + 8;
                _sqlScript.Width = panel.ClientSize.Width - 16;
                _sqlScript.Height = panel.ClientSize.Height - (_sqlBtnSave.Bottom + 16);
                _sqlScript.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
                _sqlScript.Font = new System.Drawing.Font(System.Drawing.FontFamily.GenericMonospace, 9f);
                _sqlScript.TextChanged += SqlScript_TextChanged;
                panel.Controls.Add(_sqlScript);
            }
            else
            {
                if (!tabControl1.TabPages.Contains(_sqlPage))
                {
                    tabControl1.TabPages.Insert(0, _sqlPage);
                }
                else
                {
                    int idx = tabControl1.TabPages.IndexOf(_sqlPage);
                    if (idx != 0)
                    {
                        tabControl1.TabPages.RemoveAt(idx);
                        tabControl1.TabPages.Insert(0, _sqlPage);
                    }
                }
            }
        }

        private void SqlScript_TextChanged(object sender, EventArgs e)
        {
            ApplySqlSyntaxHighlighting();
        }

        private void UpdateSqlAuthControls()
        {
            if (_sqlRbSqlAuth == null || _sqlUser == null || _sqlPassword == null)
                return;

            bool sqlAuth = _sqlRbSqlAuth.Checked;
            _sqlUser.Enabled = sqlAuth;
            _sqlPassword.Enabled = sqlAuth;
        }

        private string BuildSqlBaseConnectionString()
        {
            if (_sqlServer == null)
                throw new InvalidOperationException("Kontrolki SQL nie zostały zainicjalizowane.");

            StringBuilder sb = new StringBuilder();
            sb.Append("Server=").Append(_sqlServer.Text.Trim()).Append(";");

            if (_sqlRbWindows != null && _sqlRbWindows.Checked)
            {
                sb.Append("Integrated Security=True;");
            }
            else
            {
                if (_sqlUser == null || _sqlPassword == null)
                    throw new InvalidOperationException("Brak kontrolek login/hasło.");

                sb.Append("User Id=").Append(_sqlUser.Text.Trim()).Append(";");
                sb.Append("Password=").Append(_sqlPassword.Text).Append(";");
            }

            sb.Append("Encrypt=False;");
            sb.Append("TrustServerCertificate=True;");

            return sb.ToString();
        }

        private void SqlConnect_Click(object sender, EventArgs e)
        {
            try
            {
                if (_sqlServer == null)
                    return;

                if (string.IsNullOrWhiteSpace(_sqlServer.Text))
                {
                    MessageBox.Show(this, "Podaj nazwę serwera SQL.", "Błąd",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                if (_sqlRbSqlAuth != null && _sqlRbSqlAuth.Checked)
                {
                    if (_sqlUser == null || _sqlPassword == null ||
                        string.IsNullOrWhiteSpace(_sqlUser.Text) ||
                        string.IsNullOrWhiteSpace(_sqlPassword.Text))
                    {
                        MessageBox.Show(this, "Podaj login i hasło dla uwierzytelniania SQL Server.", "Błąd",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                }

                _sqlBaseConnectionString = BuildSqlBaseConnectionString();
                string connStr = _sqlBaseConnectionString + "Database=master;";

                using (SqlConnection conn = new SqlConnection(connStr))
                {
                    conn.Open();

                    if (conn.State != ConnectionState.Open)
                    {
                        MessageBox.Show(this, "Nie udało się połączyć z serwerem SQL.", "Błąd",
                            MessageBoxButtons.OK, MessageBoxIcon.Error);
                        SetStatus("SQL", "Brak połączenia z serwerem");
                        return;
                    }

                    List<string> dbs = GetDatabasesList(conn);

                    if (_sqlDatabases != null)
                    {
                        _sqlDatabases.Items.Clear();
                        foreach (string db in dbs)
                            _sqlDatabases.Items.Add(db);
                        if (_sqlDatabases.Items.Count > 0)
                            _sqlDatabases.SelectedIndex = 0;
                    }

                    SetStatus("SQL", "Połączono. Baz: " + dbs.Count + ".");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Błąd połączenia: " + ex.Message, "Błąd",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                SetStatus("SQL", "Błąd połączenia z serwerem");
            }
        }

        private List<string> GetDatabasesList(SqlConnection conn)
        {
            List<string> list = new List<string>();

            using (SqlCommand cmd = new SqlCommand(@"
                SELECT name 
                FROM sys.databases 
                WHERE database_id > 4
                ORDER BY name;", conn))
            using (SqlDataReader r = cmd.ExecuteReader())
            {
                while (r.Read())
                {
                    list.Add(r.GetString(0));
                }
            }

            return list;
        }

        private void SqlDatabases_SelectedIndexChanged(object sender, EventArgs e)
        {
            try
            {
                if (_sqlDatabases == null || _sqlDatabases.SelectedItem == null || string.IsNullOrEmpty(_sqlBaseConnectionString))
                    return;

                string dbName = _sqlDatabases.SelectedItem.ToString();
                string connStr = _sqlBaseConnectionString + "Database=" + dbName + ";";

                using (SqlConnection conn = new SqlConnection(connStr))
                {
                    conn.Open();
                    List<string> tables = GetTablesList(conn);

                    if (_sqlTables != null)
                    {
                        _sqlTables.Items.Clear();
                        foreach (string t in tables)
                            _sqlTables.Items.Add(t);
                        if (_sqlTables.Items.Count > 0)
                            _sqlTables.SelectedIndex = 0;
                    }

                    SetStatus("SQL", "Baza " + dbName + ": tabel " + tables.Count + ".");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Błąd pobierania tabel: " + ex.Message, "Błąd",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                SetStatus("SQL", "Błąd pobierania tabel");
            }
        }

        private List<string> GetTablesList(SqlConnection conn)
        {
            List<string> list = new List<string>();

            using (SqlCommand cmd = new SqlCommand(@"
                SELECT TABLE_SCHEMA, TABLE_NAME
                FROM INFORMATION_SCHEMA.TABLES
                WHERE TABLE_TYPE = 'BASE TABLE'
                ORDER BY TABLE_SCHEMA, TABLE_NAME;", conn))
            using (SqlDataReader r = cmd.ExecuteReader())
            {
                while (r.Read())
                {
                    string schema = r.GetString(0);
                    string name = r.GetString(1);
                    list.Add(schema + "." + name);
                }
            }

            return list;
        }

        private void SqlGenerate_Click(object sender, EventArgs e)
        {
            try
            {
                if (_sqlDatabases == null || _sqlTables == null || _sqlScript == null)
                    return;

                _sqlScript.Clear();

                if (_sqlDatabases.SelectedItem == null || _sqlTables.SelectedItem == null)
                {
                    MessageBox.Show(this, "Wybierz bazę danych i tabelę.", "Informacja",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                if (string.IsNullOrEmpty(_sqlBaseConnectionString))
                {
                    MessageBox.Show(this, "Najpierw połącz się z serwerem.", "Informacja",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                string dbName = _sqlDatabases.SelectedItem.ToString();
                string tableFull = _sqlTables.SelectedItem.ToString();
                string[] parts = tableFull.Split('.');
                if (parts.Length != 2)
                {
                    MessageBox.Show(this, "Nieprawidłowa nazwa tabeli.", "Błąd",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                string schema = parts[0];
                string table = parts[1];

                string connStr = _sqlBaseConnectionString + "Database=" + dbName + ";";

                StringBuilder sb = new StringBuilder();
                SetStatus("SQL", "Generowanie skryptu...");

                using (SqlConnection conn = new SqlConnection(connStr))
                {
                    conn.Open();

                    string createScript = GenerateCreateTableScript(conn, schema, table);
                    sb.AppendLine("-- Skrypt dla tabeli " + schema + "." + table + " w bazie " + dbName);
                    sb.AppendLine("SET ANSI_NULLS ON;");
                    sb.AppendLine("SET QUOTED_IDENTIFIER ON;");
                    sb.AppendLine();
                    sb.AppendLine(createScript);
                    sb.AppendLine("GO");
                    sb.AppendLine();

                    if (_sqlIncludeData != null && _sqlIncludeData.Checked)
                    {
                        string where = _sqlWhere == null ? "" : _sqlWhere.Text.Trim();
                        int rowCount;
                        string insertScript = GenerateInsertForTable(conn, schema, table, where, out rowCount);
                        sb.AppendLine(insertScript);
                        SetStatus("SQL", "CREATE TABLE + " + rowCount + " wierszy INSERT.");
                    }
                    else
                    {
                        SetStatus("SQL", "Wygenerowano tylko CREATE TABLE.");
                    }
                }

                _sqlScript.Text = sb.ToString();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Błąd generowania skryptu: " + ex.Message, "Błąd",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void SqlSaveToFile_Click(object sender, EventArgs e)
        {
            if (_sqlScript == null || string.IsNullOrEmpty(_sqlScript.Text))
            {
                MessageBox.Show(this, "Brak skryptu do zapisania.", "Informacja",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                using (SaveFileDialog sfd = new SaveFileDialog())
                {
                    sfd.Filter = "Pliki SQL (*.sql)|*.sql|Wszystkie pliki (*.*)|*.*";

                    string dbName = _sqlDatabases != null && _sqlDatabases.SelectedItem != null
                        ? _sqlDatabases.SelectedItem.ToString()
                        : "DB";
                    string table = _sqlTables != null && _sqlTables.SelectedItem != null
                        ? _sqlTables.SelectedItem.ToString()
                        : "Table";

                    sfd.FileName = dbName + "_" + table + ".sql";

                    if (sfd.ShowDialog(this) == DialogResult.OK)
                    {
                        File.WriteAllText(sfd.FileName, _sqlScript.Text, Encoding.UTF8);
                        SetStatus("SQL", "Zapisano do " + sfd.FileName + ".");
                    }
                }
            }
        }

        // === NOWE: skryptowanie obiektu z tempdb po object_id ===
        private void SqlScriptTempObject_Click(object sender, EventArgs e)
        {
            if (_sqlScript == null)
                return;

            _sqlScript.Clear();

            if (string.IsNullOrEmpty(_sqlBaseConnectionString))
            {
                MessageBox.Show(this, "Najpierw połącz się z serwerem (Połącz).", "Informacja",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (_sqlTempObjectId == null || string.IsNullOrWhiteSpace(_sqlTempObjectId.Text))
            {
                MessageBox.Show(this, "Podaj object_id obiektu w tempdb.", "Informacja",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (!int.TryParse(_sqlTempObjectId.Text.Trim(), out int objectId))
            {
                MessageBox.Show(this, "Nieprawidłowy object_id (oczekiwano liczby całkowitej).", "Błąd",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            try
            {
                string connStr = _sqlBaseConnectionString + "Database=tempdb;";
                using (SqlConnection conn = new SqlConnection(connStr))
                {
                    conn.Open();

                    string objName = null;
                    string schemaName = null;

                    using (SqlCommand cmd = new SqlCommand(@"
                        SELECT t.name, s.name AS schema_name
                        FROM sys.tables t
                        JOIN sys.schemas s ON t.schema_id = s.schema_id
                        WHERE t.object_id = @obj;", conn))
                    {
                        cmd.Parameters.AddWithValue("@obj", objectId);

                        using (SqlDataReader r = cmd.ExecuteReader())
                        {
                            if (r.Read())
                            {
                                objName = r.GetString(0);
                                schemaName = r.IsDBNull(1) ? "dbo" : r.GetString(1);
                            }
                        }
                    }

                    if (objName == null)
                    {
                        MessageBox.Show(this,
                            "Nie znaleziono tabeli użytkownika w tempdb dla object_id = " + objectId,
                            "Informacja",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information);
                        SetStatus("SQL", "Brak tabeli w tempdb.");
                        return;
                    }

                    StringBuilder sb = new StringBuilder();
                    sb.AppendLine("-- Skrypt obiektu z tempdb (object_id = " + objectId + ")");
                    sb.AppendLine("-- Nazwa: " + schemaName + "." + objName);
                    sb.AppendLine("USE tempdb;");
                    sb.AppendLine("GO");
                    sb.AppendLine();

                    string createScript = GenerateCreateTableScriptForObjectId(conn, objectId, schemaName, objName);
                    sb.AppendLine(createScript);
                    sb.AppendLine("GO");
                    sb.AppendLine();

                    if (_sqlIncludeData != null && _sqlIncludeData.Checked)
                    {
                        int rowCount;
                        string dataScript = GenerateInsertForTable(conn, schemaName, objName, "", out rowCount);
                        sb.AppendLine(dataScript);
                        SetStatus("SQL", "CREATE TABLE + " + rowCount + " wierszy INSERT z tempdb.");
                    }
                    else
                    {
                        SetStatus("SQL", "Wygenerowano tylko CREATE TABLE z tempdb.");
                    }

                    _sqlScript.Text = sb.ToString();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Błąd skryptowania obiektu z tempdb:\n" + ex.Message, "Błąd",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                SetStatus("SQL", "Błąd skryptowania z tempdb.");
            }
        }

        // === NOWE: pełny SELECT -> INSERT ===
        private void SqlGenerateFromSelect_Click(object sender, EventArgs e)
        {
            if (_sqlScript == null || _sqlFullSelect == null)
                return;

            _sqlScript.Clear();

            if (string.IsNullOrEmpty(_sqlBaseConnectionString))
            {
                MessageBox.Show(this, "Najpierw połącz się z serwerem (Połącz).", "Informacja",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string selectText = _sqlFullSelect.Text;
            if (string.IsNullOrWhiteSpace(selectText))
            {
                MessageBox.Show(this, "Wpisz pełne zapytanie SELECT.", "Informacja",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            try
            {
                string dbName = _sqlDatabases != null && _sqlDatabases.SelectedItem != null
                    ? _sqlDatabases.SelectedItem.ToString()
                    : "master";

                string connStr = _sqlBaseConnectionString + "Database=" + dbName + ";";

                DataTable dt = new DataTable();
                using (SqlConnection conn = new SqlConnection(connStr))
                using (SqlDataAdapter da = new SqlDataAdapter(selectText, conn))
                {
                    conn.Open();
                    da.Fill(dt);
                }

                if (dt.Rows.Count == 0)
                {
                    _sqlScript.Text = "-- SELECT nie zwrócił żadnych wierszy.";
                    return;
                }

                string targetTable = InferTargetTableNameFromSelect(selectText);

                string script = GenerateInsertFromSelectResults(dt, targetTable, selectText);
                _sqlScript.Text = script;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Błąd wykonania SELECT i generowania INSERT:\n" + ex.Message, "Błąd",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                SetStatus("SQL", "Błąd SELECT -> INSERT");
            }
        }

        private string InferTargetTableNameFromSelect(string selectText)
        {
            if (string.IsNullOrWhiteSpace(selectText))
                return null;

            var match = Regex.Match(selectText, @"\bfrom\s+([^\s;\r\n]+)", RegexOptions.IgnoreCase);
            if (!match.Success)
                return null;

            string token = match.Groups[1].Value.Trim();
            token = token.TrimEnd(',', ')');
            return token;
        }

        private string GenerateInsertFromSelectResults(DataTable dt, string targetTable, string originalSelect)
        {
            StringBuilder sb = new StringBuilder();

            sb.AppendLine("-- INSERT wygenerowany z SELECT:");
            sb.AppendLine("-- " + originalSelect.Replace("\r", " ").Replace("\n", " "));
            sb.AppendLine();

            string tableName = string.IsNullOrWhiteSpace(targetTable) ? "<TWOJA_TABELA>" : targetTable;

            string[] columnNames = new string[dt.Columns.Count];
            for (int i = 0; i < dt.Columns.Count; i++)
                columnNames[i] = "[" + dt.Columns[i].ColumnName + "]";
            string columnList = string.Join(", ", columnNames);

            foreach (DataRow row in dt.Rows)
            {
                string[] values = new string[dt.Columns.Count];
                for (int c = 0; c < dt.Columns.Count; c++)
                {
                    values[c] = ToSqlLiteral(row[c], dt.Columns[c].DataType);
                }

                string valuesList = string.Join(", ", values);
                sb.AppendLine("INSERT INTO " + tableName + " (" + columnList + ") VALUES (" + valuesList + ");");
            }

            return sb.ToString();
        }

        // ==== Generowanie T-SQL (CREATE + INSERT) ==== 
        private class ColumnInfo
        {
            public string Name { get; set; }
            public string DataType { get; set; }
            public bool IsNullable { get; set; }
            public int? MaxLength { get; set; }
            public string Default { get; set; }
            public byte? NumericPrecision { get; set; }
            public int? NumericScale { get; set; }
        }

        private string GenerateCreateTableScript(SqlConnection conn, string schema, string table)
        {
            List<ColumnInfo> columns = new List<ColumnInfo>();

            using (SqlCommand cmd = new SqlCommand(@"
                SELECT 
                    c.COLUMN_NAME,
                    c.DATA_TYPE,
                    c.IS_NULLABLE,
                    c.CHARACTER_MAXIMUM_LENGTH,
                    c.COLUMN_DEFAULT,
                    c.NUMERIC_PRECISION,
                    c.NUMERIC_SCALE
                FROM INFORMATION_SCHEMA.COLUMNS c
                WHERE c.TABLE_SCHEMA = @schema
                  AND c.TABLE_NAME = @table
                ORDER BY c.ORDINAL_POSITION;", conn))
            {
                cmd.Parameters.AddWithValue("@schema", schema);
                cmd.Parameters.AddWithValue("@table", table);

                using (SqlDataReader r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        ColumnInfo col = new ColumnInfo();
                        col.Name = r.GetString(0);
                        col.DataType = r.GetString(1);
                        col.IsNullable = r.GetString(2) == "YES";
                        col.MaxLength = r.IsDBNull(3) ? (int?)null : r.GetInt32(3);
                        col.Default = r.IsDBNull(4) ? null : r.GetString(4);
                        col.NumericPrecision = r.IsDBNull(5) ? (byte?)null : r.GetByte(5);
                        col.NumericScale = r.IsDBNull(6) ? (int?)null : Convert.ToInt32(r.GetValue(6));
                        columns.Add(col);
                    }
                }
            }

            List<string> pkColumns = new List<string>();
            string pkName = null;

            using (SqlCommand cmd = new SqlCommand(@"
                SELECT k.COLUMN_NAME, k.ORDINAL_POSITION, t.CONSTRAINT_NAME
                FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS t
                JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE k
                  ON t.CONSTRAINT_NAME = k.CONSTRAINT_NAME
                 AND t.TABLE_SCHEMA = k.TABLE_SCHEMA
                 AND t.TABLE_NAME = k.TABLE_NAME
                WHERE t.TABLE_SCHEMA = @schema
                  AND t.TABLE_NAME = @table
                  AND t.CONSTRAINT_TYPE = 'PRIMARY KEY'
                ORDER BY k.ORDINAL_POSITION;", conn))
            {
                cmd.Parameters.AddWithValue("@schema", schema);
                cmd.Parameters.AddWithValue("@table", table);

                using (SqlDataReader r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        pkColumns.Add(r.GetString(0));
                        if (pkName == null)
                            pkName = r.GetString(2);
                    }
                }
            }

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("CREATE TABLE [" + schema + "].[" + table + "]");
            sb.AppendLine("(");

            List<string> lines = new List<string>();

            foreach (ColumnInfo col in columns)
            {
                string t = BuildSqlType(col);
                string nullStr = col.IsNullable ? "NULL" : "NOT NULL";

                string defaultStr = "";
                if (!string.IsNullOrEmpty(col.Default))
                {
                    defaultStr = " DEFAULT " + col.Default;
                }

                string line = "    [" + col.Name + "] " + t + " " + nullStr + defaultStr;
                lines.Add(line);
            }

            if (pkColumns.Count > 0)
            {
                List<string> pkColsQuoted = new List<string>();
                foreach (string c in pkColumns)
                    pkColsQuoted.Add("[" + c + "]");

                if (string.IsNullOrEmpty(pkName))
                    pkName = "PK_" + table;

                string pkLine = "    CONSTRAINT [" + pkName + "] PRIMARY KEY CLUSTERED (" +
                                string.Join(", ", pkColsQuoted) + ")";
                lines.Add(pkLine);
            }

            sb.AppendLine(string.Join(",\n", lines));
            sb.Append(")");

            return sb.ToString();
        }

        // tempdb.sys.tables po object_id
        private string GenerateCreateTableScriptForObjectId(SqlConnection conn, int objectId, string schema, string table)
        {
            List<ColumnInfo> columns = new List<ColumnInfo>();

            using (SqlCommand cmd = new SqlCommand(@"
                SELECT 
                    c.name,
                    t.name AS data_type,
                    c.is_nullable,
                    c.max_length,
                    c.precision,
                    c.scale,
                    dc.definition AS column_default
                FROM sys.columns c
                JOIN sys.types t ON c.user_type_id = t.user_type_id
                LEFT JOIN sys.default_constraints dc ON c.default_object_id = dc.object_id
                WHERE c.object_id = @obj
                ORDER BY c.column_id;", conn))
            {
                cmd.Parameters.AddWithValue("@obj", objectId);

                using (SqlDataReader r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        ColumnInfo col = new ColumnInfo();
                        col.Name = r.GetString(0);
                        col.DataType = r.GetString(1);
                        col.IsNullable = r.GetBoolean(2);
                        col.MaxLength = r.IsDBNull(3) ? (int?)null : Convert.ToInt32(r.GetInt16(3));
                        col.NumericPrecision = r.IsDBNull(4) ? (byte?)null : r.GetByte(4);
                        col.NumericScale = r.IsDBNull(5) ? (int?)null : Convert.ToInt32(r.GetByte(5));
                        col.Default = r.IsDBNull(6) ? null : r.GetString(6);
                        columns.Add(col);
                    }
                }
            }

            List<string> pkColumns = new List<string>();
            string pkName = null;

            using (SqlCommand cmd = new SqlCommand(@"
                SELECT c.name, ic.key_ordinal, kc.name AS constraint_name
                FROM sys.key_constraints kc
                JOIN sys.index_columns ic 
                  ON kc.parent_object_id = ic.object_id 
                 AND kc.unique_index_id = ic.index_id
                JOIN sys.columns c 
                  ON ic.object_id = c.object_id 
                 AND ic.column_id = c.column_id
                WHERE kc.parent_object_id = @obj
                  AND kc.type = 'PK'
                ORDER BY ic.key_ordinal;", conn))
            {
                cmd.Parameters.AddWithValue("@obj", objectId);

                using (SqlDataReader r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        pkColumns.Add(r.GetString(0));
                        if (pkName == null)
                            pkName = r.GetString(2);
                    }
                }
            }

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("CREATE TABLE [" + schema + "].[" + table + "]");
            sb.AppendLine("(");

            List<string> lines = new List<string>();

            foreach (ColumnInfo col in columns)
            {
                string t = BuildSqlType(col);
                string nullStr = col.IsNullable ? "NULL" : "NOT NULL";

                string defaultStr = "";
                if (!string.IsNullOrEmpty(col.Default))
                {
                    defaultStr = " DEFAULT " + col.Default;
                }

                string line = "    [" + col.Name + "] " + t + " " + nullStr + defaultStr;
                lines.Add(line);
            }

            if (pkColumns.Count > 0)
            {
                List<string> pkColsQuoted = new List<string>();
                foreach (string c in pkColumns)
                    pkColsQuoted.Add("[" + c + "]");

                if (string.IsNullOrEmpty(pkName))
                    pkName = "PK_" + table;

                string pkLine = "    CONSTRAINT [" + pkName + "] PRIMARY KEY CLUSTERED (" +
                                string.Join(", ", pkColsQuoted) + ")";
                lines.Add(pkLine);
            }

            sb.AppendLine(string.Join(",\n", lines));
            sb.Append(")");

            return sb.ToString();
        }

        private string BuildSqlType(ColumnInfo col)
        {
            string t = col.DataType.ToLowerInvariant();

            switch (t)
            {
                case "char":
                case "varchar":
                case "nchar":
                case "nvarchar":
                case "binary":
                case "varbinary":
                    if (col.MaxLength == null || col.MaxLength <= 0)
                        return t + "(max)";
                    return t + "(" + (col.MaxLength == -1 ? "max" : col.MaxLength.ToString()) + ")";

                case "decimal":
                case "numeric":
                    if (col.NumericPrecision != null && col.NumericScale != null)
                        return t + "(" + col.NumericPrecision + "," + col.NumericScale + ")";
                    return t;

                default:
                    return t;
            }
        }

        private string GenerateInsertForTable(SqlConnection conn, string schema, string table, string whereClause, out int rowCount)
        {
            string sql = "SELECT * FROM [" + schema + "].[" + table + "]";
            if (!string.IsNullOrWhiteSpace(whereClause))
            {
                sql += " WHERE " + whereClause;
            }

            DataTable dt = new DataTable();
            using (SqlDataAdapter da = new SqlDataAdapter(sql, conn))
            {
                da.Fill(dt);
            }

            if (dt.Rows.Count == 0)
            {
                rowCount = 0;
                if (!string.IsNullOrWhiteSpace(whereClause))
                    return "-- Tabela [" + schema + "].[" + table + "] – brak danych dla WHERE " + whereClause + ".\n";
                return "-- Tabela [" + schema + "].[" + table + "] jest pusta.\n";
            }

            rowCount = dt.Rows.Count;

            StringBuilder sb = new StringBuilder();

            string[] columnNames = new string[dt.Columns.Count];
            for (int i = 0; i < dt.Columns.Count; i++)
                columnNames[i] = "[" + dt.Columns[i].ColumnName + "]";

            string columnList = string.Join(", ", columnNames);

            if (!string.IsNullOrWhiteSpace(whereClause))
                sb.AppendLine("-- Dane tabeli [" + schema + "].[" + table + "] (WHERE " + whereClause + ")");
            else
                sb.AppendLine("-- Dane tabeli [" + schema + "].[" + table + "]");

            for (int r = 0; r < dt.Rows.Count; r++)
            {
                DataRow row = dt.Rows[r];
                string[] values = new string[dt.Columns.Count];

                for (int c = 0; c < dt.Columns.Count; c++)
                {
                    object val = row[c];
                    Type type = dt.Columns[c].DataType;
                    values[c] = ToSqlLiteral(val, type);
                }

                string valuesList = string.Join(", ", values);
                sb.AppendLine("INSERT INTO [" + schema + "].[" + table + "] (" + columnList + ") VALUES (" + valuesList + ");");
            }

            return sb.ToString();
        }

        private string ToSqlLiteral(object value, Type type)
        {
            if (value == null || value == DBNull.Value)
                return "NULL";

            if (type == typeof(string) || type == typeof(char))
            {
                string s = value.ToString().Replace("'", "''");
                return "N'" + s + "'";
            }

            if (type == typeof(DateTime))
            {
                DateTime dt = (DateTime)value;
                return "'" + dt.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture) + "'";
            }

            if (type == typeof(bool))
            {
                return ((bool)value) ? "1" : "0";
            }

            if (type == typeof(byte[]))
            {
                byte[] bytes = (byte[])value;
                if (bytes.Length == 0) return "0x";
                StringBuilder sb = new StringBuilder("0x");
                foreach (byte b in bytes)
                    sb.Append(b.ToString("X2"));
                return sb.ToString();
            }

            if (type.IsPrimitive || type == typeof(decimal))
            {
                return Convert.ToString(value, CultureInfo.InvariantCulture);
            }

            string fallback = value.ToString().Replace("'", "''");
            return "N'" + fallback + "'";
        }

        // ====== Kolorowanie SQL ======
        private void ApplySqlSyntaxHighlighting()
        {
            if (_sqlScript == null || _sqlScript.TextLength == 0)
                return;

            if (_isHighlightingSql)
                return;

            try
            {
                _isHighlightingSql = true;

                int selStart = _sqlScript.SelectionStart;
                int selLength = _sqlScript.SelectionLength;

                _sqlScript.SuspendLayout();

                _sqlScript.SelectionStart = 0;
                _sqlScript.SelectionLength = _sqlScript.TextLength;
                _sqlScript.SelectionColor = Color.Black;

                // słowa kluczowe
                HighlightPattern(@"\b(SELECT|FROM|WHERE|AND|OR|INNER|LEFT|RIGHT|FULL|JOIN|ON|GROUP|BY|ORDER|INSERT|INTO|VALUES|UPDATE|SET|DELETE|TOP|DISTINCT|AS|IS|NOT|NULL|IN|EXISTS|BETWEEN|LIKE|UNION|ALL|CASE|WHEN|THEN|ELSE|END|CREATE|TABLE|ALTER|DROP|PRIMARY|KEY|CLUSTERED|NONCLUSTERED|IDENTITY|CONSTRAINT|DEFAULT|FOREIGN|REFERENCES|INDEX|VIEW|PROCEDURE|FUNCTION|DECLARE|BEGIN|END|IF|WHILE|RETURN|USE|GO)\b",
                                Color.Blue);

                // typy danych
                HighlightPattern(@"\b(INT|BIGINT|SMALLINT|TINYINT|DECIMAL|NUMERIC|MONEY|SMALLMONEY|FLOAT|REAL|DATE|DATETIME|DATETIME2|SMALLDATETIME|TIME|CHAR|NCHAR|VARCHAR|NVARCHAR|TEXT|NTEXT|BIT|BINARY|VARBINARY|IMAGE|UNIQUEIDENTIFIER|XML|CURSOR|SQL_VARIANT)\b",
                                Color.DarkCyan);

                // obiekty systemowe
                HighlightPattern(@"\b(sys\.tables|sys\.columns|sys\.objects|sys\.schemas|sys\.indexes|sys\.index_columns|sys\.key_constraints|INFORMATION_SCHEMA\.[A-Z_]+)\b",
                                Color.DarkMagenta);

                // nazwy w nawiasach []
                HighlightPattern(@"\[[^\]]+\]", Color.Brown);

                _sqlScript.SelectionStart = selStart;
                _sqlScript.SelectionLength = selLength;
                _sqlScript.SelectionColor = Color.Black;

                _sqlScript.ResumeLayout();
            }
            finally
            {
                _isHighlightingSql = false;
            }
        }

        private void HighlightPattern(string pattern, Color color)
        {
            foreach (Match m in Regex.Matches(_sqlScript.Text, pattern, RegexOptions.IgnoreCase))
            {
                _sqlScript.SelectionStart = m.Index;
                _sqlScript.SelectionLength = m.Length;
                _sqlScript.SelectionColor = color;
            }
        }

        // ====== Pomocnicze ======
        private class TabBinding
        {
            public DataTable Table { get; set; }
            public DataGridView Grid { get; set; }
        }
    }
}

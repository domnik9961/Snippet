using System;
using System.Collections.Generic;
using System.Data;              // DataTable, DataView
using System.Diagnostics;       // Process
using System.Drawing;           // SystemIcons
using System.IO;
using System.Linq;
using System.Runtime.InteropServices; // P/Invoke
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;   // Task, async/await
using System.Windows.Forms;

namespace SnippetMgr
{
    public partial class Form1 : Form
    {
        private const int MaxTabs = 9;
        private const int StatusRightMaxLen = 100;

        // ===== WinAPI / Hotkeys / Clipboard =====
        private const int WM_HOTKEY = 0x0312;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int WM_SYSKEYUP = 0x0105;
        private const int WM_CLIPBOARDUPDATE = 0x031D;
        private const int SW_RESTORE = 9;

        private const uint MOD_ALT = 0x0001;
        private const uint MOD_CONTROL = 0x0002;
        private const uint MOD_SHIFT = 0x0004;
        private const uint MOD_WIN = 0x0008;

        private const int ID_HOTKEY_PRIMARY = 9000;  // Win+Y
        private const int ID_HOTKEY_FALLBACK = 9001;  // Ctrl+Alt+Y
        private const int ID_HOTKEY_HISTORY = 9002;  // Win+`
        private const int ID_HOTKEY_HISTORY_FB = 9003;  // Ctrl+Alt+`

        [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, Keys vk);
        [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll")] private static extern bool AddClipboardFormatListener(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

        private bool _primaryRegistered = false;
        private bool _fallbackRegistered = false;
        private bool _histRegistered = false;
        private bool _histRegisteredFb = false;

        // === Clipboard collection ===
        private bool _suppressNextClipboardCapture = false;
        private const int ClipboardMaxLen = 20000; // ogranicz bardzo długie bloki

        // ===== Tray =====
        private NotifyIcon? _tray;
        private ContextMenuStrip? _trayMenu;

        // ===== Pliki =====
        private string TemplatesPath => Path.Combine(AppContext.BaseDirectory, "Data", "templates.json");
        private string HistoryPath => Path.Combine(AppContext.BaseDirectory, "Data", "copied_history.json");

        // ===== Historia (tab + grid + tabela + toolbar) =====
        private TabPage? _historyPage;
        private DataGridView? _historyGrid;
        private DataTable? _historyTable;

        // toolbar (tylko dla „Historia”)
        private ToolStrip? _histToolbar;
        private ToolStripTextBox? _histSearch;
        private CheckBox? _histOnlyPinned;
        private NumericUpDown? _histMinUses;
        private ComboBox? _histSort;

        public Form1()
        {
            InitializeComponent();

            try { statusStrip1.ShowItemToolTips = true; } catch { }

            SetupTray();

            this.Load += Form1_Load;

            this.KeyPreview = true;
            this.KeyDown += Form1_KeyDown;

            textBox1.TextChanged += textBox1_TextChanged;
            textBox1.KeyDown += textBox1_KeyDown;

            tabControl1.SelectedIndexChanged += TabControl1_SelectedIndexChanged;

            try { this.MainMenuStrip = this.menuStrip1; } catch { }

            this.Shown += (s, e) => FocusSearch();
            this.Activated += (s, e) => FocusSearch(false);
        }

        // ===== Helper: ustaw kursor w textBox1 =====
        private void FocusSearch(bool selectAll = true)
        {
            try
            {
                if (textBox1.CanFocus)
                {
                    textBox1.Focus();
                    if (selectAll) textBox1.SelectAll();
                    else textBox1.Select(textBox1.TextLength, 0);
                }
                this.ActiveControl = textBox1;
            }
            catch { }
        }

        // ===== Rejestracja globalnych skrótów + clipboard listener =====
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            TryRegisterHotkeys();
            try { AddClipboardFormatListener(this.Handle); } catch { }
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            try { RemoveClipboardFormatListener(this.Handle); } catch { }
            try { if (_primaryRegistered) UnregisterHotKey(this.Handle, ID_HOTKEY_PRIMARY); } catch { }
            try { if (_fallbackRegistered) UnregisterHotKey(this.Handle, ID_HOTKEY_FALLBACK); } catch { }
            try { if (_histRegistered) UnregisterHotKey(this.Handle, ID_HOTKEY_HISTORY); } catch { }
            try { if (_histRegisteredFb) UnregisterHotKey(this.Handle, ID_HOTKEY_HISTORY_FB); } catch { }
            base.OnHandleDestroyed(e);
        }

        private void TryRegisterHotkeys()
        {
            // Win+Y toggle
            _primaryRegistered = RegisterHotKey(this.Handle, ID_HOTKEY_PRIMARY, MOD_WIN, Keys.Y);
            if (!_primaryRegistered)
            {
                _fallbackRegistered = RegisterHotKey(this.Handle, ID_HOTKEY_FALLBACK, MOD_CONTROL | MOD_ALT, Keys.Y);
                if (_fallbackRegistered)
                {
                    BeginInvoke(new Action(() =>
                        MessageBox.Show(this, "Win+Y jest zajęty przez system. Użyj Ctrl+Alt+Y.",
                            "Skrót alternatywny", MessageBoxButtons.OK, MessageBoxIcon.Information)
                    ));
                }
                else
                {
                    BeginInvoke(new Action(() =>
                        MessageBox.Show(this, "Nie udało się zarejestrować skrótów Win+Y ani Ctrl+Alt+Y.",
                            "Błąd rejestracji", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                    ));
                }
            }

            // Win+`
            _histRegistered = RegisterHotKey(this.Handle, ID_HOTKEY_HISTORY, MOD_WIN, Keys.Oem3);
            if (!_histRegistered)
            {
                _histRegisteredFb = RegisterHotKey(this.Handle, ID_HOTKEY_HISTORY_FB, MOD_CONTROL | MOD_ALT, Keys.Oem3);
                if (_histRegisteredFb)
                {
                    BeginInvoke(new Action(() =>
                        MessageBox.Show(this, "Win+` jest zajęty. Użyj Ctrl+Alt+` aby otworzyć Historię.",
                            "Skrót alternatywny", MessageBoxButtons.OK, MessageBoxIcon.Information)
                    ));
                }
                else
                {
                    BeginInvoke(new Action(() =>
                        MessageBox.Show(this, "Nie udało się zarejestrować skrótów do Historii (Win+` / Ctrl+Alt+`).",
                            "Błąd rejestracji", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                    ));
                }
            }
        }

        // ========= ALT + 1..9 =========
        private void Form1_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Alt && e.KeyCode >= Keys.D1 && e.KeyCode <= Keys.D9)
            {
                int index = e.KeyCode - Keys.D1;
                if (index < tabControl1.TabPages.Count)
                {
                    tabControl1.SelectedIndex = index;
                    e.Handled = true;
                    ApplyFilterToActiveTab(textBox1.Text);
                    BeginInvoke(new Action(() => FocusSearch(false)));
                }
            }
        }

        // ===== Obsługa komunikatów okna =====
        protected override void WndProc(ref Message m)
        {
            // Globalne hotkeye
            if (m.Msg == WM_HOTKEY)
            {
                int id = m.WParam.ToInt32();
                if (id == ID_HOTKEY_PRIMARY || id == ID_HOTKEY_FALLBACK)
                {
                    ToggleWindow();
                    return;
                }
                if (id == ID_HOTKEY_HISTORY || id == ID_HOTKEY_HISTORY_FB)
                {
                    ShowAndActivate();
                    EnsureHistoryTabCreated();
                    if (_historyPage != null)
                    {
                        tabControl1.SelectedTab = _historyPage;
                        BeginInvoke(new Action(() =>
                        {
                            _historyGrid?.Focus();
                            if (_historyGrid != null && _historyGrid.Rows.Count > 0)
                            {
                                _historyGrid.ClearSelection();
                                _historyGrid.Rows[0].Selected = true;
                            }
                        }));
                    }
                    return;
                }
            }

            // Nasłuch schowka
            if (m.Msg == WM_CLIPBOARDUPDATE)
            {
                TryCaptureClipboard();
                base.WndProc(ref m);
                return;
            }

            // Wyłącz „Alt otwiera menu”
            if ((m.Msg == WM_SYSKEYDOWN || m.Msg == WM_SYSKEYUP) && (Keys)m.WParam == Keys.Menu)
            {
                FocusSearch(false);
                return;
            }

            base.WndProc(ref m);
        }

        // Zbieranie wszystkiego ze schowka (tekst)
        private void TryCaptureClipboard()
        {
            try
            {
                if (_suppressNextClipboardCapture)
                {
                    _suppressNextClipboardCapture = false; // pomiń jednorazowo nasz własny SetText
                    return;
                }

                if (Clipboard.ContainsText())
                {
                    string txt = Clipboard.GetText() ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(txt)) return;
                    if (txt.Length > ClipboardMaxLen) txt = txt.Substring(0, ClipboardMaxLen);

                    AddOrBumpHistory(txt, DateTime.Now, pinned: false, increaseUse: 1);
                    SaveHistoryToDisk();
                }
            }
            catch
            {
                // schowek może być chwilowo zajęty przez inne procesy – pomijamy
            }
        }

        private void ToggleWindow()
        {
            bool isActive = this.Visible &&
                            this.WindowState != FormWindowState.Minimized &&
                            Form.ActiveForm == this;

            if (isActive) HideToTray(); else ShowAndActivate();
        }

        private void HideToTray()
        {
            this.Hide();
            this.ShowInTaskbar = false;
            if (this.WindowState != FormWindowState.Minimized)
                this.WindowState = FormWindowState.Minimized;
        }

        private void ShowAndActivate()
        {
            if (!this.Visible) this.Show();
            this.ShowInTaskbar = true;

            if (this.WindowState == FormWindowState.Minimized)
                this.WindowState = FormWindowState.Normal;

            ShowWindow(this.Handle, SW_RESTORE);

            bool wasTopMost = this.TopMost;
            this.TopMost = true;
            this.TopMost = wasTopMost;

            SetForegroundWindow(this.Handle);
            this.Activate();
            this.BringToFront();
            this.Focus();

            FocusSearch(false);
        }

        // ===== Tray =====
        private void SetupTray()
        {
            _trayMenu = new ContextMenuStrip();
            _trayMenu.Items.Add("Pokaż okno", null, (_, __) => ShowAndActivate());
            _trayMenu.Items.Add("Przejdź do historii (Win+`)", null, (_, __) =>
            {
                ShowAndActivate();
                EnsureHistoryTabCreated();
                if (_historyPage != null) tabControl1.SelectedTab = _historyPage;
            });
            _trayMenu.Items.Add("Ukryj", null, (_, __) => HideToTray());
            _trayMenu.Items.Add(new ToolStripSeparator());
            _trayMenu.Items.Add("Zakończ", null, (_, __) => Close());

            _tray = new NotifyIcon
            {
                Icon = SystemIcons.Application,
                Visible = true,
                Text = "SnippetMgr (Win+Y / Ctrl+Alt+Y, Win+` / Ctrl+Alt+`)",
                ContextMenuStrip = _trayMenu
            };
            _tray.DoubleClick += (_, __) => ShowAndActivate();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            try { if (_primaryRegistered) UnregisterHotKey(this.Handle, ID_HOTKEY_PRIMARY); } catch { }
            try { if (_fallbackRegistered) UnregisterHotKey(this.Handle, ID_HOTKEY_FALLBACK); } catch { }
            try { if (_histRegistered) UnregisterHotKey(this.Handle, ID_HOTKEY_HISTORY); } catch { }
            try { if (_histRegisteredFb) UnregisterHotKey(this.Handle, ID_HOTKEY_HISTORY_FB); } catch { }
            try { RemoveClipboardFormatListener(this.Handle); } catch { }

            if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
            _trayMenu?.Dispose();
            base.OnFormClosing(e);
        }

        // ===== menu: Zarządzaj… — PRZEŁADUJ JSON =====
        private void zarzadzajToolStripMenuItem_Click(object sender, EventArgs e)
        {
            try
            {
                EnsureDataDir();

                if (!File.Exists(TemplatesPath))
                    throw new FileNotFoundException($"Nie znaleziono pliku: {TemplatesPath}");

                string json = File.ReadAllText(TemplatesPath, Encoding.UTF8);
                CreateTabsFromJson(json);
                LoadHistoryFromDisk(); // opcjonalnie
                ApplyFilterToActiveTab(textBox1.Text);
                SetStatus("JSON wczytany", "program działa");
                FocusSearch();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Nie udało się przeładować danych:\n{ex.Message}", "Błąd",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ===== Startup =====
        private void Form1_Load(object? sender, EventArgs e)
        {
            try
            {
                EnsureDataDir();

                if (!File.Exists(TemplatesPath))
                    throw new FileNotFoundException($"Nie znaleziono pliku: {TemplatesPath}");

                string json = File.ReadAllText(TemplatesPath, Encoding.UTF8);

                CreateTabsFromJson(json);
                LoadHistoryFromDisk();
                ApplyFilterToActiveTab(textBox1.Text);

                SetStatus("JSON wczytany", "program działa");
                FocusSearch();
            }
            catch (FileNotFoundException ex)
            {
                MessageBox.Show(ex.Message, "Błąd — brak pliku", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                SetStatus("Błąd", "Brak pliku JSON");
                FocusSearch();
            }
            catch (JsonException ex)
            {
                MessageBox.Show($"Plik JSON ma nieprawidłowy format.\n{ex.Message}", "Błąd JSON", MessageBoxButtons.OK, MessageBoxIcon.Error);
                SetStatus("Błąd", "Niepoprawny JSON");
                FocusSearch();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Wystąpił nieoczekiwany błąd.\n{ex.Message}", "Błąd", MessageBoxButtons.OK, MessageBoxIcon.Error);
                SetStatus("Błąd", "Program działa z błędem");
                FocusSearch();
            }
        }

        private void EnsureDataDir()
        {
            string dir = Path.Combine(AppContext.BaseDirectory, "Data");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        }

        // ====== Tworzenie zakładek z templates.json ======
        private void CreateTabsFromJson(string json)
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("tabs", out var tabs) || tabs.ValueKind != JsonValueKind.Array)
                throw new JsonException("Plik JSON nie zawiera prawidłowego pola 'tabs'.");

            tabControl1.TabPages.Clear();
            // wyczyść referencje historii (utworzymy na nowo)
            _historyPage = null; _historyGrid = null; _historyTable = null;
            _histToolbar = null; _histSearch = null; _histOnlyPinned = null; _histMinUses = null; _histSort = null;

            int totalTabs = tabs.GetArrayLength();
            int addedCount = 0;

            if (totalTabs > MaxTabs)
            {
                MessageBox.Show(
                    $"Plik JSON zawiera {totalTabs} zakładek.\n" +
                    $"Maksymalnie można utworzyć {MaxTabs} zakładek.\n" +
                    $"Zostaną wczytane tylko pierwsze {MaxTabs}.",
                    "Limit zakładek",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );
            }

            foreach (var tab in tabs.EnumerateArray())
            {
                if (addedCount >= MaxTabs) break;
                if (!tab.TryGetProperty("tab_name", out var nameEl) || nameEl.ValueKind != JsonValueKind.String)
                    continue;

                string name = nameEl.GetString() ?? "Bez nazwy";
                var page = new TabPage($"{addedCount + 1}. {name}");

                var grid = new DataGridView
                {
                    Dock = DockStyle.Fill,
                    ReadOnly = true,
                    AllowUserToAddRows = false,
                    AllowUserToDeleteRows = false,
                    AllowUserToOrderColumns = true,
                    SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                    AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                    MultiSelect = false
                };

                var table = new DataTable { CaseSensitive = false };
                table.Columns.Add("Shortcut", typeof(string));
                table.Columns.Add("Description", typeof(string));
                table.Columns.Add("Text", typeof(string));

                if (tab.TryGetProperty("entries", out var entriesEl) && entriesEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var entry in entriesEl.EnumerateArray())
                    {
                        string shortcut = entry.TryGetProperty("shortcut", out var sEl) && sEl.ValueKind == JsonValueKind.String ? sEl.GetString() ?? "" : "";
                        string description = entry.TryGetProperty("description", out var dEl) && dEl.ValueKind == JsonValueKind.String ? dEl.GetString() ?? "" : "";
                        string pasteText = entry.TryGetProperty("paste_text", out var pEl) && pEl.ValueKind == JsonValueKind.String ? pEl.GetString() ?? "" : "";

                        table.Rows.Add(shortcut, description, pasteText);
                    }
                }

                grid.DataSource = table.DefaultView;

                grid.CellDoubleClick += async (s, e) =>
                {
                    if (e.RowIndex >= 0 && e.RowIndex < grid.Rows.Count)
                    {
                        var row = grid.Rows[e.RowIndex];
                        var val = row.Cells["Text"].Value?.ToString() ?? "";
                        if (!string.IsNullOrWhiteSpace(val))
                        {
                            TryCopyWithStatus(val); // zapis + fuzja w historii
                            await HideAndPasteAsync();
                        }
                    }
                };

                page.Controls.Add(grid);
                tabControl1.TabPages.Add(page);
                addedCount++;
            }

            EnsureHistoryTabCreated();

            if (addedCount == 0)
            {
                MessageBox.Show("Nie znaleziono żadnych wpisów 'tab_name' w pliku JSON.",
                    "Brak zakładek", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                tabControl1.SelectedIndex = 0;
            }
        }

        // ====== Karta „Historia” + toolbar (BEZPIECZNA) ======
        private void EnsureHistoryTabCreated()
        {
            try
            {
                if (_historyPage != null && _historyGrid != null && _historyTable != null)
                {
                    if (tabControl1 != null && !tabControl1.TabPages.Contains(_historyPage))
                        tabControl1.TabPages.Add(_historyPage);
                    return;
                }

                if (tabControl1 == null) return;

                _historyPage = new TabPage("Historia");

                // toolbar
                _histToolbar = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Dock = DockStyle.Top, Padding = new Padding(4) };
                var lblFind = new ToolStripLabel("Szukaj:");
                _histSearch = new ToolStripTextBox { AutoSize = false, Width = 220 };
                _histSearch.TextChanged += (s, e) => ApplyHistoryView();

                _histOnlyPinned = new CheckBox { Text = "Tylko przypięte", AutoSize = true };
                _histOnlyPinned.CheckedChanged += (s, e) => ApplyHistoryView();
                var hostPinned = new ToolStripControlHost(_histOnlyPinned) { Margin = new Padding(8, 0, 0, 0) };

                var lblMin = new ToolStripLabel("Min. użyć:");
                _histMinUses = new NumericUpDown { Minimum = 0, Maximum = 9999, Value = 0, Width = 60 };
                _histMinUses.ValueChanged += (s, e) => ApplyHistoryView();
                var hostMin = new ToolStripControlHost(_histMinUses) { Margin = new Padding(4, 0, 0, 0) };

                var lblSort = new ToolStripLabel("Sortuj:");
                _histSort = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
                _histSort.Items.AddRange(new object[] { "Ostatnio użyte", "Najczęściej", "Alfabetycznie", "Data dodania" });
                if (_histSort.Items.Count > 0) _histSort.SelectedIndex = 0;
                _histSort.SelectedIndexChanged += (s, e) => ApplyHistoryView();
                var hostSort = new ToolStripControlHost(_histSort) { Margin = new Padding(4, 0, 0, 0) };

                _histToolbar.Items.Add(lblFind);
                _histToolbar.Items.Add(_histSearch);
                _histToolbar.Items.Add(new ToolStripSeparator());
                _histToolbar.Items.Add(hostPinned);
                _histToolbar.Items.Add(new ToolStripSeparator());
                _histToolbar.Items.Add(lblMin);
                _histToolbar.Items.Add(hostMin);
                _histToolbar.Items.Add(new ToolStripSeparator());
                _histToolbar.Items.Add(lblSort);
                _histToolbar.Items.Add(hostSort);

                // grid
                _historyGrid = new DataGridView
                {
                    Dock = DockStyle.Fill,
                    ReadOnly = false,
                    AllowUserToAddRows = false,
                    AllowUserToDeleteRows = false,
                    AllowUserToOrderColumns = true,
                    SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                    AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                    MultiSelect = true
                };

                _historyTable = new DataTable { CaseSensitive = false };
                _historyTable.Columns.Add("Text", typeof(string));
                _historyTable.Columns.Add("FirstAdded", typeof(DateTime));
                _historyTable.Columns.Add("LastUsed", typeof(DateTime));
                _historyTable.Columns.Add("Uses", typeof(int));
                _historyTable.Columns.Add("Pinned", typeof(bool));

                _historyGrid.DataSource = _historyTable;
                SafeSetFillWeight(_historyGrid, "Text", 60);
                SafeSetFillWeight(_historyGrid, "LastUsed", 16);
                SafeSetFillWeight(_historyGrid, "FirstAdded", 14);
                SafeSetFillWeight(_historyGrid, "Uses", 6);
                SafeSetFillWeight(_historyGrid, "Pinned", 4);

                _historyGrid.CellDoubleClick += async (s, e) =>
                {
                    if (e.RowIndex >= 0 && _historyGrid != null && e.RowIndex < _historyGrid.Rows.Count)
                    {
                        var txt = _historyGrid.Rows[e.RowIndex].Cells["Text"].Value?.ToString() ?? "";
                        if (!string.IsNullOrWhiteSpace(txt))
                        {
                            TryCopyWithStatus(txt);
                            await HideAndPasteAsync();
                        }
                    }
                };
                _historyGrid.KeyDown += (s, e) =>
                {
                    if (e.KeyCode == Keys.Delete)
                    {
                        DeleteSelectedHistoryRows();
                        e.Handled = true;
                    }
                };
                _historyGrid.CellValueChanged += (s, e) =>
                {
                    if (e.RowIndex >= 0) SaveHistoryToDisk();
                };
                _historyGrid.CellMouseDown += (s, e) =>
                {
                    if (e.Button == MouseButtons.Right && e.RowIndex >= 0)
                    {
                        _historyGrid.ClearSelection();
                        _historyGrid.Rows[e.RowIndex].Selected = true;
                        _historyGrid.CurrentCell = _historyGrid.Rows[e.RowIndex].Cells[Math.Max(0, e.ColumnIndex)];
                    }
                };

                AttachHistoryContextMenu(_historyGrid);

                _historyPage.Controls.Add(_historyGrid);
                _historyPage.Controls.Add(_histToolbar);
                tabControl1.TabPages.Add(_historyPage);

                ApplyHistoryView();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this,
                    "Nie udało się utworzyć zakładki 'Historia'.\nSzczegóły: " + ex.Message,
                    "Błąd inicjalizacji Historii", MessageBoxButtons.OK, MessageBoxIcon.Error);

                try
                {
                    if (_historyPage != null && tabControl1 != null && tabControl1.TabPages.Contains(_historyPage))
                        tabControl1.TabPages.Remove(_historyPage);
                }
                catch { }

                _historyPage = null; _historyGrid = null; _historyTable = null;
                _histToolbar = null; _histSearch = null; _histOnlyPinned = null; _histMinUses = null; _histSort = null;
            }
        }

        private static void SafeSetFillWeight(DataGridView grid, string colName, float weight)
        {
            try
            {
                if (grid.Columns.Contains(colName))
                    grid.Columns[colName].FillWeight = weight;
            }
            catch { }
        }

        private void AttachHistoryContextMenu(DataGridView grid)
        {
            var menu = new ContextMenuStrip();
            var miCopy = new ToolStripMenuItem("Kopiuj \"Text\"");
            var miCopyPaste = new ToolStripMenuItem("Kopiuj + Wklej i ukryj");
            var miPin = new ToolStripMenuItem("Przypnij / Odepnij");
            var miDel = new ToolStripMenuItem("Usuń zaznaczone");
            var miDelUnpinned = new ToolStripMenuItem("Usuń wszystkie nieprzypięte");
            var miClearAll = new ToolStripMenuItem("Wyczyść wszystko");
            var miExport = new ToolStripMenuItem("Eksport do JSON…");
            var miImport = new ToolStripMenuItem("Import z JSON…");

            miCopy.Click += (_, __) =>
            {
                var txt = GetHistorySelectedText();
                if (!string.IsNullOrEmpty(txt)) TryCopyWithStatus(txt);
            };
            miCopyPaste.Click += async (_, __) =>
            {
                var txt = GetHistorySelectedText();
                if (!string.IsNullOrEmpty(txt))
                {
                    TryCopyWithStatus(txt);
                    await HideAndPasteAsync();
                }
            };
            miPin.Click += (_, __) => TogglePinnedSelected();
            miDel.Click += (_, __) => DeleteSelectedHistoryRows();
            miDelUnpinned.Click += (_, __) => DeleteAllUnpinned();
            miClearAll.Click += (_, __) => ClearAllHistory();
            miExport.Click += (_, __) => ExportHistory();
            miImport.Click += (_, __) => ImportHistory();

            menu.Items.Add(miCopy);
            menu.Items.Add(miCopyPaste);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(miPin);
            menu.Items.Add(miDel);
            menu.Items.Add(miDelUnpinned);
            menu.Items.Add(miClearAll);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(miExport);
            menu.Items.Add(miImport);

            grid.ContextMenuStrip = menu;
        }

        private string GetHistorySelectedText()
        {
            if (_historyGrid == null) return "";
            if (_historyGrid.SelectedRows.Count > 0)
                return _historyGrid.SelectedRows[0].Cells["Text"].Value?.ToString() ?? "";
            if (_historyGrid.CurrentRow != null)
                return _historyGrid.CurrentRow.Cells["Text"].Value?.ToString() ?? "";
            return "";
        }

        private void TogglePinnedSelected()
        {
            if (_historyGrid == null || _historyTable == null) return;
            foreach (DataGridViewRow r in _historyGrid.SelectedRows)
            {
                bool cur = Convert.ToBoolean(r.Cells["Pinned"].Value ?? false);
                r.Cells["Pinned"].Value = !cur;
            }
            SaveHistoryToDisk();
            ApplyHistoryView();
        }

        private void DeleteSelectedHistoryRows()
        {
            if (_historyGrid == null || _historyTable == null) return;
            if (_historyGrid.SelectedRows.Count == 0) return;

            if (MessageBox.Show(this, "Usunąć zaznaczone wpisy historii?", "Potwierdź",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

            foreach (DataGridViewRow r in _historyGrid.SelectedRows)
            {
                if (!r.IsNewRow) _historyGrid.Rows.Remove(r);
            }
            SaveHistoryToDisk();
        }

        private void DeleteAllUnpinned()
        {
            if (_historyGrid == null || _historyTable == null) return;

            if (MessageBox.Show(this, "Usunąć wszystkie NIEPRZYPINANE wpisy?", "Potwierdź",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

            var toDelete = new List<DataGridViewRow>();
            foreach (DataGridViewRow r in _historyGrid.Rows)
            {
                bool pinned = Convert.ToBoolean(r.Cells["Pinned"].Value ?? false);
                if (!pinned && !r.IsNewRow) toDelete.Add(r);
            }
            foreach (var r in toDelete) _historyGrid.Rows.Remove(r);

            SaveHistoryToDisk();
        }

        private void ClearAllHistory()
        {
            if (_historyTable == null) return;

            if (MessageBox.Show(this, "Wyczyścić CAŁĄ historię (łącznie z przypiętymi)?", "Potwierdź",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;

            _historyTable.Rows.Clear();
            SaveHistoryToDisk();
        }

        private void ExportHistory()
        {
            try
            {
                if (_historyTable == null) return;
                using var sfd = new SaveFileDialog
                {
                    Filter = "JSON (*.json)|*.json",
                    FileName = $"copied_history_{DateTime.Now:yyyyMMdd_HHmm}.json"
                };
                if (sfd.ShowDialog(this) == DialogResult.OK)
                {
                    var list = HistoryTableToList();
                    var json = JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(sfd.FileName, json, Encoding.UTF8);
                    SetStatus("Eksport", sfd.FileName);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Eksport nieudany: {ex.Message}", "Błąd", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ImportHistory()
        {
            try
            {
                if (_historyTable == null) return;
                using var ofd = new OpenFileDialog
                {
                    Filter = "JSON (*.json)|*.json",
                    Multiselect = false
                };
                if (ofd.ShowDialog(this) == DialogResult.OK)
                {
                    var json = File.ReadAllText(ofd.FileName, Encoding.UTF8);

                    List<HistoryEntry>? listNew = null;
                    List<LegacyHistoryEntry>? listOld = null;

                    try { listNew = JsonSerializer.Deserialize<List<HistoryEntry>>(json); }
                    catch { }

                    if (listNew == null || listNew.All(x => x == null))
                    {
                        try { listOld = JsonSerializer.Deserialize<List<LegacyHistoryEntry>>(json); } catch { }
                    }

                    if (listNew != null && listNew.Count > 0)
                    {
                        foreach (var it in listNew)
                        {
                            if (string.IsNullOrWhiteSpace(it.Text)) continue;
                            AddOrBumpHistory(it.Text,
                                it.LastUsed == default ? DateTime.Now : it.LastUsed,
                                it.Pinned,
                                increaseUse: Math.Max(1, it.Uses));
                            var row = FindHistoryRowByText(it.Text);
                            if (row != null && it.FirstAdded != default)
                                row["FirstAdded"] = it.FirstAdded;
                        }
                    }
                    else if (listOld != null && listOld.Count > 0)
                    {
                        foreach (var it in listOld)
                        {
                            if (string.IsNullOrWhiteSpace(it.Text)) continue;
                            AddOrBumpHistory(it.Text,
                                it.Date == default ? DateTime.Now : it.Date,
                                it.Pinned,
                                increaseUse: 1);
                            var row = FindHistoryRowByText(it.Text);
                            if (row != null) row["FirstAdded"] = it.Date;
                        }
                    }

                    SaveHistoryToDisk();
                    ApplyHistoryView();
                    SetStatus("Importowano", "OK");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Import nieudany: {ex.Message}", "Błąd", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ===== Ładowanie / Zapis historii =====
        private void LoadHistoryFromDisk()
        {
            try
            {
                EnsureHistoryTabCreated();
                if (_historyTable == null) return;

                _historyTable.Rows.Clear();

                if (!File.Exists(HistoryPath)) return;

                var json = File.ReadAllText(HistoryPath, Encoding.UTF8);

                List<HistoryEntry>? listNew = null;
                List<LegacyHistoryEntry>? listOld = null;

                try { listNew = JsonSerializer.Deserialize<List<HistoryEntry>>(json); }
                catch { }

                if (listNew != null && listNew.Count > 0)
                {
                    foreach (var it in listNew.OrderByDescending(x => x.LastUsed))
                    {
                        var row = _historyTable.NewRow();
                        row["Text"] = it.Text ?? "";
                        row["FirstAdded"] = it.FirstAdded == default ? (it.LastUsed == default ? DateTime.Now : it.LastUsed) : it.FirstAdded;
                        row["LastUsed"] = it.LastUsed == default ? DateTime.Now : it.LastUsed;
                        row["Uses"] = Math.Max(1, it.Uses);
                        row["Pinned"] = it.Pinned;
                        _historyTable.Rows.Add(row);
                    }
                }
                else
                {
                    try { listOld = JsonSerializer.Deserialize<List<LegacyHistoryEntry>>(json); } catch { }
                    if (listOld != null)
                    {
                        foreach (var it in listOld.OrderByDescending(x => x.Date))
                        {
                            var row = _historyTable.NewRow();
                            row["Text"] = it.Text ?? "";
                            row["FirstAdded"] = it.Date == default ? DateTime.Now : it.Date;
                            row["LastUsed"] = it.Date == default ? DateTime.Now : it.Date;
                            row["Uses"] = 1;
                            row["Pinned"] = it.Pinned;
                            _historyTable.Rows.Add(row);
                        }
                    }
                }

                ApplyHistoryView();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Nie udało się wczytać historii:\n{ex.Message}", "Błąd historii",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void SaveHistoryToDisk()
        {
            try
            {
                EnsureDataDir();
                var list = HistoryTableToList();
                var json = JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(HistoryPath, json, Encoding.UTF8);
            }
            catch
            {
                // cicho
            }
        }

        private List<HistoryEntry> HistoryTableToList()
        {
            var list = new List<HistoryEntry>();
            if (_historyTable == null) return list;

            foreach (DataRow r in _historyTable.Rows)
            {
                list.Add(new HistoryEntry
                {
                    Text = r.Field<string>("Text") ?? "",
                    FirstAdded = r.Field<DateTime>("FirstAdded"),
                    LastUsed = r.Field<DateTime>("LastUsed"),
                    Uses = Math.Max/***************************************
***************************************/(1, r.Field<int>("Uses")) // <-- UWAGA: C# wymaga Math.Max, poprawka niżej
                });
            }
            // POPRAWKA: powyżej literówka — wklej końcową wersję:
            list = new List<HistoryEntry>();
            foreach (DataRow r in _historyTable.Rows)
            {
                list.Add(new HistoryEntry
                {
                    Text = r.Field<string>("Text") ?? "",
                    FirstAdded = r.Field<DateTime>("FirstAdded"),
                    LastUsed = r.Field<DateTime>("LastUsed"),
                    Uses = Math.Max(1, r.Field<int>("Uses")),
                    Pinned = r.Field<bool>("Pinned")
                });
            }

            return list.OrderByDescending(x => x.Pinned)
                       .ThenByDescending(x => x.LastUsed)
                       .ThenByDescending(x => x.Uses)
                       .ToList();
        }

        // ===== API fuzji duplikatów =====
        private void AddOrBumpHistory(string text, DateTime when, bool pinned = false, int increaseUse = 1)
        {
            EnsureHistoryTabCreated();
            if (_historyTable == null) return;
            if (string.IsNullOrWhiteSpace(text)) return;

            var row = FindHistoryRowByText(text);
            if (row != null)
            {
                int uses = Math.Max(1, Convert.ToInt32(row["Uses"])) + Math.Max(1, increaseUse);
                row["Uses"] = uses;
                row["LastUsed"] = when;
                if (pinned) row["Pinned"] = true;

                var cloned = _historyTable.NewRow();
                cloned.ItemArray = row.ItemArray.Clone() as object[] ?? row.ItemArray;
                _historyTable.Rows.Remove(row);
                _historyTable.Rows.InsertAt(cloned, 0);
            }
            else
            {
                var nr = _historyTable.NewRow();
                nr["Text"] = text;
                nr["FirstAdded"] = when;
                nr["LastUsed"] = when;
                nr["Uses"] = Math.Max(1, increaseUse);
                nr["Pinned"] = pinned;
                _historyTable.Rows.InsertAt(nr, 0);
            }

            ApplyHistoryView();
        }

        private DataRow? FindHistoryRowByText(string text)
        {
            if (_historyTable == null) return null;
            foreach (DataRow r in _historyTable.Rows)
            {
                if (string.Equals(r.Field<string>("Text") ?? "", text, StringComparison.Ordinal))
                    return r;
            }
            return null;
        }

        // ====== Filtry/sortowanie dla Historii ======
        private void ApplyHistoryView()
        {
            if (_historyTable == null) return;

            var view = _historyTable.DefaultView;

            string qToolbar = _histSearch?.Text?.Trim() ?? "";
            string qGlobal = textBox1?.Text?.Trim() ?? "";

            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(qToolbar))
                parts.Add($"[Text] LIKE '%{EscapeLikeValue(qToolbar)}%'");
            if (!string.IsNullOrWhiteSpace(qGlobal))
                parts.Add($"[Text] LIKE '%{EscapeLikeValue(qGlobal)}%'");

            if (_histOnlyPinned != null && _histOnlyPinned.Checked)
                parts.Add("[Pinned] = TRUE");

            if (_histMinUses != null && _histMinUses.Value > 0)
                parts.Add($"[Uses] >= {_histMinUses.Value}");

            view.RowFilter = parts.Count == 0 ? "" : string.Join(" AND ", parts);

            string sort = "Pinned DESC, LastUsed DESC, Uses DESC";
            if (_histSort != null)
            {
                switch (_histSort.SelectedIndex)
                {
                    case 0: sort = "Pinned DESC, LastUsed DESC, Uses DESC"; break; // Ostatnio użyte
                    case 1: sort = "Pinned DESC, Uses DESC, LastUsed DESC"; break; // Najczęściej
                    case 2: sort = "Pinned DESC, Text ASC"; break;                 // Alfabetycznie
                    case 3: sort = "Pinned DESC, FirstAdded DESC"; break;          // Data dodania
                }
            }
            view.Sort = sort;
        }

        // ===== Filtrowanie w aktywnej zakładce (inne niż Historia) =====
        private void textBox1_TextChanged(object? sender, EventArgs e)
        {
            ApplyFilterToActiveTab(textBox1.Text);
        }

        private void ApplyFilterToActiveTab(string query)
        {
            if (tabControl1.SelectedTab == _historyPage)
            {
                ApplyHistoryView();
                return;
            }

            var grid = GetActiveGrid();
            if (grid == null) return;

            DataView? view = grid.DataSource as DataView;
            if (view == null)
            {
                if (grid.DataSource is DataTable t)
                    view = t.DefaultView;
                else
                    return;
            }

            if (string.IsNullOrWhiteSpace(query))
            {
                view.RowFilter = string.Empty;
                return;
            }

            string escaped = EscapeLikeValue(query.Trim());
            view.RowFilter =
                $"[Shortcut] LIKE '%{escaped}%' OR " +
                $"[Description] LIKE '%{escaped}%' OR " +
                $"[Text] LIKE '%{escaped}%'";
        }

        private static string EscapeLikeValue(string value)
        {
            if (string.IsNullOrEmpty(value)) return value;
            value = value.Replace("'", "''");
            value = value.Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]");
            return value;
        }

        private DataGridView? GetActiveGrid()
        {
            if (tabControl1.TabPages.Count == 0) return null;
            var page = tabControl1.SelectedTab;
            if (page == null) return null;
            if (page == _historyPage) return _historyGrid;
            return page.Controls.OfType<DataGridView>().FirstOrDefault();
        }

        // ===== Enter w polu wyszukiwarki =====
        private async void textBox1_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;

                if (tabControl1.SelectedTab == _historyPage && _historyGrid != null)
                {
                    foreach (DataGridViewRow row in _historyGrid.Rows)
                    {
                        if (row.Visible)
                        {
                            var val = row.Cells["Text"].Value?.ToString() ?? "";
                            if (!string.IsNullOrWhiteSpace(val))
                            {
                                TryCopyWithStatus(val);
                                await HideAndPasteAsync();
                            }
                            break;
                        }
                    }
                    return;
                }

                var grid = GetActiveGrid();
                if (grid == null || grid.Rows.Count == 0)
                {
                    FocusSearch();
                    return;
                }

                foreach (DataGridViewRow row in grid.Rows)
                {
                    if (row.Visible)
                    {
                        var val = row.Cells["Text"].Value?.ToString() ?? "";
                        if (!string.IsNullOrWhiteSpace(val))
                        {
                            TryCopyWithStatus(val);
                            await HideAndPasteAsync();
                        }
                        break;
                    }
                }
            }
        }

        // ===== po zmianie zakładki: filtr + fokus do textBox1 =====
        private void TabControl1_SelectedIndexChanged(object? sender, EventArgs e)
        {
            ApplyFilterToActiveTab(textBox1.Text);
            BeginInvoke(new Action(() => FocusSearch(false)));
        }

        // ===== ukryj okno i wklej Ctrl+V do aktywnej aplikacji =====
        private async Task HideAndPasteAsync()
        {
            try
            {
                HideToTray();
                await Task.Delay(150);
                SendKeys.SendWait("^v");
            }
            catch
            {
                // nic – tekst już w schowku
            }
        }

        // ===== Kopiowanie + zapis do Historii (fuzja) =====
        private void TryCopyWithStatus(string fullText)
        {
            try
            {
                _suppressNextClipboardCapture = true; // nie dubluj z WM_CLIPBOARDUPDATE
                Clipboard.SetText(fullText);

                SetStatus("Skopiowano", fullText);

                AddOrBumpHistory(fullText, DateTime.Now, pinned: false, increaseUse: 1);
                SaveHistoryToDisk();
            }
            catch
            {
                MessageBox.Show("Nie udało się skopiować do schowka.", "Błąd", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void SetStatus(string left, string rightFull)
        {
            try
            {
                toolStripStatusLabel1.Text = left ?? string.Empty;

                string shown = rightFull ?? string.Empty;
                if (shown.Length > StatusRightMaxLen)
                    shown = shown.Substring(0, StatusRightMaxLen) + "…";

                toolStripStatusLabel2.Text = shown;
                toolStripStatusLabel2.ToolTipText = rightFull ?? string.Empty;
            }
            catch { }
        }
    }

    // ===== MODELE JSON (poza Form1, aby uniknąć kolizji w partial) =====

    /// <summary>
    /// Nowy format historii.
    /// </summary>
    public sealed class HistoryEntry
    {
        [JsonPropertyName("text")] public string Text { get; set; } = "";
        [JsonPropertyName("firstAdded")] public DateTime FirstAdded { get; set; }
        [JsonPropertyName("lastUsed")] public DateTime LastUsed { get; set; }
        [JsonPropertyName("uses")] public int Uses { get; set; }
        [JsonPropertyName("pinned")] public bool Pinned { get; set; }
    }

    /// <summary>
    /// Stary format (wsteczna zgodność).
    /// </summary>
    public sealed class LegacyHistoryEntry
    {
        [JsonPropertyName("text")] public string Text { get; set; } = "";
        [JsonPropertyName("date")] public DateTime Date { get; set; }
        [JsonPropertyName("pinned")] public bool Pinned { get; set; }
    }
}

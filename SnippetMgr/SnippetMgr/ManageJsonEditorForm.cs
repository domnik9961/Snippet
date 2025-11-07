using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;

namespace SnippetMgr
{
    public partial class ManageJsonEditorForm : Form
    {
        private readonly string _filePath;

        // Flaga dla Form1 – jeśli zapisaliśmy poprawny JSON, ustawiamy na true
        public bool JsonChanged { get; private set; }

        public ManageJsonEditorForm(string filePath)
        {
            _filePath = filePath;
            JsonChanged = false;

            InitializeComponent();
            LoadJsonFromFile();
        }

        private void LoadJsonFromFile()
        {
            try
            {
                if (File.Exists(_filePath))
                {
                    txtJson.Text = File.ReadAllText(_filePath, Encoding.UTF8);
                }
                else
                {
                    // Minimalny szablon, jeśli plik nie istnieje
                    txtJson.Text =
@"{
  ""configName"": ""Nowy-Profil"",
  ""config"": {
    ""sql"": {
      ""server"": """",
      ""useWindowsAuth"": true,
      ""user"": """",
      ""password"": """",
      ""defaultDatabase"": """"
    },
    ""ui"": {
      ""startMinimized"": false,
      ""alwaysOnTop"": false,
      ""hotkey"": ""Win+Y""
    },
    ""behavior"": {
      ""autoPasteOnSnippetSelect"": true,
      ""rememberLastTab"": false,
      ""clearSearchOnTabChange"": true,
      ""focusSearchOnTabChange"": true
    }
  },
  ""tabs"": []
}";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this,
                    "Nie udało się wczytać pliku JSON:\r\n" + ex.Message,
                    "Błąd",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private void btnSave_Click(object sender, EventArgs e)
        {
            try
            {
                string text = txtJson.Text ?? string.Empty;

                // Walidacja składni JSON
                using (JsonDocument.Parse(text))
                {
                    File.WriteAllText(_filePath, text, Encoding.UTF8);
                    JsonChanged = true;

                    MessageBox.Show(this,
                        "Plik JSON zapisany.",
                        "Informacja",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }
            }
            catch (JsonException jex)
            {
                MessageBox.Show(this,
                    "Błąd składni JSON:\r\n" + jex.Message,
                    "Błąd JSON",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this,
                    "Nie udało się zapisać pliku JSON:\r\n" + ex.Message,
                    "Błąd",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private void btnFormat_Click(object sender, EventArgs e)
        {
            try
            {
                string raw = txtJson.Text;
                if (string.IsNullOrWhiteSpace(raw))
                    return;

                using (JsonDocument doc = JsonDocument.Parse(raw))
                {
                    var opts = new JsonWriterOptions
                    {
                        Indented = true,
                        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                    };

                    using (var ms = new MemoryStream())
                    using (var writer = new Utf8JsonWriter(ms, opts))
                    {
                        doc.WriteTo(writer);
                        writer.Flush();
                        string formatted = Encoding.UTF8.GetString(ms.ToArray());
                        txtJson.Text = formatted;
                    }
                }
            }
            catch (JsonException jex)
            {
                MessageBox.Show(this,
                    "Błąd składni JSON przy formatowaniu:\r\n" + jex.Message,
                    "Błąd JSON",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this,
                    "Błąd formatowania JSON:\r\n" + ex.Message,
                    "Błąd",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private void btnClose_Click(object sender, EventArgs e)
        {
            this.Close();
        }

        // Skrót Ctrl+S – szybki zapis
        private void ManageJsonEditorForm_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.S)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                btnSave_Click(this, EventArgs.Empty);
            }
        }
    }
}

using System;
using System.IO;
using System.Windows.Forms;

namespace SnippetMgr
{
    // To jest plik code-behind do formularza utworzonego w Designerze.
    // Jeśli masz też ManageJsonEditorForm.Designer.cs – zostaw go bez zmian.
    public partial class ManageJsonEditorForm : Form
    {
        private readonly string _jsonPath = string.Empty;

        // Konstruktor wymagany przez Designera
        public ManageJsonEditorForm()
        {
            InitializeComponent();
        }

        // ✅ Konstruktor z 1 argumentem — tego wymaga Twój Form1
        public ManageJsonEditorForm(string jsonPath) : this()
        {
            _jsonPath = jsonPath ?? string.Empty;

            // Opcjonalne: pokaż w tytule nazwę pliku
            try
            {
                if (!string.IsNullOrWhiteSpace(_jsonPath))
                    this.Text = $"Zarządzaj — {Path.GetFileName(_jsonPath)}";
            }
            catch { /* ignoruj drobne błędy UI */ }
        }

        // (opcjonalnie) jeśli chcesz mieć dostęp do ścieżki wewnątrz formularza:
        public string JsonPath => _jsonPath;

        private void splitContainer1_Panel1_Paint(object sender, PaintEventArgs e)
        {

        }
    }
}

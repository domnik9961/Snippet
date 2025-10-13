using System.Drawing;
using System.Windows.Forms;

namespace SnippetMgr
{
    partial class Form1
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        private void InitializeComponent()
        {
            components = new System.ComponentModel.Container();

            // === Top menu ===
            menuStrip1 = new MenuStrip();
            opcjeToolStripMenuItem = new ToolStripMenuItem();
            jSONToolStripMenuItem = new ToolStripMenuItem();
            zarzadzajToolStripMenuItem = new ToolStripMenuItem();
            exitToolStripMenuItem = new ToolStripMenuItem();

            // === Layout ===
            mainLayout = new TableLayoutPanel();
            searchLayout = new TableLayoutPanel();

            // === Controls ===
            lblSearch = new Label();
            textBox1 = new TextBox();
            tabControl1 = new TabControl();
            statusStrip1 = new StatusStrip();
            toolStripStatusLabel1 = new ToolStripStatusLabel();
            toolStripStatusLabel2 = new ToolStripStatusLabel();

            // =========================
            // MenuStrip (kompakt + ciemny motyw)
            // =========================
            menuStrip1.ImageScalingSize = new Size(18, 18);
            menuStrip1.Location = new Point(0, 0);
            menuStrip1.Name = "menuStrip1";
            menuStrip1.TabIndex = 0;
            menuStrip1.Text = "menuStrip1";
            menuStrip1.RenderMode = ToolStripRenderMode.Professional;
            menuStrip1.BackColor = Color.FromArgb(33, 37, 41);
            menuStrip1.ForeColor = Color.White;
            menuStrip1.Hide();

            // "Plik"
            opcjeToolStripMenuItem.Name = "opcjeToolStripMenuItem";
            opcjeToolStripMenuItem.Text = "Plik";
            opcjeToolStripMenuItem.ForeColor = Color.White;

            // "Zarządzaj JSON…"
            zarzadzajToolStripMenuItem.Name = "zarzadzajToolStripMenuItem";
            zarzadzajToolStripMenuItem.Text = "Zarządzaj JSON…";
            zarzadzajToolStripMenuItem.ForeColor = Color.Black; // w dropdownie jasne tło
            // Handler masz w Form1.cs — podpinamy:
            zarzadzajToolStripMenuItem.Click += zarzadzajToolStripMenuItem_Click;

            // "Wyjście"
        //    exitToolStripMenuItem.Name = "exitToolStripMenuItem";
      //      exitToolStripMenuItem.Text = "Wyjście";
    //        exitToolStripMenuItem.ShortcutKeys = Keys.Alt | Keys.F4;
  //          exitToolStripMenuItem.ForeColor = Color.Black;
//            exitToolStripMenuItem.Click += (_, __) => Close();

            opcjeToolStripMenuItem.DropDownItems.AddRange(new ToolStripItem[]
            {
                zarzadzajToolStripMenuItem,
                new ToolStripSeparator(),
                exitToolStripMenuItem
            });

            // "Widok" (placeholder)
            jSONToolStripMenuItem.Name = "jSONToolStripMenuItem";
            jSONToolStripMenuItem.Text = "Widok";
            jSONToolStripMenuItem.ForeColor = Color.White;

            // Dodaj do menu głównego
            menuStrip1.Items.AddRange(new ToolStripItem[]
            {
                opcjeToolStripMenuItem,
                jSONToolStripMenuItem
            });

            // =========================
            // Główny layout (kompaktowo)
            // =========================
            mainLayout.ColumnCount = 1;
            mainLayout.RowCount = 2; // 0: pasek szukaj, 1: taby
            mainLayout.Name = "mainLayout";
            mainLayout.Dock = DockStyle.Fill;
            mainLayout.Padding = new Padding(6); // ciaśniej
            mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            // =========================
            // Pasek wyszukiwania (kompakt + kolor tła)
            // =========================
            searchLayout.ColumnCount = 2;
            searchLayout.RowCount = 1;
            searchLayout.Dock = DockStyle.Top;
            searchLayout.AutoSize = true;
            searchLayout.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            searchLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));       // label
            searchLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));  // textbox fill
            searchLayout.Padding = new Padding(6);
            searchLayout.Margin = new Padding(0, 4, 0, 6);
            searchLayout.BackColor = Color.FromArgb(245, 248, 255); // delikatny niebieskawy panel

            // Label "Szukaj:"
            lblSearch.AutoSize = true;
            lblSearch.Margin = new Padding(0, 3, 8, 0);
            lblSearch.Name = "lblSearch";
            lblSearch.Text = "Szukaj:";
            lblSearch.ForeColor = Color.FromArgb(52, 58, 64);

            // TextBox (pełna szerokość, mniejszy)
            textBox1.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            textBox1.Margin = new Padding(0, 0, 0, 0);
            textBox1.Name = "textBox1";
            textBox1.PlaceholderText = "Wpisz, aby przefiltrować…";
            textBox1.TabIndex = 0;
            textBox1.Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            textBox1.MinimumSize = new Size(200, 0);

            searchLayout.Controls.Add(lblSearch, 0, 0);
            searchLayout.Controls.Add(textBox1, 1, 0);

            // =========================
            // TabControl (wypełnia, kompaktowe marginesy)
            // =========================
            tabControl1.Dock = DockStyle.Fill;
            tabControl1.Name = "tabControl1";
            tabControl1.TabIndex = 1;
            tabControl1.Margin = new Padding(0);

            // =========================
            // StatusStrip (kompakt + lekki kolor)
            // =========================
            statusStrip1.Name = "statusStrip1";
            statusStrip1.Dock = DockStyle.Bottom;
            statusStrip1.SizingGrip = false;
            statusStrip1.BackColor = Color.FromArgb(245, 245, 248);
            statusStrip1.ForeColor = Color.FromArgb(73, 80, 87);
            statusStrip1.ImageScalingSize = new Size(16, 16);

            toolStripStatusLabel1.Name = "toolStripStatusLabel1";
            toolStripStatusLabel1.Text = ""; // lewy komunikat

            toolStripStatusLabel2.Name = "toolStripStatusLabel2";
            toolStripStatusLabel2.Spring = true; // wypchnij do prawej
            toolStripStatusLabel2.TextAlign = ContentAlignment.MiddleRight;

            statusStrip1.Items.AddRange(new ToolStripItem[]
            {
                toolStripStatusLabel1,
                toolStripStatusLabel2
            });

            // =========================
            // Złożenie całości
            // =========================
            mainLayout.Controls.Add(searchLayout, 0, 0);
            mainLayout.Controls.Add(tabControl1, 0, 1);

            // Form (kompakt)
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(640, 420);
            Controls.Add(mainLayout);
            Controls.Add(statusStrip1);
            Controls.Add(menuStrip1);
            MainMenuStrip = menuStrip1;
            MinimumSize = new Size(560, 360);
            Name = "Form1";
            StartPosition = FormStartPosition.CenterScreen;
            Text = "SnippetMgr — szybkie wklejki";

            // Porządek z-index
            menuStrip1.BringToFront();
            statusStrip1.BringToFront();
        }

        #endregion

        // === Pola ===
        private MenuStrip menuStrip1;
        private ToolStripMenuItem opcjeToolStripMenuItem;
        private ToolStripMenuItem jSONToolStripMenuItem;
        private ToolStripMenuItem zarzadzajToolStripMenuItem;
        private ToolStripMenuItem exitToolStripMenuItem;

        private TableLayoutPanel mainLayout;
        private TableLayoutPanel searchLayout;
        private Label lblSearch;

        private TextBox textBox1;
        private TabControl tabControl1;

        private StatusStrip statusStrip1;
        private ToolStripStatusLabel toolStripStatusLabel1;
        private ToolStripStatusLabel toolStripStatusLabel2;
    }
}

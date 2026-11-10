using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace FortisRdgControl {
  public sealed class GitleaksScannerForm : Form {
    private readonly TextBox folder = new TextBox();
    private readonly RadioButton filesMode = new RadioButton();
    private readonly RadioButton gitMode = new RadioButton();
    private readonly CheckBox allRefs = new CheckBox();
    private readonly Button runButton = new Button();
    private readonly Button reportButton = new Button();
    private readonly TextBox logBox = new TextBox();
    private readonly Label status = new Label();
    private readonly TextBox reportPath = new TextBox();
    private readonly System.Windows.Forms.Timer clock = new System.Windows.Forms.Timer();
    private bool busy;
    private DateTime started;
    private int lastTick;

    public GitleaksScannerForm() {
      Text = "Gitleaks — локальная проверка";
      ClientSize = new Size(750, 520);
      MinimumSize = new Size(650, 480);
      Font = new Font("Segoe UI", 9F);
      StartPosition = FormStartPosition.CenterScreen;
      FormBorderStyle = FormBorderStyle.Sizable;
      MaximizeBox = false;
      BackColor = Color.White;
      var projectLabel = new Label { Text = "Папка проекта", Left = 18, Top = 18, Width = 190 };
      folder.SetBounds(18, 42, 590, 27);
      folder.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
      var browse = new Button { Text = "Обзор…", Left = 620, Top = 40, Width = 110, Height = 30,
        Anchor = AnchorStyles.Top | AnchorStyles.Right };
      browse.Click += delegate {
        using (var dialog = new FolderBrowserDialog { Description = "Выберите папку для локальной проверки" }) {
          if (Directory.Exists(folder.Text)) dialog.SelectedPath = folder.Text;
          if (dialog.ShowDialog(this) == DialogResult.OK) folder.Text = dialog.SelectedPath;
        }
      };

      filesMode.Text = "Текущие файлы (gitleaks dir)";
      filesMode.SetBounds(18, 90, 260, 24);
      filesMode.Checked = true;
      gitMode.Text = "Git-история (gitleaks git)";
      gitMode.SetBounds(285, 90, 270, 24);
      allRefs.Text = "Вся доступная локальная история (--all)";
      allRefs.SetBounds(285, 118, 345, 24);
      allRefs.Enabled = false;
      allRefs.Checked = true;
      gitMode.CheckedChanged += delegate { allRefs.Enabled = gitMode.Checked && !busy; };
      runButton.Text = "▶ Запустить проверку";
      runButton.SetBounds(18, 155, 220, 36);
      runButton.Click += delegate { StartScan(); };
      status.Text = "Готов к проверке";
      status.Font = new Font(Font, FontStyle.Bold);
      status.SetBounds(255, 162, 470, 27);
      status.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

      var logLabel = new Label { Text = "Ход проверки (без значений секретов)", Left = 18, Top = 205, Width = 390 };
      logBox.SetBounds(18, 228, 712, 195);
      logBox.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right | AnchorStyles.Bottom;
      logBox.Multiline = true;
      logBox.ReadOnly = true;
      logBox.ScrollBars = ScrollBars.Vertical;
      logBox.BackColor = Color.FromArgb(249, 249, 249);
      logBox.Font = new Font("Consolas", 9F);

      var pathLabel = new Label { Text = "Локальный JSON-отчёт (содержимое может быть конфиденциальным)", Left = 18, Top = 436, Width = 600,
        Anchor = AnchorStyles.Left | AnchorStyles.Bottom };
      reportPath.SetBounds(18, 458, 565, 27);
      reportPath.ReadOnly = true;
      reportPath.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
      reportButton.Text = "Открыть папку";
      reportButton.SetBounds(594, 455, 136, 31);
      reportButton.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
      reportButton.Enabled = false;
      reportButton.Click += delegate {
        try {
          var path = reportPath.Text;
          if (File.Exists(path)) {
            // Open only the containing directory, never the sensitive JSON contents.
            Process.Start("explorer.exe", GitleaksScanEngine.QuoteArg(Path.GetDirectoryName(path)));
          } else MessageBox.Show(this, "Отчёт не найден на диске.", "Gitleaks");
        } catch {
          MessageBox.Show(this, "Не удалось открыть папку отчёта.", "Gitleaks");
        }
      };
      Controls.AddRange(new Control[] { projectLabel, folder, browse, filesMode, gitMode,
        allRefs, runButton, status, logLabel, logBox, pathLabel, reportPath, reportButton });
      browse.Name = "BrowseButton";
      clock.Interval = 1000;
      clock.Tick += delegate {
        if (!busy) return;
        int elapsed = (int)(DateTime.UtcNow - started).TotalSeconds;
        if (elapsed - lastTick >= 10) {
          lastTick = elapsed;
          AppendLog("Проверка выполняется… " + elapsed + " с.");
        }
      };
      FormClosing += delegate(object sender, FormClosingEventArgs e) {
        if (busy) {
          e.Cancel = true;
          MessageBox.Show(this, "Проверка ещё выполняется. Дождитесь завершения, чтобы не оставить частичный отчёт.", "Gitleaks");
        }
      };
      AppendLog("Проверка офлайн. Программа не вызывает сеть и не изменяет выбранный проект.");
      AppendLog("В журнал не попадают stdout/stderr Gitleaks и содержимое находок.");
    }

    private void AppendLog(string message) {
      if (logBox.IsDisposed) return;
      logBox.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  " + message + Environment.NewLine);
    }

    private void StartScan() {
      if (busy) return;
      var target = folder.Text.Trim();
      var history = gitMode.Checked;
      var all = allRefs.Checked;
      busy = true;
      started = DateTime.UtcNow;
      lastTick = 0;
      runButton.Enabled = false;
      folder.Enabled = false;
      filesMode.Enabled = false;
      gitMode.Enabled = false;
      allRefs.Enabled = false;
      var browse = Controls["BrowseButton"];
      if (browse != null) browse.Enabled = false;
      reportPath.Text = "";
      reportButton.Enabled = false;
      status.ForeColor = Color.DarkSlateBlue;
      status.Text = "Сканирование...";
      AppendLog("Начата новая проверка.");
      clock.Start();
      Task.Run(() => GitleaksScanEngine.Run(target, history, all, delegate(string line) {
        if (!IsDisposed && IsHandleCreated) BeginInvoke(new Action(() => AppendLog(line)));
      })).ContinueWith(task => {
        if (IsDisposed || !IsHandleCreated) return;
        BeginInvoke(new Action(() => {
          clock.Stop();
          busy = false;
          runButton.Enabled = true;
          folder.Enabled = true;
          filesMode.Enabled = true;
          gitMode.Enabled = true;
          allRefs.Enabled = gitMode.Checked;
          if (browse != null) browse.Enabled = true;
          var r = task.Status == TaskStatus.RanToCompletion ? task.Result : null;
          if (r == null) {
            status.Text = "Ошибка сканирования";
            status.ForeColor = Color.DarkRed;
            AppendLog("ОШИБКА: внутренний сбой. Отсутствие находок не подтверждено.");
            return;
          }
          reportPath.Text = r.Report;
          reportButton.Enabled = r.Report.Length > 0;
          if (r.IsClean) {
            status.Text = "Секреты не обнаружены";
            status.ForeColor = Color.DarkGreen;
          } else if (r.HasFindings) {
            status.Text = "Найдено потенциальных секретов: " + r.Findings;
            status.ForeColor = Color.DarkOrange;
          } else {
            status.Text = "Ошибка проверки — результат не подтверждён";
            status.ForeColor = Color.DarkRed;
          }
          if (r.Report.Length > 0) AppendLog("Путь к конфиденциальному отчёту указан ниже.");
        }));
      });
    }
  }
}

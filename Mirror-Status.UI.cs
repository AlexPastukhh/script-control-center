// Windows Forms read-only Git mirror inspector for Windows PowerShell 5.1.
// Git queries run on a background thread with time limits; never fetch/pull/write.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace FortisMirror {
  internal sealed class RepoRow {
    internal string Path;
    internal bool IsGit;
    internal ListViewItem Item;
  }
  internal sealed class GitResult {
    internal bool Ok;
    internal bool Timeout;
    internal string Output;
  }
  internal sealed class CheckResult {
    internal string State = "НЕ ПРОВЕРЕН";
    internal string Local = "—";
    internal string Remote = "—";
    internal string Branch = "—";
    internal string Changes = "—";
  }
  public sealed class MirrorWindow : Form {
    private readonly string root, gitExe;
    private readonly List<RepoRow> repositories = new List<RepoRow>();
    private readonly ListView list = new ListView();
    private readonly Button checkOne = MakeButton("Проверить выбранный", 175);
    private readonly Button checkAll = MakeButton("Проверить все", 135);
    private readonly Button refresh = MakeButton("Обновить список", 150);
    private readonly Button cancel = MakeButton("Отмена проверки", 155);
    private readonly Label progress = new Label();
    private readonly Label detail = new Label();
    private CancellationTokenSource current;
    private bool running;

    public MirrorWindow(string mirrorRoot, string gitPath) {
      root = Path.GetFullPath(mirrorRoot);
      gitExe = gitPath;
      Text = "Fortis — Windows Git-зеркало";
      Size = new Size(1190, 705);
      MinimumSize = new Size(950, 520);
      StartPosition = FormStartPosition.CenterScreen;
      Font = new Font("Segoe UI", 9.5f);
      BackColor = Color.FromArgb(12, 22, 38);
      ForeColor = Color.FromArgb(230, 239, 248);

      var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(12) };
      layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 113));
      layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
      layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 96));
      Controls.Add(layout);

      var header = new Panel { Dock = DockStyle.Fill };
      var title = new Label {
        Text = "Windows-клоны  /  GitHub main", Dock = DockStyle.Top, Height = 35,
        Font = new Font("Segoe UI Semibold", 16f, FontStyle.Bold), ForeColor = Color.White
      };
      var pathLabel = new Label {
        Text = "Папка: " + root, Dock = DockStyle.Top, Height = 23,
        AutoEllipsis = true, ForeColor = Color.FromArgb(161, 183, 210)
      };
      var actions = new FlowLayoutPanel {
        Dock = DockStyle.Bottom, Height = 46, WrapContents = false,
        FlowDirection = FlowDirection.LeftToRight, AutoScroll = true
      };
      foreach (Button b in new Button[] { checkOne, checkAll, refresh, cancel }) actions.Controls.Add(b);
      cancel.Enabled = false;
      // Dock order: controls added bottom-to-top.
      header.Controls.Add(actions);
      header.Controls.Add(pathLabel);
      header.Controls.Add(title);
      layout.Controls.Add(header, 0, 0);

      list.Dock = DockStyle.Fill;
      list.View = View.Details;
      list.FullRowSelect = true;
      list.GridLines = true;
      list.HideSelection = false;
      list.MultiSelect = false;
      list.BackColor = Color.FromArgb(21, 35, 54);
      list.ForeColor = Color.FromArgb(230, 239, 248);
      list.BorderStyle = BorderStyle.FixedSingle;
      list.Columns.Add("Репозиторий", 225);
      list.Columns.Add("GitHub main", 237);
      list.Columns.Add("Локальный main", 125);
      list.Columns.Add("GitHub SHA", 125);
      list.Columns.Add("Ветка", 120);
      list.Columns.Add("Изменения", 120);
      layout.Controls.Add(list, 0, 1);

      var footer = new Panel { Dock = DockStyle.Fill };
      progress.Dock = DockStyle.Top; progress.Height = 30;
      progress.Font = new Font("Segoe UI Semibold", 10f, FontStyle.Bold);
      progress.ForeColor = Color.FromArgb(192, 215, 237);
      detail.Dock = DockStyle.Top; detail.Height = 53; detail.AutoEllipsis = true;
      detail.ForeColor = Color.FromArgb(156, 180, 207);
      footer.Controls.Add(detail);
      footer.Controls.Add(progress);
      layout.Controls.Add(footer, 0, 2);

      checkOne.Click += delegate { BeginCheck(false); };
      checkAll.Click += delegate { BeginCheck(true); };
      refresh.Click += delegate { RefreshList(); };
      cancel.Click += delegate { if (current != null) current.Cancel(); };
      list.SelectedIndexChanged += delegate { ShowSelection(); };
      list.DoubleClick += delegate { BeginCheck(false); };
      FormClosing += delegate { if (current != null) current.Cancel(); };
      Shown += delegate { RefreshList(); };
      SetButtons();
    }

    private static Button MakeButton(string text, int width) {
      return new Button {
        Text = text, Width = width, Height = 33, Margin = new Padding(0, 4, 9, 4),
        FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(39, 66, 95),
        ForeColor = Color.White, UseVisualStyleBackColor = false
      };
    }

    private void SetButtons() {
      checkOne.Enabled = !running && list.SelectedItems.Count == 1 &&
        ((RepoRow)list.SelectedItems[0].Tag).IsGit;
      checkAll.Enabled = !running && repositories.Exists(delegate(RepoRow r) { return r.IsGit; });
      refresh.Enabled = !running;
      cancel.Enabled = running;
    }

    private void ShowSelection() {
      if (list.SelectedItems.Count == 1) {
        RepoRow selected = (RepoRow)list.SelectedItems[0].Tag;
        detail.Text = selected.Path + Environment.NewLine +
          "Сверяются SHA ветки main; при расхождении НЕ предполагается автоматически, что допустим pull.";
      } else {
        detail.Text = "Выберите репозиторий и нажмите «Проверить выбранный», либо нажмите «Проверить все».";
      }
      SetButtons();
    }

    private void RefreshList() {
      if (running) return;
      repositories.Clear();
      list.BeginUpdate();
      try {
        list.Items.Clear();
        if (!Directory.Exists(root)) {
          progress.Text = "Зеркальная папка не найдена: " + root;
          detail.Text = "Измените -MirrorRoot при запуске скрипта, если зеркало находится в другом месте.";
          return;
        }
        string[] folders = Directory.GetDirectories(root);
        Array.Sort(folders, StringComparer.OrdinalIgnoreCase);
        foreach (string folder in folders) {
          DirectoryInfo info = new DirectoryInfo(folder);
          if ((info.Attributes & FileAttributes.ReparsePoint) != 0) continue;
          bool git = Directory.Exists(Path.Combine(folder, ".git")) || File.Exists(Path.Combine(folder, ".git"));
          var item = new ListViewItem(info.Name);
          item.SubItems.Add(git ? "НЕ ПРОВЕРЕН" : "НЕ GIT-КЛОН");
          item.SubItems.Add("—"); item.SubItems.Add("—");
          item.SubItems.Add("—"); item.SubItems.Add("—");
          if (!git) item.ForeColor = Color.FromArgb(131, 153, 176);
          var row = new RepoRow { Path = folder, IsGit = git, Item = item };
          item.Tag = row;
          list.Items.Add(item);
          repositories.Add(row);
        }
        int count = repositories.FindAll(delegate(RepoRow r) { return r.IsGit; }).Count;
        progress.Text = "Найдено Git-клонов: " + count + "  |  Всего папок: " + repositories.Count +
          "  |  Сетевая проверка не запускалась.";
        detail.Text = "Папки показаны сразу. Проверка обращается только к GitHub origin, не изменяя Git-клоны.";
      } catch (Exception ex) {
        progress.Text = "Ошибка чтения зеркальной папки.";
        detail.Text = ex.Message;
      } finally {
        list.EndUpdate();
        SetButtons();
      }
    }

    private void BeginCheck(bool all) {
      if (running) return;
      var targets = new List<RepoRow>();
      if (all) {
        foreach (RepoRow row in repositories) if (row.IsGit) targets.Add(row);
      } else if (list.SelectedItems.Count == 1) {
        RepoRow row = (RepoRow)list.SelectedItems[0].Tag;
        if (row.IsGit) targets.Add(row);
      }
      if (targets.Count == 0) return;
      running = true;
      current = new CancellationTokenSource();
      CancellationToken token = current.Token;
      SetButtons();
      progress.Text = "Проверка: 0 / " + targets.Count;
      foreach (RepoRow row in targets) {
        row.Item.SubItems[1].Text = "В ОЧЕРЕДИ";
        row.Item.ForeColor = Color.FromArgb(230, 239, 248);
      }
      ThreadPool.QueueUserWorkItem(delegate {
        int done = 0;
        foreach (RepoRow row in targets) {
          if (token.IsCancellationRequested) break;
          Ui(delegate {
            row.Item.SubItems[1].Text = "ПРОВЕРЯЕТСЯ...";
            progress.Text = "Проверяется " + row.Item.Text + "  (" + (done + 1) + "/" + targets.Count + ")";
          });
          CheckResult result = CheckRepository(row.Path, token);
          if (token.IsCancellationRequested) break;
          done++;
          int finished = done;
          Ui(delegate {
            row.Item.SubItems[1].Text = result.State;
            row.Item.SubItems[2].Text = result.Local;
            row.Item.SubItems[3].Text = result.Remote;
            row.Item.SubItems[4].Text = result.Branch;
            row.Item.SubItems[5].Text = result.Changes;
            row.Item.ForeColor = result.State == "АКТУАЛЕН" && result.Branch == "main" && result.Changes == "Нет" ?
              Color.FromArgb(116, 215, 169) : Color.FromArgb(255, 195, 117);
            progress.Text = "Проверено: " + finished + " / " + targets.Count;
          });
        }
        bool cancelled = token.IsCancellationRequested;
        Ui(delegate {
          foreach (RepoRow row in targets)
            if (row.Item.SubItems[1].Text == "В ОЧЕРЕДИ" ||
                row.Item.SubItems[1].Text == "ПРОВЕРЯЕТСЯ...") row.Item.SubItems[1].Text = "НЕ ПРОВЕРЕН";
          running = false;
          int currentCount = 0, reviewCount = 0;
          foreach (RepoRow r in repositories) {
            if (!r.IsGit) continue;
            if (r.Item.SubItems[1].Text == "АКТУАЛЕН" && r.Item.SubItems[4].Text == "main" &&
                r.Item.SubItems[5].Text == "Нет") currentCount++;
            else if (r.Item.SubItems[1].Text != "НЕ ПРОВЕРЕН") reviewCount++;
          }
          progress.Text = (cancelled ? "Отменено; " : "Готово; ") +
            "проверено за запуск: " + done + "/" + targets.Count +
            "; полностью актуальны: " + currentCount + "; требуют внимания: " + reviewCount + ".";
          SetButtons();
        });
      });
    }

    private void Ui(Action action) {
      try {
        if (!IsDisposed && IsHandleCreated) BeginInvoke((MethodInvoker)delegate { if (!IsDisposed) action(); });
      } catch (InvalidOperationException) { }
    }
    private CheckResult CheckRepository(string path, CancellationToken token) {
      var data = new CheckResult();
      GitResult local = GitRead(path, "rev-parse --verify refs/heads/main^{commit}", 5000, token);
      GitResult branch = GitRead(path, "symbolic-ref --quiet --short HEAD", 5000, token);
      GitResult dirty = GitRead(path, "status --porcelain=v1 --untracked-files=normal --ignore-submodules=all", 9000, token);
      if (local.Ok && Regex.IsMatch(local.Output, @"^[0-9a-fA-F]{40}$")) data.Local = local.Output.Substring(0, 8);
      if (branch.Ok && branch.Output.Length > 0) data.Branch = branch.Output;
      else data.Branch = "(detached)";
      if (dirty.Ok) data.Changes = dirty.Output.Length == 0 ? "Нет" : "Да";
      else data.Changes = "?";
      if (token.IsCancellationRequested) return data;
      if (local.Timeout || branch.Timeout || dirty.Timeout) {
        data.State = "ЛОКАЛЬНЫЙ ТАЙМАУТ";
        return data;
      }
      GitResult origin = GitRead(path, "remote get-url origin", 5000, token);
      if (!origin.Ok) { data.State = "НЕТ ORIGIN"; return data; }
      if (!AllowedOrigin(origin.Output)) { data.State = "ДРУГОЙ ORIGIN"; return data; }
      GitResult remote = GitRead(path, "ls-remote --exit-code origin refs/heads/main", 12000, token);
      if (!remote.Ok) {
        data.State = remote.Timeout ? "ТАЙМАУТ GITHUB" : "GITHUB НЕДОСТУПЕН";
        return data;
      }
      Match m = Regex.Match(remote.Output, @"(?m)^([0-9a-fA-F]{40})\s+refs/heads/main\s*$");
      if (!m.Success) { data.State = "НЕТ GITHUB MAIN"; return data; }
      string remoteHash = m.Groups[1].Value.ToLowerInvariant();
      data.Remote = remoteHash.Substring(0, 8);
      if (!local.Ok) data.State = "НЕТ ЛОКАЛЬНОГО MAIN";
      else data.State = String.Equals(local.Output, remoteHash, StringComparison.OrdinalIgnoreCase) ?
        "АКТУАЛЕН" : "РАЗЛИЧАЮТСЯ";
      // A different SHA does not mean that git pull --ff-only will succeed.
      return data;
    }

    private static bool AllowedOrigin(string url) {
      return Regex.IsMatch(url,
        @"^(?:https://github\.com/|git@github\.com:)AlexPastukhh/[a-zA-Z0-9_.-]+(?:\.git)?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private GitResult GitRead(string repo, string command, int timeoutMs, CancellationToken token) {
      var result = new GitResult { Output = "" };
      if (token.IsCancellationRequested) return result;
      try {
        using (var proc = new Process()) {
          var psi = proc.StartInfo;
          psi.FileName = gitExe;
          // No shell involved; command is selected from fixed read-only Git verbs.
          psi.Arguments = "-c core.fsmonitor=false -C " + Quote(repo) + " " + command;
          psi.UseShellExecute = false;
          psi.CreateNoWindow = true;
          psi.RedirectStandardOutput = true;
          psi.RedirectStandardError = true;
          psi.EnvironmentVariables["GIT_TERMINAL_PROMPT"] = "0";
          psi.EnvironmentVariables["GCM_INTERACTIVE"] = "never";
          psi.EnvironmentVariables["GIT_OPTIONAL_LOCKS"] = "0";
          psi.EnvironmentVariables["GIT_SSH_COMMAND"] = "ssh -o BatchMode=yes -o ConnectTimeout=8";
          var output = new StringBuilder();
          object gate = new object();
          proc.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e) {
            if (e.Data != null) lock (gate) {
              if (output.Length < 8192) output.AppendLine(e.Data);
            }
          };
          // Drain stderr to avoid blocking, but never show raw credential-manager errors.
          proc.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e) { };
          proc.Start();
          proc.BeginOutputReadLine();
          proc.BeginErrorReadLine();
          var sw = Stopwatch.StartNew();
          bool stopped = false;
          while (!proc.WaitForExit(100)) {
            if (token.IsCancellationRequested || sw.ElapsedMilliseconds >= timeoutMs) {
              stopped = true;
              result.Timeout = !token.IsCancellationRequested;
              try { proc.Kill(); } catch (InvalidOperationException) { }
              catch (System.ComponentModel.Win32Exception) { }
              proc.WaitForExit(2000);
              break;
            }
          }
          if (!stopped) {
            proc.WaitForExit();
            result.Ok = proc.ExitCode == 0;
          }
          lock (gate) { result.Output = output.ToString().Trim(); }
        }
      } catch (Exception) {
        result.Ok = false;
      }
      return result;
    }

    private static string Quote(string path) {
      return "\"" + path.Replace("\"", "\\\"") + "\"";
    }

    public static void Open(string root, string git) {
      Exception error = null;
      Thread window = new Thread(delegate() {
        try {
          Application.EnableVisualStyles();
          Application.SetCompatibleTextRenderingDefault(false);
          Application.Run(new MirrorWindow(root, git));
        } catch (Exception ex) { error = ex; }
      });
      window.SetApartmentState(ApartmentState.STA);
      window.Start();
      window.Join();
      if (error != null) throw new InvalidOperationException("Ошибка окна зеркала", error);
    }
  }
}

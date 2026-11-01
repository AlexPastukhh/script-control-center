// Windows Forms Git mirror inspector for Windows PowerShell 5.1.
// Inspection never updates clones; selected manual pull is guarded and fast-forward-only.
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
    internal CheckResult Last;
  }
  internal sealed class GitResult {
    internal bool Ok;
    internal bool Timeout;
    internal int ExitCode = -1;
    internal string Failure = "";
    internal string Output;
  }
  internal sealed class CheckResult {
    internal string State = "НЕ ПРОВЕРЕН";
    internal string Local = "—";
    internal string Remote = "—";
    internal string LocalFull = "";
    internal string RemoteFull = "";
    internal bool UseOpenSsl;
    internal string Commit = "Сведения о коммите не получены.";
    internal string Branch = "—";
    internal string Changes = "—";
    internal string Behind = "—";
    internal int BehindCount = -1;
    internal int AheadCount = -1;
    internal bool CanPull {
      get {
        return State == "ОТСТАЁТ" && BehindCount > 0 && AheadCount == 0 &&
          Branch == "main" && Changes == "Нет" &&
          Regex.IsMatch(LocalFull, @"^[0-9a-fA-F]{40}$") &&
          Regex.IsMatch(RemoteFull, @"^[0-9a-fA-F]{40}$");
      }
    }
  }
  public sealed class MirrorWindow : Form {
    private readonly string root, gitExe;
    private readonly List<RepoRow> repositories = new List<RepoRow>();
    private readonly ListView list = new ListView();
    private readonly Button checkSelected = MakeButton("Проверить выбранные", 195);
    private readonly Button checkAll = MakeButton("Проверить все", 135);
    private readonly Button pullOne = MakeButton("Pull выбранного", 160);
    private readonly Button refresh = MakeButton("Обновить список", 150);
    private readonly Button cancel = MakeButton("Отмена проверки", 155);
    private readonly Label progress = new Label();
    private readonly TextBox detail = new TextBox();
    private CancellationTokenSource current;
    private bool running;
    private bool pulling;

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
      layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 155));
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
      foreach (Button b in new Button[] { checkSelected, checkAll, pullOne, refresh, cancel }) actions.Controls.Add(b);
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
      list.MultiSelect = true;
      list.BackColor = Color.FromArgb(21, 35, 54);
      list.ForeColor = Color.FromArgb(230, 239, 248);
      list.BorderStyle = BorderStyle.FixedSingle;
      list.Columns.Add("Репозиторий", 190);
      list.Columns.Add("GitHub main", 185);
      list.Columns.Add("Локальный main", 105);
      list.Columns.Add("GitHub SHA", 105);
      list.Columns.Add("Ветка", 100);
      list.Columns.Add("Изменения", 95);
      list.Columns.Add("Отстаёт на", 98);
      list.Columns.Add("Последний локальный коммит", 310);
      layout.Controls.Add(list, 0, 1);

      var footer = new Panel { Dock = DockStyle.Fill };
      progress.Dock = DockStyle.Top; progress.Height = 30;
      progress.Font = new Font("Segoe UI Semibold", 10f, FontStyle.Bold);
      progress.ForeColor = Color.FromArgb(192, 215, 237);
      detail.Dock = DockStyle.Fill;
      detail.Multiline = true; detail.ReadOnly = true; detail.ScrollBars = ScrollBars.Vertical;
      detail.BorderStyle = BorderStyle.None;
      detail.BackColor = Color.FromArgb(12, 22, 38);
      detail.ForeColor = Color.FromArgb(156, 180, 207);
      footer.Controls.Add(detail);
      footer.Controls.Add(progress);
      layout.Controls.Add(footer, 0, 2);

      checkSelected.Click += delegate { BeginCheck(false); };
      checkAll.Click += delegate { BeginCheck(true); };
      pullOne.Click += delegate { BeginPull(); };
      refresh.Click += delegate { RefreshList(); };
      cancel.Click += delegate { if (current != null) current.Cancel(); };
      list.SelectedIndexChanged += delegate { ShowSelection(); };
      list.DoubleClick += delegate { BeginCheck(false); };
      FormClosing += delegate(object sender, FormClosingEventArgs e) {
        if (pulling) {
          e.Cancel = true;
          MessageBox.Show(this, "Git pull ещё выполняется. Дождитесь результата; прерывать обновление файлов опасно.",
            "Идёт обновление", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        } else if (current != null) current.Cancel();
      };
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
      bool hasSelectedGit = false;
      foreach (ListViewItem item in list.SelectedItems) {
        if (((RepoRow)item.Tag).IsGit) { hasSelectedGit = true; break; }
      }
      checkSelected.Enabled = !running && hasSelectedGit;
      checkAll.Enabled = !running && repositories.Exists(delegate(RepoRow r) { return r.IsGit; });
      pullOne.Enabled = !running && list.SelectedItems.Count == 1 &&
        ((RepoRow)list.SelectedItems[0].Tag).Last != null &&
        ((RepoRow)list.SelectedItems[0].Tag).Last.CanPull;
      refresh.Enabled = !running;
      cancel.Enabled = running && !pulling;
    }

    private void ShowSelection() {
      if (list.SelectedItems.Count == 1) {
        RepoRow selected = (RepoRow)list.SelectedItems[0].Tag;
        if (selected.Last == null) {
          detail.Text = selected.Path + Environment.NewLine +
            "Коммиты не проверены. Проверка может загрузить историю в .git, но не изменяет рабочие файлы.";
        } else {
          CheckResult v = selected.Last;
          detail.Text = selected.Path + Environment.NewLine +
            "Локальный main: " + (v.LocalFull.Length > 0 ? v.LocalFull : "неизвестен") + Environment.NewLine +
            "Последний локальный коммит: " + v.Commit + Environment.NewLine +
            "GitHub main: " + (v.RemoteFull.Length > 0 ? v.RemoteFull : "неизвестен") + Environment.NewLine +
            "Коммитов позади: " + v.Behind +
            (v.AheadCount > 0 ? "  |  Локальных коммитов сверх общего предка: " + v.AheadCount : "") +
            (v.CanPull ? "   |   Pull доступен с подтверждением" : "   |   Pull заблокирован");
        }
      } else if (list.SelectedItems.Count > 1) {
        int gitCount = 0;
        foreach (ListViewItem item in list.SelectedItems)
          if (((RepoRow)item.Tag).IsGit) gitCount++;
        detail.Text = "Выбрано папок: " + list.SelectedItems.Count +
          " | Git-клонов для проверки: " + gitCount + Environment.NewLine +
          "Нажмите «Проверить выбранные». Для выбора нескольких используйте Ctrl или Shift." +
          Environment.NewLine + "Pull доступен только при выборе одного проверенного репозитория.";
      } else {
        detail.Text = "Выберите клоны (Ctrl/Shift) и нажмите «Проверить выбранные», либо «Проверить все».";
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
        detail.Text = "Проверка сравнивает main с GitHub и при необходимости загружает историю Git в .git; рабочие файлы не меняет.";
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
      } else {
        // Snapshot only the selected Git rows. Non-Git directories are skipped.
        // The check can run for one or many clones; Pull still requires exactly one.
        foreach (ListViewItem item in list.SelectedItems) {
          RepoRow row = (RepoRow)item.Tag;
          if (row.IsGit) targets.Add(row);
        }
      }
      if (targets.Count == 0) return;
      running = true;
      current = new CancellationTokenSource();
      CancellationToken token = current.Token;
      SetButtons();
      progress.Text = "Проверка: 0 / " + targets.Count;
      foreach (RepoRow row in targets) {
        row.Last = null;
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
            row.Last = result;
            row.Item.SubItems[1].Text = result.State;
            row.Item.SubItems[2].Text = result.Local;
            row.Item.SubItems[3].Text = result.Remote;
            row.Item.SubItems[4].Text = result.Branch;
            row.Item.SubItems[5].Text = result.Changes;
            row.Item.SubItems[6].Text = result.Behind;
            row.Item.SubItems[7].Text = result.Commit;
            row.Item.ForeColor = result.State == "АКТУАЛЕН" && result.Branch == "main" && result.Changes == "Нет" ?
              Color.FromArgb(116, 215, 169) : Color.FromArgb(255, 195, 117);
            progress.Text = "Проверено: " + finished + " / " + targets.Count;
            ShowSelection();
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

    private void BeginPull() {
      if (running || list.SelectedItems.Count != 1) return;
      RepoRow row = (RepoRow)list.SelectedItems[0].Tag;
      CheckResult expected = row.Last;
      if (!row.IsGit || expected == null || !expected.CanPull) return;
      string ask = "Обновить только этот Windows-клон?\n\n" +
        row.Path + "\n\n" +
        "Локальный main: " + expected.LocalFull + "\n" +
        "GitHub main: " + expected.RemoteFull + "\n\n" +
        "Перед pull приложение повторно проверит ветку, origin, SHA и чистоту файлов.\n" +
        "Команда: git pull --ff-only origin main.\n" +
        "При расхождении истории Git откажет; локальные правки не удаляются.\n\n" +
        "Git может выполнить настроенные фильтры файлов. Запущенные Fortis-процессы\n" +
        "не остановятся, но исходники на диске изменятся. Для запуска/Guard нужна отдельная проверка. Продолжить?";
      if (MessageBox.Show(this, ask, "Подтвердить обновление одного клона",
        MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
        MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
      running = true;
      pulling = true;
      current = new CancellationTokenSource();
      progress.Text = "Повторная проверка " + row.Item.Text + " перед обновлением...";
      SetButtons();
      ThreadPool.QueueUserWorkItem(delegate {
        GitResult pull = null;
        CheckResult after = null;
        string blocked = null;
        try {
          // Guard against a stale button or a modified repo since last inspection.
          CheckResult fresh = CheckRepository(row.Path, CancellationToken.None);
          if (!fresh.CanPull || fresh.LocalFull != expected.LocalFull ||
              fresh.RemoteFull != expected.RemoteFull ||
              fresh.Branch != expected.Branch || fresh.Changes != "Нет" ||
              fresh.UseOpenSsl != expected.UseOpenSsl)
            blocked = "Состояние репозитория изменилось либо проверка не прошла. Обновление отменено.";
          else {
            Ui(delegate { progress.Text = "Выполняется git pull --ff-only: " + row.Item.Text; });
            // Intentionally no forced cancellation during a write to the working tree.
            pull = GitPullFastForward(row.Path, fresh.UseOpenSsl);
          }
          after = CheckRepository(row.Path, CancellationToken.None);
        } catch (Exception) {
          blocked = "Неожиданная ошибка проверяльщика. Git-статус следует проверить вручную.";
        }
        GitResult result = pull;
        CheckResult checkedState = after;
        string reason = blocked;
        Ui(delegate {
          if (checkedState != null) {
            row.Last = checkedState;
            row.Item.SubItems[1].Text = checkedState.State;
            row.Item.SubItems[2].Text = checkedState.Local;
            row.Item.SubItems[3].Text = checkedState.Remote;
            row.Item.SubItems[4].Text = checkedState.Branch;
            row.Item.SubItems[5].Text = checkedState.Changes;
            row.Item.SubItems[6].Text = checkedState.Behind;
            row.Item.SubItems[7].Text = checkedState.Commit;
            row.Item.ForeColor = checkedState.State == "АКТУАЛЕН" &&
              checkedState.Branch == "main" && checkedState.Changes == "Нет" ?
              Color.FromArgb(116, 215, 169) : Color.FromArgb(255, 195, 117);
          }
          running = false;
          pulling = false;
          ShowSelection();
          bool ok = reason == null && result != null && result.Ok;
          string message = reason != null ? reason :
            result == null ? "Обновление не выполнялось." :
            result.Ok ? "Fast-forward pull завершился успешно." :
            "Git pull не завершился: " +
              (result.Failure.Length > 0 ? result.Failure : "проверьте историю, доступ и рабочее дерево.");
          progress.Text = message;
          if (ok && checkedState != null)
            message += "\n\nНовый локальный main: " + checkedState.LocalFull +
              "\nПоследний коммит: " + checkedState.Commit;
          if (ok && checkedState != null && checkedState.State != "АКТУАЛЕН")
            message += "\n\nВнимание: итоговая сетевая проверка не подтверждает совпадение SHA.";
          MessageBox.Show(this, message, ok ? "Обновление завершено" : "Обновление не выполнено",
            MessageBoxButtons.OK, ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        });
      });
    }

    // The ONLY Git write operation in this application. Owner-triggered for one clean,
    // verified main checkout. No forced timeout/kill during worktree updates.
    private GitResult GitPullFastForward(string repo, bool openSsl) {
      var result = new GitResult { Output = "" };
      try {
        using (var proc = new Process()) {
          var psi = proc.StartInfo;
          psi.FileName = gitExe;
          psi.Arguments = (openSsl ? "-c http.sslBackend=openssl -c http.sslVerify=true " : "") +
            "-c core.fsmonitor=false -c core.hooksPath=/dev/null -c submodule.recurse=false " +
            "-C " + Quote(repo) + " pull --ff-only --no-rebase --no-recurse-submodules origin main";
          psi.UseShellExecute = false;
          psi.CreateNoWindow = true;
          psi.RedirectStandardOutput = true;
          psi.RedirectStandardError = true;
          psi.EnvironmentVariables["GIT_TERMINAL_PROMPT"] = "0";
          psi.EnvironmentVariables["GCM_INTERACTIVE"] = "never";
          psi.EnvironmentVariables["GIT_SSH_COMMAND"] = "ssh -o BatchMode=yes -o ConnectTimeout=8";
          // A write needs Git locks even if the parent process configured read-only Git.
          psi.EnvironmentVariables["GIT_OPTIONAL_LOCKS"] = "1";
          var errors = new StringBuilder();
          object gate = new object();
          proc.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e) { };
          proc.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e) {
            if (e.Data != null) lock (gate) {
              if (errors.Length < 4096) errors.AppendLine(e.Data);
            }
          };
          proc.Start();
          proc.BeginOutputReadLine();
          proc.BeginErrorReadLine();
          proc.WaitForExit();
          result.ExitCode = proc.ExitCode;
          result.Ok = proc.ExitCode == 0;
          lock (gate) result.Failure = ClassifyGitError(errors.ToString());
        }
      } catch (Exception) {
        result.Ok = false;
      }
      return result;
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
      GitResult dirty = GitRead(path, "status --porcelain=v1 --untracked-files=all", 9000, token);
      GitResult log = GitRead(path, "log -1 --format=%h%x09%as%x09%s refs/heads/main", 5000, token);
      if (local.Ok && Regex.IsMatch(local.Output, @"^[0-9a-fA-F]{40}$")) {
        data.LocalFull = local.Output.ToLowerInvariant();
        data.Local = data.LocalFull.Substring(0, 8);
      }
      if (log.Ok && log.Output.Length > 0) data.Commit = log.Output;
      if (branch.Ok && branch.Output.Length > 0) data.Branch = branch.Output;
      else data.Branch = "(detached)";
      if (dirty.Ok) data.Changes = dirty.Output.Length == 0 ? "Нет" : "Да";
      else data.Changes = dirty.Timeout ? "Таймаут" : "?";
      if (token.IsCancellationRequested) return data;
      // A slow worktree status must not hide the main-SHA comparison.
      if (local.Timeout || branch.Timeout) {
        data.State = "ЛОКАЛЬНЫЙ ТАЙМАУТ";
        return data;
      }
      GitResult origin = GitRead(path, "remote get-url origin", 5000, token);
      if (!origin.Ok) { data.State = "НЕТ ORIGIN"; return data; }
      if (!AllowedOrigin(origin.Output)) { data.State = "ДРУГОЙ ORIGIN"; return data; }
      // Override the broken Windows Schannel backend for HTTPS GitHub queries only.
      // This does not change local/global Git configuration or bypass certificate checks.
      bool https = origin.Output.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase);
      data.UseOpenSsl = https;
      GitResult remote = GitRead(path, "ls-remote --exit-code origin refs/heads/main", 12000, token, https);
      if (!remote.Ok) {
        data.State = remote.Timeout ? "ТАЙМАУТ GITHUB" :
          remote.ExitCode == 2 ? "НЕТ GITHUB MAIN" :
          remote.Failure.Length > 0 ? remote.Failure : "GITHUB НЕДОСТУПЕН";
        return data;
      }
      Match m = Regex.Match(remote.Output, @"(?m)^([0-9a-fA-F]{40})\s+refs/heads/main\s*$");
      if (!m.Success) { data.State = "НЕТ GITHUB MAIN"; return data; }
      string remoteHash = m.Groups[1].Value.ToLowerInvariant();
      data.RemoteFull = remoteHash;
      data.Remote = remoteHash.Substring(0, 8);
      if (!local.Ok) { data.State = "НЕТ ЛОКАЛЬНОГО MAIN"; return data; }
      if (String.Equals(local.Output, remoteHash, StringComparison.OrdinalIgnoreCase)) {
        data.State = "АКТУАЛЕН";
        data.BehindCount = 0;
        data.AheadCount = 0;
        data.Behind = "0";
        return data;
      }
      // Shallow history can undercount commits. Never display a false exact number.
      GitResult shallow = GitRead(path, "rev-parse --is-shallow-repository", 5000, token);
      if (!shallow.Ok || shallow.Output != "false") {
        data.State = shallow.Ok ? "НЕПОЛНАЯ ИСТОРИЯ" : "ИСТОРИЯ НЕИЗВЕСТНА";
        data.Behind = "?";
        return data;
      }
      // ls-remote reveals only a SHA, not commit history. Try locally cached objects;
      // if missing, a user-triggered Check may fetch Git METADATA into this clone.
      // The working tree, branch checkout and running programs are not updated.
      GitResult counts = GitRead(path, "rev-list --left-right --count " +
        data.LocalFull + "..." + data.RemoteFull, 8000, token);
      if ((!counts.Ok || !TryCounts(counts.Output, data)) && !token.IsCancellationRequested) {
        GitResult fetched = GitFetchMain(path, https);
        if (fetched.Ok) {
          // GitHub main may have moved while fetching. Do not claim any count based
          // on an older SHA unless that exact SHA is present in the object database.
          counts = GitRead(path, "rev-list --left-right --count " +
            data.LocalFull + "..." + data.RemoteFull, 8000, token);
          if (counts.Ok) TryCounts(counts.Output, data);
        }
      }
      if (data.BehindCount < 0) {
        data.Behind = "?";
        data.State = "ИСТОРИЯ НЕИЗВЕСТНА";
      } else if (data.AheadCount > 0 && data.BehindCount > 0) {
        data.State = "ИСТОРИИ РАЗОШЛИСЬ";
      } else if (data.AheadCount > 0) {
        data.State = "ЛОКАЛЬНО ВПЕРЕДИ";
      } else if (data.BehindCount > 0) {
        data.State = "ОТСТАЁТ";
      } else {
        data.State = "ИСТОРИЯ НЕИЗВЕСТНА";
      }
      return data;
    }

    private static bool TryCounts(string text, CheckResult data) {
      Match match = Regex.Match(text, @"^\s*(\d+)\s+(\d+)\s*$");
      int ahead, behind;
      if (!match.Success || !Int32.TryParse(match.Groups[1].Value, out ahead) ||
          !Int32.TryParse(match.Groups[2].Value, out behind)) return false;
      data.AheadCount = ahead;
      data.BehindCount = behind;
      data.Behind = behind.ToString();
      return true;
    }

    // Fetch updates ONLY Git metadata (objects/FETCH_HEAD and possibly origin/main).
    // It is performed after an explicit Check, not on window opening.
    // No working-tree files, local main branch or running services are touched.
    private GitResult GitFetchMain(string repo, bool openSsl) {
      var result = new GitResult { Output = "" };
      try {
        using (var proc = new Process()) {
          var psi = proc.StartInfo;
          psi.FileName = gitExe;
          psi.Arguments = (openSsl ?
            "-c http.sslBackend=openssl -c http.sslVerify=true " : "") +
            "-c core.fsmonitor=false -c submodule.recurse=false " +
            "-C " + Quote(repo) + " fetch --no-tags --no-recurse-submodules origin main";
          psi.UseShellExecute = false;
          psi.CreateNoWindow = true;
          psi.RedirectStandardOutput = true;
          psi.RedirectStandardError = true;
          psi.EnvironmentVariables["GIT_TERMINAL_PROMPT"] = "0";
          psi.EnvironmentVariables["GCM_INTERACTIVE"] = "never";
          psi.EnvironmentVariables["GIT_OPTIONAL_LOCKS"] = "1";
          psi.EnvironmentVariables["GIT_SSH_COMMAND"] = "ssh -o BatchMode=yes -o ConnectTimeout=8";
          var errors = new StringBuilder();
          object gate = new object();
          proc.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e) { };
          proc.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e) {
            if (e.Data != null) lock (gate) {
              if (errors.Length < 4096) errors.AppendLine(e.Data);
            }
          };
          proc.Start();
          proc.BeginOutputReadLine();
          proc.BeginErrorReadLine();
          // Bound the metadata fetch, unlike the owner-approved explicit pull.
          bool completed = proc.WaitForExit(40000);
          if (!completed) {
            result.Timeout = true;
            try { proc.Kill(); } catch (Exception) { }
            proc.WaitForExit(2500);
          } else {
            proc.WaitForExit();
            result.ExitCode = proc.ExitCode;
            result.Ok = proc.ExitCode == 0;
          }
          lock (gate) result.Failure = ClassifyGitError(errors.ToString());
        }
      } catch (Exception) { result.Ok = false; }
      return result;
    }

    private static bool AllowedOrigin(string url) {
      return Regex.IsMatch(url,
        @"^(?:https://github\.com/|git@github\.com:)AlexPastukhh/[a-zA-Z0-9_.-]+(?:\.git)?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private GitResult GitRead(string repo, string command, int timeoutMs, CancellationToken token,
      bool forceOpenSsl = false) {
      var result = new GitResult { Output = "" };
      if (token.IsCancellationRequested) return result;
      try {
        using (var proc = new Process()) {
          var psi = proc.StartInfo;
          psi.FileName = gitExe;
          // No shell involved; command is selected from fixed read-only Git verbs.
          psi.Arguments = (forceOpenSsl ?
            "-c http.sslBackend=openssl -c http.sslVerify=true " : "") +
            "-c core.fsmonitor=false -C " + Quote(repo) + " " + command;
          psi.UseShellExecute = false;
          psi.CreateNoWindow = true;
          psi.RedirectStandardOutput = true;
          psi.RedirectStandardError = true;
          psi.EnvironmentVariables["GIT_TERMINAL_PROMPT"] = "0";
          psi.EnvironmentVariables["GCM_INTERACTIVE"] = "never";
          psi.EnvironmentVariables["GIT_OPTIONAL_LOCKS"] = "0";
          psi.EnvironmentVariables["GIT_SSH_COMMAND"] = "ssh -o BatchMode=yes -o ConnectTimeout=8";
          var output = new StringBuilder();
          var errors = new StringBuilder();
          object gate = new object();
          proc.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e) {
            if (e.Data != null) lock (gate) {
              if (output.Length < 8192) output.AppendLine(e.Data);
            }
          };
          // Drain stderr; classify locally but never display raw credential/URL errors.
          proc.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e) {
            if (e.Data != null) lock (gate) {
              if (errors.Length < 4096) errors.AppendLine(e.Data);
            }
          };
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
            result.ExitCode = proc.ExitCode;
            result.Ok = proc.ExitCode == 0;
          }
          lock (gate) {
            result.Output = output.ToString().Trim();
            result.Failure = ClassifyGitError(errors.ToString());
          }
        }
      } catch (Exception) {
        result.Ok = false;
      }
      return result;
    }

    private static string ClassifyGitError(string message) {
      if (Regex.IsMatch(message, @"(?i)(not possible to fast-forward|fatal: divergent branches|fatal: need to specify how to reconcile)"))
        return "ИСТОРИИ РАЗОШЛИСЬ: нужен ручной разбор";
      if (Regex.IsMatch(message, @"(?i)(your local changes|would be overwritten by merge|untracked working tree files would be overwritten)"))
        return "ОБНАРУЖЕНЫ ЛОКАЛЬНЫЕ ИЗМЕНЕНИЯ";
      if (Regex.IsMatch(message, @"(?i)(index\.lock|another git process|you have not concluded your merge)"))
        return "GIT ЗАНЯТ ИЛИ ЕСТЬ НЕЗАКОНЧЕННАЯ ОПЕРАЦИЯ";
      if (Regex.IsMatch(message, @"(?i)(schannel|AcquireCredentialsHandle|SEC_E_INTERNAL_ERROR|ssl certificate|SSL_connect|TLS handshake|unsupported SSL backend|SSL backend)"))
        return "ОШИБКА TLS";
      if (Regex.IsMatch(message, @"(?i)(authentication failed|permission denied|repository not found|could not read Username|HTTP 401|HTTP 403|not authorized)"))
        return "ОШИБКА ДОСТУПА";
      if (Regex.IsMatch(message, @"(?i)(could not resolve host|name or service not known|name resolution|no such host)"))
        return "ОШИБКА DNS";
      if (Regex.IsMatch(message, @"(?i)(failed to connect|network is unreachable|connection refused|connection timed out)"))
        return "ОШИБКА СЕТИ";
      return "";
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

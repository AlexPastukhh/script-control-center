// Fortis Docker -> GitHub -> Windows workflow inspector (Windows PowerShell 5.1).
// Real snapshot data, no simulated repo states. Branch VIEW never performs checkout.
// Writes: explicit single-repo owner-confirmed Windows Pull or guarded branch switch.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace FortisMirror {
  internal sealed class WfCommand {
    internal bool Ok;
    internal bool Timeout;
    internal string Text = "";
    internal string Error = "";
  }
  internal sealed class WfBranch {
    internal string Name = "";
    internal string DockerSha = "";
    internal string GithubSha = "";
    internal string WindowsSha = "";
    internal int DockerAhead = -1, DockerBehind = -1;
    internal int WindowsAhead = -1, WindowsBehind = -1;
    internal string LastCommit = "";
  }
  internal sealed class WfRepo {
    internal string Name = "";
    internal string DockerOrigin = "";
    internal string DockerActive = "";
    internal string DockerHead = "";
    internal int DockerChanges = -1;
    internal string DockerView = "";
    internal string WindowsView = "";
    internal string WindowsPath = "";
    internal string WindowsActive = "";
    internal string WindowsHead = "";
    internal int WindowsChanges = -1;
    internal bool WindowsExists;
    internal bool WindowsValid;
    internal bool WindowsOriginValid;
    internal bool CheckedGithub;
    internal bool CheckedWindows;
    internal string Problem = "";
    internal ListViewItem Item;
    internal readonly Dictionary<string, WfBranch> Branches =
      new Dictionary<string, WfBranch>(StringComparer.Ordinal);
    internal WfBranch Get(string name) {
      WfBranch b;
      return name != null && Branches.TryGetValue(name, out b) ? b : null;
    }
    internal WfBranch Ensure(string name) {
      WfBranch b;
      if (!Branches.TryGetValue(name, out b)) {
        b = new WfBranch { Name = name }; Branches.Add(name, b);
      }
      return b;
    }
    internal List<string> SortedBranches() {
      var names = new List<string>(Branches.Keys);
      names.Sort(StringComparer.Ordinal);
      if (names.Remove("main")) names.Insert(0, "main");
      return names;
    }
  }

  public sealed class WorkflowWindow : Form {
    private const string DockerContainer = "fortis-repo-test";
    private const string Exporter =
      "/workspace/repo/Repos/script-control-center/tools/Export-Docker-Branch-Workflow.py";
    private static readonly Regex SafeRepo =
      new Regex(@"^[A-Za-z0-9_][A-Za-z0-9_.-]{0,119}$", RegexOptions.CultureInvariant);
    private static readonly Regex SafeBranch =
      new Regex(@"^[A-Za-z0-9_][A-Za-z0-9_./-]{0,159}$", RegexOptions.CultureInvariant);
    private static readonly Regex Sha =
      new Regex(@"^[0-9a-f]{40}$", RegexOptions.CultureInvariant);
    private static readonly Regex AllowedOrigin =
      new Regex(@"^(?:https://github\.com/|git@github\.com:)AlexPastukhh/[A-Za-z0-9_.-]+(?:\.git)?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly HashSet<string> Protected =
      new HashSet<string>(new string[] { "tunnel", "rd-graphs", "script-control-center", "security" },
        StringComparer.OrdinalIgnoreCase);
    private readonly string root, git;
    private readonly ListView list = new ListView();
    private readonly TextBox details = new TextBox();
    private readonly Label status = new Label();
    private readonly Button checkSelected = Button("Проверить выбранные", 180);
    private readonly Button checkAll = Button("Проверить все", 130);
    private readonly Button pull = Button("Pull Windows", 140);
    private readonly Button copy = Button("Копировать пути", 145);
    private readonly Button refresh = Button("Обновить список", 140);
    private readonly Button cancel = Button("Отмена проверки", 145);
    private readonly Button otherDocker = Button("Другие ветки Docker", 185);
    private readonly Button otherWindows = Button("Другие ветки Windows", 195);
    private readonly List<WfRepo> repos = new List<WfRepo>();
    private CancellationTokenSource cancellation;
    private bool working, writing, showOtherDocker, showOtherWindows;
    private const int ColumnCount = 13;
    private static readonly string[] Columns = {
      "Репозиторий",
      "Docker · Ветка для просмотра", "Docker · Активная ветка",
      "Docker · GitHub", "Docker · Изменения", "Docker · SHA",
      "Windows · Ветка для просмотра", "Windows · Активная ветка",
      "Windows · GitHub", "Windows · Изменения", "Windows · SHA",
      "Workflow · Итог", "Workflow · Действие"
    };
    private static readonly int[] Widths = {
      160, 133, 124, 120, 95, 82, 133, 124, 120, 95, 82, 160, 155
    };

    private static Button Button(string label, int width) {
      return new Button { Text = label, Width = width, Height = 32,
        Margin = new Padding(0, 3, 8, 3), FlatStyle = FlatStyle.Flat,
        BackColor = Color.FromArgb(39, 66, 95), ForeColor = Color.White };
    }
    public WorkflowWindow(string mirrorRoot, string gitExe) {
      root = Path.GetFullPath(mirrorRoot);
      git = gitExe;
      Text = "Fortis — Docker → GitHub → Windows";
      Size = new Size(1435, 800);
      MinimumSize = new Size(1000, 545);
      StartPosition = FormStartPosition.CenterScreen;
      Font = new Font("Segoe UI", 9f);
      BackColor = Color.FromArgb(12, 22, 38);
      ForeColor = Color.FromArgb(230, 239, 248);
      var layout = new TableLayoutPanel { Dock = DockStyle.Fill,
        ColumnCount = 1, RowCount = 3, Padding = new Padding(9) };
      layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 84));
      layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
      layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 265));
      Controls.Add(layout);

      var heading = new Panel { Dock = DockStyle.Fill };
      var title = new Label { Dock = DockStyle.Top, Height = 27,
        Text = "Docker → GitHub → Windows · репозитории из Docker",
        Font = new Font("Segoe UI Semibold", 13f, FontStyle.Bold),
        ForeColor = Color.White };
      var actions = new FlowLayoutPanel { Dock = DockStyle.Fill,
        WrapContents = true, AutoScroll = true };
      foreach (var b in new Button[] {
        checkSelected, checkAll, pull, copy, refresh, cancel
      }) actions.Controls.Add(b);
      heading.Controls.Add(actions);
      heading.Controls.Add(title);
      layout.Controls.Add(heading, 0, 0);

      list.Dock = DockStyle.Fill;
      list.View = View.Details; list.FullRowSelect = true;
      list.GridLines = true; list.HideSelection = false; list.MultiSelect = true;
      list.BackColor = Color.FromArgb(21, 35, 54);
      list.ForeColor = Color.FromArgb(230, 239, 248);
      list.BorderStyle = BorderStyle.FixedSingle;
      for (int i=0; i<Columns.Length; i++) list.Columns.Add(Columns[i], Widths[i]);
      layout.Controls.Add(list, 0, 1);

      var bottom = new Panel { Dock = DockStyle.Fill };
      var tools = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 36 };
      tools.Controls.Add(otherDocker); tools.Controls.Add(otherWindows);
      status.Dock = DockStyle.Top; status.Height = 25;
      status.ForeColor = Color.FromArgb(191, 213, 237);
      details.Dock = DockStyle.Fill; details.Multiline = true;
      details.ReadOnly = true; details.WordWrap = true;
      details.ScrollBars = ScrollBars.Vertical; details.BorderStyle = BorderStyle.None;
      details.BackColor = Color.FromArgb(12, 22, 38);
      details.ForeColor = Color.FromArgb(183, 205, 230);
      bottom.Controls.Add(details); bottom.Controls.Add(tools); bottom.Controls.Add(status);
      layout.Controls.Add(bottom, 0, 2);

      checkSelected.Click += delegate { BeginCheck(false); };
      checkAll.Click += delegate { BeginCheck(true); };
      pull.Click += delegate { BeginPull(); };
      copy.Click += delegate { CopyPaths(); };
      refresh.Click += delegate { BeginRefresh(false); };
      cancel.Click += delegate { if (cancellation != null) cancellation.Cancel(); };
      otherDocker.Click += delegate { showOtherDocker = !showOtherDocker; ShowDetails(); };
      otherWindows.Click += delegate { showOtherWindows = !showOtherWindows; ShowDetails(); };
      list.SelectedIndexChanged += delegate { ShowDetails(); Buttons(); };
      list.MouseDown += BranchClick;
      FormClosing += delegate(object sender, FormClosingEventArgs e) {
        if (writing) {
          e.Cancel = true;
          MessageBox.Show(this, "Операция Git выполняется. Не закрывайте окно до завершения.",
            "Git операция", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        } else if (cancellation != null) cancellation.Cancel();
      };
      Shown += delegate { BeginRefresh(true); };
      status.Text = "Ожидается перечень Git-репозиториев Docker. GitHub без кнопки проверки не запрашивается.";
      Buttons();
    }

    private static bool ValidBranch(string s) {
      return !String.IsNullOrEmpty(s) && SafeBranch.IsMatch(s) &&
        !s.EndsWith("/") && !s.EndsWith(".lock") &&
        !s.Contains("..") && !s.Contains("//");
    }
    private static bool ValidOrigin(string s) {
      return !String.IsNullOrEmpty(s) && AllowedOrigin.IsMatch(s);
    }
    private static string CanonicalOrigin(string s) {
      if (!ValidOrigin(s)) return "";
      string path = s.Substring(s.IndexOf("AlexPastukhh/", StringComparison.OrdinalIgnoreCase));
      if (path.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        path = path.Substring(0, path.Length - 4);
      return path.ToLowerInvariant();
    }
    private static string Q(string s) {
      return "\"" + s.Replace("\"", "\\\"") + "\"";
    }
    private static string Short(string s) {
      return s != null && s.Length >= 8 ? s.Substring(0, 8) : "—";
    }
    private static string SafeError(string stderr) {
      if (Regex.IsMatch(stderr, @"(?i)(not possible to fast-forward|divergent branches)"))
        return "ИСТОРИИ РАЗОШЛИСЬ";
      if (Regex.IsMatch(stderr, @"(?i)(local changes|overwritten|index.lock|another git process)"))
        return "ЛОКАЛЬНЫЕ ИЗМЕНЕНИЯ / GIT ЗАНЯТ";
      if (Regex.IsMatch(stderr, @"(?i)(ssl|tls|schannel|certificate)")) return "ОШИБКА TLS";
      if (Regex.IsMatch(stderr, @"(?i)(authentication|permission denied|403|401)")) return "ОШИБКА ДОСТУПА";
      if (Regex.IsMatch(stderr, @"(?i)(resolve host|name resolution)")) return "ОШИБКА DNS";
      return "ОШИБКА КОМАНДЫ";
    }
    private static WfCommand Run(string exe, string args, int timeoutMs,
      CancellationToken token, bool write = false) {
      var result = new WfCommand();
      if (token.IsCancellationRequested) return result;
      try {
        using (var p = new Process()) {
          p.StartInfo.FileName = exe; p.StartInfo.Arguments = args;
          p.StartInfo.UseShellExecute = false;
          p.StartInfo.CreateNoWindow = true;
          p.StartInfo.RedirectStandardOutput = true;
          p.StartInfo.RedirectStandardError = true;
          p.StartInfo.EnvironmentVariables["GIT_TERMINAL_PROMPT"] = "0";
          p.StartInfo.EnvironmentVariables["GCM_INTERACTIVE"] = "never";
          p.StartInfo.EnvironmentVariables["GIT_OPTIONAL_LOCKS"] = write ? "1" : "0";
          p.StartInfo.EnvironmentVariables["GIT_SSH_COMMAND"] =
            "ssh -o BatchMode=yes -o ConnectTimeout=8";
          p.StartInfo.EnvironmentVariables["PYTHONDONTWRITEBYTECODE"] = "1";
          var stdout = new StringBuilder(); var stderr = new StringBuilder();
          object gate = new object();
          p.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e) {
            if (e.Data != null) lock (gate) {
              if (stdout.Length < 1048576) stdout.AppendLine(e.Data);
            }
          };
          p.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e) {
            if (e.Data != null) lock (gate) {
              if (stderr.Length < 4096) stderr.AppendLine(e.Data);
            }
          };
          p.Start(); p.BeginOutputReadLine(); p.BeginErrorReadLine();
          var watch = Stopwatch.StartNew();
          bool stopped = false;
          while (!p.WaitForExit(120)) {
            if ((!write && token.IsCancellationRequested) || watch.ElapsedMilliseconds >= timeoutMs) {
              result.Timeout = watch.ElapsedMilliseconds >= timeoutMs;
              stopped = true;
              // Do not kill an in-progress Git worktree mutation.
              if (!write) try { p.Kill(); } catch (Exception) { }
              else {
                // The owner must inspect Git state if a mutation exceeds the bound.
                p.WaitForExit();
              }
              break;
            }
          }
          if (!stopped) p.WaitForExit();
          result.Ok = !stopped && p.ExitCode == 0;
          lock (gate) {
            result.Text = stdout.ToString().TrimEnd('\r', '\n');
            result.Error = result.Ok ? "" : result.Timeout ? "ТАЙМАУТ" : SafeError(stderr.ToString());
          }
        }
      } catch (Exception) { result.Error = "ОШИБКА ЗАПУСКА"; }
      return result;
    }
    private WfCommand Git(string path, string cmd, int timeoutMs, CancellationToken token,
      bool write = false) {
      string prefix = "-c core.fsmonitor=false -c http.sslBackend=openssl -c http.sslVerify=true " +
        "-c core.hooksPath=/dev/null -c submodule.recurse=false ";
      // All callers use fixed commands, validated branch names and quoted known paths.
      return Run(git, prefix + (path == null ? "" : "-C " + Q(path) + " ") + cmd,
        timeoutMs, token, write);
    }
    private WfCommand Docker(string tail, int timeoutMs, CancellationToken token, bool write = false) {
      return Run("docker.exe", "exec --user 1000:1000 " + DockerContainer +
        " python3 -B " + Exporter + " " + tail, timeoutMs, token, write);
    }
    private bool ParseDocker(string text, out List<WfRepo> found) {
      found = new List<WfRepo>();
      if (text.Length > 1048576) return false;
      var lines = text.Replace("\r\n", "\n").Split('\n');
      if (lines.Length < 1 || lines.Length > 10000 ||
        !lines[0].StartsWith("FORTIS_DOCKER_BRANCH_V2\t", StringComparison.Ordinal))
        return false;
      DateTime stamp;
      if (!DateTime.TryParseExact(lines[0].Split('\t')[1], "yyyy-MM-ddTHH:mm:ssZ",
        CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal |
        DateTimeStyles.AdjustToUniversal, out stamp) ||
        stamp > DateTime.UtcNow.AddMinutes(10) ||
        DateTime.UtcNow - stamp > TimeSpan.FromMinutes(10)) return false;
      var lookup = new Dictionary<string, WfRepo>(StringComparer.OrdinalIgnoreCase);
      foreach (string line in lines) {
        if (line.Length == 0 || line.StartsWith("FORTIS_DOCKER_BRANCH_V2\t")) continue;
        var part = line.Split('\t');
        if (part[0] == "R") {
          int changes;
          if (part.Length != 6 || !SafeRepo.IsMatch(part[1]) || lookup.ContainsKey(part[1]) ||
             !Sha.IsMatch(part[3]) || !Int32.TryParse(part[4], out changes) || changes < -1)
            return false;
          if (part[2] != "-" && !ValidBranch(part[2])) return false;
          if (part[5] != "-" && !ValidOrigin(part[5])) return false;
          var r = new WfRepo { Name = part[1], DockerActive = part[2] == "-" ? "" : part[2],
            DockerHead = part[3], DockerChanges = changes,
            DockerOrigin = part[5] == "-" ? "" : part[5],
            WindowsPath = Path.Combine(root, part[1]) };
          lookup.Add(r.Name, r); found.Add(r);
        } else if (part[0] == "B") {
          WfRepo repo;
          if (part.Length != 4 || !lookup.TryGetValue(part[1], out repo) ||
            !ValidBranch(part[2]) || !Sha.IsMatch(part[3]) ||
            repo.Get(part[2]) != null) return false;
          repo.Ensure(part[2]).DockerSha = part[3];
        } else if (part[0] != "C") return false;
      }
      foreach (var r in found) {
        r.DockerView = r.DockerActive;
        r.WindowsView = r.DockerActive;
        r.WindowsExists = Directory.Exists(r.WindowsPath) &&
          Directory.Exists(Path.Combine(r.WindowsPath, ".git"));
      }
      return true;
    }
    private static bool ParseCounts(string s, out int ahead, out int behind) {
      ahead = behind = -1;
      var m = Regex.Match(s, @"^(\d+)\s+(\d+)$");
      if (!m.Success) return false;
      return Int32.TryParse(m.Groups[1].Value, out ahead) &&
        Int32.TryParse(m.Groups[2].Value, out behind);
    }
    private static bool HasDotGit(string path) {
      try {
        var dir = new DirectoryInfo(path);
        var git = new DirectoryInfo(Path.Combine(path, ".git"));
        return dir.Exists && (dir.Attributes & FileAttributes.ReparsePoint) == 0 &&
          git.Exists && (git.Attributes & FileAttributes.ReparsePoint) == 0;
      } catch (Exception) { return false; }
    }
    private static bool ValidWindowsPath(string path) {
      return HasDotGit(path);
    }
    private static void ReadCounts(WfBranch b, bool docker, string output) {
      int ahead, behind;
      if (!ParseCounts(output, out ahead, out behind)) return;
      if (docker) { b.DockerAhead = ahead; b.DockerBehind = behind; }
      else { b.WindowsAhead = ahead; b.WindowsBehind = behind; }
    }
    private void ProbeGithub(WfRepo r, CancellationToken token) {
      r.CheckedGithub = true;
      foreach (var branch in r.Branches.Values) branch.GithubSha = "";
      if (!ValidOrigin(r.DockerOrigin)) { r.Problem = "НЕРАЗРЕШЁННЫЙ ORIGIN DOCKER"; return; }
      var result = Git(null, "ls-remote --heads " + Q(r.DockerOrigin), 18000, token);
      if (!result.Ok) { r.Problem = "GITHUB: " + result.Error; return; }
      foreach (var line in result.Text.Split('\n')) {
        string[] part = line.Trim().Split('\t');
        if (part.Length != 2 || !Sha.IsMatch(part[0]) ||
          !part[1].StartsWith("refs/heads/", StringComparison.Ordinal)) continue;
        string name = part[1].Substring("refs/heads/".Length);
        if (!ValidBranch(name)) continue;
        r.Ensure(name).GithubSha = part[0];
      }
    }
    private void ProbeWindows(WfRepo r, CancellationToken token) {
      r.CheckedWindows = true;
      r.WindowsExists = HasDotGit(r.WindowsPath);
      r.WindowsValid = r.WindowsExists;
      if (!r.WindowsExists) {
        r.WindowsChanges = -1; return;
      }
      var origin = Git(r.WindowsPath, "remote get-url origin", 4000, token);
      if (!origin.Ok || !ValidOrigin(origin.Text) ||
        CanonicalOrigin(origin.Text) != CanonicalOrigin(r.DockerOrigin)) {
        r.Problem = "WINDOWS: ORIGIN НЕ СОВПАДАЕТ С DOCKER";
        return;
      }
      r.WindowsOriginValid = true;
      var active = Git(r.WindowsPath, "symbolic-ref --quiet --short HEAD", 5000, token);
      r.WindowsActive = active.Ok && ValidBranch(active.Text) ? active.Text : "";
      var head = Git(r.WindowsPath, "rev-parse --verify HEAD", 5000, token);
      r.WindowsHead = head.Ok && Sha.IsMatch(head.Text) ? head.Text : "";
      var dirty = Git(r.WindowsPath, "status --porcelain=v1 --untracked-files=all", 9000, token);
      r.WindowsChanges = dirty.Ok ? (dirty.Text.Length == 0 ? 0 : dirty.Text.Split('\n').Length) : -1;
      var branches = Git(r.WindowsPath,
        "for-each-ref --format=%(refname:short)%09%(objectname) refs/heads",
        8000, token);
      if (branches.Ok) foreach (string line in branches.Text.Split('\n')) {
        string[] parts = line.Trim().Split('\t');
        if (parts.Length == 2 && ValidBranch(parts[0]) && Sha.IsMatch(parts[1]))
          r.Ensure(parts[0]).WindowsSha = parts[1];
      }
      r.WindowsView = r.WindowsActive.Length > 0 ? r.WindowsActive : r.WindowsView;
      // A user-requested Check may retrieve Git metadata for the active branch.
      foreach (var b in r.Branches.Values) {
        if (b.GithubSha.Length == 0 || b.WindowsSha.Length == 0) continue;
        if (b.GithubSha == b.WindowsSha) {
          b.WindowsAhead = b.WindowsBehind = 0; continue;
        }
        var result = Git(r.WindowsPath, "rev-list --left-right --count " +
          b.WindowsSha + "..." + b.GithubSha, 8000, token);
        if (!result.Ok && b.Name == r.WindowsActive && ValidBranch(b.Name) &&
          !token.IsCancellationRequested) {
          var fetched = Git(r.WindowsPath, "fetch --no-tags --no-recurse-submodules origin " +
            Q(b.Name), 35000, token, true);
          if (fetched.Ok)
            result = Git(r.WindowsPath, "rev-list --left-right --count " +
              b.WindowsSha + "..." + b.GithubSha, 8000, token);
        }
        if (result.Ok) ReadCounts(b, false, result.Text);
      }
    }
    private void ProbeDockerCounts(List<WfRepo> targets, CancellationToken token) {
      // Docker itself decides ancestry. GitHub SHAs are from real ls-remote,
      // not stale origin/* refs; unknown object graphs stay UNKNOWN.
      var args = new StringBuilder("--stdout");
      int n = 0;
      foreach (var r in targets) {
        if (!r.CheckedGithub || !ValidOrigin(r.DockerOrigin)) continue;
        foreach (var b in r.Branches.Values) {
          if (!ValidBranch(b.Name) || !Sha.IsMatch(b.DockerSha) ||
            !Sha.IsMatch(b.GithubSha)) continue;
          args.Append(" --compare ").Append(r.Name).Append(" ").Append(b.Name)
            .Append(" ").Append(b.GithubSha);
          n++;
        }
      }
      if (n == 0) return;
      var cmd = Docker(args.ToString(), 90000, token);
      if (!cmd.Ok) return;
      foreach (string line in cmd.Text.Split('\n')) {
        string[] p = line.Trim().Split('\t');
        if (p.Length != 5 || p[0] != "C") continue;
        WfRepo r = targets.Find(delegate(WfRepo item) {
          return String.Equals(item.Name, p[1], StringComparison.OrdinalIgnoreCase);
        });
        if (r == null) continue;
        WfBranch b = r.Get(p[2]);
        int ahead, behind;
        if (b != null && Int32.TryParse(p[3], out ahead) &&
          Int32.TryParse(p[4], out behind) && ahead >= 0 && behind >= 0) {
          b.DockerAhead = ahead; b.DockerBehind = behind;
        }
      }
    }

    private static string DockerLabel(WfRepo r, WfBranch b) {
      if (b == null || b.DockerSha.Length == 0) return "НЕТ В DOCKER";
      if (!r.CheckedGithub) return "НЕ ПРОВЕРЕН";
      if (r.Problem.Length > 0) return r.Problem;
      if (b.GithubSha.Length == 0) return "НЕТ В GITHUB";
      if (b.DockerSha == b.GithubSha) return "РАВЕН GITHUB";
      if (b.DockerAhead > 0 && b.DockerBehind == 0)
        return "ВПЕРЕДИ +" + b.DockerAhead;
      if (b.DockerAhead == 0 && b.DockerBehind > 0)
        return "ОТСТАЁТ НА " + b.DockerBehind;
      if (b.DockerAhead > 0 && b.DockerBehind > 0) return "ИСТОРИИ РАЗОШЛИСЬ";
      return "SHA ≠ GH · НАПРАВЛЕНИЕ НЕИЗВЕСТНО";
    }
    private static string WindowsLabel(WfRepo r, WfBranch b) {
      if (!r.WindowsExists) return "НЕТ КЛОНА";
      if (!r.CheckedWindows) return "НЕ ПРОВЕРЕН";
      if (!r.WindowsOriginValid) return "НЕКОРРЕКТНЫЙ ORIGIN";
      if (b == null || b.WindowsSha.Length == 0) return "НЕТ ВЕТКИ WINDOWS";
      if (!r.CheckedGithub) return "НЕ ПРОВЕРЕН";
      if (b.GithubSha.Length == 0) return "НЕТ В GITHUB";
      if (b.WindowsSha == b.GithubSha) return "АКТУАЛЕН К GITHUB";
      if (b.WindowsAhead > 0 && b.WindowsBehind == 0)
        return "ВПЕРЕДИ GITHUB! +" + b.WindowsAhead;
      if (b.WindowsAhead == 0 && b.WindowsBehind > 0)
        return "ОТСТАЁТ НА " + b.WindowsBehind;
      if (b.WindowsAhead > 0 && b.WindowsBehind > 0) return "ИСТОРИИ РАЗОШЛИСЬ";
      return "SHA ≠ GH · НАПРАВЛЕНИЕ НЕИЗВЕСТНО";
    }
    private static string[] Overall(WfRepo r) {
      // The overall workflow is based exclusively on ACTUAL active branches.
      // Display selectors DockerView/WindowsView must never affect this status.
      WfBranch d = r.Get(r.DockerActive), w = r.Get(r.WindowsActive);
      if (r.Problem.StartsWith("WINDOWS:", StringComparison.Ordinal))
        return new string[] { "ОШИБКА WINDOWS", r.Problem };
      if (r.Problem.Length > 0) return new string[] { "НЕ ПРОВЕРЕНО", r.Problem };
      if (!r.WindowsExists) return new string[] {
        "НЕТ КЛОНА WINDOWS", "Клонировать вручную, если нужен Windows-клон" };
      if (r.CheckedWindows && !r.WindowsOriginValid)
        return new string[] { "ОШИБКА ORIGIN WINDOWS", "Сверить remote с Docker" };
      if (r.WindowsChanges > 0)
        return new string[] { "WINDOWS: ИЗМЕНЕНИЯ", "Разобрать Windows-файлы; Pull/checkout запрещён" };
      if (r.WindowsChanges < 0 && r.CheckedWindows)
        return new string[] { "WINDOWS: НЕ ПРОВЕРЕН", "Проверить Windows git status" };
      if (w != null && w.WindowsAhead > 0 && w.WindowsBehind > 0)
        return new string[] { "WINDOWS: ИСТОРИИ РАЗОШЛИСЬ", "Не выполнять Pull" };
      if (w != null && w.WindowsAhead > 0)
        return new string[] { "WINDOWS НЕОЖИДАННО ВПЕРЕДИ", "Разобрать локальные Windows-коммиты" };
      if (d != null && d.DockerAhead > 0 && d.DockerBehind > 0)
        return new string[] { "DOCKER: ИСТОРИИ РАЗОШЛИСЬ", "Ручной разбор Git-истории" };
      if (d != null && d.DockerBehind > 0 && d.DockerAhead == 0)
        return new string[] { "DOCKER ОТСТАЁТ", "Проверить неожиданное состояние Docker" };
      if (r.DockerChanges > 0)
        return new string[] { "DOCKER: ИЗМЕНЕНИЯ", "Commit в Docker → Push" };
      if (r.DockerChanges < 0)
        return new string[] { "DOCKER: НЕ ПРОВЕРЕН", "Проверить Docker git status" };
      if (d != null && d.DockerAhead > 0 && d.DockerBehind == 0)
        return new string[] { "DOCKER ВПЕРЕДИ", "Push из Docker" };
      if (!r.CheckedGithub || !r.CheckedWindows)
        return new string[] { "НЕ ПРОВЕРЕНО", "Нажать «Проверить выбранные/все»" };
      if (d == null || d.GithubSha.Length == 0)
        return new string[] { "ВЕТКА НЕ ОПУБЛИКОВАНА", "Проверить ветку Docker и GitHub" };
      if (r.DockerActive != r.WindowsActive)
        return new string[] { "АКТИВНЫЕ ВЕТКИ РАЗНЫЕ", "Проверить выбор Docker и Windows" };
      if (w != null && w.WindowsAhead == 0 && w.WindowsBehind > 0)
        return new string[] { "WINDOWS ОТСТАЁТ", "Pull выбранной Windows-ветки" };
      if (d.DockerSha == d.GithubSha && w != null &&
        w.WindowsSha == d.GithubSha && r.WindowsChanges == 0)
        return new string[] {
          r.DockerActive == "main" ? "АКТУАЛЕН" : "АКТУАЛЕН · НЕ MAIN",
          "Ничего не требуется" };
      return new string[] { "ТРЕБУЕТ ПРОВЕРКИ", "SHA или история не подтверждены" };
    }
    private static Color StateColor(string text) {
      if (text == "АКТУАЛЕН") return Color.FromArgb(116, 215, 169);
      if (text.StartsWith("WINDOWS: ИЗМЕНЕНИЯ") ||
        text.StartsWith("WINDOWS НЕОЖИДАННО") || text.Contains("РАЗОШЛИСЬ") ||
        text.StartsWith("ОШИБКА")) return Color.FromArgb(255, 140, 137);
      if (text.StartsWith("НЕ ") || text.StartsWith("НЕТ ") ||
        text.StartsWith("НЕ ПРОВЕРЕНО")) return Color.FromArgb(186, 199, 217);
      return Color.FromArgb(255, 195, 117);
    }
    private static string FileChanges(int count, bool active) {
      return !active ? "Неактивная ветка" :
        count < 0 ? "НЕ ПРОВЕРЕНО" : count == 0 ? "Нет" : "Есть: " + count;
    }
    private static string[] Fields(WfRepo r, string dockerView, string windowsView,
      bool full = false) {
      WfBranch d = r.Get(dockerView), w = r.Get(windowsView);
      string[] overall = Overall(r);
      string dsha = d == null ? "" : d.DockerSha;
      string wsha = w == null ? "" : w.WindowsSha;
      return new string[] {
        r.Name, dockerView.Length > 0 ? dockerView : "—",
        r.DockerActive.Length > 0 ? r.DockerActive : "—",
        DockerLabel(r, d), FileChanges(r.DockerChanges, r.DockerActive == dockerView),
        full ? (dsha.Length > 0 ? dsha : "Нет локальной ветки") : Short(dsha),
        windowsView.Length > 0 ? windowsView : "—",
        r.WindowsExists ? (r.WindowsActive.Length > 0 ? r.WindowsActive : "НЕ ПРОВЕРЕНА")
          : "НЕТ КЛОНА",
        WindowsLabel(r, w),
        r.WindowsExists ? FileChanges(r.WindowsChanges, r.WindowsActive == windowsView) : "Нет клона",
        full ? (wsha.Length > 0 ? wsha : "Нет подтверждённой ветки") : Short(wsha),
        overall[0], overall[1]
      };
    }
    private WfRepo Selected() {
      return list.SelectedItems.Count == 1 ? list.SelectedItems[0].Tag as WfRepo : null;
    }
    private void Buttons() {
      bool selectedGit = false;
      foreach (ListViewItem item in list.SelectedItems)
        if (item.Tag is WfRepo) selectedGit = true;
      checkSelected.Enabled = !working && selectedGit;
      checkAll.Enabled = !working && repos.Count > 0;
      refresh.Enabled = !working;
      cancel.Enabled = working && !writing;
      copy.Enabled = !working && Selected() != null;
      pull.Enabled = !working && CanPull(Selected());
      otherDocker.Enabled = !working && Selected() != null;
      otherWindows.Enabled = !working && Selected() != null;
      // Freeze selection while owner-approved Git writes update a worktree.
      list.Enabled = !writing;
    }
    private void DrawTable(string preferred) {
      list.BeginUpdate();
      try {
        list.Items.Clear();
        foreach (var r in repos) {
          string[] values = Fields(r, r.DockerView, r.WindowsView);
          var item = new ListViewItem(values[0]);
          for (int i = 1; i < ColumnCount; i++) item.SubItems.Add(values[i]);
          item.Tag = r; r.Item = item;
          item.ForeColor = StateColor(values[11]);
          list.Items.Add(item);
          if (preferred != null && String.Equals(preferred, r.Name, StringComparison.OrdinalIgnoreCase))
            item.Selected = true;
        }
        if (list.SelectedItems.Count == 0 && list.Items.Count > 0)
          list.Items[0].Selected = true;
      } finally { list.EndUpdate(); }
      ShowDetails(); Buttons();
    }
    private void RefreshRow(WfRepo r) {
      if (r.Item == null) return;
      string[] values = Fields(r, r.DockerView, r.WindowsView);
      for (int i = 0; i < ColumnCount; i++) r.Item.SubItems[i].Text = values[i];
      r.Item.ForeColor = StateColor(values[11]);
      ShowDetails(); Buttons();
    }
    private static void AddFields(StringBuilder b, WfRepo r, string d, string w) {
      string[] values = Fields(r, d, w, true);
      for (int i = 0; i < ColumnCount; i++)
        b.Append(Columns[i]).Append(": ").AppendLine(values[i]);
    }
    private void ShowDetails() {
      WfRepo r = Selected();
      if (r == null) {
        details.Text = "Выберите одну строку. Ветки для просмотра и активные ветки независимы.";
        return;
      }
      var b = new StringBuilder();
      AddFields(b, r, r.DockerView, r.WindowsView);
      b.AppendLine();
      b.AppendLine("ДОПОЛНИТЕЛЬНЫЕ СВЕДЕНИЯ ПОСЛЕ КОЛОНОК");
      b.AppendLine("Docker: /workspace/repo/Repos/" + r.Name);
      b.AppendLine("Windows: " + r.WindowsPath);
      b.AppendLine("Docker HEAD активной ветки: " + (r.DockerHead.Length > 0 ? r.DockerHead : "—"));
      b.AppendLine("Windows HEAD активной ветки: " + (r.WindowsHead.Length > 0 ? r.WindowsHead : "—"));
      b.AppendLine("Docker origin: " + (r.DockerOrigin.Length > 0 ? r.DockerOrigin : "НЕ ПРОВЕРЕН"));
      b.AppendLine("Просмотр ветки не выполняет checkout ни в Docker, ни в Windows.");
      if (showOtherDocker) {
        foreach (string name in r.SortedBranches()) {
          if (name == r.DockerView) continue;
          b.AppendLine().AppendLine("=== ДОПОЛНИТЕЛЬНАЯ ВЕТКА DOCKER: " + name + " ===");
          AddFields(b, r, name, r.WindowsView);
        }
      }
      if (showOtherWindows) {
        foreach (string name in r.SortedBranches()) {
          if (name == r.WindowsView) continue;
          b.AppendLine().AppendLine("=== ДОПОЛНИТЕЛЬНАЯ ВЕТКА WINDOWS: " + name + " ===");
          AddFields(b, r, r.DockerView, name);
        }
      }
      details.Text = b.ToString().TrimEnd();
      details.SelectionStart = 0;
      details.ScrollToCaret();
      otherDocker.Text = (showOtherDocker ? "Скрыть" : "Другие") + " ветки Docker";
      otherWindows.Text = (showOtherWindows ? "Скрыть" : "Другие") + " ветки Windows";
    }
    private void BranchClick(object sender, MouseEventArgs e) {
      if (working || e.Button != MouseButtons.Left) return;
      var hit = list.HitTest(e.Location);
      if (hit.Item == null || hit.SubItem == null) return;
      int index = hit.Item.SubItems.IndexOf(hit.SubItem);
      if (index != 1 && index != 2 && index != 6 && index != 7) return;
      var r = hit.Item.Tag as WfRepo;
      if (r == null) return;
      foreach (ListViewItem item in list.Items) item.Selected = item == hit.Item;
      bool isDocker = index == 1 || index == 2;
      bool isView = index == 1 || index == 6;
      var menu = new ContextMenuStrip();
      menu.ShowImageMargin = false;
      menu.Items.Add(isView ? "Просмотр: без переключения Git" :
        "АКТИВНАЯ ВЕТКА: только с подтверждением").Enabled = false;
      foreach (string name in r.SortedBranches()) {
        var b = r.Get(name);
        string label = isDocker ? DockerLabel(r, b) : WindowsLabel(r, b);
        string active = isDocker ? r.DockerActive : r.WindowsActive;
        string viewed = isDocker ? r.DockerView : r.WindowsView;
        var entry = new ToolStripMenuItem(name + "   [" + label + "]");
        entry.Checked = isView ? name == viewed : name == active;
        if (!isView) {
          entry.Enabled = isDocker ?
            r.DockerChanges == 0 && b != null && b.DockerSha.Length > 0 &&
            !Protected.Contains(r.Name) && ValidOrigin(r.DockerOrigin) :
            r.WindowsValid && r.WindowsOriginValid && r.WindowsChanges == 0 &&
            b != null && b.WindowsSha.Length > 0;
        }
        string target = name;
        entry.Click += delegate {
          if (isView) {
            if (isDocker) r.DockerView = target;
            else r.WindowsView = target;
            RefreshRow(r);
          } else BeginSwitch(r, isDocker, target);
        };
        menu.Items.Add(entry);
      }
      menu.Closed += delegate { menu.Dispose(); };
      menu.Show(list, e.Location);
    }
    private void CopyPaths() {
      WfRepo r = Selected();
      if (r == null) return;
      try {
        Clipboard.SetText(
          "Docker: /workspace/repo/Repos/" + r.Name + Environment.NewLine +
          "Windows: " + r.WindowsPath + Environment.NewLine +
          "Docker просмотр: " + r.DockerView + "; активна: " + r.DockerActive + Environment.NewLine +
          "Windows просмотр: " + r.WindowsView + "; активна: " + r.WindowsActive);
        status.Text = "Пути и ветки скопированы: " + r.Name;
      } catch (Exception) { status.Text = "Не удалось скопировать в буфер обмена."; }
    }
    private void Ui(Action action) {
      try {
        if (!IsDisposed && IsHandleCreated)
          BeginInvoke((MethodInvoker)delegate { if (!IsDisposed) action(); });
      } catch (InvalidOperationException) { }
    }
    private void EndWork(string message) {
      working = false; writing = false; cancellation = null;
      status.Text = message; Buttons();
    }
    private void BeginRefresh(bool initial) {
      if (working) return;
      working = true; writing = false;
      cancellation = new CancellationTokenSource();
      CancellationToken token = cancellation.Token;
      status.Text = "Docker: получение перечня репозиториев и веток...";
      Buttons();
      ThreadPool.QueueUserWorkItem(delegate {
        var cmd = Docker("--stdout", 90000, token);
        var result = new List<WfRepo>();
        bool valid = cmd.Ok && ParseDocker(cmd.Text, out result);
        if (token.IsCancellationRequested) valid = false;
        Ui(delegate {
          if (valid) {
            string oldSelected = Selected() == null ? null : Selected().Name;
            repos.Clear(); repos.AddRange(result);
            DrawTable(oldSelected);
            EndWork("Docker: найдено " + repos.Count +
              " репозиториев. GitHub и Windows проверяются отдельной кнопкой.");
          } else {
            EndWork(initial ? "Docker недоступен: список не получен. Проверьте Docker Desktop." :
              "Не удалось прочитать Docker. Старые строки сохранены, их статус не свежий.");
          }
        });
      });
    }
    private void BeginCheck(bool all) {
      if (working) return;
      var names = new List<string>();
      if (all) foreach (var r in repos) names.Add(r.Name);
      else foreach (ListViewItem item in list.SelectedItems) {
        var r = item.Tag as WfRepo;
        if (r != null) names.Add(r.Name);
      }
      if (names.Count == 0) return;
      working = true; writing = false;
      cancellation = new CancellationTokenSource();
      CancellationToken token = cancellation.Token;
      status.Text = "Docker/Windows/GitHub: проверка " + names.Count + " репозиториев...";
      Buttons();
      ThreadPool.QueueUserWorkItem(delegate {
        var snap = Docker("--stdout", 90000, token);
        List<WfRepo> found;
        if (!snap.Ok || !ParseDocker(snap.Text, out found)) {
          Ui(delegate { EndWork("Docker недоступен или некорректный ответ. Нет новых статусов."); });
          return;
        }
        foreach (var r in found) {
          if (token.IsCancellationRequested) break;
          if (!names.Contains(r.Name)) continue;
          ProbeGithub(r, token);
          if (!token.IsCancellationRequested) ProbeWindows(r, token);
        }
        if (!token.IsCancellationRequested)
          ProbeDockerCounts(found.FindAll(delegate(WfRepo r) { return names.Contains(r.Name); }), token);
        Ui(delegate {
          if (token.IsCancellationRequested) {
            EndWork("Проверка отменена. Старые результаты оставлены без замены.");
            return;
          }
          string previous = Selected() == null ? null : Selected().Name;
          foreach (var fresh in found) {
            WfRepo old = repos.Find(delegate(WfRepo r) {
              return String.Equals(r.Name, fresh.Name, StringComparison.OrdinalIgnoreCase);
            });
            if (old != null) {
              if (fresh.Get(old.DockerView) != null) fresh.DockerView = old.DockerView;
              if (fresh.Get(old.WindowsView) != null) fresh.WindowsView = old.WindowsView;
            }
          }
          repos.Clear(); repos.AddRange(found);
          DrawTable(previous);
          EndWork("Проверено " + names.Count +
            " репозиториев. У других строк статус может оставаться «Не проверен».");
        });
      });
    }

    private bool CanPull(WfRepo r) {
      if (r == null || !r.WindowsExists || !r.WindowsValid ||
        !r.WindowsOriginValid || r.WindowsChanges != 0 ||
        r.WindowsView != r.WindowsActive || !ValidBranch(r.WindowsActive) ||
        !r.CheckedGithub || !r.CheckedWindows || r.Problem.Length != 0) return false;
      WfBranch b = r.Get(r.WindowsActive);
      return b != null && Sha.IsMatch(b.GithubSha) && Sha.IsMatch(b.WindowsSha) &&
        b.WindowsAhead == 0 && b.WindowsBehind > 0 &&
        ValidOrigin(r.DockerOrigin);
    }
    private void BeginPull() {
      WfRepo r = Selected();
      if (working || !CanPull(r)) return;
      WfBranch b = r.Get(r.WindowsActive);
      string old = b.WindowsSha, remote = b.GithubSha;
      string branch = r.WindowsActive;
      if (MessageBox.Show(this,
        "Обновить только Windows-клон через fast-forward?\n\n" + r.WindowsPath +
        "\nАктивная Windows-ветка: " + branch +
        "\nТекущий SHA: " + old + "\nGitHub SHA: " + remote +
        "\n\nЭто изменит файлы Windows-клона, но не запустит программу и не переключит ветку." +
        "\nПроверка повторится непосредственно перед pull. Продолжить?",
        "Подтвердить Pull Windows", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
        MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
      writing = true; working = true; cancellation = new CancellationTokenSource();
      Buttons();
      status.Text = "Повторные проверки перед Pull " + r.Name + "...";
      ThreadPool.QueueUserWorkItem(delegate {
        string failure = null;
        // Fresh copy prevents the displayed snapshot from authorizing stale writes.
        var fresh = new WfRepo { Name = r.Name, DockerOrigin = r.DockerOrigin,
          WindowsPath = r.WindowsPath };
        fresh.Ensure(branch).GithubSha = remote;
        ProbeGithub(fresh, CancellationToken.None);
        if (fresh.Problem.Length > 0 || fresh.Get(branch).GithubSha != remote)
          failure = "GitHub SHA изменился либо источник недоступен.";
        if (failure == null) ProbeWindows(fresh, CancellationToken.None);
        WfBranch target = fresh.Get(branch);
        if (failure == null && (fresh.WindowsActive != branch || fresh.WindowsChanges != 0 ||
          !fresh.WindowsOriginValid || target.WindowsSha != old ||
          target.WindowsAhead != 0 || target.WindowsBehind <= 0))
          failure = "Windows состояние/история изменились. Pull заблокирован.";
        WfCommand result = null;
        if (failure == null) {
          // Pin the owner-approved exact SHA. A normal pull could follow a
          // newer GitHub ref after the preflight and install unreviewed files.
          result = Git(r.WindowsPath,
            "fetch --no-tags --no-recurse-submodules origin " + Q(branch),
            180000, CancellationToken.None, true);
          if (!result.Ok) failure = "Git fetch: " + result.Error + ". Проверьте состояние вручную.";
          if (failure == null) {
            var fetchedHead = Git(r.WindowsPath, "rev-parse --verify FETCH_HEAD",
              7000, CancellationToken.None);
            if (!fetchedHead.Ok || fetchedHead.Text != remote)
              failure = "GitHub SHA изменился во время fetch. Merge не выполнен.";
          }
          if (failure == null) {
            var stillActive = Git(r.WindowsPath,
              "symbolic-ref --quiet --short HEAD", 5000, CancellationToken.None);
            var stillHead = Git(r.WindowsPath, "rev-parse --verify HEAD",
              5000, CancellationToken.None);
            var stillClean = Git(r.WindowsPath,
              "status --porcelain=v1 --untracked-files=all", 9000, CancellationToken.None);
            if (!stillActive.Ok || stillActive.Text != branch ||
              !stillHead.Ok || stillHead.Text != old ||
              !stillClean.Ok || stillClean.Text.Length != 0)
              failure = "Windows изменился между fetch и merge. Обновление заблокировано.";
          }
          if (failure == null) {
            result = Git(r.WindowsPath,
              "-c core.hooksPath=/dev/null merge --ff-only FETCH_HEAD",
              240000, CancellationToken.None, true);
            if (!result.Ok) failure = "Git fast-forward merge: " + result.Error +
              ". Проверьте состояние вручную.";
          }
        }
        string outcome = failure;
        Ui(delegate {
          EndWork(outcome != null ? outcome :
            "Pull завершён. Повторная проверка Windows/GitHub обязательна.");
          BeginCheck(false);
        });
      });
    }
    private void BeginSwitch(WfRepo r, bool docker, string target) {
      if (working || !ValidBranch(target)) return;
      string oldBranch = docker ? r.DockerActive : r.WindowsActive;
      if (!ValidBranch(oldBranch) || oldBranch == target) return;
      WfBranch branch = r.Get(target);
      if (branch == null) return;
      bool enabled = docker ?
        r.DockerChanges == 0 && Sha.IsMatch(r.DockerHead) &&
        Sha.IsMatch(branch.DockerSha) && ValidOrigin(r.DockerOrigin) &&
        !Protected.Contains(r.Name) :
        r.WindowsExists && r.WindowsValid && r.WindowsOriginValid &&
        r.WindowsChanges == 0 && Sha.IsMatch(r.WindowsHead) &&
        Sha.IsMatch(branch.WindowsSha);
      if (!enabled) return;
      string side = docker ? "Docker" : "Windows";
      string oldSha = docker ? r.DockerHead : r.WindowsHead;
      if (MessageBox.Show(this,
        "СМЕНИТЬ АКТИВНУЮ ВЕТКУ " + side.ToUpperInvariant() + "?\n\n" +
        "Репозиторий: " + r.Name + "\n" +
        "Текущая: " + oldBranch + "\nЦелевая: " + target + "\n" +
        "Ожидаемый SHA до переключения: " + oldSha + "\n\n" +
        "Просмотр ветки — отдельная настройка и НЕ изменится.\n" +
        "Git switch изменит файлы работающего клона; действующие процессы не перезапускаются." +
        "\nПеред переключением состояние будет проверено повторно.\n\nПродолжить?",
        "Подтвердить Git switch " + side,
        MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
        MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
      working = true; writing = true; cancellation = new CancellationTokenSource();
      Buttons(); status.Text = "Повторная проверка перед Git switch " + side + "...";
      ThreadPool.QueueUserWorkItem(delegate {
        string failure = null;
        WfCommand switched = null;
        if (docker) {
          switched = Docker("--switch-repo " + r.Name +
            " --switch-branch " + target + " --expect-branch " + oldBranch +
            " --expect-head " + oldSha, 120000, CancellationToken.None, true);
          if (!switched.Ok || !switched.Text.StartsWith("SWITCHED\t", StringComparison.Ordinal))
            failure = "Docker switch не выполнен: " + switched.Error;
        } else {
          // Require physical local .git directory, allowlisted origin and clean status.
          var fresh = new WfRepo { Name = r.Name, DockerOrigin = r.DockerOrigin,
            WindowsPath = r.WindowsPath };
          ProbeWindows(fresh, CancellationToken.None);
          var targetRef = fresh.Get(target);
          if (!fresh.WindowsOriginValid || fresh.WindowsActive != oldBranch ||
            fresh.WindowsHead != oldSha || fresh.WindowsChanges != 0 ||
            targetRef == null || targetRef.WindowsSha != branch.WindowsSha)
            failure = "Windows preflight: ветка, SHA или файлы изменились.";
          if (failure == null) {
            switched = Git(r.WindowsPath,
              "-c core.hooksPath=/dev/null switch --no-guess " + Q(target),
              120000, CancellationToken.None, true);
            if (!switched.Ok) failure = "Git switch Windows: " + switched.Error;
          }
        }
        string outcome = failure;
        Ui(delegate {
          EndWork(outcome == null ? "Switch " + side +
            " выполнен. Проверяем новое состояние..." :
            outcome + ". Если Git начал операцию, проверьте репозиторий вручную.");
          BeginCheck(false);
        });
      });
    }

    public static void Open(string root, string git) {
      Exception error = null;
      Thread window = new Thread(new ThreadStart(delegate() {
        try {
          Application.EnableVisualStyles();
          Application.SetCompatibleTextRenderingDefault(false);
          Application.Run(new WorkflowWindow(root, git));
        } catch (Exception ex) { error = ex; }
      }));
      window.SetApartmentState(ApartmentState.STA);
      window.Start(); window.Join();
      if (error != null) throw new InvalidOperationException("Ошибка окна workflow", error);
    }
  }
}

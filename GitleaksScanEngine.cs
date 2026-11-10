using System;
using System.Diagnostics;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Web.Script.Serialization;

namespace FortisRdgControl {
  internal sealed class GitleaksScanResult {
    internal int ExitCode = -1;
    internal int Findings = -1;
    internal string Report = "";
    internal string Error = "";
    internal bool IsClean;
    internal bool HasFindings;
  }

  // Never pass Gitleaks stdout/stderr to a log or UI, even on failure.
  internal static class GitleaksScanEngine {
    private const int FindingsExitCode = 3;
    private const long MaxJsonBytes = 32L * 1024 * 1024;

    private static void SafeLog(Action<string> log, string value) {
      if (log != null) log(value);
    }

    internal static string QuoteArg(string value) {
      var builder = new StringBuilder("\"");
      int slashes = 0;
      foreach (char c in value) {
        if (c == '\\') { slashes++; continue; }
        if (c == '"') {
          builder.Append('\\', slashes * 2 + 1);
          builder.Append('"');
          slashes = 0;
          continue;
        }
        builder.Append('\\', slashes);
        slashes = 0;
        builder.Append(c);
      }
      builder.Append('\\', slashes * 2);
      builder.Append('"');
      return builder.ToString();
    }

    private static bool IsInside(string candidate, string root) {
      string a = Path.GetFullPath(candidate).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
      string b = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
      return a.Equals(b, StringComparison.OrdinalIgnoreCase) ||
        a.StartsWith(b + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static void RejectReparseAncestors(string path) {
      string current = Path.GetFullPath(path);
      while (!string.IsNullOrEmpty(current) && Directory.Exists(current)) {
        if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
          throw new InvalidOperationException("Путь через ссылку/junction не поддерживается.");
        string parent = Path.GetDirectoryName(current.TrimEnd(Path.DirectorySeparatorChar));
        if (string.IsNullOrEmpty(parent) || parent == current) break;
        current = parent;
      }
    }

    private static void Discard(object sender, DataReceivedEventArgs e) {
      // Intentionally drop even --redact output: unknown errors can contain secrets.
    }

    private static int RunProcess(ProcessStartInfo info) {
      using (var process = new Process()) {
        process.StartInfo = info;
        process.OutputDataReceived += Discard;
        process.ErrorDataReceived += Discard;
        if (!process.Start()) throw new InvalidOperationException("Не удалось создать процесс.");
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        process.WaitForExit();
        return process.ExitCode;
      }
    }

    private static ProcessStartInfo LocalProcess(string exe, string args, string cwd) {
      return new ProcessStartInfo {
        FileName = exe, Arguments = args, WorkingDirectory = cwd,
        UseShellExecute = false, CreateNoWindow = true,
        RedirectStandardOutput = true, RedirectStandardError = true
      };
    }

    private static string FindGit(string folder) {
      var locations = new System.Collections.Generic.List<string>();
      string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
      if (!string.IsNullOrEmpty(programFiles)) locations.Add(Path.Combine(programFiles, @"Git\cmd"));
      string pathValue = Environment.GetEnvironmentVariable("PATH") ?? "";
      foreach (string item in pathValue.Split(Path.PathSeparator)) {
        string itemPath = item.Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(itemPath)) continue;
        try {
          string full = Path.GetFullPath(itemPath);
          if (IsInside(full, folder)) continue; // Never run a git.exe supplied by the scanned project.
          locations.Add(full);
        } catch { }
      }
      foreach (string dir in locations) {
        try {
          string candidate = Path.Combine(dir, "git.exe");
          if (File.Exists(candidate)) return candidate;
        } catch { }
      }
      throw new InvalidOperationException("git.exe не найден. Для Git-истории требуется локальный Git.");
    }

    private static bool IsGitRepository(string folder, string gitExe) {
      try {
        // Git's rev-parse is a local read; no fetch, pull, hooks or remote connections.
        return RunProcess(LocalProcess(gitExe, "-C " + QuoteArg(folder) +
          " rev-parse --show-toplevel", folder)) == 0;
      } catch { return false; }
    }

    private static string PrivateReportDirectory(string reportRoot) {
      var acl = new DirectorySecurity();
      acl.SetAccessRuleProtection(true, false);
      var owner = WindowsIdentity.GetCurrent().User;
      if (owner == null) throw new InvalidOperationException("Не удалось определить Windows-пользователя.");
      acl.SetOwner(owner);
      acl.AddAccessRule(new FileSystemAccessRule(owner,
        FileSystemRights.FullControl, InheritanceFlags.ContainerInherit |
        InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
      acl.AddAccessRule(new FileSystemAccessRule(
        new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
        FileSystemRights.FullControl, InheritanceFlags.ContainerInherit |
        InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
      string directory = Path.Combine(reportRoot, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") +
        "-" + Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(directory, acl);
      return directory;
    }

    private static int ReadFindings(string report) {
      if (!File.Exists(report)) throw new InvalidDataException("Отчёт не создан.");
      var bytes = new FileInfo(report).Length;
      if (bytes < 1 || bytes > MaxJsonBytes)
        throw new InvalidDataException("Размер JSON-отчёта некорректен.");
      // Gitleaks v8.30.1 may encode an empty nil slice as JSON null.
      var serializer = new JavaScriptSerializer { MaxJsonLength = (int)MaxJsonBytes };
      object value = serializer.DeserializeObject(File.ReadAllText(report, Encoding.UTF8));
      if (value == null) return 0;
      var findings = value as object[];
      if (findings == null) throw new InvalidDataException("Ожидался JSON-массив.");
      foreach (var item in findings)
        if (!(item is System.Collections.Generic.Dictionary<string, object>))
          throw new InvalidDataException("Некорректный элемент отчёта.");
      return findings.Length;
    }

    internal static GitleaksScanResult Run(string selectedFolder, bool gitMode,
      bool allRefs, Action<string> notify) {
      var result = new GitleaksScanResult();
      string reportDirectory = "";
      try {
        if (Environment.OSVersion.Platform != PlatformID.Win32NT)
          throw new InvalidOperationException("Требуется Windows.");
        if (string.IsNullOrWhiteSpace(selectedFolder))
          throw new InvalidOperationException("Выберите папку проекта.");
        string folder = Path.GetFullPath(selectedFolder);
        if (!Directory.Exists(folder))
          throw new InvalidOperationException("Папка проекта не существует.");
        RejectReparseAncestors(folder);
        string localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrEmpty(localApp))
          throw new InvalidOperationException("LOCALAPPDATA недоступна.");
        string exe = Path.Combine(localApp, @"Tools\Gitleaks\v8.30.1\gitleaks.exe");
        if (!File.Exists(exe))
          throw new InvalidOperationException("Gitleaks v8.30.1 не найден в %LOCALAPPDATA%\\Tools\\Gitleaks\\v8.30.1.");
        string gitExe = "";
        if (gitMode) {
          gitExe = FindGit(folder);
          if (!IsGitRepository(folder, gitExe))
            throw new InvalidOperationException("Git-репозиторий не найден. Выберите папку внутри Git-репозитория.");
        }
        string reports = Path.GetFullPath(Path.Combine(localApp, "GitleaksReports"));
        if (IsInside(reports, folder))
          throw new InvalidOperationException("Папка отчётов находится внутри проверяемого проекта. Выберите другой проект.");
        RejectReparseAncestors(localApp);
        Directory.CreateDirectory(reports);
        RejectReparseAncestors(reports);
        reportDirectory = PrivateReportDirectory(reports);
        string report = Path.Combine(reportDirectory, "findings.json");
        SafeLog(notify, "Локальная проверка началась. Вывод Gitleaks скрыт для защиты секретов.");
        SafeLog(notify, gitMode
          ? (allRefs ? "Режим: Git-история, все локально доступные ссылки." : "Режим: Git-история, текущая ветка.")
          : "Режим: текущие файлы (dir).");
        var args = new StringBuilder(gitMode ? "git" : "dir");
        args.Append(" --no-banner --redact=100 --log-level=error --exit-code=3");
        args.Append(" --report-format=json --report-path ").Append(QuoteArg(report));
        if (gitMode && !allRefs)
          args.Append(" --log-opts=").Append(QuoteArg("--full-history --diff-filter=tuxdb HEAD"));
        // No --log-opts when allRefs: Gitleaks 8.30.1 defaults to --full-history --all.
        args.Append(" ").Append(QuoteArg(folder));
        var info = LocalProcess(exe, args.ToString(), folder);
        info.EnvironmentVariables["GIT_TERMINAL_PROMPT"] = "0";
        if (gitMode) {
          // Gitleaks resolves git via PATH: use the exact executable validated above.
          string system = Environment.GetFolderPath(Environment.SpecialFolder.System);
          string windows = Environment.GetEnvironmentVariable("SystemRoot") ?? Path.GetDirectoryName(system);
          info.EnvironmentVariables["PATH"] = Path.GetDirectoryName(gitExe) +
            Path.PathSeparator + system + Path.PathSeparator + windows;
        }
        result.ExitCode = RunProcess(info);
        SafeLog(notify, "Проверка завершилась. Код выхода: " + result.ExitCode + ".");
        result.Report = File.Exists(report) ? report : "";
        int findings = ReadFindings(report);
        result.Findings = findings;
        if (result.ExitCode == 0 && findings == 0) {
          result.IsClean = true;
          // A no-findings report has no operational value. Don't accumulate it.
          try {
            File.Delete(report);
            Directory.Delete(reportDirectory);
            result.Report = "";
          } catch {
            SafeLog(notify, "Предупреждение: пустой отчёт не удалось удалить автоматически.");
          }
          SafeLog(notify, "Секреты не обнаружены");
        } else if (result.ExitCode == FindingsExitCode && findings > 0) {
          result.HasFindings = true;
          SafeLog(notify, "Потенциальных секретов: " + findings + ". JSON-отчёт сохранён локально.");
        } else {
          result.Error = "Проверка завершилась с ошибкой или некорректным результатом.";
          SafeLog(notify, "ОШИБКА: " + result.Error + " Отсутствие находок не подтверждено.");
        }
      } catch (InvalidOperationException ex) {
        // Only our own fixed, non-secret validation messages reach the UI.
        result.Error = ex.Message;
        SafeLog(notify, "ОШИБКА: " + result.Error);
      } catch (Exception ex) {
        result.Error = "Не удалось завершить проверку (" + ex.GetType().Name + ").";
        SafeLog(notify, "ОШИБКА: " + result.Error + " Отсутствие находок не подтверждено.");
      }
      if (result.Error.Length > 0 && reportDirectory.Length > 0) {
        string candidate = Path.Combine(reportDirectory, "findings.json");
        if (File.Exists(candidate)) result.Report = candidate;
      }
      return result;
    }
  }
}

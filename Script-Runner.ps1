#requires -Version 5.1
# Independent, non-interactive log collector for user-registered scripts. No Docker operations.
param(
    [Parameter(Mandatory=$true)][string]$RunFile,
    [Parameter(Mandatory=$true)][ValidatePattern('^[0-9a-fA-F-]{36}$')][string]$Token
)
$ErrorActionPreference = 'Stop'
$log = $null
try {
    $settings = New-Object System.Xml.XmlReaderSettings
    $settings.DtdProcessing = [System.Xml.DtdProcessing]::Prohibit
    $settings.XmlResolver = $null
    $reader = [System.Xml.XmlReader]::Create($RunFile, $settings)
    try {
        $doc = New-Object System.Xml.XmlDocument
        $doc.XmlResolver = $null
        $doc.Load($reader)
    } finally { $reader.Dispose() }
    $item = $doc.RunSpec
    if ($null -eq $item -or [string]$item.Token -cne $Token) { throw 'Invalid run specification or token' }
    $log = [string]$item.LogPath
    $exe = [string]$item.Executable
    $arguments = [string]$item.Arguments
    $workingDir = [string]$item.WorkingDirectory
    $stopFile = [string]$item.StopFile
    if (-not [IO.Path]::IsPathRooted($exe) -and $exe -notin @('cmd.exe','py.exe','python.exe')) {
        throw 'Unrecognized executable'
    }
    if (-not [IO.Directory]::Exists($workingDir)) { throw 'Working directory no longer exists' }
    $collector = @'
using System;
using System.IO;
using System.Text;
using System.Diagnostics;
namespace FortisScriptRunner {
  public static class Collector {
    private static readonly object Gate = new object();
    private static string target;
    private const int MaxBytes = 2097152;
    private const int KeepBytes = 1048576;
    private static void Log(string text) {
      lock (Gate) {
        string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + text + Environment.NewLine;
        using (var stream = new FileStream(target, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
        using (var writer = new StreamWriter(stream, new UTF8Encoding(false))) writer.Write(line);
        FileInfo fi = new FileInfo(target);
        if (fi.Length > MaxBytes) {
          byte[] tail = new byte[KeepBytes];
          using (var input = new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) {
            input.Seek(-KeepBytes, SeekOrigin.End);
            int total=0, n;
            while (total < tail.Length && (n=input.Read(tail,total,tail.Length-total))>0) total+=n;
          }
          using (var output = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
            output.Write(tail, 0, tail.Length);
        }
      }
    }
    private static void KillTree(Process child) {
      try {
        string taskkill=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"taskkill.exe");
        using(var killer=new Process()) {
          killer.StartInfo.FileName=taskkill;
          killer.StartInfo.Arguments="/PID "+child.Id+" /T /F";
          killer.StartInfo.UseShellExecute=false;
          killer.StartInfo.CreateNoWindow=true;
          if(!killer.Start()) throw new InvalidOperationException("taskkill did not start");
          if(!killer.WaitForExit(12000)) {
            try { killer.Kill(); } catch { }
            throw new TimeoutException("taskkill timed out");
          }
          if(killer.ExitCode!=0) throw new InvalidOperationException("taskkill exit code "+killer.ExitCode);
        }
      } catch(Exception ex) {
        Log("[STOP TREE FALLBACK] " + ex.Message);
        try { if(!child.HasExited) child.Kill(); } catch { }
      }
    }
    public static int Run(string fileName, string arguments, string directory, string logFile, string stopFile) {
      target=logFile;
      Log("[START] " + Path.GetFileName(fileName));
      using (var child = new Process()) {
        child.StartInfo.FileName=fileName;
        child.StartInfo.Arguments=arguments;
        child.StartInfo.WorkingDirectory=directory;
        child.StartInfo.UseShellExecute=false;
        child.StartInfo.CreateNoWindow=true;
        child.StartInfo.RedirectStandardOutput=true;
        child.StartInfo.RedirectStandardError=true;
        child.OutputDataReceived += (s,e) => { if(e.Data!=null) Log(e.Data); };
        child.ErrorDataReceived += (s,e) => { if(e.Data!=null) Log("[ERR] " + e.Data); };
        try {
          if (!child.Start()) throw new InvalidOperationException("Process.Start returned false");
          Log("[PID] " + child.Id);
          child.BeginOutputReadLine();
          child.BeginErrorReadLine();
          while (!child.WaitForExit(500)) {
            if (File.Exists(stopFile)) {
              Log("[STOP] Stop requested by Control Center; terminating the owned process tree");
              KillTree(child);
              child.WaitForExit();
              break;
            }
          }
          child.WaitForExit(); // finish async output callbacks
          Log("[EXIT] " + child.ExitCode);
          try { if(File.Exists(stopFile)) File.Delete(stopFile); } catch {}
          return child.ExitCode;
        } catch(Exception e) { Log("[ERROR] " + e.Message); return 1; }
      }
    }
    public static void Failure(string logfile, string message) {
      target=logfile; Log("[RUNNER ERROR] " + message);
    }
  }
}
'@
    Add-Type -TypeDefinition $collector -ReferencedAssemblies 'System.dll' -ErrorAction Stop | Out-Null
    exit ([FortisScriptRunner.Collector]::Run($exe, $arguments, $workingDir, $log, $stopFile))
} catch {
    if ($log) {
        try {
            [IO.File]::AppendAllText($log, ('[RUNNER ERROR] ' + $_.Exception.Message + [Environment]::NewLine), [Text.Encoding]::UTF8)
        } catch {}
    }
    exit 1
} finally {
    # The request is not needed for re-launch; it contains user-provided arguments.
    try { Remove-Item -LiteralPath $RunFile -Force -ErrorAction SilentlyContinue } catch {}
}

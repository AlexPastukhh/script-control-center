from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parent

class MirrorInspectorSourceChecks(unittest.TestCase):
    def test_launcher_opens_gui_by_default_and_preserves_cli(self):
        text = (ROOT / 'Fortis-Windows-Mirror-Status.ps1').read_text(encoding='utf-8-sig')
        self.assertIn('Mirror-Status.UI.cs', text)
        self.assertIn('[FortisMirror.MirrorWindow]::Open(', text)
        self.assertIn('if ($Cli)', text)
        self.assertIn('Mirror-Status.Report.ps1', text)
        self.assertTrue((ROOT / 'Fortis-Windows-Mirror-Status.ps1').read_bytes().startswith(b'\xef\xbb\xbf'))

    def test_ui_is_on_sta_thread_and_check_does_not_block_ui(self):
        ui = (ROOT / 'Mirror-Status.UI.cs').read_text(encoding='utf-8-sig')
        for token in ('Application.Run(new MirrorWindow(', 'SetApartmentState(ApartmentState.STA)',
                      'Directory.GetDirectories(root)', 'ThreadPool.QueueUserWorkItem',
                      'BeginInvoke(', 'BeginCheck(false)', 'BeginCheck(true)',
                      'CancellationTokenSource', 'proc.Kill()', 'WaitForExit(100)'):
            self.assertIn(token, ui)

    def test_git_is_read_only_and_remote_is_allowlisted(self):
        ui = (ROOT / 'Mirror-Status.UI.cs').read_text(encoding='utf-8-sig')
        for cmd in ('rev-parse --verify', 'symbolic-ref --quiet', 'status --porcelain',
                    'remote get-url origin', 'ls-remote --exit-code'):
            self.assertIn(cmd, ui)
        for forbidden in ('"pull ', '"push ', '"fetch ', '"checkout ', '"reset ', '"clean ', '"commit '):
            self.assertNotIn(forbidden, ui)
        self.assertIn('GIT_TERMINAL_PROMPT', ui)
        self.assertIn('GCM_INTERACTIVE', ui)
        self.assertIn('GIT_OPTIONAL_LOCKS', ui)
        self.assertIn('AlexPastukhh', ui)
        self.assertIn('timeoutMs', ui)

    def test_github_https_uses_verified_openssl_without_config_mutations(self):
        ui = (ROOT / 'Mirror-Status.UI.cs').read_text(encoding='utf-8-sig')
        report = (ROOT / 'Mirror-Status.Report.ps1').read_text(encoding='utf-8-sig')
        for text in (ui, report):
            self.assertIn('http.sslBackend=openssl', text)
            self.assertIn('http.sslVerify=true', text)
            self.assertNotIn('http.sslVerify=false', text)
            self.assertNotIn('sslVerify false', text)
            self.assertNotIn('config --global', text)
            self.assertNotIn('config --local', text)
        self.assertIn('bool https = origin.Output.StartsWith("https://github.com/"', ui)
        self.assertIn('bool forceOpenSsl = false', ui)
        self.assertIn('ClassifyGitError', ui)
        for reason in ('ОШИБКА TLS', 'ОШИБКА ДОСТУПА', 'ОШИБКА DNS', 'ОШИБКА СЕТИ'):
            self.assertIn(reason, ui)
        self.assertIn('dirty.Timeout ? "Таймаут"', ui)
        self.assertIn('if (local.Timeout || branch.Timeout)', ui)
        self.assertIn('raw credential/URL errors', ui)

    def test_original_cli_report_kept(self):
        report = (ROOT / 'Mirror-Status.Report.ps1').read_text(encoding='utf-8')
        self.assertIn('Need review:', report)
        self.assertIn('ls-remote', report)

if __name__ == '__main__':
    unittest.main()

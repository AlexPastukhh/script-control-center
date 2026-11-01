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
        for forbidden in ('"push ', '"fetch ', '"checkout ', '"reset ', '"clean ', '"commit '):
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

    def test_pull_is_single_repo_explicit_confirmed_and_fast_forward_only(self):
        ui = (ROOT / 'Mirror-Status.UI.cs').read_text(encoding='utf-8-sig')
        self.assertIn('pullOne.Click += delegate { BeginPull(); }', ui)
        self.assertIn('CheckResult expected = row.Last;', ui)
        self.assertIn('!expected.CanPull', ui)
        self.assertIn('MessageBoxButtons.YesNo', ui)
        self.assertIn('MessageBoxDefaultButton.Button2', ui)
        self.assertIn('CheckResult fresh = CheckRepository(row.Path, CancellationToken.None)', ui)
        for condition in ('!fresh.CanPull', 'fresh.LocalFull != expected.LocalFull',
                          'fresh.RemoteFull != expected.RemoteFull', 'fresh.Changes != "Нет"'):
            self.assertIn(condition, ui)
        self.assertIn('GitPullFastForward(row.Path, fresh.UseOpenSsl)', ui)
        self.assertIn('pull --ff-only --no-rebase --no-recurse-submodules origin main', ui)
        self.assertIn('-c core.hooksPath=/dev/null', ui)
        self.assertIn('-c submodule.recurse=false', ui)
        self.assertIn('psi.EnvironmentVariables["GIT_OPTIONAL_LOCKS"] = "1";', ui)
        self.assertIn('e.Cancel = true;', ui)
        self.assertIn('cancel.Enabled = running && !pulling;', ui)
        self.assertIn('result.Ok', ui)
        self.assertIn('НЕЗАКОНЧЕННАЯ ОПЕРАЦИЯ', ui)

    def test_pull_requires_clean_main_known_shas_not_failed_network(self):
        ui = (ROOT / 'Mirror-Status.UI.cs').read_text(encoding='utf-8-sig')
        for condition in ('State == "РАЗЛИЧАЮТСЯ"', 'Branch == "main"',
                          'Changes == "Нет"', 'Regex.IsMatch(LocalFull,',
                          'Regex.IsMatch(RemoteFull,'):
            self.assertIn(condition, ui)
        self.assertIn('local.Output.ToLowerInvariant()', ui)
        self.assertIn('data.RemoteFull = remoteHash', ui)
        self.assertIn('log -1 --format=%h%x09%as%x09%s', ui)
        self.assertIn('Последний локальный коммит:', ui)
        self.assertIn('list.Columns.Add("Последний локальный коммит", 310)', ui)
        self.assertIn('row.Item.SubItems[6].Text = result.Commit', ui)
        self.assertIn('row.Item.SubItems[6].Text = checkedState.Commit', ui)
        self.assertIn('GitHub main:', ui)
        self.assertIn('status --porcelain=v1 --untracked-files=all', ui)
        self.assertNotIn('status --porcelain=v1 --untracked-files=normal --ignore-submodules=all', ui)
        self.assertEqual(ui.count('pull --ff-only --no-rebase --no-recurse-submodules origin main'), 1)

    def test_original_cli_report_kept(self):
        report = (ROOT / 'Mirror-Status.Report.ps1').read_text(encoding='utf-8')
        self.assertIn('Need review:', report)
        self.assertIn('ls-remote', report)

if __name__ == '__main__':
    unittest.main()

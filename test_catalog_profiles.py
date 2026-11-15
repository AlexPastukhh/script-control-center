"""Source-only profile regression guard; WinForms runtime requires Windows acceptance."""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parent
SOURCE = (ROOT / "ScriptCatalog.cs").read_text(encoding="utf-8")


class ProfileSourceTests(unittest.TestCase):
    def test_profile_ui_and_manual_controls(self):
        for marker in ('AddButton(top, "Профили"', 'private void ManageProfiles()',
                       'Сохранить состав', 'Запустить профиль', 'Остановить профиль',
                       'RunProfile(list.SelectedItem as ScriptProfile,true,dialog)',
                       'RunProfile(list.SelectedItem as ScriptProfile,false,dialog)',
                       'MessageBoxButtons.YesNo'):
            self.assertIn(marker, SOURCE)
        self.assertNotIn('RunProfile(', SOURCE[SOURCE.index('private void LoadEntries()'):SOURCE.index('private bool TryLoadEntries(')])

    def test_profile_storage_preserves_registration_ids(self):
        for marker in ('profiles.xml', 'profile.ScriptIds.Add(scriptId)',
                       'SetAttr(xml,child,"id",scriptId)', 'File.Replace(temp,profilesPath,backup)',
                       'DtdProcessing=DtdProcessing.Prohibit', 'private bool TryLoadProfiles(',
                       'profile.ScriptIds.Count>=200'):
            self.assertIn(marker, SOURCE)
        self.assertNotIn('Process.Start(', SOURCE[SOURCE.index('private void SaveProfiles()'):SOURCE.index('private static string AskProfileName(')])

    def test_group_reuses_single_entry_lifecycle(self):
        block = SOURCE[SOURCE.index('private void RunProfile('):SOURCE.index('private static string Attr(', SOURCE.index('private void RunProfile('))]
        for marker in ('StartEntry(entry,true,report)', 'StopEntry(entry,true,report)',
                       'регистрация отсутствует', 'profile.ScriptIds', 'RefreshStatuses()'):
            self.assertIn(marker,block)
        start = SOURCE[SOURCE.index('private void StartEntry(RegisteredScript selected'):SOURCE.index('private void StopEntry()')]
        stop = SOURCE[SOURCE.index('private void StopEntry(RegisteredScript selected'):SOURCE.index('private void OpenLog()')]
        for marker in ('if(IsLive(e))', 'ScanExternalRuns(new List<RegisteredScript>{e})', 'if(!fromProfile && MessageBox.Show'):
            self.assertIn(marker,start)
        for marker in ('if(!IsLive(e))', 'ScanExternalRuns(new List<RegisteredScript>{e})',
                       'visible.Ambiguous', 'SameExternal(verified,final)',
                       'File.WriteAllText(marker,"stop"', 'KillProcessTree(pid[e.Id])'):
            self.assertIn(marker,stop)
        self.assertIn('ОСТАНОВКА ЗАПРОШЕНА',stop)

if __name__ == "__main__":
    unittest.main()

import hashlib
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

import updates


class MsiUpdateTest(unittest.TestCase):
    def setUp(self):
        temp = self.enterContext(tempfile.TemporaryDirectory())
        self.home = Path(temp)
        self.app = self.home / 'installed' / 'app'
        self.app.mkdir(parents=True)
        (self.app.parent / 'msi-install.txt').touch()
        (self.app.parent / 'version.txt').write_text('1.2.2', encoding='ascii')
        self.enterContext(patch.multiple(updates.core, HERE=self.app, HOME=self.home))
        self.payload = b'staged MSI bytes'
        self.tag = 'v1.2.3'
        self.slug = 'new-owner/SMC-Compass'
        self.url = f'https://github.com/{self.slug}/releases/download/{self.tag}/{updates.MSI_ASSET}'
        self.metadata = {'tag_name':self.tag, 'html_url':f'https://github.com/{self.slug}/releases/tag/{self.tag}',
                         'assets':[{'name':updates.MSI_ASSET,'state':'uploaded','browser_download_url':self.url,
                                     'digest':'sha256:'+hashlib.sha256(self.payload).hexdigest()}]}

    def test_installed_update_follows_the_renamed_repository(self):
        with patch.object(updates, 'download', side_effect=[json.dumps(self.metadata).encode(), self.payload]) as download:
            result = updates.install('1.2.3')
        self.assertEqual(download.call_args_list[0].args,
                         (f'https://api.github.com/repositories/{updates.REPOSITORY_ID}/releases/latest',1_000_000,True))
        self.assertEqual(download.call_args_list[1].args,(self.url,updates.MAX_DOWNLOAD))
        installer = Path(result['msi'])
        self.assertEqual(installer.read_bytes(), self.payload)
        self.assertEqual(list(installer.parent.iterdir()), [installer])
        self.assertEqual(updates.current(), '1.2.2')

    def test_foreign_asset_url_is_rejected_before_download(self):
        self.metadata['assets'][0]['browser_download_url'] = self.url.replace(self.slug,'another-owner/OtherRepo')
        with patch.object(updates, 'download', return_value=json.dumps(self.metadata).encode()) as download:
            with self.assertRaisesRegex(ValueError, 'hiányzik'):
                updates.install('1.2.3')
        self.assertEqual(download.call_count,1)

    def test_missing_package_is_not_offered_for_installation(self):
        self.metadata['assets'].clear()
        with patch.object(updates, 'download', return_value=json.dumps(self.metadata).encode()):
            check = updates.check()
        self.assertFalse(check['available'])
        self.assertTrue(check['missingPackage'])

    def test_digest_mismatch_keeps_the_previous_staged_file(self):
        staged = self.home / 'updates' / updates.MSI_ASSET
        staged.parent.mkdir()
        staged.write_bytes(b'previous installer')
        with patch.object(updates, 'download', side_effect=[json.dumps(self.metadata).encode(), b'damaged download']):
            with self.assertRaisesRegex(ValueError, 'lenyomata eltér'):
                updates.install('1.2.3')
        self.assertEqual(staged.read_bytes(), b'previous installer')


if __name__ == '__main__':
    unittest.main()

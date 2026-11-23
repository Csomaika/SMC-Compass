import hashlib
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
        self.url = f'https://github.com/{updates.REPO}/releases/download/v1.2.3/{updates.MSI_ASSET}'
        self.release = {'version': '1.2.3', 'notes': '', 'asset': {
            'browser_download_url': self.url,
            'digest': 'sha256:' + hashlib.sha256(self.payload).hexdigest(),
        }}

    def test_installed_update_stages_the_verified_msi(self):
        with patch.object(updates, 'release', return_value=self.release), \
             patch.object(updates, 'download', return_value=self.payload) as download:
            result = updates.install('1.2.3')
        download.assert_called_once_with(self.url, updates.MAX_DOWNLOAD)
        installer = Path(result['msi'])
        self.assertEqual(installer.read_bytes(), self.payload)
        self.assertEqual(list(installer.parent.iterdir()), [installer])
        self.assertEqual(updates.current(), '1.2.2')

    def test_digest_mismatch_keeps_the_previous_staged_file(self):
        staged = self.home / 'updates' / updates.MSI_ASSET
        staged.parent.mkdir()
        staged.write_bytes(b'previous installer')
        with patch.object(updates, 'release', return_value=self.release), \
             patch.object(updates, 'download', return_value=b'damaged download'):
            with self.assertRaisesRegex(ValueError, 'lenyomata eltér'):
                updates.install('1.2.3')
        self.assertEqual(staged.read_bytes(), b'previous installer')


if __name__ == '__main__':
    unittest.main()

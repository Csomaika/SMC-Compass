import json
from copy import deepcopy
from unittest import TestCase
from unittest.mock import patch
from urllib.parse import parse_qs, urlsplit

import app
import desktop


class SugarConnectionTest(TestCase):
    def setUp(self):
        self.state = deepcopy(app.STATE)
        self.state['config'] = app.DEFAULT | {
            'sugarUrl': 'https://crm.example.test/#Home/overview',
            'salesSource': 'browser',
            'buyersSource': 'manual',
        }
        self.state['status'] = {}
        self.state['opportunities'] = [{'id': 'cached'}]
        for change in (patch.object(app, 'STATE', self.state),
                       patch.object(app, 'SESSION', {'sp': {}, 'sugar': '', 'sugarRefresh': '', 'sugarExpires': 0}),
                       patch.object(app, 'save')):
            change.start()
            self.addCleanup(change.stop)

    def test_web_address_and_api_address_are_separate(self):
        page = 'https://crm.example.test/sugar/index.php?module=Home#Home/demo'
        desktop.configure({'sugarUrl': page})
        self.assertEqual(desktop.dispatch('sugar-web', {})['url'],
                         'https://crm.example.test/sugar/index.php#Home/demo')
        self.assertEqual(app.sugar_rest(self.state['config']), 'https://crm.example.test/sugar/rest/v11')
        with self.assertRaisesRegex(ValueError, 'HTTPS'):
            desktop.configure({'sugarUrl': 'https://operator:password@crm.example.test/'})

    def test_login_filter_pagination_and_refresh(self):
        requests = []

        def send(url, fields=None, headers=None):
            requests.append((url, fields, headers))
            if url.endswith('/oauth2/token'):
                suffix = '1' if fields['grant_type'] == 'password' else '2'
                return json.dumps({'access_token': 'access'+suffix, 'refresh_token': 'refresh'+suffix, 'expires_in': 3600}).encode()
            query = parse_qs(urlsplit(url).query)
            self.assertEqual(query['filter[0][$owner]'], ['_this'])
            offset = int(query['offset'][0])
            return json.dumps({'records': [{'id': str(offset)}], 'next_offset': 100 if offset==0 else -1}).encode()

        with patch.object(app, 'http', side_effect=send):
            desktop.dispatch('sugar', {'username': 'fixture-user', 'password': 'fixture-password'})
            self.assertEqual(requests[0][0], 'https://crm.example.test/rest/v11/oauth2/token')
            self.assertEqual(requests[0][1]['platform'], 'smc_compass')
            self.assertEqual(requests[1][2], {'OAuth-Token': 'access1'})
            self.assertEqual([row['id'] for row in self.state['opportunities']], ['0','100'])
            self.assertNotIn('fixture-password', json.dumps(app.snapshot()))
            self.assertNotIn('refresh1', json.dumps(app.snapshot()))
            app.SESSION['sugarExpires'] = 1
            app.refresh(force=True)
            self.assertEqual(requests[3][1]['grant_type'], 'refresh_token')
            self.assertEqual(requests[3][1]['refresh_token'], 'refresh1')
            self.assertEqual(requests[4][2], {'OAuth-Token': 'access2'})

    def test_expired_manual_token_preserves_previous_import(self):
        with patch.object(app, 'http', side_effect=app.HttpStatusError(401)):
            with self.assertRaisesRegex(ValueError, 'lejárt vagy érvénytelen'):
                desktop.dispatch('sugar', {'token': 'old-token'})
        self.assertEqual(self.state['opportunities'], [{'id': 'cached'}])
        self.assertFalse(self.state['status']['sugar']['ok'])
        self.assertFalse(app.snapshot()['sugarConnected'])
        self.assertNotIn('old-token', json.dumps(self.state))

    def test_browser_redirect_and_shared_platform_rejected(self):
        with patch.object(app, 'http', side_effect=app.HttpStatusError(302)):
            with self.assertRaisesRegex(ValueError, 'webes belépésre'):
                desktop.dispatch('sugar', {'token': 'fixture-token'})
        desktop.configure({'sugarPlatform': 'base'})
        with patch.object(app, 'http') as request:
            with self.assertRaisesRegex(ValueError, 'külön API-platform'):
                desktop.dispatch('sugar', {'username': 'user', 'password': 'password'})
            request.assert_not_called()

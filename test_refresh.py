import json
import os
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

import app
import desktop
from workbook import excel_bytes, M


class WorkbookRefreshTest(unittest.TestCase):
    @staticmethod
    def workbook(actual):
        months = [10, 220, 420, 620, 820, 1030, 1232, actual, None, None, None, None]
        rows = [[None]*5+M, [None]*3+['# of sold Electric Actuators', 'TARGET']+[200]*12,
                [None]*4+['ACTUAL']+months]
        # Put the workbook data on the second sheet, away from the generated chart.
        return excel_bytes([('Forecast', [['Month','Actual','Target','Forecast'],['APR',0,1,1]]),
                            ('REPORT26', rows)], 1)

    def test_saved_sales_change_updates_forecast_and_survives_offline(self):
        with tempfile.TemporaryDirectory() as folder:
            home = Path(folder)
            path = home/'Sales.xlsx'
            state = {'config': app.DEFAULT | dict(source='sharepoint', salesSource='local', salesPath=str(path)),
                     'status': {}, 'series': {}, 'salesSheets': [], 'buyers': {}, 'manual': {}}
            with patch.multiple(app, HOME=home, FILE=home/'compass.json', STATE=state,
                                CHECKS={}, LOCAL_CHECKS={}), patch.object(app,'http') as http:
                path.write_bytes(self.workbook(1400))
                self.assertTrue(desktop.dispatch('refresh', {'localOnly': True})['changed'])
                self.assertEqual(app.scenario(2026, app.SALES_KEY)['actual'][7], 1400)
                self.assertFalse(desktop.dispatch('refresh', {'localOnly': True})['changed'])
                updated = path.with_name('new.xlsx')
                updated.write_bytes(self.workbook(1500))
                os.replace(updated,path)
                self.assertTrue(desktop.dispatch('refresh', {'localOnly': True})['changed'])
                fresh = app.scenario(2026, app.SALES_KEY)
                self.assertEqual(fresh['actual'][7],1500)
                self.assertEqual(fresh['monthly'][7],268)
                self.assertIn('1500',app.FILE.read_text(encoding='utf-8'))
                path.unlink()
                self.assertTrue(app.refresh(local_only=True))
                self.assertFalse(state['status']['sales']['ok'])
                self.assertEqual(app.scenario(2026, app.SALES_KEY)['actual'][7],1500)
                self.assertNotIn('buyers',state['status'])
                http.assert_not_called()

    def test_startup_and_periodic_refresh_use_inherited_sharepoint_url(self):
        url = 'https://contoso.sharepoint.com/sites/Sales/_layouts/15/Doc2.aspx?sourcedoc={12345678-1234-1234-1234-123456789abc}'
        with tempfile.TemporaryDirectory() as temp:
            file = Path(temp) / 'compass.json'
            for source, path, should_download in [('', '', True), ('browser', '', True),
                                                   ('manual', '', False), ('local', '', False),
                                                   ('', 'Sales.xlsx', False)]:
                with self.subTest(source=source, path=path):
                    file.write_text(json.dumps({'config': {'source':'local','salesSource':source,
                                    'salesUrl':url,'salesPath':path}}), encoding='utf-8')
                    with patch.multiple(app, FILE=file, STATE={}), patch.object(app,'refresh',return_value=False):
                        app.load()
                        self.assertEqual(desktop.dispatch('refresh', {})['browserSales'],should_download)
                        self.assertFalse(desktop.dispatch('refresh', {'localOnly':True})['browserSales'])

    def test_browser_sales_imports_only_the_new_download(self):
        with tempfile.TemporaryDirectory() as folder:
            home = Path(folder)
            path = home / 'Sales.xlsx'
            guid = '12345678-1234-1234-1234-123456789abc'
            site = 'https://contoso.sharepoint.com/sites/Sales/_layouts/15/'
            cfg = app.DEFAULT | dict(salesUrl=site+'Doc2.aspx?sourcedoc=%7B'+guid+'%7D',
                                     salesSource='local', salesPath=str(path), buyersSource='manual')
            state = {'config': cfg, 'status': {}, 'series': {}, 'salesSheets': [], 'buyers': {}, 'manual': {}}
            with patch.multiple(app, HOME=home, FILE=home/'compass.json', STATE=state,
                                CHECKS={}, LOCAL_CHECKS={}), patch.object(app,'http') as http, \
                 patch.object(desktop, 'BROWSER_SALES', {}):
                path.write_bytes(self.workbook(1400))
                app.refresh(local_only=True)
                start = desktop.dispatch('browser-sales-start', {'downloads': str(home)})
                self.assertEqual(start['url'], site+'download.aspx?UniqueId='+guid)
                (home / 'Other.xlsx').write_bytes(self.workbook(1600))
                partial = home / 'Sales (1).xlsx.crdownload'
                partial.write_bytes(self.workbook(1500))
                for _ in range(2):
                    self.assertEqual(desktop.dispatch('browser-sales-poll', {})['result'], 'waiting')
                self.assertEqual(app.scenario(2026, app.SALES_KEY)['actual'][7], 1400)
                partial.rename(home / 'Sales (1).xlsx')
                self.assertEqual(desktop.dispatch('browser-sales-poll', {})['result'], 'waiting')
                self.assertEqual(desktop.dispatch('browser-sales-poll', {}),
                                 {'result': 'imported', 'name': 'Sales (1).xlsx'})
                self.assertEqual(app.scenario(2026, app.SALES_KEY)['actual'][7], 1500)
                self.assertEqual(state['config']['salesSource'], 'browser')
                self.assertEqual(state['config']['salesDownloadName'], 'Sales.xlsx')
                self.assertEqual(state['config']['buyersSource'], 'manual')
                self.assertFalse(app.refresh(force=True))
                self.assertEqual(app.scenario(2026, app.SALES_KEY)['actual'][7], 1500)
                http.assert_not_called()


if __name__ == '__main__':
    unittest.main()

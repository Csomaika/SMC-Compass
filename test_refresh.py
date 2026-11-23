import os
import io
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
from zipfile import ZipFile

import app
import desktop
from workbook import excel_bytes, M, S, Workbook


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
                 patch.object(desktop, 'BROWSER_DOWNLOAD', {}):
                path.write_bytes(self.workbook(1400))
                app.refresh(local_only=True)
                start = desktop.dispatch('browser-download-start', {'kind': 'sales', 'downloads': str(home)})
                self.assertEqual(start['url'], site+'download.aspx?UniqueId='+guid)
                (home / 'Other.xlsx').write_bytes(self.workbook(1600))
                partial = home / 'Sales (1).xlsx.crdownload'
                partial.write_bytes(self.workbook(1500))
                for _ in range(2):
                    self.assertEqual(desktop.dispatch('browser-download-poll', {})['result'], 'waiting')
                self.assertEqual(app.scenario(2026, app.SALES_KEY)['actual'][7], 1400)
                partial.rename(home / 'Sales (1).xlsx')
                self.assertEqual(desktop.dispatch('browser-download-poll', {})['result'], 'waiting')
                self.assertEqual(desktop.dispatch('browser-download-poll', {}),
                                 {'result': 'imported', 'name': 'Sales (1).xlsx'})
                self.assertEqual(app.scenario(2026, app.SALES_KEY)['actual'][7], 1500)
                self.assertEqual(state['config']['salesSource'], 'browser')
                self.assertEqual(state['config']['salesDownloadName'], 'Sales.xlsx')
                self.assertEqual(state['config']['buyersSource'], 'manual')
                self.assertFalse(app.refresh(force=True))
                self.assertEqual(app.scenario(2026, app.SALES_KEY)['actual'][7], 1500)
                http.assert_not_called()

    @staticmethod
    def buyers_workbook():
        rows = [['Customer','Name','Potential','SMC position'], ['V01','Első vevő',3,2], ['V02','Második vevő',3,2]]
        source = excel_bytes([('Forecast', [['Month','Actual','Target','Forecast'],['APR',0,1,1]]), ('Munka1', rows)], 1)
        output = io.BytesIO()
        with ZipFile(io.BytesIO(source)) as old, ZipFile(output,'w') as new:
            for name in old.namelist():
                content = old.read(name)
                if name=='xl/worksheets/sheet2.xml':
                    content = content.replace(b'<row r="3">', b'<row r="3" hidden="1">').replace(
                        b'<autoFilter ref="A1:D3"/>', b'<autoFilter ref="A1:D3"><filterColumn colId="1"><filters>'
                        b'<filter val="Elso"/></filters></filterColumn></autoFilter>')
                new.writestr(name,content)
        return output.getvalue()

    def test_browser_speedup_preserves_saved_filter_and_uses_link_filename(self):
        with tempfile.TemporaryDirectory() as folder:
            home = Path(folder)
            link = 'https://contoso.sharepoint.com/sites/Sales/Doc.aspx?sourcedoc=12345678-1234-1234-1234-123456789abc&file=Vev%C5%91k.xlsx'
            state = {'config': app.DEFAULT | dict(buyersUrl=link, salesSource='manual'), 'buyers': {},
                     'series': {}, 'manual': {}, 'status': {}, 'scores': {}, 'opportunities': []}
            with patch.multiple(app, HOME=home, FILE=home/'compass.json', STATE=state, CHECKS={}, LOCAL_CHECKS={}), \
                 patch.object(desktop, 'BROWSER_DOWNLOAD', {}):
                start = desktop.dispatch('browser-download-start', {'kind': 'buyers', 'downloads': str(home)})
                self.assertTrue(start['url'].endswith('download.aspx?UniqueId=12345678-1234-1234-1234-123456789abc'))
                path = home/'Vevők (2).xlsx'
                path.write_bytes(self.buyers_workbook())
                self.assertEqual(desktop.dispatch('browser-download-poll', {})['result'], 'waiting')
                self.assertEqual(desktop.dispatch('browser-download-poll', {})['result'], 'imported')
                self.assertEqual(state['config']['buyersSource'], 'browser')
                self.assertEqual(state['config']['salesSource'], 'manual')
                self.assertTrue(state['buyers']['filters'][0]['criteria'])
                self.assertEqual([row['hidden'] for row in state['buyers']['items']], [False, True])
                for visible, count in [('1','1'),('0','2')]:
                    with Workbook(io.BytesIO(app.export({'visible':visible}))) as book:
                        self.assertEqual(book.sheet('9-box')[0][2][0]['C'], count)

    def test_export_contains_editable_inputs_chart_references_and_cached_results(self):
        with tempfile.TemporaryDirectory() as folder:
            home = Path(folder)
            state = {'config': app.DEFAULT.copy(), 'buyers': {}, 'series': {}, 'manual': {},
                     'status': {}, 'scores': {}, 'opportunities': []}
            with patch.multiple(app, HOME=home, FILE=home/'compass.json', STATE=state, CHECKS={}, LOCAL_CHECKS={}):
                app.ingest('sales', self.workbook(1500), state['config'], 'Teszt')
                desktop.manual({'action':'save','month':8,'amount':0,'note':'=A1'})
                with Workbook(io.BytesIO(app.export({}))) as book:
                    forecast = book.xml(book.sheets['Forecast'])
                    settings = book.xml(book.sheets['Források'])
                    actual = forecast.find(f'.//{S}c[@r="B10"]')
                    self.assertIn('H10',actual.findtext(S+'f'))
                    self.assertEqual(float(actual.findtext(S+'v')),1500)
                    self.assertEqual(forecast.find(f'.//{S}c[@r="I10"]').findtext(S+'v'),'0.0')
                    note = forecast.find(f'.//{S}c[@r="N10"]')
                    self.assertEqual(note.get('t'),'inlineStr')
                    self.assertEqual(note.findtext(f'{S}is/{S}t'),'=A1')
                    self.assertEqual(settings.find(f'.//{S}c[@r="B12"]').findtext(S+'v'),'9')
                    self.assertEqual(book.root.find(S+'calcPr').get('calcMode'),'auto')
                    self.assertIn(b"'Forecast'!$D$2:$D$13",book.zip.read('xl/charts/chart1.xml'))


if __name__ == '__main__':
    unittest.main()

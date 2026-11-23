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


if __name__ == '__main__':
    unittest.main()

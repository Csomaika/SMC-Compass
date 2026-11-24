from pathlib import Path
from tempfile import TemporaryDirectory
from unittest import TestCase
from unittest.mock import patch

import app
import desktop
from sugar_csv import opportunities_csv


class SugarCsvTest(TestCase):
    def test_original_sugar_export(self):
        raw = ('\ufeffID,Name,Account Name,Sales Stage,Amount,Probability,Currency ISO,Expected Close Date,Assigned to\n'
               'one,"Építés, 1. ütem",Minta Kft.,Proposal/Price Quote,"100,000.25",50,HUF,2026-12-21,User\n'
               'two,Redesign,Example Ltd.,Closed Lost,250.20,0,EUR,2026-12-23,User\n')
        first, second = opportunities_csv(raw.encode('utf-8'))
        self.assertEqual((first['name'], first['account_name'], first['amount'], first['currency_iso'], first['probability']),
                         ('Építés, 1. ütem', 'Minta Kft.', 100000.25, 'HUF', 50))
        self.assertEqual((second['currency_iso'], second['amount'], second['date_closed']), ('EUR', 250.20, '2026-12-23'))

    def test_local_delimiter_decimal_and_unknown_currency(self):
        data = ('"Lehetőség neve";"Státusz";"Összeg";"Valószínűség";"Pénznem"\r\n'
                '"Őrlő";"Ajánlat";"125 000,20 Ft";"40%";""\r\n'
                '"Új projekt";"Ajánlat";"1.250,15";"25,5%";""\r\n')
        first, second = opportunities_csv(data.encode('cp1250'))
        self.assertEqual((first['amount'], first['currency_iso']), (125000.20, 'HUF'))
        self.assertEqual((second['amount'], second['currency_iso'], second['probability']), (1250.15, '', 25.5))

    def test_import_is_a_local_snapshot_and_keeps_previous_data_on_failure(self):
        state = dict(config=app.DEFAULT | {'salesSource':'manual','buyersSource':'manual'}, status={},
                     opportunities=[{'id':'old'}])
        session = dict(sp={}, sugar='fixture', sugarRefresh='fixture_refresh', sugarExpires=1000)
        with TemporaryDirectory() as folder, patch.multiple(app, STATE=state, SESSION=session), patch.object(app,'save'), patch.object(app,'http') as http:
            path = Path(folder)/'Opportunities.csv'
            path.write_bytes(b'ID,Name,Sales Stage,Amount\nop-one,Equipment,Proposal,100\n')
            result = desktop.dispatch('sugar-csv', {'path':str(path)})
            self.assertEqual(result['count'], 1)
            self.assertEqual(state['opportunities'][0]['id'], 'op-one')
            self.assertEqual(state['status']['sugar']['mode'], 'Sugar CSV')
            self.assertEqual(session['sugar'], '')
            app.refresh(force=True)
            http.assert_not_called()
            path.write_bytes(b'ID,Name,Industry\naccount,Example,Hardware\n')
            with self.assertRaisesRegex(ValueError, 'Opportunities CSV'):
                desktop.dispatch('sugar-csv', {'path':str(path)})
            self.assertEqual(state['opportunities'][0]['id'], 'op-one')

    def test_row_validation(self):
        with self.assertRaisesRegex(ValueError, 'oszlopok száma'):
            opportunities_csv(b'Name,Sales Stage,Amount\nExample,Proposal\n')

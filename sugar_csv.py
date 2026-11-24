"""Read a Sugar Opportunities CSV export into Compass pipeline records."""
import csv
import io
import re
import unicodedata

from workbook import number


COLUMNS = {
    'id': ('id', 'record id', 'opportunity id', 'azonosító'),
    'name': ('name', 'opportunity name', 'opportunity', 'lehetőség neve', 'lehetőség', 'név'),
    'account_name': ('account name', 'account', 'customer name', 'vevő', 'ügyfélnév', 'ügyfél'),
    'sales_stage': ('sales stage', 'stage', 'értékesítési szakasz', 'státusz'),
    'amount': ('amount', 'opportunity amount', 'likely', 'összeg', 'várható összeg'),
    'probability': ('probability', 'probability %', 'win probability', 'valószínűség'),
    'date_closed': ('date closed', 'expected close date', 'expected close', 'close date', 'zárás dátuma', 'zárás'),
    'assigned_user_name': ('assigned user name', 'assigned to', 'assigned user', 'sales rep', 'felelős'),
    'currency_iso': ('currency iso', 'currency iso code', 'currency code', 'currency', 'pénznem', 'deviza'),
}


def heading(value):
    normalized = unicodedata.normalize('NFKD', value.lower())
    return ' '.join(re.findall(r'[a-z0-9]+', ''.join(c for c in normalized if not unicodedata.combining(c))))


def read_header(content):
    for separator in (',', ';', '\t'):
        reader = csv.reader(io.StringIO(content, newline=''), delimiter=separator, strict=True)
        try:
            header = next((row for row in reader if any(cell.strip() for cell in row)), [])
        except csv.Error:
            continue
        positions = {heading(cell): index for index, cell in enumerate(header)}
        columns = {field: next((positions[key] for name in choices if (key := heading(name)) in positions), None)
                   for field, choices in COLUMNS.items()}
        if columns['name'] is not None and any(columns[k] is not None for k in ('sales_stage','probability','date_closed')):
            return reader, header, columns, separator
    raise ValueError('Sugar Opportunities CSV kell, névvel és legalább egy szakasz-, valószínűség- vagy zárásoszloppal.')


def decimal(text, separator):
    text = re.sub(r'[\s%]', '', text)
    if ',' in text and '.' in text:
        grouping = ',' if text.rfind('.') > text.rfind(',') else '.'
        text = text.replace(grouping, '')
    elif re.fullmatch(r'[+-]?\d{1,3}(?:[.,]\d{3})+', text):
        grouping = '.' if separator==';' else ','
        text = text.replace(grouping, '')
    return number(text)


def currency_code(text):
    label = text.strip().upper()
    if re.fullmatch(r'[A-Z]{3}', label):
        return label
    return {'FT':'HUF', 'FORINT':'HUF', 'HUNGARIAN FORINT':'HUF', 'EURO':'EUR',
            '€':'EUR', 'US DOLLAR':'USD', 'US$':'USD', 'POUND STERLING':'GBP', '£':'GBP'}.get(label, '')


def opportunities_csv(blob):
    if blob.startswith((b'\xff\xfe', b'\xfe\xff')):
        content = blob.decode('utf-16')
    else:
        try:
            content = blob.decode('utf-8-sig')
        except UnicodeDecodeError:
            content = blob.decode('cp1250')
    records = {}
    try:
        reader, header, columns, separator = read_header(content)
        for row in reader:
            if not any(cell.strip() for cell in row):
                continue
            if len(row) != len(header):
                raise ValueError(f'A CSV {reader.line_num}. során eltér az oszlopok száma a fejlécétől.')
            record = {field: row[index].strip() if index is not None else '' for field, index in columns.items()}
            if not record['name']:
                raise ValueError(f'A CSV {reader.line_num}. során hiányzik a lehetőség neve.')
            record['id'] = record['id'] or f'csv-{reader.line_num}'
            # Keep an explicit currency suffix such as "100 000 Ft".
            trailing = re.fullmatch(r'(.+\d)\s*(Ft|HUF|EUR|USD|GBP|€|£)', record['amount'], re.I)
            if trailing:
                amount, currency = trailing.groups()
            else:
                amount, currency = record['amount'], ''
            record['currency_iso'] = currency_code(record['currency_iso']) or currency_code(currency)
            record['amount'] = decimal(amount, separator)
            record['probability'] = decimal(record['probability'], separator)
            records[record['id']] = record
    except csv.Error:
        raise ValueError('A Sugar CSV idézőjelezése vagy mezőszerkezete hibás. Exportáld újra az Opportunities listát.') from None
    if not records:
        raise ValueError('A Sugar CSV-ben nincs betölthető lehetőség.')
    return list(records.values())

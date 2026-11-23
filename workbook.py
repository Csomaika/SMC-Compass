"""Read saved OOXML data and write a portable, charted Excel workbook."""
from dataclasses import dataclass
import io
import math
import posixpath
import re
import unicodedata
import zipfile
from xml.etree import ElementTree as ET
from xml.sax.saxutils import escape, quoteattr

S = '{http://schemas.openxmlformats.org/spreadsheetml/2006/main}'
R = 'http://schemas.openxmlformats.org/officeDocument/2006/relationships'
P = 'http://schemas.openxmlformats.org/package/2006/relationships'
M = list('APR MAY JUN JUL AUG SEP OCT NOV DEC JAN FEB MAR'.split())


def normal(value):
    return ''.join(c for c in unicodedata.normalize('NFKD', str(value or '')).lower() if c.isalnum() and not unicodedata.combining(c))


def number(value):
    try:
        result = float(str(value).replace('\u00a0', '').replace(' ', '').replace(',', '.'))
        return result if math.isfinite(result) else None
    except (TypeError, ValueError):
        return None


def column(index):
    label = ''
    while index:
        index, remainder = divmod(index - 1, 26)
        label = chr(65 + remainder) + label
    return label


class Workbook:
    def __init__(self, source):
        self.zip = zipfile.ZipFile(source)

    def __enter__(self):
        self.root = self.xml('xl/workbook.xml')
        self.sheets = {s.get('name'): self.relations('xl/workbook.xml')[s.get('{'+R+'}id')]
                       for s in self.root.find(S+'sheets')}
        self.strings = []
        if 'xl/sharedStrings.xml' in self.zip.namelist():
            for item in self.xml('xl/sharedStrings.xml'):
                self.strings.append(''.join(t.text or '' for t in item.iter(S+'t')))
        return self

    def __exit__(self, *args):
        self.zip.close()

    def xml(self, name):
        with self.zip.open(name) as file:
            return ET.parse(file).getroot()

    def relations(self, path):
        directory, filename = posixpath.split(path)
        key = posixpath.join(directory, '_rels', filename+'.rels')
        if key not in self.zip.namelist():
            return {}
        return {x.get('Id'): posixpath.normpath(x.get('Target').lstrip('/') if x.get('Target').startswith('/')
                else posixpath.join(directory, x.get('Target'))) for x in self.xml(key) if x.get('TargetMode') != 'External'}

    def sheet(self, name):
        if name not in self.sheets:
            raise ValueError('Munkalap nem található: '+name+'. Elérhető: '+', '.join(self.sheets))
        path = self.sheets[name]
        rows, filters, parts = {}, [], []
        saved_views = False
        with self.zip.open(path) as file:
            data = None
            for event, obj in ET.iterparse(file, events=('start', 'end')):
                if event == 'start':
                    if obj.tag == S+'sheetData':
                        data = obj
                    continue
                if obj.tag == S+'row':
                    values = {}
                    for cell in obj:
                        if cell.tag != S+'c':
                            continue
                        kind, value = cell.get('t'), cell.findtext(S+'v')
                        if kind == 's' and value:
                            value = self.strings[int(value)]
                        elif kind == 'inlineStr':
                            value = ''.join(t.text or '' for t in cell.iter(S+'t'))
                        elif kind == 'e':
                            value = None
                        if value is not None and str(value).strip():
                            values[re.sub(r'\d', '', cell.get('r', ''))] = value
                    if values:
                        rows[int(obj.get('r'))] = (values, obj.get('hidden') in ('1', 'true'))
                    data.remove(obj)
                elif obj.tag == S+'autoFilter':
                    filters.append(self.filter_info(obj, 'Munkalap'))
                elif obj.tag == S+'tablePart':
                    parts.append(obj.get('{'+R+'}id'))
        relations = self.relations(path)
        for part in parts:
            table = self.xml(relations[part]); auto = table.find(S+'autoFilter')
            if auto is not None:
                filters.append(self.filter_info(auto, table.get('displayName', 'Táblázat')))
        for related in relations.values():
            if 'namedSheetView' in related:
                saved_views = True
        return rows, filters, saved_views

    @staticmethod
    def filter_info(element, target):
        active = []
        for item in element.findall(S+'filterColumn'):
            rules = [part.tag.split('}')[-1] for part in item]
            if rules:
                active.append({'column': int(item.get('colId', '0'))+1, 'rules': rules})
        return {'where': target, 'range': element.get('ref', ''), 'criteria': active}


def sales(source, sheet, start_year, previous):
    with Workbook(source) as book:
        names = list(book.sheets)
        series = {}
        for name, year in [(sheet, start_year), (previous, start_year-1)]:
            if not name or (name == sheet and year != start_year) or (year != start_year and name not in book.sheets):
                continue
            rows, filters, _ = book.sheet(name)
            months, prefix = [], 4
            for row, (values, _) in rows.items():
                for key in sorted(values, key=lambda k: (len(k), k)):
                    if str(values[key]).upper().strip() == M[0]:
                        idx = 0
                        for char in key:
                            idx = idx*26 + ord(char)-64
                        cols = [column(idx+j) for j in range(12)]
                        if [str(values.get(c,'')).upper().strip() for c in cols] == M:
                            months = cols
                            prefix = row
                            break
                if months:
                    break
            if not months:
                raise ValueError(name+': nem találtam az APR–MAR fejlécet.')
            items = {}
            lookup = {'ofsoldelectricactuators': 'Elektromos aktuátorok', 'ofsoldwirelessproducts': 'Wireless',
                      'ofsoldtempcontrolproducts': 'Hőmérséklet-szabályozók', 'ofsoldamsmodules': 'AMS modulok',
                      'ofsoldvacuumproducts': 'Vákuumtermékek', 'ofsoldionizerproducts': 'Ionizátorok',
                      'ofsoldtwoportvalves': '2 utas szelepek'}
            for row, (values, _) in rows.items():
                actual = rows.get(row+1, ({},False))[0]
                raw_name = values.get('D')
                if row <= prefix or not raw_name or normal(values.get('E')) != 'target' or normal(actual.get('E')) != 'actual':
                    continue
                if 'ratio' in normal(raw_name) or not ('sold' in normal(raw_name) or row >= 38):
                    continue
                plan = [number(values.get(c)) for c in months]
                if all(v is None for v in plan):
                    continue
                key = normal(raw_name)
                items[key] = {'label': lookup.get(key, raw_name), 'raw': raw_name, 'actual': [number(actual.get(c)) for c in months],
                              'plan': plan, 'unit': 'db', 'sourceRow': row+1}
            if not items:
                raise ValueError(name+': nem találtam feldolgozható TARGET/ACTUAL sales sorokat.')
            series[str(year)] = {'sheet': name, 'metrics': items, 'filters': filters}
    return series, names


def customers(source, sheet):
    with Workbook(source) as book:
        rows, filters, saved_views = book.sheet(sheet)
        names = list(book.sheets)
    aliases = {'id': ('customer','ugyfelazonosito','vevokod','vevoazonosito','sapid'), 'name': ('name','nev','cegnev','vevonev'),
               'city': ('city','varos','telepules'), 'owner': ('saename','sae','felelos'), 'tech':('tecname','tec'),
               'category':('ceeusercatc','kategoria'), 'roadshow':('roadshow',), 'next':('roadshowinformaciokovetkezolepes','kovetkezolepes'),
               'competitor':('versenytars','competitor'), 'potential':('potencial','potential','piacipotencial'),
               'position':('pozicio','position','smcpozicio','smcposition','kapcsolaterosseg')}
    for header, (row, _) in list(rows.items())[:30]:
        columns = {key: next((col for col,val in row.items() if normal(val) in options), '') for key, options in aliases.items()}
        if columns['name']:
            break
    else:
        raise ValueError(sheet+': nem található a Name/Név oszlop.')
    records = []
    for index, (row, hidden) in rows.items():
        name = row.get(columns['name'], '').strip()
        if index <= header or not name:
            continue
        record = {key: row.get(col, '') for key,col in columns.items()}
        record['id'] = record['id'] or 'név:'+normal(name)
        record.update(row=index, hidden=hidden)
        records.append(record)
    return {'items': records, 'sheet': sheet, 'filters': filters, 'views': saved_views, 'sheets': names,
            'hasScores': bool(columns['potential'] and columns['position'])}


@dataclass(frozen=True)
class Formula:
    expression: str
    cached: float | None


def excel_bytes(sheets, chart_rows, *, editable=False):
    def cell(row, col, value, style):
        ref = column(col)+str(row)
        style = f' s="{style}"' if style else ''
        if value is None:
            return f'<c r="{ref}"{style}/>' if style else ''
        if isinstance(value, Formula):
            error = ' t="e"' if value.cached is None else ''
            cached = '#N/A' if value.cached is None else value.cached
            return f'<c r="{ref}"{style}{error}><f>{escape(value.expression)}</f><v>{cached}</v></c>'
        if type(value) in (int, float) and math.isfinite(value):
            return f'<c r="{ref}"{style}><v>{value}</v></c>'
        # Imported text remains text, including values that start with an equals sign.
        clean = re.sub(r'[\x00-\x08\x0b\x0c\x0e-\x1f]', '', str(value))[:32767]
        return f'<c r="{ref}"{style} t="inlineStr"><is><t xml:space="preserve">{escape(clean)}</t></is></c>'

    def rels(items):
        body = ''.join(f'<Relationship Id="rId{i}" Type="{R}/{kind}" Target="{path}"/>' for i,(kind,path) in enumerate(items,1))
        return f'<Relationships xmlns="{P}">{body}</Relationships>'

    def series(col, title, color, dash='solid'):
        index = ord(col)-66
        cat = f"'Forecast'!$A$2:$A${chart_rows+1}"
        vals = f"'Forecast'!${col}$2:${col}${chart_rows+1}"
        periods = sheets[0][1][1:chart_rows+1]
        categories = ''.join(f'<c:pt idx="{i}"><c:v>{escape(str(row[0]))}</c:v></c:pt>' for i,row in enumerate(periods))
        cached = [value.cached if isinstance(value, Formula) else value for value in (row[index+1] for row in periods)]
        values = ''.join(f'<c:pt idx="{i}"><c:v>{value}</c:v></c:pt>' for i,value in enumerate(cached) if value is not None)
        return (f'<c:ser><c:idx val="{index}"/><c:order val="{index}"/><c:tx><c:v>{title}</c:v></c:tx>'
                f'<c:spPr><a:ln w="32000"><a:solidFill><a:srgbClr val="{color}"/></a:solidFill><a:prstDash val="{dash}"/></a:ln></c:spPr>'
                f'<c:cat><c:strRef><c:f>{cat}</c:f><c:strCache><c:ptCount val="{chart_rows}"/>{categories}</c:strCache></c:strRef></c:cat>'
                f'<c:val><c:numRef><c:f>{vals}</c:f><c:numCache><c:formatCode>General</c:formatCode><c:ptCount val="{chart_rows}"/>{values}</c:numCache></c:numRef></c:val></c:ser>')

    result = io.BytesIO()
    types = {'/xl/workbook.xml': 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml',
             '/xl/styles.xml': 'application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml'}
    with zipfile.ZipFile(result,'w',zipfile.ZIP_DEFLATED) as z:
        z.writestr('_rels/.rels', rels([('officeDocument','xl/workbook.xml')]))
        tab_xml = ''
        workbook_rels = []
        for index,(name,rows) in enumerate(sheets,1):
            tab_xml += f'<sheet name={quoteattr(name)} sheetId="{index}" r:id="rId{index}"/>'
            workbook_rels.append(('worksheet',f'worksheets/sheet{index}.xml'))
            types[f'/xl/worksheets/sheet{index}.xml'] = 'application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml'
            def cell_style(row, col):
                if row == 1:
                    return 1
                if not editable:
                    return 0
                if name == 'Forecast':
                    return 2 if col in (9,14) else 3 if 2<=col<=13 else 0
                if name == 'Források' and col == 2:
                    return 4 if row==10 else 2 if row in (9,11) else 5 if row in (8,16) else 0
                if name == 'Vevők' and col in (7,8,9):
                    return 2
                if name == '9-box' and col > 1:
                    return 6
                return 0

            lines = []
            for i,line in enumerate(rows,1):
                height = ' ht="32" customHeight="1"' if editable and (i==1 or name=='Források' and i in (8,16)) else ''
                cells = ''.join(cell(i,j,value,cell_style(i,j)) for j,value in enumerate(line,1))
                lines.append(f'<row r="{i}"{height}>{cells}</row>')
            widths = {'Forecast':[15,19,19,22,16,16,23,18,23,24,24,24,24,38], 'Vevők':[18,38,22,22,22,22,15,16,44,24],
                      '9-box':[35,14,14,14], 'Sugar CRM':[37,32,22,17,17,20,23,16,24], 'Források':[42,86]}
            cols = ''.join(f'<col min="{i}" max="{i}" width="{w}" customWidth="1"/>'
                           for i,w in enumerate(widths[name] if editable else [24 if index==1 else 22]*len(rows[0]),1))
            has_filter = len(rows)>1 and (not editable or name in ('Vevők','Sugar CRM'))
            auto = f'<autoFilter ref="A1:{column(len(rows[0]))}{len(rows)}"/>' if has_filter else ''
            presentation = ''
            if editable and name == 'Forecast':
                presentation = ('<mergeCells count="1"><mergeCell ref="A14:I14"/></mergeCells>'
                                '<conditionalFormatting sqref="B2:H13 L2:M13"><cfRule type="expression" dxfId="0" priority="1">'
                                '<formula>ISNA(B2)</formula></cfRule></conditionalFormatting>')
            validation = ''
            if editable and name == 'Források':
                validation = ('<dataValidations count="3"><dataValidation type="list" showErrorMessage="1" sqref="B9">'
                              '<formula1>"Szezonális,3 havi átlag"</formula1></dataValidation>'
                              '<dataValidation type="decimal" operator="between" showErrorMessage="1" sqref="B10">'
                              '<formula1>0</formula1><formula2>0.5</formula2></dataValidation>'
                              '<dataValidation type="decimal" operator="greaterThanOrEqual" allowBlank="1" showErrorMessage="1" sqref="B11">'
                              '<formula1>0</formula1></dataValidation></dataValidations>')
            elif editable and name == 'Vevők':
                validation = ('<dataValidations count="1"><dataValidation type="whole" operator="between" allowBlank="1" '
                              'showErrorMessage="1" sqref="G2:H1048576"><formula1>1</formula1><formula2>3</formula2>'
                              '</dataValidation></dataValidations>')
            drawing = '<drawing r:id="rId1"/>' if index==1 else ''
            z.writestr(f'xl/worksheets/sheet{index}.xml',f'<worksheet xmlns="{S[1:-1]}" xmlns:r="{R}"><sheetViews><sheetView workbookViewId="0" showGridLines="0"><pane ySplit="1" topLeftCell="A2" activePane="bottomLeft" state="frozen"/></sheetView></sheetViews><cols>{cols}</cols><sheetData>{"".join(lines)}</sheetData>{auto}{presentation}{validation}{drawing}</worksheet>')
        calculation = '<calcPr calcId="0" calcMode="auto" fullCalcOnLoad="1"/>' if editable else ''
        z.writestr('xl/workbook.xml',f'<workbook xmlns="{S[1:-1]}" xmlns:r="{R}"><bookViews><workbookView/></bookViews><sheets>{tab_xml}</sheets>{calculation}</workbook>')
        z.writestr('xl/_rels/workbook.xml.rels',rels(workbook_rels+[('styles','styles.xml')]))
        fill = lambda color: f'<fill><patternFill patternType="solid"><fgColor rgb="{color}"/><bgColor indexed="64"/></patternFill></fill>'
        def xf(number=0, font=0, fill_id=0, alignment=''):
            extra = f' applyAlignment="1"><alignment {alignment}/></xf>' if alignment else '/>'
            return (f'<xf numFmtId="{number}" fontId="{font}" fillId="{fill_id}" borderId="0" xfId="0"'
                    f' applyFill="{int(bool(fill_id))}" applyFont="{int(bool(font))}" applyNumberFormat="{int(bool(number))}"'+extra)
        formats = [xf(),xf(font=1,fill_id=2,alignment='wrapText="1" vertical="center"'),xf(fill_id=3),xf(number=3),
                   xf(number=9,fill_id=3),xf(alignment='wrapText="1" vertical="center"'),xf(fill_id=4,alignment='horizontal="center"')]
        z.writestr('xl/styles.xml', f'<styleSheet xmlns="{S[1:-1]}"><fonts count="2"><font><sz val="11"/><name val="Calibri"/></font><font><b/><color rgb="FFFFFFFF"/><sz val="11"/><name val="Calibri"/></font></fonts><fills count="5"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill>{fill("FF064B8D")}{fill("FFDDEFFF")}{fill("FFE2F3EE")}</fills><borders count="1"><border><left/><right/><top/><bottom/><diagonal/></border></borders><cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs><cellXfs count="{len(formats)}">{"".join(formats)}</cellXfs><cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles><dxfs count="1"><dxf><font><color rgb="FFFFFFFF"/></font>{fill("FFFFFFFF")}</dxf></dxfs></styleSheet>')
        types['/xl/drawings/drawing1.xml'] = 'application/vnd.openxmlformats-officedocument.drawing+xml'
        types['/xl/charts/chart1.xml'] = 'application/vnd.openxmlformats-officedocument.drawingml.chart+xml'
        z.writestr('xl/worksheets/_rels/sheet1.xml.rels',rels([('drawing','../drawings/drawing1.xml')]))
        z.writestr('xl/drawings/_rels/drawing1.xml.rels',rels([('chart','../charts/chart1.xml')]))
        a = 'http://schemas.openxmlformats.org/drawingml/2006/main'
        c = 'http://schemas.openxmlformats.org/drawingml/2006/chart'
        left, top, right, bottom = (0, 15, 8, 37) if editable else (8, 1, 15, 24)
        z.writestr('xl/drawings/drawing1.xml',f'<xdr:wsDr xmlns:xdr="http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing" xmlns:a="{a}"><xdr:twoCellAnchor><xdr:from><xdr:col>{left}</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>{top}</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:from><xdr:to><xdr:col>{right}</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>{bottom}</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:to><xdr:graphicFrame macro=""><xdr:nvGraphicFramePr><xdr:cNvPr id="2" name="SMC Compass Forecast"/><xdr:cNvGraphicFramePr/></xdr:nvGraphicFramePr><xdr:xfrm><a:off x="0" y="0"/><a:ext cx="0" cy="0"/></xdr:xfrm><a:graphic><a:graphicData uri="{c}"><c:chart xmlns:c="{c}" xmlns:r="{R}" r:id="rId1"/></a:graphicData></a:graphic></xdr:graphicFrame><xdr:clientData/></xdr:twoCellAnchor></xdr:wsDr>')
        bands = series('E','Óvatos','B7791F','dash')+series('F','Erős','6E56CF','dash') if editable else ''
        z.writestr('xl/charts/chart1.xml',f'<c:chartSpace xmlns:c="{c}" xmlns:a="{a}"><c:chart><c:plotArea><c:layout/><c:lineChart><c:grouping val="standard"/>{series("B","Tény","1162BD")}{series("C","Terv","8293AA","dash")}{series("D","Forecast","00968C","dash")}{bands}<c:axId val="10"/><c:axId val="20"/></c:lineChart><c:catAx><c:axId val="10"/><c:scaling><c:orientation val="minMax"/></c:scaling><c:axPos val="b"/><c:crossAx val="20"/><c:crosses val="autoZero"/><c:auto val="1"/></c:catAx><c:valAx><c:axId val="20"/><c:scaling><c:orientation val="minMax"/></c:scaling><c:axPos val="l"/><c:majorGridlines/><c:crossAx val="10"/><c:crosses val="autoZero"/></c:valAx></c:plotArea><c:legend><c:legendPos val="b"/></c:legend><c:plotVisOnly val="1"/><c:dispBlanksAs val="gap"/></c:chart></c:chartSpace>')
        over = ''.join(f'<Override PartName="{key}" ContentType="{val}"/>' for key,val in types.items())
        z.writestr('[Content_Types].xml', '<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/>'+over+'</Types>')
    return result.getvalue()

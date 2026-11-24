from datetime import datetime, timezone
from pathlib import Path
from threading import Lock, RLock
from urllib.error import HTTPError
from urllib.parse import parse_qs, urlencode, urlsplit
from urllib.request import HTTPRedirectHandler, Request, build_opener
import hashlib
import io
import json
import os
import re
import tempfile
import time
import zipfile

from workbook import customers, excel_bytes, number, sales

HERE = Path(__file__).resolve().parent
LIMIT = 35 * 1024 * 1024
SALES_KEY = 'ofsoldelectricactuators'
MONTHS = 'Ápr Máj Jún Júl Aug Szept Okt Nov Dec Jan Feb Márc'.split()
DEFAULT = dict(salesUrl='', buyersUrl='', salesPath='', buyersPath='', salesSheet='REPORT26', previousSheet='REPORT25',
               buyersSheet='Munka1', fiscal=2026, minutes=10, source='local', tenant='', client='',
               salesSource='', buyersSource='', salesDownloadName='', salesDownloadFolder='',accountEmail='',rememberLogin=False,
               sugarUrl='', sugarApi='v11', sugarMine=True)
PROFILE_FILE = HERE/'compass-profile.json'
PROFILE = json.loads(PROFILE_FILE.read_text(encoding='utf-8-sig')) if PROFILE_FILE.is_file() else {}
DEFAULT.update({key:value for key,value in PROFILE.items() if key in DEFAULT and type(value) is type(DEFAULT[key])})
HOME = Path(os.environ.get('COMPASS_DATA', Path(os.environ.get('LOCALAPPDATA', Path.home()))/'SMC Compass'))
FILE = HOME/'compass.json'
WRITE = RLock()
SYNC = Lock()
SESSION = {'sp': {}, 'sugar': ''}
CHECKS = {}
LOCAL_CHECKS = {}
STATE = {'config': DEFAULT.copy(), 'series': {}, 'salesSheets': [], 'buyers': {}, 'manual': {}, 'scores': {},
         'opportunities': [], 'status': {}}


def now():
    return datetime.now(timezone.utc).isoformat(timespec='seconds')


def save():
    HOME.mkdir(parents=True, exist_ok=True)
    fd, temporary = tempfile.mkstemp(prefix='compass-', suffix='.tmp', dir=HOME)
    try:
        with os.fdopen(fd, 'w', encoding='utf-8') as handle:
            json.dump(STATE, handle, ensure_ascii=False, allow_nan=False)
            handle.flush()
            os.fsync(handle.fileno())
        os.replace(temporary, FILE)
    finally:
        Path(temporary).unlink(missing_ok=True)


def load():
    if not FILE.exists():
        return
    try:
        stored = json.loads(FILE.read_text(encoding='utf-8'))
        if not isinstance(stored.get('config'), dict):
            raise ValueError('Érvénytelen beállítás')
        STATE.update(stored)
        cfg = DEFAULT | stored['config']
        # Older installations inherited local mode even with only a SharePoint URL.
        if (not cfg['salesSource'] and cfg['source']=='local' and
                not cfg['salesPath'] and cfg['salesUrl']):
            cfg['salesSource'] = 'browser'
        STATE['config'] = cfg
    except (ValueError, OSError, TypeError):
        raise RuntimeError('A helyi adatbázis nem olvasható: '+str(FILE))


def status(source, message, ok=False, mode=''):
    STATE['status'][source] = {'message': message, 'ok': ok, 'time': now(), 'mode': mode,
                               'lastGood': now() if ok else STATE['status'].get(source, {}).get('lastGood')}


class NoRedirect(HTTPRedirectHandler):
    def redirect_request(self, *args, **kwargs):
        return None


def http(url, fields=None, headers=None, form=False):
    body = None if fields is None else (urlencode(fields).encode() if form else json.dumps(fields).encode())
    safe_headers = {'Accept':'application/json', **(headers or {})}
    if body is not None:
        safe_headers['Content-Type'] = 'application/x-www-form-urlencoded' if form else 'application/json'
    try:
        with build_opener(NoRedirect).open(Request(url, data=body, headers=safe_headers), timeout=25) as response:
            content = response.read(LIMIT+1)
    except HTTPError as error:
        raise ValueError(f'A szerver HTTP {error.code} választ adott. Ellenőrizd az URL-t és a jogosultságot.') from None
    if len(content) > LIMIT:
        raise ValueError('A forrásfájl meghaladja a 35 MB méretkorlátot.')
    return content


def sharepoint_file(url):
    parsed = urlsplit(url)
    if parsed.scheme != 'https' or not re.fullmatch(r'[a-z0-9-]+\.sharepoint\.com', parsed.netloc, re.I):
        raise ValueError('Érvényes HTTPS SharePoint Online URL szükséges.')
    site = re.search(r'/(?:sites|teams)/[^/]+', parsed.path, re.I)
    guid = next((values[0].strip('{}') for key, values in parse_qs(parsed.query).items() if key.lower()=='sourcedoc'), '')
    if not site or not re.fullmatch(r'[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}', guid, re.I):
        raise ValueError('SharePoint munkafüzetlink szükséges (sourcedoc azonosítóval).')
    return f'https://{parsed.netloc.lower()}', site.group(), guid


def sharepoint_resource(url):
    origin, site, guid = sharepoint_file(url)
    return origin, origin+site+f"/_api/web/GetFileById(guid'{guid}')/$value"


def sharepoint_browser_download(url):
    origin, site, guid = sharepoint_file(url)
    return origin+site+'/_layouts/15/download.aspx?'+urlencode({'UniqueId':guid})


def microsoft_details(config):
    tenant, client = config['tenant'], config['client']
    uuid = r'[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}'
    if not re.fullmatch(uuid, tenant, re.I) or not re.fullmatch(uuid, client, re.I):
        raise ValueError('Az Entra Directory (tenant) ID és Application (client) ID mezőbe GUID szükséges.')
    hosts = {sharepoint_resource(config[kind+'Url'])[0] for kind in ('sales','buyers')
             if source_mode(kind, config)=='sharepoint'}
    if not hosts:
        raise ValueError('A Források fülön legalább egy forrást állíts közvetlen SharePointra.')
    if len(hosts)>1:
        raise ValueError('A két SharePoint-forrásnak azonos SharePoint-bérlőben kell lennie.')
    host = hosts.pop()
    return 'https://login.microsoftonline.com/'+tenant+'/oauth2/v2.0/', client, host+'/AllSites.Read offline_access'


def apply_token(token, old_refresh=''):
    SESSION['sp'] = {'access':token['access_token'], 'refresh':token.get('refresh_token', old_refresh),
                     'expires':time.monotonic()+max(0,int(token.get('expires_in',3600))-90)}


def sp_token(config):
    current = SESSION['sp']
    if current and current['expires'] > time.monotonic():
        return current['access']
    if not current.get('refresh'):
        if not config['tenant'] or not config['client']:
            raise ValueError('Hiányzik az Entra tenant vagy client ID. Add meg az azonosítókat, vagy állíts be szinkronizált OneDrive-fájlt.')
        raise ValueError('A SharePoint közvetlen olvasásához jelentkezz be a Források oldalon.')
    endpoint, client, scope = microsoft_details(config)
    token = json.loads(http(endpoint+'token', dict(grant_type='refresh_token', client_id=client,
                                                  refresh_token=current['refresh'],scope=scope),form=True))
    apply_token(token,current['refresh'])
    return token['access_token']


def source_mode(kind, config):
    return config.get(kind+'Source') or config['source']


def local_source(kind, config):
    file = config[kind+'Path']
    fallback = PROFILE.get(kind+'Snapshot','')
    if not (file or fallback):
        raise ValueError('Válaszd ki a helyi vagy a OneDrive-val szinkronizált Excel-fájlt a Fájlkezelőből.')
    path = Path(file) if file else Path.home()/'Downloads'/Path(fallback).name
    if path.suffix.lower() not in ('.xlsx','.xlsm'):
        raise ValueError('XLSX- vagy XLSM-munkafüzet szükséges.')
    return path, 'Figyelt Excel-fájl' if file else 'Letöltések · pillanatkép'


def read_source(kind, config):
    if source_mode(kind, config)=='sharepoint':
        endpoint = sharepoint_resource(config[kind+'Url'])[1]
        data = http(endpoint, headers={'Authorization':'Bearer '+sp_token(config),'Accept':'application/octet-stream'})
        return data, 'SharePoint'
    path, label = local_source(kind, config)
    with path.open('rb') as file:
        data = file.read(LIMIT+1)
    if len(data)>LIMIT:
        raise ValueError('A forrásfájl meghaladja a 35 MB méretkorlátot.')
    return data, label


def ingest(kind, data, config, mode, force=False):
    if not data.startswith(b'PK'):
        raise ValueError('A forrás nem megnyitható XLSX-munkafüzetként.')
    signature = (hashlib.sha256(data).digest(), mode, config['salesSheet'], config['previousSheet'],
                 config['buyersSheet'], config['fiscal'])
    if not force and CHECKS.get(kind)==signature:
        return False
    try:
        if kind=='sales':
            entries, names = sales(io.BytesIO(data), config['salesSheet'], config['fiscal'], config['previousSheet'])
            with WRITE:
                STATE['series'].update(entries)
                STATE['salesSheets'] = names
                status(kind, f'{config["salesSheet"]} · {len(entries[str(config["fiscal"])]["metrics"])} mutató', True, mode)
        else:
            entries = customers(io.BytesIO(data), config['buyersSheet'])
            with WRITE:
                STATE['buyers'] = entries
                status(kind, f'{entries["sheet"]} · {len(entries["items"])} vevősor', True, mode)
    except (zipfile.BadZipFile, KeyError, IndexError) as error:
        raise ValueError('Sérült vagy nem támogatott XLSX-szerkezet.') from error
    CHECKS[kind]=signature
    return True


def sugar_import(config):
    url = urlsplit(config['sugarUrl'].strip())
    if url.scheme!='https' or not url.hostname or url.username or url.password or url.query or url.fragment:
        raise ValueError('Add meg a Sugar példány HTTPS alapcímét.')
    version = config['sugarApi']
    if not re.fullmatch(r'v11(?:_\d{1,3})?',version):
        raise ValueError('Sugar REST-verzió formátuma: v11 vagy például v11_24.')
    if not SESSION['sugar']:
        raise ValueError('Adj meg egy érvényes Sugar REST access tokent a munkamenethez.')
    root = config['sugarUrl'].rstrip('/')+f'/rest/{version}/Opportunities/filter'
    fields = 'id,name,account_name,date_closed,sales_stage,amount,probability,currency_id,currency_iso,assigned_user_name'
    offset, records, seen = 0, {}, set()
    while offset >= 0:
        if offset in seen or len(seen) >= 200:
            raise ValueError('A Sugar lapozás megakadt vagy túllépte a 20 000 rekordos korlátot.')
        seen.add(offset)
        params = dict(fields=fields, max_num=100, offset=offset, order_by='id:ASC')
        if config['sugarMine']:
            params['my_items'] = 'true'
        reply = json.loads(http(root+'?'+urlencode(params), headers={'OAuth-Token':SESSION['sugar']}))
        for row in reply['records']:
            if row.get('id'):
                records[row['id']] = {field:row.get(field) for field in fields.split(',')}
        offset = int(reply['next_offset'])
    with WRITE:
        STATE['opportunities'] = list(records.values())
        status('sugar',f'{len(records)} lehetőség', True,'Sugar REST')
        save()


def refresh(force=False, local_only=False):
    if not SYNC.acquire(blocking=False):
        return False
    try:
        with WRITE:
            config = STATE['config'].copy()
        before = STATE['status'].copy()
        imported = False
        for kind in ('sales','buyers'):
            mode = source_mode(kind, config)
            if mode in ('manual','browser') or (local_only and (mode!='local' or not config[kind+'Path'])):
                continue
            try:
                signature = None
                if mode=='local':
                    path, _ = local_source(kind, config)
                    stamp = path.stat()
                    signature = (str(path), stamp.st_mtime_ns, stamp.st_ctime_ns, stamp.st_size,
                                 config['salesSheet'], config['previousSheet'], config['buyersSheet'], config['fiscal'])
                    if not force and LOCAL_CHECKS.get(kind)==signature and STATE['status'].get(kind,{}).get('ok'):
                        continue
                data, mode = read_source(kind, config)
                imported |= ingest(kind, data, config, mode, force or not STATE['status'].get(kind,{}).get('ok'))
                if signature is not None:
                    LOCAL_CHECKS[kind]=signature
            except Exception as error:
                with WRITE:
                    if isinstance(error, FileNotFoundError):
                        error = 'A beállított Excel-fájl még nem érhető el a Fájlkezelőben.'
                    previous = STATE['status'].get(kind,{})
                    if force or previous.get('ok') or previous.get('message')!=str(error):
                        status(kind, str(error), mode='SharePoint' if source_mode(kind,config)=='sharepoint' else 'Helyi forrás')
        if not local_only and SESSION['sugar']:
            try:
                sugar_import(config)
            except Exception as error:
                with WRITE:
                    status('sugar',str(error),mode='Sugar REST')
        changed = imported or before!=STATE['status']
        if changed:
            with WRITE:
                save()
        return changed
    finally:
        SYNC.release()


def cumulative(value, actuals):
    running, gap, actual = 0, False, []
    for idx, original in enumerate(value):
        if str(idx) in actuals:
            running += actuals[str(idx)]['amount']
            actual.append(None if gap else running)
        elif original is None:
            gap = True
            actual.append(None)
        elif gap:
            actual.append(None)
        else:
            source_previous = value[idx-1] if idx else 0
            if source_previous is None:
                gap = True
                actual.append(None)
                continue
            running += original-source_previous
            actual.append(running)
    return actual


def scenario(year, metric, factor=10, method='blend'):
    bits = STATE['series'].get(str(year),{}).get('metrics',{}).get(metric,{})
    previous = STATE['series'].get(str(year-1),{}).get('metrics',{}).get(metric,{})
    manual = STATE['manual'].get(str(year),{}).get(metric,{})
    prev_manual = STATE['manual'].get(str(year-1),{}).get(metric,{})
    original = bits.get('actual',[None]*12)
    actual = cumulative(original,manual)
    prev = cumulative(previous.get('actual',[None]*12),prev_manual)
    count = next((i for i,v in enumerate(actual) if v is None),12)
    history = actual[:count]
    monthly = [value-(history[i-1] if i else 0) for i,value in enumerate(history)]
    baseline = sum(monthly[-3:])/min(len(monthly),3) if monthly else None
    seasonal = bool(2<=count<12 and method=='blend' and prev[-1] is not None and prev[count-1]>0 and all(a>=b for a,b in zip(prev,[0]+prev[:-1])))
    point = history[-1] if history else None
    forecast, low, high = [None]*12,[None]*12,[None]*12
    if count and count<12:
        forecast[count-1] = low[count-1] = high[count-1] = point
        scale = point/prev[count-1] if seasonal else 0
        for i in range(count,12):
            estimate = (baseline+scale*(prev[i]-prev[i-1]))/2 if seasonal else baseline
            step = max(0,estimate)
            forecast[i] = forecast[i-1]+step
            low[i] = low[i-1]+step*(1-factor/100)
            high[i] = high[i-1]+step*(1+factor/100)
    plan = bits.get('plan',[None]*12)[:]
    if 'target' in manual:
        plan = [manual['target']*(i+1)/12 for i in range(12)]
    newer = any(original[i] is not None or str(i) in manual for i in range(count+1,12)) if count<12 else False
    return {'year':year, 'metric':metric, 'label':bits.get('label', 'Elektromos aktuátorok'), 'actual':actual, 'monthly':monthly,
            'plan':plan,'prev':prev,'forecast':forecast,'low':low,'high':high,'count':count,
            'seasonal':seasonal,'gap':newer,'ending':forecast[-1] if count<12 else point,
            'entry':{k:v for k,v in manual.items() if k!='target'},'target':manual.get('target'),
            'source':STATE['series'].get(str(year),{}).get('sheet','Kézi adatbevitel')}


def cleaned_scores(record):
    local = STATE['scores'].get(str(record['id']),{})
    item = record.copy()
    for key in ('potential','position'):
        raw = local.get(key, record.get(key))
        parsed = number(raw)
        mapping = {'alacsony':1,'low':1,'közepes':2,'medium':2,'magas':3,'high':3}
        item[key] = int(parsed) if parsed in (1,2,3) else mapping.get(str(raw).strip().lower())
    item['next'] = local.get('next',record.get('next',''))
    return item


def snapshot():
    with WRITE:
        buyers = STATE['buyers'].copy()
        buyers['items'] = [cleaned_scores(record) for record in buyers.get('items',[])]
        yrs = set(STATE['series']) | set(STATE['manual']) | {str(STATE['config']['fiscal'])}
        keys = {SALES_KEY:'Elektromos aktuátorok'}
        for entry in STATE['series'].values():
            keys.update({k:v['label'] for k,v in entry['metrics'].items()})
        return {'config':STATE['config'], 'status':STATE['status'], 'buyers':buyers,'years':sorted(yrs,reverse=True),
                'salesSheets':STATE['salesSheets'],'metrics':keys,'opportunities':STATE['opportunities'],
                'spConnected':bool(SESSION['sp']), 'sugarConnected':bool(SESSION['sugar'])}


def forecast_args(query):
    year = int(query.get('year',STATE['config']['fiscal']))
    metric = str(query.get('metric',SALES_KEY))
    factor = min(50,max(0,int(query.get('spread',10))))
    method = str(query.get('method','blend'))
    if not 2000<=year<=2200 or not re.fullmatch(r'[a-z0-9]{1,130}',metric):
        raise ValueError('Érvénytelen üzleti év vagy mutató.')
    return year,metric,factor,method


def export(query):
    with WRITE:
        report = scenario(*forecast_args(query))
        y = report['year']
        rows = [['Hónap','Tény YTD (db)','Terv YTD (db)','Forecast YTD (db)','Óvatos (db)','Erős (db)','Előző FY YTD (db)','Havi tény (db)']]
        for i, m in enumerate(MONTHS):
            rows.append([f'{m} {y+(i>=9)}', *[report[key][i] for key in ('actual','plan','forecast','low','high','prev')],
                         report['monthly'][i] if i<len(report['monthly']) else None])
        filtered = query.get('visible','1')=='1'
        buyers = [['Ügyfélkód','Vevő','Város','SAE','TEC','Kategória','Potenciál','SMC pozíció','Következő lépés','Rejtett a forrásban']]
        for source in STATE['buyers'].get('items',[]):
            item = cleaned_scores(source)
            if filtered and item['hidden']:
                continue
            buyers.append([*[item[key] for key in ('id','name','city','owner','tech','category','potential','position','next')], 'Igen' if item['hidden'] else 'Nem'])
        crm_keys = ('name','account_name','sales_stage','amount','currency_iso','currency_id','probability','date_closed','assigned_user_name')
        crm = [['Lehetőség','Vevő','Státusz','Összeg','ISO pénznem','Pénznem ID','Valószínűség (%)','Zárás','Felelős']]
        crm.extend([[number(a.get(k)) if k in ('amount','probability') else a.get(k) for k in crm_keys] for a in STATE['opportunities']])
        info = [['Tulajdonság','Érték'],['Üzleti év',f'FY {y} / április–március'],['Mutató',report['label']],
                ['Sales munkalap',report['source']],['Vevőlista munkalap',STATE['buyers'].get('sheet','')],
                ['Excel-szűrés (vevők)','Csak látható sorok' if filtered else 'Összes sor'],
                ['Forecast módszer','3 havi átlag + előző évi szezonalitás' if report['seasonal'] else 'Utolsó legfeljebb 3 havi átlag'],
                ['Export',now()]]
    return excel_bytes([('Forecast',rows),('Vevők',buyers),('Sugar CRM',crm),('Források',info)],12)

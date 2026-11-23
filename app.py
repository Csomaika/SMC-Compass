from collections import defaultdict
from datetime import datetime, timezone
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from threading import Lock, RLock, Thread
from urllib.error import HTTPError
from urllib.parse import parse_qs, urlencode, urlsplit
from urllib.request import HTTPRedirectHandler, Request, build_opener
import argparse
import base64
import hashlib
import io
import json
import os
import re
import secrets
import tempfile
import time
import webbrowser
import zipfile

from workbook import customers, excel_bytes, number, sales

HERE = Path(__file__).resolve().parent
PORT = 43781
LIMIT = 35 * 1024 * 1024
SALES_KEY = 'ofsoldelectricactuators'
MONTHS = 'Ápr Máj Jún Júl Aug Szept Okt Nov Dec Jan Feb Márc'.split()
DEFAULT = dict(salesUrl='', buyersUrl='', salesPath='', buyersPath='', salesSheet='REPORT26', previousSheet='REPORT25',
               buyersSheet='Munka1', fiscal=2026, minutes=10, source='local', tenant='', client='',
               sugarUrl='', sugarApi='v11', sugarMine=True)
PROFILE_FILE = HERE/'compass-profile.json'
PROFILE = json.loads(PROFILE_FILE.read_text(encoding='utf-8-sig')) if PROFILE_FILE.is_file() else {}
DEFAULT.update({key:value for key,value in PROFILE.items() if key in DEFAULT and type(value) is type(DEFAULT[key])})
HOME = Path(os.environ.get('COMPASS_DATA', Path(os.environ.get('LOCALAPPDATA', Path.home()))/'SMC Compass'))
FILE = HOME/'compass.json'
WRITE = RLock()
SYNC = Lock()
TOKEN_HEADER = secrets.token_urlsafe(32)
SESSION = {'sp': {}, 'sugar': '', 'oauth': None}
CHECKS = {}
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
        STATE['config'] = DEFAULT | stored['config']
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


def sharepoint_resource(url):
    parsed = urlsplit(url)
    if parsed.scheme != 'https' or not re.fullmatch(r'[a-z0-9-]+\.sharepoint\.com', parsed.netloc, re.I):
        raise ValueError('Érvényes HTTPS SharePoint Online URL szükséges.')
    site = re.search(r'/(?:sites|teams)/[^/]+', parsed.path, re.I)
    guid = next((values[0].strip('{}') for key, values in parse_qs(parsed.query).items() if key.lower()=='sourcedoc'), '')
    if not site or not re.fullmatch(r'[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}', guid, re.I):
        raise ValueError('SharePoint munkafüzetlink szükséges (sourcedoc azonosítóval).')
    origin = f'https://{parsed.netloc.lower()}'
    return origin, origin+site.group()+f"/_api/web/GetFileById(guid'{guid}')/$value"


def microsoft_details(config):
    tenant, client = config['tenant'], config['client']
    uuid = r'[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}'
    if not re.fullmatch(uuid, tenant, re.I) or not re.fullmatch(uuid, client, re.I):
        raise ValueError('Az Entra Directory (tenant) ID és Application (client) ID mezőbe GUID szükséges.')
    host = sharepoint_resource(config['salesUrl'])[0]
    if sharepoint_resource(config['buyersUrl'])[0] != host:
        raise ValueError('A két SharePoint-forrásnak azonos SharePoint-bérlőben kell lennie.')
    return 'https://login.microsoftonline.com/'+tenant+'/oauth2/v2.0/', client, host+'/AllSites.Read offline_access'


def oauth_start(config, port):
    endpoint, client, scope = microsoft_details(config)
    verifier = secrets.token_urlsafe(64)
    nonce = secrets.token_urlsafe(32)
    redirect = f'http://localhost:{port}/oauth/callback'
    SESSION['oauth'] = (nonce, verifier, endpoint, client, scope, redirect, time.monotonic()+600)
    challenge = base64.urlsafe_b64encode(hashlib.sha256(verifier.encode()).digest()).decode().rstrip('=')
    return endpoint+'authorize?'+urlencode(dict(client_id=client, scope=scope, response_type='code',
                                               response_mode='query',redirect_uri=redirect, state=nonce,
                                               code_challenge=challenge, code_challenge_method='S256'))


def apply_token(token, old_refresh=''):
    SESSION['sp'] = {'access':token['access_token'], 'refresh':token.get('refresh_token', old_refresh),
                     'expires':time.monotonic()+max(0,int(token.get('expires_in',3600))-90)}


def sp_token(config):
    current = SESSION['sp']
    if current and current['expires'] > time.monotonic():
        return current['access']
    if not current.get('refresh'):
        raise ValueError('A SharePoint közvetlen olvasásához jelentkezz be a Források oldalon.')
    endpoint, client, scope = microsoft_details(config)
    token = json.loads(http(endpoint+'token', dict(grant_type='refresh_token', client_id=client,
                                                  refresh_token=current['refresh'],scope=scope),form=True))
    apply_token(token,current['refresh'])
    return token['access_token']


def read_source(kind, config):
    if config['source']=='sharepoint':
        endpoint = sharepoint_resource(config[kind+'Url'])[1]
        data = http(endpoint, headers={'Authorization':'Bearer '+sp_token(config),'Accept':'application/octet-stream'})
        return data, 'SharePoint'
    file = config[kind+'Path']
    fallback = PROFILE.get(kind+'Snapshot','')
    if not (file or fallback):
        raise ValueError('Add meg a forrásfájl útvonalát vagy válassz SharePoint-kapcsolatot.')
    path = Path(file) if file else Path.home()/'Downloads'/Path(fallback).name
    if not path.is_file() or path.suffix.lower() not in ('.xlsx','.xlsm'):
        raise ValueError('Add meg az Excel-fájl helyi útvonalát, vagy tölts be egy munkafüzetet.')
    if path.stat().st_size > LIMIT:
        raise ValueError('A forrásfájl meghaladja a 35 MB méretkorlátot.')
    return path.read_bytes(), 'Figyelt helyi fájl' if file else 'Letöltések · pillanatkép'


def ingest(kind, data, config, mode, force=False):
    if not data.startswith(b'PK'):
        raise ValueError('A forrás nem megnyitható XLSX-munkafüzetként.')
    signature = (hashlib.sha256(data).digest(), mode, config['salesSheet'], config['previousSheet'],
                 config['buyersSheet'], config['fiscal'])
    if not force and CHECKS.get(kind)==signature:
        return
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


def refresh(force=False):
    if not SYNC.acquire(blocking=False):
        return False
    try:
        with WRITE:
            config = STATE['config'].copy()
        for kind in ('sales','buyers'):
            if config['source']=='manual':
                continue
            try:
                data, mode = read_source(kind, config)
                ingest(kind, data, config, mode, force or not STATE['status'].get(kind,{}).get('ok'))
            except Exception as error:
                with WRITE:
                    status(kind, str(error), mode='SharePoint' if config['source']=='sharepoint' else 'Helyi forrás')
        if SESSION['sugar']:
            try:
                sugar_import(config)
            except Exception as error:
                with WRITE:
                    status('sugar',str(error),mode='Sugar REST')
        with WRITE:
            save()
        return True
    finally:
        SYNC.release()


def scheduler():
    while True:
        try:
            refresh()
        except OSError:
            pass
        seconds = 60*STATE['config']['minutes']
        deadline = time.monotonic()+seconds
        while time.monotonic() < deadline and seconds == 60*STATE['config']['minutes']:
            time.sleep(3)


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


class Handler(BaseHTTPRequestHandler):
    def log_message(self,*args):
        pass

    def send(self, data, kind='application/json; charset=utf-8', code=200, attachment=False):
        if isinstance(data, dict):
            data = json.dumps(data, ensure_ascii=False, allow_nan=False).encode()
        if isinstance(data, str):
            data = data.encode()
        self.send_response(code)
        self.send_header('Content-Type',kind)
        self.send_header('Cache-Control','no-store')
        self.send_header('X-Content-Type-Options','nosniff')
        self.send_header('Referrer-Policy','no-referrer')
        self.send_header('Content-Security-Policy',"default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; object-src 'none'; frame-ancestors 'none'; base-uri 'none'")
        if attachment:
            self.send_header('Content-Disposition','attachment; filename="SMC-Compass-Export.xlsx"')
        self.send_header('Content-Length',str(len(data)))
        self.end_headers()
        self.wfile.write(data)

    def operation(self):
        if self.headers.get('Host') not in {f'localhost:{self.server.server_port}', f'127.0.0.1:{self.server.server_port}'}:
            self.send({'error':'Érvénytelen helyi cím.'},code=403)
            return
        url = urlsplit(self.path)
        query = {k:v[0] for k,v in parse_qs(url.query).items()}
        route = url.path
        web = {'/':('index.html','text/html'),'/style.css':('style.css','text/css'),'/ui.js':('ui.js','text/javascript'),
               '/brand.svg':('brand.svg','image/svg+xml')}
        if self.command=='GET' and route in web:
            name, mime = web[route]
            data = (HERE/'web'/name).read_bytes()
            if route=='/':
                data=data.replace(b'__LOCAL_SESSION__',TOKEN_HEADER.encode())
            self.send(data,mime+'; charset=utf-8')
            return
        if self.command=='GET' and route=='/oauth/callback':
            self.oauth_callback(query)
            return
        if self.headers.get('X-Compass') != TOKEN_HEADER:
            self.send({'error':'Nyisd meg újra az SMC Compass lapot.'},code=403)
            return
        if self.command=='GET':
            if route=='/api/state':
                with WRITE:
                    self.send(snapshot())
            elif route=='/api/forecast':
                with WRITE:
                    self.send(scenario(*forecast_args(query)))
            elif route=='/api/export':
                self.send(export(query),'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',attachment=True)
            else:
                self.send({'error':'Ismeretlen végpont.'},code=404)
            return
        length = int(self.headers.get('Content-Length','0'))
        upload = route in ('/api/import/sales','/api/import/buyers')
        if length<0 or length>(LIMIT if upload else 100_000):
            self.send({'error':'A kérés túl nagy.'},code=413)
            return
        raw = self.rfile.read(length)
        if upload:
            kind = route.split('/')[-1]
            if not SYNC.acquire(blocking=False):
                raise ValueError('Frissítés fut. Az import utána indítható.')
            try:
                ingest(kind,raw,STATE['config'].copy(),'Kézi Excel-import',True)
                with WRITE:
                    save()
            finally:
                SYNC.release()
            self.send({'ok':True})
            return
        data = json.loads(raw or b'{}')
        if route=='/api/refresh':
            self.send({'ok':refresh(force=True)})
            return
        if route=='/api/sugar':
            token = str(data.get('token','')).strip()
            if not token or len(token)>8000 or not token.isascii() or any(c.isspace() for c in token):
                raise ValueError('Érvényes Sugar access token szükséges.')
            SESSION['sugar']=token
            try:
                sugar_import(STATE['config'].copy())
            except Exception:
                SESSION['sugar']=''
                raise
            self.send({'ok':True})
            return
        with WRITE:
            if route=='/api/settings':
                updated = {}
                for key,default in DEFAULT.items():
                    if key not in data:
                        continue
                    if type(data[key]) is not type(default):
                        raise ValueError('Érvénytelen beállítás: '+key)
                    updated[key]=data[key].strip()[:1400] if isinstance(default,str) else data[key]
                config=STATE['config']|updated
                if not 2000<=config['fiscal']<=2200 or not 1<=config['minutes']<=180 or config['source'] not in ('local','manual','sharepoint'):
                    raise ValueError('Megengedett üzleti év 2000–2200, frissítési idő 1–180 perc.')
                if any(config[key]!=STATE['config'][key] for key in ('tenant','client','salesUrl','buyersUrl')):
                    SESSION['sp']={}
                    SESSION['oauth']=None
                if config['sugarUrl']!=STATE['config']['sugarUrl'] or config['sugarApi']!=STATE['config']['sugarApi']:
                    SESSION['sugar']=''
                STATE['config']=config
            elif route=='/api/oauth':
                self.send({'url':oauth_start(STATE['config'],self.server.server_port)})
                return
            elif route=='/api/manual':
                year,metric,_,_ = forecast_args(data)
                amount = number(data.get('amount'))
                action = data.get('action')
                month = int(data.get('month',0))
                if not 0<=month<12:
                    raise ValueError('Válassz érvényes hónapot.')
                if action not in ('remove','target-remove') and (amount is None or abs(amount)>1e12):
                    raise ValueError('Adj meg egy érvényes számot.')
                entry = STATE['manual'].setdefault(str(year),{}).setdefault(metric,{})
                if action=='target':
                    if amount<0:
                        raise ValueError('A célérték nem lehet negatív.')
                    entry['target']=amount
                elif action=='target-remove':
                    entry.pop('target',None)
                elif action=='remove':
                    entry.pop(str(month),None)
                else:
                    entry[str(month)]={'amount':amount,'note':str(data.get('note',''))[:500],'time':now()}
            elif route=='/api/score':
                key=str(data.get('id',''))
                if not any(str(row['id'])==key for row in STATE['buyers'].get('items',[])):
                    raise ValueError('A vevő nem található.')
                for score in ('potential','position'):
                    if data.get(score) not in (None,1,2,3):
                        raise ValueError('A pontszám 1–3 lehet.')
                STATE['scores'][key]={'potential':data.get('potential'),'position':data.get('position'),
                                      'next':str(data.get('next',''))[:1500]}
            elif route=='/api/quit':
                self.send({'ok':True})
                Thread(target=self.server.shutdown,daemon=True).start()
                return
            else:
                self.send({'error':'Ismeretlen művelet.'},code=404)
                return
            save()
        self.send({'ok':True})

    def oauth_callback(self, query):
        with WRITE:
            flow = SESSION['oauth']
            valid = flow and query.get('state') and secrets.compare_digest(flow[0],query['state']) and flow[-1]>time.monotonic()
            if not valid:
                self.send('Érvénytelen vagy lejárt bejelentkezés.', 'text/plain; charset=utf-8',400)
                return
            SESSION['oauth']=None
        _,verifier,endpoint,client,scope,redirect,_=flow
        if 'code' not in query:
            raise ValueError('A Microsoft bejelentkezés nem adott hozzáférést.')
        token=json.loads(http(endpoint+'token',dict(grant_type='authorization_code',client_id=client,scope=scope,
                                                  code=query['code'],redirect_uri=redirect,code_verifier=verifier),form=True))
        with WRITE:
            apply_token(token)
        Thread(target=refresh,daemon=True).start()
        self.send('<!doctype html><meta charset="utf-8"><title>SMC Compass</title><h2>Microsoft-bejelentkezés kész</h2><a href="/">Vissza az SMC Compasshoz</a>','text/html; charset=utf-8')

    def handle_operation(self):
        try:
            self.operation()
        except (BrokenPipeError,ConnectionResetError):
            pass
        except (ValueError, OSError, KeyError, TypeError) as error:
            self.send({'error':str(error)},code=400)
        except Exception:
            self.send({'error':'Váratlan hiba a művelet közben.'},code=500)

    do_GET = do_POST = handle_operation


def main():
    parser=argparse.ArgumentParser()
    parser.add_argument('--port',type=int,default=PORT)
    parser.add_argument('--headless',action='store_true')
    parser.add_argument('--no-sync',action='store_true')
    opts=parser.parse_args()
    load()
    browser_url=f'http://localhost:{opts.port}/'
    try:
        server=ThreadingHTTPServer(('127.0.0.1', opts.port),Handler)
    except OSError:
        if not opts.headless:
            webbrowser.open(browser_url)
            return
        raise
    if not opts.no_sync:
        Thread(target=scheduler,daemon=True).start()
    if not opts.headless:
        webbrowser.open(browser_url)
    with server:
        server.serve_forever()


if __name__=='__main__':
    main()

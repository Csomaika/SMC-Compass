"""Native desktop worker. Newline JSON on the private parent process pipes."""
import io
import json
import re
import sys
import time
from pathlib import Path
from urllib.error import HTTPError
from urllib.parse import urlencode, urlsplit
from urllib.request import Request, build_opener

import app as core
import updates

DEVICE = {}


def configure(fields):
    with core.WRITE:
        changes = {}
        for key, value in fields.items():
            if key not in core.DEFAULT or type(value) is not type(core.DEFAULT[key]):
                continue
            changes[key] = value.strip()[:1400] if isinstance(value,str) else value
        current = core.STATE['config']
        cfg = current | changes
        if not 2000 <= cfg['fiscal'] <= 2200 or not 1 <= cfg['minutes'] <= 180:
            raise ValueError('Üzleti év 2000–2200, frissítési idő 1–180 perc.')
        if cfg['source'] not in ('manual','local','sharepoint'):
            raise ValueError('Érvénytelen adatforrásmód.')
        if any(cfg[k] not in ('','manual','local','sharepoint') for k in ('salesSource','buyersSource')):
            raise ValueError('Érvénytelen munkafüzet-forrásmód.')
        if any(cfg[k] != current[k] for k in ('tenant','client','salesUrl','buyersUrl','source','salesSource','buyersSource')):
            core.SESSION['sp'] = {}
            DEVICE.clear()
        for kind in ('sales','buyers'):
            if (core.source_mode(kind,current)!=core.source_mode(kind,cfg) or cfg[kind+'Path']!=current[kind+'Path']):
                core.LOCAL_CHECKS.pop(kind,None)
                core.CHECKS.pop(kind,None)
        if any(cfg[k] != current[k] for k in ('sugarUrl','sugarApi')):
            core.SESSION['sugar'] = ''
        core.STATE['config'] = cfg
        core.save()


def manual(data):
    year, metric, _, _ = core.forecast_args(data)
    month = int(data.get('month',0))
    if not 0 <= month <= 11:
        raise ValueError('Érvénytelen hónap.')
    action = data.get('action')
    amount = core.number(data.get('amount'))
    if action in ('save','target') and (amount is None or abs(amount)>1e12):
        raise ValueError('Adj meg érvényes darabszámot.')
    if action=='target' and amount<0:
        raise ValueError('Az éves cél nem lehet negatív.')
    entry = core.STATE['manual'].setdefault(str(year),{}).setdefault(metric,{})
    if action=='save':
        entry[str(month)] = {'amount':amount,'note':str(data.get('note',''))[:500],'time':core.now()}
    elif action=='target':
        entry['target'] = amount
    elif action=='remove':
        entry.pop(str(month),None)
    elif action=='target-remove':
        entry.pop('target',None)
    else:
        raise ValueError('Ismeretlen adatbeviteli művelet.')
    core.save()


def score(data):
    key = str(data.get('id',''))
    if not any(str(row['id'])==key for row in core.STATE['buyers'].get('items',[])):
        raise ValueError('A vevő nem található.')
    if any(data.get(item) not in (None,1,2,3) for item in ('potential','position')):
        raise ValueError('A pontszám 1–3 lehet.')
    core.STATE['scores'][key] = {'potential':data.get('potential'),'position':data.get('position'),
                                 'next':str(data.get('next',''))[:1500]}
    core.save()


def write_export(data):
    path = Path(data['path'])
    if path.suffix.lower()!='.xlsx':
        raise ValueError('Az export kiterjesztése .xlsx legyen.')
    path.write_bytes(core.export(data))


def import_workbook(data):
    path = Path(data['path'])
    kind = data['kind']
    if kind not in ('sales','buyers') or path.suffix.lower() not in ('.xlsx','.xlsm'):
        raise ValueError('Válassz XLSX- vagy XLSM-munkafüzetet.')
    with path.open('rb') as file:
        content = file.read(core.LIMIT+1)
    if len(content)>core.LIMIT:
        raise ValueError('A forrásfájl meghaladja a 35 MB-ot.')
    core.ingest(kind,content,core.STATE['config'].copy(),'Kézi Excel-import',True)
    core.LOCAL_CHECKS.pop(kind,None)
    core.save()


def sugar(data):
    token = str(data.get('token','')).strip()
    if not token or len(token)>8000 or not token.isascii() or any(c.isspace() for c in token):
        raise ValueError('Érvényes Sugar access token szükséges.')
    core.SESSION['sugar'] = token
    try:
        core.sugar_import(core.STATE['config'].copy())
    except Exception:
        core.SESSION['sugar'] = ''
        raise


def start_signin():
    endpoint, client, scope = core.microsoft_details(core.STATE['config'])
    data = json.loads(core.http(endpoint+'devicecode',{'client_id':client,'scope':scope},form=True))
    url = data['verification_uri']
    loc = urlsplit(url)
    if loc.scheme!='https' or loc.netloc not in ('microsoft.com','www.microsoft.com','login.microsoftonline.com'):
        raise ValueError('A belépési cím nem ismert Microsoft-cím.')
    wait = max(5,int(data.get('interval',5)))
    ticks = time.monotonic()
    DEVICE.update(endpoint=endpoint,client=client,device=data['device_code'],
                  expires=ticks+int(data['expires_in']),interval=wait,next=ticks+wait)
    return {'url':url,'code':data['user_code'],'seconds':int(data['expires_in'])}


def poll_signin():
    ticks = time.monotonic()
    if not DEVICE or ticks>=DEVICE['expires']:
        DEVICE.clear()
        raise ValueError('A Microsoft-belépési kód lejárt.')
    if ticks < DEVICE['next']:
        return {'pending':True}
    DEVICE['next'] = ticks+DEVICE['interval']
    body = urlencode({'grant_type':'urn:ietf:params:oauth:grant-type:device_code',
                      'client_id':DEVICE['client'],'device_code':DEVICE['device']}).encode()
    req = Request(DEVICE['endpoint']+'token',body,{'Content-Type':'application/x-www-form-urlencoded'})
    try:
        with build_opener(core.NoRedirect).open(req,timeout=20) as response:
            token = json.loads(response.read(100_000))
    except HTTPError as exc:
        error = json.loads(exc.read(100_000)).get('error','')
        if error=='authorization_pending':
            return {'pending':True}
        if error=='slow_down':
            DEVICE['interval'] += 5
            DEVICE['next'] = ticks+DEVICE['interval']
            return {'pending':True}
        DEVICE.clear()
        raise ValueError('A Microsoft-belépés sikertelen vagy az eszközkódos belépést a szervezet tiltja.') from None
    core.apply_token(token)
    DEVICE.clear()
    return {'pending':False}


def dispatch(action, data):
    if action=='state':
        return core.snapshot()
    if action=='forecast':
        return core.scenario(*core.forecast_args(data))
    if action=='refresh':
        return {'ok':True, 'changed':core.refresh(force=bool(data.get('force')),local_only=bool(data.get('localOnly')))}
    if action=='config':
        configure(data)
    elif action=='manual':
        manual(data)
    elif action=='score':
        score(data)
    elif action=='import':
        import_workbook(data)
    elif action=='export':
        write_export(data)
    elif action=='sugar':
        sugar(data)
    elif action=='signin':
        return start_signin()
    elif action=='signin-poll':
        return poll_signin()
    elif action=='signin-cancel':
        DEVICE.clear()
    elif action=='update-check':
        return updates.check()
    elif action=='update-install':
        return updates.install(data['version'])
    else:
        raise ValueError('Ismeretlen asztali művelet.')
    return {'ok':True}


def main():
    sys.stdin.reconfigure(encoding='utf-8')
    sys.stdout.reconfigure(encoding='utf-8',line_buffering=True)
    try:
        core.load()
        failure = None
    except (OSError,RuntimeError,ValueError) as exc:
        failure = str(exc)
    for line in sys.stdin:
        try:
            if failure:
                raise ValueError(failure)
            message = json.loads(line)
            if message['action']=='quit':
                break
            reply = dispatch(message['action'],message.get('data',{}))
            answer = {'value':reply}
        except (ValueError,KeyError,OSError,TypeError) as exc:
            answer = {'error':str(exc)}
        except Exception:
            answer = {'error':'Váratlan hiba a művelet közben.'}
        print(json.dumps(answer,ensure_ascii=False,allow_nan=False))


if __name__=='__main__':
    main()

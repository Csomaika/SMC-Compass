"""Installer for release assets from the project's public GitHub repository."""
import hashlib
import io
import json
import os
from pathlib import Path, PurePosixPath
import re
import shutil
import tempfile
from urllib.error import HTTPError
from urllib.parse import urlsplit
from urllib.request import HTTPRedirectHandler, Request, build_opener
from zipfile import ZipFile

import app as core

REPOSITORY_ID = 1468819222
ZIP_ASSET = 'SMC-Compass-Windows.zip'
MSI_ASSET = 'SMC-Compass-Setup.msi'
MAX_DOWNLOAD = 45 * 1024 * 1024
VERSION_PATTERN = r'[0-9]{1,5}\.[0-9]{1,5}\.[0-9]{1,5}'
HOSTS = {'api.github.com','github.com','release-assets.githubusercontent.com','objects.githubusercontent.com'}


def version(value):
    clean = str(value).strip().removeprefix('v')
    if not re.fullmatch(VERSION_PATTERN,clean):
        raise ValueError('Érvénytelen programverzió.')
    return clean


def current():
    path = core.HERE.parent/'version.txt'
    if not path.is_file():
        path = core.HERE/'version.txt'
    return version(path.read_text(encoding='utf-8-sig'))


def github_only(url):
    item = urlsplit(url)
    return item.scheme=='https' and item.netloc in HOSTS


class GithubRedirect(HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, message, headers, url):
        if not github_only(url):
            raise ValueError('A kiadás letöltési címe nem GitHub-cím.')
        return super().redirect_request(req,fp,code,message,headers,url)


def download(url, limit, metadata=False):
    if not github_only(url):
        raise ValueError('Érvénytelen GitHub-cím.')
    headers = {'User-Agent':'SMC-Compass-Windows'}
    if metadata:
        headers.update({'Accept':'application/vnd.github+json','X-GitHub-Api-Version':'2022-11-28'})
    with build_opener(GithubRedirect).open(Request(url,headers=headers),timeout=25) as reply:
        data = reply.read(limit+1)
    if len(data)>limit:
        raise ValueError('A GitHub válasza túl nagy.')
    return data


def asset_name():
    return MSI_ASSET if (core.HERE.parent/'msi-install.txt').is_file() else ZIP_ASSET


def release():
    try:
        data = download(f'https://api.github.com/repositories/{REPOSITORY_ID}/releases/latest',1_000_000,True)
    except HTTPError as exc:
        if exc.code==404:
            return None
        raise ValueError(f'A GitHub nem érhető el ({exc.code}).') from None
    item = json.loads(data)
    tag = item['tag_name']
    published = version(tag)
    package = asset_name()
    page = item.get('html_url') or ''
    slug = re.fullmatch(r'https://github\.com/([A-Za-z0-9-]+/[A-Za-z0-9_.-]+)/releases/tag/'+re.escape(tag), page)
    asset = None
    if slug:
        canonical_url = f'https://github.com/{slug[1]}/releases/download/{tag}/{package}'
        asset = next((entry for entry in item.get('assets',[])
                      if entry.get('name')==package and entry.get('state')=='uploaded'
                      and entry.get('browser_download_url')==canonical_url), None)
    return {'version':published,'notes':str(item.get('body') or '')[:16000], 'asset':asset}


def check():
    installed = current()
    found = release()
    if not found:
        return {'current':installed,'available':False,'published':False}
    newer = tuple(map(int,found['version'].split('.')))>tuple(map(int,installed.split('.')))
    packaged = found['asset'] is not None
    return {'current':installed,'latest':found['version'],'available':newer and packaged,
            'missingPackage':newer and not packaged,'published':True,'notes':found['notes']}


def extract(content, stage, expected):
    with ZipFile(io.BytesIO(content)) as archive:
        if sum(item.file_size for item in archive.infolist())>140*1024*1024:
            raise ValueError('A frissítőcsomag mérete túl nagy.')
        for item in archive.infolist():
            path = PurePosixPath(item.filename)
            if (path.is_absolute() or not path.parts or path.parts[0]!='SMC-Compass'
                    or any(p in ('.','..') or ':' in p or '\\' in p for p in path.parts)
                    or re.search(r'(^|/)(compass-profile\.json|compass\.json)$',item.filename,re.I)):
                raise ValueError('A frissítőcsomag tiltott fájlútvonalat tartalmaz.')
            dest = stage.joinpath(*path.parts)
            if item.is_dir():
                dest.mkdir(parents=True,exist_ok=True)
                continue
            dest.parent.mkdir(parents=True,exist_ok=True)
            with archive.open(item) as archive_file,dest.open('xb') as output:
                shutil.copyfileobj(archive_file,output)
    package = stage/'SMC-Compass'
    if (not (package/'SMC-Compass.exe').is_file() or not (package/'runtime'/'python.exe').is_file()
            or not (package/'app'/'desktop.py').is_file() or version((package/'version.txt').read_text(encoding='utf-8-sig'))!=expected):
        raise ValueError('A frissítőcsomag hiányos vagy eltér a kiadás verziójától.')
    return package


def install(expected):
    expected = version(expected)
    package_name = asset_name()
    entry = release()
    if not entry or entry['version']!=expected or tuple(map(int,expected.split('.')))<=tuple(map(int,current().split('.'))):
        raise ValueError('A kiadás közben megváltozott. Ellenőrizd újra a frissítést.')
    asset = entry['asset']
    if not asset:
        raise ValueError('A GitHub-kiadásból hiányzik az ellenőrizhető Windows-csomag.')
    digest = asset.get('digest') or ''
    if not re.fullmatch(r'sha256:[0-9a-f]{64}',digest):
        raise ValueError('A kiadásban nincs ellenőrizhető SHA-256 lenyomat.')
    content = download(asset['browser_download_url'],MAX_DOWNLOAD)
    if hashlib.sha256(content).hexdigest()!=digest[7:]:
        raise ValueError('A letöltött csomag lenyomata eltér a GitHub-kiadásétól.')
    if package_name == MSI_ASSET:
        folder = core.HOME/'updates'
        folder.mkdir(parents=True,exist_ok=True)
        installer = folder/MSI_ASSET
        with tempfile.TemporaryDirectory(prefix='compass-msi-',dir=folder) as temp:
            pending = Path(temp)/MSI_ASSET
            pending.write_bytes(content)
            os.replace(pending,installer)
        return {'version':expected,'msi':str(installer)}

    versions = core.HOME/'versions'
    versions.mkdir(parents=True,exist_ok=True)
    final = versions/expected
    with tempfile.TemporaryDirectory(prefix='compass-update-',dir=versions) as temp:
        package = extract(content,Path(temp),expected)
        profile = core.HERE/'compass-profile.json'
        if profile.is_file():
            shutil.copy2(profile,package/'app'/'compass-profile.json')
        if not final.exists():
            package.rename(final)
        elif not (final/'SMC-Compass.exe').is_file() or version((final/'version.txt').read_text(encoding='utf-8-sig'))!=expected:
            raise ValueError('A célverzió mappája már létezik és hiányos.')
        pointer = Path(temp)/'active-version.txt'
        pointer.write_text(expected,encoding='ascii')
        os.replace(pointer,core.HOME/'active-version.txt')
    return {'version':expected,'exe':str(final/'SMC-Compass.exe')}

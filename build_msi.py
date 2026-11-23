"""Build a standard Windows Installer package with WiX using the existing payload."""
import argparse
import hashlib
from pathlib import Path
import re
import subprocess
import tempfile
from xml.etree.ElementTree import Element, SubElement, ElementTree
from zipfile import ZipFile


def part_id(prefix, path):
    return prefix + hashlib.sha256(path.casefold().encode()).hexdigest()[:24]


def wix_source(payload, version, target):
    wix = Element('Wix', {'xmlns':'http://wixtoolset.org/schemas/v4/wxs'})
    package = SubElement(wix,'Package',Name='SMC Compass',Manufacturer='SMC Compass',Version=version,
                         UpgradeCode='9BE797E3-A89A-46F7-B0E3-060EDF950399',Scope='perMachine',InstallerVersion='500')
    SubElement(package,'MajorUpgrade',Schedule='afterInstallInitialize',DowngradeErrorMessage='A newer version of SMC Compass is already installed.')
    SubElement(package,'MediaTemplate',EmbedCab='yes')
    SubElement(package,'Icon',Id='CompassIcon',SourceFile=str(payload/'Compass.ico'))
    SubElement(package,'Property',Id='ARPPRODUCTICON',Value='CompassIcon')
    SubElement(package,'StandardDirectory',Id='ProgramMenuFolder')
    programs = SubElement(package,'StandardDirectory',Id='ProgramFiles64Folder')
    root = SubElement(programs,'Directory',Id='INSTALLFOLDER',Name='SMC Compass')
    feature = SubElement(package,'Feature',Id='MainFeature',Title='SMC Compass',Level='1')
    directories = {'.':root}
    for folder in sorted((p for p in payload.rglob('*') if p.is_dir()),key=lambda p:(len(p.parts),str(p))):
        rel = folder.relative_to(payload)
        directories[rel.as_posix()] = SubElement(directories[rel.parent.as_posix()],'Directory',
                                                Id=part_id('d',rel.as_posix()),Name=folder.name)
    for file in sorted(p for p in payload.rglob('*') if p.is_file()):
        rel = file.relative_to(payload)
        name = rel.as_posix()
        component_id = part_id('c',name)
        component = SubElement(directories[rel.parent.as_posix()],'Component',Id=component_id,Guid='*',Bitness='always64')
        resource = SubElement(component,'File',Id=part_id('f',name),Source=str(file),KeyPath='yes')
        if name=='SMC-Compass.exe':
            SubElement(resource,'Shortcut',Id='CompassStartMenu',Directory='ProgramMenuFolder',Name='SMC Compass',
                       WorkingDirectory='INSTALLFOLDER',Advertise='yes')
        SubElement(feature,'ComponentRef',Id=component_id)
    ElementTree(wix).write(target,encoding='utf-8',xml_declaration=True)


def build(payload_zip, output, wix):
    with tempfile.TemporaryDirectory(prefix='compass-msi-') as temp:
        temp = Path(temp)
        with ZipFile(payload_zip) as zip_file:
            members = zip_file.namelist()
            if any(not member.startswith('SMC-Compass/') or '\\' in member or ':' in member or '..' in member.split('/') for member in members):
                raise ValueError('Unexpected payload path.')
            zip_file.extractall(temp)
        payload = temp/'SMC-Compass'
        version = (payload/'version.txt').read_text(encoding='utf-8-sig').strip()
        if not re.fullmatch(r'\d{1,3}\.\d{1,3}\.\d{1,5}',version):
            raise ValueError('Invalid Windows Installer version.')
        (payload/'msi-install.txt').write_text('Windows Installer\n',encoding='ascii')
        source = temp/'Compass.wxs'
        wix_source(payload,version,source)
        output.parent.mkdir(parents=True,exist_ok=True)
        subprocess.run([wix,'build','-arch','x64','-o',str(output),str(source)],check=True)
    print(output)


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--payload',required=True,type=Path)
    parser.add_argument('--output',required=True,type=Path)
    parser.add_argument('--wix',default='wix')
    args=parser.parse_args()
    build(args.payload,args.output.resolve(),args.wix)

"""Build the portable Windows package using a standard CPython 3.12 installation."""
import argparse
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
from zipfile import ZIP_DEFLATED, ZipFile


def build(output, profile):
    source = Path(__file__).resolve().parent
    python = Path(sys.executable).parent
    framework = Path(os.environ['WINDIR'])/'Microsoft.NET'/'Framework64'/'v4.0.30319'
    ignored = {'site-packages','__pycache__','test','tests','idlelib','tkinter','turtledemo','ensurepip','venv','distutils'}
    with tempfile.TemporaryDirectory(prefix='compass-build-') as stage:
        dist = Path(stage)/'SMC-Compass'
        runtime = dist/'runtime'
        app = dist/'app'
        runtime.mkdir(parents=True)
        app.mkdir()
        for name in ('python.exe','python312.dll','vcruntime140.dll','vcruntime140_1.dll','LICENSE.txt'):
            shutil.copy2(python/name,runtime/name)
        shutil.copytree(python/'DLLs',runtime/'DLLs',ignore=shutil.ignore_patterns('tcl*','tk*','_tkinter.pyd','*.lib','*.ico'))
        (runtime/'python312._pth').write_text('python312.zip\nDLLs\n.\n..\\app\n',encoding='ascii')
        lib = python/'Lib'
        with ZipFile(runtime/'python312.zip','w',ZIP_DEFLATED) as archive:
            for path in lib.rglob('*'):
                relative = path.relative_to(lib)
                if path.is_file() and not ignored.intersection(relative.parts):
                    archive.write(path,relative.as_posix())
        for name in ('app.py','desktop.py','updates.py','workbook.py'):
            shutil.copy2(source/name,app/name)
        for name in ('version.txt','CHANGELOG.md'):
            shutil.copy2(source/name,dist/name)
        shutil.copy2(source/'README.md',dist/'OLVASS-EL.md')
        if profile:
            shutil.copy2(profile,app/'compass-profile.json')
        version = (source/'version.txt').read_text(encoding='utf-8').strip()
        assembly = Path(stage)/'Version.cs'
        assembly.write_text('using System.Reflection;\n[assembly: AssemblyTitle("SMC Compass")]\n'
                            f'[assembly: AssemblyVersion("{version}.0")]\n[assembly: AssemblyFileVersion("{version}.0")]\n',encoding='utf-8')
        subprocess.run([str(framework/'csc.exe'),'/nologo','/target:winexe','/platform:x64',
                        '/reference:System.Windows.Forms.dll','/reference:System.Drawing.dll','/reference:System.Web.Extensions.dll',
                        f'/out:{dist / "SMC-Compass.exe"}',str(assembly),*[str(source/name) for name in ('Launcher.cs','DesktopWindow.cs','ForecastChart.cs')]],check=True)
        output.parent.mkdir(parents=True,exist_ok=True)
        with ZipFile(output,'w',ZIP_DEFLATED) as archive:
            for path in sorted(dist.rglob('*')):
                if path.is_file():
                    archive.write(path,path.relative_to(dist.parent).as_posix())
    with ZipFile(output) as archive:
        if archive.testzip() is not None:
            raise RuntimeError('A ZIP ellenőrzése sikertelen.')
    print(output)


if __name__ == '__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output',type=Path,default=Path('dist/SMC-Compass-Windows.zip'))
    parser.add_argument('--profile',type=Path,help='Optional local configuration to include in a private build')
    options=parser.parse_args()
    if sys.platform!='win32' or sys.version_info[:2]!=(3,12) or sys.maxsize<=2**32:
        parser.error('A csomagoláshoz 64 bites Windows és 64 bites CPython 3.12 szükséges.')
    build(options.output.resolve(),options.profile)

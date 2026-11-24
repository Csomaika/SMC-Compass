"""Exercise the packaged WebView2 loader and post-login redirect on Windows CI."""
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from threading import Thread
import os
import subprocess
import sys
import tempfile

from test_refresh import WorkbookRefreshTest

payload = WorkbookRefreshTest.workbook(1500)


class Site(BaseHTTPRequestHandler):
    requests_for_file = 0
    interactive_requests = 0

    def do_GET(self):
        if self.path == '/interactive':
            Site.interactive_requests += 1
            self.send_response(200)
            if Site.interactive_requests==1:
                self.send_header('Content-Type','text/html')
                data=b'<html>Account selection</html>'
            else:
                self.send_header('Content-Disposition','attachment; filename=Sales.xlsx')
                self.send_header('Content-Type','application/octet-stream')
                data=payload
        elif self.path == '/download':
            Site.requests_for_file += 1
            if Site.requests_for_file == 1:
                self.send_response(302)
                self.send_header('Location', '/landing')
                self.end_headers()
                return
            self.send_response(200)
            self.send_header('Content-Disposition', 'attachment; filename=Sales.xlsx')
            self.send_header('Content-Type', 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet')
            data = payload
        else:
            self.send_response(200)
            self.send_header('Content-Type', 'text/html')
            data = b'<html>Signed in</html>'
        self.send_header('Content-Length', str(len(data)))
        self.end_headers()
        self.wfile.write(data)


def verify(folder):
    csc = Path(os.environ['WINDIR'])/'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
    with tempfile.TemporaryDirectory() as temp, ThreadingHTTPServer(('127.0.0.1',0), Site) as server:
        dest = Path(temp)/'Sales.xlsx'
        source = Path(temp)/'Smoke.cs'
        source.write_text('''
using System;
using System.Windows.Forms;
internal class Launcher {
    internal static string DataFolder = Environment.GetEnvironmentVariable("COMPASS_DATA");
    [STAThread] static int Main(string[] args) {
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
        using(var form = new Form()) {
            form.Shown += async (s,e) => {
                try {
                    await SalesDownload.Fetch(form,args[0],args[1],false,()=>false);
                    using(var timer=new Timer { Interval=200 }) {
                        timer.Tick+=(sender,ev)=>{
                            foreach(Control child in form.Controls) {
                                var pane=child as AccountPane;
                                if(pane==null || !pane.Visible || Application.OpenForms.Count!=1)continue;
                                foreach(Control entry in pane.Bar.Controls) {
                                    var button=entry as Button;
                                    if(button!=null && button.Text=="Letöltés folytatása"){timer.Stop();button.PerformClick();return;}
                                }
                            }
                        };
                        timer.Start();
                        await SalesDownload.Fetch(form,args[0].Replace("/download","/interactive"),args[1]+".second",true,()=>false,"user@example.org",false);
                    }
                }
                catch(Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode=1; }
                finally { form.Close(); }
            };
            Application.Run(form);
        }
        return Environment.ExitCode;
    }
}''',encoding='utf-8')
        exe = folder/'SalesDownloadSmoke.exe'
        refs = [folder/('Microsoft.Web.WebView2.'+name+'.dll') for name in ('Core','WinForms')]
        subprocess.run([csc,'/nologo','/platform:x64','/reference:System.Windows.Forms.dll',
                        '/reference:System.Drawing.dll',*('/reference:'+str(ref) for ref in refs),
                        '/out:'+str(exe),'SalesDownload.cs',source],check=True)
        Thread(target=server.serve_forever,daemon=True).start()
        try:
            subprocess.run([exe,f'http://127.0.0.1:{server.server_port}/download',dest],check=True,timeout=100,
                           env=dict(os.environ,COMPASS_DATA=str(Path(os.environ['RUNNER_TEMP'])/'wv2-test')))
            if Path(str(dest)+'.second').read_bytes()!=payload or Site.interactive_requests!=2:
                raise AssertionError('Inline sign-in did not resume the private download.')
            if dest.read_bytes()!=payload or Site.requests_for_file!=2:
                raise AssertionError('The post-login download did not deliver the workbook.')
        finally:
            server.shutdown()


if __name__=='__main__':
    verify(Path(sys.argv[1]).resolve())

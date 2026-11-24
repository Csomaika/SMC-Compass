using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

internal static class SalesDownload
{
    internal static async Task Fetch(Form owner,string url,string path,bool interactive,Func<bool> canceled)
    {
        var options=new CoreWebView2EnvironmentOptions();
        options.AllowSingleSignOnUsingOSPrimaryAccount=true;
        var env=await CoreWebView2Environment.CreateAsync(null,Path.Combine(Launcher.DataFolder,"WebView2"),options);
        using(var login=new Form { Text="SMC Compass · SharePoint-bejelentkezés",Width=690,Height=660,StartPosition=FormStartPosition.CenterParent })
        using(var view=new WebView2 { Dock=DockStyle.Fill })
        {
            login.Controls.Add(view);
            // Create a window handle without displaying or activating the sign-in window.
            var handle=view.Handle;
            await view.EnsureCoreWebView2Async(env);
            var web=view.CoreWebView2;
            var done=new TaskCompletionSource<bool>();
            CoreWebView2DownloadOperation active=null;
            bool closed=false;
            login.FormClosing+=(s,e)=>{ e.Cancel=true;closed=true;login.Hide(); };
            web.NewWindowRequested+=(s,e)=>{e.Handled=true;};
            web.DownloadStarting+=(s,e)=>{
                e.Handled=true;
                if(active!=null||done.Task.IsCompleted){e.Cancel=true;return;}
                e.ResultFilePath=path;
                active=e.DownloadOperation;
                Action update=()=>{
                    if(active.State==CoreWebView2DownloadState.Completed)done.TrySetResult(true);
                    else if(active.State==CoreWebView2DownloadState.Interrupted)
                        done.TrySetException(new IOException("A SharePoint-letöltés megszakadt: "+active.InterruptReason));
                };
                active.StateChanged+=(sender,args)=>update();
                update();
            };
            web.NavigationCompleted+=(s,e)=>{
                if(done.Task.IsCompleted||active!=null||web.Source=="about:blank")return;
                // An attachment navigation can end in OperationCanceled before DownloadStarting.
                if(!e.IsSuccess && e.WebErrorStatus==CoreWebView2WebErrorStatus.OperationCanceled)return;
                if(interactive && !login.Visible)login.Show(owner);
                else if(!interactive)done.TrySetException(new InvalidOperationException(
                    "SharePoint-bejelentkezés vagy jóváhagyás szükséges. Kattints a Sales frissítése gombra."));
            };
            try
            {
                web.Navigate(url);
                var deadline=DateTime.UtcNow.AddMinutes(interactive?5:2);
                while(!done.Task.IsCompleted)
                {
                    if(closed||canceled())throw new OperationCanceledException("A Sales-frissítés megszakítva.");
                    if(DateTime.UtcNow>=deadline)throw new TimeoutException("Nem érkezett meg a Sales Excel. A Források fülön a külső böngészővel is próbálkozhatsz.");
                    await Task.WhenAny(done.Task,Task.Delay(350));
                }
                await done.Task;
            }
            finally
            {
                if(active!=null&&active.State==CoreWebView2DownloadState.InProgress)active.Cancel();
            }
        }
    }
}

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
            var resume=new Button { Dock=DockStyle.Bottom,Height=40,Text="Már bejelentkeztem · Sales letöltése" };
            login.Controls.Add(resume);
            var source=new Uri(url);
            bool retried=false;
            // Create a window handle without displaying or activating the sign-in window.
            var parentHandle=login.Handle;
            var handle=view.Handle;
            await view.EnsureCoreWebView2Async(env);
            var web=view.CoreWebView2;
            var done=new TaskCompletionSource<bool>();
            CoreWebView2DownloadOperation active=null;
            bool closed=false;
            DateTime? pageReady=null;
            web.NavigationStarting+=(s,e)=>{pageReady=null;};
            resume.Click+=(s,e)=>{ if(active==null&&!done.Task.IsCompleted)web.Navigate(url); };
            login.FormClosing+=(s,e)=>{ e.Cancel=true;closed=true;login.Hide(); };
            web.NewWindowRequested+=(s,e)=>{e.Handled=true;};
            web.DownloadStarting+=(s,e)=>{
                e.Handled=true;
                if(active!=null||done.Task.IsCompleted){e.Cancel=true;return;}
                e.ResultFilePath=path;
                active=e.DownloadOperation;
                login.Hide();
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
                Uri landing;
                if(e.IsSuccess && !retried && Uri.TryCreate(web.Source,UriKind.Absolute,out landing)
                    && source.Scheme==landing.Scheme && source.Authority==landing.Authority
                    && !String.Equals(source.AbsolutePath,landing.AbsolutePath,StringComparison.OrdinalIgnoreCase))
                {
                    // After sign-in SharePoint can return its home page instead of the attachment.
                    retried=true;
                    owner.BeginInvoke(new Action(()=>{if(active==null&&!done.Task.IsCompleted)web.Navigate(url);}));
                    return;
                }
                // Attachment navigations may finish before WebView2 raises DownloadStarting.
                pageReady=DateTime.UtcNow;
            };
            try
            {
                web.Navigate(url);
                var deadline=DateTime.UtcNow.AddMinutes(interactive?5:2);
                while(!done.Task.IsCompleted)
                {
                    if(active==null && pageReady.HasValue && (DateTime.UtcNow-pageReady.Value).TotalSeconds>=4)
                    {
                        pageReady=null;
                        if(!interactive)throw new InvalidOperationException("SharePoint-bejelentkezés vagy jóváhagyás szükséges. Kattints a Sales frissítése gombra.");
                        if(!login.Visible)login.Show(owner);
                    }
                    if(closed||canceled())throw new OperationCanceledException("A Sales-frissítés megszakítva.");
                    if(DateTime.UtcNow>=deadline)throw new TimeoutException("Nem érkezett meg a Sales Excel. A Források fülön a külső böngészővel is próbálkozhatsz.");
                    await Task.WhenAny(done.Task,Task.Delay(350));
                }
                await done.Task;
                await Task.Yield(); // Leave the WebView2 callback before disposing the control.
            }
            finally
            {
                if(active!=null&&active.State==CoreWebView2DownloadState.InProgress)active.Cancel();
            }
        }
    }
}

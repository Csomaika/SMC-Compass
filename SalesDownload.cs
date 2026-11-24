using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

internal sealed class AccountPane : Panel
{
    internal readonly WebView2 View=new WebView2 { Dock=DockStyle.Fill };
    internal readonly Label Title=new Label { Text="Microsoft · céges bejelentkezés",AutoSize=true,
        Margin=new Padding(12,12,15,6) };
    internal readonly FlowLayoutPanel Bar=new FlowLayoutPanel { Dock=DockStyle.Top,Height=49,
        BackColor=System.Drawing.Color.White,WrapContents=false };
    internal bool Canceled { get; private set; }

    internal AccountPane(Control host)
    {
        Dock=DockStyle.Fill;Visible=false;
        var cancel=new Button {Text="Mégse",AutoSize=true,Margin=new Padding(6,7,6,4)};
        cancel.Click+=(s,e)=>{Canceled=true;Hide();};
        Bar.Controls.Add(Title);Bar.Controls.Add(cancel);
        Controls.Add(View);Controls.Add(Bar);
        host.Controls.Add(this);
    }

    internal void Reveal() { BringToFront();Show();View.Focus(); }

    internal async Task Initialize(string email,bool remember)
    {
        var options=new CoreWebView2EnvironmentOptions { AllowSingleSignOnUsingOSPrimaryAccount=true };
        var env=await CoreWebView2Environment.CreateAsync(null,Path.Combine(Launcher.DataFolder,"WebView2"),options);
        var parentHandle=Handle;
        var childHandle=View.Handle;
        await View.EnsureCoreWebView2Async(env);
        var web=View.CoreWebView2;
        web.Profile.IsPasswordAutosaveEnabled=remember;
        web.NewWindowRequested+=(s,e)=>{e.Handled=true;};
        if(!String.IsNullOrWhiteSpace(email))
        {
            // Only fill a blank username on the Microsoft sign-in origin. Passwords stay in WebView2.
            string encoded=Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(email));
            await web.AddScriptToExecuteOnDocumentCreatedAsync(
                "if(window===window.top && location.origin==='https://login.microsoftonline.com'){"+
                "const email=new TextDecoder().decode(Uint8Array.from(atob('"+encoded+"'),c=>c.charCodeAt(0)));"+
                "let tries=0;const tick=setInterval(()=>{const field=document.querySelector('input[type=email]');"+
                "if(field&&field.offsetWidth){clearInterval(tick);if(!field.value){field.value=email;"+
                "field.dispatchEvent(new Event('input',{bubbles:true}));field.dispatchEvent(new Event('change',{bubbles:true}));}}"+
                "else if(++tries>60)clearInterval(tick);},250);}");
        }
    }
}

internal static class SalesDownload
{
    internal static async Task Fetch(Control owner,string url,string path,bool interactive,Func<bool> canceled,string email="",bool remember=false)
    {
        using(var login=new AccountPane(owner))
        {
            var resume=new Button { AutoSize=true,Text="Letöltés folytatása",Margin=new Padding(6,7,6,4) };
            login.Bar.Controls.Add(resume);
            var source=new Uri(url);
            bool retried=false;
            await login.Initialize(email,remember);
            var web=login.View.CoreWebView2;
            var done=new TaskCompletionSource<bool>();
            CoreWebView2DownloadOperation active=null;
            DateTime? pageReady=null;
            web.NavigationStarting+=(s,e)=>{pageReady=null;};
            resume.Click+=(s,e)=>{ if(active==null&&!done.Task.IsCompleted)web.Navigate(url); };
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
                        if(!interactive)throw new InvalidOperationException("SharePoint-bejelentkezés vagy jóváhagyás szükséges. Kattints az Adatfrissítés gombra.");
                        if(!login.Visible)login.Reveal();
                    }
                    if(login.Canceled||canceled())throw new OperationCanceledException("A Sales-frissítés megszakítva.");
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

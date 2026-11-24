using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

internal static class AnimationCheck
{
    private sealed class PaintSamples
    {
        private readonly string label;
        private readonly List<double> frames=new List<double>();

        internal PaintSamples(Control control,string label,Stopwatch clock)
        {
            this.label=label;
            control.Paint+=(sender,args)=>{if(clock.IsRunning)frames.Add(clock.Elapsed.TotalMilliseconds);};
        }

        internal void Check()
        {
            if(frames.Count<6)throw new InvalidOperationException(label+": too few rendered frames.");
            double fps=(frames.Count-1)*1000/(frames[frames.Count-1]-frames[0]);
            Console.WriteLine("{0}: {1:F1} FPS ({2} frames)",label,fps,frames.Count);
            if(fps<30)throw new InvalidOperationException(label+": rendering dropped below 30 FPS.");
        }
    }

    [STAThread]
    private static int Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        if(CompassStyle.MotionEnabled)return Measure();

        // The hosted Windows runner starts with client-area animations disabled.
        const uint setClientAreaAnimation=0x1043;
        if(!SystemParametersInfo(setClientAreaAnimation,0,new IntPtr(1),0))
        {
            Console.Error.WriteLine("Could not enable animations for the test session.");
            return 1;
        }
        try
        {
            if(!CompassStyle.MotionEnabled)
            {
                Console.Error.WriteLine("Windows animations are disabled during the frame-rate check.");
                return 1;
            }
            return Measure();
        }
        finally { SystemParametersInfo(setClientAreaAnimation,0,IntPtr.Zero,0); }
    }

    [DllImport("user32.dll")]
    private static extern bool SystemParametersInfo(uint action,uint parameter,IntPtr value,uint flags);

    private static int Measure()
    {
        int result=1;
        using(var window=new Form { ClientSize=new Size(1240,760), ShowInTaskbar=false })
        {
            var hero=new HeroPanel { Bounds=new Rectangle(215,12,1000,145) };
            var chart=new ForecastChart { Bounds=new Rectangle(215,280,1000,430) };
            var card=new KpiCard("Tény YTD","↗") { Bounds=new Rectangle(215,169,255,100) };
            var nav=new NavButton("Áttekintés","▤") { Location=new Point(10,12) };
            var strip=new RefreshStrip();
            window.Controls.AddRange(new Control[]{hero,chart,card,nav,strip});
            var clock=new Stopwatch();
            var samples=new[]{
                new PaintSamples(hero,"Source pulse",clock),
                new PaintSamples(chart,"Forecast chart",clock),
                new PaintSamples(card,"KPI card",clock),
                new PaintSamples(nav,"Navigation",clock),
                new PaintSamples(strip,"Refresh strip",clock)
            };
            window.Shown+=async (sender,args)=>{
                try
                {
                    await Task.Delay(250);
                    clock.Start();
                    chart.SetData(Json.Fields("actual",new object[]{120,220,364,462,628,774,938,null,null,null,null,null},
                        "plan",new object[]{167,333,500,667,833,1000,1167,1333,1500,1667,1833,2000},
                        "forecast",new object[]{null,null,null,null,null,null,938,1115,1285,1445,1660,1870},
                        "low",new object[]{null,null,null,null,null,null,938,1074,1212,1350,1513,1650},
                        "high",new object[]{null,null,null,null,null,null,938,1156,1358,1540,1807,2090}));
                    hero.Connected=true;
                    hero.Animate();
                    nav.Active=true;
                    card.Display(938,"Okt · 7 hónap",DesktopWindow.Blue);
                    strip.SetWorking(true);
                    await Task.Delay(1250);
                    clock.Stop();
                    foreach(var item in samples)item.Check();
                    result=0;
                }
                catch(Exception error) { Console.Error.WriteLine(error); }
                finally { window.Close(); }
            };
            Application.Run(window);
        }
        return result;
    }
}

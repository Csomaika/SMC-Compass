using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

internal static class CompassStyle
{
    internal static readonly Color Navy=Color.FromArgb(8,30,55), Border=Color.FromArgb(220,230,241),
        Pale=Color.FromArgb(234,243,252), GreenPale=Color.FromArgb(229,247,242);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SystemParametersInfo(uint action, uint parameter, out int enabled, uint flags);

    internal static bool MotionEnabled
    {
        get
        {
            int enabled;
            return !SystemInformation.HighContrast && SystemParametersInfo(0x1042,0,out enabled,0) && enabled!=0;
        }
    }

    internal static GraphicsPath Round(RectangleF box,float radius)
    {
        var path=new GraphicsPath();
        float d=Math.Min(radius*2,Math.Min(box.Width,box.Height));
        path.AddArc(box.X,box.Y,d,d,180,90);
        path.AddArc(box.Right-d,box.Y,d,d,270,90);
        path.AddArc(box.Right-d,box.Bottom-d,d,d,0,90);
        path.AddArc(box.X,box.Bottom-d,d,d,90,90);
        path.CloseFigure();
        return path;
    }

    internal static Color Mix(Color from,Color to,double fraction)
    {
        return Color.FromArgb((int)(from.R+(to.R-from.R)*fraction),(int)(from.G+(to.G-from.G)*fraction),(int)(from.B+(to.B-from.B)*fraction));
    }
}

internal sealed class AnimationFrames : IDisposable
{
    // Share one UI timer across active controls. Elapsed time still drives each animation.
    private static readonly Timer timer = new Timer { Interval = 15 };
    private static Action frame;
    private readonly Action advance;
    private bool enabled;

    static AnimationFrames()
    {
        timer.Tick += (sender, args) => { var pending = frame; if (pending != null) pending(); };
    }

    internal AnimationFrames(Action advance)
    {
        this.advance=advance;
    }

    internal bool Enabled
    {
        get { return enabled; }
        set
        {
            if(enabled==value)return;
            enabled=value;
            if(enabled)frame+=advance;
            else frame-=advance;
            timer.Enabled=frame!=null;
        }
    }

    public void Dispose() { Enabled=false; }
}

internal class CardPanel : Panel
{
    internal Color Fill=Color.White;
    internal Color Outline=CompassStyle.Border;
    internal int Radius=13;

    internal CardPanel()
    {
        BackColor=Color.Transparent;
        SetStyle(ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        base.OnPaintBackground(e);
        if(Width<3||Height<3)return;
        e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
        using(var shape=CompassStyle.Round(new RectangleF(.6f,.6f,Width-1.3f,Height-1.3f),Radius))
        using(var brush=new SolidBrush(Fill))
        using(var pen=new Pen(Outline))
        {
            e.Graphics.FillPath(brush,shape);
            e.Graphics.DrawPath(pen,shape);
        }
    }
}

internal sealed class HeroPanel : Panel
{
    private readonly AnimationFrames reveal;
    private readonly Stopwatch watch=new Stopwatch();
    private double light=1, phase;
    private readonly Stopwatch ambient = new Stopwatch();
    private bool connected;
    internal bool Connected
    {
        get { return connected; }
        set { connected=value; SyncMotion(); }
    }
    internal string Headline="Értékesítési előrejelzés", Subline="", Source="Adatforrás betöltése";

    internal HeroPanel()
    {
        BackColor=Color.Transparent;
        DoubleBuffered=true;
        ResizeRedraw=true;
        reveal=new AnimationFrames(()=>{
            if(!CanAnimate()){SyncMotion();return;}
            light=Math.Min(1,watch.Elapsed.TotalMilliseconds/520);
            phase=(ambient.Elapsed.TotalMilliseconds%2800)/2800;
            if(light>=1&&!connected)reveal.Enabled=false;
            Invalidate(new Rectangle(Math.Max(0,Width-330),0,Math.Min(330,Width),Height));
        });
    }

    internal void Animate()
    {
        light=0;watch.Restart();SyncMotion();
    }

    private bool CanAnimate()
    {
        var owner=FindForm();
        return Visible&&IsHandleCreated&&owner!=null&&owner.WindowState!=FormWindowState.Minimized&&CompassStyle.MotionEnabled;
    }

    internal void SyncMotion()
    {
        if(!CanAnimate())
        {
            reveal.Enabled=false;ambient.Stop();light=1;phase=0;
        }
        else if(connected||light<1)
        {
            ambient.Start();reveal.Enabled=true;
        }
        else {reveal.Enabled=false;ambient.Stop();}
        Invalidate();
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if(reveal!=null)SyncMotion();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if(Width<5||Height<5)return;
        var g=e.Graphics;
        g.SmoothingMode=SmoothingMode.AntiAlias;
        using(var shape=CompassStyle.Round(new RectangleF(0,0,Width-1,Height-1),16))
        using(var gradient=new LinearGradientBrush(ClientRectangle,Color.FromArgb(12,49,88),Color.FromArgb(8,113,181),11))
        {
            g.FillPath(gradient,shape);
            var clip=g.Save();g.SetClip(shape);
            var center=new PointF(Width-107,-18);
            float offset=(float)((1-light)*25);
            for(int i=0;i<6;i++)
            {
                float radius=57+i*24+offset;
                using(var pen=new Pen(Color.FromArgb(22+i*2,214,239,255),1))g.DrawEllipse(pen,center.X-radius,center.Y-radius,radius*2,radius*2);
            }
            // A soft echo expands across the contour lines while the source is connected.
            if(connected&&phase>0)
            {
                float radius=58+(float)phase*121;
                using(var echo=new Pen(Color.FromArgb((int)(62*Math.Sin(Math.PI*phase)),98,234,238),2))
                    g.DrawEllipse(echo,center.X-radius,center.Y-radius,radius*2,radius*2);
            }
            g.Restore(clip);
        }
        using(var small=new Font("Segoe UI",10,FontStyle.Bold))
        using(var heading=new Font("Segoe UI",27,FontStyle.Bold))
        using(var subtitle=new Font("Segoe UI",10.5f))
        {
            Draw(g,"ÉRTÉKESÍTÉSI ELŐREJELZÉS",small,Color.FromArgb(180,220,251),new Rectangle(23,17,Width-46,24));
            Draw(g,Headline,heading,Color.White,new Rectangle(21,41,Width-42,48));
            Draw(g,Subline,subtitle,Color.FromArgb(218,240,255),new Rectangle(23,92,Math.Max(75,Width-225),29));
            int length=Math.Min(175,TextRenderer.MeasureText(Source,small).Width+28);
            var pill=new RectangleF(Width-length-18,94,length,25);
            using(var outline=CompassStyle.Round(pill,12))
            using(var tint=new SolidBrush(Color.FromArgb(88,4,40,71)))
            using(var pen=new Pen(Color.FromArgb(85,212,238,255)))
            using(var dot=new SolidBrush(Connected?Color.FromArgb(71,221,182):Color.FromArgb(255,192,103)))
            {
                g.FillPath(tint,outline);g.DrawPath(pen,outline);
                if(connected&&phase>0)
                {
                    float r=4+(float)phase*5;
                    using(var halo=new Pen(Color.FromArgb((int)(155*(1-phase)),105,255,224),1.6f))
                        g.DrawEllipse(halo,pill.X+11.5f-r,pill.Y+12.5f-r,2*r,2*r);
                }
                g.FillEllipse(dot,pill.X+8,pill.Y+9,7,7);
            }
            Draw(g,Source,small,Color.White,new Rectangle((int)pill.X+19,(int)pill.Y,Math.Max(4,(int)pill.Width-23),25));
        }
    }

    internal static void Draw(Graphics g,string text,Font font,Color color,Rectangle rect)
    {
        TextRenderer.DrawText(g,text??"",font,rect,color,TextFormatFlags.EndEllipsis|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPadding|TextFormatFlags.SingleLine);
    }

    protected override void Dispose(bool disposing){if(disposing)reveal.Dispose();base.Dispose(disposing);}
}

internal sealed class KpiCard : Control
{
    private readonly AnimationFrames frames;
    private readonly Stopwatch clock=new Stopwatch();
    private readonly string label,symbol;
    private double from;
    private double? value;
    private string note="";
    private Color accent=DesktopWindow.Blue;
    private double progress=1;
    private double CurrentAmount { get { return from+((value??0)-from)*(1-Math.Pow(1-progress,3)); } }

    internal KpiCard(string label,string symbol)
    {
        this.label=label;this.symbol=symbol;
        AccessibleName=label;
        BackColor=DesktopWindow.Cloud;
        DoubleBuffered=true;ResizeRedraw=true;
        frames=new AnimationFrames(()=>{progress=Math.Min(1,clock.Elapsed.TotalMilliseconds/430);if(progress>=1)frames.Enabled=false;Invalidate();});
    }

    internal void Display(double? amount,string description,Color color)
    {
        bool changed=amount!=value;
        if(changed)from=value.HasValue?CurrentAmount:0;
        value=amount;note=description;accent=color;
        AccessibleDescription=DesktopWindow.Format(value,"db")+" · "+note;
        if(changed&&IsHandleCreated&&Visible&&CompassStyle.MotionEnabled){progress=0;clock.Restart();frames.Enabled=true;}
        else if(changed){progress=1;frames.Enabled=false;}
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if(Width<12||Height<12)return;
        var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;
        using(var shape=CompassStyle.Round(new RectangleF(.6f,.6f,Width-1.3f,Height-1.3f),12))
        using(var pen=new Pen(CompassStyle.Border))
        {
            g.FillPath(Brushes.White,shape);g.DrawPath(pen,shape);
        }
        using(var tag=CompassStyle.Round(new RectangleF(Width-42,12,29,29),8))
        using(var color=new SolidBrush(CompassStyle.Pale))g.FillPath(color,tag);
        using(var caption=new Font("Segoe UI",9,FontStyle.Bold))
        using(var figures=new Font("Segoe UI",23,FontStyle.Bold))
        using(var detail=new Font("Segoe UI",8.5f))
        {
            HeroPanel.Draw(g,label.ToUpperInvariant(),caption,DesktopWindow.Muted,new Rectangle(13,10,Math.Max(1,Width-57),23));
            HeroPanel.Draw(g,symbol,caption,DesktopWindow.Blue,new Rectangle(Width-34,13,22,25));
            string result=value.HasValue?DesktopWindow.Format(Math.Round(CurrentAmount),"db"):"—";
            if(label=="Eltérés a céltól"&&value>0)result="+"+result;
            HeroPanel.Draw(g,result,figures,accent,new Rectangle(12,35,Width-25,42));
            HeroPanel.Draw(g,note,detail,DesktopWindow.Muted,new Rectangle(13,74,Width-26,23));
        }
    }

    protected override void Dispose(bool disposing){if(disposing)frames.Dispose();base.Dispose(disposing);}
}

internal sealed class NavButton : Button
{
    private readonly AnimationFrames transition;
    private readonly Stopwatch easeClock=new Stopwatch();
    private bool active;
    private double level, startLevel;
    internal bool Active
    {
        get {return active;}
        set
        {
            if(active==value)return;
            active=value;startLevel=level;
            if(Visible&&IsHandleCreated&&CompassStyle.MotionEnabled){easeClock.Restart();transition.Enabled=true;}
            else {transition.Enabled=false;level=active?1:0;}
            Invalidate();
        }
    }
    private readonly string mark;
    private bool hot;

    internal NavButton(string label,string mark)
    {
        Text=label;this.mark=mark; AccessibleName=label;
        transition=new AnimationFrames(()=>{
            double t=Math.Min(1,easeClock.Elapsed.TotalMilliseconds/280);
            level=startLevel+((active?1:0)-startLevel)*(1-Math.Pow(1-t,3));
            if(t>=1)transition.Enabled=false;
            Invalidate();
        });
        Width=195;Height=45;FlatStyle=FlatStyle.Flat;FlatAppearance.BorderSize=0;
        ForeColor=Color.White;BackColor=CompassStyle.Navy;
        Font=new Font("Segoe UI",10,FontStyle.Bold);
        Cursor=Cursors.Hand;
        DoubleBuffered=true;
    }

    protected override void OnMouseEnter(EventArgs e){base.OnMouseEnter(e);hot=true;Invalidate();}
    protected override void OnMouseLeave(EventArgs e){base.OnMouseLeave(e);hot=false;Invalidate();}
    protected override void OnGotFocus(EventArgs e){base.OnGotFocus(e);Invalidate();}
    protected override void OnLostFocus(EventArgs e){base.OnLostFocus(e);Invalidate();}

    protected override void OnPaint(PaintEventArgs e)
    {
        var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;g.Clear(CompassStyle.Navy);
        if(level>0||hot||Focused)
        {
            using(var frame=CompassStyle.Round(new RectangleF(0,1,Width-1,Height-2),8))
            using(var fill=new SolidBrush(CompassStyle.Mix(hot||Focused?Color.FromArgb(20,51,80):CompassStyle.Navy,Color.FromArgb(19,76,117),level)))g.FillPath(fill,frame);
        }
        if(level>0)
        {
            float half=(float)((Height/2-7)*level);
            using(var glow=new Pen(Color.FromArgb((int)(255*level),83,224,233),3))
                g.DrawLine(glow,3,Height/2-half,3,Height/2+half);
        }
        Color text=Active||hot?Color.White:Color.FromArgb(184,207,230);
        HeroPanel.Draw(g,mark,Font,Color.FromArgb(139,195,245),new Rectangle(13,1,24,Height-2));
        HeroPanel.Draw(g,Text,Font,text,new Rectangle(42,1,Width-48,Height-2));
    }
    protected override void Dispose(bool disposing){if(disposing)transition.Dispose();base.Dispose(disposing);}
}

// Indeterminate: SharePoint and local workbook reads do not expose a percentage.
internal sealed class RefreshStrip : Control
{
    private readonly AnimationFrames frames;
    private readonly Stopwatch clock=new Stopwatch();
    private bool working;
    private double phase;

    internal RefreshStrip()
    {
        Dock=DockStyle.Bottom;Height=4;TabStop=false;BackColor=Color.White;
        DoubleBuffered=true;AccessibleRole=AccessibleRole.ProgressBar;AccessibleName="Adatfrissítés folyamatban";
        frames=new AnimationFrames(()=>{phase=(clock.Elapsed.TotalMilliseconds%1500)/1500;Invalidate();});
        Visible=false;
    }

    internal void SetWorking(bool value)
    {
        working=value;Visible=value;
        if(value){BringToFront();clock.Restart();}else clock.Reset();
        SyncMotion();
    }

    internal void SyncMotion()
    {
        var owner=FindForm();
        bool moving=working&&Visible&&owner!=null&&owner.WindowState!=FormWindowState.Minimized&&CompassStyle.MotionEnabled;
        frames.Enabled=moving;
        if(!moving)phase=.5;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if(Width<1||Height<1)return;
        var g=e.Graphics;
        g.Clear(CompassStyle.Pale);
        float w=Math.Max(48,Width*.34f);
        var segment=new RectangleF((float)((Width+w)*phase)-w,0,w,Height);
        using(var brush=new LinearGradientBrush(segment,DesktopWindow.Blue,Color.FromArgb(46,214,202),0f))
            g.FillRectangle(brush,segment);
    }

    protected override void Dispose(bool disposing){if(disposing)frames.Dispose();base.Dispose(disposing);}
}

internal sealed class NativePages : TabControl
{
    internal NativePages(){Dock=DockStyle.Fill;Appearance=TabAppearance.FlatButtons;ItemSize=new Size(0,1);SizeMode=TabSizeMode.Fixed;TabStop=false;}
    public override Rectangle DisplayRectangle { get { return new Rectangle(0,0,ClientSize.Width,ClientSize.Height); } }
    protected override void WndProc(ref Message m)
    {
        const int AdjustRect=0x1328;
        if(m.Msg==AdjustRect&&!DesignMode){m.Result=(IntPtr)1;return;}
        base.WndProc(ref m);
    }
}

// The nine buttons keep native keyboard focus and activation.
internal sealed class MatrixTile : Button
{
    private static readonly Color Accent=Color.FromArgb(8,111,158);
    private static readonly Color Teal=Color.FromArgb(12,135,151);
    private readonly AnimationFrames frames;
    private readonly Stopwatch watch=new Stopwatch();
    private static readonly string[] Names={
        "Figyelés","Alapkapcsolat","Kiszolgálás",
        "Feltérképezés","Fejlesztés","Megtartás",
        "Belépés","Pozícióerősítés","Bővítés"
    };
    private static readonly string[] Descriptions={
        "Kövesd időnként a vevő változásait. Akkor keresd meg, ha új igény vagy projekt jelenik meg.",
        "Tartsd fenn a meglévő kapcsolatot. A konkrét igényekre adj célzott választ.",
        "Tartsd meg a meglévő forgalmat, és tedd egyszerűvé az ismétlődő rendelések kiszolgálását.",
        "Térképezd fel a döntéshozókat és a belépésre alkalmas alkalmazásokat.",
        "Válassz egy konkrét projektet, ahol próbaegységgel vagy műszaki támogatással léphetsz előre.",
        "Védd a meglévő üzletet, és keresd a kapcsolódó alkalmazásokat a vevőn belül.",
        "A nagy lehetőséghez keress első belépési projektet. Referenciával vagy próbaegységgel indíts.",
        "Növeld az SMC részesedését további alkalmazásokban, üzletágakban vagy telephelyeken.",
        "Egyeztess közös növekedési tervet a vevővel, és rangsoroljátok a következő projekteket."
    };
    private readonly Font titleFont=new Font("Segoe UI",11,FontStyle.Bold);
    private readonly Font detailFont=new Font("Segoe UI",9,FontStyle.Bold);

    internal string Number { get; private set; }
    internal string Title { get; private set; }
    internal string Advice { get; private set; }
    internal string Scores { get; private set; }
    internal string Coordinates { get; private set; }
    internal string Heading { get { return Number+" · "+Title; } }
    private int count, total;
    private double fill, from, target;
    private bool hot, selected;

    internal MatrixTile(int potential,int position)
    {
        int index=(potential-1)*3+position-1;
        Number=(index+1).ToString("00");
        Title=Names[index];
        Advice=Descriptions[index];
        Coordinates=potential+" × "+position;
        Scores="Piaci potenciál "+potential+"/3 · SMC-pozíció "+position+"/3";
        Dock=DockStyle.Fill;
        Margin=new Padding(4,3,4,3);
        BackColor=Color.White;
        FlatStyle=FlatStyle.Flat;
        Cursor=Cursors.Hand;
        DoubleBuffered=true;
        ResizeRedraw=true;
        frames=new AnimationFrames(()=>{
            double progress=Math.Min(1,watch.Elapsed.TotalMilliseconds/300);
            fill=from+(target-from)*(1-Math.Pow(1-progress,3));
            if(progress>=1)frames.Enabled=false;
            Invalidate();
        });
    }

    internal void Display(int count,int total,bool selected)
    {
        this.count=count;this.total=total;this.selected=selected;
        AccessibleName=Heading+" · "+Scores+" · "+count+" vevő";
        AccessibleDescription=Advice+" "+(selected?"Aktív szűrés. Aktiválással törölhető.":"Aktiválással erre a mezőre szűr.");
        double next=total>0?(double)count/total:0;
        if(!Visible||!IsHandleCreated||!CompassStyle.MotionEnabled)
        {
            target=fill=next;frames.Enabled=false;
        }
        else if(target!=next)
        {
            from=fill;target=next;watch.Restart();frames.Enabled=true;
        }
        Invalidate();
    }

    protected override void OnMouseEnter(EventArgs e){base.OnMouseEnter(e);hot=true;Invalidate();}
    protected override void OnMouseLeave(EventArgs e){base.OnMouseLeave(e);hot=false;Invalidate();}
    protected override void OnGotFocus(EventArgs e){base.OnGotFocus(e);Invalidate();}
    protected override void OnLostFocus(EventArgs e){base.OnLostFocus(e);Invalidate();}

    protected override void OnPaint(PaintEventArgs e)
    {
        if(Width<28||Height<32)return;
        var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;g.Clear(BackColor);
        Color tint=selected?Color.FromArgb(219,244,246):CompassStyle.Mix(Color.FromArgb(247,250,254),Color.FromArgb(151,218,232),Math.Min(1,Math.Sqrt(fill)*1.30));
        Color outline=selected?Teal:Focused||hot?Accent:Color.FromArgb(219,231,243);
        using(var path=CompassStyle.Round(new RectangleF(1,1,Width-2,Height-2),10))
        using(var surface=new SolidBrush(tint))
        using(var edge=new Pen(outline,selected?2f:1f))
        {
            g.FillPath(surface,path);g.DrawPath(edge,path);
        }
        using(var badge=CompassStyle.Round(new RectangleF(10,5,26,18),5))
        {
            g.FillPath(selected?Brushes.Teal:Brushes.MidnightBlue,badge);
            HeroPanel.Draw(g,Number,detailFont,Color.White,new Rectangle(15,5,20,18));
        }
        HeroPanel.Draw(g,Title,titleFont,DesktopWindow.Ink,new Rectangle(10,22,Width-20,22));
        DrawRight(g,Coordinates,selected?Accent:DesktopWindow.Muted,new Rectangle(Width-54,5,43,18));
        if(Height>=61)
        {
            string share=total>0?(100.0*count/total).ToString("0.#")+"%":"0%";
            HeroPanel.Draw(g,count+" vevő · "+share,detailFont,DesktopWindow.Muted,new Rectangle(10,43,Width-20,15));
        }
        float width=Width-22;
        using(var rail=new SolidBrush(Color.FromArgb(220,233,243)))
        using(var bar=new SolidBrush(Teal))
        {
            g.FillRectangle(rail,11,Height-8,width,3);
            if(fill>0)g.FillRectangle(bar,11,Height-8,Math.Max(2,(float)(fill*width)),3);
        }
    }

    private void DrawRight(Graphics g,string value,Color ink,Rectangle bounds)
    {
        TextRenderer.DrawText(g,value,detailFont,bounds,ink,TextFormatFlags.Right|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPadding);
    }

    protected override void Dispose(bool disposing)
    {
        if(disposing){frames.Dispose();titleFont.Dispose();detailFont.Dispose();}
        base.Dispose(disposing);
    }
}

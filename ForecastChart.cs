using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

internal sealed class CompassMark : Control
{
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;
        g.ScaleTransform(Width/64f,Height/64f);
        using(var shape=new GraphicsPath())
        {
            shape.AddArc(0,0,24,24,180,90);shape.AddArc(40,0,24,24,270,90);
            shape.AddArc(40,40,24,24,0,90);shape.AddArc(0,40,24,24,90,90);shape.CloseFigure();
            using(var background=new SolidBrush(DesktopWindow.Blue))g.FillPath(background,shape);
        }
        using(var light=new Pen(Color.FromArgb(162,216,255),1.8f))
        {
            g.DrawEllipse(light,13,13,38,38);
            g.DrawLine(light,32,7,32,16);g.DrawLine(light,32,48,32,57);
            g.DrawLine(light,7,32,16,32);g.DrawLine(light,48,32,57,32);
        }
        g.FillPolygon(Brushes.White,new[]{new Point(44,20),new Point(37,37),new Point(20,45),new Point(26,27)});
        using(var tint=new SolidBrush(Color.FromArgb(105,188,251)))g.FillPolygon(tint,new[]{new Point(20,45),new Point(32,32),new Point(37,37)});
    }
}

internal sealed class ForecastChart : Control
{
    private readonly Font title=new Font("Segoe UI",12,FontStyle.Bold);
    private readonly Font small=new Font("Segoe UI",9);
    private readonly Timer animation=new Timer{Interval=25};
    private readonly Stopwatch clock=new Stopwatch();
    private readonly ToolTip tip=new ToolTip();
    private readonly Color actualColor=Color.FromArgb(8,106,194), forecastColor=Color.FromArgb(6,129,114),
        planColor=Color.FromArgb(117,141,169), previousColor=Color.FromArgb(164,187,213), bandColor=Color.FromArgb(176,229,220);
    private Dictionary<string,object> series=Json.Fields();
    private int hover=-1;
    private double progress=1;

    internal ForecastChart()
    {
        BackColor=Color.White;
        DoubleBuffered=true;
        ResizeRedraw=true;
        AccessibleName="Halmozott tény, terv és forecast hónaponként";
        animation.Tick+=(s,e)=>{
            progress=Math.Min(1,clock.Elapsed.TotalMilliseconds/350.0);
            if(progress>=1)animation.Stop();
            Invalidate();
        };
    }

    internal void SetData(Dictionary<string,object> values)
    {
        series=values;
        if(Visible&&IsHandleCreated&&SystemInformation.IsMenuAnimationEnabled)
        {
            progress=.1;clock.Restart();animation.Start();
        }
        else {animation.Stop();progress=1;}
        Invalidate();
    }

    private float LeftEdge { get { return 67; } }
    private float RightEdge { get { return Math.Max(LeftEdge+20,Width-31); } }
    private float X(int index) { return LeftEdge+index*(RightEdge-LeftEdge)/11; }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int index=e.X<LeftEdge-12||e.X>RightEdge+12||e.Y<46||e.Y>Height-51?-1:
            Math.Max(0,Math.Min(11,(int)Math.Round((e.X-LeftEdge)*11/(RightEdge-LeftEdge))));
        if(index==hover)return;
        hover=index;
        if(hover<0)tip.Hide(this);
        else tip.Show(String.Format("{0}  ·  Tény {1}  ·  Terv {2}  ·  Forecast {3}",DesktopWindow.Months[hover],
            DesktopWindow.Format(Value("actual",hover)),DesktopWindow.Format(Value("plan",hover)),DesktopWindow.Format(Value("forecast",hover))),this,
            Math.Max(2,Math.Min(e.X+14,Width-360)),Math.Max(2,e.Y-30),2100);
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);hover=-1;tip.Hide(this);Invalidate();
    }

    private double? Value(string key,int index)
    {
        var items=Json.Items(series,key);
        return index>=0&&index<items.Count?Json.Number(items[index]):null;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if(Width<250||Height<130)return;
        var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;
        float top=49,bottom=Height-59;
        var numbers=new[]{"actual","plan","low","high","prev"}.SelectMany(key=>Json.Items(series,key)).Select(Json.Number).Where(n=>n.HasValue).Select(n=>n.Value).ToList();
        double min=numbers.Count==0?0:Math.Min(0,numbers.Min());
        double max=numbers.Count==0?1:Math.Max(1,numbers.Max());
        double stepBase=(max-min)/4;
        double power=Math.Pow(10,Math.Floor(Math.Log10(stepBase)));
        double step=new[]{1,2,2.5,5,10}.Select(k=>k*power).FirstOrDefault(k=>k>=stepBase);
        if(step<=0)step=1;
        double lower=Math.Floor(min/step)*step,upper=Math.Ceiling(max/step)*step;
        Func<double,float> y = val=>bottom-(float)((val-lower)/(upper-lower))*(bottom-top);
        using(var ink=new SolidBrush(DesktopWindow.Ink))
        using(var dim=new SolidBrush(DesktopWindow.Muted))
        using(var light=new Pen(Color.FromArgb(222,233,244),1))
        {
            g.DrawString("Tény, terv és forecast",title,ink,16,11);
            if(numbers.Count==0)g.DrawString("Havi tényadatot a Havi adatbevitel vagy a Források fülön adhatsz meg.",small,dim,19,34);
            for(double n=lower;n<=upper+step*.00001;n+=step)
            {
                float py=y(n);g.DrawLine(light,LeftEdge,py,RightEdge,py);
                string label=DesktopWindow.Format(n);
                g.DrawString(label,small,dim,LeftEdge-g.MeasureString(label,small).Width-7,py-7);
            }
            for(int i=0;i<12;i++)
            {
                string name=DesktopWindow.Months[i];
                g.DrawString(name,small,dim,X(i)-g.MeasureString(name,small).Width/2,bottom+7);
            }
            var shade=new List<PointF>();
            for(int i=0;i<12;i++){var item=Value("high",i);if(item.HasValue)shade.Add(new PointF(X(i),y(item.Value)));}
            for(int i=11;i>=0;i--){var item=Value("low",i);if(item.HasValue)shade.Add(new PointF(X(i),y(item.Value)));}
            if(shade.Count>=4)using(var tint=new SolidBrush(Color.FromArgb((int)(150*progress),bandColor)))g.FillPolygon(tint,shade.ToArray());
            Curve(g,"prev",previousColor,1.6f,DashStyle.Dot,y,false);
            Curve(g,"plan",planColor,2,DashStyle.Dash,y,false);
            Curve(g,"forecast",forecastColor,2.5f,DashStyle.Dash,y,false);
            Curve(g,"actual",actualColor,3,DashStyle.Solid,y,true);
            if(hover>=0)using(var line=new Pen(Color.FromArgb(130,DesktopWindow.Blue),1){DashStyle=DashStyle.Dash})g.DrawLine(line,X(hover),top,X(hover),bottom);
            var legend=new[]{"Tény","Terv","Forecast","Előző FY","Forgatókönyv-tartomány"};
            var colors=new[]{actualColor,planColor,forecastColor,previousColor,bandColor};
            float p=16;
            for(int i=0;i<legend.Length;i++)
            {
                using(var chip=new Pen(colors[i],i==4?6:2.4f))g.DrawLine(chip,p,Height-21,p+18,Height-21);
                g.DrawString(legend[i],small,dim,p+23,Height-29);
                p+=g.MeasureString(legend[i],small).Width+44;
            }
        }
    }

    private void Curve(Graphics g,string name,Color color,float weight,DashStyle dash,Func<double,float> y,bool points)
    {
        using(var pen=new Pen(Color.FromArgb((int)(255*progress),color),weight){DashStyle=dash,LineJoin=LineJoin.Round})
        using(var dot=new SolidBrush(color))
        {
            PointF? previous=null;
            for(int i=0;i<12;i++)
            {
                var v=Value(name,i);
                if(!v.HasValue){previous=null;continue;}
                PointF current=new PointF(X(i),y(v.Value));
                if(previous.HasValue)g.DrawLine(pen,previous.Value,current);
                if(points)g.FillEllipse(dot,current.X-3,current.Y-3,6,6);
                previous=current;
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        if(disposing){animation.Dispose();tip.Dispose();title.Dispose();small.Dispose();}
        base.Dispose(disposing);
    }
}

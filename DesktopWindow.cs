using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

internal sealed class DesktopWindow : Form
{
    internal static readonly Color Ink = Color.FromArgb(16,39,68);
    internal static readonly Color Blue = Color.FromArgb(8,106,194);
    internal static readonly Color Muted = Color.FromArgb(78,102,130);
    internal static readonly Color Cloud = Color.FromArgb(239,244,250);
    internal static readonly Color Teal = Color.FromArgb(7,127,113);
    private static readonly CultureInfo Hu = CultureInfo.GetCultureInfo("hu-HU");
    internal static readonly string[] Months = {"Ápr","Máj","Jún","Júl","Aug","Szept","Okt","Nov","Dec","Jan","Feb","Márc"};
    private readonly Worker worker;
    internal string PendingInstaller { get; private set; }
    private readonly Timer timer = new Timer { Interval = 15000 };
    private DateTime nextRefresh;
    private Dictionary<string,object> state = Json.Fields();
    private Dictionary<string,object> forecast = Json.Fields();
    private bool busy, binding, browserSalesActive, cancelBrowserSales;
    private bool browserNamePromptDismissed;
    private int year;
    private string metric = "ofsoldelectricactuators", method = "blend", boxFilter = "";
    private int spread = 10;

    private readonly Panel workspace=new Panel { Dock=DockStyle.Fill,Margin=new Padding(0) };
    private readonly NativePages tabs = new NativePages();
    private readonly HeroPanel hero = new HeroPanel { Dock=DockStyle.Fill, Margin=new Padding(3,3,4,9) };
    private readonly Label breadcrumb = Caption("PSD   /   Áttekintés",12,true);
    private readonly Label sidebarStatus = Caption("Források betöltése",9,false);
    private readonly NavButton[] navigation = new NavButton[6];
    private readonly Label foot = Caption("Indítás…",10,false);
    private readonly Label chartNote = Caption("",10,false);
    private readonly Label matrixNote = Caption("",10,false);
    private readonly Label sugarNote = Caption("",10,false);
    private readonly Label inputNote = Caption("",10,false);
    private readonly KpiCard[] cards = new KpiCard[4];
    private readonly Label[] scenarios = new Label[3];
    private readonly Label modelNote = Caption("",9,false);
    private readonly ForecastChart chart = new ForecastChart { Dock = DockStyle.Fill };
    private readonly ComboBox years = Picker(), metrics = Picker(), methods = Picker(), spreads = Picker(),
        entryMonth = Picker(), owners = Picker();
    private readonly CheckBox visible = new CheckBox { Text = "Csak az Excelben látható sorok", Checked = true, AutoSize = true, Margin = new Padding(10,11,8,0) };
    private readonly TextBox entryAmount = Edit(120), entryNote = Edit(240), target = Edit(125), newYear = Edit(74);
    private readonly TextBox buyerSearch = Edit(230), sugarSearch = Edit(230), sugarToken = Edit(260);
    private readonly DataGridView monthly = Grid(), buyers = Grid(), pipeline = Grid();
    private readonly Dictionary<string,Button> boxes = new Dictionary<string,Button>();
    private readonly Dictionary<string,Control> settings = new Dictionary<string,Control>();
    private readonly Label sourcesStatus = Caption("",10,false);
    private readonly TextBox revisions = new TextBox { Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle, Multiline = true,
        ReadOnly = true, ScrollBars = ScrollBars.Vertical, BackColor = Color.White, ForeColor = Ink, Font = new Font("Segoe UI",11) };
    private Button refreshBtn, exportBtn, updateBtn;

    private sealed class Pick
    {
        internal readonly string Value, Label;
        internal Pick(string value,string label) { Value=value; Label=label; }
        public override string ToString() { return Label; }
    }

    internal DesktopWindow(Worker worker,bool testing=false)
    {
        this.worker = worker;
        Text = "SMC Compass  ·  " + Launcher.Version;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1090,740);
        Size = new Size(1370,890);
        Font = new Font("Segoe UI",10);
        ForeColor = Ink;
        BackColor = Cloud;
        AutoScaleMode = AutoScaleMode.Dpi;
        string ico = Path.Combine(Launcher.Folder,"Compass.ico");
        if (File.Exists(ico)) Icon = new Icon(ico);

        var shell = new TableLayoutPanel { Dock=DockStyle.Fill, ColumnCount=2, RowCount=1, BackColor=Cloud };
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,226));shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        Controls.Add(shell);
        shell.Controls.Add(Sidebar(),0,0);
        var content = new TableLayoutPanel { Dock=DockStyle.Fill, ColumnCount=1, RowCount=3, BackColor=Cloud, Margin=new Padding(0) };
        content.RowStyles.Add(new RowStyle(SizeType.Absolute,69));
        content.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute,28));
        shell.Controls.Add(content,1,0);
        content.Controls.Add(Header(),0,0);
        workspace.Controls.Add(tabs);
        content.Controls.Add(workspace,0,1);
        foot.Dock = DockStyle.Fill;
        foot.TextAlign = ContentAlignment.MiddleLeft;
        foot.Padding=new Padding(16,0,6,0);
        content.Controls.Add(foot,0,2);

        BuildOverview(Page("Áttekintés"));
        BuildInput(Page("Havi adatbevitel"));
        BuildMatrix(Page("9-box vevőmátrix"));
        BuildPipeline(Page("Sugar pipeline"));
        BuildSources(Page("Források"));
        BuildRevisions(Page("Verziónapló"));
        tabs.SelectedIndexChanged+=(s,e)=>ShowPage();
        ShowPage();

        years.SelectedIndexChanged += (s,e) => SelectForecast();
        metrics.SelectedIndexChanged += (s,e) => SelectForecast();
        methods.SelectedIndexChanged += (s,e) => SelectForecast();
        spreads.SelectedIndexChanged += (s,e) => SelectForecast();
        entryMonth.SelectedIndexChanged += (s,e) => FillManualEntry();
        buyerSearch.TextChanged += (s,e) => { if (!binding) FillCustomers(); };
        owners.SelectedIndexChanged += (s,e) => { if (!binding) FillCustomers(); };
        sugarSearch.TextChanged += (s,e) => { if (!binding) FillPipeline(); };
        visible.CheckedChanged += (s,e) => { if (!binding) FillCustomers(); };
        if(!testing)Shown += (s,e) => Run(Initial);
        timer.Tick += (s,e) => {
            if(busy)return;
            if(DateTime.UtcNow>=nextRefresh)Run(() => RefreshData(false,false),true);
            else if(HasWatchedFile())Run(() => RefreshData(false,false,true),true);
        };
        FormClosing += (s,e) => { if (busy && e.CloseReason == CloseReason.UserClosing) { e.Cancel=true; foot.Text="Művelet folyamatban. Várd meg a befejezést."; } };
        FormClosed += (s,e) => timer.Dispose();
    }

    internal void RunLayoutTest(Dictionary<string,object> model, Dictionary<string,object> graph)
    {
        state = model;
        forecast = graph;
        year = 2026;
        FillSettings();
        FillState();
        RenderForecast();
        foreach (TabPage tab in tabs.TabPages) { tabs.SelectedTab = tab; tab.PerformLayout(); }
        tabs.SelectedIndex=0;
        PerformLayout();
        tabs.SelectedTab.PerformLayout();
        using (var image = new Bitmap(980,420)) chart.DrawToBitmap(image,new Rectangle(0,0,image.Width,image.Height));
        string capture=Environment.GetEnvironmentVariable("COMPASS_TEST_CAPTURE");
        if(!String.IsNullOrEmpty(capture))
        {
            StartPosition=FormStartPosition.Manual;
            Location=new Point(-Width-100,-Height-100);
            ShowInTaskbar=false;
            Show();
            Application.DoEvents();
            forecast=Json.Fields("label","Elektromos aktuátorok","source","REPORT26","count",7,"seasonal",true,"ending",1996,
                "actual",new object[]{194,385,554,671,840,997,1143,null,null,null,null,null},
                "plan",new object[]{213,427,640,853,1067,1280,1493,1707,1920,2133,2347,2560},
                "prev",new object[]{192,335,488,641,808,1005,1181,1314,1490,1650,1776,1960},
                "forecast",new object[]{null,null,null,null,null,null,1143,1301,1470,1634,1832,1996},
                "low",new object[]{null,null,null,null,null,null,1143,1285,1437,1585,1763,1911},
                "high",new object[]{null,null,null,null,null,null,1143,1317,1503,1683,1901,2081});
            RenderForecast();
            Application.DoEvents();
            using(var image=new Bitmap(Width,Height))
            {
                DrawToBitmap(image,new Rectangle(0,0,image.Width,image.Height));
                image.Save(capture,System.Drawing.Imaging.ImageFormat.Png);
            }
        }
    }

    private async Task Initial()
    {
        state = await worker.CallAsync("state");
        year = IntValue(Json.Get(Config,"fiscal"),2026);
        FillSettings();
        FillState();
        await FetchForecast();
        timer.Start();
        await RefreshData(false,false);
    }

    private async void Run(Func<Task> work,bool quiet=false)
    {
        if (busy || IsDisposed) return;
        busy = true;
        refreshBtn.Enabled = exportBtn.Enabled = updateBtn.Enabled = false;
        UseWaitCursor = !quiet;
        if(!quiet)foot.Text = "Művelet folyamatban…";
        try
        {
            await work();
            if (!IsDisposed && foot.Text == "Művelet folyamatban…") foot.Text = "Kész";
        }
        catch (Exception error)
        {
            if (!IsDisposed)
            {
                foot.Text = error.Message;
                if (Visible&&!quiet) MessageBox.Show(this,error.Message,"SMC Compass",MessageBoxButtons.OK,MessageBoxIcon.Information);
            }
        }
        finally
        {
            busy = false;
            if (!IsDisposed)
            {
                UseWaitCursor = false;
                refreshBtn.Enabled = exportBtn.Enabled = updateBtn.Enabled = true;
            }
        }
    }

    private Control Header()
    {
        var head = new Panel { Dock=DockStyle.Fill, BackColor=Color.White, Margin=new Padding(0,0,0,8) };
        head.Paint+=(s,e)=>{using(var line=new Pen(CompassStyle.Border))e.Graphics.DrawLine(line,0,head.Height-1,head.Width,head.Height-1);};
        breadcrumb.Location=new Point(21,18);
        head.Controls.Add(breadcrumb);
        var actions = new FlowLayoutPanel { Dock=DockStyle.Right, Width=370, FlowDirection=FlowDirection.RightToLeft, Padding=new Padding(0,9,15,0), WrapContents=false, BackColor=Color.White };
        exportBtn = Button("Excel-export",false);
        refreshBtn = Button("Adatfrissítés",true);
        exportBtn.Click += (s,e) => Run(Export);
        refreshBtn.Click += (s,e) => {
            if(browserSalesActive)cancelBrowserSales=true;
            else Run(() => RefreshData(true,true));
        };
        actions.Controls.Add(exportBtn);
        actions.Controls.Add(refreshBtn);
        head.Controls.Add(actions);
        return head;
    }

    private Control Sidebar()
    {
        var panel=new Panel { Dock=DockStyle.Fill, Margin=new Padding(0), BackColor=CompassStyle.Navy };
        panel.Controls.Add(new CompassMark(Icon) { Location=new Point(18,20), Size=new Size(43,43), BackColor=CompassStyle.Navy });
        var title=Caption("SMC Compass",16,true);title.ForeColor=Color.White;title.Location=new Point(69,19);panel.Controls.Add(title);
        var product=Caption("PSD SALES WORKSPACE",8,true);product.ForeColor=Color.FromArgb(128,177,216);product.Location=new Point(71,47);panel.Controls.Add(product);
        var labels=new[]{"Áttekintés","Havi adatbevitel","9-box vevőmátrix","Sugar pipeline","Források","Verziónapló"};
        var marks=new[]{"▤","✎","▦","◇","⚙","◷"};
        for(int i=0;i<labels.Length;i++)
        {
            int index=i;
            var button=new NavButton(labels[i],marks[i]) { Location=new Point(14,91+i*50) };
            button.Click+=(s,e)=>tabs.SelectedIndex=index;
            navigation[i]=button;panel.Controls.Add(button);
        }
        var bottom=new Panel { Dock=DockStyle.Bottom, Height=70, BackColor=CompassStyle.Navy };
        bottom.Paint+=(s,e)=>{using(var line=new Pen(Color.FromArgb(42,69,96)))e.Graphics.DrawLine(line,15,0,bottom.Width-15,0);};
        var space=Caption("Helyi munkaterület",10,true);space.ForeColor=Color.White;space.Location=new Point(19,12);bottom.Controls.Add(space);
        sidebarStatus.ForeColor=Color.FromArgb(149,183,212);sidebarStatus.Location=new Point(19,36);bottom.Controls.Add(sidebarStatus);
        panel.Controls.Add(bottom);
        return panel;
    }

    private void ShowPage()
    {
        if(tabs.SelectedIndex<0)return;
        foreach(var item in navigation)
        {
            if(item==null)continue;
            item.Active=Object.ReferenceEquals(item,navigation[tabs.SelectedIndex]);
            item.Invalidate();
        }
        breadcrumb.Text="PSD   /   "+tabs.SelectedTab.Text;
        if(tabs.SelectedIndex==0)hero.Animate();
    }

    private TabPage Page(string title)
    {
        var page = new TabPage(title) { BackColor = Cloud, Padding = new Padding(15,8,16,7) };
        tabs.TabPages.Add(page);
        return page;
    }

    private static TableLayoutPanel Frame(params int[] heights)
    {
        var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = heights.Length };
        for (int i=0;i<heights.Length;i++) table.RowStyles.Add(new RowStyle(heights[i]==0 ? SizeType.Percent : SizeType.Absolute, heights[i]==0 ? 100 : heights[i]));
        return table;
    }

    private static FlowLayoutPanel Row()
    {
        return new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, AutoScroll = true, Padding = new Padding(0,3,0,2) };
    }

    private static Label Caption(string text,float size,bool bold)
    {
        return new Label { Text = text, AutoSize = true, ForeColor = bold ? Ink : Muted, Font = new Font("Segoe UI",size,bold?FontStyle.Bold:FontStyle.Regular) };
    }

    private static Button Button(string label,bool primary)
    {
        var button = new Button { Text = label, AutoSize = true, MinimumSize = new Size(80,34), Padding = new Padding(9,1,9,1),
            BackColor = primary?Blue:Color.White, ForeColor = primary?Color.White:Ink, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand };
        button.FlatAppearance.BorderColor = primary?Blue:Color.FromArgb(205,220,237);
        return button;
    }

    private Button Action(string label, Func<Task> action, bool primary = false)
    {
        var button = Button(label,primary);
        button.Click += (s,e) => Run(action);
        return button;
    }

    private static TextBox Edit(int width)
    {
        return new TextBox { Width = width, BorderStyle = BorderStyle.FixedSingle, ForeColor = Ink, Margin = new Padding(3,7,5,3) };
    }

    private static ComboBox Picker()
    {
        return new ComboBox { Width = 138, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(3,7,5,3), ForeColor = Ink };
    }

    private static void AddField(FlowLayoutPanel row, string label, Control field)
    {
        var caption = Caption(label,9,false);
        caption.Margin = new Padding(10,10,1,0);
        row.Controls.Add(caption);
        row.Controls.Add(field);
    }

    private static DataGridView Grid()
    {
        var grid = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
            RowHeadersVisible = false, BorderStyle = BorderStyle.FixedSingle, GridColor = Color.FromArgb(219,229,241), BackgroundColor = Color.White,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false,
            EnableHeadersVisualStyles = false, AutoGenerateColumns = false };
        grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(221,234,249);
        grid.ColumnHeadersDefaultCellStyle.ForeColor = Ink;
        grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI",9,FontStyle.Bold);
        grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(212,233,252);
        grid.DefaultCellStyle.SelectionForeColor = Ink;
        grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(245,249,253);
        grid.RowTemplate.Height = 27;
        return grid;
    }

    private static void Columns(DataGridView grid, params string[] names)
    {
        for (int i=0;i<names.Length;i++) grid.Columns.Add("col"+i,names[i]);
    }

    private void BuildOverview(TabPage page)
    {
        var layout = Frame(141,55,111,0,32);
        page.Controls.Add(layout);
        layout.Controls.Add(hero,0,0);
        var choice = Row();
        years.Width=98; metrics.Width=211; methods.Width=164; spreads.Width=68;
        AddField(choice,"Üzleti év",years);
        AddField(choice,"Mutató",metrics);
        AddField(choice,"Módszer",methods);
        AddField(choice,"Eltérés",spreads);
        layout.Controls.Add(choice,0,1);
        var values = new TableLayoutPanel { ColumnCount = 4, RowCount = 1, Dock = DockStyle.Fill };
        string[] names = {"Tény YTD","Éves cél","Év végi forecast","Eltérés a céltól"};
        string[] marks = {"╱","◎","↗","↔"};
        for (int i=0;i<4;i++)
        {
            values.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,25));
            cards[i] = new KpiCard(names[i],marks[i]) { Dock=DockStyle.Fill, Margin=new Padding(3,1,6,8) };
            values.Controls.Add(cards[i],i,0);
        }
        layout.Controls.Add(values,0,2);
        var visuals=new TableLayoutPanel { Dock=DockStyle.Fill, ColumnCount=2, RowCount=1, Margin=new Padding(0) };
        visuals.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));visuals.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,254));
        var left=new CardPanel { Dock=DockStyle.Fill, Margin=new Padding(3,1,7,0), Padding=new Padding(6) };
        left.Controls.Add(chart);visuals.Controls.Add(left,0,0);
        visuals.Controls.Add(Scenarios(),1,0);
        layout.Controls.Add(visuals,0,3);
        chartNote.Dock=DockStyle.Fill;
        chartNote.TextAlign=ContentAlignment.MiddleLeft;
        layout.Controls.Add(chartNote,0,4);
    }

    private Control Scenarios()
    {
        var panel=new CardPanel { Dock=DockStyle.Fill, Margin=new Padding(1,1,4,0), Padding=new Padding(13,10,13,10) };
        var content=Frame(31,40,40,40,28,0);
        content.BackColor=Color.White;
        content.Controls.Add(Caption("Év végi forgatókönyvek",12,true),0,0);
        string[] names={"Óvatos","Alap","Erős"};
        for(int i=0;i<names.Length;i++)
        {
            var row=new CardPanel { Dock=DockStyle.Fill, Radius=7, Fill=i==1?CompassStyle.GreenPale:CompassStyle.Pale,
                Outline=i==1?CompassStyle.GreenPale:CompassStyle.Pale, Margin=new Padding(0,1,0,4) };
            var label=Caption(names[i],9,true);label.Location=new Point(8,8);row.Controls.Add(label);
            scenarios[i]=Caption("—",10,true);scenarios[i].Dock=DockStyle.Right;scenarios[i].TextAlign=ContentAlignment.MiddleRight;scenarios[i].Padding=new Padding(0,6,9,0);
            if(i==1)scenarios[i].ForeColor=Teal;
            row.Controls.Add(scenarios[i]);content.Controls.Add(row,0,i+1);
        }
        var title=Caption("SZÁMÍTÁSI MÓDSZER",8.5f,true);title.Margin=new Padding(1,8,0,0);content.Controls.Add(title,0,4);
        modelNote.AutoSize=false;modelNote.Dock=DockStyle.Fill;content.Controls.Add(modelNote,0,5);
        panel.Controls.Add(content);
        return panel;
    }

    private void BuildInput(TabPage page)
    {
        var layout = Frame(39,45,45,0);
        page.Controls.Add(layout);
        inputNote.Dock = DockStyle.Fill;
        inputNote.TextAlign = ContentAlignment.MiddleLeft;
        layout.Controls.Add(inputNote,0,0);
        var row = Row();
        AddField(row,"Hónap",entryMonth);
        AddField(row,"Tény (db)",entryAmount);
        AddField(row,"Megjegyzés",entryNote);
        row.Controls.Add(Action("Havi tény mentése",() => SaveManual("save"),true));
        row.Controls.Add(Action("Kézi érték törlése",() => SaveManual("remove")));
        layout.Controls.Add(row,0,1);
        var goal = Row();
        AddField(goal,"Éves cél (db)",target);
        goal.Controls.Add(Action("Cél mentése",() => SaveManual("target"),true));
        goal.Controls.Add(Action("Excel-cél visszaállítása",() => SaveManual("target-remove")));
        AddField(goal,"Másik üzleti év",newYear);
        goal.Controls.Add(Action("Megnyitás",OpenYear));
        layout.Controls.Add(goal,0,2);
        Columns(monthly,"Hónap","Havi tény (db)","Tény YTD (db)","Forrás","Megjegyzés");
        monthly.Columns[4].FillWeight=200;
        monthly.CellDoubleClick += (s,e) => { if (e.RowIndex>=0) entryMonth.SelectedIndex=e.RowIndex; };
        layout.Controls.Add(monthly,0,3);
    }

    private void BuildMatrix(TabPage page)
    {
        var layout=Frame(38,190,43,0);
        page.Controls.Add(layout);
        matrixNote.Dock=DockStyle.Fill; matrixNote.TextAlign=ContentAlignment.MiddleLeft;
        layout.Controls.Add(matrixNote,0,0);
        var upper = new TableLayoutPanel { Dock=DockStyle.Fill, ColumnCount=2, RowCount=1 };
        upper.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,64));
        upper.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,36));
        var board = new TableLayoutPanel { Dock=DockStyle.Fill, ColumnCount=3, RowCount=3 };
        for (int i=0;i<3;i++) { board.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100f/3)); board.RowStyles.Add(new RowStyle(SizeType.Percent,100f/3)); }
        for(int row=0;row<3;row++) for(int col=0;col<3;col++)
        {
            string key=(3-row)+"-"+(col+1);
            var square=Button("",false); square.Dock=DockStyle.Fill; square.Margin=new Padding(2); square.AutoSize=false;
            square.Click += (s,e) => { boxFilter=boxFilter==key?"":key;FillCustomers(); };
            boxes[key]=square;
            board.Controls.Add(square,col,row);
        }
        upper.Controls.Add(board,0,0);
        var directions = new Panel { Dock=DockStyle.Fill, BackColor=Color.White, Margin=new Padding(9,2,1,2) };
        var info=Caption("↑  Piaci potenciál\r\n→ SMC-pozíció\r\n\r\nÜgyfél szerkesztéséhez kattints\r\nduplán a vevő sorára.",10,false);
        info.Location=new Point(14,12); directions.Controls.Add(info);
        var all=Button("Összes csoport",false); all.Location=new Point(14,124); all.Click+=(s,e)=>{boxFilter="";FillCustomers();}; directions.Controls.Add(all);
        upper.Controls.Add(directions,1,0);
        layout.Controls.Add(upper,0,1);
        var filter=Row(); AddField(filter,"Keresés",buyerSearch);AddField(filter,"SAE",owners);filter.Controls.Add(visible);
        filter.Controls.Add(Action("Vevő értékelése",EditCustomer,true));
        layout.Controls.Add(filter,0,2);
        Columns(buyers,"Ügyfélkód","Vevő","Város","SAE","9-box","Következő lépés");
        buyers.Columns[1].FillWeight=180;buyers.Columns[5].FillWeight=160;
        buyers.CellDoubleClick+=(s,e)=>{if(e.RowIndex>=0)Run(EditCustomer);};
        layout.Controls.Add(buyers,0,3);
    }

    private void BuildPipeline(TabPage page)
    {
        var layout=Frame(44,70,0); page.Controls.Add(layout);
        var filter=Row(); AddField(filter,"Keresés",sugarSearch);
        var open=Button("Sugar beállítása",false);open.Click+=(s,e)=>tabs.SelectedIndex=4;filter.Controls.Add(open);
        layout.Controls.Add(filter,0,0);
        sugarNote.Dock=DockStyle.Fill; sugarNote.TextAlign=ContentAlignment.MiddleLeft;
        layout.Controls.Add(sugarNote,0,1);
        Columns(pipeline,"Lehetőség","Vevő","Státusz","Összeg","Valószínűség","Zárás","Felelős");
        pipeline.Columns[0].FillWeight=170;pipeline.Columns[1].FillWeight=145;
        layout.Controls.Add(pipeline,0,2);
    }

    private void BuildSources(TabPage page)
    {
        var container = new Panel { Dock = DockStyle.Fill, AutoScroll=true };
        page.Controls.Add(container);
        var list = new TableLayoutPanel { Dock=DockStyle.Top, AutoSize=true, ColumnCount=2, Padding=new Padding(4,3,12,8) };
        list.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,232)); list.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        container.Controls.Add(list);
        voidRow(list,sourcesStatus);
        var mode=Picker(); mode.Width=360;
        SetChoices(mode,new[]{new Pick("local","Helyi / OneDrive-szinkronizált fájl"),new Pick("sharepoint","Közvetlen SharePoint (Entra)"),new Pick("manual","Kézi Excel-import")},"local");
        Setting(list,"Alapértelmezett forrásmód","source",mode);
        WorkbookMode(list,"Sales forrásmód","salesSource");
        WorkbookMode(list,"SpeedUp forrásmód","buyersSource");
        Setting(list,"Online frissítés (perc)","minutes",Edit(85));
        Setting(list,"Üzleti év kezdete","fiscal",Edit(85));
        Setting(list,"Sales munkalap","salesSheet",Edit(165));
        Setting(list,"Előző éves munkalap","previousSheet",Edit(165));
        Setting(list,"SpeedUp munkalap","buyersSheet",Edit(165));
        PathSetting(list,"Sales helyi / OneDrive-fájl","salesPath");
        PathSetting(list,"SpeedUp helyi / OneDrive-fájl","buyersPath");
        Setting(list,"Céges bejelentkezési e-mail","accountEmail",Edit(325));
        Setting(list,"Jelszó mentése","rememberLogin",new CheckBox { AutoSize=true,Text="A WebView2 felajánlhatja a mentést ezen a gépen" });
        Setting(list,"Sales SharePoint-munkafüzet","salesUrl",Edit(600));
        Setting(list,"Böngészős Sales-fájlnév","salesDownloadName",Edit(300));
        Setting(list,"Böngésző letöltési mappája","salesDownloadFolder",Edit(420));
        Setting(list,"SpeedUp SharePoint-munkafüzet","buyersUrl",Edit(600));
        var imports=Row(); imports.AutoSize=true;imports.Controls.Add(Action("Beállítások mentése",SaveSources,true));
        imports.Controls.Add(Action("Sales XLSX importálása",()=>Import("sales")));
        imports.Controls.Add(Action("SpeedUp XLSX importálása",()=>Import("buyers")));
        voidRow(list,imports);
        voidRow(list,Action("Külső böngésző használata egyszer",()=>ImportSalesFromBrowser(false)));
        voidRow(list,Caption("Microsoft Entra · közvetlen SharePoint",13,true));
        Setting(list,"Microsoft tenant ID","tenant",Edit(450));
        Setting(list,"Microsoft client ID","client",Edit(450));
        voidRow(list,Action("Microsoft-belépés eszközkóddal",MicrosoftSignIn));
        voidRow(list,Caption("Sugar CRM · Opportunities",13,true));
        Setting(list,"Sugar HTTPS alapcím","sugarUrl",Edit(480));
        Setting(list,"Sugar REST-verzió","sugarApi",Edit(115));
        Setting(list,"Csak a saját lehetőségek","sugarMine",new CheckBox { AutoSize=true });
        sugarToken.UseSystemPasswordChar=true;
        Setting(list,"Sugar REST access token",null,sugarToken);
        voidRow(list,Action("Mentés és Sugar-import",SugarImport));
    }

    private static void voidRow(TableLayoutPanel list, Control content)
    {
        int row=list.RowCount++;
        content.Margin=new Padding(3,7,3,4);
        list.Controls.Add(content,0,row);
        list.SetColumnSpan(content,2);
    }

    private void Setting(TableLayoutPanel list,string label,string key,Control editor)
    {
        int row=list.RowCount++;
        var caption=Caption(label,10,false);caption.Margin=new Padding(3,8,4,5);
        list.Controls.Add(caption,0,row);
        list.Controls.Add(editor,1,row);
        if(key!=null)settings[key]=editor;
    }

    private void WorkbookMode(TableLayoutPanel list,string label,string key)
    {
        var mode=Picker();mode.Width=360;
        var choices = new List<Pick>{new Pick("","Alapértelmezett mód"),new Pick("local","Helyi / OneDrive-szinkronizált fájl"),
            new Pick("sharepoint","Közvetlen SharePoint (Entra)"),new Pick("manual","Kézi Excel-import")};
        if(key=="salesSource")choices.Add(new Pick("browser","SharePoint · időzített böngészős letöltés"));
        SetChoices(mode,choices,"");
        Setting(list,label,key,mode);
    }

    private void PathSetting(TableLayoutPanel list,string label,string key)
    {
        var box=Edit(499);
        var choose=Button("Tallózás…",false);
        choose.Click+=(s,e)=>{using(var file=OpenExcel()){
            if(file.ShowDialog(this)!=DialogResult.OK)return;
            box.Text=file.FileName;
            var mode=(ComboBox)settings[key=="salesPath"?"salesSource":"buyersSource"];
            mode.SelectedIndex=1;
        }};
        var field=Row();field.AutoSize=true;field.Controls.Add(box);field.Controls.Add(choose);
        Setting(list,label,null,field);settings[key]=box;
    }

    private void BuildRevisions(TabPage page)
    {
        var layout=Frame(52,0);page.Controls.Add(layout);
        var bar=Row();
        bar.Controls.Add(Caption("Telepített verzió  ·  "+Launcher.Version,18,true));
        updateBtn=Action("Programfrissítés",UpdateProgram);
        bar.Controls.Add(updateBtn);layout.Controls.Add(bar,0,0);
        string path=Path.Combine(Launcher.Folder,"CHANGELOG.md");
        if(File.Exists(path))
            revisions.Text=String.Join(Environment.NewLine,File.ReadAllLines(path).Skip(2).Select(line=>line.StartsWith("## ")?line.Substring(3):line.StartsWith("- ")?"• "+line.Substring(2):line));
        layout.Controls.Add(revisions,0,1);
    }

    private Dictionary<string,object> Config { get { return Json.Map(Json.Get(state,"config")); } }

    private static string Selection(ComboBox control)
    {
        var item=control.SelectedItem as Pick;
        return item==null?"":item.Value;
    }

    private static void SetChoices(ComboBox control,IEnumerable<Pick> items,string selected)
    {
        control.BeginUpdate();
        control.Items.Clear();
        foreach(var item in items)
        {
            int index=control.Items.Add(item);
            if(item.Value==selected) control.SelectedIndex=index;
        }
        if(control.SelectedIndex<0&&control.Items.Count>0) control.SelectedIndex=0;
        control.EndUpdate();
    }

    private static int IntValue(object obj,int fallback)
    {
        return (int)(Json.Number(obj) ?? fallback);
    }

    private static double? At(IList<object> values,int index)
    {
        return index>=0&&index<values.Count?Json.Number(values[index]):null;
    }

    internal static string Format(double? value,string unit="")
    {
        return value.HasValue?value.Value.ToString("#,0.##",Hu)+(unit.Length==0?"":" "+unit):"—";
    }

    private Dictionary<string,object> ReportArgs()
    {
        return Json.Fields("year",year,"metric",metric,"method",method,"spread",spread,"visible",visible.Checked?"1":"0");
    }

    private void SelectForecast()
    {
        if(binding||busy||years.SelectedIndex<0||metrics.SelectedIndex<0)return;
        year=IntValue(Selection(years),year);
        metric=Selection(metrics);
        method=Selection(methods);
        spread=IntValue(Selection(spreads),10);
        Run(FetchForecast);
    }

    private async Task RefreshState(bool settingsChanged)
    {
        state=await worker.CallAsync("state");
        if(settingsChanged)FillSettings();
        FillState();
    }

    private void FillState()
    {
        binding=true;
        var list=Json.Items(state,"years").Select(v=>IntValue(v,year)).Concat(new[]{year,IntValue(Json.Get(Config,"fiscal"),2026)+1}).Distinct().OrderByDescending(v=>v);
        SetChoices(years,list.Select(v=>new Pick(v.ToString(),"FY "+v)),year.ToString());
        SetChoices(metrics,Json.Map(Json.Get(state,"metrics")).Select(x=>new Pick(x.Key,(string)x.Value)),metric);
        SetChoices(methods,new[]{new Pick("blend","Szezonalitás + trend"),new Pick("run","3 havi átlag")},method);
        SetChoices(spreads,new[]{5,10,15,20,30}.Select(v=>new Pick(v.ToString(),"±"+v+"%")),spread.ToString());
        var owner=Selection(owners);
        var available=CustomerList().Select(x=>Json.Text(x,"owner")).Where(x=>x.Length>0).Distinct().OrderBy(x=>x);
        SetChoices(owners,new[]{new Pick("","Összes")}.Concat(available.Select(x=>new Pick(x,x))),owner);
        binding=false;
        FillCustomers();FillPipeline();FillSourceStatus();
        sidebarStatus.Text=Json.Text(Source("sales"),"mode","Forrás beállítása szükséges");
    }

    private async Task FetchForecast()
    {
        forecast=await worker.CallAsync("forecast",ReportArgs());
        RenderForecast();
    }

    private void RenderForecast()
    {
        string label=Json.Text(forecast,"label","Értékesítési előrejelzés");
        int count=IntValue(Json.Get(forecast,"count"),0);
        double? actual=At(Json.Items(forecast,"actual"),count-1), goal=At(Json.Items(forecast,"plan"),11), ending=Json.Number(Json.Get(forecast,"ending"));
        var sales=Source("sales");
        bool changed=hero.Headline!=label||hero.Subline.IndexOf("FY "+year+" ",StringComparison.Ordinal)<0;
        hero.Headline=label;
        hero.Subline=String.Format("FY {0}  ·  április {0} – március {1}  ·  {2}",year,year+1,Json.Text(forecast,"source"));
        hero.Source=Json.Text(sales,"mode","Forrás beállítása");hero.Connected=Json.Flag(sales,"ok");
        if(changed)hero.Animate();else hero.Invalidate();
        double? delta=ending-goal;
        cards[0].Display(actual,count>0?Months[count-1]+" · "+count+" adatolt hónap":"Havi tényadat szükséges",Ink);
        cards[1].Display(goal,Json.Get(forecast,"target")!=null?"Kézi éves cél":Json.Text(forecast,"source"),Ink);
        cards[2].Display(ending,goal>0&&ending.HasValue?Format(100*ending.Value/goal.Value,"%")+" várható célteljesülés":"Üzleti év végi összesen",Teal);
        cards[3].Display(delta,"Forecast és éves cél",delta<0?Color.FromArgb(161,61,53):Teal);
        chart.SetData(forecast);
        scenarios[0].Text=Format(At(Json.Items(forecast,"low"),11),"db");
        scenarios[1].Text=Format(ending,"db");
        scenarios[2].Text=Format(At(Json.Items(forecast,"high"),11),"db");
        modelNote.Text=String.Format("{0}\r\n±{1}% a hátralévő hónapokra.",Json.Flag(forecast,"seasonal")?
            "A 3 havi átlag és az előző év mintája egyenlő súllyal számít.":"Gördülő átlag az utolsó legfeljebb három havi tényből.",spread);
        chartNote.Text=Json.Flag(forecast,"gap")?"Hiányzó tényhónap. A forecast az utolsó folytonos tényadatból indul.":"Halmozott darabszám • Állj egy hónap fölé a tényleges és tervezett értékekhez.";
        DateTimeOffset lastImport;
        if(DateTimeOffset.TryParse(Json.Text(sales,"lastGood"),out lastImport))
        {
            string details=Json.Flag(forecast,"gap")?" · Hiányos havi adatsor":
                count<12?" · "+Months[count]+": nincs feldolgozható tényadat":"";
            chartNote.Text="Utolsó sikeres Sales-import: "+lastImport.LocalDateTime.ToString("MM.dd. HH:mm:ss",Hu)
                +" · "+Json.Text(forecast,"source")+details;
        }
        inputNote.Text=String.Format("{0}  ·  FY {1}    |    Az adott hónap kézi ténye elsőbbséget kap az Excel-értékkel szemben.",label,year);
        newYear.Text=year.ToString();
        target.Text=Json.Number(Json.Get(forecast,"target")).HasValue ? Format(Json.Number(Json.Get(forecast,"target"))) : "";
        var previous=Selection(entryMonth);
        binding=true;
        SetChoices(entryMonth,Months.Select((m,i)=>new Pick(i.ToString(),m+" "+(year+(i>=9?1:0)))),previous.Length>0?previous:Math.Min(count,11).ToString());
        binding=false;
        var entries=Json.Map(Json.Get(forecast,"entry"));
        var monthlyValues=Json.Items(forecast,"monthly");
        monthly.Rows.Clear();
        for(int i=0;i<12;i++)
        {
            var item=Json.Map(Json.Get(entries,i.ToString()));
            var from=item.Count>0?"Kézi":At(monthlyValues,i).HasValue?"Excel":"—";
            monthly.Rows.Add(Months[i]+" "+(year+(i>=9?1:0)),Format(At(monthlyValues,i)),Format(At(Json.Items(forecast,"actual"),i)),from,Json.Text(item,"note"));
        }
        FillManualEntry();
    }

    private void FillManualEntry()
    {
        if(binding)return;
        var info=Json.Map(Json.Get(Json.Map(Json.Get(forecast,"entry")),Selection(entryMonth)));
        var amount=Json.Number(Json.Get(info,"amount"));
        entryAmount.Text=amount.HasValue?amount.Value.ToString("0.########",Hu):"";
        entryNote.Text=Json.Text(info,"note");
    }

    private async Task SaveManual(string action)
    {
        var settings=ReportArgs();
        settings["action"]=action;settings["month"]=Math.Max(0,entryMonth.SelectedIndex);
        settings["amount"]=action=="target"?target.Text:entryAmount.Text;
        settings["note"]=entryNote.Text;
        await worker.CallAsync("manual",settings);
        await RefreshState(false);await FetchForecast();
        foot.Text="Az érték mentve.";
    }

    private async Task OpenYear()
    {
        int value;
        if(!Int32.TryParse(newYear.Text,out value)||value<2000||value>2200)throw new InvalidOperationException("Adj meg egy üzleti évet 2000 és 2200 között.");
        year=value;FillState();await FetchForecast();
    }

    private static bool Contains(string text,string search)
    {
        return Hu.CompareInfo.IndexOf(text,search,CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace)>=0;
    }

    private IEnumerable<Dictionary<string,object>> CustomerList()
    {
        return Json.Items(Json.Map(Json.Get(state,"buyers")),"items").Select(Json.Map);
    }

    private void FillCustomers()
    {
        var all=CustomerList().ToList();var search=buyerSearch.Text.Trim();var owner=Selection(owners);
        var selected=all.Where(i=>(!visible.Checked||!Json.Flag(i,"hidden"))&&(owner.Length==0||Json.Text(i,"owner")==owner)
            && (search.Length==0||new[]{"id","city","name","next"}.Any(k=>Contains(Json.Text(i,k),search)))).ToList();
        var byBox=selected.GroupBy(i=>Json.Text(i,"potential")+"-"+Json.Text(i,"position")).ToDictionary(g=>g.Key,g=>g.Count());
        foreach(var each in boxes)
        {
            int count;
            byBox.TryGetValue(each.Key,out count);
            int p=each.Key[0]-'0', q=each.Key[2]-'0';
            each.Value.Text=String.Format("{0} vevő   ·   {1} × {2}",count,p,q);
            each.Value.BackColor=boxFilter==each.Key?Blue:(p+q>=5?Color.FromArgb(218,241,235):Color.FromArgb(225,237,250));
            each.Value.ForeColor=boxFilter==each.Key?Color.White:Ink;
        }
        int ranked=selected.Count(i=>Json.Get(i,"potential")!=null&&Json.Get(i,"position")!=null);
        var market=Json.Map(Json.Get(state,"buyers"));
        int active=Json.Items(market,"filters").Select(Json.Map).Count(i=>Json.Items(i,"criteria").Count>0);
        string fileState=Json.Get(market,"sheet")==null?"SpeedUp-forrás beállítása szükséges.":active>0?"Mentett Excel-szűrés aktív.":"Mentett aktív Excel-szűrés nincs.";
        if(Json.Flag(market,"views"))fileState+=" A fájl mentett külön lapnézetet is tartalmaz.";
        matrixNote.Text=String.Format("{0} besorolt · {1} pontozásra vár   |   {2} rejtett vevősor. {3}",ranked,selected.Count-ranked,all.Count(i=>Json.Flag(i,"hidden")),fileState);
        buyers.Rows.Clear();
        foreach(var item in selected)
        {
            string p=Json.Text(item,"potential"),q=Json.Text(item,"position");
            if(boxFilter.Length>0&&p+"-"+q!=boxFilter) continue;
            string score=p.Length>0&&q.Length>0?p+" × "+q:"—";
            int row=buyers.Rows.Add(Json.Text(item,"id"),Json.Text(item,"name")+(Json.Flag(item,"hidden")?"  [rejtett]":""),Json.Text(item,"city"),Json.Text(item,"owner"),score,Json.Text(item,"next"));
            buyers.Rows[row].Tag=item;
        }
    }

    private async Task EditCustomer()
    {
        if(buyers.CurrentRow==null)return;
        var item=Json.Map(buyers.CurrentRow.Tag);
        using(var dialog=new Form { Text=Json.Text(item,"name"), StartPosition=FormStartPosition.CenterParent, ClientSize=new Size(545,245),
            ShowInTaskbar=false, FormBorderStyle=FormBorderStyle.FixedDialog, MaximizeBox=false, MinimizeBox=false, Font=Font })
        {
            var panel=Frame(46,45,82,48);panel.Padding=new Padding(12);dialog.Controls.Add(panel);
            panel.Controls.Add(Caption(Json.Text(item,"name"),14,true),0,0);
            var row=Row();var pot=Picker();var pos=Picker();
            var values=new[]{new Pick("","Nincs értékelve"),new Pick("1","1 · Alacsony"),new Pick("2","2 · Közepes"),new Pick("3","3 · Magas")};
            SetChoices(pot,values,Json.Text(item,"potential"));SetChoices(pos,values,Json.Text(item,"position"));
            AddField(row,"Potenciál",pot);AddField(row,"SMC-pozíció",pos);panel.Controls.Add(row,0,1);
            var next=Edit(500);next.Multiline=true;next.Dock=DockStyle.Bottom;next.Height=53;next.Text=Json.Text(item,"next");
            var notes=new Panel{Dock=DockStyle.Fill}; notes.Controls.Add(Caption("Következő lépés",10,false));notes.Controls.Add(next);panel.Controls.Add(notes,0,2);
            var actions=Row();var ok=Button("Mentés",true);ok.DialogResult=DialogResult.OK;
            var cancel=Button("Mégse",false);cancel.DialogResult=DialogResult.Cancel;actions.Controls.Add(ok);actions.Controls.Add(cancel);panel.Controls.Add(actions,0,3);
            dialog.AcceptButton=ok;dialog.CancelButton=cancel;
            if(dialog.ShowDialog(this)!=DialogResult.OK)return;
            int p=IntValue(Selection(pot),0),q=IntValue(Selection(pos),0);
            await worker.CallAsync("score",Json.Fields("id",Json.Text(item,"id"),"potential",p==0?null:(object)p,"position",q==0?null:(object)q,"next",next.Text));
        }
        await RefreshState(false);foot.Text="A vevőértékelés mentve.";
    }

    private void FillPipeline()
    {
        string term=sugarSearch.Text.Trim();
        var data=Json.Items(state,"opportunities").Select(Json.Map).Where(x=>term.Length==0||new[]{"name","account_name","sales_stage"}.Any(k=>Contains(Json.Text(x,k),term))).ToList();
        pipeline.Rows.Clear();
        var groups=new SortedDictionary<string,double>();
        foreach(var row in data.OrderBy(x=>Json.Text(x,"date_closed")))
        {
            string currency=Json.Text(row,"currency_iso",Json.Text(row,"currency_id"));
            string stage=Json.Text(row,"sales_stage");
            double? amount=Json.Number(Json.Get(row,"amount")), probability=Json.Number(Json.Get(row,"probability"));
            string normalized=stage.ToLowerInvariant().Replace(" ","").Replace("_","");
            if(currency.Length>0&&amount.HasValue&&probability>=0&&probability<=100&&normalized.Length>0&&!normalized.Contains("closedwon")&&!normalized.Contains("closedlost"))
            {
                double total;groups.TryGetValue(currency,out total);groups[currency]=total+amount.Value*probability.Value/100;
            }
            pipeline.Rows.Add(Json.Text(row,"name"),Json.Text(row,"account_name"),stage,Format(amount,currency),Format(probability,"%"),Json.Text(row,"date_closed"),Json.Text(row,"assigned_user_name"));
        }
        sugarNote.Text=String.Format("{0} lehetőség   |   {1}\r\nSúlyozott nyitott pipeline  ·  {2}",data.Count,
            Json.Text(Source("sugar"),"message","Sugar-adatforrás beállítása szükséges."),groups.Count>0?String.Join("   /   ",groups.Select(g=>Format(g.Value,g.Key))):"—");
    }

    private Dictionary<string,object> Source(string key)
    {
        return Json.Map(Json.Get(Json.Map(Json.Get(state,"status")),key));
    }

    private void FillSourceStatus()
    {
        string[] keys={"sales","buyers","sugar"};string[] names={"Sales","SpeedUp","Sugar"};
        sourcesStatus.Text=String.Join(Environment.NewLine,keys.Select((key,i)=>{
            var source=Source(key);DateTimeOffset when;
            string last=DateTimeOffset.TryParse(Json.Text(source,"lastGood"),out when)?"  ·  "+when.LocalDateTime.ToString("g",Hu):"";
            string mode=Json.Text(source,"mode");
            return names[i]+"  ·  "+(mode.Length==0?"":mode+"  ·  ")+Json.Text(source,"message","Frissítésre vár")+last;
        }));
    }

    private void FillSettings()
    {
        foreach(var entry in settings)
        {
            var box=entry.Value as TextBox;var check=entry.Value as CheckBox;var combo=entry.Value as ComboBox;
            if(box!=null)box.Text=Json.Text(Config,entry.Key);
            if(check!=null)check.Checked=Json.Flag(Config,entry.Key);
            if(combo!=null)
                for(int i=0;i<combo.Items.Count;i++)if(((Pick)combo.Items[i]).Value==Json.Text(Config,entry.Key))combo.SelectedIndex=i;
        }
    }

    private Dictionary<string,object> ReadSettings()
    {
        var values=Json.Fields();
        foreach(var entry in settings)
        {
            var box=entry.Value as TextBox;var check=entry.Value as CheckBox;var combo=entry.Value as ComboBox;
            if(box!=null)values[entry.Key]=(entry.Key=="minutes"||entry.Key=="fiscal")?(object)IntValue(box.Text,-1):box.Text;
            if(check!=null)values[entry.Key]=check.Checked;
            if(combo!=null)values[entry.Key]=Selection(combo);
        }
        return values;
    }

    private async Task SaveSources()
    {
        var data=ReadSettings();await worker.CallAsync("config",data);
        year=IntValue(Json.Get(data,"fiscal"),year);
        await RefreshState(false);
        foot.Text="A forrásbeállítások mentve.";
        await RefreshData(true,false);
    }

    private string SourceMode(string kind)
    {
        string mode=Json.Text(Config,kind+"Source");
        return mode.Length>0?mode:Json.Text(Config,"source");
    }

    private bool HasWatchedFile()
    {
        return (SourceMode("sales")=="local"&&Json.Text(Config,"salesPath").Length>0)||
            (SourceMode("buyers")=="local"&&Json.Text(Config,"buyersPath").Length>0);
    }

    private async Task RefreshData(bool forced,bool announce,bool localOnly=false)
    {
        if(!localOnly)nextRefresh=DateTime.UtcNow.AddMinutes(IntValue(Json.Get(Config,"minutes"),10));
        var result=await worker.CallAsync("refresh",Json.Fields("force",forced,"localOnly",localOnly));
        if(forced||Json.Flag(result,"changed")) {await RefreshState(false);await FetchForecast();}
        if(Json.Flag(result,"browserSales"))
        {
            try { await ImportSalesInCompass(!forced); }
            finally { nextRefresh=DateTime.UtcNow.AddMinutes(IntValue(Json.Get(Config,"minutes"),10)); }
            return;
        }
        if(announce)foot.Text=String.Join("  ·  ",new[]{"sales","buyers"}.Select(kind=>
            (kind=="sales"?"Sales: ":"SpeedUp: ")+(SourceMode(kind)=="manual"?"kézi import":Json.Flag(Source(kind),"ok")?"frissítve":"korábbi adatok")));
    }

    private async Task ImportSalesInCompass(bool scheduled)
    {
        var start=await worker.CallAsync("sales-download-start");
        browserSalesActive=true;
        cancelBrowserSales=false;
        refreshBtn.Text="Letöltés megszakítása";
        refreshBtn.Enabled=true;
        UseWaitCursor=false;
        foot.Text="Sales Excel letöltése a Compassban…";
        try
        {
            await SalesDownload.Fetch(workspace,Json.Text(start,"url"),Json.Text(start,"path"),!scheduled,()=>cancelBrowserSales,Json.Text(Config,"accountEmail"),Json.Flag(Config,"rememberLogin"));
            foot.Text="Letöltve · a Sales-adatok beolvasása…";
            var oldActual=Json.Items(forecast,"actual").ToArray();
            await worker.CallAsync("sales-download-finish");
            await RefreshState(!scheduled);
            await FetchForecast();
            int count=IntValue(Json.Get(forecast,"count"),0);
            string value=count>0?Months[count-1]+" · "+String.Format(Hu,"{0:N0} db",At(Json.Items(forecast,"actual"),count-1)):"nincs tényadat";
            bool unchanged=oldActual.SequenceEqual(Json.Items(forecast,"actual"));
            foot.Text="Sales beolvasva "+DateTime.Now.ToString("T",Hu)+" · "+Json.Text(forecast,"source")+" · "+value
                +(unchanged?" · A kijelzett mutató tényadatai változatlanok.":"");
        }
        finally
        {
            browserSalesActive=false;
            refreshBtn.Text="Adatfrissítés";
            // The worker also releases its private file if download or import fails.
            worker.Call("sales-download-cancel",null);
        }
    }

    private async Task ImportSalesFromBrowser(bool scheduled)
    {
        browserSalesActive=true;
        cancelBrowserSales=false;
        try
        {
            var options=Json.Fields("downloads",Launcher.DownloadsFolder());
            var start=await worker.CallAsync("browser-sales-start",options);
            if(Json.Flag(start,"needsName"))
            {
                if(scheduled&&browserNamePromptDismissed)
                {
                    foot.Text="Az automatikus frissítéshez add meg a Sales-fájl nevét a Források fülön.";
                    return;
                }
                string filename=AskSalesDownloadName();
                browserNamePromptDismissed=String.IsNullOrWhiteSpace(filename);
                if(browserNamePromptDismissed){foot.Text="Böngészős frissítés megszakítva.";return;}
                await worker.CallAsync("config",Json.Fields("salesDownloadName",filename));
                settings["salesDownloadName"].Text=filename;
                options["filename"]=filename;
                start=await worker.CallAsync("browser-sales-start",options);
            }
            Launcher.OpenWeb(Json.Text(start,"url"));
            refreshBtn.Text="Böngészős frissítés megszakítása";
            refreshBtn.Enabled=true;
            UseWaitCursor=false;
            foot.Text="Várom a friss Sales Excelt a Letöltések mappában…";
            while(!cancelBrowserSales)
            {
                await Task.Delay(700);
                if(cancelBrowserSales)break;
                var result=await worker.CallAsync("browser-sales-poll");
                string status=Json.Text(result,"result");
                if(status=="imported")
                {
                    await RefreshState(!scheduled);await FetchForecast();
                    foot.Text="Sales frissítve SharePointról: "+Json.Text(result,"name");
                    return;
                }
                if(status=="timeout")
                {
                    await RefreshState(false);
                    throw new InvalidOperationException("Nem érkezett új Sales Excel. Ha a böngésző belépést kért, fejezd be, majd próbáld újra. Eltérő letöltési mappát a Források fülön adhatsz meg.");
                }
            }
            await worker.CallAsync("browser-sales-cancel");
            foot.Text="Böngészős frissítés megszakítva.";
        }
        finally
        {
            refreshBtn.Text="Adatfrissítés";
            browserSalesActive=false;
        }
    }

    private string AskSalesDownloadName()
    {
        using(var dialog = new Form { Text="Sales Excel neve", StartPosition=FormStartPosition.CenterParent,
            FormBorderStyle=FormBorderStyle.FixedDialog, MaximizeBox=false, MinimizeBox=false, ClientSize=new Size(390,128) })
        {
            var prompt=Caption("Add meg egyszer a SharePointon látható Excel-fájl nevét.",10,false);
            prompt.Location=new Point(12,12);
            var filename=Edit(355);filename.Location=new Point(14,42);
            var ok=Button("Letöltés indítása",true);ok.Location=new Point(236,79);ok.DialogResult=DialogResult.OK;
            dialog.Controls.AddRange(new Control[]{prompt,filename,ok});
            dialog.AcceptButton=ok;dialog.ActiveControl=filename;
            return dialog.ShowDialog(this)==DialogResult.OK?filename.Text.Trim():"";
        }
    }

    private static OpenFileDialog OpenExcel()
    {
        return new OpenFileDialog { Filter="Excel-munkafüzet (*.xlsx;*.xlsm)|*.xlsx;*.xlsm", CheckFileExists=true, Multiselect=false };
    }

    private async Task Import(string kind)
    {
        string path;
        using(var file=OpenExcel()) { if(file.ShowDialog(this)!=DialogResult.OK)return;path=file.FileName; }
        await worker.CallAsync("import",Json.Fields("kind",kind,"path",path));
        await RefreshState(false);await FetchForecast();foot.Text="A munkafüzet importálva.";
    }

    private async Task Export()
    {
        using(var dialog=new SaveFileDialog { Filter="Excel-munkafüzet (*.xlsx)|*.xlsx", FileName="SMC-Compass-FY"+year+".xlsx", DefaultExt="xlsx", AddExtension=true })
        {
            if(dialog.ShowDialog(this)!=DialogResult.OK)return;
            var args=ReportArgs();args["path"]=dialog.FileName;
            await worker.CallAsync("export",args);
            foot.Text="Excel-export elkészült  ·  "+dialog.FileName;
        }
    }

    private async Task SugarImport()
    {
        string token=sugarToken.Text;sugarToken.Clear();
        await SaveSources();
        if(token.Length>0)await worker.CallAsync("sugar",Json.Fields("token",token));
        await RefreshState(false);foot.Text=token.Length>0?"Sugar-adatok importálva.":"Sugar-beállítások mentve.";
    }

    private async Task MicrosoftSignIn()
    {
        await worker.CallAsync("config",ReadSettings());
        await RefreshState(false);
        var request=await worker.CallAsync("signin");
        UseWaitCursor=false;
        try
        {
            using(var pane=new AccountPane(workspace))
            {
                await pane.Initialize(Json.Text(Config,"accountEmail"),Json.Flag(Config,"rememberLogin"));
                pane.Title.Text="Microsoft-belépés · kód: "+Json.Text(request,"code");
                pane.Reveal();
                pane.View.CoreWebView2.Navigate(Json.Text(request,"url"));
                while(!pane.Canceled)
                {
                    await Task.Delay(1000);
                    if(pane.Canceled)return;
                    var response=await worker.CallAsync("signin-poll");
                    if(!Json.Flag(response,"pending"))break;
                }
                if(pane.Canceled)return;
            }
        }
        finally { worker.Call("signin-cancel",null); }
        await RefreshData(true,true);
    }

    private async Task UpdateProgram()
    {
        var check=await worker.CallAsync("update-check");
        if(!Json.Flag(check,"available"))
        {
            foot.Text=Json.Flag(check,"published")?"A telepített program naprakész.":"A GitHubon még nincs közzétett Windows-kiadás.";
            MessageBox.Show(this,foot.Text,"Programfrissítés",MessageBoxButtons.OK,MessageBoxIcon.Information);
            return;
        }
        string version=Json.Text(check,"latest");
        string notes=Json.Text(check,"notes");
        if(notes.Length>520)notes=notes.Substring(0,520)+"…";
        string prompt=Launcher.ManagedInstall
            ?"Letöltöd és telepíted a frissítést? A Compass bezárul. A telepítés végén a Start menüből indíthatod újra."
            :"Letöltöd és újraindítod az alkalmazást?";
        if(MessageBox.Show(this,"Elérhető a(z) "+version+" verzió.\r\n\r\n"+notes+"\r\n\r\n"+prompt,
            "Programfrissítés",MessageBoxButtons.YesNo,MessageBoxIcon.Information)!=DialogResult.Yes)return;
        foot.Text="Programfrissítés letöltése és ellenőrzése…";
        var result=await worker.CallAsync("update-install",Json.Fields("version",version));
        if(Launcher.ManagedInstall)
        {
            string installer=Json.Text(result,"msi");
            if(!File.Exists(installer))
                throw new InvalidOperationException("A letöltött Windows-telepítő nem található.");
            PendingInstaller=installer;
        }
        else Launcher.StartProgram(Json.Text(result,"exe"));
        busy=false;Close();
    }
}

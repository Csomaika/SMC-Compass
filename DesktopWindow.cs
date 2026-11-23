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
    private readonly Timer timer = new Timer { Interval = 15000 };
    private DateTime nextRefresh;
    private Dictionary<string,object> state = Json.Fields();
    private Dictionary<string,object> forecast = Json.Fields();
    private bool busy, binding;
    private int year;
    private string metric = "ofsoldelectricactuators", method = "blend", boxFilter = "";
    private int spread = 10;

    private readonly TabControl tabs = new TabControl { Dock = DockStyle.Fill, Padding = new Point(15,7) };
    private readonly Label headline = Caption("",22,true);
    private readonly Label syncLine = Caption("",10,false);
    private readonly Label foot = Caption("Indítás…",10,false);
    private readonly Label chartNote = Caption("",10,false);
    private readonly Label matrixNote = Caption("",10,false);
    private readonly Label sugarNote = Caption("",10,false);
    private readonly Label inputNote = Caption("",10,false);
    private readonly Label[] cards = new Label[4];
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

    internal DesktopWindow(Worker worker)
    {
        this.worker = worker;
        Text = "SMC Compass  ·  " + Launcher.Version;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(930,700);
        Size = new Size(1315,875);
        Font = new Font("Segoe UI",10);
        ForeColor = Ink;
        BackColor = Cloud;
        AutoScaleMode = AutoScaleMode.Dpi;
        string ico = Path.Combine(Launcher.Folder,"Compass.ico");
        if (File.Exists(ico)) Icon = new Icon(ico);

        var shell = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = Cloud, Padding = new Padding(14,9,14,4) };
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute,68));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute,32));
        Controls.Add(shell);
        shell.Controls.Add(Header(),0,0);
        shell.Controls.Add(tabs,0,1);
        foot.Dock = DockStyle.Fill;
        foot.TextAlign = ContentAlignment.MiddleLeft;
        shell.Controls.Add(foot,0,2);

        BuildOverview(Page("Áttekintés"));
        BuildInput(Page("Havi adatbevitel"));
        BuildMatrix(Page("9-box vevőmátrix"));
        BuildPipeline(Page("Sugar pipeline"));
        BuildSources(Page("Források"));
        BuildRevisions(Page("Verziónapló"));

        years.SelectedIndexChanged += (s,e) => SelectForecast();
        metrics.SelectedIndexChanged += (s,e) => SelectForecast();
        methods.SelectedIndexChanged += (s,e) => SelectForecast();
        spreads.SelectedIndexChanged += (s,e) => SelectForecast();
        entryMonth.SelectedIndexChanged += (s,e) => FillManualEntry();
        buyerSearch.TextChanged += (s,e) => { if (!binding) FillCustomers(); };
        owners.SelectedIndexChanged += (s,e) => { if (!binding) FillCustomers(); };
        sugarSearch.TextChanged += (s,e) => { if (!binding) FillPipeline(); };
        visible.CheckedChanged += (s,e) => { if (!binding) FillCustomers(); };
        Shown += (s,e) => Run(Initial);
        timer.Tick += (s,e) => { if (!busy && DateTime.UtcNow >= nextRefresh) Run(() => RefreshData(false,false)); };
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
        using (var image = new Bitmap(980,420)) chart.DrawToBitmap(image,new Rectangle(0,0,image.Width,image.Height));
    }

    private async Task Initial()
    {
        state = await worker.CallAsync("state");
        year = IntValue(Json.Get(Config,"fiscal"),2026);
        FillSettings();
        FillState();
        await FetchForecast();
        await RefreshData(false,false);
        timer.Start();
    }

    private async void Run(Func<Task> work)
    {
        if (busy || IsDisposed) return;
        busy = true;
        refreshBtn.Enabled = exportBtn.Enabled = updateBtn.Enabled = false;
        UseWaitCursor = true;
        foot.Text = "Művelet folyamatban…";
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
                if (Visible) MessageBox.Show(this,error.Message,"SMC Compass",MessageBoxButtons.OK,MessageBoxIcon.Information);
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
        var head = new Panel { Dock = DockStyle.Fill, BackColor = Cloud };
        var mark = new CompassMark { Location = new Point(8,7), Size = new Size(47,47) };
        var title = Caption("SMC Compass",18,true);
        title.Location = new Point(66,5);
        var caption = Caption("PSD SALES WORKSPACE   /   v" + Launcher.Version,9,false);
        caption.Location = new Point(68,35);
        head.Controls.Add(mark);
        head.Controls.Add(title);
        head.Controls.Add(caption);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Right, Width = 520, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0,10,0,0), WrapContents = false };
        updateBtn = Button("Programfrissítés",false);
        exportBtn = Button("Excel-export",true);
        refreshBtn = Button("Adatfrissítés",false);
        updateBtn.Click += (s,e) => Run(UpdateProgram);
        exportBtn.Click += (s,e) => Run(Export);
        refreshBtn.Click += (s,e) => Run(() => RefreshData(true,true));
        actions.Controls.Add(updateBtn);
        actions.Controls.Add(exportBtn);
        actions.Controls.Add(refreshBtn);
        head.Controls.Add(actions);
        return head;
    }

    private TabPage Page(string title)
    {
        var page = new TabPage(title) { BackColor = Cloud, Padding = new Padding(10) };
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
        var layout = Frame(66,52,82,0,34);
        page.Controls.Add(layout);
        var hero = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(12,56,98), Margin = new Padding(3,3,3,5) };
        headline.Text = "Értékesítési előrejelzés";
        headline.ForeColor = Color.White;
        headline.Location = new Point(16,4);
        syncLine.ForeColor = Color.FromArgb(200,226,251);
        syncLine.Location = new Point(18,38);
        hero.Controls.Add(headline);
        hero.Controls.Add(syncLine);
        layout.Controls.Add(hero,0,0);
        var choice = Row();
        years.Width=105; metrics.Width=235; methods.Width=170; spreads.Width=78;
        AddField(choice,"Üzleti év",years);
        AddField(choice,"Mutató",metrics);
        AddField(choice,"Módszer",methods);
        AddField(choice,"Eltérés",spreads);
        layout.Controls.Add(choice,0,1);
        var values = new TableLayoutPanel { ColumnCount = 4, RowCount = 1, Dock = DockStyle.Fill };
        string[] names = {"Tény YTD","Éves cél","Év végi forecast","Eltérés a céltól"};
        for (int i=0;i<4;i++)
        {
            values.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,25));
            var card = new Panel { Dock = DockStyle.Fill, Margin = new Padding(3,0,5,6), BackColor = Color.White };
            var label = Caption(names[i],10,false); label.Location=new Point(13,8); card.Controls.Add(label);
            cards[i] = Caption("—",20,true); cards[i].Location=new Point(12,29); card.Controls.Add(cards[i]);
            values.Controls.Add(card,i,0);
        }
        layout.Controls.Add(values,0,2);
        layout.Controls.Add(chart,0,3);
        chartNote.Dock=DockStyle.Fill;
        chartNote.TextAlign=ContentAlignment.MiddleLeft;
        layout.Controls.Add(chartNote,0,4);
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
        Setting(list,"Forrásmód","source",mode);
        Setting(list,"Automatikus frissítés (perc)","minutes",Edit(85));
        Setting(list,"Üzleti év kezdete","fiscal",Edit(85));
        Setting(list,"Sales munkalap","salesSheet",Edit(165));
        Setting(list,"Előző éves munkalap","previousSheet",Edit(165));
        Setting(list,"SpeedUp munkalap","buyersSheet",Edit(165));
        PathSetting(list,"Sales helyi / OneDrive-fájl","salesPath");
        PathSetting(list,"SpeedUp helyi / OneDrive-fájl","buyersPath");
        Setting(list,"Sales SharePoint-munkafüzet","salesUrl",Edit(600));
        Setting(list,"SpeedUp SharePoint-munkafüzet","buyersUrl",Edit(600));
        var imports=Row(); imports.AutoSize=true;imports.Controls.Add(Action("Beállítások mentése",SaveSources,true));
        imports.Controls.Add(Action("Sales XLSX importálása",()=>Import("sales")));
        imports.Controls.Add(Action("SpeedUp XLSX importálása",()=>Import("buyers")));
        voidRow(list,imports);
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

    private void PathSetting(TableLayoutPanel list,string label,string key)
    {
        var box=Edit(499);
        var choose=Button("Tallózás…",false);
        choose.Click+=(s,e)=>{using(var file=OpenExcel()){if(file.ShowDialog(this)==DialogResult.OK)box.Text=file.FileName;}};
        var field=Row();field.AutoSize=true;field.Controls.Add(box);field.Controls.Add(choose);
        Setting(list,label,null,field);settings[key]=box;
    }

    private void BuildRevisions(TabPage page)
    {
        var layout=Frame(52,0);page.Controls.Add(layout);
        var title=Caption("Telepített verzió  ·  "+Launcher.Version,18,true);layout.Controls.Add(title,0,0);
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
        headline.Text=label;
        var sales=Source("sales");
        syncLine.Text=String.Format("FY {0}  ·  április–március  ·  {1}  ·  {2}",year,Json.Text(forecast,"source"),Json.Text(sales,"mode","Adatforrás beállítása"));
        cards[0].Text=Format(actual,"db");cards[1].Text=Format(goal,"db");cards[2].Text=Format(ending,"db");
        cards[2].ForeColor=Teal;
        double? delta=ending-goal;
        cards[3].Text=(delta>0?"+":"")+Format(delta,"db");
        cards[3].ForeColor=delta.HasValue && delta.Value<0 ? Color.FromArgb(159,58,47) : Teal;
        chart.SetData(forecast);
        chartNote.Text=String.Format("Óvatos {0}     ·     Alap {1}     ·     Erős {2}     ·     {3}{4}",
            Format(At(Json.Items(forecast,"low"),11),"db"),Format(ending,"db"),Format(At(Json.Items(forecast,"high"),11),"db"),
            Json.Flag(forecast,"seasonal")?"3 havi átlag + előző évi szezonalitás":"3 havi átlag",Json.Flag(forecast,"gap")?"     ·     Hiányzó tényhónap":"");
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
            return names[i]+"  ·  "+Json.Text(source,"message","Frissítésre vár")+last;
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
        await RefreshState(false);await FetchForecast();
        nextRefresh=DateTime.UtcNow.AddMinutes(IntValue(Json.Get(Config,"minutes"),10));
        foot.Text="A forrásbeállítások mentve.";
    }

    private async Task RefreshData(bool forced,bool announce)
    {
        nextRefresh=DateTime.UtcNow.AddMinutes(IntValue(Json.Get(Config,"minutes"),10));
        await worker.CallAsync("refresh",Json.Fields("force",forced));
        await RefreshState(false);await FetchForecast();
        if(announce)foot.Text=Json.Text(Config,"source")=="manual"?"Kézi Excel-import mód aktív.":
            Json.Flag(Source("sales"),"ok")&&Json.Flag(Source("buyers"),"ok")?"Az Excel-források frissítve.":"A források állapota a Források fülön látható.";
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
        await SaveSources();
        var request=await worker.CallAsync("signin");
        string url=Json.Text(request,"url"),code=Json.Text(request,"code");
        if(MessageBox.Show(this,"A Microsoft bejelentkező oldalán add meg ezt a belépési kódot:\r\n\r\n"+code+"\r\n\r\nMegnyitod a Microsoft oldalát?", "SharePoint-belépés",MessageBoxButtons.OKCancel,MessageBoxIcon.Information)!=DialogResult.OK)
        {
            await worker.CallAsync("signin-cancel");return;
        }
        Launcher.OpenWeb(url);
        using(var dialog=new Form { Text="Microsoft-belépés",StartPosition=FormStartPosition.CenterParent,ClientSize=new Size(440,145),
            FormBorderStyle=FormBorderStyle.FixedDialog,MinimizeBox=false,MaximizeBox=false,ShowInTaskbar=false })
        {
            var note=Caption("Belépési kód: "+code+"\r\nA Compass a Microsoft visszajelzésére vár.",11,true);
            note.Location=new Point(15,15);dialog.Controls.Add(note);
            var cancel=Button("Mégse",false);cancel.Location=new Point(15,91);cancel.DialogResult=DialogResult.Cancel;dialog.Controls.Add(cancel);dialog.CancelButton=cancel;
            string error=null;bool polling=false;
            using(var poll=new Timer { Interval=4000 })
            {
                poll.Tick+=async (s,e)=>{
                    if(polling)return;polling=true;
                    try { var answer=await worker.CallAsync("signin-poll");if(!dialog.IsDisposed&&!Json.Flag(answer,"pending"))dialog.DialogResult=DialogResult.OK; }
                    catch(Exception ex){error=ex.Message;if(!dialog.IsDisposed)dialog.DialogResult=DialogResult.Abort;}
                    finally{polling=false;}
                };
                poll.Start();
                var result=dialog.ShowDialog(this);poll.Stop();
                if(result!=DialogResult.OK){await worker.CallAsync("signin-cancel");if(error!=null)throw new InvalidOperationException(error);return;}
            }
        }
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
        if(MessageBox.Show(this,"Elérhető a(z) "+version+" verzió.\r\n\r\n"+notes+"\r\n\r\nLetöltöd és újraindítod az alkalmazást?",
            "Programfrissítés",MessageBoxButtons.YesNo,MessageBoxIcon.Information)!=DialogResult.Yes)return;
        foot.Text="Programfrissítés letöltése és ellenőrzése…";
        var result=await worker.CallAsync("update-install",Json.Fields("version",version));
        Launcher.StartProgram(Json.Text(result,"exe"));
        busy=false;Close();
    }
}

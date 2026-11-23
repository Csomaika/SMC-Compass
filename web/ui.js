'use strict';

const $ = (selector, root = document) => root.querySelector(selector);
const all = (selector, root = document) => [...root.querySelectorAll(selector)];
const nf = new Intl.NumberFormat('hu-HU', {maximumFractionDigits: 1});
const months = ['Ápr','Máj','Jún','Júl','Aug','Szept','Okt','Nov','Dec','Jan','Feb','Márc'];
const tabs = {overview:'Áttekintés', input:'Havi adatbevitel', matrix:'9-box vevőmátrix', pipeline:'Sugar pipeline', sources:'Források'};
const icons = {
  dashboard:'<rect x="3" y="3" width="18" height="18" rx="3"/><path d="M3 9h18M9 9v12"/>',
  edit:'<path d="M4 17v3h3l12-12-3-3L4 17Zm10-10 3 3M4 12V5a1 1 0 0 1 1-1h7"/>',
  grid:'<rect x="3" y="3" width="18" height="18" rx="2.5"/><path d="M9 3v18m6-18v18M3 9h18M3 15h18"/>',
  stack:'<path d="m12 3 9 5-9 5-9-5 9-5Zm-9 9 9 5 9-5M3 16l9 5 9-5"/>',
  settings:'<circle cx="12" cy="12" r="3"/><path d="M12 2v3m0 14v3M2 12h3m14 0h3M5 5l2 2m10 10 2 2M5 19l2-2M17 7l2-2"/><circle cx="12" cy="12" r="7"/>',
  refresh:'<path d="M20 11a8 8 0 1 0-2 6m2-12v6h-6"/>',
  download:'<path d="M12 3v12m-4-4 4 4 4-4M4 16v4h16v-4"/>',
  chart:'<path d="M4 4v16h16M7 15l4-5 3 3 5-7"/>',
  target:'<circle cx="12" cy="12" r="8"/><circle cx="12" cy="12" r="4"/><path d="m12 12 8-8"/>',
  users:'<circle cx="9" cy="7" r="3"/><path d="M3 20v-2a4 4 0 0 1 4-4h4a4 4 0 0 1 4 4v2m2-15a3 3 0 0 1 0 6m0 3a4 4 0 0 1 4 4v2"/>',
  arrow:'<path d="m9 18 6-6-6-6"/>', pulse:'<path d="M3 12h4l3-6 4 12 3-6h4"/>'
};
const html = value => String(value ?? '').replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
const icon = name => `<svg class="icon" viewBox="0 0 24 24" aria-hidden="true">${icons[name] || ''}</svg>`;
const fmt = (n, unit='') => n == null ? '—' : `${nf.format(n)}${unit ? ' '+unit : ''}`;
const date = stamp => stamp ? new Date(stamp).toLocaleString('hu-HU',{dateStyle:'short',timeStyle:'short'}) : '—';
const token = $('meta[name=compass-session]').content;
const defaultMetric = 'ofsoldelectricactuators';
let state, report, current = 'overview', year, metric = defaultMetric, spread = 10, method = 'blend';
let grid = {query:'', owner:'', box:'', visible:true, page:0};
let pipelineSearch = '', pendingToast, renderRevision = 0, freshness = '';

for (const el of all('[data-icon]')) el.innerHTML = icon(el.dataset.icon);
for (const button of all('nav button')) button.setAttribute('aria-label',tabs[button.dataset.tab]);

async function api(path, body, raw=false) {
  const headers = {'X-Compass':token};
  const options = {headers};
  if (body !== undefined) {
    options.method = 'POST';
    options.body = raw ? body : JSON.stringify(body);
    headers['Content-Type'] = raw ? 'application/octet-stream' : 'application/json';
  }
  const response = await fetch('/api/'+path,options);
  if (!response.ok) {
    const data = await response.json().catch(() => ({}));
    throw Error(data.error || 'A művelet nem sikerült.');
  }
  return path.startsWith('export?') ? response.blob() : response.json();
}

function notify(message,error=false) {
  const toast = $('#toast');
  toast.textContent = message;
  toast.classList.toggle('error',error);
  toast.hidden = false;
  clearTimeout(pendingToast);
  pendingToast = setTimeout(() => toast.hidden = true,5500);
}

function guarded(task) {
  return (...args) => Promise.resolve().then(() => task(...args)).catch(error => notify(error.message,true));
}

function key() { return new URLSearchParams({year,metric,spread,method,visible:grid.visible?'1':'0'}); }
function stamp(value) {return JSON.stringify(Object.values(value.status).map(s=>[s.lastGood,s.ok,s.message]));}

async function updateState() {
  state = await api('state');
  year ??= state.config.fiscal;
  freshness = stamp(state);
  const sources = [state.status.sales,state.status.buyers];
  $('#sideMode').textContent = sources.find(s=>s?.ok)?.mode || 'Forrás beállítása szükséges';
}

function pageHead(title,desc) {return `<div class="pagehead"><h1>${title}</h1><p>${desc}</p></div>`;}
function pill(text, warn=false) {return `<span class="pill${warn?' warn':''}">${html(text)}</span>`;}
function card(title,value,sub,mark,cls='') {return `<article class="card"><div class="card-label">${title} ${icon(mark)}</div><div class="value ${cls}">${value}</div><div class="sub">${sub}</div></article>`;}
function option(value,label,selected) {return `<option value="${html(value)}" ${String(value)===String(selected)?'selected':''}>${html(label)}</option>`;}
function field(label,name,value='',type='text',extra='') {return `<label class="field">${label}<input name="${name}" type="${type}" value="${html(value)}" ${extra}></label>`;}
function select(label,name,options,extra='') {return `<label class="field">${label}<select name="${name}" ${extra}>${options}</select></label>`;}
function dataTable(head,body,empty='Nincs megjeleníthető adat.') {return `<div class="data-scroll"><table class="data"><thead><tr>${head.map(v=>`<th scope="col">${v}</th>`).join('')}</tr></thead><tbody>${body || `<tr><td colspan="${head.length}">${empty}</td></tr>`}</tbody></table></div>`;}
const btn = (label, cls='secondary', extra='') => `<button type="button" class="button ${cls}" ${extra}>${label}</button>`;

function controls(withMethod) {
  const years = [...new Set([...state.years.map(Number),year, state.config.fiscal+1])].sort((a,b)=>b-a);
  const chooseYear = years.map(y=>option(y,`FY ${y}`,year)).join('');
  const chooseMetric = Object.entries(state.metrics).map(([key,name])=>option(key,name,metric)).join('');
  return `<div class="filters">${select('Üzleti év','year',chooseYear)}${select('Mutató','metric',chooseMetric)}${withMethod ? `<div class="inline">${select('Forecast módszer','method',option('blend','Szezonalitás + trend',method)+option('run','3 havi átlag',method))}${select('Forgatókönyv eltérése','spread',[5,10,15,20,30].map(s=>option(s,`±${s}%`,spread)).join(''))}</div>`:''}</div>`;
}

function hoverData(index,r) {
  return `${months[index]} ${r.year+(index>=9?1:0)} · Tény ${fmt(r.actual[index])} · Terv ${fmt(r.plan[index])} · Forecast ${fmt(r.forecast[index])}`;
}

function chart(r) {
  const palette={actual:'#0870c6',plan:'#8498b0',forecast:'#00887a',prev:'#acbfd6'};
  const x = i => 52+i*57;
  const values = [...r.actual,...r.plan,...r.high,...r.prev].filter(v=>v!=null);
  const minimum = Math.min(0,...values);
  const maximum = Math.max(1,...values);
  const base = (maximum-minimum)/4;
  const power = 10 ** Math.floor(Math.log10(base));
  const unit = [1,2,2.5,5,10].find(n=>n*power>=base)*power;
  const lower = Math.floor(minimum/unit)*unit, upper = Math.ceil(maximum/unit)*unit;
  const y = value => 232-(value-lower)/(upper-lower)*204;
  const path = vals => {
    let drawing = false;
    return vals.map((v,i)=>{
      if(v==null) {drawing=false;return '';}
      const command = `${drawing?'L':'M'}${x(i)} ${y(v).toFixed(1)} `;
      drawing=true;
      return command;
    }).join('');
  };
  let ticks='';
  for(let val=lower;val<=upper+unit*.000001;val+=unit) {
    ticks+=`<line x1="52" x2="679" y1="${y(val)}" y2="${y(val)}" stroke="#e0e9f3"/><text x="46" y="${y(val)+4}" text-anchor="end" class="graph-label">${html(fmt(val))}</text>`;
  }
  const bounds = r.high.map((v,i)=>v==null?null:[x(i),y(v)]).filter(Boolean);
  const bottom = r.low.map((v,i)=>v==null?null:[x(i),y(v)]).filter(Boolean).reverse();
  const area = bounds.length>1?`<polygon points="${bounds.concat(bottom).map(p=>p.join(',')).join(' ')}" fill="#b1e6df" opacity=".65"/>`:'';
  const curves = ['prev','plan','forecast','actual'].map(name=>`<path d="${path(r[name])}" fill="none" stroke="${palette[name]}" stroke-width="${name==='actual'?3.5:2.2}" stroke-dasharray="${name==='forecast'?'7 4':name==='plan'?'4 4':name==='prev'?'2 4':'none'}" stroke-linecap="round" stroke-linejoin="round"/>`).join('');
  const points=r.actual.map((v,i)=>v==null?'':`<circle cx="${x(i)}" cy="${y(v)}" r="${i===r.count-1?5:3}" fill="#0870c6" stroke="white" stroke-width="1.5"/>`).join('');
  const hover=months.map((name,i)=>`<g class="graph-hover" tabindex="0" data-month="${i}" aria-label="${html(hoverData(i,r))}"><title>${html(hoverData(i,r))}</title><line x1="${x(i)}" x2="${x(i)}" y1="23" y2="232" stroke-opacity="0" stroke-dasharray="3 3"/><rect x="${x(i)-26}" y="22" width="52" height="223" fill="transparent"/><text x="${x(i)}" y="254" text-anchor="middle" class="graph-label">${name}</text></g>`).join('');
  return `<svg viewBox="0 0 707 268" class="chart" role="img" aria-label="Halmozott tény, terv és előrejelzés hónaponként">${ticks}<g class="draw-line">${area}${curves}${points}</g>${hover}</svg><div class="legend"><span><i style="background:${palette.actual}"></i>Tény</span><span><i class="dash" style="color:${palette.plan}"></i>Terv</span><span><i class="dash" style="color:${palette.forecast}"></i>Forecast</span><span><i class="dash" style="color:${palette.prev}"></i>Előző FY</span><span><i class="band"></i>Forgatókönyv-tartomány</span></div>`;
}

function overview() {
  const r = report, at = r.count-1, actual = r.actual[at], plan = r.plan[11], gap = r.ending!=null&&plan!=null?r.ending-plan:null;
  const progress = plan>0&&r.ending!=null ? 100*r.ending/plan : null;
  const last = at>=0 ? `${months[at]} ${r.year+(at>=9?1:0)} · ${r.count} lezárt/adatolt hónap` : 'Havi tényadat szükséges';
  const market = state.status.sales;
  const status = market?.ok ? (market.mode.includes('pillanatkép')?'Helyi pillanatkép':market.mode) : (market?'Frissítés ellenőrzendő':'Forrás betöltése');
  const gapFormat=gap==null?'—':`${gap>0?'+':''}${fmt(gap,'db')}`;
  const scenarioRows = [['Óvatos',r.low[11]],['Alap',r.ending],['Erős',r.high[11]]].map(([title,val])=>`<div><strong>${title}</strong><b>${fmt(val,'db')}</b></div>`).join('');
  return `<section><div class="hero"><div><div class="eyebrow">Értékesítési előrejelzés</div><h1>${html(r.label)}</h1><p>FY ${r.year} · április ${r.year} – március ${r.year+1}</p></div>${pill(status,!market?.ok || status==='Helyi pillanatkép')}</div>
  ${controls(true)}${r.gap?'<div class="notice warn">A tényidősorban hiányzó hónap van. A forecast az első hiány előtt lezárt hónapokból számol.</div>':''}
  <div class="kpis">${card('Tény YTD',fmt(actual,'db'),last,'pulse')}${card('Éves cél',fmt(plan,'db'),r.target!=null?'Kézzel megadott cél':html(r.source),'target')}${card('Év végi forecast',fmt(r.ending,'db'),progress==null?'Várható üzleti év végi összesen':`${fmt(progress)}% várható célteljesülés`,'chart','good')}${card('Eltérés a céltól',gapFormat,'Év végi forecast és cél különbsége','arrow',gap==null?'':gap>=0?'good':'bad')}</div>
  <div class="panels"><article class="panel"><header class="panel-head"><div><h2>Tény, terv és forecast</h2><p id="chartHint">Halmozott darabszám az üzleti év hónapjaiban</p></div></header>${chart(r)}</article>
  <article class="panel"><header class="panel-head"><div><h2>Év végi forgatókönyvek</h2><p>Hátralévő hónapok · ±${spread}%</p></div></header><div class="scenario">${scenarioRows}</div><div class="rule"></div><h3>Számítás</h3><p class="hint">${r.seasonal?'Az utolsó három tényhónap átlaga és az előző év havi mintája egyenlő súllyal számít.':'Az utolsó legfeljebb három tényhónap gördülő átlaga adja a jövőbeli hónapok becslését.'}</p><p class="hint">Friss adat ${date(market?.lastGood)}</p></article></div></section>`;
}

function input() {
  const rows=months.map((name,i)=>{
    const entry=report.entry[i];
    return `<tr><td>${name} ${year+(i>=9?1:0)}</td><td class="num">${fmt(report.monthly[i])}</td><td class="num">${fmt(report.actual[i])}</td><td>${entry?'Kézi':report.actual[i]!=null?'Excel':'—'}</td><td>${html(entry?.note || '')}</td><td>${btn('Szerkesztés','secondary',`data-month-edit="${i}"`)}</td></tr>`;
  }).join('');
  return `<section>${pageHead('Havi adatbevitel','Rögzíts havi darabszámot, vagy állíts be éves célt a kiválasztott mutatóhoz.')}${controls(false)}<div class="split"><article class="panel"><h2>Havi tényadat</h2><form id="manualForm"><div class="form-grid">${select('Hónap','month',months.map((n,i)=>option(i,`${n} ${year+(i>=9?1:0)}`,Math.min(report.count,11))).join(''))}${field('Tényleges eladás (db)','amount','','number','step="any" required')}<label class="field wide">Megjegyzés<input name="note" maxlength="500" placeholder="Például zárás vagy korrekció"></label></div><div class="actions"><button class="button primary">Havi tény mentése</button>${btn('Kézi havi érték törlése','secondary danger','id="manualRemove"')}</div></form><p class="hint">A kézi havi érték felülírja az adott hónap importált értékét. A következő hónapok különbségét továbbra is az Excel eredeti havi növekményéből vesszük.</p></article><article class="panel"><h2>Éves cél</h2><form id="targetForm"><div class="form-grid">${field('Éves cél (db)','amount',report.target??'','number','min="0" step="any" required')}${field('Új üzleti év megnyitása','openYear',year,'number','min="2000" max="2200" id="inputYear"')}</div><div class="actions"><button class="button primary">Cél mentése</button>${btn('Excel-cél visszaállítása','secondary','id="targetRemove"')}${btn('Üzleti év kiválasztása','secondary','id="openYear"')}</div></form><p class="hint">A kézzel megadott éves cél egyenlő arányban oszlik el az üzleti év 12 hónapja között.</p></article></div><article class="panel spaced"><header class="panel-head"><div><h2>Havi bontás</h2><p>${html(report.label)} · FY ${year}</p></div></header>${dataTable(['Hónap','Havi tény','Halmozott tény','Forrás','Megjegyzés',''],rows)}</article></section>`;
}

function sourceFlags() {
  const source = state.buyers;
  const active=(source.filters||[]).filter(f=>f.criteria.length);
  const hidden=(source.items||[]).filter(i=>i.hidden).length;
  if (!source.sheet) return 'Importálj XLSX vevőlistát a Források oldalon.';
  if (active.length) return `Aktív mentett Excel-szűrés (${active.map(x=>`${x.where}: ${x.criteria.length} oszlop`).join(', ')}). ${hidden} rejtett vevősor.`;
  return `${(source.filters||[]).length ? 'A táblázat szűrhető. A fájlban nincs mentett aktív szűrőfeltétel.' : 'A fájlban nem szerepel aktív Excel-szűrés.'} ${hidden} rejtett vevősor.`;
}

function matrix() {
  const source=state.buyers, records=source.items||[];
  const owners=[...new Set(records.map(i=>i.owner).filter(Boolean))].sort((a,b)=>a.localeCompare(b,'hu'));
  return `<section>${pageHead('9-box vevőmátrix','Pontozd a potenciális E-actuator vevőket két tengelyen, és rögzítsd a következő lépést.')}
  <div class="split"><article class="panel"><header class="panel-head"><div><h2>Potenciál × SMC-pozíció</h2><p id="matrixCount"></p></div>${btn('Összes','secondary','id="clearBox"')}</header><div class="matrix-wrap"><div class="axis-y">Potenciál · alacsony → magas</div><div id="boxes" class="matrix-grid"></div><div class="axis-x">SMC-pozíció · gyenge → erős</div></div></article>
  <article class="panel"><h2>SpeedUp forrás</h2><p class="hint">${html(sourceFlags())}${source.views?' A munkafüzetben külön lapnézetek is találhatók.':''}</p><div class="notice">A Compass a mentett XLSX látható sorait veszi át. Az Excel Online ideiglenes vagy személyes nézetében beállított szűrés csak akkor jelenik meg, ha bekerül a mentett fájlba.</div>${!source.hasScores?'<p class="hint">A pontszámokat és következő lépéseket ügyfélkódhoz kötve tárolja a program.</p>':''}<label class="field"><span class="checkbox"><input type="checkbox" id="visible" ${grid.visible?'checked':''}> Csak az Excelben látható sorok</span></label></article></div>
  <article class="panel spaced"><header class="panel-head"><div><h2>Vevőlista</h2><p>Ügyfélkód, név, város, felelős, pontszámok és következő lépés</p></div></header><div class="filters">${field('Keresés','customerSearch',grid.query,'search','placeholder="Név, ügyfélkód, város"')}${select('SAE felelős','owner',option('','Összes',grid.owner)+owners.map(o=>option(o,o,grid.owner)).join(''))}</div><div id="customerData"></div></article></section>`;
}

function customerResults() {
  const query=grid.query.trim().toLocaleLowerCase('hu');
  const pool=(state.buyers.items||[]).filter(i=>(!grid.visible||!i.hidden)&&(!grid.owner||i.owner===grid.owner)&&(!query||[i.id,i.name,i.city,i.next].some(v=>String(v).toLocaleLowerCase('hu').includes(query))));
  const unassigned=pool.filter(i=>!i.position||!i.potential).length;
  $('#matrixCount').textContent=`${pool.length-unassigned} besorolt · ${unassigned} pontozásra vár`;
  const band=['','Alacsony','Közepes','Magas'];
  $('#boxes').innerHTML=[3,2,1].flatMap(p=>[1,2,3].map(pos=>{
    const id=`${p}-${pos}`;
    const count=pool.filter(i=>i.potential===p&&i.position===pos).length;
    return `<button type="button" class="box ${p+pos>=5?'hot':p+pos<=3?'cool':''} ${grid.box===id?'chosen':''}" aria-pressed="${grid.box===id}" data-box="${id}"><b>${count}</b><small>${band[p]} · ${pos}/3</small></button>`;
  })).join('');
  for(const el of all('[data-box]')) el.onclick=()=>{grid.box=grid.box===el.dataset.box?'':el.dataset.box;grid.page=0;customerResults();};
  const filtered=grid.box?pool.filter(i=>`${i.potential}-${i.position}`===grid.box):pool;
  const size=20, pages=Math.max(1,Math.ceil(filtered.length/size));
  grid.page=Math.min(grid.page,pages-1);
  const rows=filtered.slice(grid.page*size,(grid.page+1)*size).map(i=>`<tr><td><strong>${html(i.name)}</strong><br><span class="muted">${html(i.id)}</span></td><td>${html(i.city)}</td><td>${html(i.owner)}</td><td>${i.hidden?'<span class="tag">Rejtett</span> ':''}${i.potential&&i.position?`<span class="tag">${i.potential} × ${i.position}</span>`:'—'}</td><td>${html(i.next)}</td><td>${btn('Pontozás','secondary',`data-score="${html(i.id)}"`)}</td></tr>`).join('');
  $('#customerData').innerHTML=dataTable(['Vevő','Város','SAE','9-box','Következő lépés',''],rows)+`<div class="table-footer"><span>${filtered.length} találat · ${grid.page+1}/${pages}. oldal</span><div class="inline">${btn('Előző','secondary',`id="previousPage" ${grid.page===0?'disabled':''}`)}${btn('Következő','secondary',`id="nextPage" ${grid.page>=pages-1?'disabled':''}`)}</div></div>`;
  $('#previousPage').onclick=()=>{grid.page--;customerResults();};
  $('#nextPage').onclick=()=>{grid.page++;customerResults();};
  for(const el of all('[data-score]')) el.onclick=()=>openScore(recordsById(el.dataset.score));
}

function recordsById(id) {return state.buyers.items.find(i=>String(i.id)===id);}
function openScore(item) {
  const opts = value => option('','Nincs értékelve',value)+[[1,'Alacsony / gyenge'],[2,'Közepes'],[3,'Magas / erős']].map(([i,n])=>option(i,`${i} · ${n}`,value)).join('');
  $('#modal').innerHTML=`<h2>${html(item.name)}</h2><p class="muted">${html(item.id)} · ${html(item.city)}</p><div class="form-grid">${select('Piaci potenciál','potential',opts(item.potential))}${select('SMC-pozíció','position',opts(item.position))}<label class="field wide">Következő lépés<textarea name="next" maxlength="1500">${html(item.next)}</textarea></label></div><div class="actions">${btn('Mégse','secondary','id="closeModal"')}<button class="button primary right">Mentés</button></div>`;
  $('#closeModal').onclick=()=>$('#dialog').close();
  $('#modal').onsubmit=guarded(async e=>{e.preventDefault();const fields=new FormData(e.target);await api('score',{id:item.id,potential:Number(fields.get('potential'))||null,position:Number(fields.get('position'))||null,next:fields.get('next')});$('#dialog').close();await updateState();customerResults();notify('Vevőértékelés mentve.');});
  $('#dialog').showModal();
}

function pipeline() {
  const s=state.status.sugar;
  return `<section>${pageHead('Sugar pipeline','Értékesítési lehetőségek és valószínűséggel súlyozott nyitott érték pénznemenként.')}
  <article class="panel"><header class="panel-head"><div><h2>Kapcsolati állapot</h2><p>${html(s?.message||'Sugar-adatforrás beállítása szükséges.')}${s?.lastGood?' · '+date(s.lastGood):''}</p></div>${btn('Sugar beállítása','secondary','data-open="sources"')}</header><p class="hint">A pipeline és a darabszám-előrejelzés külön mutatók. Az összegek a Sugar rekord pénznemében jelennek meg.</p></article>
  <div id="money" class="kpis"></div><article class="panel spaced"><header class="panel-head"><div><h2>Lehetőségek</h2><p id="opCount"></p></div>${field('Keresés','oppSearch',pipelineSearch,'search','placeholder="Név, vevő, státusz"')}</header><div id="oppTable"></div></article></section>`;
}

function pipelineResults() {
  const q=pipelineSearch.trim().toLowerCase();
  const records=state.opportunities.filter(x=>![x.name,x.account_name,x.sales_stage].every(v=>!String(v??'').toLowerCase().includes(q)));
  const currencies=new Map();
  for(const x of records) {
    const stage=String(x.sales_stage||'').toLowerCase().replaceAll(' ','');
    const value=Number(x.amount), pct=x.probability==null||x.probability===''?NaN:Number(x.probability);
    if(stage.includes('closedwon')||stage.includes('closedlost')||x.amount==null||x.amount===''||!Number.isFinite(value)||!stage) continue;
    const currency=x.currency_iso||x.currency_id;
    if(!currency) continue;
    const money=currencies.get(currency)||{value:0,weighted:0,count:0,unknown:0};
    money.value+=value;money.count++;
    if(Number.isFinite(pct)&&pct>=0&&pct<=100) money.weighted+=value*pct/100;
    else money.unknown++;
    currencies.set(currency,money);
  }
  $('#money').innerHTML=[...currencies].map(([label,m])=>card(`Súlyozott · ${html(label)}`,fmt(m.weighted),`${m.count} nyitott · ${fmt(m.value)} teljes érték${m.unknown?' · '+m.unknown+' súly nélkül':''}`,'stack')).join('');
  const ordered=records.slice().sort((a,b)=>String(a.date_closed||'9999').localeCompare(String(b.date_closed||'9999')));
  const rows=ordered.slice(0,300).map(x=>`<tr><td><strong>${html(x.name||'—')}</strong><br><span class="muted">${html(x.account_name||'')}</span></td><td>${html(x.sales_stage||'—')}</td><td class="num">${x.amount==null||x.amount===''?'—':html(fmt(Number(x.amount),x.currency_iso||x.currency_id||''))}</td><td class="num">${x.probability==null||x.probability===''?'—':html(`${x.probability}%`)}</td><td>${html(x.date_closed||'—')}</td><td>${html(x.assigned_user_name||'—')}</td></tr>`).join('');
  $('#opCount').textContent=`${records.length} találat${records.length>300?' · első 300 látható, az Excel-export teljes':''}`;
  $('#oppTable').innerHTML=dataTable(['Lehetőség / vevő','Státusz','Összeg','Valószínűség','Várható zárás','Felelős'],rows);
}

function sourceStatus(key,label) {
  const info=state.status[key];
  return `<div class="status-row"><span class="dot ${info?.ok?'':'warn'}"></span><div><strong>${label}</strong><small>${html(info?.message||'Frissítésre vár')}${info?.mode?' · '+html(info.mode):''}${info?.lastGood?' · Utolsó sikeres: '+date(info.lastGood):''}</small></div></div>`;
}

function sources() {
  const c=state.config;
  const file = (label,id) => `<label class="field">${label}<input type="file" accept=".xlsx,.xlsm" id="${id}"></label>`;
  const isShare = value => {try{return new URL(value).protocol==='https:'&&new URL(value).hostname.endsWith('.sharepoint.com');}catch{return false;}};
  const link = (url,title) => isShare(url)?`<a href="${html(url)}" target="_blank" rel="noopener noreferrer">${title}</a>`:'';
  return `<section>${pageHead('Adatforrások','Állítsd be a munkafüzeteket, az automatikus frissítést és a Sugar kapcsolatot.')}
  <div class="split"><div class="stack-layout"><article class="panel"><h2>Excel és SharePoint</h2><div class="status-list">${sourceStatus('sales','Sales')}${sourceStatus('buyers','SpeedUp vevőlista')}</div><form id="sourceForm"><div class="form-grid">
  ${select('Frissítés módja','source',option('local','Helyi / OneDrive-szinkronizált fájl',c.source)+option('sharepoint','Közvetlen SharePoint (Entra)',c.source)+option('manual','Kézi Excel-import',c.source))}${field('Frissítés (perc)','minutes',c.minutes,'number','min="1" max="180" required')}
  ${field('Sales munkalap','salesSheet',c.salesSheet,'text','list="salesSheets" required')}${field('Üzleti év (kezdő év)','fiscal',c.fiscal,'number','min="2000" max="2200" required')}
  ${field('Előző éves munkalap','previousSheet',c.previousSheet,'text','list="salesSheets"')}${field('SpeedUp munkalap','buyersSheet',c.buyersSheet,'text','list="buyerSheets" required')}
  <datalist id="salesSheets">${state.salesSheets.map(i=>option(i,i,'')).join('')}</datalist><datalist id="buyerSheets">${(state.buyers.sheets||[]).map(i=>option(i,i,'')).join('')}</datalist>
  <div class="wide">${field('Sales · helyi teljes fájlútvonal','salesPath',c.salesPath)}</div><div class="wide">${field('SpeedUp · helyi teljes fájlútvonal','buyersPath',c.buyersPath)}</div>
  <div class="wide">${field('Sales · SharePoint munkafüzetlink','salesUrl',c.salesUrl,'url')}</div><div class="wide">${field('SpeedUp · SharePoint munkafüzetlink','buyersUrl',c.buyersUrl,'url')}</div></div>
  <div class="link-row">${link(c.salesUrl,'Sales megnyitása ↗')}${link(c.buyersUrl,'SpeedUp megnyitása ↗')}</div>
  <div class="actions"><button type="submit" class="button primary">Források mentése</button></div></form><p class="hint">A program futása alatt a beállított időközönként ellenőrzi a két forrást. A Letöltések mappából felismert fájl pillanatkép. Élő OneDrive-frissítéshez add meg a szinkronizált fájl útvonalát.</p></article></div>
  <div class="stack-layout"><article class="panel"><h2>Közvetlen SharePoint-kapcsolat</h2><p class="hint">Microsoft Entra alkalmazásregisztráció, delegált SharePoint AllSites.Read jogosultság és mobil/asztali átirányítás szükséges. Átirányítási URI: <strong>http://localhost:${location.port}/oauth/callback</strong>.</p>
  <form id="entraForm"><div class="form-grid">${field('Directory (tenant) ID','tenant',c.tenant)}${field('Application (client) ID','client',c.client)}</div><div class="actions"><button class="button primary">Mentés és Microsoft-belépés</button></div></form><p class="hint">${state.spConnected?'Microsoft-munkamenet aktív.':'Microsoft-munkamenethez bejelentkezés szükséges.'}</p></article>
  <article class="panel"><h2>Kézi Excel-import</h2><p class="hint">A munkalap nevét előbb mentsd el. Az import a pillanatképet cseréli, a helyi pontszámokat megtartja.</p><div class="form-grid">${file('Sales XLSX','salesFile')}${file('SpeedUp XLSX','buyersFile')}</div></article>
  <article class="panel"><h2>Sugar CRM</h2><div class="status-list">${sourceStatus('sugar','Opportunities')}</div><form id="sugarForm"><div class="form-grid"><div class="wide">${field('Sugar alap URL','sugarUrl',c.sugarUrl,'url','placeholder="https://sugar.ceg.hu" required')}</div>${field('REST-verzió','sugarApi',c.sugarApi,'text','required')}${field('Access token (munkamenet)','token','','password','autocomplete="off"')}<label class="field wide"><span class="checkbox"><input name="sugarMine" type="checkbox" ${c.sugarMine?'checked':''}> Saját lehetőségek</span></label></div><div class="actions"><button class="button primary">Mentés és Sugar-import</button></div></form><p class="hint">Érvényes Sugar REST access token kell. A token a program bezárásáig memóriában marad.</p></article><article class="panel"><h2>Alkalmazás</h2><div class="actions">${btn('Compass leállítása','secondary','id="quit"')}</div></article></div></div></section>`;
}

async function render() {
  if (!state) return;
  const rev=++renderRevision;
  for(const el of all('nav button')) { const active=el.dataset.tab===current;el.classList.toggle('selected',active);el.setAttribute('aria-current',active?'page':'false'); }
  $('#crumb').textContent=tabs[current];
  if(current==='overview'||current==='input') {
    const next=await api('forecast?'+key());
    if(rev!==renderRevision) return;
    report=next;
  }
  const screens={overview,input,matrix,pipeline,sources};
  $('#screen').innerHTML=screens[current]();
  wire();
}

function wire() {
  for(const name of ['year','metric','spread','method']) {
    const node=$(`[name=${name}]`);
    if(node) node.onchange=guarded(async()=>{
      if(name==='year') year=Number(node.value);
      if(name==='metric') metric=node.value;
      if(name==='spread') spread=Number(node.value);
      if(name==='method') method=node.value;
      await render();
    });
  }
  for(const el of all('[data-month]')) {
    const hint=$('#chartHint');
    const show=()=>hint.textContent=hoverData(Number(el.dataset.month),report);
    const reset=()=>hint.textContent='Halmozott darabszám az üzleti év hónapjaiban';
    el.onmouseenter=el.onfocus=show;el.onmouseleave=el.onblur=reset;
  }
  if(current==='input') wireInput();
  if(current==='matrix') {
    $('#visible').onchange=e=>{grid.visible=e.target.checked;grid.page=0;customerResults();};
    $('[name=customerSearch]').oninput=e=>{grid.query=e.target.value;grid.page=0;customerResults();};
    $('[name=owner]').onchange=e=>{grid.owner=e.target.value;grid.page=0;customerResults();};
    $('#clearBox').onclick=()=>{grid.box='';grid.page=0;customerResults();};
    customerResults();
  }
  if(current==='pipeline') {
    $('[name=oppSearch]').oninput=e=>{pipelineSearch=e.target.value;pipelineResults();};
    $('[data-open=sources]').onclick=guarded(()=>navigate('sources'));
    pipelineResults();
  }
  if(current==='sources') wireSources();
}

function wireInput() {
  const form=$('#manualForm'), target=$('#targetForm');
  const write=async (action,from)=>{
    const obj=Object.fromEntries(new FormData(from));
    await api('manual',{...obj,year,metric,action});
    await updateState();await render();notify('Módosítás mentve.');
  };
  form.onsubmit=guarded(async e=>{e.preventDefault();await write('save',form);});
  target.onsubmit=guarded(async e=>{e.preventDefault();await write('target',target);});
  $('#manualRemove').onclick=guarded(()=>write('remove',form));
  $('#targetRemove').onclick=guarded(()=>write('target-remove',target));
  $('#openYear').onclick=guarded(async()=>{const box=$('#inputYear');if(box.reportValidity()&&box.value){year=Number(box.value);await render();}});
  const syncInput=()=>{const entry=report.entry[form.elements.month.value];form.elements.amount.value=entry?.amount??'';form.elements.note.value=entry?.note??'';};
  form.elements.month.onchange=syncInput;
  for(const btn of all('[data-month-edit]')) btn.onclick=()=>{form.elements.month.value=btn.dataset.monthEdit;syncInput();form.elements.amount.focus();};
  syncInput();
}

function wireSources() {
  const settings=async e=>{
    const data=Object.fromEntries(new FormData(e.target));
    data.fiscal=Number(data.fiscal);data.minutes=Number(data.minutes);
    await api('settings',data);year=data.fiscal;
  };
  $('#sourceForm').onsubmit=guarded(async e=>{e.preventDefault();await settings(e);await updateState();await render();notify('Források mentve.');});
  $('#entraForm').onsubmit=guarded(async e=>{
    e.preventDefault();
    const popup=window.open('about:blank','compass-entra');
    try {
      await api('settings',Object.fromEntries(new FormData(e.target)));
      const result=await api('oauth',{});
      if(popup){popup.opener=null;popup.location=result.url;}else location.assign(result.url);
    } catch(error) {popup?.close();throw error;}
  });
  for(const kind of ['sales','buyers']) $(`#${kind}File`).onchange=guarded(async e=>{
    const file=e.target.files[0];if(!file)return;
    notify('Excel-import folyamatban…');await api('import/'+kind,file,true);await updateState();await render();notify('Excel-import kész.');
  });
  $('#sugarForm').onsubmit=guarded(async e=>{
    e.preventDefault();const form=e.target;
    const {sugarUrl,sugarApi,token}=Object.fromEntries(new FormData(form));
    await api('settings',{sugarUrl,sugarApi,sugarMine:form.elements.sugarMine.checked});
    if(token.trim()) {form.elements.token.value='';await api('sugar',{token});}
    await updateState();await render();notify(token.trim()?'Sugar-import kész.':'Sugar-beállítások mentve.');
  });
  $('#quit').onclick=guarded(async()=>{await api('quit',{});$('#screen').innerHTML='<p class="notice">A Compass leállt. Az újraindításhoz nyisd meg az indítót.</p>';});
}

async function navigate(next) {current=next;await render();window.scrollTo(0,0);}
for(const button of all('nav button')) button.onclick=guarded(()=>navigate(button.dataset.tab));
$('#refresh').onclick=guarded(async()=>{
  const button=$('#refresh');button.disabled=true;
  try {
    const result=await api('refresh',{});await updateState();await render();
    const failures=[state.status.sales,state.status.buyers].filter(s=>!s?.ok).length;
    const message=!result.ok?'Frissítés fut.':state.config.source==='manual'?'Kézi Excel-import mód aktív.':failures?'Frissítés kész. Ellenőrizd a Források állapotát.':'Mindkét Excel-forrás frissült.';
    notify(message,result.ok&&!!failures&&state.config.source!=='manual');
  }finally{button.disabled=false;}
});
$('#export').onclick=guarded(async()=>{
  const button=$('#export');button.disabled=true;
  try {const blob=await api('export?'+key());const url=URL.createObjectURL(blob);const link=document.createElement('a');link.href=url;link.download=`SMC-Compass-FY${year}.xlsx`;link.click();setTimeout(()=>URL.revokeObjectURL(url),2000);notify('Excel-export elkészült.');}
  finally{button.disabled=false;}
});

async function boot() {
  await updateState();await render();
  setInterval(async()=>{
    if(!state||!['overview','matrix','pipeline'].includes(current)||document.activeElement?.matches('input,select,textarea')||$('#dialog').open)return;
    try {const previous=freshness;await updateState();if(previous!==freshness)await render();} catch { /* A bezárt helyi alkalmazás nem küld frissítést. */ }
  },3500);
}
guarded(boot)();

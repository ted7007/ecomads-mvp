import { createRequire } from 'node:module';
import { pathToFileURL } from 'node:url';
import fs from 'node:fs/promises';
const root='C:/Work/ecomads-mvp/Ecomads/Ecomads.WebApplication/ClientApp';
const output='C:/Work/ecomads-mvp/Ecomads/artifacts/results-tests';
const { createServer }=await import(pathToFileURL(root+'/node_modules/vite/dist/node/index.js'));
const {chromium}=createRequire(import.meta.url)('C:/Users/ted70/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright');
const code=`import React from 'react';import{createRoot}from'react-dom/client';import{DashboardKpiGrid}from'/src/pages/DashboardPage/components/DashboardKpiGrid.tsx';
const mode=new URLSearchParams(location.search).get('mode');
const days=Array.from({length:30},(_,i)=>({date:'2026-09-'+String(i+1).padStart(2,'0'),spend:mode==='empty'?null:mode==='partial'&&i===24?null:100,orders:1000,drr:10,checkedCampaigns:1,expectedCampaigns:1,checkedStores:1,expectedStores:1}));
const coverage={days,confirmedDays:29,drr:10,status:'preliminary',reason:null,lastCheckedAtUtc:null};
createRoot(document.getElementById('root')).render(React.createElement(DashboardKpiGrid,{campaigns:[],priorCampaigns:[],days:[],previousDays:[],coverage,previousCoverage:{...coverage,days:days.map(d=>({...d,spend:200}))},selectedMetrics:['spend'],onSelect:()=>{}}));`;
const server=await createServer({root,server:{host:'127.0.0.1',port:5192},plugins:[{name:'spend-check',configureServer(s){s.middlewares.use(async(req,res,next)=>{if(req.url?.startsWith('/__spend')){res.setHeader('Content-Type','text/html;charset=utf-8');res.end(await s.transformIndexHtml(req.url,'<div id="root"></div><script type="module" src="/__fixture.jsx"></script>'));}else next();});},resolveId(id){if(id==='/__fixture.jsx')return id;},load(id){if(id==='/__fixture.jsx')return code;}}]});
let browser;
try{await server.listen();browser=await chromium.launch({headless:true,executablePath:'C:/Users/ted70/AppData/Local/ms-playwright/chromium_headless_shell-1217/chrome-headless-shell-win64/chrome-headless-shell.exe'});
const results=[];
for(const width of[1440,390]){const page=await browser.newPage({viewport:{width,height:900}});const errors=[];page.on('pageerror',e=>errors.push(e.message));
for(const mode of['partial','full','empty']){await page.goto('http://localhost:5192/__spend?mode='+mode);const card=page.getByRole('button',{name:/^Расход:/});await card.waitFor();const text=await card.innerText();
if(mode==='partial'&&(!text.includes('29 из 30 дней')||!text.replace(/\s/g,'').includes('2900')||text.includes('было')))throw Error(text);
if(mode==='full'&&(!text.replace(/\s/g,'').includes('3000')||!text.includes('−50%')||text.includes('Предварительно')))throw Error(text);
if(mode==='empty'&&(!text.includes('—')||!text.includes('Нет подтверждённого расхода')))throw Error(text);
await page.screenshot({path:output+'/spend-'+width+'-'+mode+'.png'});results.push({width,mode,text,errors:[...errors]});}
await page.close();}if(results.some(r=>r.errors.length))throw Error(JSON.stringify(results));await fs.writeFile(output+'/spend-ui.json',JSON.stringify(results,null,2));console.log(JSON.stringify(results));
}finally{await browser?.close();await server.close();}



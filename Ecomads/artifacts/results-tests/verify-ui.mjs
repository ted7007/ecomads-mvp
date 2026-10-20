import { createRequire } from 'node:module';
import { pathToFileURL } from 'node:url';
import fs from 'node:fs/promises';
const root = 'C:/Work/ecomads-mvp/Ecomads/Ecomads.WebApplication/ClientApp';
const output = 'C:/Work/ecomads-mvp/Ecomads/artifacts/results-tests';
const { createServer } = await import(pathToFileURL(root + '/node_modules/vite/dist/node/index.js'));
const require = createRequire(import.meta.url);
const { chromium } = require('C:/Users/ted70/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright');
const fixture = JSON.parse(await fs.readFile(output+'/fixture.json','utf8'));
const id = '11111111-1111-4111-8111-111111111111';
const store = {id,name:'Тестовый кабинет',externalId:'test',tokenLastFour:'TEST',tokenExpiresAtUtc:'2027-01-01T00:00:00Z',lastSyncAt:'2026-10-20T05:00:00Z',campaignCount:1,jamStatus:'unknown',jamCheckedAtUtc:null,autoRefreshEnabled:true,autoRefreshAvailable:true,autoRefreshBlocked:false};
const statuses=['confirmed','measuring','not_evaluated','redistributed','done','not_confirmed','no_effect','not_applied','irrelevant','snoozed','historical','pending'];
const rows=statuses.map((status,i)=>({...structuredClone(fixture.report.rows[0]),id:i===0?fixture.report.rows[0].id:id.slice(0,-2)+String(i+10),status,
 savings:status==='confirmed'?800:status==='no_effect'?0:null,reason:status==='not_evaluated'?'Методика смешанного пакета не согласована':status==='measuring'?'Идёт семидневный замер':fixture.report.rows[0].reason,
 members:[{...fixture.report.rows[0].members[0],object:i===0?'плед на диван':'Тестовый объект '+i,decision:status==='irrelevant'?'irrelevant':status==='snoozed'?'snoozed':null}]}));
const bannerCode=`import React from 'react';import{createRoot}from'react-dom/client';import{ThemeProvider}from'@mui/material';import{MemoryRouter}from'react-router-dom';import{QueryClient,QueryClientProvider}from'@tanstack/react-query';import{workspaceLightTheme}from'/src/app/theme.ts';import{ResultsBanner}from'/src/pages/ResultsPage/ResultsBanner.tsx';createRoot(document.getElementById('root')).render(React.createElement(ThemeProvider,{theme:workspaceLightTheme},React.createElement(QueryClientProvider,{client:new QueryClient()},React.createElement(MemoryRouter,null,React.createElement(ResultsBanner,{storeId:'${id}'})))));`;
const server=await createServer({root,configFile:root+'/vite.config.ts',server:{port:5191,strictPort:true},plugins:[{name:'banner-test',configureServer(s){s.middlewares.use(async(req,res,next)=>{if(req.url!=='/__banner')return next();res.setHeader('Content-Type','text/html');res.end(await s.transformIndexHtml('/__banner','<html><head><meta name="viewport" content="width=device-width,initial-scale=1" /></head><body><div id="root"></div><script type="module">'+bannerCode+'</script></body></html>'));})}}]});
await server.listen();let browser;
const results=[];
try {
 browser=await chromium.launch({headless:true,executablePath:'C:/Users/ted70/AppData/Local/ms-playwright/chromium_headless_shell-1217/chrome-headless-shell-win64/chrome-headless-shell.exe'});
 for(const width of [1440,390]){
  const page=await browser.newPage({viewport:{width,height:1100}});let mode='full';const errors=[];const mutations=[];
  page.on('pageerror',e=>{errors.push(e.message);console.log('PAGE ERROR '+e.message);});
  await page.addInitScript(()=>{localStorage.setItem('ecomads_token','test');});
  await page.route('https://fonts.googleapis.com/**',route=>route.fulfill({contentType:'text/css',body:''}));
  await page.route('http://localhost:5191/api/**',async route=>{
   const req=route.request(),u=new URL(req.url()); if(req.method()!=='GET')mutations.push(req.method()+' '+u.pathname);
   let body;
   if(u.pathname==='/api/auth/me')body={id,name:'Test',email:'test@example.test',isDemoUser:false};
   else if(u.pathname==='/api/wb/stores')body=mode==='no-stores'?[]:[store];
   else if(u.pathname.startsWith('/api/wb/results/'))body={...fixture.detail,row:{...rows[0],id:u.pathname.split('/').at(-1)}};
   else if(u.pathname==='/api/wb/results'){
    if(mode==='error')return route.fulfill({status:500,contentType:'application/json',body:'{"message":"test error"}'});
    if(mode==='loading')await new Promise(r=>setTimeout(r,2000));
    const filter=u.searchParams.get('filter')??'all';const size=Number(u.searchParams.get('pageSize')??10);let list=mode==='empty'||mode==='no-stores'?[]:rows;
    list=list.filter(r=>filter==='all'||filter==='measuring'&&r.status==='measuring'||filter==='not_applied'&&r.status==='not_applied'||filter==='rejected'&&['snoozed','irrelevant'].includes(r.status)||filter==='result'&&['confirmed','not_evaluated','redistributed','done','not_confirmed','no_effect'].includes(r.status));
    const p=Math.min(Number(u.searchParams.get('page')??0),Math.max(0,Math.ceil(list.length/size)-1));
    body={...fixture.report,rows:list.slice(p*size,(p+1)*size),page:p,pageSize:size,total:list.length};
    if(mode==='empty'||mode==='no-stores')body={...body,summary:{...body.summary,savings:0,confirmedMeasurements:0,confirmedRecommendations:0,forecast:0,measuring:0,ordersBefore:null,ordersAfter:null,ordersReason:'Нет завершённых замеров'},weeks:[],funnel:{...body.funnel,issued:0,applied:0,confirmed:0,accepted:0,appliedWithoutAcceptance:0},collectionStartedAtUtc:null};
   }else return route.fulfill({status:404,contentType:'application/json',body:'{}'});
   await route.fulfill({contentType:'application/json',body:JSON.stringify(body)});
  });
  await page.goto('http://localhost:5191/results');
  try{await page.getByRole('heading',{name:'Сэкономлено на рекламе'}).waitFor();}catch(e){console.log(await page.locator('body').innerText());await page.screenshot({path:output+'/debug.png'});throw e;}
  await page.screenshot({path:output+'/'+width+'-top.png'});
  await page.getByRole('heading',{name:'Журнал действий'}).scrollIntoViewIfNeeded();
  await page.screenshot({path:output+'/'+width+'-journal.png'});
  await page.getByRole('button',{name:'Раскрыть результат: плед на диван',exact:true}).click();
  await page.getByText('Как посчитано',{exact:true}).first().scrollIntoViewIfNeeded();
  await page.screenshot({path:output+'/'+width+'-details.png'});
  if(width===1440)await page.getByRole('button',{name:'5 строк на странице',exact:true}).click();
  else {await page.getByRole('combobox').last().click();await page.getByRole('option',{name:'5',exact:true}).click();}
  if(width===1440)await page.getByRole('button',{name:'Go to page 2',exact:true}).click();
  else await page.getByRole('button',{name:'Следующая страница',exact:true}).click();
  await page.getByRole('button',{name:'Раскрыть результат: Тестовый объект 5',exact:true}).waitFor();
  await page.getByRole('button',{name:'На замере',exact:true}).click();
  await page.getByRole('button',{name:'Раскрыть результат: Тестовый объект 1',exact:true}).waitFor();
  await page.getByRole('button',{name:'Все',exact:true}).click();
  await page.getByRole('button',{name:'Всё время',exact:true}).click();
  const overflow=await page.evaluate(()=>({body:document.documentElement.scrollWidth>innerWidth,main:document.querySelector('main').scrollWidth>document.querySelector('main').clientWidth}));
  if(overflow.body||overflow.main||errors.length||mutations.length)throw Error(JSON.stringify({width,overflow,errors,mutations}));
  mode='empty';await page.reload();await page.getByText('История результатов пока накапливается',{exact:true}).waitFor();await page.screenshot({path:output+'/'+width+'-empty.png'});
  mode='no-stores';await page.reload();await page.getByRole('link',{name:'Подключить кабинет WB'}).waitFor();
  mode='loading';await page.reload();await page.getByText('Загружаем результаты…',{exact:true}).waitFor();await page.screenshot({path:output+'/'+width+'-loading.png'});await page.getByRole('heading',{name:'Сэкономлено на рекламе'}).waitFor();
  mode='error';await page.reload();await page.getByText('Не удалось загрузить результаты. Проверьте соединение и выбранный кабинет.').waitFor();
  mode='full';await page.goto('http://localhost:5191/__banner');await page.getByText(/Сэкономлено в этом месяце/).waitFor();
  if(!(await page.getByRole('link',{name:'Результаты'}).getAttribute('href')).includes('/results?storeId='))throw Error('Banner link');
  results.push({width,overflow,errors,mutations,filters:true,pagination:true,details:true,empty:true,noStores:true,loading:true,error:true,banner:true});await page.close();
 }
 await fs.writeFile(output+'/ui-results.json',JSON.stringify(results,null,2));console.log(JSON.stringify(results));
}finally{await browser?.close();await server.close();}

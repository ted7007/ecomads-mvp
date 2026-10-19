import assert from 'node:assert/strict';
import test from 'node:test';
import { build } from 'esbuild';
import { fileURLToPath } from 'node:url';
const result = await build({entryPoints:[fileURLToPath(new URL('../src/pages/WbStoresPage/storesModel.ts',import.meta.url))],bundle:true,write:false,format:'esm',platform:'node'});
const {displayJob,jobTone,jobStatus,jobProgress,canPause,summaryPeriodError,historyRows,sortedHistory,moscowTime}=await import(`data:text/javascript;base64,${Buffer.from(result.outputFiles[0].text).toString('base64')}`);
const job=(extra={})=>({id:'new',kind:'fullstats',status:'pending',stage:'waiting',waitReason:'rate_limit',paused:false,totalCount:0,processedCount:0,unit:'campaign',nextAttemptAtUtc:'2099-01-01T00:00:00Z',canRetry:false,...extra});
test('active job takes precedence consistently over old completed job',()=>{
 const current=job(),old=job({id:'old',status:'completed'});
 const selected=displayJob({activeJob:current,lastJob:old});
 assert.equal(selected,current);assert.equal(jobTone(selected),'warning');assert.equal(jobStatus(selected),'Ожидание лимита WB');
 assert.equal(displayJob({activeJob:null,lastJob:old}),old);
 assert.equal(displayJob({activeJob:null,lastJob:null}),null);
});
test('semantic status and action eligibility distinguish pause, failures and no job',()=>{
 assert.equal(jobTone(null),'neutral');assert.equal(jobTone(job({status:'completed'})),'success');assert.equal(jobTone(job({status:'failed'})),'error');
 assert.equal(jobTone(job({kind:'funnel_year',paused:true})),'neutral');
 assert.equal(jobTone(job({status:'running',waitReason:null,stage:'requesting'})),'info');
 assert.equal(canPause(job({kind:'funnel_year'})),true);assert.equal(canPause(job()),false);
 assert.equal(canPause(job({kind:'funnel_year',status:'completed'})),false);
});
test('unknown total is a count, not a percentage or an invented total',()=>{
 assert.equal(jobProgress(job({processedCount:3})),'3 кампании');
 assert.equal(jobProgress(job({retryCount:5})),'0 кампаний');
 assert.equal(jobProgress(job({totalCount:12,processedCount:9,retryCount:2})),'9 из 10 кампаний · повторно 0 из 2');
});
test('summary periods validate inclusive 31-day boundary and Moscow completed days',()=>{
 assert.equal(summaryPeriodError('2026-09-19','2026-10-19','2026-10-20'),null);
 assert.match(summaryPeriodError('2026-09-18','2026-10-19','2026-10-20'),/31/);
 assert.match(summaryPeriodError('2026-10-20','2026-10-20','2026-10-20'),/вчера/);
 assert.equal(summaryPeriodError('2026-10-19','2026-10-19','2026-10-20'),null);
 for(const [start,end] of [['','2026-10-19'],['2026-02-30','2026-03-01'],['2026-10-19','2026-10-18']])assert.ok(summaryPeriodError(start,end,'2026-10-20'));
});
test('history keys do not collapse duplicate cabinet names and dates, sort before pagination',()=>{
 const events=[{storeName:'Я',reportDate:'2026-10-18',lastAtUtc:'2026-10-19T06:00:00Z',chats:[]},{storeName:'Я',reportDate:'2026-10-18',lastAtUtc:'2026-10-19T06:00:00Z',chats:[]},{storeName:'А',reportDate:'2026-10-17',lastAtUtc:'2026-10-20T06:00:00Z',chats:[]}];
 const rows=historyRows(events);assert.equal(new Set(rows.map(r=>r.key)).size,3);
 assert.equal(sortedHistory(rows,'lastAtUtc',false).slice(0,1)[0].storeName,'А');
 assert.deepEqual(sortedHistory(rows,'storeName',true).map(r=>r.storeName),['А','Я','Я']);
 assert.deepEqual(sortedHistory(rows,'reportDate',true).map(r=>r.reportDate),['2026-10-17','2026-10-18','2026-10-18']);
 assert.equal(rows[0].storeName,'Я');
});

test('connection dates are displayed in Moscow including UTC day rollover',()=>{
 assert.match(moscowTime('2026-10-19T22:30:00Z'),/20\.10\.2026/);
 assert.match(moscowTime('2026-10-19T22:30:00Z'),/01:30.*МСК/);
 assert.equal(moscowTime(null),'—');
});

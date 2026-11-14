import assert from 'node:assert/strict';
import test from 'node:test';
import { build } from 'esbuild';
import { fileURLToPath } from 'node:url';
async function load(path) {
 const result=await build({entryPoints:[fileURLToPath(new URL(path,import.meta.url))],bundle:true,write:false,format:'esm',platform:'node'});
 return import(`data:text/javascript;base64,${Buffer.from(result.outputFiles[0].text).toString('base64')}`);
}
const {sortRows}=await load('../src/shared/lib/sortRows.ts');
const {pairKey,clusterCtr,jamCandidates,packageCards,recommendationItems}=await load('../src/pages/CampaignPage/components/campaignReportModel.ts');
test('null, undefined and NaN stay last in both directions; zero is known; source unchanged',()=>{
 const rows=[{id:'b',v:2},{id:'a',v:2},{id:'nil',v:null},{id:'nan',v:NaN},{id:'zero',v:0},{id:'missing'}];
 for(const asc of [true,false]){
  const sorted=sortRows(rows,r=>r.v,asc,r=>r.id);
  assert.deepEqual(sorted.slice(0,3).map(r=>r.id),asc?['zero','a','b']:['a','b','zero']);
  assert.deepEqual(sorted.slice(3).map(r=>r.id),['missing','nan','nil']);
 }
 assert.deepEqual(rows.map(r=>r.id),['b','a','nil','nan','zero','missing']);
});
test('sort is numeric before pagination and supports Russian strings',()=>{
 const rows=[{id:'Я',v:2},{id:'А',v:100},{id:'Б',v:10}];
 assert.deepEqual(sortRows(rows,r=>r.v,false,r=>r.id).slice(0,2).map(r=>r.v),[100,10]);
 assert.deepEqual(sortRows(rows,r=>r.id,true,r=>r.id).map(r=>r.id),['А','Б','Я']);
});
test('CTR needs positive views and known clicks, including known zero',()=>{
 assert.equal(clusterCtr({views:0,clicks:0}),null);
 assert.equal(clusterCtr({views:null,clicks:10}),null);
 assert.equal(clusterCtr({views:100,clicks:null}),null);
 assert.equal(clusterCtr({views:100,clicks:0}),0);
 assert.equal(clusterCtr({views:200,clicks:5}),2.5);
});
test('Jam shortlist requires positive orders and absence of loaded-cluster match',()=>{
 const rows=[{orders:3,matchingLoadedAdCluster:false},{orders:3,matchingLoadedAdCluster:true},{orders:0,matchingLoadedAdCluster:false},{orders:null,matchingLoadedAdCluster:false}];
 assert.deepEqual(jamCandidates(rows),[rows[0]]);
 assert.equal(pairKey('123',' ПЛЕД '),pairKey('123','плед'));
 assert.notEqual(pairKey('123','плед'),pairKey('124','плед'));
 assert.notEqual(pairKey('1:a','b'),pairKey('1','a:b'));
});
test('package deduplication applies only to R03; missing IDs and empty packages are safe',()=>{
 const a={evaluationId:'a',rule:{ruleId:'R03'}}, b={evaluationId:'a',rule:{ruleId:'R06'}}, c={evaluationId:'c',rule:{ruleId:'R03'}};
 const report={cards:[a,b,a,c]};
 assert.deepEqual(packageCards(report,['a','a','missing']),[a]);
 assert.deepEqual(packageCards(report,[]),[]);
 assert.equal(report.cards.length,4);
});

test('package occupies the rank of its first member and does not duplicate members',()=>{
 const expensive={evaluationId:'expensive',rule:{ruleId:'R06'}};
 const a={evaluationId:'a',rule:{ruleId:'R03'}}, b={evaluationId:'b',rule:{ruleId:'R03'}};
 const middle={evaluationId:'middle',rule:{ruleId:'R06'}};
 const items=recommendationItems({cards:[expensive,a,middle,b],packages:[{id:'package',evaluationIds:['b','a']}]});
 assert.deepEqual(items.map(x=>x.id),['expensive:R06','package','middle:R06']);
 assert.deepEqual(items[1].cards,[a,b]);
});

const {recommendationAction,recommendationLimitations,recommendationMoney,recommendationReason}=await load('../src/shared/lib/recommendationPresentation.ts');
test('weekly spend is distinct from savings and unavailable money is omitted',()=>{
 assert.equal(recommendationMoney({subject:{scope:'cluster'},ranking:null}),null);
 const money=recommendationMoney({preliminary:true,ranking:{weeklyInefficientSpend:5000,basis:'spend_above_norm'}});
 assert.match(money.text,/Расход сверх нормы: около 5\s000 ₽ в неделю/);
 assert.doesNotMatch(money.text,/Экономия/i);
});
const cluster = {rule:{ruleId:'R03'},subject:{scope:'cluster',clusterName:'плед'},facts:{spend:1240,orderedProducts:0,clicks:40}};
test('CPC and low clicks both keep observation, but each explains the right reason in the right place',()=>{
 const watch={kind:'watch',label:'Наблюдать'};
 const cpc={...cluster,presentation:{primary:watch,restrictions:[{code:'settings_unsupported',label:'WB не поддерживает…'}]}};
 assert.equal(recommendationAction(cpc),'Пока наблюдайте за кластером.');
 assert.doesNotMatch(recommendationReason(cpc),/WB|CPC|кликов/);
 assert.deepEqual(recommendationLimitations(cpc),['EcomAds не предлагает менять ставку или исключать этот кластер.']);
 const highCpc={...cpc,rule:{ruleId:'R06'},economy:{drr:.38},norm:{value:.2},presentation:{...cpc.presentation,restrictions:[
  ...cpc.presentation.restrictions,{code:'unified_below_exclusion'}]}};
 assert.equal(recommendationAction(highCpc),'Пока наблюдайте за кластером.');
 assert.doesNotMatch(recommendationReason(highCpc),/Порог для исключения/);
 const few={...cluster,presentation:{primary:watch,restrictions:[{code:'insufficient_clicks'}],clickEvidence:{minimumClicks:150,source:'fallback'}}};
 assert.match(recommendationReason(few),/нужно 150 кликов, пока их 40/);
 assert.deepEqual(recommendationLimitations(few),[]);
 assert.doesNotMatch(recommendationReason({...few,facts:{...few.facts,orderedProducts:null,clicks:null}}),/ни одной|0 кликов/);
});
test('a budget action distinguishes a known shortfall from an unknown balance',()=>{
 const topUp = (balance) => ({...cluster,rule:{ruleId:'R02'},presentation:{primary:{kind:'top_up_budget'},restrictions:[],
  details:{kind:'budget',balance,topUp:4200.25,budget:1200,dailySpend:1800}}});
 assert.match(recommendationAction(topUp(1000)),/^Сначала пополните рекламный счёт WB, затем бюджет кампании на 4\s200,25 ₽/);
 assert.match(recommendationAction(topUp(null)),/^Пополните бюджет кампании на 4\s200,25 ₽/);
 assert.match(recommendationLimitations(topUp(null)).join(),/Баланс рекламного счёта не подтверждён/);
});
test('bid text preserves the placement and exact target for product and cluster changes',()=>{
 const bid = {...cluster,rule:{ruleId:'R07'},presentation:{restrictions:[],primary:{kind:'reduce_article_bid',
  placement:'recommendation',label:'Снизьте ставку в рекомендациях: 140,65 → 98,46 ₽',targetBid:98.46}}};
 assert.equal(recommendationAction(bid),'Снизьте ставку товара в рекомендациях с 140,65 до 98,46 ₽.');
 assert.equal(recommendationAction({...cluster,presentation:{restrictions:[],control:{currentBid:120.15},
  primary:{kind:'reduce_bid',label:'',targetBid:84.11}}}),'Снизьте ставку по кластеру с 120,15 до 84,11 ₽.');
});

import assert from 'node:assert/strict';
import test from 'node:test';
import { build } from 'esbuild';
import { fileURLToPath } from 'node:url';
async function load(path) {
 const result=await build({entryPoints:[fileURLToPath(new URL(path,import.meta.url))],bundle:true,write:false,format:'esm',platform:'node'});
 return import(`data:text/javascript;base64,${Buffer.from(result.outputFiles[0].text).toString('base64')}`);
}
const {sortRows}=await load('../src/shared/lib/sortRows.ts');
const {pairKey,clusterCtr,jamCandidates,packageCards}=await load('../src/pages/CampaignPage/components/campaignReportModel.ts');
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

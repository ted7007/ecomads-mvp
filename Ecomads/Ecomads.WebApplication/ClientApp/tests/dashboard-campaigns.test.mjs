import assert from 'node:assert/strict';
import test from 'node:test';
import { fileURLToPath } from 'node:url';
import { build } from 'esbuild';

const result = await build({ entryPoints: [fileURLToPath(new URL('../src/pages/DashboardPage/components/campaignRows.ts', import.meta.url))], bundle: true, write: false, format: 'esm', platform: 'node' });
const { campaignValue, sortCampaigns } = await import(`data:text/javascript;base64,${Buffer.from(result.outputFiles[0].text).toString('base64')}`);
const campaign = (id, spend, extra = {}) => ({ id, name: id, targetDrr: 10, minCtr: 3, wbStatus: 9,
  kpi: { spend, revenue: 100, drr: spend, clicks: 10, impressions: 100, ctr: 10, coverageDays: 7, expectedDays: 7, ...extra } });
const ids = rows => rows.map(row => row.id);

test('numeric ordering applies to the complete set before slicing a page', () => {
  const rows = [campaign('two', 2), campaign('hundred', 100), campaign('ten', 10)];
  assert.deepEqual(ids(sortCampaigns(rows, [], 'spend', false).slice(0, 2)), ['hundred', 'ten']);
  assert.deepEqual(ids(sortCampaigns(rows, [], 'spend', true)), ['two', 'ten', 'hundred']);
  assert.deepEqual(ids(rows), ['two', 'hundred', 'ten']);
});
test('unknown DRR and CPC stay last in both directions, zero remains a real value', () => {
  const rows = [campaign('unknown', 50, { revenue: 0, clicks: 0 }), campaign('zero', 0), campaign('known', 10)];
  for (const key of ['drr', 'cpc']) {
    assert.deepEqual(ids(sortCampaigns(rows, [], key, true)), ['zero', 'known', 'unknown']);
    assert.deepEqual(ids(sortCampaigns(rows, [], key, false)), ['known', 'zero', 'unknown']);
  }
});
test('spend deltas require full coverage and a positive baseline', () => {
  const current = campaign('a', 50);
  assert.equal(campaignValue(current, 'delta', campaign('a', 100)), -50);
  assert.equal(campaignValue(current, 'delta', campaign('a', 0)), null);
  assert.equal(campaignValue(current, 'delta', campaign('a', 100, { coverageDays: 6 })), null);
  assert.equal(campaignValue(campaign('a', 0, { coverageDays: 0 }), 'spend'), null);
});
test('negative changes sort numerically and equal values have a stable ID tie-break', () => {
  const rows = [campaign('b', 50), campaign('a', 50), campaign('c', 150)];
  const prior = rows.map(row => campaign(row.id, 100));
  assert.deepEqual(ids(sortCampaigns(rows, prior, 'delta', true)), ['a', 'b', 'c']);
  assert.deepEqual(ids(sortCampaigns(rows, prior, 'delta', false)), ['c', 'a', 'b']);
});
test('Russian campaign names sort by locale', () => {
  const rows = [campaign('Яблоко', 1), campaign('Бета', 1), campaign('Альфа', 1)];
  assert.deepEqual(ids(sortCampaigns(rows, [], 'name', true)), ['Альфа', 'Бета', 'Яблоко']);
});

import assert from 'node:assert/strict';
import test from 'node:test';
import { fileURLToPath } from 'node:url';
import { build } from 'esbuild';

const result = await build({ entryPoints: [fileURLToPath(new URL('../src/pages/DashboardPage/components/spendSummary.ts', import.meta.url))], bundle: true, write: false, format: 'esm', platform: 'node' });
const { spendSummary } = await import(`data:text/javascript;base64,${Buffer.from(result.outputFiles[0].text).toString('base64')}`);
const days = Array.from({ length: 30 }, (_, i) => ({ date: `day-${i}`, spend: 100 }));

test('one unknown day preserves the sum of 29 confirmed days and identifies the gap', () => {
  const result = spendSummary(days.map((day, i) => i === 5 ? { ...day, spend: null } : day));
  assert.equal(result.total, 2900); assert.equal(result.complete, false);
  assert.equal(result.confirmedDays, 29); assert.equal(result.expectedDays, 30);
  assert.deepEqual(result.missingDates, ['day-5']);
});
test('full coverage permits comparison, including explicit confirmed zero', () => {
  const result = spendSummary(days.map(day => ({ ...day, spend: 0 })));
  assert.equal(result.total, 0); assert.equal(result.complete, true); assert.equal(result.confirmedDays, 30);
});
test('no confirmed days never produces a fabricated zero', () => {
  for (const rows of [[], days.map(day => ({ ...day, spend: null }))]) {
    const result = spendSummary(rows); assert.equal(result.total, null); assert.equal(result.complete, false);
  }
});

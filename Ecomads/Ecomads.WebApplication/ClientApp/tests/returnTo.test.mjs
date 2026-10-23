import { test } from 'node:test';
import assert from 'node:assert/strict';
import { build } from 'esbuild';

const bundle = await build({ entryPoints: ['src/shared/auth/returnTo.ts'], bundle: true, write: false, format: 'esm', platform: 'node' });
const { safeReturnTo, withReturnTo } = await import(`data:text/javascript;base64,${Buffer.from(bundle.outputFiles[0].text).toString('base64')}`);

test('keeps campaign filters and fragment through both public pages', () => {
  const target = '/campaign/abc?startDate=2026-10-01&storeId=123#clusters';
  const site = withReturnTo('/site', target);
  const login = withReturnTo('/login', new URL(site, 'https://ecomads.ru').searchParams.get('returnTo'));
  assert.equal(safeReturnTo(new URL(login, 'https://ecomads.ru').searchParams.get('returnTo')), target);
});

test('rejects external destinations and authentication redirect loops', () => {
  for (const target of ['https://evil.invalid', '//evil.invalid', '/\\evil.invalid', '/site', '/login?returnTo=/login', '/%2f%2fevil.invalid', '/campaign/../../login', '/dashboard\nLocation:evil'])
    assert.equal(safeReturnTo(target), '/dashboard', target);
});

test('permits all supported internal product routes', () => {
  for (const target of ['/dashboard', '/results', '/norms', '/wb-stores', '/demo-feedback', '/admin/pilot-applications'])
    assert.equal(safeReturnTo(target), target);
});

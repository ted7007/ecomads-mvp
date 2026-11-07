import { test } from 'node:test';
import assert from 'node:assert/strict';
import { runInNewContext } from 'node:vm';
import { build } from 'esbuild';

const bundle = await build({ entryPoints: ['src/shared/auth/attribution.ts'], bundle: true, write: false,
  format: 'iife', globalName: 'attribution', platform: 'node' });
const now = Date.parse('2026-11-07T00:00:00.000Z');
const day = 24 * 60 * 60 * 1000;

function storage() {
  const values = new Map();
  return { getItem: key => values.get(key) ?? null, setItem: (key, value) => values.set(key, value) };
}

function page(localStorage = storage()) {
  const context = { localStorage, URL };
  runInNewContext(bundle.outputFiles[0].text, context);
  return context.attribution;
}

function plain(value) {
  return value === undefined ? undefined : JSON.parse(JSON.stringify(value));
}

test('landing CTA and tab reopening preserve only the first source and its original time', () => {
  const localStorage = storage();
  const landing = page(localStorage);
  landing.captureAttribution('https://ecomads.ru/site?utm_source=Campaign_A&email=private&returnTo=/dashboard', now);
  const expected = { utm_source: 'Campaign_A', captured_at_utc: new Date(now).toISOString() };
  const registration = page(localStorage);
  registration.captureAttribution('https://ecomads.ru/register', now + 1000);
  assert.deepEqual(plain(registration.getAttribution(now + 1000)), expected);
  const reopened = page(localStorage);
  reopened.captureAttribution('https://ecomads.ru/site', now + day);
  reopened.captureAttribution('https://ecomads.ru/site?utm_source=Campaign_B', now + 29 * day);
  assert.deepEqual(plain(reopened.getAttribution(now + 29 * day)), expected);
});

test('expires exactly after 30 days and a new tagged visit starts a new period', () => {
  const app = page();
  app.captureAttribution('https://ecomads.ru/?utm_source=A', now);
  assert.equal(app.getAttribution(now + 30 * day - 1).utm_source, 'A');
  assert.equal(app.getAttribution(now + 30 * day), undefined);
  app.captureAttribution('https://ecomads.ru/register', now + 30 * day);
  assert.equal(app.getAttribution(now + 30 * day), undefined);
  app.captureAttribution('https://ecomads.ru/site?utm_source=B', now + 30 * day);
  assert.deepEqual(plain(app.getAttribution(now + 30 * day)), {
    utm_source: 'B', captured_at_utc: new Date(now + 30 * day).toISOString()
  });
});

test('missing, invalid, duplicated and nested sources are ignored', () => {
  for (const query of ['', 'utm_source=', 'utm_source=a%0A', 'utm_source=a+b', 'utm_source=%D1%8F',
    'utm_source=' + 'a'.repeat(129), 'utm_source=A&utm_source=B', 'returnTo=%2Fdashboard%3Futm_source%3DA']) {
    const app = page();
    app.captureAttribution('https://ecomads.ru/site?' + query, now);
    assert.equal(app.getAttribution(now), undefined, query);
  }
  const app = page();
  app.captureAttribution('https://ecomads.ru/site?utm_source=A.z-0_9', now);
  assert.equal(app.getAttribution(now).utm_source, 'A.z-0_9');
});

test('corrupt storage and blocked storage do not prevent capture', () => {
  for (const damaged of ['{', 'null', '3', '{"utm_source":false}', JSON.stringify({
    utm_source: 'future', captured_at_utc: new Date(now + day).toISOString()
  })]) {
    const app = page({ getItem: () => damaged, setItem() {} });
    assert.equal(app.getAttribution(now), undefined);
    app.captureAttribution('https://ecomads.ru/site?utm_source=A', now);
    assert.equal(app.getAttribution(now + 1).utm_source, 'A');
  }
  const app = page({ getItem() { throw Error('blocked'); }, setItem() { throw Error('blocked'); } });
  app.captureAttribution('https://ecomads.ru/site?utm_source=A', now);
  app.captureAttribution('https://ecomads.ru/register?utm_source=B', now + day);
  assert.equal(app.getAttribution(now + day).utm_source, 'A');
  assert.equal(app.getAttribution(now + 30 * day), undefined);
});

test('application measures the original tagged URL before redirects and observes SPA campaigns', async () => {
  const appBundle = await build({ entryPoints: ['src/main.tsx'], bundle: true, write: false, format: 'iife',
    platform: 'node', jsx: 'automatic', define: { 'process.env.NODE_ENV': '"production"' }, plugins: [{ name: 'ui-stubs', setup(builder) {
      builder.onResolve({ filter: /^(react-router-dom|react-dom\/client)$|\/RequireAuth$|\/app\/App$/ },
        args => ({ path: args.path, namespace: 'ui-stubs' }));
      builder.onResolve({ filter: /\/pages\/|\/ui\/AppLayout$/ }, args => ({ path: args.path, external: true }));
      builder.onLoad({ filter: /.*/, namespace: 'ui-stubs' }, args => ({ contents:
        args.path === 'react-router-dom' ? `
          export const Navigate = () => null;
          export function createBrowserRouter() {
            globalThis.beforeRouter();
            const router = { state: { initialized: true, navigation: { state: 'idle' },
              location: new URL(window.location.href) }, subscribe(callback) { globalThis.navigate = url => {
                router.state.location = new URL(url); callback();
              }; } };
            return router;
          }` : args.path === 'react-dom/client' ? 'export default { createRoot: () => ({ render() {} }) };'
          : 'export const App = () => null; export const RequireAuth = () => null;'
      }));
    } }] });
  for (const path of ['/', '/dashboard', '/site']) {
    const initialUrl = `https://ecomads.ru${path}?utm_source=A&extra=private`;
    const localStorage = storage();
    const scripts = [];
    const context = { URL, localStorage, window: { location: new URL(initialUrl) },
      document: { scripts, title: 'EcomAds', referrer: '', getElementById: () => ({}),
        createElement: () => ({}), head: { appendChild: script => scripts.push(script) } },
      beforeRouter() {
        assert.equal(context.window.ym.a[0][1], 'init');
        assert.equal(context.window.ym.a[0][2].url, initialUrl);
        assert.equal(JSON.parse(localStorage.getItem('ecomads.firstAttribution')).utm_source, 'A');
      }
    };
    runInNewContext(appBundle.outputFiles[0].text, context);
    assert.equal(context.window.ym.a.length, 1, 'initial location is measured only once');
    context.navigate('https://ecomads.ru/site');
    context.navigate('https://ecomads.ru/register?utm_source=B');
    assert.equal(context.window.ym.a[2][1], 'hit');
    assert.equal(JSON.parse(localStorage.getItem('ecomads.firstAttribution')).utm_source, 'A');
    assert.equal(context.window.location.href, initialUrl, 'attribution never strips the URL');
  }
});

test('normal registration API reads current attribution and excludes expired data', async () => {
  const apiBundle = await build({ stdin: { contents: `
    export { register, login } from './src/shared/auth/authApi';
    export { captureAttribution } from './src/shared/auth/attribution';
  `, resolveDir: process.cwd() }, bundle: true, write: false, format: 'iife', globalName: 'auth', platform: 'node' });
  let time = now;
  const requests = [];
  class Clock extends Date { static now() { return time; } }
  const context = { URL, Date: Clock, localStorage: storage(), Headers, Blob, FormData,
    fetch: async (url, options) => {
      requests.push({ url, body: JSON.parse(options.body) });
      return new Response(JSON.stringify({ token: 'token', sellerId: 'seller', name: 'User', email: 'user@example.test' }),
        { headers: { 'content-type': 'application/json' } });
    } };
  runInNewContext(apiBundle.outputFiles[0].text, context);
  context.auth.captureAttribution('https://ecomads.ru/site?utm_source=A&secret=private');
  const credentials = { name: 'User', email: 'user@example.test', password: 'password' };
  await context.auth.register(credentials);
  assert.deepEqual(requests[0].body.attribution, { utm_source: 'A', captured_at_utc: new Date(now).toISOString() });
  await context.auth.login({ email: credentials.email, password: credentials.password });
  assert.equal(requests[1].body.attribution, undefined);
  time += 30 * day;
  await context.auth.register(credentials);
  assert.equal(requests[2].body.attribution, undefined);
});

import React from 'react';
import ReactDOM from 'react-dom/client';
import { App } from './app/App';
import { router } from './app/router';
import { trackPage } from './shared/analytics/metrika';
import { captureAttribution } from './shared/auth/attribution';

function trackCurrentPage() {
  if (!router.state.initialized || router.state.navigation.state !== 'idle') return;
  const { pathname, search, hash } = router.state.location;
  const url = new URL(pathname + search + hash, window.location.origin).href;
  captureAttribution(url);
  trackPage(url);
}
router.subscribe(trackCurrentPage);
trackCurrentPage();

ReactDOM.createRoot(document.getElementById('root') as HTMLElement).render(
  <React.StrictMode>
    <App />
  </React.StrictMode>
);


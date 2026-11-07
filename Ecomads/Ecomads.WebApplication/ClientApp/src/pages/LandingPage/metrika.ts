const counterId = 113819247;
const tagUrl = `https://mc.yandex.ru/metrika/tag.js?id=${counterId}`;

type Metrika = ((...args: unknown[]) => void) & { a?: unknown[][]; l?: number };

export function startLandingMetrika() {
  const target = window as Window & { ym?: Metrika };
  if (!target.ym) {
    const queue: Metrika = (...args) => { (queue.a ??= []).push(args); };
    queue.l = Date.now();
    target.ym = queue;
  }
  if (!Array.from(document.scripts).some(script => script.src === tagUrl)) {
    const script = document.createElement('script');
    script.async = true;
    script.src = tagUrl;
    document.head.appendChild(script);
  }
  target.ym(counterId, 'init', {
    ssr: true, webvisor: true, clickmap: true, ecommerce: 'dataLayer',
    referrer: document.referrer, url: location.href,
    accurateTrackBounce: true, trackLinks: true,
  });
  return () => { target.ym?.(counterId, 'destruct'); };
}

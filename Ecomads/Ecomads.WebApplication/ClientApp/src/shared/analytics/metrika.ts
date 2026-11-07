const counterId = 113819247;
const tagUrl = `https://mc.yandex.ru/metrika/tag.js?id=${counterId}`;

type Metrika = ((...args: unknown[]) => void) & { a?: unknown[][]; l?: number };

let previousUrl: string | undefined;

export function trackPage(url: string) {
  if (url === previousUrl) return;
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
  if (previousUrl) {
    target.ym(counterId, 'hit', url, { referer: previousUrl, title: document.title });
  } else target.ym(counterId, 'init', {
    ssr: true, webvisor: true, clickmap: true, ecommerce: 'dataLayer',
    referrer: document.referrer, url,
    accurateTrackBounce: true, trackLinks: true,
  });
  previousUrl = url;
}

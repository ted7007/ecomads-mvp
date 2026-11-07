import { LegalLinks } from '../LegalPage/LegalLinks';
import { useState } from 'react';
import { Link, useLocation } from 'react-router-dom';
import { BrandLogo } from '../../shared/ui/BrandLogo';
import { safeReturnTo, withReturnTo } from '../../shared/auth/returnTo';

export function DemoDecision() {
  const [decision, setDecision] = useState<string | null>(null);
  return <div style={{ display: 'flex', flexWrap: 'wrap', gap: 6 }}>
    {decision ? <><span style={{ fontSize: 13, color: 'var(--good)' }} role="status">{decision}</span>
      <button className="mini" onClick={() => setDecision(null)}>Отменить</button></> : <>
      <button className="mini" style={{ background: 'var(--pri)', color: '#fff' }} onClick={() => setDecision('Принято — это пример решения')}>Принял</button>
      <button className="mini" onClick={() => setDecision('Отложено — это пример решения')}>Отложить</button>
      <button className="mini" onClick={() => setDecision('Отмечено как неактуальное')}>Не актуально</button>
    </>}
  </div>;
}

const faq = [
  ['Сколько стоит?', 'После регистрации демо-доступ бесплатен в течение 3 дней.'],
  ['ecomads сам меняет ставки?', 'Сервис анализирует данные и предлагает действия. Изменения в рекламном кабинете Wildberries вносите вы.'],
  ['Нужна ли подписка «Джем»?', 'Нет. Основная аналитика работает без неё. С подпиской доступна дополнительная аналитика по поисковым запросам.'],
  ['Какой токен нужен?', 'Базовый токен WB с доступом «Только чтение» к категории «Продвижение». Доступ к «Аналитике» и «Статистике» нужен для соответствующих дополнительных данных. Инструкция есть при подключении кабинета.'],
  ['Как часто обновляются данные?', 'Плановый сбор выполняется дважды в день: в 06:15 и 18:15 по Москве, если автообновление включено. Сводку в Telegram можно подключить в настройках кабинета.'],
  ['Откуда берутся рекомендации?', 'Правила сравнивают показатели кампаний и запросов с вашими нормами ДРР и другими метриками. Каждая рекомендация сопровождается причиной и предлагаемым действием.']
];
export function LandingFaq() {
  const [open, setOpen] = useState<number | null>(0);
  return <>{faq.map(([q, a], i) => <div key={q} style={{ background: 'var(--card)', border: '1px solid var(--line)', borderRadius: 16 }}>
    <button className="faqq" aria-expanded={open === i} aria-controls={`faq-${i}`} id={`faq-button-${i}`} onClick={() => setOpen(open === i ? null : i)}>
      <span>{q}</span><span aria-hidden="true">{open === i ? '−' : '+'}</span>
    </button>
    <div id={`faq-${i}`} role="region" aria-labelledby={`faq-button-${i}`} hidden={open !== i}>
      <p style={{ padding: '0 20px 18px', fontSize: 15, lineHeight: 1.55, color: 'var(--ink2)' }}>{a}</p>
    </div>
  </div>)}</>;
}

export function LandingFooter() {
  const location = useLocation();
  const loginUrl = withReturnTo('/login', safeReturnTo(new URLSearchParams(location.search).get('returnTo')));
  return <footer className="landing-footer"><div><a href="#top" aria-label="EcomAds — наверх"><BrandLogo height={28} /></a><span>© {new Date().getFullYear()}</span></div>
    <div><a href="https://t.me/ecomads_official?direct" target="_blank" rel="noopener noreferrer">Напишите в Telegram ↗</a><Link to={loginUrl}>Войти</Link></div>
  <LegalLinks /></footer>;
}

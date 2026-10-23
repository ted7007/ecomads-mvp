import { useState } from 'react';
import { Link, useLocation } from 'react-router-dom';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import { HttpError, httpClient } from '../../shared/api/httpClient';
import { BrandLogo } from '../../shared/ui/BrandLogo';
import { safeReturnTo, withReturnTo } from '../../shared/auth/returnTo';

const formSchema = z.object({
  name: z.string().trim().min(1, 'Укажите имя').max(100, 'До 100 символов'),
  contact: z.string().trim().max(100).regex(/^(@[A-Za-z0-9_]{5,32}|\+?[\d ()-]{7,25})$/, 'Укажите Telegram @username или телефон'),
  stores: z.enum(['', 'one', 'two-five', 'over-five', 'agency']),
  budget: z.enum(['', 'under-100k', '100k-500k', '500k-1m', 'over-1m']),
  jam: z.enum(['', 'yes', 'no', 'unknown']),
  contactConsent: z.boolean().refine(Boolean, 'Подтвердите, что команда может с вами связаться'),
  website: z.string().max(200)
});
type Values = z.infer<typeof formSchema>;
const defaults: Values = { name: '', contact: '', stores: '', budget: '', jam: '', contactConsent: false, website: '' };

export function PilotForm() {
  const [submissionId, setSubmissionId] = useState(() => crypto.randomUUID());
  const [sent, setSent] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const { register, handleSubmit, watch, setValue, reset, formState: { errors, isSubmitting } } = useForm<Values>({
    resolver: zodResolver(formSchema), defaultValues: defaults
  });
  const submit = handleSubmit(async (values) => {
    setError(null);
    try {
      const response = await httpClient<unknown>('/api/pilot-applications', { method: 'POST', skipAuth: true,
        body: { ...values, submissionId, stores: values.stores || null, budget: values.budget || null, jam: values.jam || null } });
      z.object({ accepted: z.literal(true) }).parse(response);
      setSent(true);
    } catch (e) {
      setError(e instanceof HttpError && e.status === 429
        ? `Слишком много попыток. Повторите через ${e.response.headers.get('Retry-After') || '60'} сек.`
        : 'Не удалось отправить заявку. Введённые данные сохранены — попробуйте ещё раз.');
    }
  });

  if (sent) return <div className="pilot-form" role="status" style={{ padding: '24px 0' }}>
    <span style={{ fontSize: 36, color: 'var(--good)' }} aria-hidden="true">✓</span>
    <h3 style={{ fontSize: 22 }}>Заявка отправлена</h3>
    <p>Свяжемся по указанному контакту, расскажем про пилот и поможем подключить кабинет.</p>
    <button type="button" className="btns" onClick={() => { reset(defaults); setSubmissionId(crypto.randomUUID()); setSent(false); }}>Заполнить заново</button>
  </div>;

  return <form className="pilot-form" onSubmit={submit} noValidate>
    <h3 style={{ fontSize: 22 }}>Заявка в пилот</h3>
    <label className="pilot-field" htmlFor="pilot-name">Как к вам обращаться
      <input className="fld" id="pilot-name" placeholder="Имя" autoComplete="name" maxLength={100} disabled={isSubmitting}
        aria-invalid={Boolean(errors.name)} aria-describedby={errors.name ? 'pilot-name-error' : undefined} {...register('name')} />
      {errors.name && <span id="pilot-name-error" role="alert">{errors.name.message}</span>}
    </label>
    <label className="pilot-field" htmlFor="pilot-contact">Telegram или телефон
      <input className="fld" id="pilot-contact" placeholder="@username или +7…" maxLength={100} disabled={isSubmitting}
        aria-invalid={Boolean(errors.contact)} aria-describedby={errors.contact ? 'pilot-contact-error' : undefined} {...register('contact')} />
      {errors.contact && <span id="pilot-contact-error" role="alert">{errors.contact.message}</span>}
    </label>
    <div className="pilot-row">
      <label className="pilot-field" htmlFor="pilot-stores">Кабинетов WB
        <select className="fld" id="pilot-stores" disabled={isSubmitting} {...register('stores')}>
          <option value="">Не указано</option><option value="one">Один</option><option value="two-five">2–5</option>
          <option value="over-five">Больше 5</option><option value="agency">Веду клиентов как агентство</option>
        </select>
      </label>
      <label className="pilot-field" htmlFor="pilot-budget">Реклама в месяц
        <select className="fld" id="pilot-budget" disabled={isSubmitting} {...register('budget')}>
          <option value="">Не указано</option><option value="under-100k">До 100 тыс. ₽</option>
          <option value="100k-500k">100–500 тыс. ₽</option><option value="500k-1m">500 тыс. – 1 млн ₽</option>
          <option value="over-1m">Больше 1 млн ₽</option>
        </select>
      </label>
    </div>
    <fieldset style={{ margin: 0, padding: 0, border: 0 }}>
      <legend style={{ marginBottom: 8, fontSize: 14 }}>Подписка «Джем»</legend>
      <div className="jam-options" role="group" aria-label="Подписка Джем">
        {([['yes', 'Есть'], ['no', 'Нет'], ['unknown', 'Не знаю']] as const).map(([value, label]) =>
          <button key={value} type="button" disabled={isSubmitting} aria-pressed={watch('jam') === value}
            onClick={() => setValue('jam', watch('jam') === value ? '' : value)}>{label}</button>)}
      </div>
    </fieldset>
    <label className="bot-field" aria-hidden="true">Ваш сайт
      <input tabIndex={-1} autoComplete="off" {...register('website')} />
    </label>
    <label className="contact-consent" htmlFor="pilot-consent">
      <input id="pilot-consent" type="checkbox" disabled={isSubmitting} {...register('contactConsent')}
        aria-invalid={Boolean(errors.contactConsent)} />
      <span>Подтверждаю, что команда EcomAds может связаться со мной по указанному контакту по поводу участия в пилоте.</span>
    </label>
    {errors.contactConsent && <span className="form-error" role="alert">{errors.contactConsent.message}</span>}
    <p className="pilot-note">Используем данные для обработки заявки и связи по пилоту. Храним заявку и контакт 180 дней. Без рассылок.</p>
    {error && <p className="form-error" role="alert">{error}</p>}
    <button className="btnp" type="submit" disabled={isSubmitting}>{isSubmitting ? 'Отправляем…' : 'Отправить заявку'}</button>
    <p className="pilot-note">Свяжемся по указанному контакту. Заявка не создаёт аккаунт. Для самостоятельного знакомства доступно <Link to="/login">демо на 3 дня</Link>.</p>
  </form>;
}

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
  ['Сколько стоит?', 'Самостоятельное демо доступно на 3 дня. Участие в пилоте обсуждается с командой по заявке и бесплатно на согласованный период.'],
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
    <div><a href="https://t.me/ecomads_mvp01" target="_blank" rel="noopener noreferrer">Группа EcomAds в Telegram ↗</a><Link to={loginUrl}>Войти</Link></div>
  </footer>;
}

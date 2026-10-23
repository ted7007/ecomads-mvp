import { useEffect } from 'react';
import { useLocation } from 'react-router-dom';
import { BrandLogo } from '../../shared/ui/BrandLogo';
import { safeReturnTo, withReturnTo } from '../../shared/auth/returnTo';
import { PilotForm, DemoDecision, LandingFaq, LandingFooter } from './LandingInteractions';
import '../../shared/assets/fonts/golos-text.css';
import './landing.css';

export function LandingPage() {
  const location = useLocation();
  const returnTo = safeReturnTo(new URLSearchParams(location.search).get('returnTo'));
  const loginUrl = withReturnTo('/login', returnTo);
  useEffect(() => { document.title = 'EcomAds — ИИ-маркетолог для рекламы Wildberries'; }, []);
  return (
<div id="top" className="landing-page tlight" style={{"background": "var(--bg)", "minHeight": "100vh", "display": "flex", "flexDirection": "column", "alignItems": "center"}}>

  <header style={{"width": "100%", "maxWidth": "1200px", "boxSizing": "border-box", "padding": "20px 24px", "display": "flex", "alignItems": "center", "justifyContent": "space-between", "gap": "16px", "flexWrap": "wrap"}}>
    <a href="#top" aria-label="ecomads — на главную" style={{"display": "flex", "alignItems": "center", "gap": "9px", "color": "var(--ink)"}}><BrandLogo /></a>
    <nav className="hidenav" style={{"display": "flex", "alignItems": "center", "gap": "24px"}}>
      <a className="navl" href="#how">Как работает</a>
      <a className="navl" href="#who">Для кого</a>
      <a className="navl" href="#safety">Безопасность</a>
      <a className="navl" href="#faq">Вопросы</a>
    </nav>
    <div style={{"display": "flex", "alignItems": "center", "gap": "10px"}}>
      <a className="btns" href={loginUrl} style={{"height": "40px", "fontSize": "15px", "padding": "0 16px"}}>Войти</a>
      <a className="btnp" href="#pilot" style={{"height": "40px", "fontSize": "15px", "padding": "0 16px"}}>Попасть в пилот</a>
    </div>
  </header>

  <section style={{"width": "100%", "maxWidth": "1200px", "boxSizing": "border-box", "padding": "56px 24px 88px", "display": "flex", "flexWrap": "wrap", "alignItems": "center", "gap": "56px"}}>
    <div style={{"flex": "1 1 460px", "minWidth": "0", "display": "flex", "flexDirection": "column", "gap": "24px"}}>
      <span style={{"alignSelf": "flex-start", "display": "inline-flex", "alignItems": "center", "gap": "8px", "fontSize": "14px", "fontWeight": "600", "padding": "6px 12px", "borderRadius": "999px", "background": "var(--good-bg)", "border": "1px solid var(--good-bd)", "color": "var(--good)"}}>
        <span style={{"width": "8px", "height": "8px", "borderRadius": "50%", "background": "var(--good)"}}></span>
        Идёт набор в бесплатный пилот
      </span>
      <h1 className="h1" style={{"fontSize": "54px", "lineHeight": "1.06", "fontWeight": "800", "letterSpacing": "-0.03em"}}>ИИ-маркетолог для внутренней рекламы Wildberries</h1>
      <p style={{"fontSize": "20px", "lineHeight": "1.5", "color": "var(--ink2)", "maxWidth": "560px"}}>Каждое утро показывает, где реклама тратит бюджет впустую, что можно масштабировать и с чего начать. Не ещё один отчёт, а список конкретных действий.</p>
      <div style={{"display": "flex", "flexWrap": "wrap", "gap": "12px"}}>
        <a className="btnp" href="#pilot">Попасть в пилот</a>
        <a className="btns" href="#how">Как это работает</a>
      </div>
      <div style={{"display": "flex", "flexWrap": "wrap", "gap": "8px 20px", "fontSize": "14px", "color": "var(--mut)"}}>
        <span>Подключение по API-токену WB</span>
        <span>С подпиской «Джем» и без</span>
        <span>Сводка в Telegram</span>
      </div>
    </div>

    <div style={{"flex": "1 1 420px", "minWidth": "0", "display": "flex", "flexDirection": "column", "gap": "14px"}}>
      <div style={{"background": "var(--card)", "border": "1px solid var(--line)", "borderRadius": "24px", "padding": "20px", "display": "flex", "flexDirection": "column", "gap": "14px", "boxShadow": "var(--shadow)"}}>
        <div style={{"display": "flex", "justifyContent": "space-between", "alignItems": "center", "gap": "12px"}}>
          <div style={{"display": "flex", "alignItems": "center", "gap": "10px"}}>
            <span style={{"width": "36px", "height": "36px", "borderRadius": "50%", "overflow": "hidden", "flexShrink": "0"}}><svg width="36" height="36" viewBox="0 0 48 48" aria-hidden="true" style={{"display": "block"}}><rect width="48" height="48" fill="#5A22C8"></rect><rect x="11" y="25" width="6.5" height="12" rx="3.25" fill="#FFFFFF"></rect><rect x="20.75" y="18" width="6.5" height="19" rx="3.25" fill="#FFFFFF"></rect><rect x="30.5" y="27" width="6.5" height="10" rx="3.25" fill="#FFFFFF" fillOpacity="0.45"></rect><circle cx="33.75" cy="15" r="4.75" fill="#FFFFFF"></circle></svg></span>
            <div style={{"display": "flex", "flexDirection": "column"}}>
              <span style={{"fontSize": "15px", "fontWeight": "700"}}>ecomads · утренняя сводка</span>
              <span style={{"fontSize": "13px", "color": "var(--mut)"}}>Telegram · пример сводки</span>
            </div>
          </div>
          <span className="chip" style={{"background": "var(--soft)", "color": "var(--mut)"}}>пример</span>
        </div>
        <div className="num" style={{"display": "grid", "gridTemplateColumns": "repeat(3, minmax(0, 1fr))", "gap": "8px"}}>
          <div style={{"background": "var(--tg)", "borderRadius": "12px", "padding": "10px 12px", "display": "flex", "flexDirection": "column", "gap": "2px"}}><span style={{"fontSize": "12px", "color": "var(--mut)"}}>Расход вчера</span><span style={{"fontSize": "18px", "fontWeight": "700"}}>14 820 ₽</span></div>
          <div style={{"background": "var(--tg)", "borderRadius": "12px", "padding": "10px 12px", "display": "flex", "flexDirection": "column", "gap": "2px"}}><span style={{"fontSize": "12px", "color": "var(--mut)"}}>Заказы</span><span style={{"fontSize": "18px", "fontWeight": "700"}}>63</span></div>
          <div style={{"background": "var(--tg)", "borderRadius": "12px", "padding": "10px 12px", "display": "flex", "flexDirection": "column", "gap": "2px"}}><span style={{"fontSize": "12px", "color": "var(--mut)"}}>ДРР</span><span style={{"fontSize": "18px", "fontWeight": "700"}}>11,4%</span></div>
        </div>
        <div style={{"fontSize": "13px", "fontWeight": "600", "color": "var(--mut)", "textTransform": "uppercase", "letterSpacing": ".04em"}}>Требуют внимания · 3</div>

        <article style={{"background": "var(--pop)", "border": "1px solid var(--line2)", "borderRadius": "16px", "padding": "14px", "display": "flex", "flexDirection": "column", "gap": "8px"}}>
          <div style={{"display": "flex", "alignItems": "center", "gap": "8px", "flexWrap": "wrap"}}><span className="chip" style={{"background": "var(--bad-bg)", "color": "var(--bad)"}}>Расход без заказов</span><span style={{"fontSize": "15px", "fontWeight": "700"}}>Платья летние</span></div>
          <div className="num" style={{"fontSize": "14px", "color": "var(--ink2)"}}>Запрос «платье на выпускной»: <b style={{"color": "var(--bad)"}}>2 340 ₽</b>, 0 заказов при 61 клике</div>
          <div style={{"fontSize": "14px"}}><b>Что сделать:</b> исключить фразу из кластера</div>
          <DemoDecision />
        </article>

        <article style={{"background": "var(--pop)", "border": "1px solid var(--line2)", "borderRadius": "16px", "padding": "14px", "display": "flex", "flexDirection": "column", "gap": "6px"}}>
          <div style={{"display": "flex", "alignItems": "center", "gap": "8px", "flexWrap": "wrap"}}><span className="chip" style={{"background": "var(--warn-bg)", "color": "var(--warn)"}}>ДРР выше нормы</span><span style={{"fontSize": "15px", "fontWeight": "700"}}>Сарафаны</span></div>
          <div className="num" style={{"fontSize": "14px", "color": "var(--ink2)"}}>ДРР <b style={{"color": "var(--warn)"}}>31%</b> при вашей норме 18% → снизить ставку</div>
        </article>

        <article style={{"background": "var(--pop)", "border": "1px solid var(--line2)", "borderRadius": "16px", "padding": "14px", "display": "flex", "flexDirection": "column", "gap": "6px"}}>
          <div style={{"display": "flex", "alignItems": "center", "gap": "8px", "flexWrap": "wrap"}}><span className="chip" style={{"background": "var(--good-bg)", "color": "var(--good)"}}>Можно масштабировать</span><span style={{"fontSize": "15px", "fontWeight": "700"}}>Рубашки льняные</span></div>
          <div className="num" style={{"fontSize": "14px", "color": "var(--ink2)"}}>ДРР <b style={{"color": "var(--good)"}}>9%</b>, 14 заказов → поднимать ставку постепенно</div>
        </article>
      </div>
    </div>
  </section>

  <section style={{"width": "100%", "maxWidth": "1200px", "boxSizing": "border-box", "padding": "0 24px 96px", "display": "flex", "flexDirection": "column", "gap": "32px"}}>
    <div style={{"display": "flex", "flexDirection": "column", "gap": "12px", "maxWidth": "760px"}}>
      <span style={{"fontSize": "14px", "fontWeight": "600", "color": "var(--acc)", "textTransform": "uppercase", "letterSpacing": ".06em"}}>Проблема</span>
      <h2 className="h2" style={{"fontSize": "38px", "lineHeight": "1.15", "fontWeight": "800", "letterSpacing": "-0.02em"}}>Данные в кабинете WB есть. Времени разбираться в них каждый день — нет</h2>
    </div>
    <div style={{"display": "grid", "gridTemplateColumns": "repeat(auto-fit, minmax(260px, 1fr))", "gap": "16px"}}>
      <article style={{"background": "var(--card)", "border": "1px solid var(--line)", "borderRadius": "20px", "padding": "24px", "display": "flex", "flexDirection": "column", "gap": "12px"}}>
        <svg width="28" height="28" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true" style={{"color": "var(--acc)"}}><rect x="3" y="4" width="18" height="16" rx="2"></rect><path d="M3 10h18M9 4v16"></path></svg>
        <h3 style={{"fontSize": "19px", "fontWeight": "700"}}>Десятки кампаний и сотни запросов</h3>
        <p style={{"fontSize": "15px", "lineHeight": "1.5", "color": "var(--ink2)"}}>Чтобы найти, где утекает бюджет, приходится вручную пролистывать таблицы по каждой кампании и каждому кластеру.</p>
      </article>
      <article style={{"background": "var(--card)", "border": "1px solid var(--line)", "borderRadius": "20px", "padding": "24px", "display": "flex", "flexDirection": "column", "gap": "12px"}}>
        <svg width="28" height="28" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true" style={{"color": "var(--acc)"}}><circle cx="12" cy="12" r="9"></circle><path d="M12 7v5l3 2"></path></svg>
        <h3 style={{"fontSize": "19px", "fontWeight": "700"}}>Проблему замечают поздно</h3>
        <p style={{"fontSize": "15px", "lineHeight": "1.5", "color": "var(--ink2)"}}>Запрос, который неделю тратит деньги без заказов, становится заметен только в отчёте за месяц.</p>
      </article>
      <article style={{"background": "var(--card)", "border": "1px solid var(--line)", "borderRadius": "20px", "padding": "24px", "display": "flex", "flexDirection": "column", "gap": "12px"}}>
        <svg width="28" height="28" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true" style={{"color": "var(--acc)"}}><path d="M4 6h16M4 12h10M4 18h6"></path></svg>
        <h3 style={{"fontSize": "19px", "fontWeight": "700"}}>Непонятно, что трогать первым</h3>
        <p style={{"fontSize": "15px", "lineHeight": "1.5", "color": "var(--ink2)"}}>Цифры видны, но решения — снизить ставку, исключить фразу или масштабировать — каждый раз приходится принимать с нуля.</p>
      </article>
    </div>
  </section>

  <section id="how" style={{"width": "100%", "maxWidth": "1200px", "boxSizing": "border-box", "padding": "0 24px 96px", "display": "flex", "flexDirection": "column", "gap": "32px"}}>
    <div style={{"display": "flex", "flexDirection": "column", "gap": "12px", "maxWidth": "760px"}}>
      <span style={{"fontSize": "14px", "fontWeight": "600", "color": "var(--acc)", "textTransform": "uppercase", "letterSpacing": ".06em"}}>Как работает</span>
      <h2 className="h2" style={{"fontSize": "38px", "lineHeight": "1.15", "fontWeight": "800", "letterSpacing": "-0.02em"}}>Данные → проблема → рекомендация → действие</h2>
    </div>
    <ol style={{"listStyle": "none", "margin": "0", "padding": "0", "display": "grid", "gridTemplateColumns": "repeat(auto-fit, minmax(240px, 1fr))", "gap": "16px"}}>
      <li style={{"background": "var(--card)", "border": "1px solid var(--line)", "borderRadius": "20px", "padding": "24px", "display": "flex", "flexDirection": "column", "gap": "12px"}}>
        <span style={{"width": "40px", "height": "40px", "borderRadius": "12px", "background": "var(--acc-soft)", "color": "var(--acc2)", "display": "flex", "alignItems": "center", "justifyContent": "center", "fontSize": "18px", "fontWeight": "800"}}>1</span>
        <h3 style={{"fontSize": "18px", "fontWeight": "700"}}>Подключаете кабинет</h3>
        <p style={{"fontSize": "15px", "lineHeight": "1.5", "color": "var(--ink2)"}}>Создаёте базовый токен в ЛК WB и вставляете его в ecomads. Кампании и товары подтянутся сами, без выгрузок файлов.</p>
      </li>
      <li style={{"background": "var(--card)", "border": "1px solid var(--line)", "borderRadius": "20px", "padding": "24px", "display": "flex", "flexDirection": "column", "gap": "12px"}}>
        <span style={{"width": "40px", "height": "40px", "borderRadius": "12px", "background": "var(--acc-soft)", "color": "var(--acc2)", "display": "flex", "alignItems": "center", "justifyContent": "center", "fontSize": "18px", "fontWeight": "800"}}>2</span>
        <h3 style={{"fontSize": "18px", "fontWeight": "700"}}>Задаёте нормы</h3>
        <p style={{"fontSize": "15px", "lineHeight": "1.5", "color": "var(--ink2)"}}>Допустимый ДРР и другие показатели по кабинету или кампании. Начните с настроек по умолчанию и уточните нормы под свой бизнес.</p>
      </li>
      <li style={{"background": "var(--card)", "border": "1px solid var(--line)", "borderRadius": "20px", "padding": "24px", "display": "flex", "flexDirection": "column", "gap": "12px"}}>
        <span style={{"width": "40px", "height": "40px", "borderRadius": "12px", "background": "var(--acc-soft)", "color": "var(--acc2)", "display": "flex", "alignItems": "center", "justifyContent": "center", "fontSize": "18px", "fontWeight": "800"}}>3</span>
        <h3 style={{"fontSize": "18px", "fontWeight": "700"}}>Утром получаете сводку</h3>
        <p style={{"fontSize": "15px", "lineHeight": "1.5", "color": "var(--ink2)"}}>В Telegram и в веб-кабинете: что вышло за нормы, где расход без заказов, что можно масштабировать.</p>
      </li>
      <li style={{"background": "var(--card)", "border": "1px solid var(--line)", "borderRadius": "20px", "padding": "24px", "display": "flex", "flexDirection": "column", "gap": "12px"}}>
        <span style={{"width": "40px", "height": "40px", "borderRadius": "12px", "background": "var(--acc-soft)", "color": "var(--acc2)", "display": "flex", "alignItems": "center", "justifyContent": "center", "fontSize": "18px", "fontWeight": "800"}}>4</span>
        <h3 style={{"fontSize": "18px", "fontWeight": "700"}}>Отрабатываете список</h3>
        <p style={{"fontSize": "15px", "lineHeight": "1.5", "color": "var(--ink2)"}}>Каждая рекомендация — задача: «принял», «отложить» или «не актуально». Изменения в WB вносите вы, решение всегда за вами.</p>
      </li>
    </ol>
  </section>

  <section style={{"width": "100%", "maxWidth": "1200px", "boxSizing": "border-box", "padding": "0 24px 96px", "display": "flex", "flexDirection": "column", "gap": "32px"}}>
    <div style={{"display": "flex", "flexDirection": "column", "gap": "12px", "maxWidth": "760px"}}>
      <span style={{"fontSize": "14px", "fontWeight": "600", "color": "var(--acc)", "textTransform": "uppercase", "letterSpacing": ".06em"}}>Что внутри</span>
      <h2 className="h2" style={{"fontSize": "38px", "lineHeight": "1.15", "fontWeight": "800", "letterSpacing": "-0.02em"}}>Короткий рабочий экран вместо большого отчёта</h2>
    </div>
    <div style={{"display": "grid", "gridTemplateColumns": "repeat(auto-fit, minmax(330px, 1fr))", "gap": "16px"}}>
      <article style={{"background": "var(--card)", "border": "1px solid var(--line)", "borderRadius": "20px", "padding": "24px", "display": "flex", "flexDirection": "column", "gap": "10px"}}>
        <h3 style={{"fontSize": "19px", "fontWeight": "700"}}>Сводка с нормами</h3>
        <p style={{"fontSize": "15px", "lineHeight": "1.5", "color": "var(--ink2)"}}>Расход, заказы, CTR и два ДРР — рекламный и общий — со сравнением с прошлым периодом. Вверху только то, что требует внимания.</p>
      </article>
      <article style={{"background": "var(--card)", "border": "1px solid var(--line)", "borderRadius": "20px", "padding": "24px", "display": "flex", "flexDirection": "column", "gap": "10px"}}>
        <h3 style={{"fontSize": "19px", "fontWeight": "700"}}>Кампании и кластеры запросов</h3>
        <p style={{"fontSize": "15px", "lineHeight": "1.5", "color": "var(--ink2)"}}>От общей картины — в конкретную кампанию и до уровня поисковых кластеров, где обычно и прячется неэффективный расход.</p>
      </article>
      <article style={{"background": "var(--card)", "border": "1px solid var(--line)", "borderRadius": "20px", "padding": "24px", "display": "flex", "flexDirection": "column", "gap": "10px"}}>
        <h3 style={{"fontSize": "19px", "fontWeight": "700"}}>Рекомендации с объяснением</h3>
        <p style={{"fontSize": "15px", "lineHeight": "1.5", "color": "var(--ink2)"}}>Каждая карточка: проблема, что сделать и почему. Рекомендации формируются по правилам и вашим нормам, с объяснением причин.</p>
      </article>
      <article style={{"background": "var(--card)", "border": "1px solid var(--line)", "borderRadius": "20px", "padding": "24px", "display": "flex", "flexDirection": "column", "gap": "10px"}}>
        <div style={{"display": "flex", "alignItems": "center", "gap": "8px"}}><h3 style={{"fontSize": "19px", "fontWeight": "700"}}>Работает с «Джемом» и без</h3><span className="chip" style={{"background": "var(--jam-bg)", "color": "var(--jam)"}}>Джем</span></div>
        <p style={{"fontSize": "15px", "lineHeight": "1.5", "color": "var(--ink2)"}}>Есть подписка — используем дополнительную аналитику по запросам. Нет — всё основное работает на стандартных данных рекламного кабинета.</p>
      </article>
    </div>
  </section>

  <section style={{"width": "100%", "maxWidth": "1200px", "boxSizing": "border-box", "padding": "0 24px 96px", "display": "flex", "flexDirection": "column", "gap": "32px"}}>
    <div style={{"display": "flex", "flexDirection": "column", "gap": "12px", "maxWidth": "760px"}}>
      <span style={{"fontSize": "14px", "fontWeight": "600", "color": "var(--acc)", "textTransform": "uppercase", "letterSpacing": ".06em"}}>Чем отличается</span>
      <h2 className="h2" style={{"fontSize": "38px", "lineHeight": "1.15", "fontWeight": "800", "letterSpacing": "-0.02em"}}>Не ещё один аналитический кабинет</h2>
    </div>
    <div style={{"display": "flex", "flexWrap": "wrap", "gap": "16px"}}>
      <div style={{"flex": "1 1 360px", "minWidth": "0", "border": "1px solid var(--ctl-bd)", "borderRadius": "20px", "padding": "24px", "display": "flex", "flexDirection": "column", "gap": "14px"}}>
        <h3 style={{"fontSize": "18px", "fontWeight": "700", "color": "var(--mut)"}}>Обычный сервис аналитики</h3>
        <ul style={{"margin": "0", "padding": "0 0 0 20px", "display": "flex", "flexDirection": "column", "gap": "10px", "fontSize": "16px", "lineHeight": "1.45", "color": "var(--ink2)"}}>
          <li>Показывает все цифры и графики</li>
          <li>Проблему нужно найти самому</li>
          <li>Вывод и приоритет — на вас</li>
          <li>Нужно зайти и посмотреть</li>
        </ul>
      </div>
      <div style={{"flex": "1 1 360px", "minWidth": "0", "background": "var(--card)", "border": "1px solid var(--acc)", "borderRadius": "20px", "padding": "24px", "display": "flex", "flexDirection": "column", "gap": "14px"}}>
        <h3 style={{"fontSize": "18px", "fontWeight": "700", "color": "var(--acc2)"}}>ecomads</h3>
        <ul style={{"margin": "0", "padding": "0 0 0 20px", "display": "flex", "flexDirection": "column", "gap": "10px", "fontSize": "16px", "lineHeight": "1.45", "color": "var(--ink)"}}>
          <li>Показывает только отклонения от ваших норм</li>
          <li>Находит проблему и объясняет причину</li>
          <li>Предлагает действие и расставляет приоритет</li>
          <li>Сам присылает сводку утром в Telegram</li>
        </ul>
      </div>
    </div>
  </section>

  <section id="who" style={{"width": "100%", "maxWidth": "1200px", "boxSizing": "border-box", "padding": "0 24px 96px", "display": "flex", "flexDirection": "column", "gap": "32px"}}>
    <div style={{"display": "flex", "flexDirection": "column", "gap": "12px", "maxWidth": "760px"}}>
      <span style={{"fontSize": "14px", "fontWeight": "600", "color": "var(--acc)", "textTransform": "uppercase", "letterSpacing": ".06em"}}>Для кого</span>
      <h2 className="h2" style={{"fontSize": "38px", "lineHeight": "1.15", "fontWeight": "800", "letterSpacing": "-0.02em"}}>Для тех, кто сам ведёт рекламу на WB</h2>
    </div>
    <div style={{"display": "grid", "gridTemplateColumns": "repeat(auto-fit, minmax(280px, 1fr))", "gap": "16px"}}>
      <article style={{"background": "var(--card)", "border": "1px solid var(--line)", "borderRadius": "20px", "padding": "24px", "display": "flex", "flexDirection": "column", "gap": "10px"}}>
        <h3 style={{"fontSize": "19px", "fontWeight": "700"}}>Селлер, который делает всё сам</h3>
        <p style={{"fontSize": "15px", "lineHeight": "1.5", "color": "var(--ink2)"}}>Товары, поставки, реклама — всё на одном человеке. Нужно за пять минут утром понять, какая кампания просела и что делать.</p>
      </article>
      <article style={{"background": "var(--card)", "border": "1px solid var(--line)", "borderRadius": "20px", "padding": "24px", "display": "flex", "flexDirection": "column", "gap": "10px"}}>
        <h3 style={{"fontSize": "19px", "fontWeight": "700"}}>Селлер с большим ассортиментом</h3>
        <p style={{"fontSize": "15px", "lineHeight": "1.5", "color": "var(--ink2)"}}>Много SKU, много кампаний, активно тестируете и масштабируете. Нужно быстро находить слабые места, а не искать их вручную.</p>
      </article>
      <article style={{"background": "var(--card)", "border": "1px solid var(--line)", "borderRadius": "20px", "padding": "24px", "display": "flex", "flexDirection": "column", "gap": "10px"}}>
        <h3 style={{"fontSize": "19px", "fontWeight": "700"}}>Маркетолог или агентство</h3>
        <p style={{"fontSize": "15px", "lineHeight": "1.5", "color": "var(--ink2)"}}>Ведёте рекламу нескольких клиентов. Нужно пробежаться по кабинетам и сразу увидеть, где проблема.</p>
      </article>
    </div>
  </section>

  <section id="safety" style={{"width": "100%", "maxWidth": "1200px", "boxSizing": "border-box", "padding": "0 24px 96px"}}>
    <div style={{"background": "var(--card)", "border": "1px solid var(--line)", "borderRadius": "28px", "padding": "40px 32px", "display": "flex", "flexWrap": "wrap", "gap": "32px 48px"}}>
      <div style={{"flex": "1 1 320px", "minWidth": "0", "display": "flex", "flexDirection": "column", "gap": "14px"}}>
        <svg width="36" height="36" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true" style={{"color": "var(--good)"}}><path d="M12 3l8 3v6c0 4.5-3.4 8.2-8 9-4.6-.8-8-4.5-8-9V6z"></path><path d="M9 12l2 2 4-4"></path></svg>
        <h2 className="h2" style={{"fontSize": "32px", "lineHeight": "1.2", "fontWeight": "800", "letterSpacing": "-0.02em"}}>Токен только на чтение</h2>
        <p style={{"fontSize": "16px", "lineHeight": "1.5", "color": "var(--ink2)"}}>ecomads читает статистику, но не управляет вашим кабинетом.</p>
      </div>
      <ul style={{"flex": "2 1 480px", "minWidth": "0", "listStyle": "none", "margin": "0", "padding": "0", "display": "grid", "gridTemplateColumns": "repeat(auto-fit, minmax(220px, 1fr))", "gap": "20px 24px"}}>
        <li style={{"display": "flex", "flexDirection": "column", "gap": "6px"}}><b style={{"fontSize": "16px"}}>Базовый токен WB</b><span style={{"fontSize": "15px", "lineHeight": "1.5", "color": "var(--ink2)"}}>Доступ только к категориям «Продвижение», «Аналитика» и «Статистика».</span></li>
        <li style={{"display": "flex", "flexDirection": "column", "gap": "6px"}}><b style={{"fontSize": "16px"}}>Не меняем ставки и кампании</b><span style={{"fontSize": "15px", "lineHeight": "1.5", "color": "var(--ink2)"}}>Все изменения в WB вносите вы сами по рекомендациям.</span></li>
        <li style={{"display": "flex", "flexDirection": "column", "gap": "6px"}}><b style={{"fontSize": "16px"}}>Отзыв в один клик</b><span style={{"fontSize": "15px", "lineHeight": "1.5", "color": "var(--ink2)"}}>Токен можно удалить в ЛК WB в любой момент — доступ сразу пропадёт.</span></li>
        <li style={{"display": "flex", "flexDirection": "column", "gap": "6px"}}><b style={{"fontSize": "16px"}}>Хранение данных</b><span style={{"fontSize": "15px", "lineHeight": "1.5", "color": "var(--ink2)"}}>Токен WB хранится в зашифрованном виде. Статистика — в базе данных сервиса; база не доступна из интернета.</span></li>
      </ul>
    </div>
  </section>

  <section id="pilot" style={{"width": "100%", "maxWidth": "1200px", "boxSizing": "border-box", "padding": "0 24px 96px", "display": "flex", "flexWrap": "wrap", "gap": "32px"}}>
    <div style={{"flex": "1 1 420px", "minWidth": "0", "display": "flex", "flexDirection": "column", "gap": "20px"}}>
      <span style={{"fontSize": "14px", "fontWeight": "600", "color": "var(--acc)", "textTransform": "uppercase", "letterSpacing": ".06em"}}>Пилот</span>
      <h2 className="h2" style={{"fontSize": "38px", "lineHeight": "1.15", "fontWeight": "800", "letterSpacing": "-0.02em"}}>Ищем первых пользователей</h2>
      <p style={{"fontSize": "17px", "lineHeight": "1.5", "color": "var(--ink2)"}}>Продукт на стадии пилота. Приглашаем участников бесплатно в обмен на честную обратную связь. Оставьте заявку — команда обсудит участие и поможет с подключением.</p>
      <div style={{"display": "flex", "flexWrap": "wrap", "gap": "16px"}}>
        <div style={{"flex": "1 1 220px", "minWidth": "0", "display": "flex", "flexDirection": "column", "gap": "10px"}}>
          <b style={{"fontSize": "16px"}}>Вы получаете</b>
          <ul style={{"margin": "0", "padding": "0 0 0 20px", "display": "flex", "flexDirection": "column", "gap": "8px", "fontSize": "15px", "lineHeight": "1.45", "color": "var(--ink2)"}}>
            <li>Бесплатный доступ на время пилота</li>
            <li>Ежедневную сводку и рекомендации по вашему кабинету</li>
            <li>Прямую связь с командой</li>
          </ul>
        </div>
        <div style={{"flex": "1 1 220px", "minWidth": "0", "display": "flex", "flexDirection": "column", "gap": "10px"}}>
          <b style={{"fontSize": "16px"}}>Мы просим</b>
          <ul style={{"margin": "0", "padding": "0 0 0 20px", "display": "flex", "flexDirection": "column", "gap": "8px", "fontSize": "15px", "lineHeight": "1.45", "color": "var(--ink2)"}}>
            <li>Подключить кабинет WB</li>
            <li>Отмечать решения по рекомендациям</li>
            <li>Будем просить короткую обратную связь о том, что работает, а что нет</li>
          </ul>
        </div>
      </div>
    </div>

    <div style={{"flex": "1 1 420px", "minWidth": "0", "background": "var(--card)", "border": "1px solid var(--line)", "borderRadius": "24px", "padding": "28px", "display": "flex", "flexDirection": "column", "gap": "16px", "boxShadow": "var(--shadow)"}}>
      <PilotForm />
    </div>
  </section>

  <section id="faq" style={{"width": "100%", "maxWidth": "860px", "boxSizing": "border-box", "padding": "0 24px 96px", "display": "flex", "flexDirection": "column", "gap": "24px"}}>
    <h2 className="h2" style={{"fontSize": "38px", "lineHeight": "1.15", "fontWeight": "800", "letterSpacing": "-0.02em", "textAlign": "center"}}>Частые вопросы</h2>
    <div style={{"display": "flex", "flexDirection": "column", "gap": "10px"}}>
      <LandingFaq />
    </div>
  </section>

  <section style={{"width": "100%", "maxWidth": "1200px", "boxSizing": "border-box", "padding": "0 24px 72px"}}>
    <div style={{"background": "var(--pri)", "color": "#FFFFFF", "borderRadius": "28px", "padding": "44px 32px", "display": "flex", "flexWrap": "wrap", "alignItems": "center", "justifyContent": "space-between", "gap": "24px"}}>
      <div style={{"display": "flex", "flexDirection": "column", "gap": "8px", "maxWidth": "620px"}}>
        <h2 className="h2" style={{"fontSize": "32px", "lineHeight": "1.2", "fontWeight": "800", "letterSpacing": "-0.02em", "color": "#FFFFFF"}}>Перестаньте искать проблемы в таблицах</h2>
        <p style={{"fontSize": "17px", "lineHeight": "1.5", "color": "#FFFFFF"}}>Подключите кабинет и получайте готовый список действий каждое утро.</p>
      </div>
      <a href="#pilot" style={{"display": "inline-flex", "alignItems": "center", "height": "48px", "padding": "0 24px", "borderRadius": "999px", "background": "#FFFFFF", "color": "#4A1BA6", "fontSize": "16px", "fontWeight": "700"}}>Попасть в пилот</a>
    </div>
  </section>

  <LandingFooter />
</div>

  );
}

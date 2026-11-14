import type { RecommendationCard } from '../api/clusterRecommendationApi';
import { formatMoney } from './formatMoney';

type Evidence = Pick<RecommendationCard, 'subject' | 'rule' | 'facts' | 'norm' | 'economy' | 'presentation'>;
type Window = { startDate: string; endDate: string };

const number = (value: number) => value.toLocaleString('ru-RU', { maximumFractionDigits: 1 });
const rubles = (value: number) => `${value.toLocaleString('ru-RU', { maximumFractionDigits: 2 })} ₽`;
const percent = (ratio: number) => `${(ratio * 100).toLocaleString('ru-RU', { maximumFractionDigits: 2 })}%`;
const plural = new Intl.PluralRules('ru-RU', { maximumFractionDigits: 1 });
const quantity = (amount: number, forms: [string, string, string]) => {
  const form = plural.select(amount);
  return `${number(amount)} ${form === 'one' ? forms[0] : form === 'many' ? forms[2] : forms[1]}`;
};
const date = (value: string) => value.split('-').reverse().join('.');
export const recommendationDates = (window: Window) => `${date(window.startDate)} — ${date(window.endDate)}`;

export function recommendationPeriod(card: Evidence, window: Window) {
  const details = card.presentation?.details;
  const dates = recommendationDates(details?.window ?? window);
  return details?.kind === 'budget' ? `Средний расход: ${dates}`
    : details?.kind === 'stock' ? card.rule.reasonCode === 'out_of_stock' ? `Расход: ${dates}` : `Темп заказов: ${recommendationDates(window)}`
    : `Данные за ${dates}`;
}

export function recommendationTitle(card: Evidence) {
  switch (card.rule.ruleId) {
    case 'R01': return card.rule.reasonCode === 'out_of_stock' ? 'Товар закончился' : 'Товар заканчивается';
    case 'R02': return 'Заканчивается бюджет';
    case 'R03': return 'Расход без заказов';
    case 'R06': return 'ДРР кластера выше нормы';
    case 'R07': return 'ДРР товара выше нормы';
    default: return 'Требует внимания';
  }
}

const restricted = (card: Evidence, code: string) => card.presentation?.restrictions.some(r => r.code === code);
const belowExclusionThreshold = (card: Evidence) => card.presentation?.primary.kind === 'watch' &&
  restricted(card, 'unified_below_exclusion') && !restricted(card, 'settings_unsupported') && !restricted(card, 'excluded');
const hasShortfall = (card: Evidence) => {
  const details = card.presentation?.details;
  return details?.kind === 'budget' && details.balance != null && details.topUp != null && details.balance < details.topUp;
};

export function recommendationReason(card: Evidence, includeActionReason = true) {
  const { rule, facts, economy, norm, presentation: p } = card;
  const details = p?.details;
  if (rule.ruleId === 'R01') {
    if (rule.reasonCode === 'out_of_stock') return `Товара нет в наличии. За последние 3 дня на его рекламу потрачено ${formatMoney(facts.spend)}.`;
    if (details?.stockDays != null && details.stockDays < 1) return 'При текущем темпе заказов запаса меньше чем на день.';
    return details?.stockDays != null ? `При текущем темпе заказов запаса примерно на ${number(details.stockDays)} дн.` : 'Проверьте остаток товара.';
  }
  if (rule.ruleId === 'R02') {
    const balance = details?.budget != null ? `В бюджете осталось ${formatMoney(details.budget)}.` : 'Бюджета хватит меньше чем на день.';
    const pace = details?.dailySpend != null ? ` При среднем расходе ${formatMoney(details.dailySpend)} в день этого не хватит на день.` : '';
    return balance + (hasShortfall(card) ? ' На рекламном счёте не хватает денег для пополнения.' : details?.budget != null ? pace : '');
  }
  if (rule.ruleId === 'R03') {
    const outcome = facts.orderedProducts === 0 ? ' Не заказали ни одной единицы товара.' : '';
    const threshold = p?.clickEvidence?.minimumClicks;
    const clicks = restricted(card, 'insufficient_clicks') && p?.primary.kind === 'watch' && threshold != null && facts.clicks != null
      ? ` Для исключения нужно ${number(threshold)} кликов, пока их ${number(facts.clicks)}.` : '';
    return `Потрачено ${formatMoney(facts.spend)}.${outcome}${clicks}`;
  }
  if (rule.ruleId === 'R06' || rule.ruleId === 'R07') {
    if (economy.drr == null) return 'Не удалось рассчитать ДРР.';
    const drr = percent(economy.drr);
    if (includeActionReason && belowExclusionThreshold(card))
      return `Оценочный ДРР — ${drr}. Порог для исключения при единой ставке — ${percent(norm.value * 2)}.`;
    const key = p?.isKey && p.keyShare != null && p.primary.kind === 'check_product'
      ? ` На кластер приходится ${percent(p.keyShare)} заказанных единиц товара в кампании.` : '';
    return `${rule.ruleId === 'R06' ? 'Оценочный ДРР' : 'ДРР рекламы товара'} — ${drr} при норме ${percent(norm.value)}.${key}`;
  }
  return `Расход: ${formatMoney(facts.spend)}.`;
}

export function recommendationAction(card: Evidence, action = card.presentation?.primary) {
  if (!action) return null;
  if (action === card.presentation?.primary && restricted(card, 'exclude_first'))
    return 'Сначала откройте рекомендации по исключению кластеров этого товара.';
  switch (action.kind) {
    case 'exclude':
      return card.subject.clusterName ? `Добавьте кластер «${card.subject.clusterName}» в минус-фразы WB.` : 'Добавьте кластер в минус-фразы WB.';
    case 'watch':
      if (card.rule.ruleId === 'R01' && action.label === 'Учтите остаток при завершении распродажи') return `${action.label}.`;
      if (card.subject.scope !== 'cluster') return card.rule.ruleId === 'R01' ? 'Пока следите за остатком товара.' : 'Пока не меняйте ставку товара.';
      if (restricted(card, 'excluded')) return 'Пока не возвращайте кластер в рекламу.';
      if (restricted(card, 'settings_unsupported')) return 'Пока наблюдайте за кластером.';
      return restricted(card, 'insufficient_clicks') || restricted(card, 'unified_below_exclusion')
        ? 'Пока оставьте кластер в рекламе и наблюдайте.' : 'Пока наблюдайте за кластером.';
    case 'bid_at_minimum':
    case 'cluster_minimum_unknown':
      return card.subject.scope === 'cluster' ? 'Пока наблюдайте за кластером.' : 'Пока не меняйте ставку товара.';
    case 'reduce_bid':
    case 'reduce_article_bid': {
      const scope = action.kind === 'reduce_bid' ? 'по кластеру' : 'товара';
      // Earlier snapshots store the original product bid only in the action label.
      const original = action.label.match(/:\s*([\d.,\s]+)\s*→/u)?.[1].trim();
      const placement = action.placement === 'search' || (!action.placement && action.label.startsWith('Снизьте ставку в поиске:')) ? ' в поиске'
        : action.placement === 'recommendation' || (!action.placement && action.label.startsWith('Снизьте ставку в рекомендациях:')) ? ' в рекомендациях' : '';
      const from = action.kind === 'reduce_bid' && card.presentation?.control?.currentBid != null
        ? rubles(card.presentation.control.currentBid).replace(/\s*₽$/u, '') : original;
      if (action.targetBid == null) return action.label;
      return `Снизьте ставку ${scope}${placement} ${from ? `с ${from} до` : 'до'} ${rubles(action.targetBid)}.`;
    }
    case 'top_up_budget': {
      const topUp = card.presentation?.details?.topUp;
      if (topUp == null) return action.label;
      const amount = rubles(topUp);
      return hasShortfall(card) ? `Сначала пополните рекламный счёт WB, затем бюджет кампании на ${amount}.` : `Пополните бюджет кампании на ${amount}.`;
    }
    case 'pause_campaign': return 'Приостановите кампанию до пополнения запасов.';
    case 'remove_article': return 'Временно уберите этот товар из рекламы.';
    case 'plan_supply': return 'Запланируйте поставку и пока не усиливайте рекламу.';
    case 'check_product':
      if (card.rule.ruleId === 'R01') return card.rule.reasonCode === 'out_of_stock'
        ? 'Проверьте, можно ли остановить рекламу только этого товара.' : 'Проверьте рекламу этого товара.';
      return 'Проверьте цену и карточку товара.';
    default: return null;
  }
}

export function recommendationMoney(card: RecommendationCard) {
  const ranking = card.ranking;
  if (ranking?.weeklyInefficientSpend == null || ranking.basis === 'unavailable') {
    return card.subject.scope === 'cluster' && card.presentation?.weeklySpend != null ? {
      text: `Расход в пересчёте на неделю: около ${formatMoney(card.presentation.weeklySpend)}`,
      explanation: 'Общий расход кластера в пересчёте на неделю.'
    } : null;
  }
  const withoutUnits = ranking.basis === 'spend_without_units';
  return {
    text: withoutUnits ? `Расход в пересчёте на неделю: около ${formatMoney(ranking.weeklyInefficientSpend)}`
      : `Расход сверх нормы: около ${formatMoney(ranking.weeklyInefficientSpend)} в неделю`,
    explanation: withoutUnits ? 'Расход без заказанных единиц в пересчёте на неделю.'
      : 'Расход сверх допустимого при текущей сумме заказов и норме ДРР, в пересчёте на неделю.'
  };
}

export function recommendationDetailReason(card: Evidence) {
  const { rule, facts, economy, norm, presentation: p } = card;
  const details = p?.details;
  switch (rule.ruleId) {
    case 'R03': {
      const outcome = facts.orderedProducts === 0 ? ' По рекламе не заказали ни одной единицы товара.' : '';
      const threshold = facts.orderedProducts === 0 && rule.threshold != null
        ? ` Мы отмечаем такие кластеры при расходе от ${rubles(rule.threshold)}.` : '';
      return `За указанные даты на кластер потрачено ${rubles(facts.spend)}.${outcome}${threshold}`;
    }
    case 'R06':
      return recommendationReason(card, false) + (economy.drr != null
        ? ' Сумму заказов по кластеру оцениваем по средней сумме заказа на единицу товара.' : '');
    case 'R07': {
      if (economy.estimatedOrderSum == null || economy.drr == null) return recommendationReason(card);
      const count = facts.orders != null ? ` Число заказов в расчёте: ${number(facts.orders)}.` : '';
      return `На рекламу потрачено ${rubles(facts.spend)}, с неё заказали на ${rubles(economy.estimatedOrderSum)}. ДРР — ${percent(economy.drr)} при норме ${percent(norm.value)}.${count}`;
    }
    case 'R01': {
      if (rule.reasonCode === 'out_of_stock') return `Товара нет в наличии. За 3 дня на его рекламу потрачено ${rubles(facts.spend)}.`;
      if (details?.fbw == null || details.fbs == null || details.dailyUnits == null || details.stockDays == null)
        return recommendationReason(card);
      const days = details.stockDays < 1 ? 'меньше чем на день' : `примерно на ${quantity(details.stockDays, ['день', 'дня', 'дней'])}`;
      return `В наличии ${quantity(details.fbw + details.fbs, ['единица', 'единицы', 'единиц'])}. В среднем заказывают по ${number(details.dailyUnits)} в день — запаса ${days}.`;
    }
    case 'R02':
      if (details?.budget == null || details.dailySpend == null || details.topUp == null) return recommendationReason(card);
      return `В бюджете осталось ${rubles(details.budget)}. При среднем расходе ${rubles(details.dailySpend)} в день на три дня нужно около ${rubles(details.dailySpend * 3)}.`;
    default:
      return recommendationReason(card);
  }
}

export function recommendationDetailCalculation(card: Evidence) {
  const { rule, economy } = card;
  if (rule.ruleId === 'R03' && rule.threshold != null && economy.allowedSpendPerUnit != null) {
    return { title: 'Как рассчитан порог', lines: [
      'Берём среднюю сумму заказа на одну единицу товара, умножаем на вашу норму ДРР и удваиваем. По умолчанию минимальный порог — 300 ₽.'
    ] };
  }
  if (rule.ruleId !== 'R06') return null;
  const lines = [];
  if (economy.drrMin != null && economy.drrMax != null)
    lines.push(`При разных оценках цены ДРР составляет от ${percent(economy.drrMin)} до ${percent(economy.drrMax)}.`);
  if (economy.drrPlusOne != null)
    lines.push(`Если бы заказали ещё одну единицу, ДРР составил бы ${percent(economy.drrPlusOne)}.`);
  return lines.length ? { title: 'Как рассчитано', lines } : null;
}

export function recommendationDetailActionNotes(card: Evidence, coverage?: Parameters<typeof recommendationLimitations>[1]) {
  const notes = recommendationLimitations(card, coverage);
  if (card.rule.ruleId === 'R06' && belowExclusionThreshold(card))
    notes.unshift(`При единой ставке мы предлагаем исключить кластер только при ДРР от ${percent(card.norm.value * 2)}.`);
  const minimum = card.presentation?.clickEvidence?.minimumClicks;
  const clicks = card.facts.clicks;
  if (card.rule.ruleId === 'R03' && restricted(card, 'insufficient_clicks') && minimum != null && clicks != null) {
    const generic = notes.indexOf('Кликов пока мало для исключения кластера.');
    if (generic >= 0) notes.splice(generic, 1);
    notes.unshift(`Пока ${quantity(clicks, ['клик', 'клика', 'кликов'])}. Для рекомендации об исключении нужно не меньше ${quantity(minimum, ['клика', 'кликов', 'кликов'])}.`);
  }
  return notes;
}

export function recommendationLimitations(card: Evidence, coverage?: { availableDays?: number | null; expectedDays?: number | null }) {
  const p = card.presentation;
  if (!p) return [];
  const messages = p.restrictions.flatMap(({ code }) => {
    switch (code) {
      case 'settings_unsupported': return ['EcomAds не предлагает менять ставку или исключать этот кластер.'];
      case 'settings_stale': return ['Настройки рекламы ещё не обновились.'];
      case 'settings_unknown':
      case 'unsupported_control': return ['Не удалось проверить настройки рекламы для этого кластера.'];
      case 'incomplete':
        return [coverage?.availableDays != null && coverage.expectedDays != null && coverage.availableDays < coverage.expectedDays
          ? `Предварительно: данные есть за ${coverage.availableDays} из ${coverage.expectedDays} дней.` : 'Предварительно: данных за период пока недостаточно.'];
      case 'recent': return ['WB ещё может уточнить данные за эти даты.'];
      case 'key':
        return p.primary.kind === 'check_product' && p.keyShare != null ? [] : ['Кластер приносит заметную долю заказов товара, поэтому исключение не предлагается.'];
      case 'key_unknown': return ['Пока неизвестно, какая доля заказов приходится на кластер.'];
      case 'excluded': return ['Этот кластер уже исключён из рекламы.'];
      case 'insufficient_clicks':
        return p.primary.kind === 'watch' && p.clickEvidence?.minimumClicks != null && card.facts.clicks != null ? [] : ['Кликов пока мало для исключения кластера.'];
      case 'clicks_unknown': return ['Данные о кликах ещё недоступны.'];
      case 'unified_below_exclusion': return [];
      case 'scope_unknown': return ['Снижение общей ставки может затронуть другие товары.'];
      case 'launch': return ['В первые 21 день после запуска EcomAds не предлагает менять расходы по этому сигналу.'];
      default: return code.startsWith('settings_') ? ['Не удалось проверить настройки рекламы для этого кластера.'] : [];
    }
  });
  if (p.primary.kind === 'bid_at_minimum') messages.push('Ставка уже минимальная.');
  if (p.primary.kind === 'cluster_minimum_unknown' || p.primary.label === 'Не удалось проверить минимальную ставку')
    messages.push('Минимальная ставка пока не подтверждена.');
  if (p.primary.label === 'Не удалось проверить шаг ставки' || p.primary.label === 'Доступный шаг не позволяет снизить ставку')
    messages.push('Подходящую более низкую ставку пока не удалось подобрать.');
  if (card.rule.reasonCode === 'out_of_stock' && p.primary.kind === 'check_product' && p.warning)
    messages.push('Пауза всей кампании остановит рекламу и других товаров.');
  if (p.details?.kind === 'budget' && p.details.balance == null)
    messages.push('Баланс рекламного счёта не подтверждён. Проверьте его перед пополнением бюджета.');
  return [...new Set(messages)];
}

export function recommendationEmptyMessage(status: string, budgetHidden = status === 'budget_auto_replenishment') {
  if (budgetHidden) return 'Совет о пополнении бюджета скрыт: в «Нормах» отмечено автопополнение.';
  switch (status) {
    case 'empty': return 'За этот период в статистике нет поисковых кластеров.';
    case 'not_connected': return 'Подключите кабинет WB, чтобы получать рекомендации.';
    case 'disabled': return 'Рекомендации отключены.';
    case 'not_ready': return 'Рекомендаций пока нет: данные WB ещё не готовы.';
    case 'limited':
    case 'unavailable': return 'Рекомендаций пока нет: для части проверок не хватает данных.';
    case 'low_sample': return 'Пока мало заказанных единиц для оценки ДРР.';
    case 'no_deviations': return 'По выполненным проверкам отклонений не найдено.';
    default: return 'Пока нет рекомендаций к выполнению.';
  }
}

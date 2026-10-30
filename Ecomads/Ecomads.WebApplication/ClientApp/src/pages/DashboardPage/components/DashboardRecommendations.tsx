import { Alert, Box, Button, Card, CardContent, Chip, Stack, Tooltip, Typography } from '@mui/material';
import { Link } from 'react-router-dom';
import { lightUiTokens as ui } from '../../../app/lightUiTokens';
import { appRoutes } from '../../../app/routes';
import type { ProjectDashboard } from '../../../shared/api/apiTypes';
import type { RecommendationCard } from '../../../shared/api/clusterRecommendationApi';
import { getRecommendationSummary } from '../../../shared/api/clusterRecommendationApi';
import { DecisionButtons } from '../../../shared/ui/ClusterRecommendationCard';
import { formatMoney } from '../../../shared/lib/formatMoney';
import { formatPercent } from '../../../shared/lib/formatPercent';
import { RecommendationExposure } from '../../../shared/ui/RecommendationExposure';
import type { DashboardFilters } from '../dashboardApi';
import { recommendationLimitation, recommendationMoney } from '../../../shared/lib/recommendationPresentation';

type Summary = Awaited<ReturnType<typeof getRecommendationSummary>>;

function signal(card: RecommendationCard) {
  const details = card.presentation?.details;
  if (details?.kind === 'budget') return { tag: 'Бюджет', tone: ui.status.error,
    value: details.budget == null ? 'Бюджет не подтверждён' : `Бюджет ${formatMoney(details.budget)}`,
    norm: details.dailySpend == null ? '' : `расход ${formatMoney(details.dailySpend)}/день` };
  if (details?.kind === 'stock') return { tag: 'Остатки', tone: ui.status.warning,
    value: details.stockDays == null ? 'Запас товара требует проверки' : `Запаса на ${details.stockDays.toLocaleString('ru-RU', { maximumFractionDigits: 1 })} дн.`, norm: '' };
  if (card.rule.ruleId === 'R03') return { tag: 'Без заказов', tone: ui.status.warning,
    value: `Расход ${formatMoney(card.facts.spend)}`, norm: `${card.facts.clicks ?? '—'} кликов · ${card.facts.orderedProducts ?? '—'} заказанных единиц · ДЦЗ ${formatMoney(card.economy.allowedSpendPerUnit)}` };
  if (!['R06', 'R07'].includes(card.rule.ruleId)) return { tag: 'Требует внимания', tone: ui.status.warning,
    value: `Расход ${formatMoney(card.facts.spend)}`, norm: '' };
  return { tag: 'Выше нормы', tone: ui.status.warning,
    value: card.economy.drr == null ? 'ДРР не определён' : `${card.subject.scope === 'cluster' ? 'Оценочный ДРР' : 'ДРР'} ${formatPercent(card.economy.drr * 100, 1)}`,
    norm: `норма ${formatPercent(card.norm.value * 100, 1)}` };
}

function AttentionCard({ card, runId, name, to }: { card: RecommendationCard; runId: string; name: string; to: string }) {
  const item = signal(card);
  const money = recommendationMoney(card);
  const limitation = recommendationLimitation(card);
  const decision = card.decision;
  const decisionLabel = decision?.status === 'accepted' ? 'Принято' : decision?.status === 'snoozed' ? 'Отложено' : decision?.status === 'irrelevant' ? 'Не актуально' : decision?.status;
  const action = [card.presentation?.primary.label ?? card.rule.explanation,
    ...(card.presentation?.alternatives.map(a => `или ${a.label.toLocaleLowerCase('ru-RU')}`) ?? [])].filter(Boolean).join(' · ');
  const subject = card.subject.clusterName ?? card.subject.nomenclatureName;
  const note = [card.subject.scope === 'cluster' && card.presentation && !money ? `Расход ≈ ${formatMoney(card.presentation.weeklySpend)}/нед.` : '',
    card.preliminary ? 'Предварительно' : '', subject && card.subject.scope !== 'campaign' ? subject : '',
    decision ? `${decisionLabel}${decision.snoozeUntil ? ` до ${decision.snoozeUntil.split('-').reverse().join('.')}` : ''}` : ''].filter(Boolean).join(' · ');
  return <Card component="article" sx={{ minHeight: 250, minWidth: 0, display: 'flex', scrollSnapAlign: 'start' }}><CardContent sx={{ flex: 1, minWidth: 0, display: 'flex', boxSizing: 'border-box', p: 1.75, '&:last-child': { pb: 1.75 } }}>
    <Stack spacing={.5} sx={{ flex: 1, minWidth: 0 }}>
      <RecommendationExposure runId={runId} evaluationId={card.evaluationId} ruleId={card.rule.ruleId} />
      <Stack direction="row" gap={.75} alignItems="center" sx={{ minHeight: 22 }}>
        <Chip size="small" label={item.tag} sx={{ ...item.tone, height: 21, maxWidth: '45%', borderRadius: ui.radius.badge, fontWeight: 700, fontSize: 11 }} />
        <Typography fontSize={14} fontWeight={700} noWrap title={name} sx={{ flex: 1, minWidth: 0 }}>{name}</Typography>
        <Button component={Link} to={to} size="small" sx={{ minWidth: 0, p: 0, fontSize: 12, flexShrink: 0 }}>Открыть</Button>
      </Stack>
      <Typography variant="body2" sx={{ flexShrink: 0, overflowWrap: 'anywhere' }} title={`${item.value}${item.norm ? ` · ${item.norm}` : ''}`}>
        <Box component="span" sx={{ color: item.tone.color, fontWeight: 700 }}>{item.value}</Box>
        {item.norm ? <Box component="span" color="text.secondary"> · {item.norm}</Box> : null}
      </Typography>
      {money ? <Tooltip title={money.explanation}><Typography variant="caption" sx={{ fontWeight: 700, overflowWrap: 'anywhere' }}>{card.preliminary ? 'Предварительно · ' : ''}{money.text}</Typography></Tooltip> : null}
      <Tooltip title={action}><Typography variant="body2" sx={{ fontSize: 13, lineHeight: '18px', height: 36, flexShrink: 0,
        display: '-webkit-box', WebkitLineClamp: 2, WebkitBoxOrient: 'vertical', overflow: 'hidden', overflowWrap: 'anywhere' }}>{action}</Typography></Tooltip>
      {limitation ? <Typography variant="caption" color="text.secondary" sx={{ overflowWrap: 'anywhere' }}>{limitation}</Typography> : null}
      <Typography variant="caption" noWrap title={note} sx={{ height: 16, lineHeight: '16px', color: decision?.status === 'accepted' ? 'success.main' : 'text.secondary' }}>{note || '\u00a0'}</Typography>
      {card.presentation ? <Box sx={{ mt: 'auto !important' }}><DecisionButtons compact campaignId={card.subject.campaignId} runId={runId} evaluationIds={[card.evaluationId]} ruleId={card.rule.ruleId} /></Box> : null}
    </Stack>
  </CardContent></Card>;

}

export function DashboardRecommendations({ data, campaigns, filters }: { data: Summary; campaigns: ProjectDashboard[]; filters: DashboardFilters }) {
  const names = new Map([...data.campaigns.filter(c => c.campaignId).map(c => [c.campaignId!, c.name] as const), ...campaigns.map(c => [c.id, c.name] as const)]);
  const allClear = data.enabled && data.campaigns.length > 0 && data.remaining === 0 && data.top.length === 0 && data.campaigns.every(c => c.status === 'no_deviations');
  const budgetHidden = data.campaigns.some(c => c.status === 'budget_auto_replenishment');
  const campaignLink = (id: string) => `${appRoutes.campaignPath(id)}?${new URLSearchParams({ startDate: filters.startDate ?? '', endDate: filters.endDate ?? '' })}`;
  const visible = data.top.slice(0, 3);
  const remaining = data.remaining + data.top.length - visible.length;
  return <Stack component="section" aria-label="Требуют внимания" spacing={1}>
    <Stack direction="row" gap={1} alignItems="baseline" sx={{ minWidth: 0 }}>
      <Typography component="h2" variant="h6" fontWeight={700} sx={{ flexShrink: 0 }}>Требуют внимания</Typography>
      <Tooltip title={`За фиксированные 14 дней: ${data.window.startDate} — ${data.window.endDate}, МСК. Остатки и бюджет — по отдельным оперативным окнам.`}>
        <Typography variant="caption" color="text.secondary" noWrap>{data.top.length + data.remaining} · за 14 дней</Typography>
      </Tooltip>
    </Stack>
    {visible.length ? <Box sx={{ display: 'grid', gridAutoRows: '1fr', alignItems: 'stretch', gridAutoFlow: { xs: 'column', md: 'row' },
      gridAutoColumns: { xs: 'minmax(280px, 90%)', sm: 'calc((100% - 14px) / 2)', md: 'auto' },
      gridTemplateColumns: { md: `repeat(${Math.min(3, visible.length)}, minmax(0, 1fr))` }, gap: '14px',
      overflowX: 'auto', scrollSnapType: 'x mandatory', pb: .5 }}>
      {visible.map(({ runId, card }) => <AttentionCard key={`${runId}:${card.evaluationId}:${card.rule.ruleId}`} card={card} runId={runId}
        name={names.get(card.subject.campaignId) ?? 'Кампания WB'} to={campaignLink(card.subject.campaignId)} />)}
    </Box> : null}
    {remaining > 0 ? <Typography variant="caption" color="text.secondary">Ещё {remaining} рекомендаций — на экранах кампаний.</Typography> : null}
    {!visible.length ? <Alert severity={allClear ? 'success' : 'info'}>{allClear ? 'По выполненным проверкам отклонений не найдено.' :
      !data.enabled ? 'Рекомендации отключены или нет подключённых кабинетов.' : budgetHidden ? 'Рекомендаций для показа нет. Советы о бюджете скрыты для кампаний с отмеченным автопополнением. Изменить отметку можно в «Нормах».' : 'Рекомендаций пока нет. Данные или проверки могут быть неполными; подробности — на экранах кампаний.'}</Alert> : null}
  </Stack>;
}

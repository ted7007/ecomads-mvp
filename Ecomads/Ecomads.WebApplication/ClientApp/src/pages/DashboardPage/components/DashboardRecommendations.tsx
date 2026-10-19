import { Accordion, AccordionDetails, AccordionSummary, Alert, Box, Button, Card, CardContent, Chip, Stack, Typography } from '@mui/material';
import { Link } from 'react-router-dom';
import { lightUiTokens as ui } from '../../../app/lightUiTokens';
import { appRoutes } from '../../../app/routes';
import type { ProjectDashboard } from '../../../shared/api/apiTypes';
import type { RecommendationCard } from '../../../shared/api/clusterRecommendationApi';
import { getRecommendationSummary } from '../../../shared/api/clusterRecommendationApi';
import { ClusterRecommendationCard, DecisionButtons } from '../../../shared/ui/ClusterRecommendationCard';
import { formatMoney } from '../../../shared/lib/formatMoney';
import { formatPercent } from '../../../shared/lib/formatPercent';
import type { DashboardFilters } from '../dashboardApi';

type Summary = Awaited<ReturnType<typeof getRecommendationSummary>>;

function signal(card: RecommendationCard) {
  const details = card.presentation?.details;
  if (details?.kind === 'budget') return { tag: 'Бюджет', tone: ui.status.error,
    value: details.budget == null ? 'Бюджет не подтверждён' : `Бюджет ${formatMoney(details.budget)}`,
    norm: details.dailySpend == null ? '' : `расход ${formatMoney(details.dailySpend)}/день` };
  if (details?.kind === 'stock') return { tag: 'Остатки', tone: ui.status.warning,
    value: details.stockDays == null ? 'Запас товара требует проверки' : `Запаса на ${details.stockDays.toLocaleString('ru-RU', { maximumFractionDigits: 1 })} дн.`, norm: '' };
  if (card.rule.ruleId === 'R03') return { tag: 'Без заказов', tone: ui.status.warning,
    value: `Расход ${formatMoney(card.facts.spend)}`, norm: `заказанных единиц ${card.facts.orderedProducts ?? '—'}` };
  if (!['R06', 'R07'].includes(card.rule.ruleId)) return { tag: 'Требует внимания', tone: ui.status.warning,
    value: `Расход ${formatMoney(card.facts.spend)}`, norm: '' };
  return { tag: 'Выше нормы', tone: ui.status.warning,
    value: card.economy.drr == null ? 'ДРР не определён' : `${card.subject.scope === 'cluster' ? 'Оценочный ДРР' : 'ДРР'} ${formatPercent(card.economy.drr * 100, 1)}`,
    norm: `норма ${formatPercent(card.norm.value * 100, 1)}` };
}

function AttentionCard({ card, runId, name, to }: { card: RecommendationCard; runId: string; name: string; to: string }) {
  const item = signal(card);
  const decision = card.decision;
  const decisionLabel = decision?.status === 'accepted' ? 'Принято' : decision?.status === 'snoozed' ? 'Отложено' : decision?.status === 'irrelevant' ? 'Не актуально' : decision?.status;
  return <Card component="article" sx={{ height: '100%' }}><CardContent sx={{ p: 2, '&:last-child': { pb: 2 } }}>
    <Stack spacing={1}>
      <Stack direction="row" gap={.75} alignItems="flex-start">
        <Chip size="small" label={item.tag} sx={{ ...item.tone, height: 21, borderRadius: ui.radius.badge, fontWeight: 700, fontSize: 11 }} />
        <Typography fontSize={14} fontWeight={700} sx={{ flex: 1, minWidth: 0, overflowWrap: 'anywhere' }}>{name}</Typography>
        <Button component={Link} to={to} size="small" sx={{ minWidth: 0, p: 0, fontSize: 12 }}>Открыть</Button>
      </Stack>
      {card.subject.scope !== 'campaign' && (card.subject.clusterName || card.subject.nomenclatureName) ? <Typography variant="caption" color="text.secondary" sx={{ overflowWrap: 'anywhere' }}>
        {card.subject.clusterName ?? card.subject.nomenclatureName}</Typography> : null}
      <Typography variant="body2"><Box component="span" sx={{ color: item.tone.color, fontWeight: 700 }}>{item.value}</Box>
        {item.norm ? <Box component="span" color="text.secondary"> · {item.norm}</Box> : null}</Typography>
      <Typography variant="body2" sx={{ overflowWrap: 'anywhere' }}>{card.presentation?.primary.label ?? card.rule.explanation}</Typography>
      {card.preliminary ? <Typography variant="caption" color="warning.main">Предварительная рекомендация</Typography> : null}
      {decision ? <Typography variant="caption" sx={{ fontWeight: 700, color: decision.status === 'accepted' ? 'success.main' : 'text.secondary' }}>
        {decisionLabel}{decision.snoozeUntil ? ` до ${decision.snoozeUntil.split('-').reverse().join('.')}` : ''}</Typography> : null}
      {card.presentation ? <DecisionButtons compact campaignId={card.subject.campaignId} runId={runId} evaluationIds={[card.evaluationId]} ruleId={card.rule.ruleId} /> : null}
      <Accordion disableGutters elevation={0} sx={{ bgcolor: 'transparent', boxShadow: 'none', '&:before': { display: 'none' } }}>
        <AccordionSummary expandIcon="⌄" sx={{ p: 0, minHeight: 28, '& .MuiAccordionSummary-content': { my: .5 } }}><Typography variant="caption">Почему и на каких данных</Typography></AccordionSummary>
        <AccordionDetails sx={{ px: 0, pb: 0 }}><ClusterRecommendationCard card={card} runId={runId} showDecisionButtons={false} /></AccordionDetails>
      </Accordion>
    </Stack>
  </CardContent></Card>;
}

export function DashboardRecommendations({ data, campaigns, filters }: { data: Summary; campaigns: ProjectDashboard[]; filters: DashboardFilters }) {
  const names = new Map([...data.campaigns.filter(c => c.campaignId).map(c => [c.campaignId!, c.name] as const), ...campaigns.map(c => [c.id, c.name] as const)]);
  const allClear = data.enabled && data.campaigns.length > 0 && data.remaining === 0 && data.top.length === 0 && data.campaigns.every(c => c.status === 'no_deviations');
  const campaignLink = (id: string) => `${appRoutes.campaignPath(id)}?${new URLSearchParams({ startDate: filters.startDate ?? '', endDate: filters.endDate ?? '' })}`;
  return <Stack component="section" aria-label="Требуют внимания" spacing={1.5}>
    <Stack direction={{ xs: 'column', sm: 'row' }} gap={.75} alignItems={{ sm: 'baseline' }}>
      <Typography component="h2" variant="h6" fontWeight={700}>Требуют внимания</Typography>
      <Typography variant="caption" color="text.secondary">{data.top.length + data.remaining} · за {data.window.startDate.split('-').reverse().join('.')} — {data.window.endDate.split('-').reverse().join('.')} · МСК</Typography>
    </Stack>
    <Typography variant="caption" color="text.secondary">Рекомендации за фиксированные 14 дней. Остатки и бюджет — по отдельным оперативным окнам.</Typography>
    <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', sm: 'repeat(2, minmax(0, 1fr))', lg: 'repeat(3, minmax(0, 1fr))' }, gap: '14px' }}>
      {data.top.map(({ runId, card }) => <AttentionCard key={`${runId}:${card.evaluationId}:${card.rule.ruleId}`} card={card} runId={runId}
        name={names.get(card.subject.campaignId) ?? 'Кампания WB'} to={campaignLink(card.subject.campaignId)} />)}
    </Box>
    {data.remaining > 0 ? <Typography variant="body2" color="text.secondary">Ещё {data.remaining} рекомендаций — на экранах кампаний.</Typography> : null}
    {!data.top.length ? <Alert severity={allClear ? 'success' : 'info'}>{allClear ? 'Все кампании в норме. По выполненным проверкам отклонений не найдено.' :
      !data.enabled ? 'Рекомендации отключены или нет подключённых кабинетов.' : 'Рекомендаций пока нет. Проверьте итоги по кампаниям: данные или проверки могут быть неполными.'}</Alert> : null}
    {data.campaigns.some(c => c.status !== 'no_deviations') ? <Accordion disableGutters elevation={0}>
      <AccordionSummary expandIcon="⌄"><Typography variant="body2">Итоги по кампаниям и ограничения данных</Typography></AccordionSummary>
      <AccordionDetails><Stack spacing={1}>{data.campaigns.map((c, index) => <Stack key={c.campaignId ?? `${c.name}:${index}`} spacing={.5}>
        <Typography variant="body2">{c.name}: {c.count} рекомендаций · {c.message}</Typography>
        {c.campaignId ? <Button component={Link} to={campaignLink(c.campaignId)} size="small" sx={{ alignSelf: 'flex-start' }}>Подробнее</Button> : null}
      </Stack>)}</Stack></AccordionDetails>
    </Accordion> : null}
  </Stack>;
}

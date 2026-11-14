import { Alert, Box, Button, Card, CardContent, Chip, Stack, Tooltip, Typography } from '@mui/material';
import { Link } from 'react-router-dom';
import { lightUiTokens as ui } from '../../../app/lightUiTokens';
import { appRoutes } from '../../../app/routes';
import type { ProjectDashboard } from '../../../shared/api/apiTypes';
import type { RecommendationCard } from '../../../shared/api/clusterRecommendationApi';
import { getRecommendationSummary } from '../../../shared/api/clusterRecommendationApi';
import { DecisionButtons } from '../../../shared/ui/ClusterRecommendationCard';
import { RecommendationExposure } from '../../../shared/ui/RecommendationExposure';
import type { DashboardFilters } from '../dashboardApi';
import { recommendationAction, recommendationEmptyMessage, recommendationMoney, recommendationPeriod, recommendationReason, recommendationTitle } from '../../../shared/lib/recommendationPresentation';

type Summary = Awaited<ReturnType<typeof getRecommendationSummary>>;

function AttentionCard({ card, runId, name, to, window }: { card: RecommendationCard; runId: string; name: string; to: string; window: Summary['window'] }) {
  const tone = card.rule.ruleId === 'R02' ? ui.status.error : ui.status.warning;
  const money = recommendationMoney(card);
  const decision = card.decision;
  const decisionLabel = decision?.status === 'accepted' ? 'Принято' : decision?.status === 'snoozed' ? 'Отложено' : decision?.status === 'irrelevant' ? 'Не актуально' : decision?.status;
  const action = recommendationAction(card);
  const subject = card.subject.clusterName ?? card.subject.nomenclatureName ?? card.subject.nomenclatureId;
  const note = [card.preliminary ? 'Предварительно' : '',
    decision ? `${decisionLabel}${decision.snoozeUntil ? ` до ${decision.snoozeUntil.split('-').reverse().join('.')}` : ''}` : ''].filter(Boolean).join(' · ');
  return <Card component="article" sx={{ minHeight: 250, minWidth: 0, display: 'flex', scrollSnapAlign: 'start' }}><CardContent sx={{ flex: 1, minWidth: 0, display: 'flex', boxSizing: 'border-box', p: 1.75, '&:last-child': { pb: 1.75 } }}>
    <Stack spacing={.5} sx={{ flex: 1, minWidth: 0 }}>
      <RecommendationExposure runId={runId} evaluationId={card.evaluationId} ruleId={card.rule.ruleId} />
      <Stack direction="row" gap={.75} alignItems="center" sx={{ minHeight: 22 }}>
        <Typography fontSize={14} fontWeight={700} noWrap title={name} sx={{ flex: 1, minWidth: 0 }}>{name}</Typography>
        <Button component={Link} to={to} size="small" sx={{ minWidth: 0, p: 0, fontSize: 12, flexShrink: 0 }}>Открыть</Button>
      </Stack>
      <Chip size="small" label={recommendationTitle(card)} sx={{ ...tone, alignSelf: 'flex-start', maxWidth: '100%', borderRadius: ui.radius.badge, fontWeight: 700, fontSize: 11 }} />
      {subject && card.subject.scope !== 'campaign' ? <Typography variant="body2" fontWeight={700} noWrap title={subject}>{subject}</Typography> : null}
      <Typography variant="caption" color="text.secondary">{recommendationPeriod(card, window)}</Typography>
      <Typography variant="body2"><strong>Почему:</strong> {recommendationReason(card)}</Typography>
      {action ? <Typography variant="body2" sx={{ overflowWrap: 'anywhere' }}><strong>Что делать:</strong> {action}</Typography> : null}
      {money ? <Tooltip title={money.explanation}><Typography variant="caption" fontWeight={700}>{money.text}</Typography></Tooltip> : null}
      {note ? <Typography variant="caption" color={decision?.status === 'accepted' ? 'success.main' : 'text.secondary'}>{note}</Typography> : null}
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
      <Typography variant="caption" color="text.secondary">{data.top.length + data.remaining}</Typography>
    </Stack>
    {visible.length ? <Box sx={{ display: 'grid', gridAutoRows: '1fr', alignItems: 'stretch', gridAutoFlow: { xs: 'column', md: 'row' },
      gridAutoColumns: { xs: 'minmax(280px, 90%)', sm: 'calc((100% - 14px) / 2)', md: 'auto' },
      gridTemplateColumns: { md: `repeat(${Math.min(3, visible.length)}, minmax(0, 1fr))` }, gap: '14px',
      overflowX: 'auto', scrollSnapType: 'x mandatory', pb: .5 }}>
      {visible.map(({ runId, card }) => <AttentionCard key={`${runId}:${card.evaluationId}:${card.rule.ruleId}`} card={card} runId={runId} window={data.window}
        name={names.get(card.subject.campaignId) ?? 'Кампания WB'} to={campaignLink(card.subject.campaignId)} />)}
    </Box> : null}
    {remaining > 0 ? <Typography variant="caption" color="text.secondary">Ещё {remaining} рекомендаций — на экранах кампаний.</Typography> : null}
    {!visible.length ? <Alert severity={allClear ? 'success' : 'info'}>{!data.enabled
      ? 'Рекомендации отключены или нет подключённых кабинетов.'
      : recommendationEmptyMessage(allClear ? 'no_deviations' : data.campaigns.some(c => ['limited', 'not_ready', 'unavailable'].includes(c.status)) ? 'limited' : '', budgetHidden)}</Alert> : null}
  </Stack>;
}

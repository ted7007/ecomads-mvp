import { Accordion, AccordionDetails, AccordionSummary, Box, Button, ButtonBase, Card, CardContent, Checkbox, Chip, Stack, Typography } from '@mui/material';
import { useState } from 'react';
import type { ClusterRecommendationResponse, RecommendationCard } from '../../../shared/api/clusterRecommendationApi';
import { formatMoney } from '../../../shared/lib/formatMoney';
import { ClusterRecommendationCard, DecisionButtons } from '../../../shared/ui/ClusterRecommendationCard';
import { PaginationBar } from '../../../shared/ui/PaginationBar';
import { DetailsDrawer } from './DetailsDrawer';
import { recommendationItems } from './campaignReportModel';
import { recommendationAction, recommendationDates, recommendationEmptyMessage, recommendationMoney, recommendationPeriod, recommendationReason, recommendationTitle } from '../../../shared/lib/recommendationPresentation';
import { RecommendationExposure } from '../../../shared/ui/RecommendationExposure';
function CopyButton({ text, label = 'Копировать кластер', disabled = false }: { text: string; label?: string; disabled?: boolean }) {
  const [status, setStatus] = useState('');
  return <><Button size="small" disabled={disabled} onClick={async () => { try { await navigator.clipboard.writeText(text); setStatus('Скопировано'); } catch { setStatus('Не скопировано'); } }} title={status === 'Не скопировано' ? 'Откройте подробности и выделите текст вручную' : status || undefined}>{status || label}</Button></>;
}
export function ClusterRecommendations({ campaignId, report }: { campaignId: string; report: ClusterRecommendationResponse }) {
  const [page, setPage] = useState(0), [size, setSize] = useState(5), [opened, setOpened] = useState<RecommendationCard | null>(null);
  const openedCard = opened ? report.cards.find(c => c.evaluationId === opened.evaluationId && c.rule.ruleId === opened.rule.ruleId) : null;
  const openedQuality = openedCard ? report.evaluations.find(e => e.evaluationId === openedCard.evaluationId)?.quality : undefined;
  const items = recommendationItems(report);
  const safePage = Math.min(page, Math.max(0, Math.ceil(items.length / size) - 1));
  return <Stack spacing={2}><Typography component="h2" variant="h6" fontWeight={700}>Рекомендации кампании</Typography>
    <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: '1fr 1fr' }, gap: 2 }}>{items.slice(safePage * size, (safePage + 1) * size).map(item => {
      if (item.kind === 'package') return <Box key={item.id} sx={{ gridColumn: '1 / -1' }}><ExclusionPackage report={report} cards={item.cards} onOpen={setOpened} /></Box>;
      const card = item.card;
      const money = recommendationMoney(card), reason = recommendationReason(card), action = recommendationAction(card);
      return <Card key={item.id}><CardContent sx={{ minHeight: 280, height: '100%', minWidth: 0, boxSizing: 'border-box', display: 'flex', flexDirection: 'column', gap: .5, '& > *': { flexShrink: 0 }, '& .MuiButton-root': { fontSize: 12, minHeight: 28, py: .25 }, '&:last-child': { pb: 2 } }}>
      <RecommendationExposure runId={report.runId} evaluationId={card.evaluationId} ruleId={card.rule.ruleId} /><Stack direction="row" gap={1} useFlexGap flexWrap="wrap" alignItems="center"><Chip size="small" color="warning" variant="outlined" label={recommendationTitle(card)} />{card.preliminary ? <Chip size="small" variant="outlined" label="Предварительно" /> : null}{card.decision ? <Chip size="small" color={card.decision.status === 'accepted' ? 'success' : card.decision.status === 'snoozed' ? 'info' : 'default'} label={card.decision.status === 'accepted' ? 'Принял' : card.decision.status === 'snoozed' ? 'Отложено' : 'Не актуально'} /> : null}</Stack>
      <ButtonBase onClick={() => setOpened(card)} aria-label={`Открыть рекомендацию: ${card.subject.clusterName ?? card.subject.nomenclatureName ?? 'Вся кампания'}`} sx={{ display: 'block', width: '100%', textAlign: 'left', borderRadius: 1 }}><Typography fontWeight={700} sx={{ fontSize: 14, lineHeight: '20px' }} noWrap title={card.subject.clusterName ?? card.subject.nomenclatureName ?? 'Вся кампания'}>{card.subject.clusterName ?? card.subject.nomenclatureName ?? 'Вся кампания'}</Typography></ButtonBase>
      <Typography variant="caption" color="text.secondary">{card.subject.nomenclatureId ? `Артикул ${card.subject.nomenclatureId} · ` : ''}{recommendationPeriod(card, report.window)}</Typography>
      <Typography variant="body2" sx={{ fontSize: 13, lineHeight: '18px' }}><strong>Почему:</strong> {reason}</Typography>
      {action ? <Typography variant="body2" sx={{ overflowWrap: 'anywhere', fontSize: 13, lineHeight: '18px' }}><strong>Что делать:</strong> {action}</Typography> : null}
      {money ? <Typography variant="caption" title={money.explanation} fontWeight={700}>{money.text}</Typography> : null}
      <Stack direction="row" sx={{ mt: 'auto' }}>{card.subject.clusterName ? <CopyButton text={card.subject.clusterName} /> : null}<Button size="small" onClick={() => setOpened(card)}>Подробнее</Button></Stack>
      {report.runId && card.presentation ? <DecisionButtons compact campaignId={campaignId} runId={report.runId} evaluationIds={[card.evaluationId]} ruleId={card.rule.ruleId} /> : null}
    </CardContent></Card>; })}</Box>
    {items.length ? <PaginationBar desktopSizeButtons count={items.length} page={safePage} rowsPerPage={size} onPageChange={setPage} onRowsPerPageChange={n => { setSize(n); setPage(0); }} /> : null}
    {!report.cards.length ? <Typography color="text.secondary">{recommendationEmptyMessage(report.status)}</Typography> : null}
    <DetailsDrawer open={!!openedCard} onClose={() => setOpened(null)} title="Рекомендация">{openedCard ? <ClusterRecommendationCard card={openedCard} runId={report.runId} window={report.window} quality={openedQuality} /> : null}</DetailsDrawer>
  </Stack>;
}
function ExclusionPackage({ report, cards, onOpen }: { report: ClusterRecommendationResponse; cards: RecommendationCard[]; onOpen: (card: RecommendationCard) => void }) {
  const [selected, setSelected] = useState(() => cards.map(c => c.evaluationId));
  const [page, setPage] = useState(0), [size, setSize] = useState(5);
  const chosen = cards.filter(c => selected.includes(c.evaluationId));
  const groups = new Map<string, string[]>(); chosen.forEach(c => { const article = c.subject.nomenclatureId ?? '—'; groups.set(article, [...(groups.get(article) ?? []), c.subject.clusterName ?? '']); });
  const text = [...groups].map(([a, n]) => `Артикул ${a}\n${n.join('\n')}`).join('\n\n');
  const safePage = Math.min(page, Math.max(0, Math.ceil(cards.length / size) - 1));
  const weeklySpend = chosen.every(c => c.presentation != null) ? chosen.reduce((sum, c) => sum + c.presentation!.weeklySpend, 0) : null;
  return <Card role="region" aria-label="Пакет исключений" sx={{ borderColor: 'warning.main' }}><CardContent><Stack spacing={1.5}><Typography variant="h6">Кластеры без заказанных единиц · {cards.length}</Typography>
    <Typography variant="caption" color="text.secondary">Данные за {recommendationDates(report.window)}</Typography>
    <Typography variant="body2">Выберите кластеры, скопируйте список и добавьте названия в минус-фразы WB для каждого артикула.</Typography>
    {cards.slice(safePage * size, (safePage + 1) * size).map(c => <Stack key={c.evaluationId} direction="row" alignItems="center" gap={1}><RecommendationExposure runId={report.runId} evaluationId={c.evaluationId} ruleId={c.rule.ruleId} /><Checkbox checked={selected.includes(c.evaluationId)} inputProps={{ 'aria-label': `Выбрать ${c.subject.clusterName}` }} onChange={(_, yes) => setSelected(old => yes ? [...new Set([...old, c.evaluationId])] : old.filter(id => id !== c.evaluationId))} /><Box sx={{ flex: 1, minWidth: 0 }}><ButtonBase onClick={() => onOpen(c)} aria-label={`Открыть рекомендацию: ${c.subject.clusterName}`} sx={{ display: 'block', textAlign: 'left', borderRadius: 1, fontWeight: 700, overflowWrap: 'anywhere' }}>{c.subject.clusterName}</ButtonBase><Typography variant="caption">Арт. {c.subject.nomenclatureId} · Расход {formatMoney(c.facts.spend)}</Typography>{c.decision ? <Chip size="small" color={c.decision.status === 'accepted' ? 'success' : c.decision.status === 'snoozed' ? 'info' : 'default'} label={c.decision.status === 'accepted' ? 'Принял' : c.decision.status === 'snoozed' ? 'Отложено' : 'Не актуально'} /> : null}</Box></Stack>)}
    <PaginationBar desktopSizeButtons count={cards.length} page={safePage} rowsPerPage={size} onPageChange={setPage} onRowsPerPageChange={n => { setSize(n); setPage(0); }} />
    <Typography>Выбрано {chosen.length} из {cards.length}{chosen.length > 0 && weeklySpend != null ? ` · Расход выбранных кластеров: около ${formatMoney(weeklySpend)} в неделю` : ''}</Typography>
    <CopyButton text={text} label="Скопировать выбранные" disabled={!chosen.length} />
    {report.runId && cards.length ? <DecisionButtons compact campaignId={cards[0].subject.campaignId} runId={report.runId} evaluationIds={chosen.map(c => c.evaluationId)} ruleId="R03" /> : null}
    <Accordion disableGutters elevation={0}><AccordionSummary expandIcon="⌄"><Typography variant="caption">Список для ручного копирования</Typography></AccordionSummary><AccordionDetails><Typography component="pre" sx={{ whiteSpace: 'pre-wrap', overflowWrap: 'anywhere', userSelect: 'text' }}>{text}</Typography></AccordionDetails></Accordion>
  </Stack></CardContent></Card>;
}

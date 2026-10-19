import { Accordion, AccordionDetails, AccordionSummary, Alert, Button, Checkbox, Stack, TextField, Typography } from '@mui/material';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect, useState } from 'react';
import { getArticleNorms, saveArticleNorm } from '../../../shared/api/clusterRecommendationApi';
import type { ArticleNorm, ClusterRecommendationResponse } from '../../../shared/api/clusterRecommendationApi';
import { formatMoney } from '../../../shared/lib/formatMoney';
import { ClusterRecommendationCard, DecisionButtons } from '../../../shared/ui/ClusterRecommendationCard';

const sources: Record<string, string> = { article: 'товар', campaign: 'кампания', store: 'кабинет', default: 'по умолчанию' };


export function ClusterRecommendations({ campaignId, report }: { campaignId: string; report: ClusterRecommendationResponse }) {
  const unavailable = report.evaluations.filter(row => row.rules.some(rule => rule.status === 'skipped'));
  return <Stack spacing={2}>
    <Typography variant="h6" fontWeight={700}>Рекомендации кампании</Typography>
    <Typography variant="body2" color="text.secondary">
      Рекомендации за 14 дней: {report.window.startDate} — {report.window.endDate}, МСК. Период отчёта не влияет на расчёт.
    </Typography>
    <Alert severity={report.cards.length || unavailable.length ? 'warning' : 'info'}>{report.message}</Alert>
    <Typography variant="body2">Рекомендаций: {report.cards.length} · расход уникальных кластеров ≈ {formatMoney([...new Map(report.cards.filter(c => c.subject.scope === 'cluster').map(c => [`${c.subject.articleId}:${c.subject.clusterName}`, c])).values()].reduce((sum, c) => sum + (c.presentation?.weeklySpend ?? c.facts.spend / report.window.days * 7), 0))} в неделю.</Typography>
    {report.packages?.map(bundle => <ExclusionPackage key={bundle.id} report={report} ids={bundle.evaluationIds} />)}
    {report.cards.filter(card => !report.packages?.some(bundle => bundle.evaluationIds.includes(card.evaluationId))).map(card => <ClusterRecommendationCard key={`${card.evaluationId}:${card.rule.ruleId}`} card={card} runId={report.runId} />)}
    {unavailable.length ? <Accordion disableGutters elevation={0}>
      <AccordionSummary expandIcon="⌄"><Typography>Ограничения расчёта · {unavailable.length} объектов</Typography></AccordionSummary>
      <AccordionDetails><Stack spacing={1}>{unavailable.map(row => <Typography variant="body2" key={row.evaluationId}>
        {row.subject.clusterName ?? row.subject.nomenclatureName ?? 'Кампания'} {row.subject.nomenclatureId ?? ''}: {row.rules.find(rule => rule.status === 'skipped')?.explanation}
      </Typography>)}</Stack></AccordionDetails>
    </Accordion> : null}
    <Typography variant="caption" color="text.secondary">Проверок: {report.coverage.evaluated} из {report.coverage.enabled} включённых · пропущено: {report.coverage.skipped}
      {report.calculatedAtUtc ? ` · расчёт ${new Date(report.calculatedAtUtc).toLocaleString('ru-RU', { timeZone: 'Europe/Moscow' })} МСК` : ''}</Typography>
    <ArticleNormEditor campaignId={campaignId} />
  </Stack>;
}

function ArticleNormEditor({ campaignId }: { campaignId: string }) {
  const [open, setOpen] = useState(false);
  const query = useQuery({ queryKey: ['wb-article-norms', campaignId], queryFn: () => getArticleNorms(campaignId), enabled: open });
  return <Accordion expanded={open} onChange={(_, expanded) => setOpen(expanded)} disableGutters elevation={0}>
    <AccordionSummary expandIcon="⌄"><Typography>Нормы и даты запуска товаров</Typography></AccordionSummary>
    <AccordionDetails><Stack spacing={2}>
      <Typography variant="body2" color="text.secondary">Норма товара важнее нормы кампании и кабинета. Пустое поле наследует значение.</Typography>
      {query.isLoading ? <Typography>Загрузка норм…</Typography> : null}
      {query.isError ? <Alert severity="error">Не удалось загрузить нормы. <Button onClick={() => void query.refetch()}>Повторить</Button></Alert> : null}
      {query.data?.map(row => <ArticleNormField key={row.articleId} campaignId={campaignId} row={row} />)}
    </Stack></AccordionDetails>
  </Accordion>;
}

function ArticleNormField({ campaignId, row }: { campaignId: string; row: ArticleNorm }) {
  const [value, setValue] = useState(row.targetDrr?.toString() ?? '');
  const [launchDate, setLaunchDate] = useState(row.launchDate ?? '');
  useEffect(() => setLaunchDate(row.launchDate ?? ''), [row.launchDate]);
  useEffect(() => setValue(row.targetDrr?.toString() ?? ''), [row.targetDrr]);
  const cache = useQueryClient();
  const numeric = value === '' ? null : Number(value);
  const invalid = numeric !== null && (!Number.isFinite(numeric) || numeric < 0 || numeric > 1000 || Math.round(numeric * 100) / 100 !== numeric);
  const save = useMutation({ mutationFn: () => saveArticleNorm(campaignId, row.articleId, numeric, launchDate || null),
    onSuccess: async () => { await Promise.all([
      cache.invalidateQueries({ queryKey: ['wb-recommendation-summary'] }),
      cache.invalidateQueries({ queryKey: ['wb-article-norms', campaignId] }),
      cache.invalidateQueries({ queryKey: ['wb-cluster-recommendations', campaignId] })
    ]); } });
  return <Stack spacing={0.5}>
    <Stack direction={{ xs: 'column', sm: 'row' }} gap={1} alignItems={{ sm: 'center' }}>
      <Typography variant="body2" sx={{ flex: 1 }}>{row.nomenclatureId} · {row.name}</Typography>
      <TextField label="Норма ДРР, %" type="number" size="small" value={value} error={invalid}
        placeholder={String(row.effectiveTargetDrr)} inputProps={{ min: 0, max: 1000, step: 0.01 }}
        onChange={event => setValue(event.target.value)} helperText={`Сейчас ${row.effectiveTargetDrr}% · ${sources[row.source]}`} />
      <TextField type="date" label="Запуск товара" size="small" value={launchDate} InputLabelProps={{ shrink: true }}
        helperText={row.effectiveLaunchDate ? `Сейчас ${row.effectiveLaunchDate}` : 'Пусто: наследуется дата кампании'} onChange={e => setLaunchDate(e.target.value)} />
      <Button disabled={invalid || save.isPending || numeric === row.targetDrr && launchDate === (row.launchDate ?? '')} onClick={() => save.mutate()}>Сохранить</Button>
    </Stack>
    {save.isError ? <Alert severity="error">Не удалось сохранить норму.</Alert> : null}
  </Stack>;
}

function ExclusionPackage({ report, ids }: { report: ClusterRecommendationResponse; ids: string[] }) {
  const [selected, setSelected] = useState<string[]>(ids);
  const [copied, setCopied] = useState('');
  const cards = report.cards.filter(c => ids.includes(c.evaluationId));
  const chosen = cards.filter(c => selected.includes(c.evaluationId));
  useEffect(() => setSelected(ids), [ids]);
  return <Stack spacing={1} sx={{ border: '1px solid', borderColor: 'primary.main', borderRadius: 2, p: 2 }}>
    <Typography fontWeight={700}>Пакет исключений · {cards.length} кластеров</Typography>
    <Typography variant="body2">Выберите кластеры. Список сгруппирован по товарам; исключения в WB задаются для конкретного артикула.</Typography>
    {cards.map(c => <Stack key={c.evaluationId} direction="row" alignItems="center"><Checkbox checked={selected.includes(c.evaluationId)}
      onChange={(_, checked) => setSelected(old => checked ? [...old, c.evaluationId] : old.filter(id => id !== c.evaluationId))} inputProps={{ 'aria-label': `Выбрать ${c.subject.clusterName}` }} />
      <Accordion sx={{ flex: 1 }} disableGutters elevation={0}><AccordionSummary expandIcon="⌄"><Typography variant="body2">{c.subject.nomenclatureId} · {c.subject.clusterName} · {formatMoney(c.facts.spend)}</Typography></AccordionSummary>
        <AccordionDetails><ClusterRecommendationCard card={c} runId={report.runId} /></AccordionDetails></Accordion></Stack>)}
    <Typography variant="body2">Выбрано {chosen.length} · расход выбранных кластеров ≈ {formatMoney(chosen.reduce((sum, c) => sum + (c.presentation?.weeklySpend ?? 0), 0))} в неделю</Typography>
    <Button disabled={!chosen.length} onClick={async () => { try {
      const groups = new Map<string, string[]>();
      chosen.forEach(c => groups.set(c.subject.nomenclatureId!, [...(groups.get(c.subject.nomenclatureId!) ?? []), c.subject.clusterName!]));
      await navigator.clipboard.writeText([...groups].map(([nm, names]) => `Артикул ${nm}\n${names.join('\n')}`).join('\n\n'));
      setCopied('Список скопирован');
    } catch { setCopied('Не удалось скопировать. Выделите список вручную.'); } }}>Копировать выбранные</Button>
    {copied ? <Typography variant="caption">{copied}</Typography> : null}
    {report.runId ? <DecisionButtons campaignId={cards[0].subject.campaignId} runId={report.runId} evaluationIds={selected} ruleId="R03" /> : null}
  </Stack>;
}

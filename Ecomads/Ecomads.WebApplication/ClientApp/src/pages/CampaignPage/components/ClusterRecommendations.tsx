import { Accordion, AccordionDetails, AccordionSummary, Alert, Button, Chip, Stack, TextField, Typography } from '@mui/material';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect, useState } from 'react';
import { getArticleNorms, saveArticleNorm } from '../../../shared/api/clusterRecommendationApi';
import type { ArticleNorm, ClusterRecommendationResponse } from '../../../shared/api/clusterRecommendationApi';
import { formatMoney } from '../../../shared/lib/formatMoney';
import { formatPercent } from '../../../shared/lib/formatPercent';

const sources: Record<string, string> = { article: 'товар', campaign: 'кампания', store: 'кабинет', default: 'по умолчанию' };
const percent = (value: number | null) => value === null ? '—' : formatPercent(value * 100, 2);

export function ClusterRecommendations({ campaignId, report }: { campaignId: string; report: ClusterRecommendationResponse }) {
  const unavailable = report.evaluations.filter(row => row.rules.some(rule => rule.status === 'skipped'));
  return <Stack spacing={2}>
    <Typography variant="h6" fontWeight={800}>Рекомендации по кластерам</Typography>
    <Typography variant="body2" color="text.secondary">
      {report.window.startDate} — {report.window.endDate} · даты по МСК · тот же период, что в таблице
    </Typography>
    <Alert severity={report.cards.length || unavailable.length ? 'warning' : 'info'}>{report.message}</Alert>
    {report.cards.map(card => <Stack key={`${card.evaluationId}:${card.rule.ruleId}`} spacing={0.75}
      sx={{ border: '1px solid', borderColor: 'divider', borderRadius: 2, p: 2 }}>
      <Stack direction="row" spacing={1} alignItems="center" useFlexGap flexWrap="wrap">
        <Typography fontWeight={700}>{card.rule.ruleId === 'R03' ? 'Расход без заказанных единиц' : 'ДРР кластера выше нормы'}</Typography>
        <Chip size="small" label="Предварительно" color="warning" variant="outlined" />
      </Stack>
      <Typography>{card.subject.clusterName} · артикул {card.subject.nomenclatureId}</Typography>
      <Typography variant="body2">Расход {formatMoney(card.facts.spend)} · заказанные единицы: {card.facts.orderedProducts ?? '—'}.</Typography>
      {card.rule.ruleId === 'R03' ? <Typography variant="body2">
        Порог расхода {formatMoney(card.rule.threshold ?? 0)} · допустимый расход на единицу {formatMoney(card.economy.allowedSpendPerUnit ?? 0)}.
      </Typography> : <Typography variant="body2">
        Оценочный ДРР {percent(card.economy.drr)} · сценарный диапазон {percent(card.economy.drrMin)} — {percent(card.economy.drrMax)}.
        {' '}С одной дополнительной единицей: {percent(card.economy.drrPlusOne)}.
      </Typography>}
      <Typography variant="body2">Норма {percent(card.norm.value)} · источник: {sources[card.norm.source]}.</Typography>
      <Typography variant="body2">{card.rule.explanation}</Typography>
      <Typography variant="caption" color="text.secondary">Сумма заказов кластера оценена по средней сумме на единицу товара за выбранный период. Это сценарная оценка, а не фактическая выручка. Данные могут уточняться.</Typography>
    </Stack>)}
    {unavailable.length ? <Accordion disableGutters elevation={0}>
      <AccordionSummary expandIcon="⌄"><Typography>Не удалось проверить {unavailable.length} объектов</Typography></AccordionSummary>
      <AccordionDetails><Stack spacing={1}>{unavailable.map(row => <Typography variant="body2" key={row.evaluationId}>
        {row.subject.clusterName} · {row.subject.nomenclatureId}: {row.rules.find(rule => rule.status === 'skipped')?.explanation}
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
    <AccordionSummary expandIcon="⌄"><Typography>Нормы ДРР по товарам этой кампании</Typography></AccordionSummary>
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
  useEffect(() => setValue(row.targetDrr?.toString() ?? ''), [row.targetDrr]);
  const cache = useQueryClient();
  const numeric = value === '' ? null : Number(value);
  const invalid = numeric !== null && (!Number.isFinite(numeric) || numeric < 0 || numeric > 1000 || Math.round(numeric * 100) / 100 !== numeric);
  const save = useMutation({ mutationFn: () => saveArticleNorm(campaignId, row.articleId, numeric),
    onSuccess: async () => { await Promise.all([
      cache.invalidateQueries({ queryKey: ['wb-article-norms', campaignId] }),
      cache.invalidateQueries({ queryKey: ['wb-cluster-recommendations', campaignId] })
    ]); } });
  return <Stack spacing={0.5}>
    <Stack direction={{ xs: 'column', sm: 'row' }} gap={1} alignItems={{ sm: 'center' }}>
      <Typography variant="body2" sx={{ flex: 1 }}>{row.nomenclatureId} · {row.name}</Typography>
      <TextField label="Норма ДРР, %" type="number" size="small" value={value} error={invalid}
        placeholder={String(row.effectiveTargetDrr)} inputProps={{ min: 0, max: 1000, step: 0.01 }}
        onChange={event => setValue(event.target.value)} helperText={`Сейчас ${row.effectiveTargetDrr}% · ${sources[row.source]}`} />
      <Button disabled={invalid || save.isPending || numeric === row.targetDrr} onClick={() => save.mutate()}>Сохранить</Button>
    </Stack>
    {save.isError ? <Alert severity="error">Не удалось сохранить норму.</Alert> : null}
  </Stack>;
}

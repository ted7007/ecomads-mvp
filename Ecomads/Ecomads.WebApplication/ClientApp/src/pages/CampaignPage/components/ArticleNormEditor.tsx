import { Accordion, AccordionDetails, AccordionSummary, Alert, Button, Stack, TextField, Typography } from '@mui/material';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect, useState } from 'react';
import { getArticleNorms, saveArticleNorm, type ArticleNorm } from '../../../shared/api/clusterRecommendationApi';
const sources: Record<string, string> = { article: 'товар', campaign: 'кампания', store: 'кабинет', default: 'по умолчанию' };
export function ArticleNormEditor({ campaignId }: { campaignId: string }) {
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
  const save = useMutation({
    mutationFn: () => saveArticleNorm(campaignId, row.articleId, numeric, launchDate || null),
    onSuccess: async () => {
      await Promise.all([
        cache.invalidateQueries({ queryKey: ['wb-recommendation-summary'] }),
        cache.invalidateQueries({ queryKey: ['wb-article-norms', campaignId] }),
        cache.invalidateQueries({ queryKey: ['wb-cluster-recommendations', campaignId] })
      ]);
    }
  });
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


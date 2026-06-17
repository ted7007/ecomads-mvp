import { Alert, Box, Button, Card, CardContent, Chip, Divider, Stack, TextField, Typography } from '@mui/material';
import { useEffect, useState } from 'react';
import type { InsightDecision } from '../campaignApi';
import type { KeywordInsightDetail, KeywordRecommendationRow } from '../campaignSchemas';
import { formatExpectedEffect, formatMetricName, formatMetricValue, getDecisionLabel, getStatusColor, getStatusLabel } from '../campaignFormatters';

type InsightPanelProps = {
  insight: KeywordInsightDetail | null;
  keyword: KeywordRecommendationRow | null;
  isUpdating: boolean;
  onDecision: (insightId: string, decision: InsightDecision) => void;
  onSaveComment: (insightId: string, comment: string) => void;
};

export function InsightPanel({ insight, keyword, isUpdating, onDecision, onSaveComment }: InsightPanelProps) {
  const [comment, setComment] = useState('');
  const panelSx = { borderRadius: 2, boxShadow: 'none', border: '1px solid #E2E8F0' };

  useEffect(() => {
    setComment(insight?.userComment ?? '');
  }, [insight?.insightId, insight?.userComment]);

  if (!keyword) {
    return (
      <Card sx={panelSx}>
        <CardContent>
          <Typography color="text.secondary">Выберите строку ключевого слова, чтобы увидеть детали рекомендации.</Typography>
        </CardContent>
      </Card>
    );
  }

  if (!insight) {
    return (
      <Card sx={panelSx}>
        <CardContent>
          <Stack spacing={1}>
            <Typography variant="h6" fontWeight={800}>
              {keyword.phrase}
            </Typography>
            <Alert severity="info">По этому ключевому слову нет активной рекомендации.</Alert>
          </Stack>
        </CardContent>
      </Card>
    );
  }

  return (
    <Card sx={panelSx}>
      <CardContent>
        <Stack spacing={2}>
          <Box>
            <Typography color="text.secondary" variant="overline">
              Insight
            </Typography>
            <Typography variant="h6" fontWeight={800}>
              {insight.phrase}
            </Typography>
            <Stack direction="row" spacing={1} flexWrap="wrap" sx={{ mt: 1 }}>
              <Chip color={getStatusColor(insight.status)} label={getStatusLabel(insight.status)} size="small" />
              <Chip label={`${insight.priorityLevel} · ${Math.round(insight.priorityScore)}`} size="small" />
              {insight.decisionStatus !== 'None' ? <Chip color="primary" label={getDecisionLabel(insight.decisionStatus)} size="small" /> : null}
            </Stack>
          </Box>

          <Divider />

          <Section title="Почему">
            <Typography>{insight.shortExplanation || '—'}</Typography>
          </Section>

          <MetricGroups metrics={insight.metrics} />

          <Section title="Действие">
            <Typography fontWeight={700}>{insight.recommendedActionTitle || '—'}</Typography>
            <Typography color="text.secondary">{insight.recommendedActionDescription || '—'}</Typography>
          </Section>

          <Section title="Ожидаемый эффект">
            <Typography>{formatExpectedEffect(insight.expectedEffectType, insight.expectedEffectMoney, insight.expectedEffectText)}</Typography>
          </Section>

          <Stack direction="row" gap={1} flexWrap="wrap">
            <Button disabled={isUpdating} variant={insight.decisionStatus === 'Accepted' ? 'contained' : 'outlined'} onClick={() => onDecision(insight.insightId, 'accept')}>
              В работу
            </Button>
            <Button disabled={isUpdating} variant={insight.decisionStatus === 'Applied' ? 'contained' : 'outlined'} onClick={() => onDecision(insight.insightId, 'apply')}>
              Выполнено
            </Button>
            <Button disabled={isUpdating} color="warning" variant={insight.decisionStatus === 'Postponed' ? 'contained' : 'outlined'} onClick={() => onDecision(insight.insightId, 'postpone')}>
              Отложить
            </Button>
            <Button disabled={isUpdating} color="error" variant={insight.decisionStatus === 'Rejected' ? 'contained' : 'outlined'} onClick={() => onDecision(insight.insightId, 'reject')}>
              Отклонить
            </Button>
          </Stack>

          <Stack spacing={1}>
            <TextField label="Комментарий" multiline minRows={3} value={comment} onChange={(event) => setComment(event.target.value)} />
            <Button disabled={isUpdating} variant="outlined" onClick={() => onSaveComment(insight.insightId, comment)}>
              Сохранить комментарий
            </Button>
          </Stack>
        </Stack>
      </CardContent>
    </Card>
  );
}

const metricGroups = [
  { title: 'Трафик', keys: ['views', 'impressions', 'clicks', 'ctr'] },
  { title: 'Экономика', keys: ['spend', 'orders', 'revenue', 'drr'] },
  { title: 'Эффективность', keys: ['cr', 'cpc', 'cpo', 'averageOrderValue', 'confidenceScore'] }
];

function MetricGroups({ metrics }: { metrics: Record<string, number | null | undefined> }) {
  const renderedKeys = new Set<string>();

  return (
    <Stack spacing={1.5}>
      {metricGroups.map((group) => {
        const entries = group.keys
          .filter((key) => metrics[key] !== undefined)
          .map((key) => {
            renderedKeys.add(key);
            return [key, metrics[key]] as const;
          });

        if (entries.length === 0) {
          return null;
        }

        return <MetricGroup key={group.title} title={group.title} entries={entries} />;
      })}
      <MetricGroup
        title="Прочее"
        entries={Object.entries(metrics)
          .filter(([key]) => !renderedKeys.has(key))
          .slice(0, 6)}
      />
    </Stack>
  );
}

function MetricGroup({ title, entries }: { title: string; entries: Array<readonly [string, number | null | undefined]> }) {
  if (entries.length === 0) {
    return null;
  }

  return (
    <Stack spacing={0.75}>
      <Typography color="text.secondary" fontWeight={800} variant="caption" sx={{ textTransform: 'uppercase' }}>
        {title}
      </Typography>
      <Box
        sx={{
          display: 'grid',
          gridTemplateColumns: 'repeat(2, minmax(0, 1fr))',
          gap: 1
        }}
      >
        {entries.map(([key, value]) => (
          <Box key={key} sx={{ bgcolor: '#F8FAFC', border: '1px solid #E2E8F0', borderRadius: 2, p: 1.25 }}>
            <Typography color="text.secondary" variant="caption">
              {formatMetricName(key)}
            </Typography>
            <Typography fontSize={15} fontWeight={800}>
              {formatMetricValue(key, value)}
            </Typography>
          </Box>
        ))}
      </Box>
    </Stack>
  );
}

function Section({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <Stack spacing={0.5}>
      <Typography variant="subtitle2" fontWeight={800}>
        {title}
      </Typography>
      {children}
    </Stack>
  );
}

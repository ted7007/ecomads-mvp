import { Box, Button, Chip, Stack, Typography } from '@mui/material';
import type { KeywordRecommendationOverlay } from '../campaignSchemas';
import { getStatusLabel, getStatusStyle } from '../campaignFormatters';

const statusOrder = ['ToRemove', 'NeedsAttention', 'Effective', 'Watch', 'LowData', 'Neutral'];

type KeywordOverlaySummaryProps = {
  overlay?: KeywordRecommendationOverlay;
  selectedStatus: string;
  onStatusChange: (status: string) => void;
};

export function KeywordOverlaySummary({ overlay, selectedStatus, onStatusChange }: KeywordOverlaySummaryProps) {
  if (!overlay) {
    return null;
  }

  const counts = overlay.recommendationSummary.counts;
  const total = overlay.keywords.length;
  const generated = overlay.generatedAt ? new Date(overlay.generatedAt).toLocaleString('ru-RU') : 'нет генерации';

  return (
    <Stack spacing={2}>
      <Box>
        <Stack direction="row" alignItems="center" spacing={1} flexWrap="wrap">
          <Typography variant="h6" fontWeight={800}>
            Recommendation overlay
          </Typography>
          {overlay.recommendationSummary.generatedWithoutLlm ? <Chip color="warning" label="fallback" size="small" /> : null}
        </Stack>
        <Box
          sx={{
            bgcolor: '#EEF2F7',
            border: '1px solid #D8E0EA',
            borderRadius: 2,
            color: '#0F172A',
            display: 'flex',
            justifyContent: 'space-between',
            gap: 2,
            mt: 1,
            p: 1.5
          }}
        >
          <Typography fontWeight={700}>{overlay.recommendationSummary.text || 'По текущим правилам нет активных рекомендаций.'}</Typography>
          <Typography color="text.secondary" variant="body2" sx={{ flex: '0 0 auto' }}>
            {generated}
          </Typography>
        </Box>
      </Box>

      <Stack direction="row" spacing={1} flexWrap="wrap">
        <Button size="small" variant={selectedStatus === 'All' ? 'contained' : 'outlined'} onClick={() => onStatusChange('All')} sx={{ borderRadius: 999 }}>
          Все · {total}
        </Button>
        {statusOrder.map((status) => {
          const count = counts[status.charAt(0).toLowerCase() + status.slice(1) as keyof typeof counts] ?? 0;
          const style = getStatusStyle(status);

          return (
            <Button
              key={status}
              size="small"
              variant={selectedStatus === status ? 'contained' : 'outlined'}
              onClick={() => onStatusChange(status)}
              sx={{
                borderRadius: 999,
                color: selectedStatus === status ? '#FFFFFF' : style.text,
                borderColor: style.border,
                bgcolor: selectedStatus === status ? 'primary.main' : '#FFFFFF',
                '&:hover': {
                  borderColor: style.border,
                  bgcolor: selectedStatus === status ? 'primary.dark' : style.bg
                }
              }}
            >
              {getStatusLabel(status)} · {count}
            </Button>
          );
        })}
      </Stack>
    </Stack>
  );
}

import { Box, Card, CardContent, Stack, Typography } from '@mui/material';
import type { MonthlyRecommendationStats } from '../../../shared/api/apiTypes';
import { EmptyState } from '../../../shared/ui/EmptyState';

const barConfig = [
  { key: 'accepted', label: 'В работе', color: '#10B981' },
  { key: 'pending', label: 'Отложено', color: '#F59E0B' },
  { key: 'applied', label: 'Выполнено', color: '#22C55E' }
] as const;

export function RecommendationChart({ monthly }: { monthly: MonthlyRecommendationStats[] }) {
  const maxValue = Math.max(1, ...monthly.map((month) => month.accepted + month.pending + month.applied));

  return (
    <Card>
      <CardContent>
        <Stack spacing={3}>
          <Typography variant="h6" fontWeight={800}>
            Статистика по рекомендациям
          </Typography>

          {monthly.length === 0 ? (
            <EmptyState title="Нет данных для отображения" />
          ) : (
            <>
              <Box sx={{ display: 'flex', alignItems: 'flex-end', gap: 2, height: 280, overflowX: 'auto', pb: 1 }}>
                {monthly.map((month) => (
                  <Stack key={month.month} alignItems="center" justifyContent="flex-end" sx={{ minWidth: 84, height: '100%' }}>
                    <Stack direction="row" alignItems="flex-end" justifyContent="center" spacing={0.5} sx={{ flex: 1, width: '100%' }}>
                      {barConfig.map((bar) => {
                        const value = month[bar.key];
                        const height = Math.max(8, (value / maxValue) * 230);

                        return (
                          <Box
                            key={bar.key}
                            title={`${bar.label}: ${value}`}
                            sx={{
                              width: 18,
                              height,
                              bgcolor: bar.color,
                              borderRadius: '8px 8px 0 0'
                            }}
                          />
                        );
                      })}
                    </Stack>
                    <Typography color="text.secondary" variant="body2">
                      {month.month}
                    </Typography>
                  </Stack>
                ))}
              </Box>

              <Stack direction="row" justifyContent="center" spacing={3} flexWrap="wrap">
                {barConfig.map((bar) => (
                  <Stack direction="row" alignItems="center" gap={1} key={bar.key}>
                    <Box sx={{ width: 12, height: 12, borderRadius: 1, bgcolor: bar.color }} />
                    <Typography color="text.secondary" variant="body2">
                      {bar.label}
                    </Typography>
                  </Stack>
                ))}
              </Stack>
            </>
          )}
        </Stack>
      </CardContent>
    </Card>
  );
}


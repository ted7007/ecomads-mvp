import CheckCircleIcon from '@mui/icons-material/CheckCircle';
import CheckSquareIcon from '@mui/icons-material/CheckBox';
import HelpIcon from '@mui/icons-material/Help';
import ScheduleIcon from '@mui/icons-material/Schedule';
import TrendingDownIcon from '@mui/icons-material/TrendingDown';
import TrendingUpIcon from '@mui/icons-material/TrendingUp';
import { Card, CardContent, Grid, Stack, Typography } from '@mui/material';
import type { ReactNode } from 'react';
import type { RecommendationStatsResponse } from '../../../shared/api/apiTypes';
import { formatMoney } from '../../../shared/lib/formatMoney';

type CardItem = {
  icon: ReactNode;
  label: string;
  value: string;
};

export function RecommendationStatsCards({ stats }: { stats: RecommendationStatsResponse }) {
  const items: CardItem[] = [
    { icon: <CheckCircleIcon fontSize="small" />, label: 'В работе', value: stats.counts.accepted.toLocaleString('ru-RU') },
    { icon: <ScheduleIcon fontSize="small" />, label: 'Отложено рекомендаций', value: stats.counts.pending.toLocaleString('ru-RU') },
    { icon: <CheckSquareIcon fontSize="small" />, label: 'Выполнено', value: stats.counts.applied.toLocaleString('ru-RU') },
    { icon: <TrendingDownIcon fontSize="small" />, label: 'Ожидаемая экономия', value: formatMoney(stats.expectedSaving) },
    { icon: <TrendingUpIcon fontSize="small" />, label: 'Потенциальная выручка', value: formatMoney(stats.expectedAdditionalRevenue) },
    { icon: <HelpIcon fontSize="small" />, label: 'Без расчёта эффекта', value: stats.notCalculatedCount.toLocaleString('ru-RU') }
  ];

  return (
    <Grid container spacing={2}>
      {items.map((item) => (
        <Grid item xs={12} sm={6} lg={4} key={item.label}>
          <Card sx={{ height: '100%' }}>
            <CardContent>
              <Stack spacing={1}>
                <Stack direction="row" alignItems="center" gap={1} color="text.secondary">
                  {item.icon}
                  <Typography variant="body2">{item.label}</Typography>
                </Stack>
                <Typography variant="h4" fontWeight={800}>
                  {item.value}
                </Typography>
              </Stack>
            </CardContent>
          </Card>
        </Grid>
      ))}
    </Grid>
  );
}


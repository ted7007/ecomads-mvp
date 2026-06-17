import CheckCircleIcon from '@mui/icons-material/CheckCircle';
import CheckSquareIcon from '@mui/icons-material/CheckBox';
import ScheduleIcon from '@mui/icons-material/Schedule';
import { Card, CardContent, Grid, LinearProgress, Stack, Typography } from '@mui/material';
import type { RecommendationCounts } from '../../../shared/api/apiTypes';

export function RecommendationStatusSummary({ counts }: { counts: RecommendationCounts }) {
  const total = counts.accepted + counts.pending + counts.applied;
  const items = [
    { label: 'В работе', value: counts.accepted, icon: <CheckCircleIcon />, color: 'success.main' },
    { label: 'Отложенные', value: counts.pending, icon: <ScheduleIcon />, color: 'warning.main' },
    { label: 'Выполненные', value: counts.applied, icon: <CheckSquareIcon />, color: 'success.main' }
  ];

  return (
    <Grid container spacing={2}>
      {items.map((item) => {
        const percent = total > 0 ? Math.round((item.value / total) * 100) : 0;

        return (
          <Grid item xs={12} md={4} key={item.label}>
            <Card sx={{ height: '100%' }}>
              <CardContent>
                <Stack spacing={2}>
                  <Stack direction="row" justifyContent="space-between" alignItems="center">
                    <Typography variant="h6" fontWeight={700}>
                      {item.label}
                    </Typography>
                    <Stack sx={{ color: item.color }}>{item.icon}</Stack>
                  </Stack>
                  <Typography variant="h4" fontWeight={800} sx={{ color: item.color }}>
                    {item.value}
                  </Typography>
                  <Typography color="text.secondary">{percent}% от общего числа</Typography>
                  <LinearProgress
                    value={percent}
                    variant="determinate"
                    sx={{
                      height: 8,
                      borderRadius: 99,
                      '& .MuiLinearProgress-bar': {
                        bgcolor: item.color
                      }
                    }}
                  />
                </Stack>
              </CardContent>
            </Card>
          </Grid>
        );
      })}
    </Grid>
  );
}


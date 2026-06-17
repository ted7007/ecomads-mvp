import AttachMoneyIcon from '@mui/icons-material/AttachMoney';
import MouseIcon from '@mui/icons-material/Mouse';
import ShoppingBagIcon from '@mui/icons-material/ShoppingBag';
import TargetIcon from '@mui/icons-material/TrackChanges';
import TrendingUpIcon from '@mui/icons-material/TrendingUp';
import { Box, Card, CardContent, Stack, Typography } from '@mui/material';
import type { ReactNode } from 'react';
import type { ProjectDashboard } from '../../../shared/api/apiTypes';
import { formatMoney } from '../../../shared/lib/formatMoney';
import { formatPercent } from '../../../shared/lib/formatPercent';

type KpiItem = {
  icon: ReactNode;
  label: string;
  value: string;
};

export function CampaignKpiGrid({ campaign }: { campaign: ProjectDashboard | null }) {
  const kpi = campaign?.kpi;
  const items: KpiItem[] = [
    { icon: <AttachMoneyIcon fontSize="small" />, label: 'Заказано на сумму', value: formatMoney(kpi?.orderedAmount ?? 0) },
    { icon: <ShoppingBagIcon fontSize="small" />, label: 'Расход', value: formatMoney(kpi?.spend ?? 0) },
    { icon: <MouseIcon fontSize="small" />, label: 'Клики', value: (kpi?.clicks ?? 0).toLocaleString('ru-RU') },
    { icon: <TargetIcon fontSize="small" />, label: 'ДРР', value: formatPercent(kpi?.drr ?? 0, 1) },
    { icon: <TrendingUpIcon fontSize="small" />, label: 'CTR', value: formatPercent(kpi?.ctr ?? 0, 2) }
  ];

  return (
    <Box
      sx={{
        display: 'grid',
        gridTemplateColumns: {
          xs: '1fr',
          sm: 'repeat(2, minmax(0, 1fr))',
          lg: 'repeat(5, minmax(0, 1fr))'
        },
        gap: 2,
        width: '100%'
      }}
    >
      {items.map((item) => (
        <Box key={item.label} sx={{ minWidth: 0 }}>
          <Card sx={{ height: '100%', borderRadius: 2 }}>
            <CardContent sx={{ p: 2.5, '&:last-child': { pb: 2.5 } }}>
              <Stack spacing={1}>
                <Stack direction="row" alignItems="center" gap={1} color="text.secondary">
                  {item.icon}
                  <Typography variant="body2">{item.label}</Typography>
                </Stack>
                <Typography variant="h4" fontWeight={800} sx={{ lineHeight: 1.15 }}>
                  {item.value}
                </Typography>
              </Stack>
            </CardContent>
          </Card>
        </Box>
      ))}
    </Box>
  );
}

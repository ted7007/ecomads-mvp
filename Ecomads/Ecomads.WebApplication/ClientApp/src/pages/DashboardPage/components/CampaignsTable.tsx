import ArrowForwardIosIcon from '@mui/icons-material/ArrowForwardIos';
import { Box, Chip, MenuItem, Select, Stack, Table, TableBody, TableCell, TableContainer, TableHead, TableRow, TableSortLabel, Typography } from '@mui/material';
import { useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { appRoutes } from '../../../app/routes';
import type { ProjectDashboard } from '../../../shared/api/apiTypes';
import { EmptyState } from '../../../shared/ui/EmptyState';
import { formatMoney } from '../../../shared/lib/formatMoney';
import { formatPercent } from '../../../shared/lib/formatPercent';
import type { DashboardFilters } from '../dashboardApi';
import { PaginationBar } from '../../../shared/ui/PaginationBar';

type SortKey = 'spend' | 'revenue' | 'drr' | 'name';

export function CampaignsTable({ campaigns, filters }: { campaigns: ProjectDashboard[]; filters: DashboardFilters }) {
  const navigate = useNavigate();
  const [sort, setSort] = useState<SortKey>('spend');
  const [ascending, setAscending] = useState(false);
  const [page, setPage] = useState(0);
  const [rowsPerPage, setRowsPerPage] = useState(5);
  useEffect(() => setPage(0), [campaigns, sort, ascending, rowsPerPage]);
  if (!campaigns.length) return <EmptyState title="Кампаний нет" description="Загрузите статистику или измените период." />;

  const sorted = [...campaigns].sort((left, right) => {
    const a = sort === 'name' ? left.name : sort === 'revenue' ? left.kpi.revenue : left.kpi[sort];
    const b = sort === 'name' ? right.name : sort === 'revenue' ? right.kpi.revenue : right.kpi[sort];
    const order = typeof a === 'string' && typeof b === 'string' ? a.localeCompare(b, 'ru') : Number(a) - Number(b);
    return (ascending ? order : -order) || left.name.localeCompare(right.name, 'ru');
  });
  const visible = sorted.slice(page * rowsPerPage, (page + 1) * rowsPerPage);
  const open = (campaign: ProjectDashboard) => {
    const search = new URLSearchParams();
    if (filters.startDate) search.set('startDate', filters.startDate);
    if (filters.endDate) search.set('endDate', filters.endDate);
    navigate({ pathname: appRoutes.campaignPath(campaign.id), search: search.toString() });
  };
  const selectSort = (next: SortKey) => {
    if (next === sort) setAscending(!ascending);
    else { setSort(next); setAscending(next === 'name'); }
  };

  return <>
    <Box sx={{ display: { xs: 'block', md: 'none' } }}>
      <Stack direction="row" alignItems="center" gap={1} sx={{ mb: 1.5 }}>
        <Typography variant="body2" color="text.secondary">Сортировка</Typography>
        <Select size="small" value={sort} onChange={(event) => selectSort(event.target.value as SortKey)} sx={{ ml: 'auto', minWidth: 170 }}>
          <MenuItem value="spend">Расход</MenuItem><MenuItem value="revenue">Заказы с рекламы</MenuItem>
          <MenuItem value="drr">ДРР рекламы</MenuItem><MenuItem value="name">Название</MenuItem>
        </Select>
      </Stack>
      <Stack gap={0}>
        {visible.map((campaign) => <Box key={campaign.id} role="button" tabIndex={0} onClick={() => open(campaign)}
          onKeyDown={(event) => { if (event.key === 'Enter') open(campaign); }}
          sx={{ p: 1.5, borderBottom: '1px solid', borderColor: 'divider', cursor: 'pointer' }}>
          <Stack direction="row" alignItems="center" gap={1}>
            <Typography fontWeight={700} sx={{ flex: 1, minWidth: 0 }} noWrap>{campaign.name}</Typography>
            <ArrowForwardIosIcon sx={{ fontSize: 13, color: 'text.secondary' }} />
          </Stack>
          <Stack direction="row" justifyContent="space-between" sx={{ mt: .7 }}>
            <Typography variant="body2">Расход {formatMoney(campaign.kpi.spend)}</Typography>
            <Typography variant="body2" fontWeight={700}>{campaign.kpi.revenue > 0 ? formatPercent(campaign.kpi.drr, 1) : 'ДРР —'}</Typography>
          </Stack>
          <Typography variant="caption" color="text.secondary">Заказы с рекламы {formatMoney(campaign.kpi.revenue)} · дней {campaign.kpi.coverageDays}/{campaign.kpi.expectedDays}</Typography>
        </Box>)}
      </Stack>
    </Box>
    <TableContainer sx={{ display: { xs: 'none', md: 'block' }, overflowX: 'auto' }}>
      <Table size="small" sx={{ minWidth: 780 }} aria-label="Рекламные кампании">
        <TableHead><TableRow>
          {[['name', 'Кампания'], ['spend', 'Расход'], ['revenue', 'Заказы с рекламы'], ['drr', 'ДРР рекламы']].map(([key, label]) =>
            <TableCell key={key} align={key === 'name' ? 'left' : 'right'} sortDirection={sort === key ? ascending ? 'asc' : 'desc' : false}>
              <TableSortLabel active={sort === key} direction={sort === key && ascending ? 'asc' : 'desc'} onClick={() => selectSort(key as SortKey)}>{label}</TableSortLabel>
            </TableCell>)}
          <TableCell align="right">CTR</TableCell><TableCell align="right">Покрытие</TableCell>
        </TableRow></TableHead>
        <TableBody>{visible.map((campaign) => <TableRow key={campaign.id} hover>
          <TableCell><Typography component="a" href={`${appRoutes.campaignPath(campaign.id)}?startDate=${filters.startDate}&endDate=${filters.endDate}`}
            fontWeight={700} color="text.primary" sx={{ textDecoration: 'none', '&:hover': { color: 'primary.main' } }}>{campaign.name}</Typography>
            <Chip size="small" variant="outlined" color={campaign.wbStatus === 9 ? 'success' : 'default'} label={campaign.wbStatus === 9 ? 'Активна' : campaign.wbStatus === 11 ? 'Приостановлена' : 'Кампания WB'} sx={{ mt: .5 }} /></TableCell>
          <TableCell align="right">{formatMoney(campaign.kpi.spend)}</TableCell>
          <TableCell align="right">{formatMoney(campaign.kpi.revenue)}</TableCell>
          <TableCell align="right">{campaign.kpi.revenue > 0 ? formatPercent(campaign.kpi.drr, 1) : '—'}
            {campaign.kpi.revenue > 0 && campaign.kpi.drr > campaign.targetDrr ? <Chip size="small" color="warning" label="Выше цели" sx={{ ml: 1 }} /> : null}</TableCell>
          <TableCell align="right">{campaign.kpi.impressions > 0 ? formatPercent(campaign.kpi.ctr, 2) : '—'}</TableCell>
          <TableCell align="right">{campaign.kpi.coverageDays}/{campaign.kpi.expectedDays}</TableCell>
        </TableRow>)}</TableBody>
      </Table>
    </TableContainer>
    <PaginationBar count={campaigns.length} page={page} rowsPerPage={rowsPerPage}
      onPageChange={setPage} onRowsPerPageChange={(size) => { setRowsPerPage(size); setPage(0); }} />
  </>;
}

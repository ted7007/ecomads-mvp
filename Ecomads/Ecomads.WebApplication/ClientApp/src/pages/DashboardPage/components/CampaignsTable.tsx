import SwapVertIcon from '@mui/icons-material/SwapVert';
import { Box, Button, Chip, IconButton, MenuItem, Select, Stack, Table, TableBody, TableCell, TableContainer, TableHead, TableRow, TableSortLabel, Tooltip, Typography } from '@mui/material';
import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { appRoutes } from '../../../app/routes';
import { lightUiTokens as ui } from '../../../app/lightUiTokens';
import type { ProjectDashboard } from '../../../shared/api/apiTypes';
import { EmptyState } from '../../../shared/ui/EmptyState';
import { formatMoney } from '../../../shared/lib/formatMoney';
import { formatPercent } from '../../../shared/lib/formatPercent';
import type { DashboardFilters } from '../dashboardApi';
import { PaginationBar } from '../../../shared/ui/PaginationBar';
import { completeKpi } from '../../../shared/lib/kpiComparison';
import { campaignStatus, campaignValue, sortCampaigns } from './campaignRows';
import type { CampaignSortKey } from './campaignRows';

const columns: { key: CampaignSortKey; label: string; width: string }[] = [
  { key: 'name', label: 'Кампания', width: '23%' }, { key: 'status', label: 'Статус', width: '12%' },
  { key: 'spend', label: 'Расход', width: '12%' }, { key: 'delta', label: 'К пред. пер.', width: '10%' },
  { key: 'revenue', label: 'Заказы с рекламы, ₽', width: '15%' }, { key: 'drr', label: 'ДРР рекламы', width: '11%' },
  { key: 'ctr', label: 'CTR', width: '8%' }, { key: 'cpc', label: 'CPC', width: '9%' }
];

export function CampaignsTable({ campaigns, priorCampaigns, filters, hasStores = true }: {
  campaigns: ProjectDashboard[]; priorCampaigns: ProjectDashboard[]; filters: DashboardFilters; hasStores?: boolean;
}) {
  const [sort, setSort] = useState<CampaignSortKey>('spend');
  const [ascending, setAscending] = useState(false);
  const [page, setPage] = useState(0);
  const [rowsPerPage, setRowsPerPage] = useState(5);
  useEffect(() => setPage(0), [campaigns, sort, ascending, rowsPerPage, filters.startDate, filters.endDate]);
  const safePage = Math.min(page, Math.max(0, Math.ceil(campaigns.length / rowsPerPage) - 1));
  const priorById = new Map(priorCampaigns.map(campaign => [campaign.id, campaign]));
  const visible = sortCampaigns(campaigns, priorCampaigns, sort, ascending).slice(safePage * rowsPerPage, (safePage + 1) * rowsPerPage);
  const selectSort = (next: CampaignSortKey) => {
    if (next === sort) setAscending(!ascending);
    else { setSort(next); setAscending(next === 'name' || next === 'status'); }
    setPage(0);
  };
  const href = (campaign: ProjectDashboard) => {
    const search = new URLSearchParams();
    if (filters.startDate) search.set('startDate', filters.startDate);
    if (filters.endDate) search.set('endDate', filters.endDate);
    return `${appRoutes.campaignPath(campaign.id)}?${search}`;
  };
  const value = (campaign: ProjectDashboard, key: CampaignSortKey) => campaignValue(campaign, key, priorById.get(campaign.id));
  const money = (campaign: ProjectDashboard, key: CampaignSortKey) => {
    const amount = value(campaign, key);
    return typeof amount === 'number' ? formatMoney(amount) : '—';
  };
  const percent = (campaign: ProjectDashboard, key: CampaignSortKey) => {
    const amount = value(campaign, key);
    return typeof amount === 'number' ? formatPercent(amount, key === 'ctr' ? 2 : 1) : '—';
  };
  const breach = (campaign: ProjectDashboard, key: 'drr' | 'ctr') => completeKpi(campaign) && value(campaign, key) !== null &&
    (key === 'drr' ? campaign.kpi.drr > campaign.targetDrr : campaign.kpi.ctr < campaign.minCtr);
  const status = (campaign: ProjectDashboard) => <Chip size="small" label={campaignStatus(campaign.wbStatus)}
    sx={{ ...(campaign.wbStatus === 9 ? ui.status.success : ui.status.neutral), height: 22, maxWidth: '100%',
      borderRadius: ui.radius.badge, fontSize: 11, '& .MuiChip-label': { px: .75 } }} />;

  if (!campaigns.length) return <Stack alignItems="center" gap={1.5}>
    <EmptyState title={hasStores ? 'За выбранный период кампаний нет' : 'Подключите кабинет WB'}
      description={hasStores ? 'Измените период или проверьте загрузку данных из WB.' : 'После подключения здесь появятся рекламные кампании и их показатели.'} />
    <Button component={Link} to={appRoutes.wbStores} variant="contained">{hasStores ? 'Проверить загрузку' : 'Подключить кабинет WB'}</Button>
  </Stack>;

  return <>
    <Box sx={{ display: { xs: 'block', md: 'none' } }}>
      <Stack direction="row" alignItems="center" gap={.5} sx={{ mb: 1.5 }}>
        <Typography variant="body2" color="text.secondary">Сортировка</Typography>
        <Select size="small" value={sort} inputProps={{ 'aria-label': 'Сортировка кампаний' }} onChange={event => selectSort(event.target.value as CampaignSortKey)} sx={{ ml: 'auto', flex: 1, minWidth: 0 }}>
          {columns.map(column => <MenuItem value={column.key} key={column.key}>{column.label}</MenuItem>)}
        </Select>
        <IconButton onClick={() => { setAscending(!ascending); setPage(0); }}
          aria-label={`Сортировать по ${ascending ? 'убыванию' : 'возрастанию'}`} title={ascending ? 'По возрастанию ↑' : 'По убыванию ↓'}>
          <SwapVertIcon /><Typography variant="caption" aria-hidden>{ascending ? '↑' : '↓'}</Typography>
        </IconButton>
      </Stack>
      {visible.map(campaign => <Box key={campaign.id} component={Link} to={href(campaign)}
        sx={{ display: 'flex', alignItems: 'center', gap: 1, py: 1.5, borderBottom: '1px solid', borderColor: 'divider',
          color: 'text.primary', textDecoration: 'none', '&:focus-visible': ui.focus }}>
        <Stack spacing={.4} sx={{ flex: 1, minWidth: 0 }}>
          <Typography fontWeight={700} sx={{ overflowWrap: 'anywhere' }}>{campaign.name}</Typography>
          <Typography variant="caption" color="text.secondary">{money(campaign, 'spend')} · {money(campaign, 'revenue')}</Typography>
          <Box>{status(campaign)}</Box>
          {!completeKpi(campaign) ? <Typography variant="caption" color="text.secondary">Данные: {campaign.kpi.coverageDays} из {campaign.kpi.expectedDays} дней</Typography> : null}
        </Stack>
        <Typography fontWeight={700} color={breach(campaign, 'drr') ? 'warning.main' : 'text.primary'}>{percent(campaign, 'drr')}</Typography>
      </Box>)}
    </Box>
    <TableContainer sx={{ display: { xs: 'none', md: 'block' }, overflowX: 'auto' }}>
      <Table size="small" sx={{ width: '100%', tableLayout: 'fixed', '& .MuiTableCell-root': { px: 1.25 } }} aria-label="Рекламные кампании">
        <TableHead><TableRow>{columns.map(column => <TableCell key={column.key} sx={{ width: column.width }}
          align={column.key === 'name' || column.key === 'status' ? 'left' : 'right'} sortDirection={sort === column.key ? ascending ? 'asc' : 'desc' : false}>
          <TableSortLabel active={sort === column.key} direction={sort === column.key && ascending ? 'asc' : 'desc'}
            onClick={() => selectSort(column.key)} aria-label={`Сортировать: ${column.label}`}>{column.label}</TableSortLabel>
        </TableCell>)}</TableRow></TableHead>
        <TableBody>{visible.map(campaign => {
          const delta = value(campaign, 'delta');
          return <TableRow key={campaign.id} hover>
            <TableCell><Typography component={Link} to={href(campaign)} fontWeight={700} color="text.primary"
              sx={{ textDecoration: 'none', overflowWrap: 'anywhere', '&:hover': { color: 'primary.main' } }}>{campaign.name}</Typography>
              {!completeKpi(campaign) ? <Typography variant="caption" color="text.secondary" display="block">Данные: {campaign.kpi.coverageDays} из {campaign.kpi.expectedDays} дней</Typography> : null}</TableCell>
            <TableCell>{status(campaign)}</TableCell>
            <TableCell align="right" sx={{ fontWeight: 700 }}>{money(campaign, 'spend')}</TableCell>
            <TableCell align="right" sx={{ color: 'text.secondary' }}>{typeof delta === 'number' ? `${delta > 0 ? '+' : ''}${formatPercent(delta, 0)}` : '—'}</TableCell>
            <TableCell align="right">{money(campaign, 'revenue')}</TableCell>
            <TableCell align="right" sx={{ fontWeight: breach(campaign, 'drr') ? 700 : undefined, color: breach(campaign, 'drr') ? 'warning.main' : undefined }}>
              <Tooltip title={breach(campaign, 'drr') ? `Выше нормы ${formatPercent(campaign.targetDrr, 1)}` : ''}><span>{percent(campaign, 'drr')}</span></Tooltip></TableCell>
            <TableCell align="right" sx={{ fontWeight: breach(campaign, 'ctr') ? 700 : undefined, color: breach(campaign, 'ctr') ? 'warning.main' : undefined }}>
              <Tooltip title={breach(campaign, 'ctr') ? `Ниже нормы ${formatPercent(campaign.minCtr, 1)}` : ''}><span>{percent(campaign, 'ctr')}</span></Tooltip></TableCell>
            <TableCell align="right">{money(campaign, 'cpc')}</TableCell>
          </TableRow>;
        })}</TableBody>
      </Table>
    </TableContainer>
    <PaginationBar desktopSizeButtons count={campaigns.length} page={safePage} rowsPerPage={rowsPerPage}
      onPageChange={setPage} onRowsPerPageChange={size => { setRowsPerPage(size); setPage(0); }} />
  </>;
}

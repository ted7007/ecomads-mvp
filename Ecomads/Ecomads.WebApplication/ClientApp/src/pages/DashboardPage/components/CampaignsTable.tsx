import VisibilityIcon from '@mui/icons-material/Visibility';
import { Chip, IconButton, Table, TableBody, TableCell, TableContainer, TableHead, TableRow, Typography } from '@mui/material';
import { useNavigate } from 'react-router-dom';
import { appRoutes } from '../../../app/routes';
import type { ProjectDashboard } from '../../../shared/api/apiTypes';
import { EmptyState } from '../../../shared/ui/EmptyState';
import { formatMoney } from '../../../shared/lib/formatMoney';
import { formatPercent } from '../../../shared/lib/formatPercent';
import type { DashboardFilters } from '../dashboardApi';

export function CampaignsTable({ campaigns, filters }: { campaigns: ProjectDashboard[]; filters: DashboardFilters }) {
  const navigate = useNavigate();

  if (campaigns.length === 0) {
    return <EmptyState title="Кампаний нет" description="Загрузите статистику или измените период." />;
  }

  const sortedCampaigns = [...campaigns].sort((left, right) =>
    Number(right.kpi.coverageDays > 0) - Number(left.kpi.coverageDays > 0) ||
    right.kpi.spend - left.kpi.spend || left.name.localeCompare(right.name, 'ru'));

  return (
    <TableContainer>
      <Table>
        <TableHead>
          <TableRow>
            <TableCell>Название</TableCell>
            <TableCell align="right">Расход</TableCell>
            <TableCell align="right">ДРР рекламы</TableCell>
            <TableCell align="right">Цель ДРР</TableCell>
            <TableCell align="right">Клики</TableCell>
            <TableCell align="right">CTR</TableCell>
            <TableCell align="center">Действие</TableCell>
          </TableRow>
        </TableHead>
        <TableBody>
          {sortedCampaigns.map((campaign) => (
            <TableRow hover key={campaign.id}>
              <TableCell>
                <Typography fontWeight={600}>{campaign.name}</Typography>
                {campaign.kpi.coverageDays < campaign.kpi.expectedDays ?
                  <Typography variant="caption" color="warning.main">Данные: {campaign.kpi.coverageDays}/{campaign.kpi.expectedDays} дней</Typography> : null}
              </TableCell>
              <TableCell align="right">{formatMoney(campaign.kpi.spend)}</TableCell>
              <TableCell align="right">{campaign.kpi.revenue > 0 ? formatPercent(campaign.kpi.drr, 1) : '—'}</TableCell>
              <TableCell align="right">{campaign.kpi.revenue > 0 && campaign.kpi.drr > campaign.targetDrr
                ? <Chip color="warning" size="small" variant="outlined" label={formatPercent(campaign.targetDrr, 1)} />
                : formatPercent(campaign.targetDrr, 1)}</TableCell>
              <TableCell align="right">{campaign.kpi.clicks.toLocaleString('ru-RU')}</TableCell>
              <TableCell align="right">{formatPercent(campaign.kpi.ctr, 2)}</TableCell>
              <TableCell align="center">
                <IconButton aria-label={`Открыть кампанию ${campaign.name}`} onClick={() => {
                  const search = new URLSearchParams();
                  if (filters.startDate) search.set('startDate', filters.startDate);
                  if (filters.endDate) search.set('endDate', filters.endDate);
                  navigate({ pathname: appRoutes.campaignPath(campaign.id), search: search.toString() });
                }}>
                  <VisibilityIcon />
                </IconButton>
              </TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </TableContainer>
  );
}


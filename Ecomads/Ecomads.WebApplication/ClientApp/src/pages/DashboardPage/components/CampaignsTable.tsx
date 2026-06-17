import VisibilityIcon from '@mui/icons-material/Visibility';
import { IconButton, Table, TableBody, TableCell, TableContainer, TableHead, TableRow, Typography } from '@mui/material';
import { useNavigate } from 'react-router-dom';
import { appRoutes } from '../../../app/routes';
import type { ProjectDashboard } from '../../../shared/api/apiTypes';
import { EmptyState } from '../../../shared/ui/EmptyState';
import { formatMoney } from '../../../shared/lib/formatMoney';
import { formatPercent } from '../../../shared/lib/formatPercent';

export function CampaignsTable({ campaigns }: { campaigns: ProjectDashboard[] }) {
  const navigate = useNavigate();

  if (campaigns.length === 0) {
    return <EmptyState title="Кампаний нет" description="Загрузите статистику или измените период." />;
  }

  return (
    <TableContainer>
      <Table>
        <TableHead>
          <TableRow>
            <TableCell>Название</TableCell>
            <TableCell align="right">Расход</TableCell>
            <TableCell align="right">ДРР</TableCell>
            <TableCell align="right">Клики</TableCell>
            <TableCell align="right">CTR</TableCell>
            <TableCell align="center">Действие</TableCell>
          </TableRow>
        </TableHead>
        <TableBody>
          {campaigns.map((campaign) => (
            <TableRow hover key={campaign.id}>
              <TableCell>
                <Typography fontWeight={600}>{campaign.name}</Typography>
              </TableCell>
              <TableCell align="right">{formatMoney(campaign.kpi.spend)}</TableCell>
              <TableCell align="right">{formatPercent(campaign.kpi.drr, 1)}</TableCell>
              <TableCell align="right">{campaign.kpi.clicks.toLocaleString('ru-RU')}</TableCell>
              <TableCell align="right">{formatPercent(campaign.kpi.ctr, 2)}</TableCell>
              <TableCell align="center">
                <IconButton aria-label={`Открыть кампанию ${campaign.name}`} onClick={() => navigate(appRoutes.campaignPath(campaign.id))}>
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


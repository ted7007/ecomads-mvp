import { Chip, Table, TableBody, TableCell, TableContainer, TableHead, TableRow, Typography } from '@mui/material';
import type { RecommendationDetail } from '../../../shared/api/apiTypes';
import { EmptyState } from '../../../shared/ui/EmptyState';
import { formatActualEffect, formatExpectedEffect, getRecommendationTitle, getStatusView } from '../reportFormatters';

export function RecommendationsTable({ recommendations }: { recommendations: RecommendationDetail[] }) {
  if (recommendations.length === 0) {
    return <EmptyState title="Рекомендаций нет" description="За выбранный период нет рекомендаций для отображения." />;
  }

  return (
    <TableContainer>
      <Table>
        <TableHead>
          <TableRow>
            <TableCell>Рекомендация</TableCell>
            <TableCell>Действие</TableCell>
            <TableCell>Статус</TableCell>
            <TableCell>Ожидаемый эффект</TableCell>
            <TableCell>Фактический эффект</TableCell>
            <TableCell>Период</TableCell>
            <TableCell>Кампания</TableCell>
            <TableCell>Комментарий</TableCell>
          </TableRow>
        </TableHead>
        <TableBody>
          {recommendations.map((recommendation) => {
            const status = getStatusView(recommendation.status);

            return (
              <TableRow hover key={recommendation.id}>
                <TableCell>
                  <Typography fontWeight={600}>{getRecommendationTitle(recommendation)}</Typography>
                </TableCell>
                <TableCell>{recommendation.action || '—'}</TableCell>
                <TableCell>
                  <Chip color={status.color} label={status.label} size="small" />
                </TableCell>
                <TableCell>{formatExpectedEffect(recommendation)}</TableCell>
                <TableCell>{formatActualEffect(recommendation)}</TableCell>
                <TableCell>{recommendation.period || '—'}</TableCell>
                <TableCell>{recommendation.campaign || 'Не указана'}</TableCell>
                <TableCell>{recommendation.comment || '—'}</TableCell>
              </TableRow>
            );
          })}
        </TableBody>
      </Table>
    </TableContainer>
  );
}


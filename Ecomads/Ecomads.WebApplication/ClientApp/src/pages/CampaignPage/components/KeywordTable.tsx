import { Box, Table, TableBody, TableCell, TableContainer, TableHead, TableRow, TableSortLabel, Typography } from '@mui/material';
import type { KeywordRecommendationRow } from '../campaignSchemas';
import { getDecisionLabel, getDrrColor, getStatusLabel, getStatusStyle } from '../campaignFormatters';
import { EmptyState } from '../../../shared/ui/EmptyState';
import { formatMoney } from '../../../shared/lib/formatMoney';
import { formatPercent } from '../../../shared/lib/formatPercent';

export type KeywordSortField = keyof Pick<
  KeywordRecommendationRow,
  'phrase' | 'status' | 'shortRecommendation' | 'views' | 'clicks' | 'ctr' | 'spend' | 'orders' | 'revenue' | 'drr'
>;

export type KeywordSort = {
  field: KeywordSortField | '';
  direction: 'asc' | 'desc';
};

type KeywordTableProps = {
  rows: KeywordRecommendationRow[];
  selectedInsightId: string;
  selectedKeywordId: string;
  sort: KeywordSort;
  onSortChange: (field: KeywordSortField) => void;
  onSelect: (row: KeywordRecommendationRow) => void;
};

const columns: Array<{ field: KeywordSortField; label: string; align?: 'left' | 'right' }> = [
  { field: 'phrase', label: 'Фраза' },
  { field: 'status', label: 'Статус' },
  { field: 'shortRecommendation', label: 'Рекомендация' },
  { field: 'views', label: 'Показы', align: 'right' },
  { field: 'clicks', label: 'Клики', align: 'right' },
  { field: 'ctr', label: 'CTR', align: 'right' },
  { field: 'spend', label: 'Затраты', align: 'right' },
  { field: 'orders', label: 'Заказы', align: 'right' },
  { field: 'revenue', label: 'Выручка', align: 'right' },
  { field: 'drr', label: 'ДРР' }
];

export function KeywordTable({ rows, selectedInsightId, selectedKeywordId, sort, onSortChange, onSelect }: KeywordTableProps) {
  if (rows.length === 0) {
    return <EmptyState title="Нет данных за выбранный период" />;
  }

  return (
    <TableContainer sx={{ maxHeight: 680, overflowX: 'auto' }}>
      <Table stickyHeader size="small" sx={{ minWidth: 1260, tableLayout: 'fixed' }}>
        <TableHead>
          <TableRow>
            {columns.map((column) => (
              <TableCell
                align={column.align}
                key={column.field}
                sx={{
                  bgcolor: '#F8FAFC',
                  color: '#0F172A',
                  fontWeight: 800,
                  width:
                    column.field === 'phrase'
                      ? 260
                      : column.field === 'status'
                        ? 180
                        : column.field === 'shortRecommendation'
                          ? 145
                          : column.field === 'drr'
                            ? 130
                            : 95
                    ,
                  pr: column.field === 'drr' ? 3.5 : undefined
                }}
              >
                <TableSortLabel
                  active={sort.field === column.field}
                  direction={sort.field === column.field ? sort.direction : 'asc'}
                  onClick={() => onSortChange(column.field)}
                >
                  {column.label}
                </TableSortLabel>
              </TableCell>
            ))}
          </TableRow>
        </TableHead>
        <TableBody>
          {rows.map((row) => {
            const selected = row.keywordId === selectedKeywordId || Boolean(row.mainInsightId && row.mainInsightId === selectedInsightId);
            const statusStyle = getStatusStyle(row.status);

            return (
              <TableRow
                hover
                selected={selected}
                key={`${row.keywordId}-${row.mainInsightId || row.phrase}`}
                onClick={() => onSelect(row)}
                sx={{
                  cursor: 'pointer',
                  bgcolor: statusStyle.rowBg,
                  '&:hover': { bgcolor: '#F1F5F9' },
                  '&.Mui-selected': {
                    bgcolor: statusStyle.rowBg,
                    boxShadow: 'inset 3px 0 0 #FF6B4A'
                  },
                  '& .MuiTableCell-root': {
                    borderColor: 'rgba(203, 213, 225, 0.65)',
                    py: 1.25
                  }
                }}
              >
                <TableCell>
                  <Typography fontWeight={800} sx={{ whiteSpace: 'normal', lineHeight: 1.25 }}>
                    {row.phrase}
                  </Typography>
                </TableCell>
                <TableCell>
                  <StatusBadge status={row.status} />
                  {row.decisionStatus !== 'None' ? (
                    <Typography color="text.secondary" variant="caption" display="block">
                      {getDecisionLabel(row.decisionStatus)}
                    </Typography>
                  ) : null}
                </TableCell>
                <TableCell>
                  <Typography color="text.secondary" fontSize={13} fontWeight={700} noWrap>
                    {row.shortRecommendation || '—'}
                  </Typography>
                </TableCell>
                <TableCell align="right">{(row.views ?? 0).toLocaleString('ru-RU')}</TableCell>
                <TableCell align="right">{(row.clicks ?? 0).toLocaleString('ru-RU')}</TableCell>
                <TableCell align="right">{formatPercent(row.ctr, 2)}</TableCell>
                <TableCell align="right">{formatMoney(row.spend ?? 0)}</TableCell>
                <TableCell align="right">{(row.orders ?? 0).toLocaleString('ru-RU')}</TableCell>
                <TableCell align="right">{formatMoney(row.revenue ?? 0)}</TableCell>
                <TableCell sx={{ color: getDrrColor(row.drr), fontSize: 13, fontWeight: 800, pl: 2.5, pr: 3.5, whiteSpace: 'nowrap' }}>
                  {formatPercent(row.drr, 1)}
                </TableCell>
              </TableRow>
            );
          })}
        </TableBody>
      </Table>
    </TableContainer>
  );
}

function StatusBadge({ status }: { status: string }) {
  const style = getStatusStyle(status);

  return (
    <Box
      component="span"
      sx={{
        display: 'inline-flex',
        alignItems: 'center',
        gap: '7px',
        minHeight: 28,
        px: '9px',
        py: '4px',
        borderRadius: 999,
        border: `1px solid ${style.border}`,
        color: style.text,
        bgcolor: style.bg,
        fontSize: 12,
        fontWeight: 800,
        whiteSpace: 'nowrap'
      }}
    >
      <Box component="span" sx={{ width: 8, height: 8, borderRadius: '50%', bgcolor: style.dot }} />
      {getStatusLabel(status)}
    </Box>
  );
}

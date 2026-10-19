import SwapVertIcon from '@mui/icons-material/SwapVert';
import { Box, ButtonBase, IconButton, MenuItem, Select, Stack, Table, TableBody, TableCell, TableContainer, TableHead, TableRow, TableSortLabel, Typography } from '@mui/material';
import type { SxProps, Theme } from '@mui/material';
import { useEffect, useState } from 'react';
import type { ReactNode } from 'react';
import { sortRows } from '../../../shared/lib/sortRows';
import type { SortValue } from '../../../shared/lib/sortRows';
import { PaginationBar } from '../../../shared/ui/PaginationBar';
import { DetailsDrawer } from './DetailsDrawer';

export type ReportColumn<T> = {
  key: string; label: string; value: (row: T) => SortValue; render: (row: T) => ReactNode;
  text?: boolean; sortable?: boolean; width?: string; sx?: SxProps<Theme>;
};

export function ReportTable<T>({ rows, columns, rowId, label, resetKey, defaultSort = 'spend', mobile, details }: {
  rows: T[]; columns: ReportColumn<T>[]; rowId: (row: T) => string; label: string; resetKey?: string;
  defaultSort?: string; mobile: (row: T) => ReactNode; details?: (row: T) => ReactNode;
}) {
  const [sort, setSort] = useState(defaultSort);
  const [ascending, setAscending] = useState(false);
  const [page, setPage] = useState(0);
  const [size, setSize] = useState(5);
  const [openedId, setOpenedId] = useState<string | null>(null);
  useEffect(() => { setPage(0); setOpenedId(null); }, [resetKey]);
  const column = columns.find(c => c.key === sort) ?? columns.find(c => c.sortable !== false)!;
  const sorted = sortRows(rows, column.value, ascending, rowId);
  const safePage = Math.min(page, Math.max(0, Math.ceil(rows.length / size) - 1));
  const visible = sorted.slice(safePage * size, (safePage + 1) * size);
  const opened = rows.find(row => rowId(row) === openedId);
  const changeSort = (key: string) => { setAscending(sort === key ? !ascending : !!columns.find(c => c.key === key)?.text); setSort(key); setPage(0); };
  return <>
    <Stack sx={{ display: { xs: 'flex', md: 'none' } }} spacing={1.5}>
      <Stack direction="row" alignItems="center" gap={.5}>
        <Typography variant="caption" color="text.secondary">Сортировка</Typography>
        <Select size="small" value={column.key} inputProps={{ 'aria-label': `Сортировка: ${label}` }} onChange={e => changeSort(e.target.value)} sx={{ flex: 1, minWidth: 0 }}>
          {columns.filter(c => c.sortable !== false).map(c => <MenuItem key={c.key} value={c.key}>{c.label}</MenuItem>)}
        </Select>
        <IconButton aria-label={`Сортировать ${label} по ${ascending ? 'убыванию' : 'возрастанию'}`} onClick={() => { setAscending(!ascending); setPage(0); }}><SwapVertIcon /></IconButton>
      </Stack>
      {visible.map(row => <Box key={rowId(row)} sx={{ borderBottom: '1px solid', borderColor: 'divider', pb: 1.5 }}>
        {details ? <ButtonBase component="div" role="button" tabIndex={0} aria-label={`Открыть ${label}: ${columns[0].value(row) ?? rowId(row)}`} onClick={() => setOpenedId(rowId(row))} sx={{ width: '100%', display: 'block', textAlign: 'left', borderRadius: 1 }}>{mobile(row)}</ButtonBase> : mobile(row)}
      </Box>)}
    </Stack>
    <TableContainer sx={{ display: { xs: 'none', md: 'block' }, overflowX: 'auto' }}>
      <Table size="small" aria-label={label} sx={{ width: '100%', tableLayout: 'fixed', minWidth: columns.length > 8 ? 1100 : undefined, '& .MuiTableCell-head': { overflowWrap: 'normal', '& .MuiTableSortLabel-root': { overflowWrap: 'normal' } }, '& .MuiTableCell-root': { px: 1, py: 1.25, overflowWrap: 'normal' } }}>
        <TableHead><TableRow>{columns.map(c => <TableCell key={c.key} align={c.text ? 'left' : 'right'}
          sx={[{ width: c.width, overflowWrap: 'normal', fontSize: 11, '& .MuiTableSortLabel-root': { display: 'inline', position: 'relative' }, '& .MuiTableSortLabel-icon': { width: 12, height: 12, ml: 0, mr: 0, position: 'absolute', right: -12, top: 0 } }, ...(Array.isArray(c.sx) ? c.sx : [c.sx ?? {}])]} sortDirection={sort === c.key ? ascending ? 'asc' : 'desc' : false}>
          {c.sortable === false ? c.label : <TableSortLabel active={sort === c.key} direction={sort === c.key && ascending ? 'asc' : 'desc'}
            onClick={() => changeSort(c.key)} aria-label={`Сортировать ${label}: ${c.label}`}>{c.label}</TableSortLabel>}
        </TableCell>)}</TableRow></TableHead>
        <TableBody>{visible.map(row => <TableRow key={rowId(row)} hover>{columns.map((c, index) => <TableCell key={c.key} align={c.text ? 'left' : 'right'} sx={c.sx}>
          {index === 0 && details ? <ButtonBase component="div" role="button" tabIndex={0} aria-label={`Открыть ${label}: ${c.value(row) ?? rowId(row)}`} onClick={() => setOpenedId(rowId(row))} sx={{ width: '100%', display: 'block', textAlign: 'left', borderRadius: 1 }}>{c.render(row)}</ButtonBase> : c.render(row)}
        </TableCell>)}</TableRow>)}</TableBody>
      </Table>
    </TableContainer>
    <PaginationBar desktopSizeButtons count={rows.length} page={safePage} rowsPerPage={size} onPageChange={setPage} onRowsPerPageChange={next => { setSize(next); setPage(0); }} />
    <DetailsDrawer open={!!opened && !!details} onClose={() => setOpenedId(null)} title={label}>{opened && details ? details(opened) : null}</DetailsDrawer>
  </>;
}

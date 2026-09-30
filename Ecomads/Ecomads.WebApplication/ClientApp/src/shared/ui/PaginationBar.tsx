import { MenuItem, Pagination, Select, Stack, Typography, useMediaQuery } from '@mui/material';

export function PaginationBar({ count, page, rowsPerPage, onPageChange, onRowsPerPageChange }: {
  count: number; page: number; rowsPerPage: number;
  onPageChange: (page: number) => void; onRowsPerPageChange: (size: number) => void;
}) {
  const mobile = useMediaQuery('(max-width:600px)');
  const pages = Math.max(1, Math.ceil(count / rowsPerPage));
  const from = count ? page * rowsPerPage + 1 : 0;
  const to = Math.min(count, (page + 1) * rowsPerPage);
  return <Stack direction="row" alignItems="center" justifyContent="space-between" gap={mobile ? .5 : 1} sx={{ pt: 1.5, minWidth: 0 }}>
    <Stack direction="row" alignItems="center" gap={mobile ? .5 : 1}>
      <Typography variant="caption" color="text.secondary">{mobile ? 'Строк' : 'Строк на странице'}</Typography>
      <Select size="small" value={rowsPerPage} aria-label="Строк на странице" onChange={(e) => onRowsPerPageChange(Number(e.target.value))}
        sx={{ height: 34, borderRadius: '8px', '& .MuiSelect-select': { py: 0.7 } }}>
        {[5, 10, 20, 100].map((size) => <MenuItem value={size} key={size}>{size}</MenuItem>)}
      </Select>
    </Stack>
    <Stack direction="row" alignItems="center" gap={mobile ? .25 : 1} sx={{ ml: 'auto', minWidth: 0 }}>
      <Typography variant="caption" color="text.secondary">{from}–{to} из {count}</Typography>
      <Pagination size="small" count={pages} page={Math.min(page + 1, pages)} onChange={(_, next) => onPageChange(next - 1)}
        siblingCount={0} boundaryCount={mobile ? 0 : 1} sx={{ '& .MuiPaginationItem-root': { borderRadius: '8px', minWidth: mobile ? 26 : 32, height: mobile ? 26 : 32, mx: mobile ? 0 : .25 } }} />
    </Stack>
  </Stack>;
}

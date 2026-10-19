import { Button, MenuItem, Pagination, Select, Stack, Typography, useMediaQuery } from '@mui/material';
import { lightUiTokens as ui } from '../../app/lightUiTokens';

export function PaginationBar({ count, page, rowsPerPage, onPageChange, onRowsPerPageChange, desktopSizeButtons = false }: {
  count: number; page: number; rowsPerPage: number;
  onPageChange: (page: number) => void; onRowsPerPageChange: (size: number) => void;
  desktopSizeButtons?: boolean;
}) {
  const mobile = useMediaQuery('(max-width:600px)');
  const pages = Math.max(1, Math.ceil(count / rowsPerPage));
  const from = count ? page * rowsPerPage + 1 : 0;
  const to = Math.min(count, (page + 1) * rowsPerPage);
  return <Stack direction="row" alignItems="center" justifyContent="space-between" gap={mobile ? .5 : 1} sx={{ pt: 1.5, minWidth: 0 }}>
    <Stack direction="row" alignItems="center" gap={mobile ? .5 : 1}>
      <Typography variant="caption" color="text.secondary">{mobile ? 'Строк' : 'Строк на странице'}</Typography>
      {desktopSizeButtons && !mobile ? <Stack direction="row" gap={.5}>{[5, 10, 20, 100].map(size =>
        <Button key={size} size="small" variant={size === rowsPerPage ? 'contained' : 'outlined'} aria-pressed={size === rowsPerPage}
          aria-label={`${size} строк на странице`} onClick={() => onRowsPerPageChange(size)}
          sx={{ minWidth: 32, px: 1, borderRadius: ui.radius.pagination }}>{size}</Button>)}</Stack> :
      <Select size="small" value={rowsPerPage} aria-label="Строк на странице" onChange={(e) => onRowsPerPageChange(Number(e.target.value))}
        sx={{ minHeight: '34px !important', height: 34, borderRadius: ui.radius.control, '& .MuiSelect-select': { py: 0.7 } }}>
        {[5, 10, 20, 100].map((size) => <MenuItem value={size} key={size}>{size}</MenuItem>)}
      </Select>}
    </Stack>
    <Stack direction="row" alignItems="center" gap={mobile ? .25 : 1} sx={{ ml: 'auto', minWidth: 0 }}>
      <Typography variant="caption" color="text.secondary" sx={{ fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>{from}–{to} из {count}</Typography>
      {desktopSizeButtons && mobile ? <Stack direction="row" gap={.5}>
        <Button size="small" variant="outlined" aria-label="Предыдущая страница" disabled={page <= 0} onClick={() => onPageChange(page - 1)} sx={{ minWidth: 32, px: .5 }}>‹</Button>
        <Button size="small" variant="outlined" aria-label="Следующая страница" disabled={page >= pages - 1} onClick={() => onPageChange(page + 1)} sx={{ minWidth: 32, px: .5 }}>›</Button>
      </Stack> : <Pagination size="small" count={pages} page={Math.min(page + 1, pages)} onChange={(_, next) => onPageChange(next - 1)}
        siblingCount={0} boundaryCount={mobile ? 0 : 1} sx={{ '& .MuiPaginationItem-root': { borderRadius: ui.radius.pagination, minWidth: mobile ? 26 : 32, height: mobile ? 26 : 32, mx: mobile ? 0 : .25 } }} />}
    </Stack>
  </Stack>;
}

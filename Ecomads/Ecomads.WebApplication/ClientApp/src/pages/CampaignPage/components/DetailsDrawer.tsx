import CloseIcon from '@mui/icons-material/Close';
import { Box, Drawer, IconButton, Stack, Typography } from '@mui/material';
import { useId } from 'react';
import type { ReactNode } from 'react';

export function DetailsDrawer({ open, onClose, title, children }: { open: boolean; onClose: () => void; title: string; children: ReactNode }) {
  const labelId = useId();
  return <Drawer anchor="right" open={open} onClose={onClose} PaperProps={{
    role: 'dialog', 'aria-labelledby': labelId,
    sx: { width: { xs: '100%', sm: 520 }, maxWidth: '100%', p: { xs: 2, sm: 3 }, bgcolor: 'background.default' }
  }}>
    <Stack direction="row" alignItems="center" gap={1} sx={{ mb: 2 }}>
      <Typography id={labelId} component="h2" variant="h6" sx={{ flex: 1, minWidth: 0, overflowWrap: 'anywhere' }}>{title}</Typography>
      <IconButton onClick={onClose} aria-label="Закрыть подробности"><CloseIcon /></IconButton>
    </Stack>
    <Box sx={{ minWidth: 0, overflowWrap: 'anywhere' }}>{children}</Box>
  </Drawer>;
}

import { Box, Stack, Typography } from '@mui/material';
import type { ReactNode } from 'react';

type PageHeaderProps = {
  title: string;
  description?: ReactNode;
  actions?: ReactNode;
  compact?: boolean;
};

export function PageHeader({ title, description, actions }: PageHeaderProps) {
  return (
    <Stack direction={{ xs: 'column', sm: 'row' }} justifyContent="space-between" alignItems={{ sm: 'center' }} gap={2} sx={{ minWidth: 0 }}>
      <Stack spacing={0.5} sx={{ minWidth: 0 }}>
        <Typography component="h1" variant="h4" fontWeight={700} color="text.primary" sx={{ overflowWrap: 'anywhere' }}>
          {title}
        </Typography>
        {description ? (typeof description === 'string' ?
          <Typography color="text.secondary" sx={{ fontSize: { xs: 13, sm: 15 } }}>{description}</Typography> : <Box>{description}</Box>) : null}
      </Stack>
      {actions}
    </Stack>
  );
}


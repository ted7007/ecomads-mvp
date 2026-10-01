import { Box, Stack, Typography } from '@mui/material';
import type { ReactNode } from 'react';

type PageHeaderProps = {
  title: string;
  description?: ReactNode;
  actions?: ReactNode;
};

export function PageHeader({ title, description, actions }: PageHeaderProps) {
  return (
    <Stack direction={{ xs: 'column', sm: 'row' }} justifyContent="space-between" gap={2} sx={{ mb: 3 }}>
      <Stack spacing={0.5}>
        <Typography component="h1" variant="h4" fontWeight={800} color="text.primary">
          {title}
        </Typography>
        {description ? (typeof description === 'string' ?
          <Typography color="text.secondary">{description}</Typography> : <Box>{description}</Box>) : null}
      </Stack>
      {actions}
    </Stack>
  );
}


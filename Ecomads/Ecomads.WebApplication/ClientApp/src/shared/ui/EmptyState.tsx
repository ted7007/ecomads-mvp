import { Box, Typography } from '@mui/material';

type EmptyStateProps = {
  title?: string;
  description?: string;
};

export function EmptyState({ title = 'Нет данных', description }: EmptyStateProps) {
  return (
    <Box sx={{ border: '1px dashed', borderColor: 'divider', borderRadius: 3, p: 4, textAlign: 'center' }}>
      <Typography variant="h6">{title}</Typography>
      {description ? (
        <Typography color="text.secondary" sx={{ mt: 1 }}>
          {description}
        </Typography>
      ) : null}
    </Box>
  );
}


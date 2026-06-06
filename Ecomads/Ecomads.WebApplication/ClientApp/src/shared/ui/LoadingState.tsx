import { Box, CircularProgress, Stack, Typography } from '@mui/material';

type LoadingStateProps = {
  title?: string;
  description?: string;
};

export function LoadingState({ title = 'Загрузка', description }: LoadingStateProps) {
  return (
    <Box sx={{ py: 6 }}>
      <Stack alignItems="center" spacing={2}>
        <CircularProgress size={32} />
        <Typography variant="h6">{title}</Typography>
        {description ? (
          <Typography color="text.secondary" textAlign="center">
            {description}
          </Typography>
        ) : null}
      </Stack>
    </Box>
  );
}


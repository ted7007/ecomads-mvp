import { Alert, AlertTitle, Button, Stack } from '@mui/material';

type ErrorStateProps = {
  title?: string;
  description?: string;
  retryLabel?: string;
  onRetry?: () => void;
};

export function ErrorState({ title = 'Ошибка', description, retryLabel = 'Повторить', onRetry }: ErrorStateProps) {
  return (
    <Alert
      severity="error"
      action={
        onRetry ? (
          <Button color="inherit" size="small" onClick={onRetry}>
            {retryLabel}
          </Button>
        ) : null
      }
    >
      <Stack spacing={0.5}>
        <AlertTitle>{title}</AlertTitle>
        {description}
      </Stack>
    </Alert>
  );
}


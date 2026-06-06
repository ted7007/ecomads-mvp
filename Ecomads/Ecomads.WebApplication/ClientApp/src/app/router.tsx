import { createBrowserRouter, Navigate } from 'react-router-dom';
import { Box, Button, Card, CardContent, Container, Stack, Typography } from '@mui/material';
import { LoadingState } from '../shared/ui/LoadingState';

function AppPlaceholderPage() {
  return (
    <Box sx={{ minHeight: '100vh', bgcolor: 'background.default', py: 6 }}>
      <Container maxWidth="md">
        <Card>
          <CardContent>
            <Stack spacing={2}>
              <Typography component="h1" variant="h4" fontWeight={700}>
                EcomAds React frontend
              </Typography>
              <Typography color="text.secondary">
                Технический placeholder для изолированного ClientApp. Реальные страницы пока не перенесены.
              </Typography>
              <Stack direction="row" spacing={2}>
                <Button href="/dashboard.html" variant="contained">
                  Legacy dashboard
                </Button>
                <Button href="/report.html" variant="outlined">
                  Legacy report
                </Button>
              </Stack>
            </Stack>
          </CardContent>
        </Card>
      </Container>
    </Box>
  );
}

export const router = createBrowserRouter(
  [
    {
      path: '/',
      element: <AppPlaceholderPage />
    },
    {
      path: '/report-placeholder',
      element: <LoadingState title="Report placeholder" description="React report page будет добавлена отдельной задачей." />
    },
    {
      path: '*',
      element: <Navigate to="/" replace />
    }
  ],
  {
    basename: '/app'
  }
);


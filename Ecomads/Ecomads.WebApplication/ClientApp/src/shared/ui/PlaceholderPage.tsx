import { Button, Card, CardContent, Stack, Typography } from '@mui/material';
import { legacyRoutes } from '../../app/routes';
import { PageHeader } from './PageHeader';

type PlaceholderPageProps = {
  title: string;
  description: string;
  legacyHref?: string;
};

export function PlaceholderPage({ title, description, legacyHref }: PlaceholderPageProps) {
  return (
    <Stack spacing={3}>
      <PageHeader title={title} description="React shell готов. Реальная страница будет перенесена отдельной задачей." />
      <Card>
        <CardContent>
          <Stack spacing={2}>
            <Typography variant="h6">{description}</Typography>
            <Typography color="text.secondary">
              Этот экран нужен, чтобы маршруты и layout уже были стабильны до переноса бизнес-страниц.
            </Typography>
            <Stack direction="row" spacing={2}>
              {legacyHref ? (
                <Button href={legacyHref} variant="contained">
                  Открыть legacy
                </Button>
              ) : null}
              <Button href={legacyRoutes.dashboard} variant="outlined">
                Legacy dashboard
              </Button>
            </Stack>
          </Stack>
        </CardContent>
      </Card>
    </Stack>
  );
}


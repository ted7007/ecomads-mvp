import { Alert, Container, Stack, Typography } from '@mui/material';
import { Link, useSearchParams } from 'react-router-dom';
import { legalTitles } from './LegalLinks';
import { useRegistrationDocuments } from './useRegistrationDocuments';

export function LegalPage({ slug }: { slug: keyof typeof legalTitles }) {
  const { data, isError } = useRegistrationDocuments();
  const [params] = useSearchParams();
  const requestedVersion = params.get('version');
  const text = data?.[slug].trim();
  const [title, ...body] = text?.split(/\n\s*\n/) ?? [];
  return <Container component="main" maxWidth="md" sx={{ py: 5 }}>
    <Link to="/site">На главную EcomAds</Link>
    <Typography component="h1" variant="h4" sx={{ my: 3 }}>{title ?? legalTitles[slug]}</Typography>
    {!data ? (isError
      ? <Alert severity="error">Не удалось загрузить документ. Обновите страницу.</Alert>
      : <Typography role="status">Загружаем документ…</Typography>
    ) : (
      <Stack spacing={2} sx={{ overflowWrap: 'anywhere' }}>
        {requestedVersion && requestedVersion !== data.version && (
          <Alert severity="info">Документы обновились. Ниже показана действующая редакция. Перед отправкой формы обновите страницу регистрации.</Alert>
        )}
        {body.map((paragraph, index) => /^\d+\. /.test(paragraph)
          ? <Typography key={index} component="h2" variant="h6">{paragraph}</Typography>
          : <Typography key={index} sx={{ whiteSpace: 'pre-line' }}>{paragraph}</Typography>)}
      </Stack>
    )}
  </Container>;
}

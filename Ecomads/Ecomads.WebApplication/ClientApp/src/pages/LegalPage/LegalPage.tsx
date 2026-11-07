/// <reference types="vite/client" />
import { Container, Typography } from '@mui/material';
import { Link } from 'react-router-dom';
import { legalTitles } from './LegalLinks';
import privacy from './privacy.txt?raw';
import consent from './consent.txt?raw';
import cookies from './cookies.txt?raw';

const documents = { privacy, consent, cookies };
export function LegalPage({ slug }: { slug: keyof typeof documents }) {
  return <Container component="main" maxWidth="md" sx={{ py: 5 }}>
    <Link to="/site">На главную EcomAds</Link>
    <Typography component="h1" variant="h4" sx={{ my: 3 }}>{legalTitles[slug]}</Typography>
    <Typography sx={{ whiteSpace: 'pre-wrap', overflowWrap: 'anywhere' }}>{documents[slug]}</Typography>
  </Container>;
}

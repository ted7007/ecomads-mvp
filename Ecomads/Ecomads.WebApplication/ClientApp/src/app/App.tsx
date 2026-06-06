import { CssBaseline } from '@mui/material';
import { RouterProvider } from 'react-router-dom';
import { AppProviders } from './providers';
import { router } from './router';

export function App() {
  return (
    <AppProviders>
      <CssBaseline />
      <RouterProvider router={router} />
    </AppProviders>
  );
}


import { zodResolver } from '@hookform/resolvers/zod';
import TelegramIcon from '@mui/icons-material/Telegram';
import { Alert, Box, Button, Card, CardContent, Container, Link, Stack, Tab, Tabs, TextField, Typography } from '@mui/material';
import { useState } from 'react';
import { useForm } from 'react-hook-form';
import { Navigate, useLocation, useNavigate } from 'react-router-dom';
import { appRoutes } from '../../app/routes';
import { login, register } from '../../shared/auth/authApi';
import { getToken, persistAuthResponse } from '../../shared/auth/tokenStorage';
import { loginFormSchema, registerFormSchema, type LoginFormValues, type RegisterFormValues } from './loginSchemas';

type AuthMode = 'login' | 'demo';

type LocationState = {
  from?: {
    pathname?: string;
  };
};

export function LoginPage() {
  const [mode, setMode] = useState<AuthMode>('login');
  const [error, setError] = useState<string | null>(null);
  const navigate = useNavigate();
  const location = useLocation();
  const redirectTo = (location.state as LocationState | null)?.from?.pathname ?? appRoutes.dashboard;

  if (getToken()) {
    return <Navigate to={appRoutes.dashboard} replace />;
  }

  const handleSuccess = () => {
    navigate(redirectTo, { replace: true });
  };

  return (
    <Box
      sx={{
        minHeight: '100vh',
        bgcolor: '#0F172A',
        display: 'grid',
        placeItems: 'center',
        px: 2,
        py: 4
      }}
    >
      <Container maxWidth="sm" disableGutters>
        <Stack spacing={3}>
          <Stack spacing={1} alignItems="center">
            <Typography component="h1" variant="h4" fontWeight={900} color="#F8FAFC" textAlign="center">
              EcomAds
            </Typography>
            <Typography color="#CBD5E1" textAlign="center">
              Аналитика рекламы и рекомендации для Wildberries
            </Typography>
          </Stack>

          <Card sx={{ width: '100%', borderRadius: 3, boxShadow: '0 24px 80px rgba(0,0,0,0.35)' }}>
            <CardContent sx={{ p: { xs: 3, sm: 4 } }}>
              <Stack spacing={3}>
                <Tabs
                  value={mode}
                  onChange={(_, value: AuthMode) => {
                    setMode(value);
                    setError(null);
                  }}
                  variant="fullWidth"
                >
                  <Tab label="Вход" value="login" />
                  <Tab label="Демо-доступ" value="demo" />
                </Tabs>

                {error ? <Alert severity="error">{error}</Alert> : null}

                <Box>
                  {mode === 'login' ? (
                    <LoginForm setError={setError} onSuccess={handleSuccess} />
                  ) : (
                    <RegisterForm setError={setError} onSuccess={handleSuccess} />
                  )}
                </Box>
              </Stack>
            </CardContent>
          </Card>

          <Link
            href="https://t.me/ecomads_mvp01"
            rel="noopener noreferrer"
            target="_blank"
            underline="hover"
            sx={{ alignSelf: 'center', color: '#BAE6FD', display: 'inline-flex', alignItems: 'center', gap: 0.75, fontWeight: 700 }}
          >
            <TelegramIcon fontSize="small" />
            Группа EcomAds в Telegram
          </Link>
        </Stack>
      </Container>
    </Box>
  );
}

type AuthFormProps = {
  setError: (message: string | null) => void;
  onSuccess: () => void;
};

function LoginForm({ setError, onSuccess }: AuthFormProps) {
  const {
    formState: { errors, isSubmitting },
    handleSubmit,
    register: registerField
  } = useForm<LoginFormValues>({
    resolver: zodResolver(loginFormSchema),
    defaultValues: {
      email: '',
      password: ''
    }
  });

  const submit = handleSubmit(async (values) => {
    setError(null);

    try {
      const response = await login(values);
      persistAuthResponse(response);
      onSuccess();
    } catch (error) {
      setError(error instanceof Error ? error.message : 'Ошибка входа');
    }
  });

  return (
    <Stack component="form" spacing={2} onSubmit={submit} noValidate>
      <TextField
        autoComplete="email"
        autoFocus
        disabled={isSubmitting}
        error={Boolean(errors.email)}
        fullWidth
        helperText={errors.email?.message}
        label="Email"
        type="email"
        {...registerField('email')}
      />
      <TextField
        autoComplete="current-password"
        disabled={isSubmitting}
        error={Boolean(errors.password)}
        fullWidth
        helperText={errors.password?.message}
        label="Пароль"
        type="password"
        {...registerField('password')}
      />
      <Button disabled={isSubmitting} fullWidth size="large" type="submit" variant="contained">
        {isSubmitting ? 'Входим...' : 'Войти'}
      </Button>
    </Stack>
  );
}

function RegisterForm({ setError, onSuccess }: AuthFormProps) {
  const {
    formState: { errors, isSubmitting },
    handleSubmit,
    register: registerField
  } = useForm<RegisterFormValues>({
    resolver: zodResolver(registerFormSchema),
    defaultValues: {
      name: '',
      email: '',
      password: ''
    }
  });

  const submit = handleSubmit(async (values) => {
    setError(null);

    try {
      const response = await register(values);
      persistAuthResponse(response);
      onSuccess();
    } catch (error) {
      setError(error instanceof Error ? error.message : 'Ошибка регистрации');
    }
  });

  return (
    <Stack component="form" spacing={2} onSubmit={submit} noValidate>
      <TextField
        autoComplete="name"
        autoFocus
        disabled={isSubmitting}
        error={Boolean(errors.name)}
        fullWidth
        helperText={errors.name?.message}
        label="Имя"
        {...registerField('name')}
      />
      <TextField
        autoComplete="email"
        disabled={isSubmitting}
        error={Boolean(errors.email)}
        fullWidth
        helperText={errors.email?.message}
        label="Email"
        type="email"
        {...registerField('email')}
      />
      <TextField
        autoComplete="new-password"
        disabled={isSubmitting}
        error={Boolean(errors.password)}
        fullWidth
        helperText={errors.password?.message}
        label="Пароль"
        type="password"
        {...registerField('password')}
      />
      <Typography color="text.secondary" variant="body2">
        Сейчас продукт открыт только в формате demo на 3 дня.
      </Typography>
      <Button disabled={isSubmitting} fullWidth size="large" type="submit" variant="contained">
        {isSubmitting ? 'Создаем demo...' : 'Получить demo-доступ'}
      </Button>
    </Stack>
  );
}

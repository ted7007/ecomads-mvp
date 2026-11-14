import { zodResolver } from '@hookform/resolvers/zod';
import TelegramIcon from '@mui/icons-material/Telegram';
import { ThemeProvider, createTheme } from '@mui/material/styles';
import '../../shared/assets/fonts/golos-text.css';
import { Alert, Box, Button, Card, CardContent, Checkbox, Container, FormControl, FormControlLabel, FormHelperText, Link, Stack, TextField, Typography } from '@mui/material';
import { useState } from 'react';
import { Controller, useForm } from 'react-hook-form';
import { Navigate, useLocation, useNavigate } from 'react-router-dom';
import { appRoutes } from '../../app/routes';
import { login, register } from '../../shared/auth/authApi';
import { clearAuth, persistAuthResponse } from '../../shared/auth/tokenStorage';
import { useSession, SessionStatus } from '../../shared/auth/useSession';
import { safeReturnTo, withReturnTo } from '../../shared/auth/returnTo';
import { legalTitles } from '../LegalPage/LegalLinks';
import { useRegistrationDocuments } from '../LegalPage/useRegistrationDocuments';
import { BrandLogo } from '../../shared/ui/BrandLogo';
import { loginFormSchema, registerFormSchema, type LoginFormValues, type RegisterFormValues } from './loginSchemas';

const authTheme = createTheme({
  palette: {
    mode: 'light',
    primary: { main: '#5A22C8', dark: '#4A1BA6' },
    text: { primary: '#1D1D1F', secondary: '#5C5C66' },
    background: { default: '#F3F2F8', paper: '#FFFFFF' },
  },
  typography: {
    fontFamily: '"EcomAds Golos Text", sans-serif',
    button: { textTransform: 'none', fontWeight: 600 },
  },
  shape: { borderRadius: 12 },
});

type LocationState = {
  from?: {
    pathname?: string;
  };
};

export function LoginPage() {
  const [error, setError] = useState<string | null>(null);
  const navigate = useNavigate();
  const location = useLocation();
  const session = useSession();
  const isRegistration = location.pathname === appRoutes.register;
  const redirectTo = isRegistration ? appRoutes.wbStores : safeReturnTo(new URLSearchParams(location.search).get('returnTo') ?? (location.state as LocationState | null)?.from?.pathname);

  if (session.token && !session.invalid) {
    if (session.isPending) return <SessionStatus retry={() => void session.refetch()} />;
    if (session.isError) return <SessionStatus error retry={() => void session.refetch()} />;
    return <Navigate to={redirectTo} replace />;
  }
  if (session.invalid) clearAuth();

  const handleSuccess = () => {
    navigate(redirectTo, { replace: true });
  };

  return (
    <ThemeProvider theme={authTheme}>
    <Box
      sx={{
        minHeight: '100vh',
        background: 'radial-gradient(1100px 700px at 6% 0%, #DCD0FF 0, transparent 62%), radial-gradient(900px 640px at 100% 8%, #FFE1CF 0, transparent 58%), #F3F2F8',
        display: 'grid',
        placeItems: 'center',
        px: 2,
        py: 4
      }}
    >
      <Container maxWidth="sm" disableGutters>
        <Stack spacing={3}>
          <Stack spacing={1} alignItems="center">
            <Link href={withReturnTo(appRoutes.site, redirectTo)} aria-label="EcomAds — на лендинг"><BrandLogo height={42} /></Link>
            <Typography color="text.secondary" textAlign="center">
              Аналитика рекламы и рекомендации для Wildberries
            </Typography>
          </Stack>

          <Card sx={{ width: '100%', borderRadius: 3, bgcolor: 'rgba(255,255,255,0.94)', border: '1px solid rgba(255,255,255,0.85)', boxShadow: '0 24px 60px rgba(40,50,90,0.12)' }}>
            <CardContent sx={{ p: { xs: 3, sm: 4 } }}>
              <Stack spacing={3}>
                <Typography component="h1" variant="h5">{isRegistration ? 'Регистрация' : 'Вход'}</Typography>
                {error ? <Alert severity="error">{error}</Alert> : null}

                <Box>
                  {!isRegistration ? (
                    <LoginForm setError={setError} onSuccess={handleSuccess} />
                  ) : (
                    <RegisterForm setError={setError} onSuccess={handleSuccess} />
                  )}
                </Box>
                <Link href={isRegistration ? appRoutes.login : appRoutes.register}>
                  {isRegistration ? 'Уже есть аккаунт? Войти' : 'Начать бесплатно'}
                </Link>
              </Stack>
            </CardContent>
          </Card>

          <Link
            href="https://t.me/ecomads_official?direct"
            rel="noopener noreferrer"
            target="_blank"
            underline="hover"
            sx={{ alignSelf: 'center', color: 'primary.main', display: 'inline-flex', alignItems: 'center', gap: 0.75, fontWeight: 700 }}
          >
            <TelegramIcon fontSize="small" />
            Напишите в Telegram
          </Link>
        </Stack>
      </Container>
    </Box>
    </ThemeProvider>
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

const segmentFields = [
  { name: 'stores', label: 'Кабинетов WB', options: [['one', 'Один'], ['two-five', '2–5'], ['over-five', 'Больше 5'], ['agency', 'Веду клиентов как агентство']] },
  { name: 'budget', label: 'Реклама в месяц', options: [['under-100k', 'До 100 тыс. ₽'], ['100k-500k', '100–500 тыс. ₽'], ['500k-1m', '500 тыс. – 1 млн ₽'], ['over-1m', 'Больше 1 млн ₽']] },
  { name: 'jam', label: 'Подписка «Джем»', options: [['yes', 'Есть'], ['no', 'Нет'], ['unknown', 'Не знаю']] },
] as const;

function RegisterForm({ setError, onSuccess }: AuthFormProps) {
  const { data: documents, isError: documentsFailed, refetch } = useRegistrationDocuments();
  const documentQuery = documents ? `?version=${documents.version}` : '';
  const {
    control,
    formState: { errors, isSubmitting },
    handleSubmit,
    register: registerField
  } = useForm<RegisterFormValues>({
    resolver: zodResolver(registerFormSchema),
    defaultValues: {
      name: '',
      email: '',
      password: '',
      stores: '',
      budget: '',
      jam: '',
      personalDataConsent: false
    }
  });

  const submit = handleSubmit(async (values) => {
    if (!documents) return;
    setError(null);

    try {
      const response = await register({
        ...values,
        registrationDocumentsVersion: documents.version,
        stores: values.stores || null,
        budget: values.budget || null,
        jam: values.jam || null
      });
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
      <Typography color="text.secondary" variant="body2">Бесплатный демо-доступ на 3 дня. После регистрации подключите кабинет Wildberries.</Typography>
      <Typography color="text.secondary" variant="body2">Расскажите о вашем бизнесе — эти поля необязательны.</Typography>
      {segmentFields.map(({ name, label, options }) => (
        <TextField key={name} select SelectProps={{ native: true }} defaultValue="" fullWidth label={label}
          InputLabelProps={{ shrink: true }} disabled={isSubmitting} {...registerField(name)}>
          <option value="">Не указано</option>
          {options.map(([value, title]) => <option key={value} value={value}>{title}</option>)}
        </TextField>
      ))}
      {documents ? (
        <Box>
          <Link href={`/legal/privacy${documentQuery}`} target="_blank" rel="noopener noreferrer" variant="body2">
            {legalTitles.privacy}
          </Link>
          <FormControl error={Boolean(errors.personalDataConsent)} fullWidth>
            <Controller name="personalDataConsent" control={control} render={({ field }) => (
              <FormControlLabel
                control={
                  <Checkbox
                    checked={field.value}
                    disabled={isSubmitting}
                    inputRef={field.ref}
                    name={field.name}
                    onBlur={field.onBlur}
                    onChange={(_, checked) => field.onChange(checked)}
                    inputProps={{
                      'aria-required': true,
                      'aria-invalid': Boolean(errors.personalDataConsent),
                      'aria-describedby': errors.personalDataConsent ? 'registration-consent-error' : undefined
                    }}
                  />
                }
                label={<span>Даю <Link href={`/legal/consent${documentQuery}`} target="_blank" rel="noopener noreferrer">согласие на обработку персональных данных</Link></span>}
              />
            )} />
            {errors.personalDataConsent && (
              <FormHelperText id="registration-consent-error" role="alert">{errors.personalDataConsent.message}</FormHelperText>
            )}
          </FormControl>
        </Box>
      ) : documentsFailed ? (
        <Alert severity="error" action={<Button color="inherit" onClick={() => void refetch()}>Повторить</Button>}>
          Не удалось загрузить документы для регистрации.
        </Alert>
      ) : <Typography role="status" variant="body2">Загружаем документы…</Typography>}
      <Button disabled={isSubmitting || !documents} fullWidth size="large" type="submit" variant="contained">
        {isSubmitting ? 'Создаём аккаунт…' : 'Начать бесплатно'}
      </Button>
    </Stack>
  );
}

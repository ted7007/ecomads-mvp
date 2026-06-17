import FeedbackIcon from '@mui/icons-material/Feedback';
import {
  Alert,
  Button,
  Card,
  CardContent,
  Checkbox,
  FormControl,
  FormControlLabel,
  FormGroup,
  FormHelperText,
  FormLabel,
  Radio,
  RadioGroup,
  Stack,
  TextField,
  Typography
} from '@mui/material';
import { zodResolver } from '@hookform/resolvers/zod';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Controller, useForm, type FieldPath, type UseFormRegister } from 'react-hook-form';
import { useNavigate } from 'react-router-dom';
import { appRoutes } from '../../app/routes';
import { queryKeys } from '../../shared/api/queryKeys';
import { ErrorState } from '../../shared/ui/ErrorState';
import { LoadingState } from '../../shared/ui/LoadingState';
import { PageHeader } from '../../shared/ui/PageHeader';
import {
  clarityScoreOptions,
  continueUsingOptions,
  demoFeedbackFormSchema,
  getDemoFeedbackState,
  missingForDecisionOptions,
  mostUsefulFeatureOptions,
  primaryTaskOptions,
  submitDemoFeedback,
  type DemoFeedbackFormValues,
  usedSectionOptions
} from './demoFeedbackApi';

export function DemoFeedbackPage() {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const {
    control,
    formState: { errors },
    handleSubmit,
    register,
    watch
  } = useForm<DemoFeedbackFormValues>({
    resolver: zodResolver(demoFeedbackFormSchema),
    defaultValues: {
      usedSections: [],
      missingForDecision: [],
      generalComment: '',
      improvementPriority: ''
    }
  });

  const continueUsingAnswer = watch('continueUsingAnswer');
  const shouldAskImprovementPriority = continueUsingAnswer === 'maybe_after_improvements' || continueUsingAnswer === 'no';

  const feedbackStateQuery = useQuery({
    queryKey: queryKeys.demoFeedback.current,
    queryFn: getDemoFeedbackState
  });

  const submitMutation = useMutation({
    mutationFn: submitDemoFeedback,
    onSuccess: async (response) => {
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: queryKeys.auth.me }),
        queryClient.invalidateQueries({ queryKey: queryKeys.demoFeedback.current })
      ]);
      navigate(appRoutes.dashboard, {
        replace: true,
        state: {
          demoFeedbackSuccess: response.message
        }
      });
    }
  });

  const submit = handleSubmit((values) => {
    submitMutation.mutate({
      ...values,
      improvementPriority: shouldAskImprovementPriority ? values.improvementPriority : undefined
    });
  });

  if (feedbackStateQuery.isLoading) {
    return <LoadingState title="Загружаем форму обратной связи" />;
  }

  if (feedbackStateQuery.isError) {
    return <ErrorState title="Не удалось открыть форму обратной связи" description={getErrorMessage(feedbackStateQuery.error)} />;
  }

  const feedbackState = feedbackStateQuery.data;

  return (
    <Stack spacing={3}>
      <PageHeader title="Демо-доступ закончился" />

      <Card>
        <CardContent>
          <Stack spacing={2.5}>
            <FeedbackIcon color="primary" fontSize="large" />
            <Typography component="h1" variant="h5" fontWeight={800}>
              Оставьте обратную связь
            </Typography>
            <Typography color="text.secondary" variant="body1">
              Ответы помогут довести EcomAds до MVP. После отправки доступ снова откроется до выхода MVP-версии.
            </Typography>

            {feedbackState?.hasSubmitted ? (
              <FeedbackAlreadySubmitted onDashboard={() => navigate(appRoutes.dashboard)} />
            ) : null}

            {!feedbackState?.hasSubmitted && !feedbackState?.canSubmit ? (
              <FeedbackUnavailable onDashboard={() => navigate(appRoutes.dashboard)} />
            ) : null}

            {feedbackState?.canSubmit ? (
              <Stack component="form" spacing={2.5} onSubmit={submit} noValidate>
                {submitMutation.isError ? <Alert severity="error">{getErrorMessage(submitMutation.error)}</Alert> : null}

                <RadioField
                  error={errors.primaryTask?.message}
                  label="Какую задачу вы пытались решить в EcomAds?"
                  name="primaryTask"
                  options={primaryTaskOptions}
                  registerField={register}
                />

                <CheckboxField
                  control={control}
                  error={errors.usedSections?.message}
                  label="Какие разделы вы успели использовать?"
                  name="usedSections"
                  options={usedSectionOptions}
                />

                <RadioField
                  error={errors.mostUsefulFeature?.message}
                  label="Какая функция оказалась самой полезной?"
                  name="mostUsefulFeature"
                  options={mostUsefulFeatureOptions}
                  registerField={register}
                />

                <RadioField
                  error={errors.recommendationsClarityScore?.message}
                  label="Насколько рекомендации были понятны и применимы?"
                  name="recommendationsClarityScore"
                  options={clarityScoreOptions}
                  registerField={register}
                />

                <CheckboxField
                  control={control}
                  error={errors.missingForDecision?.message}
                  label="Чего не хватило, чтобы принять решение по рекламе?"
                  name="missingForDecision"
                  options={missingForDecisionOptions}
                />

                <TextField
                  error={Boolean(errors.generalComment)}
                  fullWidth
                  helperText={errors.generalComment?.message ?? 'Минимум 50 символов.'}
                  label="Оставьте короткий комментарий по демо-доступу"
                  minRows={4}
                  multiline
                  placeholder="Напишите, что было полезно, что было непонятно и что нужно улучшить, чтобы вы продолжили пользоваться сервисом."
                  {...register('generalComment')}
                />

                <RadioField
                  error={errors.continueUsingAnswer?.message}
                  label="Хотите продолжить пользоваться EcomAds до выхода MVP?"
                  name="continueUsingAnswer"
                  options={continueUsingOptions}
                  registerField={register}
                />

                {shouldAskImprovementPriority ? (
                  <TextField
                    error={Boolean(errors.improvementPriority)}
                    fullWidth
                    helperText={errors.improvementPriority?.message}
                    label="Что нужно доработать в первую очередь?"
                    minRows={3}
                    multiline
                    {...register('improvementPriority')}
                  />
                ) : null}

                <Button disabled={submitMutation.isPending} size="large" type="submit" variant="contained">
                  {submitMutation.isPending ? 'Отправляем...' : 'Отправить и продлить доступ'}
                </Button>
              </Stack>
            ) : null}
          </Stack>
        </CardContent>
      </Card>
    </Stack>
  );
}

type Option<TValue extends string | number = string> = {
  value: TValue;
  label: string;
};

type RadioFieldProps<TName extends FieldPath<DemoFeedbackFormValues>> = {
  error?: string;
  label: string;
  name: TName;
  options: readonly Option<string | number>[];
  registerField: UseFormRegister<DemoFeedbackFormValues>;
};

function RadioField<TName extends FieldPath<DemoFeedbackFormValues>>({ error, label, name, options, registerField }: RadioFieldProps<TName>) {
  return (
    <FormControl error={Boolean(error)}>
      <FormLabel>{label}</FormLabel>
      <RadioGroup>
        {options.map((option) => (
          <FormControlLabel key={option.value} control={<Radio {...registerField(name)} />} label={option.label} value={option.value.toString()} />
        ))}
      </RadioGroup>
      {error ? <FormHelperText>{error}</FormHelperText> : null}
    </FormControl>
  );
}

type CheckboxFieldProps<TName extends 'usedSections' | 'missingForDecision'> = {
  control: ReturnType<typeof useForm<DemoFeedbackFormValues>>['control'];
  error?: string;
  label: string;
  name: TName;
  options: readonly Option[];
};

function CheckboxField<TName extends 'usedSections' | 'missingForDecision'>({ control, error, label, name, options }: CheckboxFieldProps<TName>) {
  return (
    <FormControl error={Boolean(error)}>
      <FormLabel>{label}</FormLabel>
      <Controller
        control={control}
        name={name}
        render={({ field }) => {
          const selectedValues: string[] = Array.isArray(field.value) ? field.value : [];

          return (
            <FormGroup>
              {options.map((option) => (
                <FormControlLabel
                  key={option.value}
                  control={
                    <Checkbox
                      checked={selectedValues.includes(option.value)}
                      onChange={(event) => {
                        const nextValues = event.target.checked
                          ? [...selectedValues, option.value]
                          : selectedValues.filter((value) => value !== option.value);
                        field.onChange(nextValues);
                      }}
                    />
                  }
                  label={option.label}
                />
              ))}
            </FormGroup>
          );
        }}
      />
      {error ? <FormHelperText>{error}</FormHelperText> : null}
    </FormControl>
  );
}

function FeedbackAlreadySubmitted({ onDashboard }: { onDashboard: () => void }) {
  return (
    <Stack spacing={2}>
      <Alert severity="success">Обратная связь уже отправлена. Доступ к MVP-версии открыт.</Alert>
      <Button onClick={onDashboard} variant="contained">
        Перейти к обзору рекламы
      </Button>
    </Stack>
  );
}

function FeedbackUnavailable({ onDashboard }: { onDashboard: () => void }) {
  return (
    <Stack spacing={2}>
      <Alert severity="info">Форма появится после окончания демо-доступа. Пока доступ открыт, можно продолжать пользоваться сервисом.</Alert>
      <Button onClick={onDashboard} variant="outlined">
        Вернуться к обзору рекламы
      </Button>
    </Stack>
  );
}

function getErrorMessage(error: unknown): string {
  return error instanceof Error ? error.message : 'Произошла ошибка';
}

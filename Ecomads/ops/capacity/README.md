# Замеры ёмкости EcomAds

Инструменты не изменяют код приложения и не создают запросы нагрузки к рабочему сайту или WB.
`benchmarks/CapacityBench` вызывает реальные контроллеры, импортер fullstats и один проход
RecommendationWorker; все исходящие HTTP-запросы приложения блокируются. Внешнее обновление
кабинетов в этом тесте выключено, а не эмулируется внутри воркера. Лимиты и бюджет WB
проверяются отдельно расчётной моделью. Это не полный end-to-end тест сбора WB.

## Локальный стенд

PostgreSQL запускается отдельно от существующего `ecomads-db`:

```powershell
docker run --detach --name ecomads-capacity-db --label ecomads.purpose=capacity --cpus=1 --memory=512m --publish 127.0.0.1:55432:5432 --env POSTGRES_USER=capacity --env POSTGRES_PASSWORD=capacity-local-only --env POSTGRES_DB=capacity_admin postgres:15-alpine
dotnet build benchmarks/CapacityBench/CapacityBench.csproj -c Release -p:AllowMissingPrunePackageData=true
dotnet benchmarks/CapacityBench/bin/Release/net10.0/CapacityBench.dll 10 artifacts/capacity/local-10.json
dotnet benchmarks/CapacityBench/bin/Release/net10.0/CapacityBench.dll 50 artifacts/capacity/local-50.json
dotnet benchmarks/CapacityBench/bin/Release/net10.0/CapacityBench.dll 100 artifacts/capacity/local-100.json
```

Пароль выше принадлежит исключительно одноразовому локальному стенду. Стенд принимает
только отдельный `capacity_admin` с пользователем `capacity` на порту 55432. Он создаёт
случайную БД с префиксом `ecomads_capacity_` и удаляет именно эту БД после записи результата.
Настоящие кабинеты, пароли, токены и дампы production не используются.

Каждый кабинет: 40 кампаний, 100 товаров каталога, 80 рекламных пар,
3 кластера на пару и 30 дней данных. Рекламные пары используют 80 из 100 товаров.
На один виртуальный пользователь приходится 10 циклов трёх GET-запросов: список кампаний,
рекомендации кампании, сводка рекомендаций. Между циклами пауза 250 мс.
Три уровня параллелизма: 3, 5, 10. Параллельно идёт один реальный фоновый проход.
Холодный импорт семи дней fullstats измеряется на одном кабинете в каждом сценарии.

Показатели CPU — процессорные секунды приложения и cgroup тестового PostgreSQL.
Аллокации за фазу не равны требуемой RAM: память возвращается сборщиком мусора.
Память cgroup PostgreSQL включает файловый кеш и может достигать лимита без OOM.
Peak PostgreSQL монотонен в течение жизни контейнера; это не отдельный пик каждого сценария.
Для памяти приложения сохраняется максимум RSS процесса, а не только последний снимок.

Дополнительный Linux-тест выполняется в Docker Desktop: приложение 1 CPU/1024 MiB,
БД 1 CPU/512 MiB. Оставшиеся условные 512 MiB резервируются системе. Это проверка
ограниченного бюджета, но не воспроизведение частоты CPU, диска, ОС и соседей реального VPS.

```powershell
docker run --rm --name ecomads-capacity-app --cpus=1 --memory=1g --mount 'type=bind,source=C:/Work/ecomads-mvp/Ecomads,target=/work' --workdir /work --env 'ECOMADS_CAPACITY_POSTGRES=Host=host.docker.internal;Port=55432;Username=capacity;Password=capacity-local-only;Database=capacity_admin;Maximum Pool Size=30' --env 'ECOMADS_CAPACITY_APP_LIMITS=1 CPU / 1024 MiB; database separately 1 CPU / 512 MiB' mcr.microsoft.com/dotnet/aspnet:10.0 dotnet benchmarks/CapacityBench/bin/Release/net10.0/CapacityBench.dll 10 artifacts/capacity/constrained-10.json
```

Обычные прогоны измерены на Windows/.NET 10.0.13; Linux-тест использует runtime 10.0.8
из установленного образа. HTTP TestServer не включает TLS, Caddy, браузер и сетевую задержку.
В синтетической БД нет 90-дневного архива расчётов/ответов и годовой истории.

Более близкий к VPS вариант проверен с общим набором двух CPU для обоих контейнеров.
Перед ним выполнено `docker update --cpus=2 --cpuset-cpus=0,1 ecomads-capacity-db`.
В команде приложения заменены CPU-флаги на `--cpus=2 --cpuset-cpus=0,1`, добавлены
`ECOMADS_CAPACITY_APP_LIMITS=2 shared logical CPUs (cpuset 0,1) / 1024 MiB` и
`ECOMADS_CAPACITY_DB_LIMITS=Same 2 logical CPUs (cpuset 0,1), shared with app / 512 MiB`.
Результат — `artifacts/capacity/shared-2cpu-10.json`. Оба процесса конкурируют за одни
и те же два логических процессора; это не сумма двух отдельных лимитов по два CPU.

## Лимиты WB

```powershell
python ops/capacity/wb-api-limits.py
python ops/capacity/request-budget.py
python ops/capacity/recommendation-storage.py
python -m unittest discover -s ops/capacity -p test_*.py -v
```

Первый скрипт скачивает только публичные официальные спецификации и сохраняет таблицы
лимитов с URL и контрольной суммой источника. Модель бюджета использует текущие интервалы
приложения и идеальный двухминутный проход. Она оценивает спрос, а не достигнутую частоту:
длительные проходы, ошибки и занятые слоты сокращают фактическое число запросов.
Синтетические проверки Slots проверяют допущения модели; это не тест реального WbRateLimits.
Модель recommendation-storage считает размер архива немедленно по числу кампаний,
частоте новых версий, размеру записи и сроку хранения. Проценты срабатывания правил
задаются сценариями, а не выдаются за наблюдаемые бизнес-вероятности.
Один профиль продавца WB сохраняет общий лимит даже при нескольких локальных подключениях.
Сторонние интеграции продавца могут расходовать доступный бюджет.

## Суточное наблюдение production

Запускавшийся 22 октября сборщик: `ecomads-capacity-20261022.service`.
Остановлен по просьбе пользователя после примерно 48 минут; автоматизация итогов удалена.
Данные сохранены. Описание ниже фиксирует параметры выполненного запуска.
Результаты: `/opt/ecomads/capacity/run-20261022/`.
Он завершится сам после 24 часов; systemd ограничивает его 10% CPU, 128 MiB и 25 часами.
Минутные снимки системы и Docker, SQL-агрегаты раз в 15 минут; SQL-транзакции read-only,
таймаут запроса 3 секунды, ожидания блокировки 500 мс. Лимит выходного файла 32 MiB.
Контейнеры приложения не перезапускаются. Данные клиентов и тела WB-ответов не выгружаются.

```powershell
ssh my-vps "systemctl status ecomads-capacity-20261022 --no-pager"
scp my-vps:/opt/ecomads/capacity/run-20261022/samples.jsonl artifacts/capacity/vps-samples.jsonl
scp my-vps:/opt/ecomads/capacity/run-20261022/completed.json artifacts/capacity/vps-completed.json
python ops/capacity/summarize-observations.py artifacts/capacity/vps-samples.jsonl --output artifacts/capacity/vps-summary.json
```

Системные timestamps в файлах — UTC. Даты пользовательского отчёта — Asia/Vladivostok.
Досрочная остановка только сборщика: `ssh my-vps "systemctl stop ecomads-capacity-20261022"`.
Статусы очереди в агрегатах включают старые задания; `eligible` проверяет время и паузу,
но не все условия разрешения кабинета/rollout. Возраст задания сам по себе не доказывает
реальную задержку свежих данных. Журнал WB не охватывает все оперативные запросы.

По завершении локальной работы отдельный контейнер можно остановить:
`docker stop ecomads-capacity-db`. Не останавливать существующий пользовательский `ecomads-db`.

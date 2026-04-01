1. Аутентификация и пользователи
1.1. Регистрация и аутентификация
POST /api/auth/register - Регистрация нового пользователя (email, пароль)
POST /api/auth/login - Авторизация пользователя по email/пароль
POST /api/auth/telegram - Авторизация через Telegram
POST /api/auth/logout - Выход из системы
POST /api/auth/refresh - Обновление токена доступа
GET /api/auth/me - Получение данных текущего пользователя
1.2. Управление профилем
GET /api/users/profile - Получение профиля пользователя
PUT /api/users/profile - Обновление профиля пользователя
PUT /api/users/password - Изменение пароля
POST /api/users/telegram/connect - Подключение Telegram для уведомлений
DELETE /api/users/telegram - Отключение Telegram
2. Управление рекламной кампанией (номенклатурой)
GET /api/projects - Получение списка РК пользователя
POST /api/projects - Создание новой РК
GET /api/projects/{id} - Получение данных конкретного РК
PUT /api/projects/{id} - Обновление РК
DELETE /api/projects/{id} - Удаление РК
PUT /api/projects/{id}/goals - Установка целевых показателей (ДРР, режим приоритета, пороги алертов)
3. Импорт данных
POST /api/projects/{projectId}/import - Загрузка файла данных (CSV/XLSX)
GET /api/projects/{projectId}/import/status - Получение статуса загрузки
GET /api/projects/{projectId}/import/history - История загрузок
GET /api/projects/{projectId}/import/validation - Результаты валидации последней загрузки
4. Кампании и статистика
GET /api/projects/{projectId}/campaigns - Получение списка кампаний
GET /api/projects/{projectId}/campaigns/{campaignId} - Детальная информация по кампании
GET /api/projects/{projectId}/campaigns/{campaignId}/stats - Статистика кампании за период
GET /api/projects/{projectId}/campaigns/{campaignId}/keywords - Ключевые слова кампании
GET /api/projects/{projectId}/campaigns/{campaignId}/keywords/{keywordId}/stats - Статистика по ключевому слову
5. Аналитика и дашборды
GET /api/projects/{projectId}/dashboard - Данные для основного дашборда
GET /api/projects/{projectId}/analytics/kpi - Основные KPI проекта за период
GET /api/projects/{projectId}/analytics/drr - Анализ ДРР за период
GET /api/projects/{projectId}/analytics/campaigns - Сводная таблица кампаний с метриками
6. Оповещения (алерты)
GET /api/projects/{projectId}/alerts - Получение списка активных оповещений
GET /api/projects/{projectId}/alerts/history - История оповещений
PUT /api/projects/{projectId}/alerts/{alertId} - Обновление статуса оповещения (прочитано/непрочитано)
PUT /api/projects/{projectId}/alerts/settings - Настройка триггеров оповещений
7. Рекомендации
GET /api/projects/{projectId}/recommendations - Получение списка активных рекомендаций
GET /api/projects/{projectId}/recommendations/{recommendationId} - Детальная информация по рекомендации
PUT /api/projects/{projectId}/recommendations/{recommendationId}/status - Обновление статуса рекомендации (принято/отложено/неактуально)
GET /api/projects/{projectId}/recommendations/history - История рекомендаций
8. Отчеты "Эффект сервиса"
GET /api/projects/{projectId}/reports/impact - Получение отчета "Эффект сервиса" за период
GET /api/projects/{projectId}/reports/time-saved - Отчет по экономии времени
GET /api/projects/{projectId}/reports/drr-change - Отчет по изменению ДРР
9. Системные эндпоинты
GET /api/health - Проверка доступности API
GET /api/version - Информация о текущей версии API
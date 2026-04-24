// /js/api.js

// Базовая функция для выполнения авторизованных запросов
export const fetchWithAuth = async (url, options = {}) => {
    const token = localStorage.getItem('ecomads_token');
    
    // Добавляем заголовок авторизации к запросу
    const authOptions = {
        ...options,
        headers: {
            ...options.headers,
            'Authorization': token ? `Bearer ${token}` : '',
            'Content-Type': 'application/json'
        }
    };
    
    const response = await fetch(url, authOptions);
    
    // Если сервер вернул 401 Unauthorized, токен недействителен
    if (response.status === 401) {
        //localStorage.removeItem('ecomads_token');
        window.location.href = '/index.html';
        return null;
    }
    
    return response;
};

export const getCampaignsData = async () => {
    try {
        const response = await fetchWithAuth('/api/projects');
        if (!response || !response.ok) throw new Error('Ошибка сети');
        return await response.json();
    } catch (error) {
        console.error('Ошибка загрузки кампаний:', error);
        return [];
    }
};

export const getDashboardData = async () => {
    // Временная заглушка, пока не будет готов контроллер для KPI
    return {
        kpis: {
            spent: "124 560 ₽",
            orders: "847",
            drr: "18.5%",
            ctr: "4.2%"
        }
    };
};

// API для получения статистики рекомендаций
export const getRecommendationsStats = async (period = 'month') => {
    try {
        // Используем реальный API для получения данных
        const response = await fetchWithAuth(`/api/recommendations/stats?period=${period}`);
        
        if (!response || !response.ok) {
            // Если API не работает или вернул ошибку, используем моковые данные
            console.warn('Не удалось получить данные с сервера, используем моковые данные');
            return getMockRecommendationsStats(period);
        }
        
        // Получаем данные с сервера
        const data = await response.json();
        console.log('Получены данные с сервера:', data);
        
        // Проверяем структуру данных
        if (!data) {
            console.error('API вернул пустой ответ');
            return getMockRecommendationsStats(period);
        }
        
        // Корректируем данные, если требуется
        const result = {
            counts: data.counts || { accepted: 0, pending: 0, rejected: 0 },
            monthly: data.monthly || [],
            recommendations: []
        };
        
        // Обрабатываем массив рекомендаций
        if (data.recommendations && Array.isArray(data.recommendations)) {
            result.recommendations = data.recommendations.map(rec => ({
                id: rec.id || '',
                text: rec.text || '',
                status: rec.status || 'новая',
                date: rec.date || new Date().toISOString(),
                campaign: rec.campaign || 'Не указано',
                comment: rec.comment || ''
            }));
        } else {
            console.warn('API не вернул массив рекомендаций');
        }
        
        console.log('Преобразованные данные для UI:', result);
        return result;
    } catch (error) {
        console.error('Ошибка загрузки статистики рекомендаций:', error);
        // При ошибке также возвращаем моковые данные
        console.warn('Из-за ошибки используем моковые данные');
        return getMockRecommendationsStats(period);
    }
};

// Функция для генерации моковых данных
function getMockRecommendationsStats(period) {
    // Настраиваем количество записей в зависимости от выбранного периода
    let counts = { accepted: 0, pending: 0, rejected: 0 };
    let monthly = [];
    let recommendations = [];
    
    // Генерируем разные данные в зависимости от периода
    switch(period) {
        case 'week':
            counts = { accepted: 8, pending: 3, rejected: 2 };
            monthly = generateMonthlyData(3); // Последние 3 месяца
            recommendations = generateMockRecommendations(5);
            break;
        case 'month':
            counts = { accepted: 24, pending: 11, rejected: 7 };
            monthly = generateMonthlyData(6); // Последние 6 месяцев
            recommendations = generateMockRecommendations(10);
            break;
        case 'quarter':
            counts = { accepted: 57, pending: 23, rejected: 18 };
            monthly = generateMonthlyData(6); // Последние 6 месяцев
            recommendations = generateMockRecommendations(15);
            break;
        case 'year':
            counts = { accepted: 142, pending: 56, rejected: 34 };
            monthly = generateMonthlyData(12); // Все 12 месяцев
            recommendations = generateMockRecommendations(20);
            break;
    }
    
    return { counts, monthly, recommendations };
}

// Генерация данных по месяцам
function generateMonthlyData(monthCount) {
    const months = ['Янв', 'Фев', 'Март', 'Апр', 'Май', 'Июнь', 'Июль', 'Авг', 'Сен', 'Окт', 'Ноя', 'Дек'];
    const currentMonth = new Date().getMonth();
    const result = [];
    
    for (let i = 0; i < monthCount; i++) {
        const monthIndex = (currentMonth - monthCount + i + 12) % 12; // Получаем правильный индекс месяца
        
        // Генерируем случайные данные
        const accepted = Math.floor(Math.random() * 20) + 5;
        const pending = Math.floor(Math.random() * 10) + 1;
        const rejected = Math.floor(Math.random() * 8) + 1;
        const total = accepted + pending + rejected;
        
        result.push({
            month: months[monthIndex],
            accepted,
            pending,
            rejected,
            total
        });
    }
    
    return result;
}

// Генерация примеров рекомендаций
function generateMockRecommendations(count) {
    const recommendations = [
        {
            text: 'Увеличить ставки для ключевого слова "Смартфон Samsung" на 15%',
            campaign: 'Электроника 2023',
            status: 'accepted',
            comment: 'Ставки повышены, наблюдается рост показов'
        },
        {
            text: 'Добавить минус-слова для кампании "Детские товары"',
            campaign: 'Детские товары',
            status: 'pending',
            comment: 'Необходимо уточнить список минус-слов'
        },
        {
            text: 'Остановить показы для площадки social.example.com',
            campaign: 'Общая кампания',
            status: 'rejected',
            comment: 'Площадка приносит конверсии, остановка нецелесообразна'
        },
        {
            text: 'Увеличить бюджет кампании "Зимняя обувь" на 30%',
            campaign: 'Зимняя обувь',
            status: 'accepted',
            comment: 'Бюджет увеличен'
        },
        {
            text: 'Изменить таргетинг кампании на возрастную группу 25-44',
            campaign: 'Аксессуары',
            status: 'pending',
            comment: ''
        },
        {
            text: 'Добавить объявления для новой линейки товаров',
            campaign: 'Новинки',
            status: 'accepted',
            comment: 'Объявления добавлены'
        },
        {
            text: 'Оптимизировать мобильные ставки (-20%)',
            campaign: 'Электроника 2023',
            status: 'rejected',
            comment: 'Большая часть трафика приходит с мобильных устройств'
        },
        {
            text: 'Добавить быстрые ссылки в объявления',
            campaign: 'Бытовая техника',
            status: 'accepted',
            comment: 'Ссылки добавлены'
        },
        {
            text: 'Исключить нецелевые регионы из таргетинга',
            campaign: 'Региональная',
            status: 'pending',
            comment: 'В процессе анализа регионов'
        },
        {
            text: 'Создать отдельную кампанию для брендового поиска',
            campaign: 'Общая кампания',
            status: 'accepted',
            comment: ''
        },
        {
            text: 'Увеличить ставки в прайм-тайм на 25%',
            campaign: 'Активная',
            status: 'rejected',
            comment: 'Недостаточный бюджет'
        }
    ];
    
    // Перемешиваем рекомендации
    const shuffled = [...recommendations].sort(() => 0.5 - Math.random());
    
    // Берем нужное количество и добавляем даты (случайные в пределах последних 30 дней)
    return shuffled.slice(0, count).map(rec => {
        const daysAgo = Math.floor(Math.random() * 30) + 1;
        const date = new Date();
        date.setDate(date.getDate() - daysAgo);
        
        return {
            ...rec,
            date: date.toISOString()
        };
    }).sort((a, b) => new Date(b.date) - new Date(a.date)); // Сортируем по дате (новые сверху)
}

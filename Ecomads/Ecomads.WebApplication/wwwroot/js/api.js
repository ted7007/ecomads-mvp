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

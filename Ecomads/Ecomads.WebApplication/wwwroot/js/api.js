// /js/api.js
export const getCampaignsData = async () => {
    try {
        const response = await fetch('/api/projects');
        if (!response.ok) throw new Error('Ошибка сети');
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

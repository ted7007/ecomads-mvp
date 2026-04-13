// /js/main.js
import { getDashboardData, getCampaignsData } from './api.js';

document.addEventListener('DOMContentLoaded', async () => {
    // ... (код инициализации иконок)
    if (typeof feather !== 'undefined') {
        feather.replace({ strokeWidth: 1.5, width: 18, height: 18 });
    }

    // Загрузка данных
    try {
        const [dashboardData, campaignsData] = await Promise.all([
            getDashboardData(),
            getCampaignsData()
        ]);
        updateDashboardUI(dashboardData, campaignsData);
    } catch (error) {
        console.error('Ошибка загрузки данных:', error);
    }
});

function updateDashboardUI(data, campaigns) {
    // ... (код обновления KPI остался)

    // Обновление таблицы кампаний
    const body = document.getElementById('campaigns-body');
    if (body) {
        body.innerHTML = campaigns.map(item => `
            <tr>
                <td><span class="row-indicator indicator-green"></span> ${item.name}</td>
                <td>${item.kpi.spend.toLocaleString()} ₽</td>
                <td>${item.kpi.drr.toFixed(1)}%</td>
                <td>${item.kpi.clicks}</td>
                <td>${item.kpi.ctr.toFixed(2)}%</td>
                <td>
                    <button onclick="window.location.href='/campaign.html?id=${item.id}'" style="background:none; border:none; cursor:pointer;">
                        <i data-feather="eye"></i>
                    </button>
                </td>
            </tr>
        `).join('');
        
        if (typeof feather !== 'undefined') {
            feather.replace({ strokeWidth: 1.5, width: 18, height: 18 });
        }
    }
}

// /js/main.js
import { getDashboardData, getCampaignsData } from './api.js';

document.addEventListener('DOMContentLoaded', async () => {
    // Инициализация иконок Feather
    if (typeof feather !== 'undefined') {
        feather.replace({ strokeWidth: 1.5, width: 18, height: 18 });
    }

    // Загрузка данных для дашборда
    if (document.getElementById('dashboard-content')) {
        try {
            const [dashboardData, campaignsData] = await Promise.all([
                getDashboardData(),
                getCampaignsData()
            ]);
            updateDashboardUI(dashboardData, campaignsData);
        } catch (error) {
            console.error('Ошибка загрузки данных:', error);
        }
    }
});

function updateDashboardUI(data, campaigns) {
    const elements = {
        'kpi-spent': data.kpis.spent,
        'kpi-orders': data.kpis.orders,
        'kpi-drr': data.kpis.drr,
        'kpi-ctr': data.kpis.ctr
    };

    for (const [id, value] of Object.entries(elements)) {
        const el = document.getElementById(id);
        if (el) el.textContent = value;
    }

    const body = document.getElementById('campaigns-body');
    if (body) {
        body.innerHTML = campaigns.map(item => `
            <tr>
                <td><span class="row-indicator indicator-green"></span> ${item.name}</td>
                <td>${item.kpi.spend.toLocaleString()} ₽</td>
                <td>${item.kpi.drr.toFixed(1)}%</td>
                <td>${item.kpi.clicks}</td>
                <td>${(item.kpi.ctr * 100).toFixed(2)}%</td>
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

export async function loadKeywordStats(campaignId) {
    try {
        const response = await fetch(`/api/statistics/keywords/${campaignId}`);
        if (!response.ok) throw new Error('Ошибка загрузки ключей');
        const data = await response.json();
        
        const body = document.getElementById('keywords-body');
        if (body) {
            body.innerHTML = data.map(item => `
                <tr>
                    <td>${item.phrase}</td>
                    <td>${item.impressions}</td>
                    <td>${item.clicks}</td>
                    <td>${(item.ctr / 100).toFixed(2)}%</td>
                    <td>${item.spend.toLocaleString()} ₽</td>
                    <td>${item.orders}</td>
                    <td>${item.revenue.toLocaleString()} ₽</td>
                    <td class="${item.drr > 20 ? 'drr-red' : 'drr-green'}">${item.drr.toFixed(1)}%</td>
                </tr>
            `).join('');
        }
    } catch (error) {
        console.error('Ошибка загрузки ключевых слов:', error);
    }
}

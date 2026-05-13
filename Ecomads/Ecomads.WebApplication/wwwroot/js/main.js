// /js/main.js
import { getDashboardData, getCampaignsData } from './api.js';
import { fetchWithAuth } from './modal.js';

document.addEventListener('DOMContentLoaded', async () => {
    if (typeof feather !== 'undefined') {
        feather.replace({ strokeWidth: 1.5, width: 18, height: 18 });
    }

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

function updateDashboardUI(_data, campaigns) {
    const totals = campaigns.reduce((acc, item) => {
        acc.spend += item.kpi.spend || 0;
        acc.revenue += item.kpi.revenue || 0;
        acc.earnings += item.kpi.earnings || 0;
        acc.clicks += item.kpi.clicks || 0;
        return acc;
    }, { spend: 0, revenue: 0, earnings: 0, clicks: 0 });

    const avgDrr = totals.revenue > 0 ? (totals.spend / totals.revenue) * 100 : 0;
    const weightedCtr = totals.clicks > 0
        ? campaigns.reduce((sum, item) => sum + ((item.kpi.ctr || 0) * (item.kpi.clicks || 0)), 0) / totals.clicks
        : 0;

    const elements = {
        'kpi-earnings': `${Math.round(totals.earnings).toLocaleString()} ₽`,
        'kpi-spent': `${Math.round(totals.spend).toLocaleString()} ₽`,
        'kpi-orders': totals.clicks.toLocaleString(),
        'kpi-drr': `${avgDrr.toFixed(1)}%`,
        'kpi-ctr': `${weightedCtr.toFixed(2)}%`
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

export async function loadKeywordStats(campaignId) {
    try {
        const response = await fetchWithAuth(`/api/statistics/keywords/${campaignId}`);
        if (!response || !response.ok) throw new Error('Ошибка загрузки ключей');
        const data = await response.json();

        const body = document.getElementById('keywords-body');
        if (body) {
            body.innerHTML = data.map(item => `
                <tr>
                    <td>${item.phrase}</td>
                    <td>${item.impressions}</td>
                    <td>${item.clicks}</td>
                    <td>${item.ctr.toFixed(2)}%</td>
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

export async function loadCampaignSummary(campaignId) {
    try {
        const campaigns = await getCampaignsData();
        const normalizedCampaignId = String(campaignId).toLowerCase();
        const campaign = campaigns.find(x => String(x.id).toLowerCase() === normalizedCampaignId);
        if (!campaign) {
            return;
        }

        const setText = (id, value) => {
            const el = document.getElementById(id);
            if (el) el.textContent = value;
        };

        const kpi = campaign.kpi;
        setText('campaign-name', campaign.name);
        setText('campaign-kpi-earnings', `${Math.round(kpi.earnings).toLocaleString()} ₽`);
        setText('campaign-kpi-spent', `${Math.round(kpi.spend).toLocaleString()} ₽`);
        setText('campaign-kpi-orders', `${(kpi.clicks || 0).toLocaleString()}`);
        setText('campaign-kpi-drr', `${(kpi.drr || 0).toFixed(1)}%`);
        setText('campaign-kpi-ctr', `${(kpi.ctr || 0).toFixed(2)}%`);
    } catch (error) {
        console.error('Ошибка загрузки KPI кампании:', error);
    }
}

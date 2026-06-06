// /js/main.js
import * as api from './api.js';

document.addEventListener('DOMContentLoaded', async () => {
    if (typeof feather !== 'undefined') {
        feather.replace({ strokeWidth: 1.5, width: 18, height: 18 });
    }

    if (document.getElementById('dashboard-content')) {
        await setupDashboardPeriodControls();
        await loadDashboardData();
    }
});

let dashboardFilter = { startDate: '', endDate: '' };
let keywordRows = [];
let keywordSort = { field: '', direction: 'asc' };

async function setupDashboardPeriodControls() {
    const periodSelect = document.getElementById('dashboard-period-select');
    const startInput = document.getElementById('dashboard-start-date');
    const endInput = document.getElementById('dashboard-end-date');
    const applyBtn = document.getElementById('dashboard-apply-period');

    if (!periodSelect || !startInput || !endInput || !applyBtn) {
        return;
    }

    const periods = api.getLoadedPeriods ? await api.getLoadedPeriods() : [];
    for (const p of periods) {
        const start = formatDateForInput(p.startDate);
        const end = formatDateForInput(p.endDate);
        const option = document.createElement('option');
        option.value = `${start}|${end}`;
        option.textContent = `${start} - ${end}`;
        periodSelect.appendChild(option);
    }

    periodSelect.addEventListener('change', () => {
        if (!periodSelect.value) {
            startInput.value = '';
            endInput.value = '';
            return;
        }

        const [start, end] = periodSelect.value.split('|');
        startInput.value = start;
        endInput.value = end;
    });

    applyBtn.addEventListener('click', async () => {
        dashboardFilter = {
            startDate: startInput.value,
            endDate: endInput.value
        };
        await loadDashboardData();
    });
}

async function loadDashboardData() {
    try {
        const campaignsData = await api.getCampaignsData(dashboardFilter);
        updateDashboardUI(campaignsData);
    } catch (error) {
        console.error('Ошибка загрузки данных:', error);
    }
}

function updateDashboardUI(campaigns) {
    const totals = campaigns.reduce((acc, item) => {
        acc.spend += item.kpi.spend || 0;
        acc.revenue += item.kpi.revenue || 0;
        acc.orderedAmount += item.kpi.orderedAmount || 0;
        acc.clicks += item.kpi.clicks || 0;
        return acc;
    }, { spend: 0, revenue: 0, orderedAmount: 0, clicks: 0 });

    const avgDrr = totals.revenue > 0 ? (totals.spend / totals.revenue) * 100 : 0;
    const weightedCtr = totals.clicks > 0
        ? campaigns.reduce((sum, item) => sum + ((item.kpi.ctr || 0) * (item.kpi.clicks || 0)), 0) / totals.clicks
        : 0;

    const elements = {
        'kpi-ordered-amount': `${Math.round(totals.orderedAmount).toLocaleString()} ₽`,
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

export async function loadKeywordStats(campaignId, filters = {}) {
    try {
        const query = new URLSearchParams();
        if (filters.startDate) query.set('startDate', filters.startDate);
        if (filters.endDate) query.set('endDate', filters.endDate);
        const suffix = query.toString() ? `?${query.toString()}` : '';
        const endpoint = `/api/statistics/keywords/${campaignId}${suffix}`;

        const response = await api.fetchWithAuth(endpoint);
        if (!response || !response.ok) throw new Error('Ошибка загрузки ключей');

        const contentType = response.headers.get('content-type') || '';
        if (!contentType.includes('application/json')) {
            const raw = await response.text();
            throw new Error(`Ожидался JSON, получен ${contentType || 'unknown'}: ${raw.slice(0, 120)}`);
        }

        keywordRows = await response.json();
        renderKeywordRows();
    } catch (error) {
        console.error('Ошибка загрузки ключевых слов:', error);
    }
}

function renderKeywordRows() {
    const body = document.getElementById('keywords-body');
    if (!body) {
        return;
    }

    const rows = getSortedKeywordRows();
    body.innerHTML = rows.map(item => `
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

function getSortedKeywordRows() {
    if (!keywordSort.field) {
        return keywordRows;
    }

    const direction = keywordSort.direction === 'asc' ? 1 : -1;
    return [...keywordRows].sort((a, b) => {
        const left = a[keywordSort.field];
        const right = b[keywordSort.field];

        if (typeof left === 'string' || typeof right === 'string') {
            return String(left ?? '').localeCompare(String(right ?? ''), 'ru') * direction;
        }

        return ((Number(left) || 0) - (Number(right) || 0)) * direction;
    });
}

function setupKeywordSortHandlers() {
    const headers = document.querySelectorAll('.keyword-sortable[data-sort]');
    if (!headers.length) {
        return;
    }

    for (const header of headers) {
        if (header.dataset.sortReady === 'true') {
            continue;
        }

        header.dataset.sortReady = 'true';
        header.addEventListener('click', () => {
            const field = header.dataset.sort;
            const nextDirection = keywordSort.field === field && keywordSort.direction === 'asc' ? 'desc' : 'asc';
            keywordSort = { field, direction: nextDirection };

            for (const h of headers) {
                h.removeAttribute('data-sort-dir');
            }

            header.setAttribute('data-sort-dir', nextDirection);
            renderKeywordRows();
        });
    }
}

export async function loadCampaignSummary(campaignId, filters = {}) {
    try {
        const campaigns = await api.getCampaignsData(filters);
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
        setText('campaign-kpi-ordered-amount', `${Math.round(kpi.orderedAmount || 0).toLocaleString()} ₽`);
        setText('campaign-kpi-spent', `${Math.round(kpi.spend).toLocaleString()} ₽`);
        setText('campaign-kpi-orders', `${(kpi.clicks || 0).toLocaleString()}`);
        setText('campaign-kpi-drr', `${(kpi.drr || 0).toFixed(1)}%`);
        setText('campaign-kpi-ctr', `${(kpi.ctr || 0).toFixed(2)}%`);
    } catch (error) {
        console.error('Ошибка загрузки KPI кампании:', error);
    }
}

export async function setupCampaignPeriodControls(campaignId) {
    setupKeywordSortHandlers();

    const periodSelect = document.getElementById('campaign-period-select');
    const startInput = document.getElementById('campaign-start-date');
    const endInput = document.getElementById('campaign-end-date');
    const applyBtn = document.getElementById('campaign-apply-period');

    if (!periodSelect || !startInput || !endInput || !applyBtn) {
        await loadCampaignSummary(campaignId);
        await loadKeywordStats(campaignId);
        return;
    }

    const periods = api.getLoadedPeriods ? await api.getLoadedPeriods() : [];
    for (const p of periods) {
        const start = formatDateForInput(p.startDate);
        const end = formatDateForInput(p.endDate);
        const option = document.createElement('option');
        option.value = `${start}|${end}`;
        option.textContent = `${start} - ${end}`;
        periodSelect.appendChild(option);
    }

    const applyFilters = async () => {
        const filters = {
            startDate: startInput.value,
            endDate: endInput.value
        };

        await loadCampaignSummary(campaignId, filters);
        await loadKeywordStats(campaignId, filters);
    };

    periodSelect.addEventListener('change', () => {
        if (!periodSelect.value) {
            startInput.value = '';
            endInput.value = '';
            return;
        }

        const [start, end] = periodSelect.value.split('|');
        startInput.value = start;
        endInput.value = end;
    });

    applyBtn.addEventListener('click', applyFilters);
    await applyFilters();
}

function formatDateForInput(dateValue) {
    if (!dateValue) {
        return '';
    }

    const d = new Date(dateValue);
    const y = d.getUTCFullYear();
    const m = String(d.getUTCMonth() + 1).padStart(2, '0');
    const day = String(d.getUTCDate()).padStart(2, '0');
    return `${y}-${m}-${day}`;
}

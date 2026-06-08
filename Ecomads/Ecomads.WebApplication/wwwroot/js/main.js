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
let keywordOverlay = null;
let keywordOverlayFilters = {};
let keywordOverlayCampaignId = '';
let keywordStatusFilter = 'All';
let selectedInsightId = '';

const STATUS_LABELS = {
    ToRemove: 'К удалению',
    NeedsAttention: 'Требует внимания',
    Effective: 'Эффективное',
    Watch: 'Наблюдать',
    LowData: 'Мало данных',
    Neutral: 'Без рекомендации'
};

const STATUS_ORDER = ['ToRemove', 'NeedsAttention', 'Effective', 'Watch', 'LowData', 'Neutral'];
const DECISION_LABELS = {
    None: 'Не выбрано',
    Accepted: 'Принято',
    Postponed: 'Отложено',
    Rejected: 'Отклонено'
};

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
        keywordOverlayCampaignId = campaignId;
        keywordOverlayFilters = { ...filters };
        keywordOverlay = await loadKeywordOverlay(campaignId, filters);

        if (keywordOverlay) {
            keywordRows = normalizeOverlayRows(keywordOverlay.keywords || []);
            renderKeywordOverlaySummary(keywordOverlay);
            renderKeywordStatusFilters(keywordOverlay);
            renderKeywordRows();
            return;
        }

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

        keywordRows = normalizeLegacyKeywordRows(await response.json());
        renderKeywordOverlaySummary(null);
        renderKeywordStatusFilters(null);
        renderKeywordRows();
    } catch (error) {
        console.error('Ошибка загрузки ключевых слов:', error);
    }
}

async function loadKeywordOverlay(campaignId, filters = {}) {
    try {
        const query = new URLSearchParams();
        if (filters.startDate) query.set('startDate', filters.startDate);
        if (filters.endDate) query.set('endDate', filters.endDate);
        const suffix = query.toString() ? `?${query.toString()}` : '';
        const endpoint = `/api/recommendations/campaign/${campaignId}/keyword-overlay${suffix}`;

        const response = await api.fetchWithAuth(endpoint);
        if (!response || !response.ok) {
            return null;
        }

        return await response.json();
    } catch (error) {
        console.warn('Recommendation overlay недоступен, используется обычная статистика:', error);
        return null;
    }
}

function renderKeywordRows() {
    const body = document.getElementById('keywords-body');
    if (!body) {
        return;
    }

    const rows = getSortedKeywordRows();
    if (!rows.length) {
        body.innerHTML = '<tr><td colspan="10" class="keyword-empty-cell">Нет данных за выбранный период</td></tr>';
        const panel = document.getElementById('keyword-insight-panel');
        if (panel) {
            panel.hidden = true;
            panel.innerHTML = '';
        }
        return;
    }

    body.innerHTML = rows.map(item => `
        <tr class="keyword-row keyword-status-${item.status}" data-insight-id="${escapeHtml(item.mainInsightId || '')}" data-keyword-id="${escapeHtml(item.keywordId || '')}">
            <td class="keyword-phrase-cell">${escapeHtml(item.phrase)}</td>
            <td>${renderStatusBadge(item.status, item.decisionStatus)}</td>
            <td class="keyword-recommendation-cell">${escapeHtml(item.shortRecommendation || '-')}</td>
            <td>${formatInteger(item.impressions)}</td>
            <td>${formatInteger(item.clicks)}</td>
            <td>${formatPercent(item.ctr, 2)}</td>
            <td>${formatCurrency(item.spend)}</td>
            <td>${formatInteger(item.orders)}</td>
            <td>${formatCurrency(item.revenue)}</td>
            <td class="${getDrrClass(item.drr)}">${formatPercent(item.drr, 1)}</td>
        </tr>
    `).join('');

    setupKeywordRowHandlers(body);
}

function getSortedKeywordRows() {
    const filteredRows = keywordStatusFilter === 'All'
        ? keywordRows
        : keywordRows.filter(row => row.status === keywordStatusFilter);

    if (!keywordSort.field) {
        return filteredRows;
    }

    const direction = keywordSort.direction === 'asc' ? 1 : -1;
    return [...filteredRows].sort((a, b) => {
        const left = a[keywordSort.field];
        const right = b[keywordSort.field];

        if (typeof left === 'string' || typeof right === 'string') {
            return String(left ?? '').localeCompare(String(right ?? ''), 'ru') * direction;
        }

        return ((Number(left) || 0) - (Number(right) || 0)) * direction;
    });
}

function normalizeOverlayRows(rows) {
    return rows.map(row => ({
        ...(() => {
            const impressions = row.views ?? row.impressions ?? 0;
            const clicks = row.clicks ?? 0;
            const spend = toNumber(row.spend);
            const revenue = toNumber(row.revenue);

            return {
                impressions,
                clicks,
                spend,
                revenue,
                ctr: calculateCtr(clicks, impressions, row.ctr),
                drr: calculateDrr(spend, revenue, row.drr)
            };
        })(),
        keywordId: row.keywordId || '',
        phrase: row.phrase || '',
        status: normalizeStatus(row.status),
        shortRecommendation: row.shortRecommendation || '',
        orders: row.orders ?? 0,
        priorityScore: toNumber(row.priorityScore),
        priorityLevel: normalizePriority(row.priorityLevel),
        confidenceLevel: normalizeConfidence(row.confidenceLevel),
        recommendedAction: row.recommendedAction,
        mainInsightId: row.mainInsightId || '',
        hasInsight: Boolean(row.hasInsight),
        decisionStatus: normalizeDecision(row.decisionStatus)
    }));
}

function normalizeLegacyKeywordRows(rows) {
    return rows.map(row => ({
        ...(() => {
            const impressions = row.impressions ?? row.views ?? 0;
            const clicks = row.clicks ?? 0;
            const spend = toNumber(row.spend);
            const revenue = toNumber(row.revenue);

            return {
                impressions,
                clicks,
                spend,
                revenue,
                ctr: calculateCtr(clicks, impressions, row.ctr),
                drr: calculateDrr(spend, revenue, row.drr)
            };
        })(),
        keywordId: row.id || row.keywordId || '',
        phrase: row.phrase || '',
        status: 'Neutral',
        shortRecommendation: '',
        orders: row.orders ?? 0,
        priorityScore: 0,
        priorityLevel: 'Low',
        confidenceLevel: 'Low',
        recommendedAction: null,
        mainInsightId: '',
        hasInsight: false,
        decisionStatus: 'None'
    }));
}

function renderKeywordOverlaySummary(overlay) {
    const summary = document.getElementById('keyword-overlay-summary');
    const mode = document.getElementById('keyword-overlay-mode');
    if (!summary || !mode) {
        return;
    }

    if (!overlay) {
        mode.textContent = 'Статистика';
        summary.hidden = true;
        summary.innerHTML = '';
        return;
    }

    mode.textContent = 'Recommendation overlay';
    const generated = overlay.generatedAt
        ? new Date(overlay.generatedAt).toLocaleString('ru-RU')
        : 'нет генерации';
    const text = overlay.recommendationSummary?.text || 'По текущим правилам нет активных рекомендаций.';
    const withoutLlm = overlay.recommendationSummary?.generatedWithoutLlm ? ' · fallback' : '';

    summary.hidden = false;
    summary.innerHTML = `
        <div class="keyword-overlay-summary-text">${escapeHtml(text)}</div>
        <div class="keyword-overlay-summary-meta">${escapeHtml(generated)}${withoutLlm}</div>
    `;
}

function renderKeywordStatusFilters(overlay) {
    const container = document.getElementById('keyword-status-filters');
    if (!container) {
        return;
    }

    if (!overlay) {
        container.hidden = true;
        container.innerHTML = '';
        keywordStatusFilter = 'All';
        return;
    }

    const counts = overlay.recommendationSummary?.counts || {};
    const total = (overlay.keywords || []).length;
    const buttons = [
        { status: 'All', label: 'Все', count: total },
        ...STATUS_ORDER.map(status => ({
            status,
            label: STATUS_LABELS[status],
            count: getStatusCount(counts, status)
        }))
    ];

    container.hidden = false;
    container.innerHTML = buttons.map(item => `
        <button class="keyword-status-filter ${keywordStatusFilter === item.status ? 'active' : ''}" data-status="${item.status}">
            <span>${escapeHtml(item.label)}</span>
            <strong>${item.count}</strong>
        </button>
    `).join('');

    for (const button of container.querySelectorAll('.keyword-status-filter')) {
        button.addEventListener('click', () => {
            keywordStatusFilter = button.dataset.status || 'All';
            renderKeywordStatusFilters(keywordOverlay);
            renderKeywordRows();
        });
    }
}

function setupKeywordRowHandlers(body) {
    for (const row of body.querySelectorAll('.keyword-row')) {
        row.addEventListener('click', () => {
            const insightId = row.dataset.insightId || '';
            const keywordId = row.dataset.keywordId || '';
            selectedInsightId = insightId;
            body.querySelectorAll('.keyword-row').forEach(item => item.classList.remove('selected'));
            row.classList.add('selected');
            renderKeywordInsightPanel(findInsightDetail(insightId), findKeywordRow(keywordId));
        });
    }
}

function renderKeywordInsightPanel(insight, keyword = null) {
    const panel = document.getElementById('keyword-insight-panel');
    if (!panel) {
        return;
    }

    if (!keywordOverlay) {
        panel.hidden = true;
        panel.innerHTML = '';
        return;
    }

    if (!insight) {
        const fallbackKeyword = keyword || {};
        panel.hidden = false;
        panel.innerHTML = `
            <div class="keyword-insight-panel-head">
                <div>
                    <span class="keyword-panel-eyebrow">Ключевое слово</span>
                    <h3>${escapeHtml(fallbackKeyword.phrase || 'Нет выбранной строки')}</h3>
                </div>
                <button class="keyword-panel-close" type="button" aria-label="Закрыть">×</button>
            </div>
            <div class="keyword-panel-empty">
                <p>По этому ключевому слову нет активной рекомендации.</p>
                <p>Показатели не выделяются как проблемные или перспективные по текущим правилам.</p>
            </div>
        `;
        setupPanelClose(panel);
        return;
    }

    const decisionStatus = normalizeDecision(insight.decisionStatus);
    panel.hidden = false;
    panel.innerHTML = `
        <div class="keyword-insight-panel-head">
            <div>
                <span class="keyword-panel-eyebrow">Insight</span>
                <h3>${escapeHtml(insight.phrase || '')}</h3>
                <div class="keyword-panel-badges">
                    ${renderStatusBadge(normalizeStatus(insight.status), decisionStatus)}
                    <span class="keyword-priority-badge">${escapeHtml(normalizePriority(insight.priorityLevel))} · ${Math.round(toNumber(insight.priorityScore))}</span>
                </div>
            </div>
            <button class="keyword-panel-close" type="button" aria-label="Закрыть">×</button>
        </div>
        <div class="keyword-panel-section">
            <h4>Почему</h4>
            <p>${escapeHtml(insight.shortExplanation || '')}</p>
        </div>
        <div class="keyword-panel-metrics">
            ${renderMetricCards(insight.metrics || {})}
        </div>
        <div class="keyword-panel-section">
            <h4>Действие</h4>
            <strong>${escapeHtml(insight.recommendedActionTitle || '')}</strong>
            <p>${escapeHtml(insight.recommendedActionDescription || '')}</p>
        </div>
        <div class="keyword-panel-section">
            <h4>Ожидаемый эффект</h4>
            <p>${escapeHtml(insight.expectedEffectText || '')}</p>
        </div>
        ${renderActionsList('Разрешено', insight.allowedActions || [])}
        ${renderActionsList('Нельзя', insight.forbiddenActions || [], true)}
        <div class="keyword-panel-actions" data-insight-id="${escapeHtml(insight.insightId)}">
            <button class="keyword-decision-btn accept ${decisionStatus === 'Accepted' ? 'active' : ''}" data-decision="accept">Принять</button>
            <button class="keyword-decision-btn postpone ${decisionStatus === 'Postponed' ? 'active' : ''}" data-decision="postpone">Отложить</button>
            <button class="keyword-decision-btn reject ${decisionStatus === 'Rejected' ? 'active' : ''}" data-decision="reject">Отклонить</button>
        </div>
        <div class="keyword-panel-comment">
            <label for="keyword-insight-comment">Комментарий</label>
            <textarea id="keyword-insight-comment" rows="3">${escapeHtml(insight.userComment || '')}</textarea>
            <button id="keyword-save-comment" type="button">Сохранить</button>
        </div>
        ${renderInsightHistory(insight.history || [])}
    `;

    setupPanelClose(panel);
    setupInsightDecisionHandlers(panel, insight.insightId);
}

function setupPanelClose(panel) {
    const close = panel.querySelector('.keyword-panel-close');
    if (!close) {
        return;
    }

    close.addEventListener('click', () => {
        panel.hidden = true;
    });
}

function setupInsightDecisionHandlers(panel, insightId) {
    for (const button of panel.querySelectorAll('.keyword-decision-btn')) {
        button.addEventListener('click', async () => {
            const decision = button.dataset.decision;
            if (!decision) {
                return;
            }

            try {
                button.disabled = true;
                await updateInsightDecision(insightId, decision);
                await reloadKeywordOverlayAndPanel(insightId);
            } catch (error) {
                console.error('Ошибка при обновлении решения insight:', error);
                alert('Не удалось обновить решение');
            } finally {
                button.disabled = false;
            }
        });
    }

    const saveComment = panel.querySelector('#keyword-save-comment');
    const comment = panel.querySelector('#keyword-insight-comment');
    if (saveComment && comment) {
        saveComment.addEventListener('click', async () => {
            try {
                saveComment.disabled = true;
                await updateInsightComment(insightId, comment.value);
                await reloadKeywordOverlayAndPanel(insightId);
            } catch (error) {
                console.error('Ошибка при сохранении комментария insight:', error);
                alert('Не удалось сохранить комментарий');
            } finally {
                saveComment.disabled = false;
            }
        });
    }
}

async function updateInsightDecision(insightId, decision) {
    const response = await api.fetchWithAuth(`/api/recommendations/insights/${encodeURIComponent(insightId)}/${decision}`, {
        method: 'POST'
    });

    if (!response || !response.ok) {
        throw new Error('Не удалось обновить решение');
    }
}

async function updateInsightComment(insightId, userComment) {
    const response = await api.fetchWithAuth(`/api/recommendations/insights/${encodeURIComponent(insightId)}/comment`, {
        method: 'PUT',
        body: JSON.stringify({ userComment })
    });

    if (!response || !response.ok) {
        throw new Error('Не удалось сохранить комментарий');
    }
}

async function reloadKeywordOverlayAndPanel(insightId) {
    await loadKeywordStats(keywordOverlayCampaignId, keywordOverlayFilters);
    const insight = findInsightDetail(insightId);
    const keyword = insight ? findKeywordRow(insight.keywordId) : null;
    renderKeywordInsightPanel(insight, keyword);
}

function findInsightDetail(insightId) {
    if (!insightId || !keywordOverlay?.insights) {
        return null;
    }

    return keywordOverlay.insights.find(item => item.insightId === insightId) || null;
}

function findKeywordRow(keywordId) {
    if (!keywordId) {
        return null;
    }

    return keywordRows.find(row => String(row.keywordId).toLowerCase() === String(keywordId).toLowerCase()) || null;
}

function renderStatusBadge(status, decisionStatus = 'None') {
    const normalizedStatus = normalizeStatus(status);
    const normalizedDecision = normalizeDecision(decisionStatus);
    const decisionLabel = normalizedDecision !== 'None'
        ? `<small>${escapeHtml(DECISION_LABELS[normalizedDecision] || normalizedDecision)}</small>`
        : '';

    return `
        <span class="keyword-status-badge badge-${normalizedStatus}">
            <span class="keyword-status-dot"></span>
            <span>${escapeHtml(STATUS_LABELS[normalizedStatus] || normalizedStatus)}</span>
            ${decisionLabel}
        </span>
    `;
}

function renderMetricCards(metrics) {
    const metricOrder = ['views', 'impressions', 'clicks', 'spend', 'orders', 'revenue', 'ctr', 'cr', 'cpc', 'drr'];
    const entries = [];
    const usedKeys = new Set();

    for (const key of metricOrder) {
        if (key === 'views' && metrics.views === undefined && metrics.impressions !== undefined) {
            continue;
        }

        if (metrics[key] !== undefined) {
            entries.push([key, metrics[key]]);
            usedKeys.add(key);
        }
    }

    for (const [key, value] of Object.entries(metrics)) {
        if (!usedKeys.has(key) && entries.length < 10) {
            entries.push([key, value]);
        }
    }

    if (!entries.length) {
        return '<div class="keyword-metric-card"><span>Метрики</span><strong>-</strong></div>';
    }

    return entries.map(([key, value]) => `
        <div class="keyword-metric-card">
            <span>${escapeHtml(formatMetricName(key))}</span>
            <strong>${escapeHtml(formatMetricValue(key, value))}</strong>
        </div>
    `).join('');
}

function renderActionsList(title, actions, isForbidden = false) {
    if (!actions.length) {
        return '';
    }

    return `
        <div class="keyword-panel-section ${isForbidden ? 'forbidden' : ''}">
            <h4>${escapeHtml(title)}</h4>
            <div class="keyword-action-list">
                ${actions.map(action => `<span>${escapeHtml(formatAction(action))}</span>`).join('')}
            </div>
        </div>
    `;
}

function renderInsightHistory(history) {
    if (!history.length) {
        return '';
    }

    return `
        <div class="keyword-panel-section">
            <h4>История</h4>
            <div class="keyword-history-list">
                ${history.map(item => `
                    <div class="keyword-history-item">
                        <strong>${escapeHtml(formatHistoryType(item.type))}</strong>
                        <span>${escapeHtml(formatDateTime(item.createdAt))}</span>
                        ${item.comment ? `<p>${escapeHtml(item.comment)}</p>` : ''}
                    </div>
                `).join('')}
            </div>
        </div>
    `;
}

function getStatusCount(counts, status) {
    const key = status.charAt(0).toLowerCase() + status.slice(1);
    return counts[key] ?? counts[status] ?? 0;
}

function getDrrClass(value) {
    if (value === null || value === undefined) {
        return '';
    }

    return Number(value) > 20 ? 'drr-red' : 'drr-green';
}

function calculateCtr(clicks, impressions, fallbackCtr = null) {
    const normalizedClicks = toNumber(clicks);
    const normalizedImpressions = toNumber(impressions);

    if (normalizedImpressions > 0) {
        return normalizedClicks / normalizedImpressions * 100;
    }

    if (fallbackCtr === null || fallbackCtr === undefined) {
        return null;
    }

    return normalizePercentValue(fallbackCtr);
}

function calculateDrr(spend, revenue, fallbackDrr = null) {
    const normalizedSpend = toNumber(spend);
    const normalizedRevenue = toNumber(revenue);

    if (normalizedRevenue <= 0) {
        return null;
    }

    if (normalizedSpend >= 0) {
        return normalizedSpend / normalizedRevenue * 100;
    }

    return fallbackDrr === null || fallbackDrr === undefined
        ? null
        : normalizePercentValue(fallbackDrr);
}

function normalizePercentValue(value) {
    const number = toNumber(value);
    return number > 0 && number <= 1 ? number * 100 : number;
}

function formatCurrency(value) {
    return `${Math.round(toNumber(value)).toLocaleString('ru-RU')} ₽`;
}

function formatMoney(value, digits = 0) {
    const number = toNumber(value);
    return `${number.toLocaleString('ru-RU', {
        minimumFractionDigits: digits,
        maximumFractionDigits: digits
    })} ₽`;
}

function formatInteger(value) {
    return Math.round(toNumber(value)).toLocaleString('ru-RU');
}

function formatPercent(value, digits) {
    if (value === null || value === undefined || Number.isNaN(Number(value))) {
        return '—';
    }

    return `${toNumber(value).toFixed(digits)}%`;
}

function formatMetricName(key) {
    const names = {
        cr: 'CR',
        cpc: 'CPC',
        ctr: 'CTR',
        spend: 'Расход',
        revenue: 'Выручка',
        orders: 'Заказы',
        views: 'Показы',
        impressions: 'Показы',
        drr: 'ДРР',
        clicks: 'Клики',
        confidenceScore: 'Уверенность',
        wastedSpend: 'Потери',
        cpo: 'CPO',
        averageOrderValue: 'Средний чек',
        avgDailyOrders: 'Заказов в день'
    };

    return names[key] || key;
}

function formatMetricValue(key, value) {
    if (value === null || value === undefined || Number.isNaN(Number(value))) {
        return '—';
    }

    const number = toNumber(value);
    if (['spend', 'revenue', 'wastedSpend'].includes(key)) {
        return formatMoney(number, 0);
    }

    if (['cpc', 'cpo', 'averageOrderValue'].includes(key)) {
        return formatMoney(number, 2);
    }

    if (key === 'ctr') {
        return formatPercent(number, 2);
    }

    if (key === 'cr') {
        return number === 0 ? '0%' : formatPercent(number, 1);
    }

    if (key === 'drr') {
        return formatPercent(number, 1);
    }

    if (key === 'confidenceScore') {
        return number.toFixed(2);
    }

    return Number.isInteger(number) ? formatInteger(number) : number.toFixed(2);
}

function formatAction(action) {
    const actionNames = [
        'Watch',
        'CollectMoreData',
        'DecreaseBid',
        'DecreaseBidCarefully',
        'IncreaseBid',
        'IncreaseBidGradually',
        'IncreaseBidAggressively',
        'ConsiderMinusKeyword',
        'MinusKeyword',
        'ImmediateMinusKeyword',
        'MoveToWatchlist',
        'Optimize',
        'Scale',
        'AggressiveScale',
        'FindSimilarKeywords',
        'Maintain',
        'Disable',
        'ImmediateDisable',
        'AggressiveBidChange',
        'SeparateControl',
        'ScaleGoodKeywords',
        'IncreaseBidForScaleCandidates',
        'ExpandRelevantKeywords',
        'AcceptHigherDrrTemporarily',
        'AggressivelyReduceAllSpend',
        'DisableConvertingKeywords'
    ];
    const numeric = Number(action);
    const normalizedAction = Number.isInteger(numeric) && actionNames[numeric]
        ? actionNames[numeric]
        : String(action);
    const actions = {
        ConsiderMinusKeyword: 'Исключить',
        DecreaseBid: 'Снизить ставку',
        DecreaseBidCarefully: 'Снизить осторожно',
        IncreaseBid: 'Повысить ставку',
        IncreaseBidGradually: 'Повысить ставку',
        IncreaseBidAggressively: 'Резко повысить ставку',
        Scale: 'Масштабировать',
        AggressiveScale: 'Агрессивно масштабировать',
        CollectMoreData: 'Собрать данные',
        Watch: 'Наблюдать',
        Optimize: 'Оптимизировать',
        Maintain: 'Сохранить',
        MoveToWatchlist: 'В наблюдение',
        FindSimilarKeywords: 'Найти похожие',
        MinusKeyword: 'Минус-слово',
        ImmediateMinusKeyword: 'Сразу исключить',
        Disable: 'Отключить',
        ImmediateDisable: 'Сразу отключить',
        AggressiveBidChange: 'Резко менять ставку',
        SeparateControl: 'Отдельный контроль',
        ScaleGoodKeywords: 'Масштабировать хорошие ключи',
        IncreaseBidForScaleCandidates: 'Повышать ставки кандидатам',
        ExpandRelevantKeywords: 'Расширять релевантные ключи',
        AcceptHigherDrrTemporarily: 'Временно принять высокий ДРР',
        AggressivelyReduceAllSpend: 'Резко снижать весь расход',
        DisableConvertingKeywords: 'Отключать конвертирующие ключи'
    };

    return actions[normalizedAction] || normalizedAction;
}

function formatHistoryType(type) {
    const types = {
        Accepted: 'Принято',
        Postponed: 'Отложено',
        Rejected: 'Отклонено',
        CommentUpdated: 'Комментарий'
    };

    return types[type] || String(type);
}

function formatDateTime(value) {
    if (!value) {
        return '';
    }

    return new Date(value).toLocaleString('ru-RU');
}

function normalizeStatus(value) {
    const numeric = Number(value);
    if (Number.isInteger(numeric) && STATUS_ORDER[numeric]) {
        return STATUS_ORDER[numeric];
    }

    const stringValue = String(value || 'Neutral');
    return STATUS_LABELS[stringValue] ? stringValue : 'Neutral';
}

function normalizeDecision(value) {
    const values = ['None', 'Accepted', 'Postponed', 'Rejected'];
    const numeric = Number(value);
    if (Number.isInteger(numeric) && values[numeric]) {
        return values[numeric];
    }

    const stringValue = String(value || 'None');
    return DECISION_LABELS[stringValue] ? stringValue : 'None';
}

function normalizePriority(value) {
    const values = ['Low', 'Medium', 'High', 'Critical'];
    const numeric = Number(value);
    return Number.isInteger(numeric) && values[numeric] ? values[numeric] : String(value || 'Low');
}

function normalizeConfidence(value) {
    const values = ['Low', 'Medium', 'High'];
    const numeric = Number(value);
    return Number.isInteger(numeric) && values[numeric] ? values[numeric] : String(value || 'Low');
}

function toNumber(value) {
    const number = Number(value);
    return Number.isFinite(number) ? number : 0;
}

function escapeHtml(value) {
    return String(value ?? '')
        .replace(/&/g, '&amp;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;')
        .replace(/'/g, '&#039;');
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

    window.reloadKeywordRecommendationOverlay = applyFilters;

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

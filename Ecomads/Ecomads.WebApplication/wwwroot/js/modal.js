// /js/modal.js

export const fetchWithAuth = async (url, options = {}) => {
    const token = localStorage.getItem('ecomads_token');

    const headers = { ...options.headers };
    headers['Authorization'] = token ? `Bearer ${token}` : '';

    if (!(options.body instanceof FormData)) {
        headers['Content-Type'] = 'application/json';
    }

    const authOptions = {
        ...options,
        headers
    };

    const response = await fetch(url, authOptions);

    if (response.status === 401) {
        localStorage.removeItem('ecomads_token');
        window.location.href = '/index.html';
        return null;
    }

    return response;
};

export function createUploadModal(isKeywords = false, campaignId = null) {
    const modal = document.createElement('div');
    modal.className = 'modal-overlay';
    modal.id = 'upload-modal';

    const dashboardExtras = isKeywords
        ? ''
        : `
            <div class="form-group">
                <label>Режим загрузки</label>
                <select id="upload-mode">
                    <option value="general">Только общий отчет</option>
                    <option value="with-keywords">Общий отчет + отчет по ключевым словам</option>
                </select>
            </div>
            <p class="text-label" style="margin: 0 0 12px 0;">
                Эта модалка загружает общий отчет Wildberries за период.
                Если выбрать второй режим, дополнительно загружается отчет по ключевым словам для номенклатуры из общего отчета.
            </p>
            <div class="form-group" id="keywords-file-group" style="opacity: 0.6;">
                <label>Файл по ключевым словам (.xlsx)</label>
                <input type="file" id="keywords-file-input" accept=".xlsx" disabled>
                <div id="keywords-file-input-name" class="text-label" style="margin-top: 6px;">Файл не выбран</div>
            </div>
        `;

    modal.innerHTML = `
        <div class="modal">
            <h2>${isKeywords ? 'Загрузка ключевых слов' : 'Загрузка статистики'}</h2>
            <p>Загрузите файл с ${isKeywords ? 'ключевыми словами' : 'общей статистикой'} из Wildberries.</p>

            <div class="form-group">
                <label>Период</label>
                <div style="display: flex; gap: 10px;">
                    <input type="date" id="start-date">
                    <input type="date" id="end-date">
                </div>
            </div>
            <div class="form-group">
                <label>Файл статистики (.xlsx)</label>
                <input type="file" id="file-input" accept=".xlsx">
                <div id="file-input-name" class="text-label" style="margin-top: 6px;">Файл не выбран</div>
            </div>
            ${dashboardExtras}
            <div class="modal-actions">
                <button class="btn-cancel" onclick="document.getElementById('upload-modal').style.display='none'">Отмена</button>
                <button class="btn-submit" onclick="handleFileUpload(${isKeywords}, '${campaignId || ''}')">Загрузить</button>
            </div>
        </div>
    `;

    document.body.appendChild(modal);

    if (!isKeywords) {
        setupDashboardUploadModeControls();
    }

    setupFileInputNamePreview('file-input', 'file-input-name');
    setupFileInputNamePreview('keywords-file-input', 'keywords-file-input-name');
}

window.handleFileUpload = async (isKeywords, campaignId) => {
    const fileInput = document.getElementById('file-input');
    const start = document.getElementById('start-date').value;
    const end = document.getElementById('end-date').value;
    const keywordsFileInput = document.getElementById('keywords-file-input');
    const uploadMode = document.getElementById('upload-mode');

    if (!fileInput.files[0] || !start || !end) {
        alert('Пожалуйста, заполните все обязательные поля');
        return;
    }

    const formData = new FormData();
    formData.append('file', fileInput.files[0]);
    formData.append('startDate', start);
    formData.append('endDate', end);

    let endpoint = '/api/statistics/upload';

    if (isKeywords) {
        if (campaignId) {
            formData.append('campaignId', campaignId);
        }
        endpoint = '/api/statistics/upload-keywords';
    } else {
        const mode = uploadMode?.value ?? 'general';
        const keywordsFile = keywordsFileInput?.files?.[0];

        if (mode === 'with-keywords') {
            if (!keywordsFile) {
                alert('Добавьте файл отчета по ключевым словам.');
                return;
            }
            formData.append('keywordsFile', keywordsFile);
            endpoint = '/api/statistics/upload-with-keywords';
        } else {
            endpoint = '/api/statistics/upload';
        }
    }

    try {
        const response = await fetchWithAuth(endpoint, {
            method: 'POST',
            body: formData
        });

        if (response?.ok) {
            alert('Данные успешно загружены!');
            document.getElementById('upload-modal').style.display = 'none';
            window.location.reload();
        } else {
            const error = response ? await response.text() : 'Unauthorized';
            alert('Ошибка загрузки: ' + error);
        }
    } catch (error) {
        console.error('Upload error:', error);
        alert('Произошла ошибка при отправке файла.');
    }
};

function setupDashboardUploadModeControls() {
    const modeSelect = document.getElementById('upload-mode');
    const keywordsFileGroup = document.getElementById('keywords-file-group');
    const keywordsFileInput = document.getElementById('keywords-file-input');

    if (!modeSelect || !keywordsFileGroup || !keywordsFileInput) {
        return;
    }

    const applyMode = () => {
        const isExtendedMode = modeSelect.value === 'with-keywords';

        keywordsFileInput.disabled = !isExtendedMode;
        keywordsFileGroup.style.opacity = isExtendedMode ? '1' : '0.6';

        if (!isExtendedMode) {
            keywordsFileInput.value = '';
            const keywordsFileNameLabel = document.getElementById('keywords-file-input-name');
            if (keywordsFileNameLabel) {
                keywordsFileNameLabel.textContent = 'Файл не выбран';
            }
        }
    };

    modeSelect.addEventListener('change', applyMode);
    applyMode();
}

function setupFileInputNamePreview(inputId, labelId) {
    const input = document.getElementById(inputId);
    const label = document.getElementById(labelId);

    if (!input || !label) {
        return;
    }

    const update = () => {
        const fileName = input.files && input.files.length > 0 ? input.files[0].name : '';
        label.textContent = fileName || 'Файл не выбран';
    };

    input.addEventListener('change', update);
    update();
}

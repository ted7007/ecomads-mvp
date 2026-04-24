// /js/modal.js

export const fetchWithAuth = async (url, options = {}) => {
    const token = localStorage.getItem('ecomads_token');
    
    // Создаем копию заголовков
    const headers = { ...options.headers };
    
    // Добавляем заголовок авторизации
    headers['Authorization'] = token ? `Bearer ${token}` : '';
    
    // Если тело запроса не FormData, устанавливаем Content-Type: application/json
    if (!(options.body instanceof FormData)) {
        headers['Content-Type'] = 'application/json';
    }
    // Для FormData не устанавливаем Content-Type, чтобы браузер сам добавил boundary

    const authOptions = {
        ...options,
        headers: headers
    };

    const response = await fetch(url, authOptions);

    // Если сервер вернул 401 Unauthorized, токен недействителен
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
            </div>
            <div class="modal-actions">
                <button class="btn-cancel" onclick="document.getElementById('upload-modal').style.display='none'">Отмена</button>
                <button class="btn-submit" onclick="handleFileUpload(${isKeywords}, '${campaignId || ''}')">Загрузить</button>
            </div>
        </div>
    `;
    document.body.appendChild(modal);
}

window.handleFileUpload = async (isKeywords, campaignId) => {
    const fileInput = document.getElementById('file-input');
    const start = document.getElementById('start-date').value;
    const end = document.getElementById('end-date').value;

    if (!fileInput.files[0] || !start || !end) {
        alert('Пожалуйста, заполните все поля');
        return;
    }

    const formData = new FormData();
    formData.append('file', fileInput.files[0]);
    formData.append('startDate', start);
    formData.append('endDate', end);
    
    if (isKeywords && campaignId) {
        formData.append('campaignId', campaignId);
    }

    try {
        const endpoint = isKeywords ? '/api/statistics/upload-keywords' : '/api/statistics/upload';
        const response = await fetchWithAuth(endpoint, {
            method: 'POST',
            body: formData
        });

        if (response.ok) {
            alert('Данные успешно загружены!');
            document.getElementById('upload-modal').style.display = 'none';
            window.location.reload(); 
        } else {
            const error = await response.text();
            alert('Ошибка загрузки: ' + error);
        }
    } catch (error) {
        console.error('Ошибка:', error);
        alert('Произошла ошибка при отправке файла.');
    }
};

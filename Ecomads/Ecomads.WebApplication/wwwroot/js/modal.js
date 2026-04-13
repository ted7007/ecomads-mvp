// /js/modal.js
export function createUploadModal() {
    const modal = document.createElement('div');
    modal.className = 'modal-overlay';
    modal.id = 'upload-modal';
    
    modal.innerHTML = `
        <div class="modal">
            <h2>Загрузка статистики</h2>
            <p>Загрузите файл с общей статистикой из Wildberries, чтобы обновить данные.</p>
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
                <button class="btn-submit" onclick="handleFileUpload()">Загрузить</button>
            </div>
        </div>
    `;
    document.body.appendChild(modal);
}

window.handleFileUpload = async () => {
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

    try {
        const response = await fetch('/api/statistics/upload', {
            method: 'POST',
            body: formData
        });

        if (response.ok) {
            alert('Статистика успешно загружена!');
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

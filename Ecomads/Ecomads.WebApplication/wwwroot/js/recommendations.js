import { fetchWithAuth } from './auth.js';

/**
 * Загружает рекомендации для кампании
 * @param {string} campaignId - ID кампании
 * @returns {Promise<Array>} - массив рекомендаций
 */
export async function loadRecommendations(campaignId) {
    try {
        const response = await fetchWithAuth(`/api/recommendations/campaign/${campaignId}`);
        if (!response.ok) {
            throw new Error(`Ошибка при загрузке рекомендаций: ${response.statusText}`);
        }
        return await response.json();
    } catch (error) {
        console.error('Ошибка при загрузке рекомендаций:', error);
        return [];
    }
}

/**
 * Генерирует новую рекомендацию для кампании
 * @param {string} campaignId - ID кампании
 * @param {string} goal - Цель рекомендации (опционально)
 * @returns {Promise<Object>} - новая рекомендация
 */
export async function generateRecommendation(campaignId, goal = 'рост прибыли') {
    try {
        const response = await fetchWithAuth('/api/recommendations/generate', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json'
            },
            body: JSON.stringify({
                campaignId,
                goal
            })
        });
        
        if (!response.ok) {
            throw new Error(`Ошибка при генерации рекомендации: ${response.statusText}`);
        }
        
        return await response.json();
    } catch (error) {
        console.error('Ошибка при генерации рекомендации:', error);
        throw error;
    }
}

/**
 * Обновляет статус рекомендации
 * @param {string} recommendationId - ID рекомендации
 * @param {string} status - новый статус ('принята', 'отложена', 'отклонена')
 * @param {string} comment - комментарий пользователя (опционально)
 * @returns {Promise<Object>} - обновленная рекомендация
 */
export async function updateRecommendationStatus(recommendationId, status, comment = '') {
    try {
        const response = await fetchWithAuth(`/api/recommendations/${recommendationId}/status`, {
            method: 'PUT',
            headers: {
                'Content-Type': 'application/json'
            },
            body: JSON.stringify({
                status,
                userComment: comment
            })
        });
        
        if (!response.ok) {
            throw new Error(`Ошибка при обновлении статуса рекомендации: ${response.statusText}`);
        }
        
        return await response.json();
    } catch (error) {
        console.error('Ошибка при обновлении статуса рекомендации:', error);
        throw error;
    }
}

/**
 * Отображает рекомендации на странице
 * @param {Array} recommendations - массив рекомендаций
 * @param {HTMLElement} container - контейнер для отображения рекомендаций
 */
export function renderRecommendations(recommendations, container) {
    if (!recommendations || recommendations.length === 0) {
        container.innerHTML = '<p>Нет доступных рекомендаций</p>';
        return;
    }
    
    // Очищаем контейнер
    container.innerHTML = '';
    
    // Отображаем каждую рекомендацию
    recommendations.forEach((rec, index) => {
        const createdDate = new Date(rec.createdAt).toLocaleString('ru-RU');
        const priorityClass = getPriorityClass(rec);
        
        const recCard = document.createElement('div');
        recCard.className = `rec-card ${priorityClass}`;
        recCard.innerHTML = `
            <div class="rec-header">
                <span class="rec-title">
                    <i data-feather="${getFeatherIcon(rec)}" style="color: ${getColorByPriority(priorityClass)}"></i>
                    Рекомендация · ${rec.goal}
                </span>
                <span class="rec-rule-badge">${createdDate}</span>
            </div>
            <div class="rec-detail">
                <div class="rec-item"><strong>Проблема</strong><p>${rec.problem || 'Не указана'}</p></div>
                <div class="rec-item"><strong>Рекомендация</strong><p>${rec.recommendationText || 'Не указана'}</p></div>
                <div class="rec-item"><strong>Ожидаемый эффект</strong><p>${rec.expectedEffect || 'Не указан'}</p></div>
            </div>
            <div class="rec-actions">
                <span class="action-btn accept" data-id="${rec.id}" data-status="принята">
                    <i data-feather="check-circle"></i> Принять
                </span>
                <span class="action-btn later" data-id="${rec.id}" data-status="отложена">
                    <i data-feather="clock"></i> Отложить
                </span>
                <span class="action-btn dismiss" data-id="${rec.id}" data-status="отклонена">
                    <i data-feather="x-circle"></i> Отклонить
                </span>
            </div>
            <div class="rec-comment">
                <input type="text" placeholder="Комментарий (необязательно)" data-id="${rec.id}">
            </div>
        `;
        
        container.appendChild(recCard);
    });
    
    // Инициализируем иконки Feather
    if (window.feather) {
        feather.replace({ strokeWidth: 1.5, width: 18, height: 18 });
    }
    
    // Добавляем обработчики событий для кнопок статусов
    setupStatusButtons(container);
}

/**
 * Получает класс приоритета для рекомендации
 * @param {Object} recommendation - рекомендация
 * @returns {string} - класс приоритета
 */
function getPriorityClass(recommendation) {
    // По умолчанию средний приоритет
    let priorityClass = 'rule-2';
    
    if (recommendation.problem && recommendation.problem.toLowerCase().includes('неэффектив')) {
        priorityClass = 'rule-1'; // Высокий приоритет
    } else if (recommendation.recommendationText && recommendation.recommendationText.toLowerCase().includes('расшир')) {
        priorityClass = 'rule-4'; // Низкий приоритет / возможность
    }
    
    return priorityClass;
}

/**
 * Получает иконку Feather для рекомендации
 */
function getFeatherIcon(recommendation) {
    const priorityClass = getPriorityClass(recommendation);
    
    switch (priorityClass) {
        case 'rule-1': return 'alert-triangle';
        case 'rule-4': return 'thumbs-up';
        default: return 'zap';
    }
}

/**
 * Получает цвет по приоритету
 */
function getColorByPriority(priorityClass) {
    switch (priorityClass) {
        case 'rule-1': return 'var(--danger)';
        case 'rule-2':
        case 'rule-3': return 'var(--warning)';
        case 'rule-4': return 'var(--success)';
        default: return 'var(--text-secondary)';
    }
}

/**
 * Настраивает обработчики событий для кнопок статусов
 */
function setupStatusButtons(container) {
    // Кнопки статусов
    const statusButtons = container.querySelectorAll('.action-btn');
    statusButtons.forEach(button => {
        button.addEventListener('click', async function() {
            const id = this.getAttribute('data-id');
            const status = this.getAttribute('data-status');
            const commentInput = container.querySelector(`.rec-comment input[data-id="${id}"]`);
            const comment = commentInput ? commentInput.value : '';
            
            try {
                await updateRecommendationStatus(id, status, comment);
                alert(`Статус рекомендации обновлен: ${status}`);
                
                // Отмечаем выбранную кнопку
                const parentCard = this.closest('.rec-card');
                if (parentCard) {
                    parentCard.querySelectorAll('.action-btn').forEach(btn => {
                        btn.classList.remove('selected');
                    });
                    this.classList.add('selected');
                }
            } catch (error) {
                console.error('Ошибка при обновлении статуса:', error);
                alert('Не удалось обновить статус рекомендации');
            }
        });
    });
}
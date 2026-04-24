/**
 * Авторизация и управление доступом
 */

// Проверка авторизации пользователя
export async function checkAuth() {
    const token = localStorage.getItem('ecomads_token');
    if (!token) {
        return false;
    }
    
    try {
        const response = await fetch('/api/auth/me', {
            headers: {
                'Authorization': `Bearer ${token}`
            }
        });
        
        return response.ok;
    } catch (error) {
        console.error('Ошибка при проверке авторизации:', error);
        return false;
    }
}

// Выполнение авторизованного запроса к API
export async function fetchWithAuth(url, options = {}) {
    const token = localStorage.getItem('ecomads_token');
    if (!token) {
        window.location.href = '/index.html';
        throw new Error('Отсутствует токен авторизации');
    }
    
    // Добавляем заголовок авторизации ко всем запросам
    const authOptions = {
        ...options,
        headers: {
            ...options.headers,
            'Authorization': `Bearer ${token}`
        }
    };
    
    try {
        const response = await fetch(url, authOptions);
        
        if (response.status === 401) {
            localStorage.removeItem('ecomads_token');
            localStorage.removeItem('ecomads_user');
            window.location.href = '/index.html';
            throw new Error('Сессия истекла. Пожалуйста, войдите снова.');
        }
        
        return response;
    } catch (error) {
        // Если ошибка связана с сетью, а не с ответом сервера
        if (!error.response) {
            console.error('Сетевая ошибка:', error);
        }
        throw error;
    }
}

// Авторизация пользователя
export async function login(email, password) {
    const response = await fetch('/api/auth/login', {
        method: 'POST',
        headers: {
            'Content-Type': 'application/json',
        },
        body: JSON.stringify({ email, password }),
    });
    
    if (!response.ok) {
        const data = await response.json();
        throw new Error(data.message || 'Неверный email или пароль');
    }
    
    const data = await response.json();
    
    // Сохраняем токен и информацию о пользователе
    localStorage.setItem('ecomads_token', data.token);
    localStorage.setItem('ecomads_user', JSON.stringify({
        id: data.sellerId,
        name: data.name,
        email: data.email
    }));
    
    return data;
}

// Выход пользователя
export function logout() {
    localStorage.removeItem('ecomads_token');
    localStorage.removeItem('ecomads_user');
    window.location.href = '/index.html';
}

// Получение информации о текущем пользователе
export function getCurrentUser() {
    const userJson = localStorage.getItem('ecomads_user');
    if (!userJson) return null;
    
    try {
        return JSON.parse(userJson);
    } catch (e) {
        console.error('Ошибка при получении данных пользователя:', e);
        return null;
    }
}
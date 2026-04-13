// /js/navigation.js
export function initNavigation() {
    const navItems = document.querySelectorAll('.sidebar .nav-item');

    // Карта соответствия путей (добавьте свои URL)
    const routes = {
        'Дашборд': '/dashboard.html',
        'Кампании': '/campaigns.html',
        'Рекомендации': '/recommendations.html',
        'Настройки': '/settings.html'
    };

    navItems.forEach(item => {
        // Добавляем href для навигации
        const name = item.innerText.trim().split('\n')[0].trim();
        if (routes[name]) {
            item.setAttribute('href', routes[name]);
            item.addEventListener('click', (e) => {
                // Если нужно SPA-поведение, здесь будет fetch
                // Пока просто переходим по ссылке
                window.location.href = routes[name];
            });
        }

        // Подсветка текущей страницы
        if (window.location.pathname === routes[name]) {
            navItems.forEach(nav => nav.classList.remove('active'));
            item.classList.add('active');
        }
    });
}

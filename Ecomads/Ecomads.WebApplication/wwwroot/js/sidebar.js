// /js/sidebar.js
export function renderSidebar(activeItem) {
    const sidebar = `
        <div class="sidebar">
            <div class="sidebar-logo">
                <span>EcomAds</span>
                <span class="mvp-badge">MVP</span>
            </div>
            <nav>
                <a href="/dashboard.html" class="nav-item ${activeItem === 'dashboard' ? 'active' : ''}">
                    <i data-feather="pie-chart"></i> Дашборд
                </a>
                <a href="/campaign.html" class="nav-item ${activeItem === 'campaign' ? 'active' : ''}">
                    <i data-feather="layers"></i> Кампания
                </a>
                <a href="/recommendations.html" class="nav-item ${activeItem === 'recommendations' ? 'active' : ''}">
                    <i data-feather="zap"></i> Рекомендации <span class="nav-badge">4</span>
                </a>
                <a href="/settings.html" class="nav-item ${activeItem === 'settings' ? 'active' : ''}">
                    <i data-feather="settings"></i> Настройки
                </a>
            </nav>
            <div class="sidebar-footer">
                <a href="/" class="nav-item"><i data-feather="log-out"></i> Выход</a>
            </div>
        </div>
    `;
    const root = document.getElementById('sidebar-root');
    if (root) {
        root.innerHTML = sidebar;
        if (typeof feather !== 'undefined') {
            feather.replace({ strokeWidth: 1.5, width: 18, height: 18 });
        }
    }
}

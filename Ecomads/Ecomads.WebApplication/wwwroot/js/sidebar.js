// /js/sidebar.js
import { logout } from './auth.js';

export function renderSidebar(activeItem) {
    // Add style for disabled nav items
    const style = document.createElement('style');
    style.textContent = `
        .nav-item.disabled {
            pointer-events: none;
            opacity: 0.6;
            user-select: none;
        }
    `;
    document.head.appendChild(style);
    
    const sidebar = `
        <div class="sidebar" id="main-sidebar">
            <div class="sidebar-logo">
                <span>EcomAds</span>
                <span class="mvp-badge">MVP</span>
                <button class="sidebar-close-btn" id="sidebar-close" aria-label="Закрыть меню">
                    <i data-feather="x"></i>
                </button>
            </div>
            <nav>
                <a href="/dashboard.html" class="nav-item ${activeItem === 'dashboard' ? 'active' : ''}">
                    <i data-feather="pie-chart"></i> Дашборд
                </a>
                <span class="nav-item disabled ${activeItem === 'campaign' ? 'active' : ''}" style="cursor: default; opacity: 0.7;">
                    <i data-feather="layers"></i> Кампания
                </span>
                <a href="/report.html" class="nav-item ${activeItem === 'report' ? 'active' : ''}">
                    <i data-feather="bar-chart-2"></i> Отчёт эффект.
                </a>
                <a href="/settings.html" class="nav-item ${activeItem === 'settings' ? 'active' : ''}">
                    <i data-feather="settings"></i> Настройки
                </a>
            </nav>
            <div class="sidebar-footer">
                <a href="#" class="nav-item" id="logout-button"><i data-feather="log-out"></i> Выход</a>
            </div>
        </div>
        <div class="mobile-header">
            <button class="menu-toggle" id="sidebar-toggle" aria-label="Открыть меню">
                <i data-feather="menu"></i>
            </button>
            <div class="mobile-logo">EcomAds</div>
        </div>
        <div class="sidebar-overlay" id="sidebar-overlay"></div>
    `;
    
    const root = document.getElementById('sidebar-root');
    if (root) {
        root.innerHTML = sidebar;
        if (typeof feather !== 'undefined') {
            feather.replace({ strokeWidth: 1.5, width: 18, height: 18 });
        }
        
        // Initialize mobile sidebar functionality
        const sidebarToggle = document.getElementById('sidebar-toggle');
        const sidebarClose = document.getElementById('sidebar-close');
        const sidebarOverlay = document.getElementById('sidebar-overlay');
        const mainSidebar = document.getElementById('main-sidebar');
        
        if (sidebarToggle && mainSidebar) {
            sidebarToggle.addEventListener('click', () => {
                mainSidebar.classList.add('sidebar-open');
                document.body.classList.add('sidebar-active');
            });
        }
        
        if (sidebarClose && mainSidebar) {
            sidebarClose.addEventListener('click', () => {
                mainSidebar.classList.remove('sidebar-open');
                document.body.classList.remove('sidebar-active');
            });
        }
        
        if (sidebarOverlay && mainSidebar) {
            sidebarOverlay.addEventListener('click', () => {
                mainSidebar.classList.remove('sidebar-open');
                document.body.classList.remove('sidebar-active');
            });
        }
        
        // Добавляем обработчик для кнопки выхода
        const logoutButton = document.getElementById('logout-button');
        if (logoutButton) {
            logoutButton.addEventListener('click', (e) => {
                e.preventDefault();
                logout();
            });
        }
    }
}

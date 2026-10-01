// PictureNames · выбор темы оформления.
// Тема — личная настройка, хранится в localStorage, на других игроков не влияет.
(function () {
    'use strict';

    const STORAGE_KEY = 'picturenames-theme';
    const DEFAULT_THEME = 'dark';

    const THEMES = [
        { id: 'dark',   name: 'Тёмная',            icon: '🌙' },
        { id: 'light',  name: 'Светлая',           icon: '☀'  },
        { id: 'neon',   name: 'Неон',              icon: '⚡' },
        { id: 'paper',  name: 'Бумага',            icon: '📜' },
        { id: 'grim',   name: 'Мрачная',           icon: '🗡' },
        { id: 'mystic', name: 'Мистическая',       icon: '🔮' },
        { id: 'mech',   name: 'Mechromancer Zero', icon: '⚙' },
    ];

    function getStoredTheme() {
        try {
            const t = localStorage.getItem(STORAGE_KEY);
            if (t && THEMES.some(x => x.id === t)) return t;
        } catch (e) { /* localStorage может быть выключен */ }
        return DEFAULT_THEME;
    }

    function applyTheme(id, persist) {
        if (!THEMES.some(x => x.id === id)) id = DEFAULT_THEME;
        document.documentElement.setAttribute('data-theme', id);
        if (persist !== false) {
            try { localStorage.setItem(STORAGE_KEY, id); } catch (e) {}
        }
        document.querySelectorAll('.theme-item').forEach(el => {
            el.classList.toggle('active', el.dataset.themeId === id);
        });
    }

    // Применяем сразу — до DOMContentLoaded, чтобы не было мигания
    const initial = getStoredTheme();
    document.documentElement.setAttribute('data-theme', initial);

    document.addEventListener('DOMContentLoaded', () => {
        const btn = document.getElementById('theme-toggle');
        const menu = document.getElementById('theme-menu');
        if (!btn || !menu) return;

        menu.innerHTML = '';
        for (const t of THEMES) {
            const item = document.createElement('button');
            item.type = 'button';
            item.className = 'theme-item';
            item.dataset.themeId = t.id;
            item.innerHTML =
                `<span class="theme-icon">${t.icon}</span>` +
                `<span class="theme-name">${t.name}</span>`;
            if (t.id === initial) item.classList.add('active');
            item.addEventListener('click', () => {
                applyTheme(t.id);
                closeMenu();
            });
            menu.appendChild(item);
        }

        const current = THEMES.find(x => x.id === (document.documentElement.getAttribute('data-theme') || DEFAULT_THEME));
        if (current) btn.textContent = current.icon;

        function openMenu() {
            menu.classList.add('open');
            btn.classList.add('active');
        }
        function closeMenu() {
            menu.classList.remove('open');
            btn.classList.remove('active');
        }

        btn.addEventListener('click', e => {
            e.stopPropagation();
            menu.classList.contains('open') ? closeMenu() : openMenu();
        });

        document.addEventListener('click', e => {
            if (!menu.contains(e.target) && e.target !== btn) closeMenu();
        });

        document.addEventListener('keydown', e => {
            if (e.key === 'Escape') closeMenu();
        });

        const observer = new MutationObserver(() => {
            const t = THEMES.find(x => x.id === document.documentElement.getAttribute('data-theme'));
            if (t) btn.textContent = t.icon;
        });
        observer.observe(document.documentElement, { attributes: true, attributeFilter: ['data-theme'] });
    });

    window.PictureNamesTheme = { applyTheme, THEMES };
})();
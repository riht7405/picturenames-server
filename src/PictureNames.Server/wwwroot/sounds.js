// PictureNames · звуковая система.
// Папки на сервере = категории. В каждой — файлы. Играем случайный.
// Хранит кэш Audio-объектов, mute-статус в localStorage.
(function () {
    'use strict';

    const MUTE_KEY = 'picturenames-sound-muted';
    const VOLUME_KEY = 'picturenames-sound-volume';
    const DEFAULT_VOLUME = 0.7;

    const Sounds = {
        manifest: null,
        cache: Object.create(null),
        muted: false,
        volume: DEFAULT_VOLUME,
        initialized: false,

        async init() {
            if (this.initialized) return;
            this.initialized = true;

            try {
                this.muted = localStorage.getItem(MUTE_KEY) === '1';
            } catch (e) { /* localStorage может быть выключен */ }
            try {
                const v = parseFloat(localStorage.getItem(VOLUME_KEY));
                if (!isNaN(v) && v >= 0 && v <= 1) this.volume = v;
            } catch (e) {}

            try {
                const resp = await fetch('/api/sounds', { cache: 'no-store' });
                if (resp.ok) {
                    this.manifest = await resp.json();
                } else {
                    this.manifest = {};
                }
            } catch (e) {
                console.warn('[Sounds] manifest fetch failed', e);
                this.manifest = {};
            }
        },

        play(category) {
            if (this.muted) return;
            if (!this.manifest) return;

            const urls = this.manifest[category];
            if (!urls || urls.length === 0) return;

            const url = urls[Math.floor(Math.random() * urls.length)];
            const audio = this.getOrCreate(url);

            try {
                audio.currentTime = 0;
            } catch (e) { /* иногда первый раз нельзя — ничего страшного */ }

            const p = audio.play();
            if (p && typeof p.catch === 'function') {
                p.catch(() => { /* браузер заблокировал до первого клика — игнорируем */ });
            }
        },

        getOrCreate(url) {
            let a = this.cache[url];
            if (!a) {
                a = new Audio(url);
                a.preload = 'auto';
                a.volume = this.volume;
                this.cache[url] = a;
            }
            return a;
        },

        // Предзагрузка популярных категорий — чтобы первый звук не тормозил
        async preload(categories) {
            if (!this.manifest) return;
            for (const cat of categories) {
                const urls = this.manifest[cat];
                if (!urls) continue;
                for (const url of urls) {
                    this.getOrCreate(url);
                    // Попробуем подгрузить не блокируя
                    const a = this.cache[url];
                    try { a.load(); } catch (e) {}
                }
            }
        },

        setMuted(muted) {
            this.muted = !!muted;
            try { localStorage.setItem(MUTE_KEY, this.muted ? '1' : '0'); } catch (e) {}
            this.updateToggleButtons();
        },

        toggleMuted() {
            this.setMuted(!this.muted);
        },

        setVolume(v) {
            this.volume = Math.max(0, Math.min(1, v));
            try { localStorage.setItem(VOLUME_KEY, String(this.volume)); } catch (e) {}
            for (const key in this.cache) {
                this.cache[key].volume = this.volume;
            }
        },

        updateToggleButtons() {
            document.querySelectorAll('.sound-toggle').forEach(btn => {
                btn.textContent = this.muted ? '🔇' : '🔊';
                btn.title = this.muted ? 'Звук выключен' : 'Звук включён';
                btn.classList.toggle('muted', this.muted);
            });
        },

        bindToggleButtons() {
            document.querySelectorAll('.sound-toggle').forEach(btn => {
                btn.addEventListener('click', e => {
                    e.stopPropagation();
                    this.toggleMuted();
                });
            });
            this.updateToggleButtons();
        },
    };

    // Глобально доступный объект
    window.Sounds = Sounds;

    // Применяем mute сразу (до DOMContentLoaded), чтобы не мигало
    try {
        Sounds.muted = localStorage.getItem(MUTE_KEY) === '1';
    } catch (e) {}

    document.addEventListener('DOMContentLoaded', async () => {
        await Sounds.init();
        Sounds.bindToggleButtons();
    });

    // Первый клик по странице — разблокировка аудио в браузере
    // (некоторые браузеры не дают играть звук до действия пользователя)
    const unlockAudio = () => {
        document.removeEventListener('pointerdown', unlockAudio);
        document.removeEventListener('keydown', unlockAudio);
        // Прогреваем пустой короткий звук через Web Audio
        try {
            const ctx = new (window.AudioContext || window.webkitAudioContext)();
            if (ctx.state === 'suspended') ctx.resume();
        } catch (e) {}
    };
    document.addEventListener('pointerdown', unlockAudio, { once: true });
    document.addEventListener('keydown', unlockAudio, { once: true });
})();
// wwwroot/js/theme.js
// Light/dark theme management for Bootstrap. Default: dark.
// No 'auto' mode — themes are explicit.

const THEME_KEY = 'skipwatch-theme';
const DEFAULT_THEME = 'dark';

export function getTheme() {
    try {
        const t = localStorage.getItem(THEME_KEY);
        return t === 'light' ? 'light' : 'dark';
    } catch {
        return DEFAULT_THEME;
    }
}

export function setTheme(theme) {
    try {
        if (theme !== 'light' && theme !== 'dark') {
            theme = DEFAULT_THEME;
        }

        localStorage.setItem(THEME_KEY, theme);
        document.cookie = THEME_KEY + '=' + theme + ';path=/;max-age=31536000;SameSite=Lax';

        applyTheme(theme);

        window.dispatchEvent(new CustomEvent('themeChanged', { detail: theme }));
    } catch (error) {
        console.warn('Failed to set theme:', error);
    }
}

function applyTheme(theme) {
    const html = document.documentElement;
    html.setAttribute('data-bs-theme', theme);
    html.classList.remove('theme-light', 'theme-dark');
    html.classList.add('theme-' + theme);
}

export function getThemeIcon(theme) {
    return theme === 'light' ? 'bi-sun-fill' : 'bi-moon-fill';
}

// Apply theme immediately on module load to prevent FOUC.
(function () {
    if (typeof window !== 'undefined' && typeof document !== 'undefined') {
        applyTheme(getTheme());
    }
})();

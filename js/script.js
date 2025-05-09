document.addEventListener('DOMContentLoaded', () => {
    const lightThemeButton = document.getElementById('theme-light');
    const darkThemeButton = document.getElementById('theme-dark');
    const systemThemeButton = document.getElementById('theme-system');
    const body = document.body;
    const buttons = [lightThemeButton, darkThemeButton, systemThemeButton].filter(btn => btn !== null); // Filter out nulls if buttons don't exist on a page

    function applyTheme(theme) {
        body.classList.remove('light-theme', 'dark-theme');
        buttons.forEach(btn => btn.classList.remove('active'));

        if (theme === 'light') {
            body.classList.add('light-theme');
            if(lightThemeButton) lightThemeButton.classList.add('active');
            localStorage.setItem('llm-extension-for-cmd-pal-theme', 'light');
        } else if (theme === 'dark') {
            body.classList.add('dark-theme');
            if(darkThemeButton) darkThemeButton.classList.add('active');
            localStorage.setItem('llm-extension-for-cmd-pal-theme', 'dark');
        } else { // System theme
            if(systemThemeButton) systemThemeButton.classList.add('active');
            localStorage.setItem('llm-extension-for-cmd-pal-theme', 'system');
            // CSS media queries will handle appearance for system theme
        }
    }

    if (lightThemeButton) {
        lightThemeButton.addEventListener('click', () => applyTheme('light'));
    }
    if (darkThemeButton) {
        darkThemeButton.addEventListener('click', () => applyTheme('dark'));
    }
    if (systemThemeButton) {
        systemThemeButton.addEventListener('click', () => applyTheme('system'));
    }

    const savedTheme = localStorage.getItem('llm-extension-for-cmd-pal-theme') || 'system';
    applyTheme(savedTheme);

    window.matchMedia('(prefers-color-scheme: dark)').addEventListener('change', event => {
        if (localStorage.getItem('llm-extension-for-cmd-pal-theme') === 'system') {
            applyTheme('system');
        }
    });

    // Listen for theme changes from other tabs/windows
    window.addEventListener('storage', (event) => {
        if (event.key === 'llm-extension-for-cmd-pal-theme' && event.newValue) {
            // Ensure the new value is one of the expected theme strings
            if (['light', 'dark', 'system'].includes(event.newValue)) {
                applyTheme(event.newValue);
            }
        }
    });

    // Update current year in footer
    const currentYearSpan = document.getElementById('currentYear');
    if (currentYearSpan) {
        currentYearSpan.textContent = new Date().getFullYear();
    }
});

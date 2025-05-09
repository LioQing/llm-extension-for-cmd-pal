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
            localStorage.setItem('theme', 'light');
        } else if (theme === 'dark') {
            body.classList.add('dark-theme');
            if(darkThemeButton) darkThemeButton.classList.add('active');
            localStorage.setItem('theme', 'dark');
        } else { // System theme
            if(systemThemeButton) systemThemeButton.classList.add('active');
            localStorage.setItem('theme', 'system');
            // CSS media queries will handle appearance for system theme
            updateSystemButtonActiveState();
        }
    }

    function updateSystemButtonActiveState() {
        if (localStorage.getItem('theme') === 'system') {
            buttons.forEach(btn => btn.classList.remove('active'));
            if(systemThemeButton) systemThemeButton.classList.add('active');
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

    const savedTheme = localStorage.getItem('theme') || 'system';
    applyTheme(savedTheme);

    window.matchMedia('(prefers-color-scheme: dark)').addEventListener('change', event => {
        if (localStorage.getItem('theme') === 'system') {
            applyTheme('system');
        }
    });

    // Initial check for system theme button if system is default
    if (savedTheme === 'system') {
        updateSystemButtonActiveState();
    }

    // Update current year in footer
    const currentYearSpan = document.getElementById('currentYear');
    if (currentYearSpan) {
        currentYearSpan.textContent = new Date().getFullYear();
    }
});

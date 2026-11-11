function boot() {
    const route = location.hash.replace(/^#\//, '');
    bindEvents();
    if (config.initialRoute === 'public/home' && route.startsWith('public/')) {
        go(route);
        return;
    }
    render();
    if (ui.route.startsWith('public/') || ui.route === 'patient/booking') loadCatalog();
}

window.addEventListener('DOMContentLoaded', boot);

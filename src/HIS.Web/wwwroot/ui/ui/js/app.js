function boot() {
    const route = location.hash.replace(/^#\//, '');
    if (config.initialRoute === 'public/home' && route.startsWith('public/'))
        ui.route = route;
    bindEvents();
    render();
    if (ui.route.startsWith('public/')) loadCatalog();
}

window.addEventListener('DOMContentLoaded', boot);

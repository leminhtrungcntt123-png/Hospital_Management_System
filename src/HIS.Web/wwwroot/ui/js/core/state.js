const config = JSON.parse(document.getElementById('his-ui-config').textContent);
window.hisUiConfig = config;

const ui = {
    route: config.initialRoute || 'public/home',
    address: config.initialRoute || 'public/home',
    query: '',
    department: '',
    doctorPage: 1,
    mobileMenu: false,
};

let catalog = { status: 'idle', doctors: [], departments: [] };
let returnFocus;

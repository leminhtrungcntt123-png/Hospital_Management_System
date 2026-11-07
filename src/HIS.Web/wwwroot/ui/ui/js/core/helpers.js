const $ = (selector) => document.querySelector(selector);
const $$ = (selector) => [...document.querySelectorAll(selector)];
const esc = (value) =>
    String(value ?? '').replace(
        /[&<>"']/g,
        (character) =>
            ({
                '&': '&amp;',
                '<': '&lt;',
                '>': '&gt;',
                '"': '&quot;',
                "'": '&#39;',
            })[character],
    );
const icon = (name) =>
    `<svg class="icon" viewBox="0 0 24 24" aria-hidden="true"><path d="${paths[name] || paths.file}" /></svg>`;
const initials = (name) =>
    String(name || '')
        .trim()
        .split(/\s+/)
        .slice(-2)
        .map((part) => part[0] || '')
        .join('')
        .toUpperCase();
const activeRole = () => roleInfo[config.role] || { name: 'Tài khoản' };
const matched = (text) =>
    String(text || '')
        .toLocaleLowerCase('vi')
        .includes(ui.query.toLocaleLowerCase('vi'));

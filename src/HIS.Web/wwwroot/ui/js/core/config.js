/* core / config */
const paths = {
    grid: 'M3 3h7v7H3z M14 3h7v7h-7z M3 14h7v7H3z M14 14h7v7h-7z',
    calendar:
        'M8 2v4 M16 2v4 M3 10h18 M5 4h14a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2 M7 14h3 M14 14h3 M7 18h3',
    users: 'M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2 M9 11a4 4 0 1 0 0-8 4 4 0 0 0 0 8 M17 4a4 4 0 0 1 0 8 M22 21v-2a4 4 0 0 0-3-3.87',
    clipboard:
        'M9 4H6a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V6a2 2 0 0 0-2-2h-3 M9 2h6v4H9z M8 12h8 M8 16h5',
    wallet: 'M20 8V5a2 2 0 0 0-2-2H5a3 3 0 0 0 0 6h15v11H5a3 3 0 0 1-3-3V6 M20 12h-5v5h5 M16 14.5h.1',
    clock: 'M12 8v4l3 2 M22 12a10 10 0 1 1-20 0 10 10 0 0 1 20 0',
    shield: 'M12 3 3 7v5c0 5 9 9 9 9s9-4 9-9V7z M8 12l3 3 5-6',
    search: 'm21 21-5-5 M18 10a8 8 0 1 1-16 0 8 8 0 0 1 16 0',
    bell: 'M18 8a6 6 0 0 0-12 0c0 7-3 7-3 9h18c0-2-3-2-3-9 M10 21h4',
    chevron: 'm9 5 7 7-7 7',
    down: 'm6 9 6 6 6-6',
    plus: 'M12 5v14 M5 12h14',
    arrow: 'M5 12h14 m-5-5 5 5-5 5',
    external:
        'M14 3h7v7 M21 3l-9 9 M10 3H5a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-5',
    building:
        'M4 22V4h16v18 M2 22h20 M8 8h1 M15 8h1 M8 12h1 M15 12h1 M10 22v-6h4v6',
    check: 'm5 12 4 4L19 6',
    close: 'm6 6 12 12 M18 6 6 18',
    download: 'M12 3v12 m-5-5 5 5 5-5 M5 15v6h14v-6',
    eye: 'M2 12s4-7 10-7 10 7 10 7-4 7-10 7S2 12 2 12 M15 12a3 3 0 1 1-6 0 3 3 0 0 1 6 0',
    more: 'M5 12h.01 M12 12h.01 M19 12h.01',
    userplus:
        'M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2 M13 7a4 4 0 1 1-8 0 4 4 0 0 1 8 0 M20 8v6 M17 11h6',
    stethoscope:
        'M5 3v5a5 5 0 0 0 10 0V3 M3 3h4 M13 3h4 M10 13v3a5 5 0 0 0 10 0v-2 M22 11a2 2 0 1 1-4 0 2 2 0 0 1 4 0',
    trend: 'm3 17 6-6 4 4 8-10 M15 5h6v6',
    file: 'M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z M14 2v6h6 M8 13h8 M8 17h5',
    heart: 'M20.8 4.6a5.5 5.5 0 0 0-7.8 0L12 5.7l-1.1-1.1a5.5 5.5 0 0 0-7.8 7.8L12 21l8.8-8.6a5.5 5.5 0 0 0 0-7.8',
    pulse: 'M2 12h5l3-8 4 16 3-8h5',
    phone: 'M7 3H3v4c0 8 6 14 14 14h4v-4l-5-2-2 2c-4-1-6-3-7-7l2-2z',
    menu: 'M3 6h18 M3 12h18 M3 18h18',
    logout: 'M9 3H4v18h5 M12 12h10 m-4-4 4 4-4 4',
    printer: 'M6 9V3h12v6 M6 18H3V9h18v9h-3 M6 14h12v8H6z M17 12h.1',
    trash: 'M3 6h18 M8 6V3h8v3 M5 6l1 15h12l1-15 M10 10v7 M14 10v7',
    edit: 'm16 3 5 5-12 12-6 1 1-6z M14 5l5 5',
    mail: 'M3 4h18v16H3z M3 4l9 8 9-8',
    location:
        'M20 10c0 6-8 12-8 12S4 16 4 10a8 8 0 1 1 16 0 M15 10a3 3 0 1 1-6 0 3 3 0 0 1 6 0',
    info: 'M12 11v6 M12 7h.01 M22 12a10 10 0 1 1-20 0 10 10 0 0 1 20 0',
    refresh:
        'M21 3v6h-6 M3 21v-6h6 M3.5 8a9 9 0 0 1 15-4L21 9 M3 15l2.5 5a9 9 0 0 0 15-4',
    volume: 'M11 5 6 9H2v6h4l5 4z M15 8a6 6 0 0 1 0 8 M18 5a10 10 0 0 1 0 14',
};

const roleInfo = {
    PATIENT: { name: 'Bệnh nhân', icon: 'heart' },
    NURSE: { name: 'Y tá / Lễ tân', icon: 'users' },
    DOCTOR: { name: 'Bác sĩ', icon: 'stethoscope' },
    DEPARTMENT_HEAD: { name: 'Trưởng khoa', icon: 'building' },
    ADMIN: { name: 'Quản trị viên', icon: 'shield' },
};

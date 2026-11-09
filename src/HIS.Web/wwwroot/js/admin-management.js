(() => {
    'use strict';

    const workspace = document.querySelector('.misa-app, .admin-workspace');
    if (!workspace) return;

    const api = workspace.dataset.api.replace(/\/$/, '');
    const find = (selector) => document.querySelector(selector);
    const escape = (value) =>
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
    const money = (value) =>
        new Intl.NumberFormat('vi-VN', { maximumFractionDigits: 0 }).format(value);
    const date = (value) => String(value).split('-').reverse().join('/');
    const titles = {
        dashboard: [
            'Tổng quan & Doanh thu',
            'Theo dõi thời gian thực số lượt tiếp nhận bệnh nhân và dòng tiền vận hành.',
        ],
        requests: ['Nghỉ / Đổi ca trực', 'Duyệt đề nghị và sắp xếp lại lịch khám trong khoa.'],
        departments: ['Khoa phòng ban', 'Danh sách khoa và đơn vị chuyên môn trong bệnh viện.'],
        staff: ['Quản lý nhân sự', 'Danh sách bác sĩ, y tá và trưởng khoa theo phòng ban.'],
        doctors: ['Đội ngũ Bác sĩ', 'Theo dõi chuyên môn và đơn vị công tác của bác sĩ.'],
        accounts: ['Tài khoản & Phân quyền', 'Tạo tài khoản, kích hoạt và phân quyền truy cập hệ thống.'],
    };

    let directory = null;
    let busy = false;
    let reportVersion = 0;
    let currentReport = null;
    let activeChartFlow = 'all';

    function notify(message, error = false) {
        const target = find('#admin-message');
        if (!target) return;
        target.textContent = message;
        target.classList.toggle('error', error);
        target.hidden = false;
        if (!error) {
            setTimeout(() => {
                target.hidden = true;
            }, 4000);
        }
    }

    async function request(path, data) {
        const headers = { Accept: 'application/json' };
        if (data !== undefined) {
            headers['Content-Type'] = 'application/json';
            const tokenInput = find('#admin-token input');
            if (tokenInput) {
                headers.RequestVerificationToken = tokenInput.value;
            }
        }

        const response = await fetch(api + '/' + path, {
            method: data === undefined ? 'GET' : 'POST',
            credentials: 'same-origin',
            cache: 'no-store',
            headers,
            body: data === undefined ? undefined : JSON.stringify(data),
        });

        if (response.redirected || response.status === 401 || response.status === 403) {
            throw new Error(
                'Cần đăng nhập bằng tài khoản Admin đang hoạt động để sử dụng chức năng này.',
            );
        }

        if (!response.headers.get('content-type')?.includes('application/json')) {
            throw new Error(
                'Chưa nhận được dữ liệu từ máy chủ. Vui lòng kiểm tra kết nối và tải lại.',
            );
        }

        const result = await response.json();
        if (!response.ok) throw new Error(result.message || 'Không thực hiện được yêu cầu.');
        return result;
    }

    function table(id, headers, rows) {
        const target = find(id);
        if (!target) return;
        const heading = headers
            .map((h) => /* HTML */ `<th scope="col">${escape(h)}</th>`)
            .join('');
        const body = rows.length
            ? rows
                  .map(
                      (row) =>
                          /* HTML */ `<tr>
                              ${row.map((cell) => /* HTML */ `<td>${cell}</td>`).join('')}
                          </tr>`,
                  )
                  .join('')
            : /* HTML */ `<tr>
                  <td class="empty" colspan="${headers.length}">Chưa có dữ liệu phù hợp.</td>
              </tr>`;
        target.innerHTML = /* HTML */ `<table class="misa-table">
            <thead>
                <tr>
                    ${heading}
                </tr>
            </thead>
            <tbody>
                ${body}
            </tbody>
        </table>`;
    }

    function accountRows(rows, actions) {
        return rows.map((row) => [
            /* HTML */ `<strong>${escape(row.name)}</strong>`,
            /* HTML */ `<code class="username-tag">${escape(row.username)}</code>`,
            /* HTML */ `<span class="misa-badge-role role-${escape(row.roleName).toLowerCase()}">${escape(row.roleName)}</span>`,
            escape(row.department || '—'),
            /* HTML */ `<span class="misa-badge-status ${row.isActive ? 'active' : 'inactive'}"
                >${row.isActive ? '● Đang hoạt động' : '○ Ngừng hoạt động'}</span
            >`,
            ...(actions
                ? [
                      row.canChangeStatus
                          ? /* HTML */ `<button
                                type="button"
                                class="btn misa-btn-table-action ${row.isActive ? 'danger' : 'success'}"
                                data-account="${row.id}"
                                data-active="${!row.isActive}"
                            >
                                ${row.isActive ? 'Khóa tài khoản' : 'Kích hoạt'}
                            </button>`
                          : '<span class="self-tag">Đang đăng nhập</span>',
                  ]
                : []),
        ]);
    }

    // =========================================================================
    // GOOGLE CONTACTS / GMAIL CRM SPLIT-VIEW (MASTER - DETAIL)
    // =========================================================================
    const AVATAR_PALETTES = [
        { bg: '#fed7aa', text: '#c2410c' }, // peach/orange (như TT)
        { bg: '#bae6fd', text: '#0369a1' }, // sky blue (như MK)
        { bg: '#fbcfe8', text: '#be185d' }, // soft pink (như NH)
        { bg: '#e9d5ff', text: '#7e22ce' }, // soft purple (như QA)
        { bg: '#bbf7d0', text: '#15803d' }, // mint green (như HN)
        { bg: '#fde68a', text: '#b45309' }, // warm amber (như TL)
        { bg: '#99f6e4', text: '#0f766e' }, // soft teal (như YN)
        { bg: '#fecdd3', text: '#be123c' }, // soft rose (như PT)
        { bg: '#c7d2fe', text: '#4338ca' }, // indigo
        { bg: '#e2e8f0', text: '#334155' }, // slate
    ];

    function getInitials(name) {
        if (!name) return '??';
        const parts = name.trim().split(/\s+/);
        if (parts.length === 1) return parts[0].slice(0, 2).toUpperCase();
        if (parts.length >= 3) {
            return (parts[parts.length - 2][0] + parts[parts.length - 1][0]).toUpperCase();
        }
        return (parts[0][0] + parts[parts.length - 1][0]).toUpperCase();
    }

    function getAvatarColors(name, index) {
        if (typeof index === 'number') {
            return AVATAR_PALETTES[index % AVATAR_PALETTES.length];
        }
        let hash = 0;
        for (let i = 0; i < (name || '').length; i++) {
            hash = (hash << 5) - hash + name.charCodeAt(i);
            hash |= 0;
        }
        return AVATAR_PALETTES[Math.abs(hash) % AVATAR_PALETTES.length];
    }

    const currentProfileMap = {
        staff: null,
        accounts: null,
    };
    const currentSubtabMap = {
        staff: 'overview',
        accounts: 'overview',
    };

    function renderSplitView(sectionKey, rawItems) {
        const countBadge = find(`#${sectionKey}-count-badge`);
        const masterList = find(`#${sectionKey}-master-list`);
        const detailPane = find(`#${sectionKey}-detail-pane`);
        if (!masterList || !detailPane) return;

        const items = (rawItems || []).map((row) => ({
            id: row.id,
            name: row.name || row.username,
            username: row.username,
            roleName: row.roleName || (row.role === 'Admin' ? 'Quản trị viên' : row.role),
            role: row.role,
            department: row.department || (row.role === 'Admin' ? 'Ban Giám Đốc / CNTT' : 'Chưa phân khoa'),
            isActive: row.isActive,
            canChangeStatus: row.canChangeStatus,
            meta: `${row.department || 'Bệnh viện Medicare'} · Mã: MED-${row.id}`,
        }));

        if (countBadge) {
            countBadge.textContent = items.length >= 1000 ? (items.length / 1000).toFixed(3) : String(items.length);
        }

        if (items.length === 0) {
            masterList.innerHTML = `<div class="profile-subtab-empty-box" style="padding: 24px 12px; text-align: center; color: #64748b; font-size: 13px;">Chưa có dữ liệu ${sectionKey === 'staff' ? 'nhân sự' : 'tài khoản'}.</div>`;
            detailPane.innerHTML = `<div class="profile-subtab-empty-box" style="padding: 48px 16px; text-align: center; color: #64748b; font-size: 13px;">Chọn một bản ghi để xem chi tiết hoặc bấm thêm mới.</div>`;
            return;
        }

        if (!currentProfileMap[sectionKey] || !items.find(i => i.id === currentProfileMap[sectionKey]?.id)) {
            currentProfileMap[sectionKey] = items[0];
        } else {
            currentProfileMap[sectionKey] = items.find(i => i.id === currentProfileMap[sectionKey].id) || items[0];
        }

        const activeItem = currentProfileMap[sectionKey];

        masterList.innerHTML = items.map((item, index) => {
            const initials = getInitials(item.name);
            const palette = getAvatarColors(item.name, index);
            const isActive = activeItem && activeItem.id === item.id;
            return /* HTML */ `
                <div class="misa-split-item ${isActive ? 'active' : ''}" data-item-id="${item.id}" role="option" aria-selected="${isActive}">
                    <div class="misa-split-avatar" style="background-color: ${palette.bg}; color: ${palette.text};">
                        ${escape(initials)}
                    </div>
                    <div class="misa-split-item-info">
                        <span class="misa-split-item-name ${isActive ? 'active' : ''}">${escape(item.name)}</span>
                    </div>
                </div>
            `;
        }).join('');

        masterList.querySelectorAll('.misa-split-item').forEach((el) => {
            el.addEventListener('click', () => {
                const itemId = Number(el.dataset.itemId);
                const selected = items.find(i => i.id === itemId);
                if (selected) {
                    currentProfileMap[sectionKey] = selected;
                    masterList.querySelectorAll('.misa-split-item').forEach(i => i.classList.remove('active'));
                    el.classList.add('active');
                    renderDetailPane(sectionKey, selected);
                }
            });
        });

        renderDetailPane(sectionKey, activeItem);
    }

    function renderDetailPane(sectionKey, item) {
        const detailPane = find(`#${sectionKey}-detail-pane`);
        if (!detailPane || !item) return;

        const initials = getInitials(item.name);
        const palette = getAvatarColors(item.name);
        const subtab = currentSubtabMap[sectionKey] || 'overview';

        detailPane.innerHTML = /* HTML */ `
            <div class="profile-detail-header">
                <div class="profile-header-left">
                    <div class="profile-large-avatar" style="background-color: ${palette.bg}; color: ${palette.text};">
                        ${escape(initials)}
                    </div>
                    <div class="profile-header-title-wrap">
                        <h2>${escape(item.name)}</h2>
                        <div class="profile-header-subline">
                            <span><code class="username-tag">@${escape(item.username)}</code></span>
                            <span>·</span>
                            <span>${escape(item.department)}</span>
                        </div>
                    </div>
                </div>
                <div class="profile-header-badges">
                    <span class="badge-vip">${escape(item.roleName)}</span>
                    <span class="${item.isActive ? 'badge-active-soft' : 'badge-warning-soft'}">
                        <span>${item.isActive ? '● Đang hoạt động' : '○ Bị tạm khóa'}</span>
                    </span>
                </div>
            </div>

            <div class="profile-subtabs-bar" role="tablist">
                <button type="button" class="profile-pill-tab ${subtab === 'overview' ? 'active' : ''}" data-subtab="overview">Tổng quan hồ sơ</button>
                <button type="button" class="profile-pill-tab ${subtab === 'security' ? 'active' : ''}" data-subtab="security">Phân quyền &amp; Quyền hạn</button>
            </div>

            <div class="profile-tab-body">
                ${subtab === 'overview' ? renderOverviewSubtab(item) : renderSecuritySubtab(item)}
            </div>
        `;

        detailPane.querySelectorAll('.profile-pill-tab').forEach((tabBtn) => {
            tabBtn.addEventListener('click', () => {
                const tabKey = tabBtn.dataset.subtab;
                currentSubtabMap[sectionKey] = tabKey;
                renderDetailPane(sectionKey, item);
            });
        });
    }

    function renderOverviewSubtab(item) {
        return /* HTML */ `
            <div class="profile-content-grid">
                <!-- Cột trái: Thông tin nhân sự / tài khoản -->
                <div class="profile-timeline-block">
                    <h3 class="timeline-block-title">Hồ sơ chuyên môn &amp; Đơn vị công tác</h3>
                    <div class="profile-info-fields" style="display: flex; flex-direction: column; gap: 12px; padding: 12px 4px;">
                        <div class="profile-info-row">
                            <span style="font-size: 12px; color: #64748b; font-weight: 500;">Mã định danh hệ thống:</span>
                            <div style="font-size: 13.5px; font-weight: 600; color: #0f172a; margin-top: 2px;"><code>MED-${item.id}</code></div>
                        </div>
                        <div class="profile-info-row">
                            <span style="font-size: 12px; color: #64748b; font-weight: 500;">Họ và tên hiển thị:</span>
                            <div style="font-size: 14px; font-weight: 600; color: #0f172a; margin-top: 2px;">${escape(item.name)}</div>
                        </div>
                        <div class="profile-info-row">
                            <span style="font-size: 12px; color: #64748b; font-weight: 500;">Tên tài khoản:</span>
                            <div style="font-size: 13.5px; font-weight: 600; color: #0068ff; margin-top: 2px;"><code class="username-tag">@${escape(item.username)}</code></div>
                        </div>
                        <div class="profile-info-row">
                            <span style="font-size: 12px; color: #64748b; font-weight: 500;">Chức danh / Vai trò:</span>
                            <div style="font-size: 13.5px; font-weight: 600; color: #334155; margin-top: 2px;">
                                <span class="misa-badge-role role-${escape(item.roleName).toLowerCase()}">${escape(item.roleName)}</span>
                            </div>
                        </div>
                        <div class="profile-info-row">
                            <span style="font-size: 12px; color: #64748b; font-weight: 500;">Khoa / Phòng ban công tác:</span>
                            <div style="font-size: 13.5px; font-weight: 600; color: #334155; margin-top: 2px;">${escape(item.department)}</div>
                        </div>
                    </div>
                </div>

                <!-- Cột phải: Trạng thái & Thao tác quản trị -->
                <div class="profile-cards-block">
                    <h3 class="profile-cards-title">Trạng thái &amp; Thao tác quản trị</h3>

                    <div class="misa-detail-alert-card ${item.isActive ? 'card-blue' : 'card-pink'}">
                        <div class="alert-card-title">
                            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
                                <circle cx="12" cy="12" r="10"></circle>
                                <polyline points="12 6 12 12 14 14"></polyline>
                            </svg>
                            <span>${item.isActive ? 'Tài khoản đang hoạt động' : 'Tài khoản đang bị tạm khóa'}</span>
                        </div>
                        <p class="alert-card-desc">
                            ${item.isActive
                                ? 'Người dùng có thể đăng nhập vào cổng thông tin bệnh viện Medicare và thực hiện chức năng theo đúng vai trò.'
                                : 'Mọi phiên làm việc và quyền đăng nhập của người dùng tạm thời bị từ chối truy cập.'}
                        </p>
                    </div>

                    <div class="misa-detail-alert-card card-yellow">
                        <div class="alert-card-title">Chính sách an toàn hệ thống</div>
                        <ul class="alert-card-list">
                            <li>• Xác thực danh tính qua ASP.NET Core Identity &amp; Cookie bảo mật.</li>
                            <li>• Chặn thao tác trái quyền với Middleware Authorize và Antiforgery Token.</li>
                        </ul>
                    </div>

                    <div class="profile-detail-actions">
                        ${item.canChangeStatus ? /* HTML */ `
                            <button
                                type="button"
                                class="btn misa-btn-table-action ${item.isActive ? 'danger' : 'success'}"
                                data-account="${item.id}"
                                data-active="${!item.isActive}"
                            >
                                ${item.isActive ? 'Khóa tài khoản' : 'Kích hoạt tài khoản'}
                            </button>
                        ` : '<span class="self-tag">Tài khoản đang đăng nhập (Không thể tự khóa)</span>'}
                    </div>
                </div>
            </div>
        `;
    }

    function renderSecuritySubtab(item) {
        return /* HTML */ `
            <div class="profile-subtab-info-card">
                <h4>Chính sách phân quyền &amp; Giám sát truy cập</h4>
                <p>• Mã định danh hồ sơ: <code>MED-${item.id}</code></p>
                <p>• Tên đăng nhập: <strong>@${escape(item.username)}</strong></p>
                <p>• Vai trò phân quyền: <strong>${escape(item.roleName)}</strong> (${escape(item.role)})</p>
                <p>• Phòng ban trực thuộc: <strong>${escape(item.department)}</strong></p>
                <p>• Trạng thái hoạt động: <strong>${item.isActive ? 'Đang hoạt động' : 'Tạm khóa'}</strong></p>
                <p>• Quyền quản trị tài khoản: <strong>${item.canChangeStatus ? 'Quản trị viên được phép thao tác đổi trạng thái' : 'Tài khoản quản trị hiện tại của bạn'}</strong></p>
            </div>
        `;
    }

    async function loadDirectory() {
        directory = await request('Directory');
        const headings = [
            'Họ tên / Tên hiển thị',
            'Tên đăng nhập',
            'Vai trò',
            'Khoa phòng',
            'Trạng thái',
        ];
        table('#accounts-table', [...headings, 'Thao tác'], accountRows(directory.accounts, true));
        table('#staff-table', headings, accountRows(directory.staff, false));
        renderSplitView('accounts', directory.accounts);
        renderSplitView('staff', directory.staff);
        table(
            '#departments-table',
            ['Mã', 'Tên phòng ban', 'Mô tả phạm vi'],
            directory.departments.map((row) => [
                /* HTML */ `<code class="dept-code">${escape(row.id)}</code>`,
                /* HTML */ `<strong>${escape(row.name)}</strong>`,
                escape(row.description || '—'),
            ]),
        );
        table(
            '#doctors-table',
            ['Họ tên Bác sĩ', 'Chuyên khoa', 'Phòng ban', 'Chứng chỉ hành nghề'],
            directory.doctors.map((row) => [
                /* HTML */ `<strong>${escape(row.fullName)}</strong>`,
                /* HTML */ `<span class="specialty-badge">${escape(row.specialty || 'Đa khoa')}</span>`,
                escape(row.departmentName),
                /* HTML */ `<code>${escape(row.licenseNumber || '—')}</code>`,
            ]),
        );
    }

    function renderCharts(report) {
        if (!report) return;

        // 1. Dual-axis Combo Chart (Bar: Doanh thu / Thu + Line: Lượt tiếp nhận)
        const canvas = document.getElementById('revenueChartCanvas');
        if (window.Chart && canvas) {
            if (window._revenueChartInstance) {
                window._revenueChartInstance.destroy();
            }

            const labels = report.days.map((d) => date(d.date).slice(0, 5));
            let barData = report.days.map((d) => d.revenue);
            let barLabel = 'Doanh thu (VNĐ)';
            let barBg = 'rgba(0, 104, 255, 0.85)';
            let barBorder = '#0068ff';

            if (activeChartFlow === 'out') {
                barData = report.days.map((d) => d.refunds);
                barLabel = 'Hoàn tiền / Chi (VNĐ)';
                barBg = 'rgba(245, 158, 11, 0.85)';
                barBorder = '#f59e0b';
            } else if (activeChartFlow === 'balance') {
                barData = report.days.map((d) => d.revenue - d.refunds);
                barLabel = 'Dòng tiền thuần (VNĐ)';
                barBg = 'rgba(16, 185, 129, 0.85)';
                barBorder = '#10b981';
            }

            const visitsData = report.days.map((d) => d.visits);

            const ctx = canvas.getContext('2d');
            window._revenueChartInstance = new window.Chart(ctx, {
                data: {
                    labels: labels,
                    datasets: [
                        {
                            type: 'bar',
                            label: barLabel,
                            data: barData,
                            backgroundColor: barBg,
                            borderColor: barBorder,
                            borderWidth: 1.5,
                            borderRadius: 6,
                            borderSkipped: false,
                            yAxisID: 'yRevenue',
                            order: 2,
                            maxBarThickness: 36,
                        },
                        {
                            type: 'line',
                            label: 'Lượt tiếp nhận',
                            data: visitsData,
                            borderColor: '#f97316',
                            backgroundColor: '#f97316',
                            borderWidth: 2.5,
                            pointBackgroundColor: '#ffffff',
                            pointBorderColor: '#f97316',
                            pointBorderWidth: 2.5,
                            pointRadius: 4.5,
                            pointHoverRadius: 6.5,
                            tension: 0.35,
                            yAxisID: 'yVisits',
                            order: 1,
                        },
                    ],
                },
                options: {
                    responsive: true,
                    maintainAspectRatio: false,
                    interaction: { mode: 'index', intersect: false },
                    plugins: {
                        legend: { display: false },
                        tooltip: {
                            backgroundColor: '#1e293b',
                            titleColor: '#ffffff',
                            bodyColor: '#e2e8f0',
                            padding: 10,
                            cornerRadius: 6,
                            callbacks: {
                                label: function (context) {
                                    if (context.dataset.yAxisID === 'yRevenue') {
                                        return ' ' + context.dataset.label + ': ' + money(context.raw) + ' ₫';
                                    }
                                    return ' Lượt khám: ' + context.raw + ' lượt';
                                },
                            },
                        },
                    },
                    scales: {
                        x: {
                            grid: { display: false },
                            ticks: {
                                font: { family: "'Inter', sans-serif", size: 11 },
                                color: '#64748b',
                            },
                        },
                        yRevenue: {
                            type: 'linear',
                            position: 'left',
                            beginAtZero: true,
                            suggestedMin: 0,
                            suggestedMax: 1000000,
                            grid: { color: '#f1f5f9' },
                            ticks: {
                                font: { family: "'Inter', sans-serif", size: 11 },
                                color: '#64748b',
                                callback: function (val) {
                                    if (Math.abs(val) >= 1000000000) return (val / 1000000000).toFixed(1) + ' tỷ';
                                    if (Math.abs(val) >= 1000000) return (val / 1000000).toFixed(0) + 'M';
                                    if (Math.abs(val) >= 1000) return (val / 1000).toFixed(0) + 'k';
                                    return val;
                                },
                            },
                        },
                        yVisits: {
                            type: 'linear',
                            position: 'right',
                            beginAtZero: true,
                            suggestedMin: 0,
                            suggestedMax: 5,
                            grid: { drawOnChartArea: false },
                            ticks: {
                                font: { family: "'Inter', sans-serif", size: 11 },
                                color: '#f97316',
                                precision: 0,
                            },
                        },
                    },
                },
            });
        } else {
            // Fallback khi không có Chart.js
            const fb = find('#revenue-chart');
            if (fb && report.days?.length) {
                const maximum = Math.max(1, ...report.days.map((day) => Math.abs(day.revenue)));
                fb.style.display = 'flex';
                fb.innerHTML = report.days
                    .map(
                        (day) =>
                            /* HTML */ `<div
                                class="chart-day"
                                title="${escape(date(day.date) + ': ' + money(day.revenue) + ' ₫ · ' + day.visits + ' lượt')}"
                            >
                                <div
                                    class="chart-bar ${day.revenue < 0 ? 'negative' : ''}"
                                    style="height:${Math.max(2, (Math.abs(day.revenue) / maximum) * 160)}px"
                                ></div>
                                <small>${escape(date(day.date).slice(0, 5))}</small>
                            </div>`,
                    )
                    .join('');
            }
        }

        // 2. Donut Chart (Cơ cấu dòng tiền MISA style)
        const donutCanvas = document.getElementById('cashflowDonutCanvas');
        if (window.Chart && donutCanvas) {
            if (window._cashflowDonutInstance) {
                window._cashflowDonutInstance.destroy();
            }

            const deposits = Math.max(0, report.deposits || 0);
            const payments = Math.max(0, report.payments || 0);
            const refunds = Math.max(0, report.refunds || 0);
            const hasData = deposits > 0 || payments > 0 || refunds > 0;

            const dData = hasData ? [deposits, payments, refunds] : [1];
            const dColors = hasData
                ? ['#0068ff', '#10b981', '#f59e0b']
                : ['#e2e8f0'];
            const dLabels = hasData
                ? ['Tạm ứng viện phí', 'Thanh toán viện phí', 'Hoàn tiền / Miễn giảm']
                : ['Chưa có giao dịch'];

            const centerTotal = find('#donut-center-total');
            if (centerTotal) {
                centerTotal.textContent = hasData ? money(deposits + payments + refunds) + ' ₫' : '0 ₫';
            }

            const dCtx = donutCanvas.getContext('2d');
            window._cashflowDonutInstance = new window.Chart(dCtx, {
                type: 'doughnut',
                data: {
                    labels: dLabels,
                    datasets: [
                        {
                            data: dData,
                            backgroundColor: dColors,
                            borderWidth: 3,
                            borderColor: '#ffffff',
                            hoverOffset: 6,
                        },
                    ],
                },
                options: {
                    responsive: true,
                    maintainAspectRatio: false,
                    cutout: '72%',
                    plugins: {
                        legend: { display: false },
                        tooltip: {
                            backgroundColor: '#1e293b',
                            titleColor: '#ffffff',
                            bodyColor: '#e2e8f0',
                            padding: 10,
                            cornerRadius: 6,
                            callbacks: {
                                label: function (context) {
                                    if (!hasData) return ' Chưa có giao dịch trong kỳ';
                                    return ' ' + context.label + ': ' + money(context.raw) + ' ₫';
                                },
                            },
                        },
                    },
                },
            });
        }
    }

    async function loadReport(withFilter = true) {
        const version = ++reportVersion;
        const filter = find('#report-filter');
        const query = withFilter && filter ? '?' + new URLSearchParams(new FormData(filter)) : '';
        let report = await request('Report' + query);
        if (version !== reportVersion) return;

        currentReport = report;

        if (filter) {
            filter.elements.from.value = report.from;
            filter.elements.to.value = report.to;
        }

        const periodBadge = find('#report-period');
        if (periodBadge) {
            periodBadge.textContent = `${date(report.from)} – ${date(report.to)}`;
        }

        // Cập nhật các thẻ KPI MISA chuẩn
        const elRev = find('#kpi-revenue');
        if (elRev) elRev.textContent = money(report.revenue) + ' ₫';

        const elVisits = find('#kpi-visits');
        if (elVisits) elVisits.textContent = money(report.visits) + ' lượt';

        const elDeposits = find('#kpi-deposits');
        if (elDeposits) elDeposits.textContent = money(report.deposits) + ' ₫';
        const elDepositsText = find('#kpi-deposits-text');
        if (elDepositsText) elDepositsText.textContent = money(report.deposits) + ' ₫';

        const elPayments = find('#kpi-payments');
        if (elPayments) elPayments.textContent = money(report.payments) + ' ₫';

        const elRefunds = find('#kpi-refunds');
        if (elRefunds) elRefunds.textContent = money(report.refunds) + ' ₫';
        const elRefundsText = find('#kpi-refunds-text');
        if (elRefundsText) elRefundsText.textContent = money(report.refunds) + ' ₫';

        const elHighlight = find('#highlightTotalRevenue');
        if (elHighlight) elHighlight.textContent = money(report.revenue) + ' ₫';

        // Render biểu đồ
        renderCharts(report);

        // Render bảng chi tiết
        table(
            '#report-table',
            ['Ngày', 'Tạm ứng', 'Thanh toán', 'Hoàn tiền', 'Thực thu', 'Lượt tiếp nhận'],
            report.days.map((day) => [
                /* HTML */ `<strong>${escape(date(day.date))}</strong>`,
                ...[day.deposits, day.payments, day.refunds, day.revenue, day.visits].map(
                    (value) => /* HTML */ `<span class="number">${escape(money(value))}</span>`,
                ),
            ]),
        );
    }

    async function loadRequests() {
        const rows = await request('Requests');
        const statuses = { Pending: 'Chờ duyệt', Approved: 'Đã duyệt', Rejected: 'Đã từ chối' };
        table(
            '#requests-table',
            ['Bác sĩ / Khoa phòng', 'Ngày / Ca trực', 'Chi tiết đề nghị', 'Trạng thái', 'Thao tác xử lý'],
            rows.map((row) => [
                /* HTML */ `<strong>${escape(row.doctor)}</strong><br /><small class="text-muted"
                        >${escape(row.department)}</small
                    >`,
                /* HTML */ `<span class="misa-badge-date">${escape(date(row.date))}</span><br /><small
                        >${escape(row.shift)}</small
                    >`,
                /* HTML */ `<span class="req-type">${escape(row.type)}</span><br /><small class="text-muted"
                        >${escape(row.reason || 'Không có ghi chú')}</small
                    >`,
                /* HTML */ `<span class="misa-badge-status status-${row.status.toLowerCase()}"
                    >${escape(statuses[row.status] || row.status)}</span
                >`,
                row.canApprove || row.canReject
                    ? /* HTML */ `<div class="request-actions" data-request="${row.id}">
                          ${
                              row.canApprove
                                  ? /* HTML */ `<select aria-label="Bác sĩ thay thế" class="misa-select-sm">
                                        <option value="">
                                            ${escape(row.replacement ? 'Giữ đề nghị: ' + row.replacement : 'Tự phân bổ trong khoa')}
                                        </option>
                                        ${row.replacements.map((option) => /* HTML */ `<option value="${option.id}">${escape(option.name)}</option>`).join('')}
                                    </select>`
                                  : '<small class="text-muted">Ca đã bắt đầu hoặc đã qua; chỉ có thể từ chối.</small>'
                          }
                          <div class="misa-btn-group-sm">
                              ${row.canApprove ? '<button type="button" class="btn misa-btn-sm-primary" data-decision="approve">Duyệt ca</button>' : ''}${row.canReject ? '<button type="button" class="btn misa-btn-sm-secondary" data-decision="reject">Từ chối</button>' : ''}
                          </div>
                      </div>`
                    : /* HTML */ `<span class="processed-tag">${escape(row.replacement || 'Đã xử lý')}</span>`,
            ]),
        );
    }

    function closeNavigation() {
        find('#admin-sidebar')?.classList.remove('open');
        find('#admin-menu-toggle')?.setAttribute('aria-expanded', 'false');
        const scrim = find('.sidebar-scrim');
        if (scrim) scrim.hidden = true;
    }

    const menuToggle = find('#admin-menu-toggle');
    if (menuToggle) {
        menuToggle.addEventListener('click', () => {
            const sidebar = find('#admin-sidebar');
            const open = sidebar ? sidebar.classList.toggle('open') : false;
            menuToggle.setAttribute('aria-expanded', String(open));
            const scrim = find('.sidebar-scrim');
            if (scrim) scrim.hidden = !open;
        });
    }

    const scrimEl = find('.sidebar-scrim');
    if (scrimEl) scrimEl.addEventListener('click', closeNavigation);

    find('#admin-sidebar nav')?.addEventListener('click', (event) => {
        if (event.target.closest('[data-section]')) closeNavigation();
    });

    document.addEventListener('keydown', (event) => {
        if (event.key === 'Escape') {
            closeNavigation();
            find('#misa-quick-add-menu')?.setAttribute('hidden', '');
        }
    });

    function selectSection() {
        closeNavigation();
        const selected = location.hash.slice(1) || workspace.dataset.section;
        const section = titles[selected] ? selected : 'dashboard';

        document
            .querySelectorAll('[data-panel]')
            .forEach((panel) => (panel.hidden = panel.dataset.panel !== section));

        document.querySelectorAll('nav [data-section]').forEach((link) => {
            link.classList.toggle('active', link.dataset.section === section);
            if (link.dataset.section === section) link.setAttribute('aria-current', 'page');
            else link.removeAttribute('aria-current');
        });

        const msg = find('#admin-message');
        if (msg) msg.hidden = true;

        const breadcrumb = find('#admin-breadcrumb');
        if (breadcrumb && titles[section]) breadcrumb.textContent = titles[section][0];

        const pTitle = find('#page-title');
        if (pTitle && titles[section]) pTitle.textContent = titles[section][0];

        const pDesc = find('#page-description');
        if (pDesc && titles[section]) pDesc.textContent = titles[section][1];
    }

    function openAccount(staffOnly) {
        if (!directory)
            return notify('Danh sách chưa tải được. Hãy làm mới trước khi tạo tài khoản.', true);
        const form = find('#account-form');
        form.reset();
        form.querySelector('.form-error').textContent = '';
        const roles = directory.roles.filter((role) => !staffOnly || role.requiresDepartment);
        form.elements.roleId.innerHTML = roles
            .map((role) => /* HTML */ `<option value="${role.id}">${escape(role.name)}</option>`)
            .join('');
        form.elements.departmentId.innerHTML =
            '<option value="">Chọn phòng ban</option>' +
            directory.departments
                .map((row) => /* HTML */ `<option value="${row.id}">${escape(row.name)}</option>`)
                .join('');
        find('#account-title').textContent = staffOnly
            ? 'Thêm nhân sự và tài khoản'
            : 'Tạo tài khoản mới';
        updateRoleFields();
        find('#account-dialog').showModal();
    }

    function updateRoleFields() {
        const form = find('#account-form');
        const role = directory?.roles.find(
            (role) => role.id === Number(form.elements.roleId.value),
        );
        const fields = [
            ['[data-name-field]', role?.requiresName],
            ['[data-department-field]', role?.requiresDepartment],
            ['[data-patient-field]', role?.requiresPatient],
        ];
        for (const [selector, shown] of fields) {
            form.querySelectorAll(selector).forEach((field) => {
                field.hidden = !shown;
                field.querySelectorAll('input,select').forEach((input) => {
                    input.disabled = !shown;
                    input.required = !!shown;
                });
            });
        }
        find('#account-hint').textContent = role?.requiresName
            ? 'Tài khoản mới được kích hoạt và liên kết hồ sơ đúng vai trò/phòng ban. Mật khẩu không hiển thị trong danh sách.'
            : 'Tài khoản Admin dùng tên đăng nhập làm tên hiển thị.';
    }

    async function mutate(path, payload, form) {
        if (busy) return;
        busy = true;
        const enabled = [...document.querySelectorAll('button:not(:disabled)')];
        enabled.forEach((button) => (button.disabled = true));
        try {
            const result = await request(path, payload);
            form?.closest('dialog')?.close();
            form?.reset();
            notify(result.message);
            try {
                await Promise.all([loadDirectory(), loadRequests()]);
            } catch {
                notify(result.message + ' Danh sách chưa tải lại được; hãy bấm Làm mới.', true);
            }
        } catch (error) {
            if (form) form.querySelector('.form-error').textContent = error.message;
            else notify(error.message, true);
        } finally {
            busy = false;
            enabled
                .filter((button) => button.isConnected)
                .forEach((button) => (button.disabled = false));
        }
    }

    // Xử lý nút Thêm nhanh MISA
    const quickAddBtn = find('#misa-quick-add-btn');
    const quickAddMenu = find('#misa-quick-add-menu');
    if (quickAddBtn && quickAddMenu) {
        quickAddBtn.addEventListener('click', (e) => {
            e.stopPropagation();
            quickAddMenu.toggleAttribute('hidden');
        });
        find('#quick-add-staff-trigger')?.addEventListener('click', () => {
            quickAddMenu.setAttribute('hidden', '');
            openAccount(true);
        });
        find('#quick-add-account-trigger')?.addEventListener('click', () => {
            quickAddMenu.setAttribute('hidden', '');
            openAccount(false);
        });
    }

    // Xử lý các nút subtab biểu đồ THU / CHI / TỒN
    document.querySelectorAll('.misa-chart-subtab').forEach((tab) => {
        tab.addEventListener('click', (e) => {
            document.querySelectorAll('.misa-chart-subtab').forEach((t) => t.classList.remove('active'));
            tab.classList.add('active');
            const txt = tab.textContent.trim().toUpperCase();
            if (txt.includes('HOÀN') || txt.includes('CHI')) activeChartFlow = 'out';
            else if (txt.includes('THUẦN') || txt.includes('TỒN')) activeChartFlow = 'balance';
            else activeChartFlow = 'all';
            if (currentReport) renderCharts(currentReport);
        });
    });

    find('#chart-refresh-trigger')?.addEventListener('click', () => {
        if (currentReport) renderCharts(currentReport);
    });

    find('#report-filter')?.addEventListener('submit', (event) => {
        event.preventDefault();
        loadReport().catch((error) => notify(error.message, true));
    });

    find('#department-form')?.addEventListener('submit', (event) => {
        event.preventDefault();
        mutate(
            'Departments',
            Object.fromEntries(new FormData(event.currentTarget)),
            event.currentTarget,
        );
    });

    find('#account-form')?.addEventListener('submit', (event) => {
        event.preventDefault();
        const data = Object.fromEntries(new FormData(event.currentTarget));
        data.roleId = Number(data.roleId);
        data.departmentId = data.departmentId ? Number(data.departmentId) : null;
        data.dateOfBirth = data.dateOfBirth || null;
        mutate('Accounts', data, event.currentTarget);
    });

    find('#account-form [name="roleId"]')?.addEventListener('change', updateRoleFields);
    find('#new-staff')?.addEventListener('click', () => openAccount(true));
    find('#new-account')?.addEventListener('click', () => openAccount(false));
    find('#refresh-data')?.addEventListener('click', () =>
        refresh().catch((error) => notify(error.message, true)),
    );
    window.addEventListener('hashchange', selectSection);

    document.addEventListener('click', (event) => {
        if (quickAddMenu && !event.target.closest('.misa-quick-add-container')) {
            quickAddMenu.setAttribute('hidden', '');
        }

        const open = event.target.closest('[data-open]');
        if (open) {
            const dialog = document.getElementById(open.dataset.open);
            if (dialog) {
                dialog.querySelector('form')?.reset();
                const err = dialog.querySelector('.form-error');
                if (err) err.textContent = '';
                dialog.showModal();
            }
        }
        if (event.target.closest('[data-close]')) {
            event.target.closest('dialog')?.close();
        }
        const account = event.target.closest('[data-account]');
        if (account && confirm('Xác nhận thay đổi trạng thái truy cập của tài khoản này?')) {
            mutate('AccountStatus', {
                accountId: Number(account.dataset.account),
                isActive: account.dataset.active === 'true',
            });
        }
        const decision = event.target.closest('[data-decision]');
        if (
            decision &&
            confirm(
                'Xác nhận xử lý đề nghị này? Khi duyệt, lịch khám liên quan có thể được chuyển hoặc yêu cầu đặt lại.',
            )
        ) {
            const container = decision.closest('[data-request]');
            const replacement = container.querySelector('select')?.value;
            mutate('Decision', {
                exceptionId: Number(container.dataset.request),
                approve: decision.dataset.decision === 'approve',
                replacementDoctorId: replacement ? Number(replacement) : null,
            });
        }
    });

    // Xử lý chuyển đổi qua lại giữa Thẻ hồ sơ (Split View) và Bảng danh sách (Grid View)
    document.querySelectorAll('.misa-toggle-pill[data-view-target]').forEach((btn) => {
        btn.addEventListener('click', () => {
            const targetId = btn.dataset.viewTarget;
            const parent = btn.closest('.misa-view-toggle-pills');
            parent?.querySelectorAll('.misa-toggle-pill').forEach((b) => b.classList.remove('active'));
            btn.classList.add('active');

            if (targetId.startsWith('staff-')) {
                const isSplit = targetId === 'staff-split';
                const splitEl = find('#staff-split-view');
                const gridEl = find('#staff-grid-view');
                if (splitEl) splitEl.style.display = isSplit ? 'grid' : 'none';
                if (gridEl) gridEl.style.display = isSplit ? 'none' : 'block';
            } else if (targetId.startsWith('accounts-')) {
                const isSplit = targetId === 'accounts-split';
                const splitEl = find('#accounts-split-view');
                const gridEl = find('#accounts-grid-view');
                if (splitEl) splitEl.style.display = isSplit ? 'grid' : 'none';
                if (gridEl) gridEl.style.display = isSplit ? 'none' : 'block';
            }
        });
    });

    // Xử lý tìm kiếm thời gian thực trên danh sách bên trái (Master list)
    ['staff', 'accounts'].forEach((key) => {
        const splitSearch = find(`#${key}-split-search`);
        if (splitSearch) {
            splitSearch.addEventListener('input', (e) => {
                const q = e.target.value.toLowerCase().trim();
                const items = document.querySelectorAll(`#${key}-master-list .misa-split-item`);
                items.forEach((item) => {
                    const text = item.textContent.toLowerCase();
                    item.style.display = text.includes(q) ? 'flex' : 'none';
                });
            });
        }
    });

    find('#export-revenue')?.addEventListener('click', async () => {
        const form = find('#report-filter');
        if (!form || !form.reportValidity()) return;
        const button = find('#export-revenue');
        button.disabled = true;
        try {
            const query = new URLSearchParams(new FormData(form));
            const response = await fetch(api + '/Revenue.csv?' + query, {
                credentials: 'same-origin',
                cache: 'no-store',
            });
            if (
                response.redirected ||
                !response.ok ||
                !response.headers.get('content-type')?.includes('text/csv')
            ) {
                throw new Error(
                    'Chưa xuất được báo cáo. Hãy kiểm tra phiên đăng nhập và khoảng thời gian.',
                );
            }
            const url = URL.createObjectURL(await response.blob());
            const link = document.createElement('a');
            link.href = url;
            link.download = `doanh-thu-${query.get('from')}-${query.get('to')}.csv`;
            link.click();
            setTimeout(() => URL.revokeObjectURL(url), 1000);
        } catch (error) {
            notify(error.message, true);
        } finally {
            button.disabled = false;
        }
    });

    // Tìm kiếm nhanh trong bảng theo từ khóa
    const searchInput = find('#misa-global-search');
    if (searchInput) {
        searchInput.addEventListener('input', (e) => {
            const query = e.target.value.toLowerCase().trim();
            const activePanel = document.querySelector('[data-panel]:not([hidden])');
            if (!activePanel) return;
            const rows = activePanel.querySelectorAll('tbody tr');
            rows.forEach((row) => {
                if (row.querySelector('td.empty')) return;
                const text = row.textContent.toLowerCase();
                row.style.display = text.includes(query) ? '' : 'none';
            });
        });
    }

    async function refresh() {
        const filter = find('#report-filter');
        await Promise.all([
            loadDirectory(),
            loadRequests(),
            loadReport(!!filter?.elements?.from?.value),
        ]);
    }

    selectSection();
    refresh().catch((error) => notify(error.message, true));
})();

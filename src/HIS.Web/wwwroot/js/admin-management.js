(() => {
    'use strict';

    const workspace = document.querySelector('.admin-workspace');
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
        new Intl.NumberFormat('vi-VN', { maximumFractionDigits: 2 }).format(value);
    const date = (value) => String(value).split('-').reverse().join('/');
    const titles = {
        dashboard: [
            'Tổng quan',
            'Theo dõi số lượt tiếp nhận và dòng tiền theo thời gian.',
        ],
        requests: ['Nghỉ / đổi ca', 'Duyệt đề nghị và sắp xếp lại lịch khám trong khoa.'],
        scheduling: [
            'Lịch làm việc',
            'Theo dõi và sắp xếp ca làm việc của bác sĩ trong khoa.',
        ],
        departments: ['Phòng ban', 'Danh sách khoa và đơn vị chuyên môn trong bệnh viện.'],
        rooms: ['Phòng khám', 'Quản lý buồng / phòng khám thuộc từng khoa chuyên môn.'],
        services: ['Dịch vụ & bảng giá', 'Thiết lập danh mục dịch vụ kỹ thuật, xét nghiệm và giá niêm yết.'],
        medications: ['Danh mục thuốc', 'Quản lý danh mục thuốc, hoạt chất, hàm lượng và đơn giá.'],
        staff: ['Quản lý nhân sự', 'Danh sách bác sĩ, y tá và trưởng khoa theo phòng ban.'],
        doctors: ['Danh sách bác sĩ', 'Theo dõi chuyên môn và đơn vị công tác.'],
        accounts: ['Quản lý tài khoản', 'Tạo tài khoản, kích hoạt và ngừng hoạt động truy cập.'],
        audit: ['Nhật ký truy vết', 'Theo dõi lịch sử truy cập hồ sơ bệnh án và phân quyền y tế.'],
    };

    let directory = null;
    let catalog = null;
    let schedulingState = null;
    let requestRows = [];
    let requestStatusFilter = 'all';
    let schedulingWeekOffset = 0;
    let schedViewMode = 'progress';
    let rosterSearch = '';
    let rosterPage = 1;
    let reqStatus = 'pending';
    let reqSearch = '';
    let reqPage = 1;
    let reqSelectedId = 0;
    let reqRooms = new Map();
    let assignDoctors = new Set();
    let assignSearch = '';
    const ROSTER_PAGE_SIZE = 5;
    const REQ_PAGE_SIZE = 5;
    let busy = false;
    let reportVersion = 0;
    let auditVersion = 0;
    const REPORT_PAGE_SIZE = 5;
    let reportDays = [];
    let reportPage = 1;

    const checkIconSvg = '<svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"><polyline points="20 6 9 17 4 12"></polyline></svg>';
    const errorIconSvg = '<svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="10"></circle><line x1="12" y1="8" x2="12" y2="12"></line><line x1="12" y1="16" x2="12.01" y2="16"></line></svg>';

    function notify(message, error = false) {
        const target = find('#admin-message');
        if (!target) return;
        const titleEl = target.querySelector('.toast-title');
        const textEl = target.querySelector('.toast-text');
        const iconEl = target.querySelector('.toast-icon');

        if (titleEl && textEl && iconEl) {
            titleEl.textContent = error ? 'Lỗi thao tác' : 'Thành công';
            textEl.textContent = message;
            iconEl.innerHTML = error ? errorIconSvg : checkIconSvg;
        } else {
            target.textContent = message;
        }

        target.classList.toggle('error', error);
        target.classList.toggle('success', !error);
        target.hidden = false;

        if (typeof clearTimeout === 'function') clearTimeout(notify.timer);
        if (typeof setTimeout === 'function') {
            notify.timer = setTimeout(() => {
                target.hidden = true;
            }, 4500);
        }
    }

    function askConfirm(message, title = 'Xác nhận thao tác', okText = 'Đồng ý', isDanger = false) {
        const dialog = find('#admin-confirm-dialog');
        if (!dialog || typeof dialog.showModal !== 'function') {
            return Promise.resolve(confirm(message));
        }

        const msgEl = find('#admin-confirm-msg');
        const titleEl = find('#admin-confirm-title');
        const okBtn = find('#admin-confirm-ok');
        const cancelBtn = dialog.querySelector('[data-confirm-cancel]');
        const closeBtn = dialog.querySelector('[data-close]');

        if (msgEl) msgEl.textContent = message;
        if (titleEl) titleEl.textContent = title;
        if (okBtn) {
            okBtn.textContent = okText;
            okBtn.className = `btn button ${isDanger ? 'danger' : 'primary'}`;
        }

        return new Promise((resolve) => {
            const cleanup = () => {
                okBtn?.removeEventListener('click', onOk);
                cancelBtn?.removeEventListener('click', onCancel);
                closeBtn?.removeEventListener('click', onCancel);
                dialog.removeEventListener('close', onCancel);
            };

            const onOk = () => {
                cleanup();
                dialog.close();
                resolve(true);
            };

            const onCancel = () => {
                cleanup();
                if (dialog.open) dialog.close();
                resolve(false);
            };

            okBtn?.addEventListener('click', onOk);
            cancelBtn?.addEventListener('click', onCancel);
            closeBtn?.addEventListener('click', onCancel);
            dialog.addEventListener('close', onCancel, { once: true });

            dialog.showModal();
        });
    }

    async function request(path, data) {
        const headers = { Accept: 'application/json' };
        if (data !== undefined) {
            headers['Content-Type'] = 'application/json';
            headers.RequestVerificationToken = find('#admin-token input').value;
        }

        const response = await fetch(api + '/' + path, {
            method: data === undefined ? 'GET' : 'POST',
            credentials: 'same-origin',
            cache: 'no-store',
            headers,
            body: data === undefined ? undefined : JSON.stringify(data),
        });

        if (response.status === 499) {
            return { candidates: [], availableRooms: [], totalActiveRooms: 0 };
        }

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
            .map((heading) => /* HTML */ `<th scope="col">${escape(heading)}</th>`)
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
        target.innerHTML = /* HTML */ `<table>
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

    function accountRows(rows, mode) {
        return rows.map((row) => [
            /* HTML */ `<strong>${escape(row.name)}</strong>`,
            escape(row.username),
            escape(row.roleName),
            escape(row.department || '—'),
            /* HTML */ `<span class="status ${row.isActive ? '' : 'inactive'}"
                >${row.isActive ? 'Đang hoạt động' : 'Ngừng hoạt động'}</span
            >`,
            ...(mode
                ? [
                      /* HTML */ `<div class="row-actions">
                          <button
                              type="button"
                              class="btn button sm small-button secondary"
                              data-edit-account="${row.id}"
                              data-staff="${mode === 'staff'}"
                          >
                              Sửa
                          </button>
                          ${
                              row.canChangeStatus
                                  ? /* HTML */ `<button
                                        type="button"
                                        class="btn button sm small-button ${row.isActive ? 'danger' : 'secondary'}"
                                        data-account="${row.id}"
                                        data-active="${!row.isActive}"
                                    >
                                        ${row.isActive ? 'Ngừng hoạt động' : 'Kích hoạt'}
                                    </button>`
                                  : '<span>Đang đăng nhập</span>'
                          }
                      </div>`,
                  ]
                : []),
        ]);
    }

    function renderDepartmentsTable(filterDeptId = '') {
        if (!directory?.departments) return;
        const filtered = filterDeptId
            ? directory.departments.filter((d) => String(d.id) === String(filterDeptId))
            : directory.departments;

        table(
            '#departments-table',
            ['Mã', 'Tên phòng ban', 'Mô tả', 'Thao tác'],
            filtered.map((row) => [
                escape(row.id),
                /* HTML */ `<strong>${escape(row.name)}</strong>`,
                escape(row.description || '—'),
                /* HTML */ `<div class="row-actions">
                    <button type="button" class="btn button sm small-button secondary" data-edit-department="${row.id}">Sửa</button>
                    <button type="button" class="btn button sm small-button danger" data-delete-department="${row.id}">Xóa</button>
                </div>`,
            ]),
        );
    }

    async function loadDirectory() {
        directory = await request('Directory');
        const headings = [
            'Họ tên / tên hiển thị',
            'Tên đăng nhập',
            'Vai trò',
            'Phòng ban',
            'Trạng thái',
        ];
        table('#accounts-table', [...headings, 'Thao tác'], accountRows(directory.accounts, 'accounts'));
        table('#staff-table', [...headings, 'Thao tác'], accountRows(directory.staff, 'staff'));
        const filterDept = find('#filter-department');
        if (filterDept) {
            const currentVal = filterDept.value;
            filterDept.innerHTML =
                '<option value="">Tất cả phòng ban</option>' +
                directory.departments
                    .map((d) => /* HTML */ `<option value="${d.id}">${escape(d.name)}</option>`)
                    .join('');
            filterDept.value = currentVal;
            renderDepartmentsTable(filterDept.value);
        } else {
            renderDepartmentsTable('');
        }
        table(
            '#doctors-table',
            ['Họ tên', 'Chuyên môn', 'Phòng ban', 'Chứng chỉ hành nghề'],
            directory.doctors.map((row) => [
                /* HTML */ `<strong>${escape(row.fullName)}</strong>`,
                escape(row.specialty || '—'),
                escape(row.departmentName),
                escape(row.licenseNumber || '—'),
            ]),
        );
    }

    async function loadReport(withFilter = true) {
        const version = ++reportVersion;
        const filter = find('#report-filter');

        // Khởi tạo mặc định 7 ngày gần nhất nếu chưa chọn ngày
        if (!filter.elements.from.value || !filter.elements.to.value) {
            const today = new Date();
            const past7 = new Date();
            past7.setDate(today.getDate() - 6);
            filter.elements.to.value = today.toISOString().slice(0, 10);
            filter.elements.from.value = past7.toISOString().slice(0, 10);
        }

        const query = withFilter ? '?' + new URLSearchParams(new FormData(filter)) : '';
        const report = await request('Report' + query);
        if (version !== reportVersion) return;

        filter.elements.from.value = report.from;
        filter.elements.to.value = report.to;
        find('#report-period').textContent = `${date(report.from)} – ${date(report.to)}`;
        const statConfig = [
            {
                label: 'Doanh thu thực thu',
                value: money(report.revenue) + ' ₫',
                icon: /* HTML */ `<svg class="stat-svg" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M12 2v20M17 5H9.5a3.5 3.5 0 0 0 0 7h5a3.5 3.5 0 0 1 0 7H6"/></svg>`
            },
            {
                label: 'Lượt tiếp nhận',
                value: money(report.visits) + ' lượt',
                icon: /* HTML */ `<svg class="stat-svg" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2"/><circle cx="9" cy="7" r="4"/><path d="M23 21v-2a4 4 0 0 0-3-3.87"/><path d="M16 3.13a4 4 0 0 1 0 7.75"/></svg>`
            },
            {
                label: 'Tạm ứng',
                value: money(report.deposits) + ' ₫',
                icon: /* HTML */ `<svg class="stat-svg" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><rect x="2" y="5" width="20" height="14" rx="2"/><line x1="2" y1="10" x2="22" y2="10"/></svg>`
            },
            {
                label: 'Hoàn tiền',
                value: money(report.refunds) + ' ₫',
                icon: /* HTML */ `<svg class="stat-svg" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="23 4 23 10 17 10"/><path d="M20.49 15a9 9 0 1 1-2.12-9.36L23 10"/></svg>`
            }
        ];

        find('#report-stats').innerHTML = statConfig
            .map(
                (item) =>
                    /* HTML */ `<article class="stat">
                        <div class="stat-icon-wrap">${item.icon}</div>
                        <div class="stat-body">
                            <span class="stat-label">${escape(item.label)}</span>
                            <strong class="stat-value" title="${escape(item.value)}">${escape(item.value)}</strong>
                        </div>
                    </article>`,
            )
            .join('');

        const maximum = Math.max(1, ...report.days.map((day) => Math.abs(day.revenue)));
        const rawMaximum = Math.max(0, ...report.days.map((day) => Math.abs(day.revenue)));
        const axis = find('#revenue-chart-y');
        if (axis) {
            if (rawMaximum <= 0) {
                axis.innerHTML = '<span></span><span></span><span></span><span></span><span>0 ₫</span>';
            } else {
                const compact = (value) =>
                    value >= 1e9 ? money(value / 1e9) + ' tỷ' : value >= 1e6 ? money(value / 1e6) + ' tr' : money(value);
                axis.innerHTML = [rawMaximum, rawMaximum * 0.75, rawMaximum * 0.5, rawMaximum * 0.25, 0]
                    .map((value) => /* HTML */ `<span>${escape(compact(value))}</span>`)
                    .join('');
            }
        }
        find('#revenue-chart').classList.toggle('is-empty', rawMaximum <= 0);
        const barTones = ['#86efac', '#4ade80', '#22c55e', '#16a34a', '#15803d', '#107c41', '#0d6535'];
        find('#revenue-chart').innerHTML = report.days
            .map((day, index) => {
                const isZero = day.revenue === 0;
                const pct = isZero ? 2 : Math.max(6, Math.round((Math.abs(day.revenue) / maximum) * 100));
                const barHeight = isZero ? '3px' : pct + '%';
                const formattedRev = money(day.revenue) + ' ₫';
                const tone = barTones[index % barTones.length];
                return /* HTML */ `<div class="chart-day" title="${escape(date(day.date) + ': ' + formattedRev + ' · ' + day.visits + ' lượt')}">
                    <span class="chart-bar-tooltip">${formattedRev}</span>
                    <div class="chart-bar-wrap">
                        <div
                            class="chart-bar ${day.revenue < 0 ? 'negative' : isZero ? 'zero-rev' : ''}"
                            style="height:${barHeight}; --bar-tone:${tone}"
                        ></div>
                    </div>
                    <span class="chart-day-label">${escape(date(day.date).slice(0, 5))}</span>
                </div>`;
            })
            .join('');
        reportDays = report.days || [];
        renderReportPage(1);

        try {
            const deptStats = await request('DepartmentStats' + query);
            renderDonutChart(deptStats);
            table(
                '#department-stats-table',
                ['Khoa chuyên môn', 'Lượt khám', 'Thực thu'],
                deptStats.map((row) => [
                    /* HTML */ `<strong>${escape(row.departmentName)}</strong>`,
                    /* HTML */ `<span class="number">${escape(money(row.visits))} lượt</span>`,
                    /* HTML */ `<span class="number ${row.revenue > 0 ? 'money-pos' : ''}">${escape(money(row.revenue))} ₫</span>`,
                ]),
            );
        } catch {
            renderDonutChart([]);
            table('#department-stats-table', ['Khoa chuyên môn', 'Lượt khám', 'Thực thu'], []);
            notify('Chưa tải được thống kê doanh thu theo khoa.', true);
        }
    }

    function renderDonutChart(deptStats) {
        const segmentsContainer = find('#donut-segments');
        const legendContainer = find('#donut-legend');
        const totalNum = find('#donut-center-total');
        const totalBadge = find('#donut-total-badge');
        if (!segmentsContainer || !legendContainer) return;

        const totalVisits = (deptStats || []).reduce((sum, d) => sum + (d.visits || 0), 0);
        if (totalNum) totalNum.textContent = totalVisits.toLocaleString('vi-VN');
        if (totalBadge) totalBadge.textContent = totalVisits + ' lượt';

        if (!deptStats || deptStats.length === 0 || totalVisits === 0) {
            segmentsContainer.innerHTML = '';
            legendContainer.innerHTML = '<div style="font-size:11.5px;color:#94a3b8;text-align:center;padding:12px;">Chưa có lượt tiếp nhận trong kỳ này</div>';
            return;
        }

        const colors = ['#107c41', '#16a34a', '#22c55e', '#4ade80', '#86efac', '#cbd5e1'];
        const sorted = [...deptStats].filter(d => d.visits > 0).sort((a, b) => b.visits - a.visits);
        const topDepts = sorted.slice(0, 4);
        const otherVisits = sorted.slice(4).reduce((sum, d) => sum + d.visits, 0);
        if (otherVisits > 0) {
            topDepts.push({ departmentName: 'Khoa khác', visits: otherVisits, isOther: true });
        }

        const radius = 56;
        const circumference = 2 * Math.PI * radius;
        let cumulativePct = 0;

        const segmentsHtml = topDepts.map((d, index) => {
            const pct = d.visits / totalVisits;
            const strokeDasharray = `${(pct * circumference).toFixed(2)} ${circumference.toFixed(2)}`;
            const strokeDashoffset = (-cumulativePct * circumference).toFixed(2);
            cumulativePct += pct;
            const color = d.isOther ? '#cbd5e1' : colors[index % colors.length];

            return `<circle cx="80" cy="80" r="${radius}" fill="none" stroke="${color}" stroke-width="22" stroke-dasharray="${strokeDasharray}" stroke-dashoffset="${strokeDashoffset}" stroke-linecap="butt" />`;
        }).join('');

        segmentsContainer.innerHTML = segmentsHtml;

        const legendHtml = topDepts.map((d, index) => {
            const color = d.isOther ? '#cbd5e1' : colors[index % colors.length];
            const pct = Math.round((d.visits / totalVisits) * 100);
            return `<div class="donut-legend-item">
                <div class="donut-legend-left">
                    <span class="donut-legend-dot" style="background:${color}"></span>
                    <span class="donut-legend-name" title="${escape(d.departmentName)}">${escape(d.departmentName)}</span>
                </div>
                <div class="donut-legend-right">
                    <strong class="donut-legend-val">${d.visits}</strong>
                    <span class="donut-legend-pct">${pct}%</span>
                </div>
            </div>`;
        }).join('');

        legendContainer.innerHTML = legendHtml;
    }

    function renderReportPage(page) {
        const totalPages = Math.max(1, Math.ceil(reportDays.length / REPORT_PAGE_SIZE));
        reportPage = Math.min(Math.max(1, page), totalPages);
        const startIndex = (reportPage - 1) * REPORT_PAGE_SIZE;
        const pageDays = reportDays.slice(startIndex, startIndex + REPORT_PAGE_SIZE);

        table(
            '#report-table',
            ['Ngày', 'Lượt khám', 'Thực thu'],
            pageDays.map((day) => [
                escape(date(day.date)),
                /* HTML */ `<span class="number">${escape(money(day.visits))}</span>`,
                /* HTML */ `<span class="number ${day.revenue > 0 ? 'money-pos' : ''}">${escape(money(day.revenue))} ₫</span>`,
            ]),
        );

        const pager = find('#report-pagination');
        if (!pager) return;
        if (reportDays.length <= REPORT_PAGE_SIZE) {
            pager.hidden = true;
            pager.innerHTML = '';
            return;
        }
        pager.hidden = false;

        const startPage = Math.max(1, Math.min(reportPage - 2, totalPages - 4));
        const endPage = Math.min(totalPages, startPage + 4);
        const pageButtons = [];
        for (let page = startPage; page <= endPage; page += 1) {
            pageButtons.push(
                /* HTML */ `<button type="button" class="pager-btn ${page === reportPage ? 'active' : ''}" data-report-page="${page}" aria-label="Trang ${page}">${page}</button>`,
            );
        }

        pager.innerHTML = /* HTML */ `
            <span class="pager-summary">
                ${startIndex + 1}–${Math.min(startIndex + REPORT_PAGE_SIZE, reportDays.length)} / ${reportDays.length} ngày
            </span>
            <div class="pager-buttons">
                <button type="button" class="pager-btn" id="report-prev-page" aria-label="Trang trước" ${reportPage <= 1 ? 'disabled' : ''}>‹</button>
                ${pageButtons.join('')}
                <button type="button" class="pager-btn" id="report-next-page" aria-label="Trang sau" ${reportPage >= totalPages ? 'disabled' : ''}>›</button>
            </div>
        `;
        find('#report-prev-page')?.addEventListener('click', () => renderReportPage(reportPage - 1));
        find('#report-next-page')?.addEventListener('click', () => renderReportPage(reportPage + 1));
        pager.querySelectorAll('[data-report-page]').forEach((button) =>
            button.addEventListener('click', () => renderReportPage(Number(button.dataset.reportPage))),
        );
    }

    function renderRoomsTable(filterDeptId = '') {
        if (!catalog?.rooms) return;
        const filtered = filterDeptId
            ? catalog.rooms.filter((r) => String(r.departmentId) === String(filterDeptId))
            : catalog.rooms;

        table(
            '#rooms-table',
            ['Khoa / Phòng ban', 'Số phòng', 'Tên phòng khám', 'Lịch trực liên kết', 'Thao tác'],
            filtered.map((row) => [
                escape(row.departmentName),
                /* HTML */ `<strong>${escape(row.roomNumber)}</strong>`,
                escape(row.roomName),
                /* HTML */ `<span class="status ${row.activeSchedulesCount > 0 ? '' : 'inactive'}">${row.activeSchedulesCount} lịch phân công</span>`,
                /* HTML */ `<div class="row-actions">
                    <button type="button" class="btn button sm small-button secondary" data-edit-room="${row.id}">Sửa</button>
                    ${
                        row.activeSchedulesCount === 0
                            ? /* HTML */ `<button type="button" class="btn button sm small-button danger" data-delete-room="${row.id}">Xóa</button>`
                            : '<span>Đang sử dụng</span>'
                    }
                </div>`,
            ]),
        );
    }

    async function loadCatalog() {
        catalog = await request('CatalogDirectory');

        const filterRoomDept = find('#filter-room-department');
        if (filterRoomDept) {
            const currentVal = filterRoomDept.value;
            filterRoomDept.innerHTML =
                '<option value="">Tất cả khoa chuyên môn</option>' +
                catalog.departments
                    .map((d) => {
                        const count = catalog.rooms.filter((r) => r.departmentId === d.id).length;
                        return /* HTML */ `<option value="${d.id}">${escape(d.name)} (${count} phòng)</option>`;
                    })
                    .join('');
            filterRoomDept.value = currentVal;
            renderRoomsTable(filterRoomDept.value);
        } else {
            renderRoomsTable('');
        }

        table(
            '#services-table',
            ['Mã', 'Loại dịch vụ', 'Tên dịch vụ kỹ thuật', 'Đơn giá niêm yết', 'Trạng thái', 'Thao tác'],
            catalog.services.map((row) => [
                escape(row.id),
                /* HTML */ `<span class="status">${escape(row.serviceTypeName)}</span>`,
                /* HTML */ `<strong>${escape(row.name)}</strong>`,
                /* HTML */ `<span class="number">${escape(money(row.price))} ₫</span>`,
                /* HTML */ `<span class="status ${row.isActive ? '' : 'inactive'}">${row.isActive ? 'Áp dụng' : 'Tạm ngưng'}</span>`,
                /* HTML */ `<button type="button" class="btn button sm small-button secondary" data-edit-service="${row.id}">Sửa</button>`,
            ]),
        );

        table(
            '#medications-table',
            ['Mã', 'Tên thuốc', 'Hoạt chất', 'Hàm lượng', 'Đơn vị', 'Đơn giá', 'Trạng thái', 'Thao tác'],
            catalog.medications.map((row) => [
                escape(row.id),
                /* HTML */ `<strong>${escape(row.name)}</strong>`,
                escape(row.activeIngredient || '—'),
                escape(row.strength || '—'),
                escape(row.unit),
                /* HTML */ `<span class="number">${escape(money(row.price))} ₫</span>`,
                /* HTML */ `<span class="status ${row.isActive ? '' : 'inactive'}">${row.isActive ? 'Kê đơn' : 'Tạm ngưng'}</span>`,
                /* HTML */ `<button type="button" class="btn button sm small-button secondary" data-edit-medication="${row.id}">Sửa</button>`,
            ]),
        );

        const roomDeptSelect = find('#room-form [name="departmentId"]');
        if (roomDeptSelect) {
            roomDeptSelect.innerHTML = '<option value="">Chọn khoa chuyên môn</option>' +
                catalog.departments.map((d) => /* HTML */ `<option value="${d.id}">${escape(d.name)}</option>`).join('');
        }

        const serviceTypeSelect = find('#service-form [name="serviceTypeId"]');
        if (serviceTypeSelect) {
            serviceTypeSelect.innerHTML = '<option value="">Chọn loại dịch vụ</option>' +
                catalog.serviceTypes.map((st) => /* HTML */ `<option value="${st.id}">${escape(st.name)}</option>`).join('');
        }
    }

    async function loadAudit(withFilter = null) {
        const filter = find('#audit-filter');
        let query = '';
        let searchKeyword = '';
        const shouldFilter = withFilter !== null
            ? withFilter
            : Boolean(filter && (filter.elements.from?.value || filter.elements.to?.value || filter.elements.action?.value || filter.elements.search?.value));

        if (filter && shouldFilter) {
            const formData = new FormData(filter);
            const params = new URLSearchParams();
            if (formData.get('from')) params.set('from', formData.get('from'));
            if (formData.get('to')) params.set('to', formData.get('to'));
            if (formData.get('action')) params.set('action', formData.get('action'));
            if (formData.get('search')) {
                searchKeyword = (formData.get('search') || '').trim().toLowerCase();
                params.set('search', (formData.get('search') || '').trim());
            }
            const qs = params.toString();
            if (qs) query = '?' + qs;
        }

        const currentVersion = ++auditVersion;
        let rows = await request('AuditLogs' + query);
        if (currentVersion !== auditVersion) return;

        const actionMap = {
            VIEW_RECORD: { label: 'Xem bệnh án', cls: 'info' },
            CREATE_RECORD: { label: 'Lưu bệnh án', cls: '' },
            CHECK_IN_WEB: { label: 'Tiếp nhận hẹn', cls: 'info' },
            REGISTER_WALK_IN: { label: 'Tiếp nhận quầy', cls: 'info' },
            ADMIN_CREATE_ACCOUNT: { label: 'Tạo tài khoản', cls: '' },
            ADMIN_ACTIVATE_ACCOUNT: { label: 'Kích hoạt TK', cls: '' },
            ADMIN_DEACTIVATE_ACCOUNT: { label: 'Khóa tài khoản', cls: 'danger' },
            ADMIN_APPROVE_SCHEDULE: { label: 'Duyệt lịch trực', cls: '' },
            ADMIN_REJECT_SCHEDULE: { label: 'Từ chối ca', cls: 'danger' },
            AdminLogin: { label: 'Đăng nhập', cls: 'warning' },
            ViewDepartmentCatalog: { label: 'Xem danh mục', cls: 'neutral' },
        };

        if (searchKeyword) {
            rows = rows.filter((r) => {
                const actionLabel = (actionMap[r.action]?.label || '').toLowerCase();
                return (r.fullName && r.fullName.toLowerCase().includes(searchKeyword)) ||
                    (r.username && r.username.toLowerCase().includes(searchKeyword)) ||
                    (r.patientName && r.patientName.toLowerCase().includes(searchKeyword)) ||
                    (r.ipAddress && r.ipAddress.toLowerCase().includes(searchKeyword)) ||
                    (r.action && r.action.toLowerCase().includes(searchKeyword)) ||
                    actionLabel.includes(searchKeyword);
            });
        }

        const countEl = find('#audit-count');
        if (countEl) {
            countEl.textContent = `${rows.length} sự kiện`;
        }

        table(
            '#audit-table',
            ['Thời gian', 'Tài khoản / Nhân sự', 'Hành động', 'Bệnh nhân', 'Bệnh án', 'Địa chỉ IP'],
            rows.map((row) => {
                const act = actionMap[row.action] || { label: row.action, cls: 'neutral' };
                return [
                    /* HTML */ `<span style="white-space:nowrap; font-size:12px; color:#475569;">${escape(new Date(row.occurredAt).toLocaleString('vi-VN'))}</span>`,
                    /* HTML */ `<strong>${escape(row.fullName)}</strong><br /><small style="color:#64748b;">${escape(row.username)}</small>`,
                    /* HTML */ `<span class="status ${act.cls}" title="${escape(row.action)}">${escape(act.label)}</span>`,
                    row.patientName ? /* HTML */ `<strong>${escape(row.patientName)}</strong>` : '<span style="color:#94a3b8;">—</span>',
                    row.medicalRecordId ? /* HTML */ `<strong style="color:#0f766e;">#BA-${escape(row.medicalRecordId)}</strong>` : '<span style="color:#94a3b8;">—</span>',
                    /* HTML */ `<code style="font-size:12px; font-weight:600; color:#0369a1;">${escape(row.ipAddress || 'nội bộ')}</code>`,
                ];
            }),
        );
    }

    function syncRequestsDeptFilter() {
        const sel = find('#requests-dept-filter');
        if (!sel || !directory?.departments) return;
        const prev = sel.value;
        sel.innerHTML =
            '<option value="">-- Chọn khoa để duyệt --</option>' +
            directory.departments.map((d) => /* HTML */ `<option value="${d.id}">${escape(d.name)}</option>`).join('');
        if (prev && directory.departments.some((d) => String(d.id) === prev)) {
            sel.value = prev;
        } else if (directory.departments.length > 0 && !sel.value) {
            sel.value = String(directory.departments[0].id);
        }
    }

    async function loadRequests() {
        requestRows = await request('Requests');
        syncRequestsDeptFilter();
        renderRequestsTable();
    }

    function renderRequestsTable() {
        const statuses = { Pending: 'Chờ duyệt', Approved: 'Đã duyệt', Rejected: 'Từ chối' };
        const selectedDeptId = Number(find('#requests-dept-filter')?.value || 0);
        const deptFiltered = selectedDeptId > 0
            ? requestRows.filter((r) => Number(r.departmentId) === selectedDeptId)
            : [];
        const filtered = requestStatusFilter === 'all'
            ? deptFiltered
            : deptFiltered.filter((r) => r.status === requestStatusFilter);

        document.querySelectorAll('[data-req-filter]').forEach((btn) => {
            const active = btn.dataset.reqFilter === requestStatusFilter;
            btn.className = `btn button ${active ? 'primary' : 'secondary'} sm`;
        });

        table(
            '#requests-table',
            ['Bác sĩ', 'Ngày / Ca', 'Loại / Lý do', 'Đề xuất thay thế', 'Trạng thái', 'Thao tác'],
            filtered.map((row) => [
                /* HTML */ `<strong>${escape(row.doctor)}</strong><br /><small style="color:#64748b;">${escape(row.department)}</small>`,
                /* HTML */ `${escape(date(row.date))}<br /><small style="color:#64748b;">${escape(row.shift)}</small>`,
                /* HTML */ `<strong>${escape(row.type)}</strong><br /><small style="color:#64748b;">${escape(row.reason || 'Không có ghi chú')}</small>`,
                row.replacement ? escape(row.replacement) : '<span style="color:#94a3b8;">—</span>',
                /* HTML */ `<span class="status ${row.status === 'Approved' ? '' : 'inactive'}">${escape(statuses[row.status] || row.status)}</span>`,
                row.canApprove || row.canReject
                    ? /* HTML */ `<div class="request-actions" data-request="${row.id}">
                          ${
                              row.canApprove
                                  ? /* HTML */ `<select aria-label="Bác sĩ thay thế">
                                        <option value="">
                                            ${escape(row.replacement ? 'Giữ đề nghị: ' + row.replacement : 'Tự phân bổ trong khoa')}
                                        </option>
                                        ${row.replacements.map((option) => /* HTML */ `<option value="${option.id}">${escape(option.name)}</option>`).join('')}
                                    </select>`
                                  : '<small>Ca đã bắt đầu hoặc đã qua; chỉ có thể từ chối.</small>'
                          }
                          <div>
                              ${row.canApprove ? '<button type="button" class="btn button primary sm small-button" data-decision="approve">Duyệt</button>' : ''}${row.canReject ? '<button type="button" class="btn button secondary sm small-button" data-decision="reject">Từ chối</button>' : ''}
                          </div>
                      </div>`
                    : /* HTML */ `<span style="color:#64748b;">${escape(row.approvedAt ? date(String(row.approvedAt).slice(0, 10)) : 'Đã xử lý')}</span>`,
            ]),
        );
    }

    find('#requests-status-tabs')?.addEventListener('click', (e) => {
        const btn = e.target.closest('[data-req-filter]');
        if (!btn) return;
        requestStatusFilter = btn.dataset.reqFilter;
        renderRequestsTable();
    });

    find('#requests-dept-filter')?.addEventListener('change', () => {
        renderRequestsTable();
    });

    function closeNavigation() {
        find('#admin-sidebar').classList.remove('open');
        find('#admin-menu-toggle').setAttribute('aria-expanded', 'false');
        find('.admin-sidebar-scrim').hidden = true;
    }

    find('#admin-menu-toggle').addEventListener('click', () => {
        const open = find('#admin-sidebar').classList.toggle('open');
        find('#admin-menu-toggle').setAttribute('aria-expanded', String(open));
        find('.admin-sidebar-scrim').hidden = !open;
    });
    find('.admin-sidebar-scrim').addEventListener('click', closeNavigation);
    find('#admin-sidebar nav').addEventListener('click', (event) => {
        const link = event.target.closest('[data-section]');
        if (link) {
            closeNavigation();
            const section = link.dataset.section;
            if (section && titles[section]) {
                selectSection(section);
            }
        }
    });
    document.addEventListener('keydown', (event) => {
        if (event.key === 'Escape') closeNavigation();
    });

    const quickSearch = find('#admin-quick-search');
    if (quickSearch) {
        document.addEventListener('keydown', (event) => {
            if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'k') {
                event.preventDefault();
                quickSearch.focus();
                quickSearch.select();
            }
        });
        quickSearch.addEventListener('keydown', (event) => {
            if (event.key !== 'Enter') return;
            event.preventDefault();
            const keyword = quickSearch.value.trim().toLowerCase();
            if (!keyword) return;
            const link = [...document.querySelectorAll('#admin-sidebar nav [data-section]')].find((item) =>
                item.textContent.toLowerCase().includes(keyword),
            );
            if (!link) {
                notify(`Không tìm thấy mục phù hợp với "${quickSearch.value.trim()}".`, true);
                return;
            }
            quickSearch.value = '';
            find('#admin-message').hidden = true;
            selectSection(link.dataset.section);
        });
    }

    function selectSection(explicitSection) {
        closeNavigation();
        const selected = explicitSection || location.hash.slice(1) || workspace.dataset.section;
        const section = titles[selected] ? selected : 'dashboard';
        document
            .querySelectorAll('[data-panel]')
            .forEach((panel) => (panel.hidden = panel.dataset.panel !== section));
        document.querySelectorAll('nav [data-section]').forEach((link) => {
            link.classList.toggle('active', link.dataset.section === section);
            if (link.dataset.section === section) link.setAttribute('aria-current', 'page');
            else link.removeAttribute('aria-current');
        });
        find('#admin-message').hidden = true;
        find('#page-title').textContent = titles[section][0];
        find('#admin-breadcrumb').textContent = titles[section][0];
        find('#page-description').textContent = titles[section][1];
        const reportFilter = find('#report-filter');
        if (reportFilter) reportFilter.hidden = section !== 'dashboard';
    }

    function openAccount(staffOnly, row = null) {
        if (!directory)
            return notify('Danh sách chưa tải được. Hãy làm mới trước khi tạo tài khoản.', true);
        const form = find('#account-form');
        form.reset();
        form.querySelector('.form-error').textContent = '';
        const editing = !!row;
        form.dataset.mode = editing ? 'edit' : 'create';
        form.elements.id.value = editing ? row.id : '';

        const roles = directory.roles.filter(
            (role) => !staffOnly || role.requiresDepartment || role.code === 'ACCOUNTANT',
        );
        // Khi sửa quản trị viên, chỉ giữ đúng vai trò hiện tại để tránh tự hạ quyền hệ thống
        form.elements.roleId.innerHTML = (editing && row.role === 'ADMIN'
            ? roles.filter((role) => role.code === 'ADMIN')
            : roles
        )
            .map((role) => /* HTML */ `<option value="${role.id}">${escape(role.name)}</option>`)
            .join('');
        form.elements.departmentId.innerHTML =
            '<option value="">Chọn phòng ban</option>' +
            directory.departments
                .map((department) => /* HTML */ `<option value="${department.id}">${escape(department.name)}</option>`)
                .join('');

        form.elements.username.value = editing ? row.username : '';
        form.elements.username.readOnly = editing;
        form.elements.password.value = '';
        form.elements.password.required = !editing;
        find('#account-password-label').textContent = editing
            ? 'Mật khẩu mới (để trống nếu giữ nguyên)'
            : 'Mật khẩu ban đầu *';
        if (editing) {
            const currentRole = directory.roles.find((role) => role.code === row.role);
            if (currentRole) form.elements.roleId.value = String(currentRole.id);
            form.elements.fullName.value = row.name;
        }

        find('#account-title').textContent = editing
            ? staffOnly
                ? 'Cập nhật nhân sự'
                : 'Cập nhật tài khoản'
            : staffOnly
              ? 'Thêm nhân sự và tài khoản'
              : 'Tạo tài khoản';
        find('#account-submit').textContent = editing ? 'Lưu thay đổi' : 'Tạo tài khoản';
        updateRoleFields();

        // Đặt sau updateRoleFields vì hàm này bật lại input theo vai trò đang chọn
        form.elements.isActive.checked = editing ? row.isActive : true;
        form.elements.isActive.disabled = editing && !row.canChangeStatus;
        find('#account-dialog').showModal();
    }

    function updateRoleFields() {
        const form = find('#account-form');
        const editing = form.dataset.mode === 'edit';
        const role = directory?.roles.find(
            (role) => role.id === Number(form.elements.roleId.value),
        );
        const fields = [
            ['[data-name-field]', role?.requiresName],
            ['[data-department-field]', !editing && role?.requiresDepartment],
            ['[data-patient-field]', !editing && role?.requiresPatient],
            ['[data-email-field]', !editing],
            ['[data-status-field]', editing],
        ];
        for (const [selector, shown] of fields) {
            form.querySelectorAll(selector).forEach((field) => {
                field.hidden = !shown;
                field.querySelectorAll('input,select').forEach((input) => {
                    input.disabled = !shown;
                    input.required = !!shown && input.type !== 'checkbox';
                });
            });
        }
        find('#account-hint').textContent = editing
            ? `Đang sửa tài khoản ${form.elements.username.value}. ${
                  role?.requiresDepartment ? 'Phòng ban hiện tại được giữ nguyên. ' : ''
              }Để trống mật khẩu nếu không đổi.`
            : role?.requiresName
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
                await Promise.all([loadDirectory(), loadRequests(), loadCatalog(), loadAudit(), loadScheduling()]);
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

    find('#report-filter').addEventListener('submit', (event) => {
        event.preventDefault();
        loadReport().catch((error) => notify(error.message, true));
    });

    find('#department-form').addEventListener('submit', (event) => {
        event.preventDefault();
        const form = event.currentTarget;
        const data = Object.fromEntries(new FormData(form));
        const id = data.id ? Number(data.id) : null;
        delete data.id;
        mutate(id ? 'Departments/Update' : 'Departments', id ? { id, ...data } : data, form);
    });

    find('#room-form').addEventListener('submit', (event) => {
        event.preventDefault();
        const form = event.currentTarget;
        const data = Object.fromEntries(new FormData(form));
        const id = data.id ? Number(data.id) : null;
        delete data.id;
        data.departmentId = Number(data.departmentId);
        mutate(id ? 'Rooms/Update' : 'Rooms', id ? { id, ...data } : data, form);
    });

    find('#service-form').addEventListener('submit', (event) => {
        event.preventDefault();
        const form = event.currentTarget;
        const data = Object.fromEntries(new FormData(form));
        data.id = data.id ? Number(data.id) : null;
        data.serviceTypeId = Number(data.serviceTypeId);
        data.price = Number(data.price);
        data.isActive = form.elements.isActive.checked;
        mutate('Services', data, form);
    });

    find('#medication-form').addEventListener('submit', (event) => {
        event.preventDefault();
        const form = event.currentTarget;
        const data = Object.fromEntries(new FormData(form));
        data.id = data.id ? Number(data.id) : null;
        data.price = Number(data.price);
        data.isActive = form.elements.isActive.checked;
        mutate('Medications', data, form);
    });

    find('#account-form').addEventListener('submit', (event) => {
        event.preventDefault();
        const form = event.currentTarget;
        const data = Object.fromEntries(new FormData(form));
        const id = data.id ? Number(data.id) : null;
        delete data.id;
        data.roleId = Number(data.roleId);
        if (form.dataset.mode === 'edit') {
            mutate(
                'Accounts/Update',
                {
                    id,
                    fullName: data.fullName || form.elements.username.value,
                    roleId: data.roleId,
                    password: data.password || null,
                    isActive: form.elements.isActive.checked,
                },
                form,
            );
            return;
        }
        data.departmentId = data.departmentId ? Number(data.departmentId) : null;
        data.dateOfBirth = data.dateOfBirth || null;
        mutate('Accounts', data, form);
    });

    find('#account-form [name="roleId"]').addEventListener('change', updateRoleFields);
    find('#audit-filter')?.addEventListener('submit', (e) => {
        e.preventDefault();
        loadAudit(true).catch((err) => notify(err.message, true));
    });
    find('#reset-audit')?.addEventListener('click', () => {
        const filter = find('#audit-filter');
        if (filter) {
            filter.reset();
            loadAudit(false).catch((err) => notify(err.message, true));
        }
    });
    find('#filter-room-department')?.addEventListener('change', (e) => {
        renderRoomsTable(e.target.value);
    });
    find('#filter-department')?.addEventListener('change', (e) => {
        renderDepartmentsTable(e.target.value);
    });
    find('#new-staff').addEventListener('click', () => openAccount(true));
    find('#new-account').addEventListener('click', () => openAccount(false));
    find('#refresh-data').addEventListener('click', () =>
        refresh().catch((error) => notify(error.message, true)),
    );
    // Không truyền thẳng selectSection: hashchange sẽ đưa đối tượng event vào làm tên mục và luôn quay về dashboard
    window.addEventListener('hashchange', () => selectSection());

    document.addEventListener('click', async (event) => {
        const toastClose = event.target.closest('#admin-message .toast-close');
        if (toastClose) {
            const msg = find('#admin-message');
            if (msg) msg.hidden = true;
            return;
        }
        const open = event.target.closest('[data-open]');
        if (open) {
            const dialog = document.getElementById(open.dataset.open);
            dialog.querySelector('form').reset();
            dialog.querySelector('.form-error').textContent = '';
            if (open.id === 'new-service') {
                dialog.querySelector('#service-title').textContent = 'Thêm dịch vụ y tế & Bảng giá';
                dialog.querySelector('form').elements.id.value = '';
            }
            if (open.id === 'new-medication') {
                dialog.querySelector('#medication-title').textContent = 'Thêm thuốc & Biệt dược';
                dialog.querySelector('form').elements.id.value = '';
            }
            if (open.dataset.open === 'department-dialog') {
                dialog.querySelector('#department-title').textContent = 'Thêm phòng ban';
                dialog.querySelector('form').elements.id.value = '';
                dialog.querySelector('form button[type="submit"]').textContent = 'Lưu phòng ban';
            }
            if (open.dataset.open === 'room-dialog') {
                dialog.querySelector('form').elements.id.value = '';
                dialog.querySelector('#room-title').textContent = 'Thêm phòng khám';
                dialog.querySelector('form button[type="submit"]').textContent = 'Lưu phòng khám';
                const currentFilter = find('#filter-room-department')?.value;
                if (currentFilter) {
                    dialog.querySelector('form').elements.departmentId.value = currentFilter;
                }
            }
            dialog.showModal();
        }
        if (event.target.closest('[data-close]')) event.target.closest('dialog').close();
        const account = event.target.closest('[data-account]');
        if (account && await askConfirm('Xác nhận thay đổi trạng thái truy cập của tài khoản này?', 'Trạng thái tài khoản')) {
            mutate('AccountStatus', {
                accountId: Number(account.dataset.account),
                isActive: account.dataset.active === 'true',
            });
        }
        const editRoom = event.target.closest('[data-edit-room]');
        if (editRoom) {
            const item = catalog?.rooms.find((room) => room.id === Number(editRoom.dataset.editRoom));
            if (item) {
                const dialog = document.getElementById('room-dialog');
                const form = dialog.querySelector('form');
                form.reset();
                form.querySelector('.form-error').textContent = '';
                form.elements.id.value = item.id;
                form.elements.departmentId.value = String(item.departmentId);
                form.elements.roomNumber.value = item.roomNumber;
                form.elements.roomName.value = item.roomName;
                dialog.querySelector('#room-title').textContent = 'Sửa phòng khám';
                form.querySelector('button[type="submit"]').textContent = 'Lưu thay đổi';
                dialog.showModal();
            }
        }
        const deleteRoom = event.target.closest('[data-delete-room]');
        if (deleteRoom && await askConfirm('Xác nhận xóa phòng khám này khỏi khoa?', 'Xóa phòng khám', 'Xóa', true)) {
            mutate('Rooms/Delete', { roomId: Number(deleteRoom.dataset.deleteRoom) });
        }
        const editDepartment = event.target.closest('[data-edit-department]');
        if (editDepartment) {
            const item = directory?.departments.find(
                (department) => department.id === Number(editDepartment.dataset.editDepartment),
            );
            if (item) {
                const dialog = document.getElementById('department-dialog');
                const form = dialog.querySelector('form');
                form.reset();
                form.querySelector('.form-error').textContent = '';
                form.elements.id.value = item.id;
                form.elements.name.value = item.name;
                form.elements.description.value = item.description || '';
                dialog.querySelector('#department-title').textContent = 'Sửa phòng ban';
                dialog.querySelector('button[type="submit"]').textContent = 'Lưu thay đổi';
                dialog.showModal();
            }
        }
        const deleteDepartment = event.target.closest('[data-delete-department]');
        if (
            deleteDepartment &&
            await askConfirm(
                'Xác nhận xóa phòng ban này? Chỉ xóa được khi chưa có bác sĩ hoặc phòng khám liên kết.',
                'Xóa phòng ban',
                'Xóa',
                true,
            )
        ) {
            mutate('Departments/Delete', {
                departmentId: Number(deleteDepartment.dataset.deleteDepartment),
            });
        }
        const editAccount = event.target.closest('[data-edit-account]');
        if (editAccount) {
            const staffOnly = editAccount.dataset.staff === 'true';
            const item = (staffOnly ? directory?.staff : directory?.accounts)?.find(
                (account) => account.id === Number(editAccount.dataset.editAccount),
            );
            if (item) openAccount(staffOnly, item);
        }
        const editService = event.target.closest('[data-edit-service]');
        if (editService) {
            const item = catalog?.services.find((s) => s.id === Number(editService.dataset.editService));
            if (item) {
                const dialog = document.getElementById('service-dialog');
                const form = dialog.querySelector('form');
                form.reset();
                form.elements.id.value = item.id;
                form.elements.serviceTypeId.value = item.serviceTypeId;
                form.elements.name.value = item.name;
                form.elements.price.value = item.price;
                form.elements.isActive.checked = item.isActive;
                dialog.querySelector('#service-title').textContent = 'Cập nhật dịch vụ y tế';
                dialog.showModal();
            }
        }
        const editMedication = event.target.closest('[data-edit-medication]');
        if (editMedication) {
            const item = catalog?.medications.find((m) => m.id === Number(editMedication.dataset.editMedication));
            if (item) {
                const dialog = document.getElementById('medication-dialog');
                const form = dialog.querySelector('form');
                form.reset();
                form.elements.id.value = item.id;
                form.elements.name.value = item.name;
                form.elements.activeIngredient.value = item.activeIngredient || '';
                form.elements.strength.value = item.strength || '';
                form.elements.unit.value = item.unit;
                form.elements.price.value = item.price;
                form.elements.isActive.checked = item.isActive;
                dialog.querySelector('#medication-title').textContent = 'Cập nhật thuốc & biệt dược';
                dialog.showModal();
            }
        }
        const decision = event.target.closest('[data-decision]');
        if (
            decision &&
            await askConfirm(
                'Xác nhận xử lý đề nghị này? Khi duyệt, lịch khám liên quan có thể được chuyển hoặc yêu cầu đặt lại.',
                'Duyệt đề nghị',
            )
        ) {
            const container = decision.closest('[data-request]');
            const replacement = container.querySelector('select')?.value;
            const departmentId = Number(find('#requests-dept-filter')?.value || 0);
            if (!departmentId) return notify('Vui lòng chọn khoa chuyên môn trước khi duyệt.', true);
            mutate('Decision', {
                exceptionId: Number(container.dataset.request),
                approve: decision.dataset.decision === 'approve',
                replacementDoctorId: replacement ? Number(replacement) : null,
                departmentId,
            });
        }
    });

    find('#export-revenue').addEventListener('click', async () => {
        if (!find('#report-filter').reportValidity()) return;
        const button = find('#export-revenue');
        button.disabled = true;
        try {
            const query = new URLSearchParams(new FormData(find('#report-filter')));
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

    async function loadScheduling(deptIdOverride = null) {
        const deptSelect = find('#scheduling-dept');
        if (!deptSelect) return;

        if (directory?.departments) {
            const currentVal = deptIdOverride ?? (deptSelect.value || String(directory.departments[0]?.id ?? ''));
            deptSelect.innerHTML = directory.departments
                .map((d) => /* HTML */ `<option value="${d.id}">${escape(d.name)}</option>`)
                .join('');
            if (currentVal) deptSelect.value = String(currentVal);

            const scopeSelect = find('#period-scope-dept');
            if (scopeSelect) {
                const prevScope = scopeSelect.value;
                scopeSelect.innerHTML = directory.departments
                    .map((d) => /* HTML */ `<option value="${d.id}">${escape(d.name)}</option>`)
                    .join('');
                if (prevScope && directory.departments.some((d) => String(d.id) === prevScope)) {
                    scopeSelect.value = prevScope;
                }
            }
        }

        const explicitDept = deptIdOverride ?? (deptSelect.dataset.userPicked ? Number(deptSelect.value || 0) : 0);
        const params = new URLSearchParams();
        if (explicitDept > 0) params.set('departmentId', String(explicitDept));
        params.set('weekOffset', String(schedulingWeekOffset));
        schedulingState = await request('SchedulingOverview?' + params.toString());
        if (schedulingState?.selectedDeptId && deptSelect.value !== String(schedulingState.selectedDeptId)) {
            deptSelect.value = String(schedulingState.selectedDeptId);
        }

        reqRooms = new Map();
        const publishBtn = find('#publish-period-btn');
        if (publishBtn) {
            const period = schedulingState?.activePeriod;
            const isPublished = !!period && (period.status === 3 || period.status === 'Published');
            publishBtn.hidden = !period || isPublished;
            if (period) publishBtn.dataset.periodId = period.id;
            const generateBtn = find('#sched-generate-slots');
            if (generateBtn) generateBtn.hidden = !(schedulingState?.board?.totals?.departmentsReady > 0);
        }
        const rangeBtn = find('#sched-this-week');
        if (rangeBtn) rangeBtn.textContent = schedWeekText();

        renderSchedPeriodPill();
        renderSchedKpis();
        renderDeptProgress();
        renderWeeklyScheduleGrid();
        renderAssign();
        renderRequests();
        applySchedView(schedViewMode);
    }

    const SCHED_DAY_LABELS = ['Chủ Nhật', 'Thứ Hai', 'Thứ Ba', 'Thứ Tư', 'Thứ Năm', 'Thứ Sáu', 'Thứ Bảy'];

    function schedShiftOf(s) {
        return s?.shift === 1 || s?.shift === 'Afternoon' ? 1 : 0;
    }

    function schedIsApproved(s) {
        return s?.status === 1 || s?.status === 'Approved';
    }

    function schedIsPending(s) {
        return s?.status === 0 || s?.status === 'Bidding';
    }

    function schedIsActive(s) {
        return schedIsApproved(s) || schedIsPending(s);
    }

    function schedWeekDays() {
        const start = schedulingState?.weekStart;
        if (!start) return [];
        const today = schedulingState?.today;
        const base = new Date(start + 'T00:00:00');
        const days = [];
        for (let i = 0; i < 7; i += 1) {
            const d = new Date(base);
            d.setDate(base.getDate() + i);
            const dd = String(d.getDate()).padStart(2, '0');
            const mm = String(d.getMonth() + 1).padStart(2, '0');
            const iso = `${d.getFullYear()}-${mm}-${dd}`;
            days.push({
                iso,
                dayName: SCHED_DAY_LABELS[d.getDay()],
                shortDate: `${dd}/${mm}`,
                isToday: iso === today,
                isPast: today ? iso < today : false,
                isWeekend: d.getDay() === 0 || d.getDay() === 6,
            });
        }
        return days;
    }

    function applySchedView(mode) {
        schedViewMode = ['progress', 'roster', 'assign', 'requests'].includes(mode) ? mode : 'progress';
        ['progress', 'roster', 'assign', 'requests'].forEach((name) => {
            const pane = find(`#sched-view-${name}`);
            if (pane) pane.hidden = name !== schedViewMode;
        });

        const fullPane = schedViewMode === 'assign' || schedViewMode === 'requests';
        const board = find('#sched-board');
        if (board) board.hidden = fullPane;
        const kpis = find('#sched-kpis');
        if (kpis) kpis.hidden = fullPane;
        const tools = find('#sched-board-tools');
        if (tools) tools.hidden = schedViewMode !== 'roster';

        document.querySelectorAll('[data-sched-view]').forEach((btn) => {
            const active = btn.dataset.schedView === schedViewMode;
            btn.classList.toggle('is-active', active);
            btn.setAttribute('aria-selected', active ? 'true' : 'false');
        });

        const heading = {
            progress: titles.scheduling,
            roster: titles.scheduling,
            assign: ['Phân lịch bác sĩ', 'Xếp ca và phòng khám cho bác sĩ trong khoa.'],
            requests: ['Duyệt yêu cầu', 'Ca bác sĩ đã đăng ký, chờ bạn duyệt và gán phòng.'],
        }[schedViewMode];
        const pageTitle = find('#page-title');
        const pageSub = find('#page-description');
        const activeSection = find('#admin-sidebar nav .nav-item.active')?.dataset.section;
        if (pageTitle && activeSection === 'scheduling') pageTitle.textContent = heading[0];
        if (pageSub && activeSection === 'scheduling') pageSub.textContent = heading[1];

        if (schedViewMode === 'assign') renderAssign();
        if (schedViewMode === 'requests') renderRequests();
    }

    const schedHhmm = (t) => (t ? String(t).slice(0, 5) : '');
    const schedInitials = (name, fallback) => (name || fallback).trim().split(/\s+/).slice(-2).map((w) => w[0]?.toUpperCase() || '').join('');
    const schedNorm = (value) => String(value || '').normalize('NFD').replace(/[\u0300-\u036f]/g, '').replace(/đ/g, 'd').toLowerCase();

    function schedWeekText() {
        const start = schedulingState?.weekStart;
        const end = schedulingState?.weekEnd;
        if (!start || !end) return '—';
        const s = new Date(start + 'T00:00:00');
        const e = new Date(end + 'T00:00:00');
        const sm = s.getMonth() + 1;
        const em = e.getMonth() + 1;
        if (sm === em) return `${s.getDate()} – ${e.getDate()} tháng ${sm}, ${e.getFullYear()}`;
        return `${s.getDate()} tháng ${sm} – ${e.getDate()} tháng ${em}, ${e.getFullYear()}`;
    }

    function renderSchedPeriodPill() {
        const host = find('#sched-period-pill');
        if (!host) return;
        const rows = schedulingState?.board?.departments || [];
        const rangeText = find('#sched-this-week')?.textContent.trim() || 'tuần đang xem';
        if (rows.length > 0) {
            const openCount = rows.filter((row) => row.hasOpenPeriod).length;
            const publishedCount = rows.filter((row) => row.isPublished).length;
            if (publishedCount > 0) {
                host.className = 'sched-period-pill is-published';
                host.textContent = 'Đã xuất bản';
                host.title = `${rangeText} · ${publishedCount}/${rows.length} khoa đã chốt lịch`;
            } else if (openCount > 0) {
                host.className = 'sched-period-pill is-open';
                host.textContent = 'Đang mở đăng ký';
                host.title = `${rangeText} · ${openCount}/${rows.length} khoa đang mở đăng ký`;
            } else {
                host.className = 'sched-period-pill is-none';
                host.textContent = 'Chưa có đợt cho tuần này';
                host.removeAttribute('title');
            }
            return;
        }
        const period = schedulingState?.activePeriod;
        if (!period) {
            host.className = 'sched-period-pill is-none';
            host.textContent = 'Chưa có kỳ đăng ký';
            host.removeAttribute('title');
            return;
        }
        const isPublished = period.status === 3 || period.status === 'Published';
        const state = isPublished ? 'is-published' : (period.isOpenForBidding ? 'is-open' : 'is-closed');
        const label = isPublished ? 'Đã xuất bản' : (period.isOpenForBidding ? 'Đang mở đăng ký' : 'Đã đóng đăng ký');
        host.className = `sched-period-pill ${state}`;
        host.textContent = label;
        host.title = `${period.name || 'Kỳ làm việc'} · ${label}`;
    }

    function renderSchedKpis() {
        const host = find('#sched-kpis');
        if (!host) return;
        const totals = schedulingState?.board?.totals || {};
        const ready = Number(totals.departmentsReady || 0);
        const deptTotal = Number(totals.departmentsTotal || 0);
        const registered = Number(totals.doctorsRegistered || 0);
        const doctorTotal = Number(totals.doctorsTotal || 0);
        const pending = Number(totals.pendingShifts || 0);
        const understaffed = Number(totals.understaffedShifts || 0);
        const icon = (path) => /* HTML */ `<span class="sched-kpi-icon"><svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">${path}</svg></span>`;
        const fraction = (value, total) => `${value}<span class="sched-kpi-sub"> / ${total}</span>`;
        host.innerHTML = /* HTML */ `
            <article class="sched-kpi" title="Số khoa đã công bố lịch / tổng số khoa có bác sĩ đang hoạt động">
                ${icon('<rect x="3" y="3" width="7" height="7" rx="1.5"></rect><rect x="14" y="3" width="7" height="7" rx="1.5"></rect><rect x="3" y="14" width="7" height="7" rx="1.5"></rect><rect x="14" y="14" width="7" height="7" rx="1.5"></rect>')}
                <div class="sched-kpi-main">
                    <span class="sched-kpi-label">Khoa đã chốt lịch</span>
                    <strong class="sched-kpi-value">${fraction(ready, deptTotal)}</strong>
                </div>
            </article>
            <article class="sched-kpi" title="Bác sĩ có ca đã duyệt hoặc chờ duyệt / tổng bác sĩ đang hoạt động">
                ${icon('<path d="M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2"></path><circle cx="9" cy="7" r="4"></circle><polyline points="16 11 18 13 22 9"></polyline>')}
                <div class="sched-kpi-main">
                    <span class="sched-kpi-label">Bác sĩ đã đăng ký</span>
                    <strong class="sched-kpi-value">${fraction(registered, doctorTotal)}</strong>
                </div>
            </article>
            <button type="button" class="sched-kpi ${pending > 0 ? 'is-action' : ''}" ${pending > 0 ? 'data-sched-kpi="pending"' : ''} title="${pending > 0 ? 'Mở danh sách ca chờ duyệt của khoa đang chọn' : 'Không có ca chờ duyệt'}">
                ${icon('<circle cx="12" cy="12" r="9"></circle><polyline points="12 7 12 12 15 14"></polyline>')}
                <div class="sched-kpi-main">
                    <span class="sched-kpi-label">Ca chờ duyệt</span>
                    <strong class="sched-kpi-value ${pending > 0 ? 'is-warn' : ''}">${pending}</strong>
                </div>
            </button>
            <article class="sched-kpi ${understaffed > 0 ? 'is-warn' : ''}" title="Ca có bác sĩ đăng ký nhưng chưa đủ so với số phòng hoạt động">
                ${icon('<path d="M10.29 3.86 1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z"></path><line x1="12" y1="9" x2="12" y2="13"></line><line x1="12" y1="17" x2="12.01" y2="17"></line>')}
                <div class="sched-kpi-main">
                    <span class="sched-kpi-label">Ca thiếu người</span>
                    <strong class="sched-kpi-value ${understaffed > 0 ? 'is-warn' : ''}">${understaffed}</strong>
                </div>
            </article>
        `;
    }

    function renderDeptProgress() {
        const host = find('#dept-progress');
        if (!host) return;
        const rows = schedulingState?.board?.departments || [];
        if (rows.length === 0) {
            host.innerHTML = '<p class="empty">Chưa có khoa nào có bác sĩ đang hoạt động.</p>';
            return;
        }
        const badge = (row) => {
            if (row.pendingShifts > 0) return `<span class="dp-badge is-pending">Chờ duyệt ${row.pendingShifts}</span>`;
            if (row.isPublished) return '<span class="dp-badge is-final">Đã chốt</span>';
            if (row.hasOpenPeriod) return '<span class="dp-badge is-open">Đang mở</span>';
            return '<span class="dp-badge is-idle">Chưa mở</span>';
        };
        const allPeriods = schedulingState?.periods || [];
        const latestPeriodOf = (deptId) => allPeriods.find((p) => p.departmentId === deptId) || null;
        host.innerHTML = /* HTML */ `
            <div class="dp-scroll">
                <table class="dept-progress">
                    <thead>
                        <tr>
                            <th>Khoa</th>
                            <th>Trưởng khoa</th>
                            <th>Đợt duyệt ca mới nhất</th>
                            <th>Bác sĩ đăng ký</th>
                            <th>Trạng thái duyệt</th>
                            <th class="dp-actions-col">Thao tác</th>
                        </tr>
                    </thead>
                    <tbody>
                        ${rows.map((row) => {
                            const percent = row.doctorCount > 0 ? Math.round((row.registeredDoctorCount / row.doctorCount) * 100) : 0;
                            const latest = latestPeriodOf(row.departmentId);
                            return /* HTML */ `
                                <tr>
                                    <td class="dp-name">${escape(row.name)}</td>
                                    <td class="dp-head">${escape(row.headName || '—')}</td>
                                    <td>
                                        ${latest
                                            ? /* HTML */ `<strong>${escape(latest.name)}</strong><br /><small style="color:#64748b;">${escape(date(latest.startDate))} – ${escape(date(latest.endDate))}</small>`
                                            : '<span style="color:#94a3b8;">Chưa có đợt</span>'}
                                    </td>
                                    <td>
                                        <div class="dp-progress">
                                            <span class="dp-bar"><i style="width:${Math.min(100, Math.max(0, percent))}%"></i></span>
                                            <b>${row.registeredDoctorCount}/${row.doctorCount}</b>
                                        </div>
                                    </td>
                                    <td>${badge(row)}</td>
                                    <td class="dp-actions">
                                        <button type="button" class="dp-action" data-sched-dept="${row.departmentId}" data-sched-dept-view="roster">Chi tiết</button>
                                        <button type="button" class="dp-action" data-sched-dept="${row.departmentId}" data-sched-dept-view="assign">Xếp ca</button>
                                        ${row.pendingShifts > 0
                                            ? `<button type="button" class="dp-action is-strong" data-sched-dept="${row.departmentId}" data-sched-dept-view="requests">Duyệt ${row.pendingShifts}</button>`
                                            : ''}
                                    </td>
                                </tr>
                            `;
                        }).join('')}
                    </tbody>
                </table>
            </div>
        `;
    }

    function schedDoctorsFiltered() {
        const query = schedNorm(rosterSearch.trim());
        return (schedulingState?.doctors || []).filter(
            (d) => !query || schedNorm(`${d.fullName} ${d.specialty || ''}`).includes(query),
        );
    }

    function renderWeeklyScheduleGrid() {
        const container = find('#dept-schedules-grid');
        if (!container) return;

        const schedules = (schedulingState?.schedules || []).filter(schedIsActive);
        const dutyShifts = schedulingState?.dutyShifts || [];
        const days = schedWeekDays();
        const approved = schedules.filter(schedIsApproved).length;
        const pending = schedules.filter(schedIsPending).length;
        const meta = find('#roster-meta');
        if (meta) meta.textContent = `${approved} ca đã duyệt · ${pending} chờ duyệt`;

        if (days.length === 0) {
            container.innerHTML = '<p class="empty">Chưa có dữ liệu tuần làm việc.</p>';
            return;
        }

        const doctors = schedDoctorsFiltered();
        const pages = Math.max(1, Math.ceil(doctors.length / ROSTER_PAGE_SIZE));
        rosterPage = Math.min(Math.max(1, rosterPage), pages);
        const start = (rosterPage - 1) * ROSTER_PAGE_SIZE;
        const pageRows = doctors.slice(start, start + ROSTER_PAGE_SIZE);

        const roomOf = (sched) => sched.roomNumber || 'Chưa phòng';
        const scheduleTitle = (sched, shift) => {
            const schedStart = schedHhmm(sched.startTime) || (shift === 0 ? '07:30' : '13:00');
            const end = schedHhmm(sched.endTime) || (shift === 0 ? '11:30' : '16:30');
            return `${shift === 0 ? 'Ca sáng' : 'Ca chiều'} ${schedStart} – ${end} · ${roomOf(sched)}`;
        };
        const checkIcon = '<svg width="11" height="11" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="3" aria-hidden="true"><polyline points="20 6 9 17 4 12"></polyline></svg>';

        const pill = (sched, shift) => {
            if (schedIsPending(sched)) {
                return /* HTML */ `
                    <button type="button" class="wk-pill is-pending" data-req-open-schedule="${sched.id}" title="Chờ duyệt · ${escape(scheduleTitle(sched, shift))} — bấm để duyệt">
                        <span class="wk-pill-room">${escape(roomOf(sched))}</span><span class="wk-pill-flag">· Duyệt</span>
                    </button>
                `;
            }
            return /* HTML */ `
                <div class="wk-pill ${shift === 0 ? 'is-morning' : 'is-afternoon'}" data-edit-schedule="${sched.id}" title="${escape(scheduleTitle(sched, shift))} — bấm để chỉnh">
                    <button type="button" class="wk-pill-edit" data-edit-schedule="${sched.id}" title="Sửa ca" aria-label="Sửa ca">
                        <svg width="9" height="9" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5"><path d="M12 20h9"></path><path d="M16.5 3.5a2.12 2.12 0 0 1 3 3L7 19l-4 1 1-4z"></path></svg>
                    </button>
                    <span class="wk-pill-room">${escape(roomOf(sched))}</span>${checkIcon}
                    <button type="button" class="wk-pill-del" data-delete-schedule="${sched.id}" title="Hủy ca" aria-label="Hủy ca">×</button>
                </div>
            `;
        };

        const cell = (doctor, day, shift) => {
            const sched = schedules.find((s) => s.doctorId === doctor.id && s.workDate === day.iso && schedShiftOf(s) === shift);
            const duty = shift === 0
                ? dutyShifts.find((ds) => ds.accountId === doctor.accountId && String(ds.startTime).slice(0, 10) === day.iso)
                : null;
            const chips = [];
            if (sched) chips.push(pill(sched, shift));
            if (duty) {
                const range = `${schedHhmm(String(duty.startTime).slice(11))} – ${schedHhmm(String(duty.endTime).slice(11))}`;
                chips.push(/* HTML */ `<span class="wk-duty" title="Trực đêm · ${range} · ${escape(duty.note || 'Trực đêm tại khoa')}">Trực</span>`);
            }
            if (!sched && !duty && !day.isPast) {
                chips.push(/* HTML */ `<button type="button" class="wk-none is-add" data-assign-doctor="${doctor.id}" data-date="${day.iso}" data-shift="${shift}" title="Xếp ca · ${escape(day.dayName)} ${day.shortDate}">+</button>`);
            }
            if (chips.length === 0) return '<span class="wk-none is-past">—</span>';
            return `<div class="wk-stack">${chips.join('')}</div>`;
        };

        const dayClass = (day) => `${day.isToday ? 'is-today' : ''} ${day.isWeekend ? 'is-weekend' : ''}`;

        container.innerHTML = /* HTML */ `
            ${doctors.length === 0
                ? '<p class="empty">Không có bác sĩ khớp bộ lọc.</p>'
                : /* HTML */ `
                <div class="wk-table-shell">
                    <table class="wk-table wk-table-rows">
                        <thead>
                            <tr>
                                <th class="wk-corner">Bác sĩ</th>
                                <th class="wk-shift-col">Ca</th>
                                ${days.map((day) => /* HTML */ `
                                    <th class="wk-day ${dayClass(day)}">
                                        <span class="wk-day-name">${day.dayName}</span>
                                        <span class="wk-day-date">${day.shortDate}</span>
                                        ${day.isToday ? '<span class="wk-today-badge">Hôm nay</span>' : ''}
                                    </th>
                                `).join('')}
                            </tr>
                        </thead>
                        <tbody>
                            ${pageRows.map((doctor) => /* HTML */ `
                                <tr class="wk-row-top">
                                    <td class="wk-person" rowspan="2">
                                        <span class="wk-person-box">
                                            <span class="wk-ava">${escape(schedInitials(doctor.fullName, 'BS'))}</span>
                                            <span class="wk-person-main">
                                                <strong title="${escape(doctor.fullName)}">${escape(doctor.fullName)}</strong>
                                                <small title="${escape(doctor.specialty || '')}">${escape(doctor.specialty || 'Bác sĩ')}</small>
                                            </span>
                                        </span>
                                    </td>
                                    <td class="wk-shift-label is-morning">Sáng</td>
                                    ${days.map((day) => /* HTML */ `<td class="wk-cell ${dayClass(day)}">${cell(doctor, day, 0)}</td>`).join('')}
                                </tr>
                                <tr class="wk-row-bottom">
                                    <td class="wk-shift-label is-afternoon">Chiều</td>
                                    ${days.map((day) => /* HTML */ `<td class="wk-cell ${dayClass(day)}">${cell(doctor, day, 1)}</td>`).join('')}
                                </tr>
                            `).join('')}
                        </tbody>
                    </table>
                </div>
                <div class="wk-pager">
                    <span class="wk-pager-info">Hiển thị ${start + 1}–${start + pageRows.length} trong ${doctors.length} bác sĩ</span>
                    <div class="wk-pager-btns" role="group" aria-label="Chuyển trang lịch bác sĩ">
                        <button type="button" data-roster-page="-1" ${rosterPage <= 1 ? 'disabled' : ''} aria-label="Trang trước">‹</button>
                        <span class="wk-pager-current">${rosterPage} / ${pages}</span>
                        <button type="button" data-roster-page="1" ${rosterPage >= pages ? 'disabled' : ''} aria-label="Trang sau">›</button>
                    </div>
                </div>
            `}
        `;
    }

    function schedShiftLabel(shift) {
        return shift === 1 ? 'Ca chiều · 13:00 – 16:30' : 'Ca sáng · 07:30 – 11:30';
    }

    function assignDates() {
        const start = find('#assign-date')?.value;
        if (!start) return [];
        const weeks = Number(find('#assign-repeat')?.value || 1);
        const base = new Date(start + 'T00:00:00');
        const list = [];
        for (let i = 0; i < Math.max(1, Math.min(4, weeks)); i += 1) {
            const d = new Date(base);
            d.setDate(base.getDate() + i * 7);
            const mm = String(d.getMonth() + 1).padStart(2, '0');
            const dd = String(d.getDate()).padStart(2, '0');
            list.push(`${d.getFullYear()}-${mm}-${dd}`);
        }
        return list;
    }

    function assignExisting(doctorId, dateIso, shift) {
        return (schedulingState?.schedules || [])
            .filter(schedIsActive)
            .find((s) => s.doctorId === doctorId && s.workDate === dateIso && schedShiftOf(s) === shift);
    }

    function assignError(message) {
        const box = find('#assign-error');
        if (!box) return;
        box.textContent = message || '';
        box.hidden = !message;
    }

    function renderAssign() {
        const dateInput = find('#assign-date');
        if (!dateInput) return;
        if (!dateInput.value) dateInput.value = schedulingState?.today || '';

        const roomSelect = find('#assign-room');
        if (roomSelect) {
            const rooms = schedulingState?.rooms || [];
            const current = roomSelect.value;
            roomSelect.innerHTML = rooms.length
                ? rooms.map((r) => /* HTML */ `<option value="${r.id}">${escape(r.roomNumber)} · ${escape(r.roomName)}</option>`).join('')
                : '<option value="">Khoa chưa có phòng khám</option>';
            if (current && rooms.some((r) => String(r.id) === current)) roomSelect.value = current;
        }

        renderAssignDoctors();
        renderAssignSummary();
    }

    function renderAssignDoctors() {
        const host = find('#assign-doctor-list');
        if (!host) return;
        const shift = Number(find('#assign-shift')?.value || 0);
        const dates = assignDates();
        const query = schedNorm(assignSearch.trim());
        const doctors = (schedulingState?.doctors || []).filter(
            (d) => !query || schedNorm(`${d.fullName} ${d.specialty || ''}`).includes(query),
        );

        host.innerHTML = doctors.length
            ? doctors.map((d) => {
                  const busy = dates.some((dt) => assignExisting(d.id, dt, shift));
                  const checked = assignDoctors.has(d.id);
                  return /* HTML */ `
                      <label class="assign-doc ${busy ? 'is-busy' : ''} ${checked ? 'is-checked' : ''}">
                          <input type="checkbox" data-assign-doc="${d.id}" ${checked ? 'checked' : ''} ${busy ? 'disabled' : ''} />
                          <span class="wk-ava">${escape(schedInitials(d.fullName, 'BS'))}</span>
                          <span class="wk-person-main">
                              <strong>${escape(d.fullName)}</strong>
                              <small>${escape(d.specialty || 'Bác sĩ')}</small>
                          </span>
                          <span class="assign-doc-tag">${busy ? 'Đã có ca' : (checked ? 'Đã chọn' : 'Trống lịch')}</span>
                      </label>
                  `;
              }).join('')
            : '<p class="empty">Không có bác sĩ khớp tìm kiếm.</p>';

        const count = find('#assign-count');
        if (count) count.textContent = `Đã chọn ${assignDoctors.size} bác sĩ`;
    }

    function renderAssignSummary() {
        const host = find('#assign-summary');
        if (!host) return;
        const dateIso = find('#assign-date')?.value;
        const shift = Number(find('#assign-shift')?.value || 0);
        const weeks = Number(find('#assign-repeat')?.value || 1);
        const room = (schedulingState?.rooms || []).find((r) => String(r.id) === find('#assign-room')?.value);
        const doctors = (schedulingState?.doctors || []).filter((d) => assignDoctors.has(d.id));
        const day = schedWeekDays().find((d) => d.iso === dateIso);
        const rows = [
            ['Ngày làm việc', dateIso ? `${day ? `${day.dayName}, ` : ''}${date(dateIso)}` : 'Chưa chọn'],
            ['Ca làm việc', schedShiftLabel(shift)],
            ['Phòng khám', room ? `${room.roomNumber} · ${room.roomName}` : 'Chưa chọn'],
            ['Lặp lại', weeks > 1 ? `${weeks} tuần liên tiếp` : 'Không lặp lại'],
        ].map(([label, value]) => /* HTML */ `<div class="assign-sum-row"><span>${label}</span><strong>${escape(value)}</strong></div>`).join('');

        host.innerHTML = /* HTML */ `
            ${rows}
            <div class="assign-sum-doctors">
                <span>Bác sĩ (${doctors.length})</span>
                ${doctors.length
                    ? /* HTML */ `<ul>${doctors.map((d) => /* HTML */ `<li><svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5"><polyline points="20 6 9 17 4 12"></polyline></svg>${escape(d.fullName)}</li>`).join('')}</ul>`
                    : '<p>Chưa chọn bác sĩ nào.</p>'}
            </div>
        `;

        const save = find('#assign-save');
        if (save) save.disabled = busy || !dateIso || doctors.length === 0;
    }

    function openAssignFor({ doctorId = 0, workDate = '', shift = 0 } = {}) {
        assignError('');
        applySchedView('assign');
        const dateInput = find('#assign-date');
        if (dateInput && workDate) dateInput.value = workDate;
        const shiftSelect = find('#assign-shift');
        if (shiftSelect) shiftSelect.value = String(shift === 1 ? 1 : 0);
        if (doctorId) assignDoctors.add(doctorId);
        renderAssign();
    }

    async function saveAssign() {
        if (busy) return;
        const dateIso = find('#assign-date')?.value;
        const shift = Number(find('#assign-shift')?.value || 0);
        const roomId = Number(find('#assign-room')?.value || 0);
        const departmentId = Number(find('#scheduling-dept')?.value || 0);
        const note = (find('#assign-note')?.value || '').trim() || null;
        const doctors = (schedulingState?.doctors || []).filter((d) => assignDoctors.has(d.id));

        if (!dateIso) return assignError('Chọn ngày làm việc trước khi lưu.');
        if (!departmentId) return assignError('Chọn khoa chuyên môn trước khi lưu.');
        if (!roomId) return assignError('Khoa chưa có phòng khám để gán.');
        if (doctors.length === 0) return assignError('Chọn ít nhất một bác sĩ.');
        assignError('');

        const tasks = [];
        let skipped = 0;
        assignDates().forEach((workDate) => {
            doctors.forEach((d) => {
                if (assignExisting(d.id, workDate, shift)) {
                    skipped += 1;
                    return;
                }
                tasks.push({ workDate, doctorId: d.id });
            });
        });
        if (tasks.length === 0) return assignError('Các bác sĩ đã chọn đều đã có ca trong khung giờ này.');

        busy = true;
        const saveBtn = find('#assign-save');
        if (saveBtn) {
            saveBtn.disabled = true;
            saveBtn.textContent = 'Đang lưu…';
        }
        let created = 0;
        try {
            for (const task of tasks) {
                await request('UpsertSchedule', {
                    id: null,
                    doctorId: task.doctorId,
                    departmentId,
                    roomId,
                    workDate: task.workDate,
                    shift,
                    doctorNote: note,
                });
                created += 1;
            }
            assignDoctors.clear();
            const noteBox = find('#assign-note');
            if (noteBox) noteBox.value = '';
            await loadScheduling(departmentId);
            applySchedView('roster');
            notify(`Đã phân ${created} ca cho ${doctors.length} bác sĩ${skipped ? ` · bỏ qua ${skipped} ca trùng` : ''}.`);
        } catch (error) {
            await loadScheduling(departmentId).catch(() => {});
            assignError(created ? `Đã lưu ${created}/${tasks.length} ca. ${error.message}` : error.message);
        } finally {
            busy = false;
            if (saveBtn) saveBtn.textContent = 'Lưu phân lịch';
            renderAssignSummary();
        }
    }

    function reqDoctor(doctorId) {
        return (schedulingState?.doctors || []).find((d) => d.id === doctorId) || null;
    }

    function reqDayLabel(workDate) {
        const day = schedWeekDays().find((d) => d.iso === workDate);
        return day ? `${day.dayName}, ${day.shortDate}` : date(workDate);
    }

    function renderRequests() {
        const listHost = find('#req-list');
        if (!listHost) return;

        const schedules = (schedulingState?.schedules || []).filter(schedIsActive);
        const pending = schedules.filter(schedIsPending);
        const approved = schedules.filter(schedIsApproved);

        const countPending = find('#req-count-pending');
        const countApproved = find('#req-count-approved');
        if (countPending) countPending.textContent = String(pending.length);
        if (countApproved) countApproved.textContent = String(approved.length);
        const badge = find('#sched-request-badge');
        if (badge) {
            badge.textContent = String(pending.length);
            badge.hidden = pending.length === 0;
        }
        const weekLabel = find('#req-week-label');
        if (weekLabel) weekLabel.textContent = schedWeekText();

        document.querySelectorAll('[data-req-status]').forEach((btn) => {
            const active = btn.dataset.reqStatus === reqStatus;
            btn.classList.toggle('is-active', active);
            btn.setAttribute('aria-selected', active ? 'true' : 'false');
        });

        const base = reqStatus === 'approved' ? approved : pending;
        const query = schedNorm(reqSearch.trim());
        const rows = base
            .filter((s) => !query || schedNorm(`${(reqDoctor(s.doctorId)?.fullName || '')} ${(reqDoctor(s.doctorId)?.specialty || '')}`).includes(query))
            .sort((a, b) => String(a.workDate).localeCompare(String(b.workDate)) || schedShiftOf(a) - schedShiftOf(b));

        if (reqSelectedId && !rows.some((s) => s.id === reqSelectedId)) reqSelectedId = rows[0]?.id || 0;
        if (!reqSelectedId && rows.length) reqSelectedId = rows[0].id;

        const pages = Math.max(1, Math.ceil(rows.length / REQ_PAGE_SIZE));
        reqPage = Math.min(Math.max(1, reqPage), pages);
        const start = (reqPage - 1) * REQ_PAGE_SIZE;
        const pageRows = rows.slice(start, start + REQ_PAGE_SIZE);

        listHost.innerHTML = pageRows.length
            ? pageRows.map((s) => {
                  const isPending = schedIsPending(s);
                  const doctor = reqDoctor(s.doctorId);
                  return /* HTML */ `
                      <button type="button" class="req-row ${s.id === reqSelectedId ? 'is-selected' : ''}" data-req-open="${s.id}">
                          <span class="wk-ava">${escape(schedInitials(doctor?.fullName, 'BS'))}</span>
                          <span class="req-row-main">
                              <strong>${escape(doctor?.fullName || 'Bác sĩ')}</strong>
                              <small>${reqDayLabel(s.workDate)} · ${schedShiftOf(s) === 0 ? 'Ca sáng' : 'Ca chiều'}</small>
                          </span>
                          <span class="req-row-side">
                              <span class="req-chip ${isPending ? 'is-wait' : 'is-ok'}">${isPending ? 'Chờ duyệt' : 'Đã duyệt'}</span>
                              <small>${s.roomNumber ? `P.${escape(s.roomNumber)}` : 'Chưa gán phòng'}</small>
                          </span>
                      </button>
                  `;
              }).join('')
            : /* HTML */ `<div class="req-empty">
                  <strong>${reqStatus === 'approved' ? 'Chưa có ca nào được duyệt trong tuần này' : 'Không có yêu cầu chờ duyệt'}</strong>
                  <p>${reqStatus === 'approved' ? 'Các ca đã duyệt sẽ xuất hiện tại đây sau khi bạn xử lý.' : 'Mọi ca bác sĩ đăng ký trong tuần đã được xử lý.'}</p>
              </div>`;

        const foot = find('#req-foot');
        if (foot) {
            foot.innerHTML = rows.length
                ? /* HTML */ `
                    <span class="wk-pager-info">Hiển thị ${start + 1}–${start + pageRows.length} trong ${rows.length} yêu cầu</span>
                    <div class="wk-pager-btns" role="group" aria-label="Chuyển trang yêu cầu">
                        <button type="button" data-req-page="-1" ${reqPage <= 1 ? 'disabled' : ''} aria-label="Trang trước">‹</button>
                        <span class="wk-pager-current">${reqPage} / ${pages}</span>
                        <button type="button" data-req-page="1" ${reqPage >= pages ? 'disabled' : ''} aria-label="Trang sau">›</button>
                    </div>
                `
                : '';
        }

        renderRequestDetail();
    }

    function renderRequestDetail() {
        const host = find('#req-detail');
        if (!host) return;
        const sched = (schedulingState?.schedules || []).find((s) => s.id === reqSelectedId && schedIsActive(s));
        if (!sched) {
            host.innerHTML = /* HTML */ `
                <div class="req-detail-empty">
                    <svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8"><path d="M9 11l3 3L22 4"></path><path d="M21 12v7a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h11"></path></svg>
                    <strong>Chọn một yêu cầu</strong>
                    <p>Bấm vào danh sách bên trái để xem chi tiết và duyệt ca.</p>
                </div>
            `;
            return;
        }

        const isPending = schedIsPending(sched);
        const shift = schedShiftOf(sched);
        const start = schedHhmm(sched.startTime) || (shift === 0 ? '07:30' : '13:00');
        const end = schedHhmm(sched.endTime) || (shift === 0 ? '11:30' : '16:30');
        const doctor = reqDoctor(sched.doctorId);

        let roomField;
        if (isPending) {
            const deptId = Number(find('#scheduling-dept')?.value || 0);
            const key = `${deptId}-${sched.workDate}-${shift}`;
            let rooms = reqRooms.get(key);
            if (rooms === undefined && deptId > 0) {
                reqRooms.set(key, null);
                request(`DecisionMatrix?departmentId=${deptId}&workDate=${encodeURIComponent(sched.workDate)}&shift=${shift}`)
                    .then((matrix) => {
                        reqRooms.set(key, matrix.availableRooms || []);
                        renderRequestDetail();
                    })
                    .catch(() => {
                        reqRooms.set(key, []);
                        renderRequestDetail();
                    });
                rooms = null;
            }
            if (rooms === undefined || rooms === null) {
                roomField = '<span class="req-loading">Đang tải phòng trống…</span>';
            } else if (rooms.length === 0) {
                roomField = '<span class="req-warn">Hết phòng khả dụng</span>';
            } else {
                roomField = /* HTML */ `<select id="req-room" aria-label="Chọn phòng khám">${rooms
                    .map((r) => /* HTML */ `<option value="${r.id}">${escape(r.roomNumber)} · ${escape(r.roomName)}</option>`)
                    .join('')}</select>`;
            }
        } else {
            roomField = sched.roomNumber
                ? /* HTML */ `<strong>P.${escape(sched.roomNumber)}</strong>`
                : '<span class="req-warn">Chưa gán phòng</span>';
        }

        host.innerHTML = /* HTML */ `
            <header class="req-detail-head">
                <div>
                    <p class="eyebrow">${isPending ? 'YÊU CẦU CHỜ DUYỆT' : 'CA ĐÃ DUYỆT'}</p>
                    <h3>${escape(doctor?.fullName || 'Bác sĩ')}</h3>
                    <small>${escape(doctor?.specialty || 'Bác sĩ')}</small>
                </div>
                <span class="req-chip ${isPending ? 'is-wait' : 'is-ok'}">${isPending ? 'Chờ duyệt' : 'Đã duyệt'}</span>
            </header>
            <div class="req-detail-body">
                <div class="req-detail-row"><span>Ngày làm việc</span><strong>${reqDayLabel(sched.workDate)}</strong></div>
                <div class="req-detail-row"><span>Ca khám</span><strong>${start} – ${end}</strong></div>
                <div class="req-detail-row"><span>Phòng khám</span><div class="req-room-field">${roomField}</div></div>
                ${sched.doctorNote ? /* HTML */ `<blockquote class="req-note">“${escape(sched.doctorNote)}”</blockquote>` : ''}
                ${!isPending && sched.reviewedByName
                    ? /* HTML */ `<p class="req-reviewer">Duyệt bởi ${escape(sched.reviewedByName)}${sched.reviewedAt ? ` · ${new Date(sched.reviewedAt).toLocaleString('vi-VN')}` : ''}</p>`
                    : ''}
            </div>
            <footer class="req-actions">
                ${isPending
                    ? /* HTML */ `
                        <button type="button" class="btn button secondary" data-req-reject="${sched.id}">Từ chối</button>
                        <button type="button" class="btn button primary" data-req-approve="${sched.id}">Duyệt &amp; gán phòng</button>
                    `
                    : /* HTML */ `<button type="button" class="btn button secondary" data-edit-schedule="${sched.id}">Chỉnh sửa ca</button>`}
            </footer>
        `;
    }

    function openRequest(scheduleId) {
        const sched = (schedulingState?.schedules || []).find((s) => s.id === scheduleId);
        reqStatus = sched && schedIsApproved(sched) ? 'approved' : 'pending';
        reqSelectedId = scheduleId;
        reqPage = 1;
        applySchedView('requests');
    }

    async function runSchedAction(path, payload, successMessage, nextStatus = '') {
        if (busy) return;
        busy = true;
        try {
            const result = await request(path, payload);
            if (nextStatus) {
                reqStatus = nextStatus;
                reqPage = 1;
            }
            notify(result.message || successMessage);
            await loadScheduling(Number(find('#scheduling-dept')?.value || 0) || null);
        } catch (error) {
            notify(error.message, true);
        } finally {
            busy = false;
        }
    }

    find('#scheduling-dept')?.addEventListener('change', (e) => {
        e.currentTarget.dataset.userPicked = '1';
        loadScheduling(Number(e.target.value)).catch((err) => notify(err.message, true));
    });

    find('#sched-kpis')?.addEventListener('click', (e) => {
        if (!e.target.closest('[data-sched-kpi="pending"]')) return;
        reqStatus = 'pending';
        reqPage = 1;
        reqSelectedId = 0;
        applySchedView('requests');
    });

    find('#dept-progress')?.addEventListener('click', async (e) => {
        const btn = e.target.closest('[data-sched-dept]');
        if (!btn) return;
        const deptId = Number(btn.dataset.schedDept || 0);
        const rawView = btn.dataset.schedDeptView;
        const view = rawView === 'requests' ? 'requests' : (rawView === 'assign' ? 'assign' : 'roster');
        const deptSelect = find('#scheduling-dept');
        if (deptId > 0 && deptSelect) {
            deptSelect.dataset.userPicked = '1';
            deptSelect.value = String(deptId);
        }
        try {
            await loadScheduling(deptId || null);
            if (view === 'requests') {
                reqStatus = 'pending';
                reqPage = 1;
                reqSelectedId = 0;
            }
            applySchedView(view);
        } catch (error) {
            notify(error.message, true);
        }
    });

    find('#open-create-period')?.addEventListener('click', () => {
        const dialog = find('#create-period-dialog');
        const form = find('#create-period-form');
        form.reset();
        form.querySelector('.form-error').textContent = '';
        const deptId = Number(find('#scheduling-dept')?.value || 0) || schedulingState?.selectedDeptId;
        if (deptId && form.elements.departmentId) {
            form.elements.departmentId.value = String(deptId);
        }
        const now = new Date();
        const day = now.getDay();
        const diffToMon = (day === 0 ? -6 : 1) - day;
        const thisMon = new Date(now.getFullYear(), now.getMonth(), now.getDate() + diffToMon);
        const nextSun = new Date(thisMon.getFullYear(), thisMon.getMonth(), thisMon.getDate() + 13);
        const toIso = (d) => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
        if (form.elements.startDate) {
            form.elements.startDate.min = toIso(thisMon);
            form.elements.startDate.max = toIso(nextSun);
        }
        if (form.elements.endDate) {
            form.elements.endDate.min = toIso(thisMon);
            form.elements.endDate.max = toIso(nextSun);
        }
        dialog.showModal();
    });

    find('#create-period-form')?.addEventListener('submit', (e) => {
        e.preventDefault();
        const data = Object.fromEntries(new FormData(e.currentTarget));
        data.departmentId = data.departmentId ? Number(data.departmentId) : null;
        if (!data.departmentId) return notify('Vui lòng chọn khoa chuyên môn.', true);
        mutate('SchedulingPeriods', data, e.currentTarget);
    });

    find('#publish-period-btn')?.addEventListener('click', async (e) => {
        const periodId = Number(e.currentTarget.dataset.periodId || 0);
        if (!periodId) return;
        const departmentId = schedulingState?.selectedDeptId || Number(find('#scheduling-dept')?.value || 0) || null;
        if (await askConfirm('Xác nhận xuất bản kỳ lịch và sinh slot khám trực tuyến cho tất cả ca đã duyệt?', 'Xuất bản kỳ lịch', 'Xuất bản')) {
            mutate('PublishPeriod', { periodId, departmentId });
        }
    });

    function openUpsertScheduleModal(preset = null) {
        const dialog = find('#upsert-schedule-dialog');
        const form = find('#upsert-schedule-form');
        form.reset();
        form.querySelector('.form-error').textContent = '';
        const docs = schedulingState?.doctors || [];
        const rooms = schedulingState?.rooms || [];
        form.elements.doctorId.innerHTML = docs.length > 0
            ? docs.map((d) => /* HTML */ `<option value="${d.id}">${escape(d.fullName)}</option>`).join('')
            : '<option value="">Không có bác sĩ nào</option>';
        form.elements.roomId.innerHTML = rooms.length > 0
            ? rooms.map((r) => /* HTML */ `<option value="${r.id}">${escape(r.roomNumber)} - ${escape(r.roomName)}</option>`).join('')
            : '<option value="">Không có phòng khám nào</option>';

        form.elements.id.value = preset?.id || '';
        if (preset?.doctorId) form.elements.doctorId.value = String(preset.doctorId);
        if (preset?.roomId) form.elements.roomId.value = String(preset.roomId);
        if (preset?.workDate) form.elements.workDate.value = preset.workDate;
        if (preset?.shift !== undefined && preset?.shift !== null) form.elements.shift.value = String(preset.shift);
        if (preset?.doctorNote) form.elements.doctorNote.value = preset.doctorNote;
        find('#upsert-schedule-title').textContent = 'Chỉnh sửa ca khám';
        dialog.showModal();
    }

    find('#open-upsert-schedule')?.addEventListener('click', () => {
        assignDoctors.clear();
        applySchedView('assign');
    });

    document.querySelectorAll('[data-sched-view]').forEach((btn) => {
        btn.addEventListener('click', () => applySchedView(btn.dataset.schedView));
    });

    find('#sched-board-tools')?.addEventListener('input', (e) => {
        if (e.target.id !== 'roster-search') return;
        rosterSearch = e.target.value;
        rosterPage = 1;
        renderWeeklyScheduleGrid();
    });

    find('#dept-schedules-grid')?.addEventListener('click', (e) => {
        const pager = e.target.closest('[data-roster-page]');
        if (!pager) return;
        rosterPage += Number(pager.dataset.rosterPage);
        renderWeeklyScheduleGrid();
    });

    ['#assign-date', '#assign-shift', '#assign-room', '#assign-repeat'].forEach((selector) => {
        find(selector)?.addEventListener('change', () => {
            assignError('');
            renderAssignDoctors();
            renderAssignSummary();
        });
    });

    find('#assign-search')?.addEventListener('input', (e) => {
        assignSearch = e.target.value;
        renderAssignDoctors();
        const input = find('#assign-search');
        if (input) {
            input.focus();
            input.setSelectionRange(input.value.length, input.value.length);
        }
    });

    find('#assign-doctor-list')?.addEventListener('change', (e) => {
        const input = e.target.closest('[data-assign-doc]');
        if (!input) return;
        const id = Number(input.dataset.assignDoc);
        if (input.checked) assignDoctors.add(id);
        else assignDoctors.delete(id);
        renderAssignDoctors();
        renderAssignSummary();
    });

    find('#assign-back')?.addEventListener('click', () => applySchedView('roster'));
    find('#assign-cancel')?.addEventListener('click', () => {
        assignDoctors.clear();
        assignError('');
        applySchedView('roster');
    });
    find('#assign-save')?.addEventListener('click', () => saveAssign());

    document.querySelectorAll('[data-req-status]').forEach((btn) => {
        btn.addEventListener('click', () => {
            reqStatus = btn.dataset.reqStatus === 'approved' ? 'approved' : 'pending';
            reqPage = 1;
            reqSelectedId = 0;
            renderRequests();
        });
    });

    find('#req-search')?.addEventListener('input', (e) => {
        reqSearch = e.target.value;
        reqPage = 1;
        renderRequests();
        const input = find('#req-search');
        if (input) {
            input.focus();
            input.setSelectionRange(input.value.length, input.value.length);
        }
    });

    find('#req-list')?.addEventListener('click', (e) => {
        const open = e.target.closest('[data-req-open]');
        if (!open) return;
        reqSelectedId = Number(open.dataset.reqOpen);
        renderRequests();
    });

    find('#req-foot')?.addEventListener('click', (e) => {
        const pager = e.target.closest('[data-req-page]');
        if (!pager) return;
        reqPage += Number(pager.dataset.reqPage);
        renderRequests();
    });

    find('#req-back')?.addEventListener('click', () => applySchedView('roster'));

    find('#req-detail')?.addEventListener('click', async (e) => {
        const approveBtn = e.target.closest('[data-req-approve]');
        if (approveBtn) {
            const scheduleId = Number(approveBtn.dataset.reqApprove);
            const roomId = Number(find('#req-room')?.value || 0);
            if (!roomId) return notify('Chọn phòng khám trước khi duyệt.', true);
            reqSelectedId = scheduleId;
            const departmentId = schedulingState?.selectedDeptId || Number(find('#scheduling-dept')?.value || 0) || null;
            runSchedAction('ApproveShift', { scheduleId, roomId, departmentId }, 'Đã duyệt ca khám.', 'approved');
            return;
        }
        const rejectBtn = e.target.closest('[data-req-reject]');
        if (rejectBtn) {
            if (!await askConfirm('Từ chối yêu cầu này? Ca sẽ bị hủy khỏi lịch của bác sĩ.', 'Từ chối yêu cầu', 'Từ chối', true)) return;
            const scheduleId = Number(rejectBtn.dataset.reqReject);
            const departmentId = Number(find('#scheduling-dept')?.value || 0);
            reqSelectedId = 0;
            runSchedAction('DeleteSchedule', { scheduleId, departmentId }, 'Đã từ chối ca khám.');
        }
    });

    find('#sched-prev-week')?.addEventListener('click', () => {
        schedulingWeekOffset -= 1;
        loadScheduling(Number(find('#scheduling-dept')?.value || 0) || null).catch((err) => notify(err.message, true));
    });
    find('#sched-this-week')?.addEventListener('click', () => {
        schedulingWeekOffset = 0;
        loadScheduling(Number(find('#scheduling-dept')?.value || 0) || null).catch((err) => notify(err.message, true));
    });
    find('#sched-next-week')?.addEventListener('click', () => {
        schedulingWeekOffset += 1;
        loadScheduling(Number(find('#scheduling-dept')?.value || 0) || null).catch((err) => notify(err.message, true));
    });
    find('#sched-generate-slots')?.addEventListener('click', () => {
        if (!schedulingState?.weekStart || !schedulingState?.weekEnd) return;
        const departmentId = Number(find('#scheduling-dept')?.value || 0) || null;
        mutate('GenerateSlots', {
            startDate: schedulingState.weekStart,
            endDate: schedulingState.weekEnd,
            departmentId,
        });
    });

    find('#upsert-schedule-form')?.addEventListener('submit', (e) => {
        e.preventDefault();
        const data = Object.fromEntries(new FormData(e.currentTarget));
        data.id = data.id ? Number(data.id) : null;
        data.departmentId = Number(find('#scheduling-dept')?.value || 0);
        data.doctorId = Number(data.doctorId);
        data.roomId = data.roomId ? Number(data.roomId) : null;
        data.shift = Number(data.shift);
        mutate('UpsertSchedule', data, e.currentTarget);
    });

    document.addEventListener('click', async (e) => {
        const delSchedBtn = e.target.closest('[data-delete-schedule]');
        if (delSchedBtn) {
            e.stopPropagation();
            if (await askConfirm('Xác nhận hủy ca khám này?', 'Hủy ca khám', 'Hủy ca', true)) {
                const departmentId = Number(find('#scheduling-dept')?.value || 0);
                mutate('DeleteSchedule', { scheduleId: Number(delSchedBtn.dataset.deleteSchedule), departmentId });
            }
            return;
        }
        const assignBtn = e.target.closest('[data-assign-doctor]');
        if (assignBtn) {
            openAssignFor({
                doctorId: Number(assignBtn.dataset.assignDoctor),
                workDate: assignBtn.dataset.date,
                shift: Number(assignBtn.dataset.shift || 0),
            });
            return;
        }
        const editSchedCard = e.target.closest('[data-edit-schedule]');
        if (editSchedCard) {
            const sched = (schedulingState?.schedules || []).find((s) => s.id === Number(editSchedCard.dataset.editSchedule));
            if (sched) {
                openUpsertScheduleModal({
                    id: sched.id,
                    doctorId: sched.doctorId,
                    roomId: sched.roomId,
                    workDate: sched.workDate,
                    shift: sched.shift === 1 || sched.shift === 'Afternoon' ? 1 : 0,
                    doctorNote: sched.doctorNote || '',
                });
            }
            return;
        }
        const pendingPill = e.target.closest('[data-req-open-schedule]');
        if (pendingPill) {
            openRequest(Number(pendingPill.dataset.reqOpenSchedule));
        }
    });

    async function refresh() {
        await Promise.all([
            loadDirectory(),
            loadRequests(),
            loadReport(!!find('#report-filter').elements.from.value),
            loadCatalog(),
            loadAudit(),
        ]);
        await loadScheduling();
    }

    selectSection();
    refresh().catch((error) => notify(error.message, true));
})();

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
            'Tổng quan & doanh thu',
            'Theo dõi số lượt tiếp nhận và dòng tiền theo thời gian.',
        ],
        requests: ['Nghỉ / đổi ca', 'Duyệt đề nghị và sắp xếp lại lịch khám trong khoa.'],
        scheduling: [
            'Phân & duyệt ca khám',
            'Tạo kỳ đăng ký, duyệt ca theo ma trận ưu tiên, gán phòng và xuất bản lịch khám.',
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
    let selectedMatrixDate = null;
    let selectedMatrixShift = 0;
    let schedulingWeekOffset = 0;
    let busy = false;
    let reportVersion = 0;
    let auditVersion = 0;
    const REPORT_PAGE_SIZE = 5;
    let reportDays = [];
    let reportPage = 1;

    function notify(message, error = false) {
        const target = find('#admin-message');
        target.textContent = message;
        target.classList.toggle('error', error);
        target.hidden = false;
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

    function accountRows(rows, actions) {
        return rows.map((row) => [
            /* HTML */ `<strong>${escape(row.name)}</strong>`,
            escape(row.username),
            escape(row.roleName),
            escape(row.department || '—'),
            /* HTML */ `<span class="status ${row.isActive ? '' : 'inactive'}"
                >${row.isActive ? 'Đang hoạt động' : 'Ngừng hoạt động'}</span
            >`,
            ...(actions
                ? [
                      row.canChangeStatus
                          ? /* HTML */ `<button
                                type="button"
                                class="btn button sm small-button ${row.isActive ? 'danger' : 'secondary'}"
                                data-account="${row.id}"
                                data-active="${!row.isActive}"
                            >
                                ${row.isActive ? 'Ngừng hoạt động' : 'Kích hoạt'}
                            </button>`
                          : '<span>Đang đăng nhập</span>',
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
            ['Mã', 'Tên phòng ban', 'Mô tả'],
            filtered.map((row) => [
                escape(row.id),
                /* HTML */ `<strong>${escape(row.name)}</strong>`,
                escape(row.description || '—'),
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
        table('#accounts-table', [...headings, 'Thao tác'], accountRows(directory.accounts, true));
        table('#staff-table', headings, accountRows(directory.staff, false));
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
        const query = withFilter ? '?' + new URLSearchParams(new FormData(filter)) : '';
        const report = await request('Report' + query);
        if (version !== reportVersion) return;

        filter.elements.from.value = report.from;
        filter.elements.to.value = report.to;
        find('#report-period').textContent = `${date(report.from)} – ${date(report.to)}`;
        find('#report-stats').innerHTML = [
            ['Doanh thu thực thu', money(report.revenue) + ' ₫', 'Sau khi trừ hoàn tiền', true],
            ['Lượt đã tiếp nhận', money(report.visits), 'Theo thời điểm check-in', false],
            ['Tạm ứng', money(report.deposits) + ' ₫', 'Đã ghi nhận trong kỳ', false],
            ['Hoàn tiền', money(report.refunds) + ' ₫', 'Đã hoàn trong kỳ', false],
        ]
            .map(
                ([label, value, note, highlight]) =>
                    /* HTML */ `<article class="stat ${highlight ? 'highlight' : ''}">
                        <span>${label}</span><strong>${escape(value)}</strong><small>${note}</small>
                    </article>`,
            )
            .join('');

        const maximum = Math.max(1, ...report.days.map((day) => Math.abs(day.revenue)));
        find('#revenue-chart').innerHTML = report.days
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
        reportDays = report.days || [];
        renderReportPage(1);

        try {
            const deptStats = await request('DepartmentStats' + query);
            table(
                '#department-stats-table',
                ['Khoa chuyên môn', 'Lượt tiếp nhận', 'Doanh thu thực thu'],
                deptStats.map((row) => [
                    /* HTML */ `<strong>${escape(row.departmentName)}</strong>`,
                    /* HTML */ `<span class="number">${escape(money(row.visits))} lượt</span>`,
                    /* HTML */ `<span class="number">${escape(money(row.revenue))} ₫</span>`,
                ]),
            );
        } catch {
            table('#department-stats-table', ['Khoa chuyên môn', 'Lượt tiếp nhận', 'Doanh thu thực thu'], []);
            notify('Chưa tải được thống kê doanh thu theo khoa.', true);
        }
    }

    function renderReportPage(page) {
        const totalPages = Math.max(1, Math.ceil(reportDays.length / REPORT_PAGE_SIZE));
        reportPage = Math.min(Math.max(1, page), totalPages);
        const startIndex = (reportPage - 1) * REPORT_PAGE_SIZE;
        const pageDays = reportDays.slice(startIndex, startIndex + REPORT_PAGE_SIZE);

        table(
            '#report-table',
            ['Ngày', 'Tạm ứng', 'Thanh toán', 'Hoàn tiền', 'Thực thu', 'Lượt tiếp nhận'],
            pageDays.map((day) => [
                escape(date(day.date)),
                ...[day.deposits, day.payments, day.refunds, day.revenue, day.visits].map(
                    (value) => /* HTML */ `<span class="number">${escape(money(value))}</span>`,
                ),
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
        pager.innerHTML = /* HTML */ `
            <span style="font-size:13px; color:#64748b;">
                Hiển thị ${startIndex + 1}–${Math.min(startIndex + REPORT_PAGE_SIZE, reportDays.length)} / ${reportDays.length} ngày (tối đa ${REPORT_PAGE_SIZE} ngày/trang)
            </span>
            <div style="display:flex; align-items:center; gap:8px;">
                <button type="button" class="btn button secondary sm" id="report-prev-page" ${reportPage <= 1 ? 'disabled' : ''}>← Trước</button>
                <span style="font-size:13px; font-weight:600;">Trang ${reportPage} / ${totalPages}</span>
                <button type="button" class="btn button secondary sm" id="report-next-page" ${reportPage >= totalPages ? 'disabled' : ''}>Sau →</button>
            </div>
        `;
        find('#report-prev-page')?.addEventListener('click', () => renderReportPage(reportPage - 1));
        find('#report-next-page')?.addEventListener('click', () => renderReportPage(reportPage + 1));
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
                row.activeSchedulesCount === 0
                    ? /* HTML */ `<button type="button" class="btn button sm small-button danger" data-delete-room="${row.id}">Xóa</button>`
                    : '<span>Đang sử dụng</span>',
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

    async function loadRequests() {
        requestRows = await request('Requests');
        renderRequestsTable();
    }

    function renderRequestsTable() {
        const statuses = { Pending: 'Chờ duyệt', Approved: 'Đã duyệt', Rejected: 'Từ chối' };
        const filtered = requestStatusFilter === 'all'
            ? requestRows
            : requestRows.filter((r) => r.status === requestStatusFilter);

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
            : 'Tạo tài khoản';
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
        mutate(
            'Departments',
            Object.fromEntries(new FormData(event.currentTarget)),
            event.currentTarget,
        );
    });

    find('#room-form').addEventListener('submit', (event) => {
        event.preventDefault();
        const data = Object.fromEntries(new FormData(event.currentTarget));
        data.departmentId = Number(data.departmentId);
        mutate('Rooms', data, event.currentTarget);
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
        const data = Object.fromEntries(new FormData(event.currentTarget));
        data.roleId = Number(data.roleId);
        data.departmentId = data.departmentId ? Number(data.departmentId) : null;
        data.dateOfBirth = data.dateOfBirth || null;
        mutate('Accounts', data, event.currentTarget);
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

    document.addEventListener('click', (event) => {
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
            if (open.dataset.open === 'room-dialog') {
                const currentFilter = find('#filter-room-department')?.value;
                if (currentFilter) {
                    dialog.querySelector('form').elements.departmentId.value = currentFilter;
                }
            }
            dialog.showModal();
        }
        if (event.target.closest('[data-close]')) event.target.closest('dialog').close();
        const account = event.target.closest('[data-account]');
        if (account && confirm('Xác nhận thay đổi trạng thái truy cập của tài khoản này?')) {
            mutate('AccountStatus', {
                accountId: Number(account.dataset.account),
                isActive: account.dataset.active === 'true',
            });
        }
        const deleteRoom = event.target.closest('[data-delete-room]');
        if (deleteRoom && confirm('Xác nhận xóa phòng khám này khỏi khoa?')) {
            mutate('Rooms/Delete', { roomId: Number(deleteRoom.dataset.deleteRoom) });
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
                scopeSelect.innerHTML =
                    '<option value="">Toàn bệnh viện (dùng chung)</option>' +
                    directory.departments
                        .map((d) => /* HTML */ `<option value="${d.id}">${escape(d.name)}</option>`)
                        .join('');
                if (prevScope) scopeSelect.value = prevScope;
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

        const periodInfo = find('#active-period-info');
        const publishBtn = find('#publish-period-btn');
        const period = schedulingState?.activePeriod;
        if (!period) {
            periodInfo.innerHTML = '<strong>Chưa có kỳ đăng ký nào.</strong> Bấm <em>+ Tạo kỳ đăng ký</em> để mở đợt đăng ký ca cho bác sĩ.';
            if (publishBtn) publishBtn.hidden = true;
        } else {
            const isPublished = period.status === 3 || period.status === 'Published';
            const statusText = isPublished ? 'Đã xuất bản' : (period.isOpenForBidding ? 'Đang mở đăng ký' : 'Đã đóng đăng ký');
            periodInfo.innerHTML = /* HTML */ `
                <div style="display:flex; justify-content:space-between; align-items:center; flex-wrap:wrap; gap:8px;">
                    <div>
                        <strong>${escape(period.name)}</strong> (${escape(period.departmentName || 'Toàn viện')}) ·
                        <span>${escape(date(period.startDate))} – ${escape(date(period.endDate))}</span>
                    </div>
                    <span class="badge">${escape(statusText)}</span>
                </div>
            `;
            if (publishBtn) {
                publishBtn.hidden = isPublished;
                publishBtn.dataset.periodId = period.id;
            }
        }

        const gridCells = schedulingState?.demandGrid?.cells || [];
        const groupedDates = [];
        const byDate = new Map();
        for (const cell of gridCells) {
            if (!byDate.has(cell.workDate)) {
                const entry = { workDate: cell.workDate, morning: null, afternoon: null };
                byDate.set(cell.workDate, entry);
                groupedDates.push(entry);
            }
            const entry = byDate.get(cell.workDate);
            if (cell.shift === 0 || cell.shift === 'Morning') entry.morning = cell;
            else entry.afternoon = cell;
        }

        const dayNames = ['CHỦ NHẬT', 'THỨ HAI', 'THỨ BA', 'THỨ TƯ', 'THỨ NĂM', 'THỨ SÁU', 'THỨ BẢY'];
        const renderShiftCard = (cell, shiftVal, label) => {
            if (!cell) return '';
            const totalRooms = cell.totalRooms ?? 0;
            const currentBidders = cell.currentBidders ?? 0;
            const approvedCount = cell.approvedCount ?? 0;
            const pendingCount = Math.max(0, currentBidders - approvedCount);
            const isSelected = selectedMatrixDate === cell.workDate && selectedMatrixShift === shiftVal;
            const borderStyle = isSelected ? '2px solid #0284c7' : (approvedCount > 0 || pendingCount > 0 ? '1px solid #bbf7d0' : '1px solid #e2e8f0');
            const bgStyle = approvedCount > 0 || pendingCount > 0 ? '#f0fdf4' : '#ffffff';
            const hotBadge = cell.isHotShift ? '<span style="background:#fef3c7; color:#c2410c; font-size:10px; font-weight:700; padding:2px 6px; border-radius:999px;">🔥 Hot</span>' : '';
            const statusChip = pendingCount > 0
                ? `<span style="background:#fef3c7; color:#92400e; font-size:11px; font-weight:600; padding:2px 6px; border-radius:4px;">⏳ ${pendingCount} chờ duyệt</span>`
                : (approvedCount > 0
                    ? `<span style="background:#dcfce7; color:#166534; font-size:11px; font-weight:600; padding:2px 6px; border-radius:4px;">✓ ${approvedCount} đã duyệt</span>`
                    : `<span style="color:#94a3b8; font-size:11px;">Chưa có bác sĩ</span>`);
            const biddersHtml = (cell.bidders || []).map((b) => {
                const approved = b.status === 1 || b.status === 'Approved';
                return `<div style="font-size:11.5px; padding:3px 6px; border-radius:4px; background:${approved ? '#dcfce7' : '#fef3c7'}; color:${approved ? '#166534' : '#92400e'}; border:1px solid ${approved ? '#86efac' : '#fde68a'};">
                    ${approved ? '✓' : '⏳'} ${escape(b.doctorName)}${b.roomNumber ? ` (${escape(b.roomNumber)})` : ''}
                </div>`;
            }).join('');
            const actionBtn = pendingCount > 0
                ? `<button type="button" class="btn button primary sm" style="background:#d97706; border-color:#d97706;" data-select-matrix="${escape(cell.workDate)}" data-shift="${shiftVal}">Duyệt ca này</button>`
                : (approvedCount > 0
                    ? `<button type="button" class="btn button secondary sm" data-select-matrix="${escape(cell.workDate)}" data-shift="${shiftVal}">✓ Xem phân buồng</button>`
                    : '');

            return /* HTML */ `
                <div style="border:${borderStyle}; background:${bgStyle}; border-radius:8px; padding:10px; display:flex; flex-direction:column; gap:6px;">
                    <div style="display:flex; justify-content:space-between; align-items:center; gap:4px;">
                        <strong style="font-size:12px;">${label}</strong>
                        ${hotBadge}
                    </div>
                    <div style="display:flex; justify-content:space-between; align-items:center; font-size:11.5px;">
                        <span><strong>${approvedCount}/${totalRooms}</strong> phòng</span>
                        ${statusChip}
                    </div>
                    <div style="display:flex; flex-direction:column; gap:4px;">${biddersHtml}</div>
                    <div style="display:flex; justify-content:space-between; align-items:center; flex-wrap:wrap; gap:6px; margin-top:2px;">
                        <button type="button" style="background:none; border:none; color:#0284c7; font-size:11.5px; font-weight:600; cursor:pointer; padding:0; white-space:nowrap;" data-select-matrix="${escape(cell.workDate)}" data-shift="${shiftVal}">Xem ma trận ↓</button>
                        ${actionBtn}
                    </div>
                </div>
            `;
        };

        const board = find('#demand-grid-board');
        if (board) {
            if (groupedDates.length === 0) {
                board.innerHTML = '<p class="empty" style="grid-column:1/-1;">Chưa có dữ liệu lưới ca cho kỳ này.</p>';
            } else {
                board.innerHTML = groupedDates.map((g) => {
                    const dt = new Date(g.workDate + 'T00:00:00');
                    const dayName = dayNames[dt.getDay()];
                    const shortDate = date(g.workDate).slice(0, 5);
                    return /* HTML */ `
                        <div style="display:flex; flex-direction:column; gap:8px;">
                            <div style="text-align:center; padding:6px; border-bottom:1px solid #e2e8f0;">
                                <div style="font-size:11px; color:#64748b; font-weight:700;">${dayName}</div>
                                <strong style="font-size:14px;">${shortDate}</strong>
                            </div>
                            ${renderShiftCard(g.morning, 0, '🌅 Sáng (07:30-11:30)')}
                            ${renderShiftCard(g.afternoon, 1, '🌆 Chiều (13:00-16:30)')}
                        </div>
                    `;
                }).join('');
            }
        }

        if (!selectedMatrixDate && gridCells.length > 0) {
            const firstPending = gridCells.find((c) => (c.currentBidders ?? 0) > (c.approvedCount ?? 0));
            const firstCell = firstPending || gridCells[0];
            selectedMatrixDate = firstCell.workDate;
            selectedMatrixShift = firstCell.shift === 1 || firstCell.shift === 'Afternoon' ? 1 : 0;
        }
        if (selectedMatrixDate) {
            await loadInlineDecisionMatrix(selectedMatrixDate, selectedMatrixShift);
        } else {
            table('#dm-table', ['#', 'Bác sĩ', '1. Trực đêm', '2. Luân phiên ca Hot', '3. Định mức tuần', '4. BN tái khám', 'Trạng thái', 'Duyệt / Điều phối'], []);
        }

        renderWeeklyScheduleGrid();
    }

    function renderWeeklyScheduleGrid() {
        const container = find('#dept-schedules-grid');
        if (!container) return;

        const doctors = schedulingState?.doctors || [];
        const staffList = schedulingState?.staff || [];
        const doctorAccountIds = new Set(doctors.map((d) => d.accountId));
        const nonDoctorStaff = staffList.filter((s) => !doctorAccountIds.has(s.id));
        const schedules = schedulingState?.schedules || [];
        const dutyShifts = schedulingState?.dutyShifts || [];
        const weekStartStr = schedulingState?.weekStart;
        const todayStr = schedulingState?.today;

        if (!weekStartStr) {
            container.innerHTML = '<p class="empty">Chưa có dữ liệu tuần làm việc.</p>';
            return;
        }

        const weekDays = [];
        const base = new Date(weekStartStr + 'T00:00:00');
        const dayLabels = ['Chủ Nhật', 'Thứ Hai', 'Thứ Ba', 'Thứ Tư', 'Thứ Năm', 'Thứ Sáu', 'Thứ Bảy'];
        for (let i = 0; i < 7; i++) {
            const d = new Date(base);
            d.setDate(base.getDate() + i);
            const yyyy = d.getFullYear();
            const mm = String(d.getMonth() + 1).padStart(2, '0');
            const dd = String(d.getDate()).padStart(2, '0');
            const iso = `${yyyy}-${mm}-${dd}`;
            weekDays.push({
                iso,
                dayName: dayLabels[d.getDay()],
                shortDate: `${dd}/${mm}`,
                isToday: iso === todayStr,
                isPast: todayStr ? iso < todayStr : false,
            });
        }

        if (doctors.length === 0 && nonDoctorStaff.length === 0) {
            container.innerHTML = '<p class="empty">Khoa này chưa có nhân sự nào để hiển thị lịch tuần.</p>';
            return;
        }

        const renderShiftBlock = (doc, day, shiftVal) => {
            const isMorning = shiftVal === 0;
            const sched = schedules.find(
                (s) => s.doctorId === doc.id && s.workDate === day.iso && (s.shift === shiftVal || s.shift === (isMorning ? 'Morning' : 'Afternoon')) && s.status !== 4 && s.status !== 'Cancelled' && s.status !== 2 && s.status !== 'Rejected' && s.status !== 3 && s.status !== 'Reallocated',
            );

            if (!sched) {
                if (day.isPast) {
                    return `<div style="background:#f1f5f9; color:#94a3b8; font-size:11px; text-align:center; padding:6px; border-radius:6px;">Đã qua</div>`;
                }
                const label = isMorning ? '+ Ca sáng (07:30)' : '+ Ca chiều (13:00)';
                return `<button type="button" data-add-schedule="${doc.id}" data-date="${day.iso}" data-shift="${shiftVal}" style="width:100%; background:#ffffff; border:1px dashed #cbd5e1; color:#64748b; font-size:11px; padding:6px; border-radius:6px; cursor:pointer;">${label}</button>`;
            }

            if (sched.status === 0 || sched.status === 'Bidding') {
                return /* HTML */ `
                    <div style="background:#fef3c7; border:1px solid #fde68a; border-radius:6px; padding:6px 8px; font-size:11px;">
                        <div style="display:flex; justify-content:space-between; align-items:center; gap:4px;">
                            <strong>${isMorning ? 'Ca sáng (07:30-11:30)' : 'Ca chiều (13:00-16:30)'}</strong>
                            <span style="background:#f59e0b; color:#fff; padding:1px 5px; border-radius:4px; font-size:10px; font-weight:700;">Chờ duyệt pick</span>
                        </div>
                        <div style="display:flex; justify-content:space-between; align-items:center; margin-top:4px; color:#92400e;">
                            <span>${escape(sched.doctorNote || 'Đăng ký ca')}</span>
                            <button type="button" data-select-matrix="${day.iso}" data-shift="${shiftVal}" style="background:none; border:none; color:#d97706; font-weight:700; cursor:pointer; padding:0;">Duyệt</button>
                        </div>
                    </div>
                `;
            }

            const bg = isMorning ? '#e0f2fe' : '#f3e8ff';
            const border = isMorning ? '#bae6fd' : '#e9d5ff';
            const accent = isMorning ? '#0369a1' : '#7e22ce';
            const walkIn = Math.max(0, (sched.maxPatients ?? 40) - (sched.onlineSlots ?? 0));
            return /* HTML */ `
                <div style="background:${bg}; border:1px solid ${border}; border-radius:6px; padding:6px 8px; font-size:11px; cursor:pointer;" data-edit-schedule="${sched.id}">
                    <div style="display:flex; justify-content:space-between; align-items:center; gap:4px; color:${accent};">
                        <strong>${isMorning ? 'Ca sáng' : 'Ca chiều'}</strong>
                        <span>${isMorning ? '07:30-11:30' : '13:00-16:30'}
                            <button type="button" data-delete-schedule="${sched.id}" title="Hủy ca" style="background:none; border:none; color:#dc2626; font-weight:700; cursor:pointer; margin-left:4px; padding:0;">×</button>
                        </span>
                    </div>
                    <div style="margin-top:3px; color:#1e293b;">
                        <span style="background:#fff; padding:1px 5px; border-radius:4px; font-weight:700; color:${accent};">${escape(sched.roomNumber || '—')}</span>
                        <strong>${sched.maxPatients}</strong> <small>(On: ${sched.onlineSlots} · Quầy: ${walkIn})</small>
                    </div>
                </div>
            `;
        };

        container.innerHTML = /* HTML */ `
            <table style="min-width:1050px;">
                <thead>
                    <tr>
                        <th style="min-width:210px;">Bác sĩ / Nhân sự</th>
                        ${weekDays.map((d) => /* HTML */ `
                            <th style="text-align:center; min-width:140px; ${d.isToday ? 'background:#ecfeff; border-top:2px solid #06b6d4; color:#0e7490;' : ''}">
                                <div style="font-size:11px; font-weight:600;">${d.dayName}</div>
                                <div style="font-size:13px; font-weight:700;">${d.shortDate}</div>
                                ${d.isToday ? '<div style="font-size:10px; color:#0891b2;">Hôm nay</div>' : ''}
                            </th>
                        `).join('')}
                    </tr>
                </thead>
                <tbody>
                    ${doctors.map((doc) => {
                        const initials = (doc.fullName || 'BS').trim().split(/\s+/).slice(-2).map((w) => w[0]?.toUpperCase() || '').join('');
                        return /* HTML */ `
                            <tr>
                                <td>
                                    <div style="display:flex; align-items:center; gap:8px;">
                                        <span style="width:30px; height:30px; border-radius:50%; background:#e0f2fe; color:#0369a1; display:inline-flex; align-items:center; justify-content:center; font-size:11px; font-weight:700; flex-shrink:0;">${escape(initials)}</span>
                                        <div>
                                            <strong>${escape(doc.fullName)}</strong>
                                            <small style="display:block; color:#64748b;">${escape(doc.specialty || doc.departmentName || '')}</small>
                                        </div>
                                    </div>
                                </td>
                                ${weekDays.map((day) => {
                                    const nightDuty = dutyShifts.find((ds) => ds.accountId === doc.accountId && String(ds.startTime).slice(0, 10) === day.iso);
                                    const nightHtml = nightDuty
                                        ? `<div style="background:#0f172a; color:#fff; border-radius:6px; padding:6px 8px; font-size:11px;">
                                                <div style="display:flex; justify-content:space-between;"><strong>🌙 Trực đêm</strong><span>18:00-07:00</span></div>
                                                <small style="color:#cbd5e1; display:block; margin-top:2px;">${escape(nightDuty.note || 'Trực đêm')}</small>
                                           </div>`
                                        : '';
                                    return /* HTML */ `
                                        <td style="vertical-align:top; ${day.isToday ? 'background:#f0fdfa;' : ''}">
                                            <div style="display:flex; flex-direction:column; gap:6px;">
                                                ${renderShiftBlock(doc, day, 0)}
                                                ${renderShiftBlock(doc, day, 1)}
                                                ${nightHtml}
                                            </div>
                                        </td>
                                    `;
                                }).join('')}
                            </tr>
                        `;
                    }).join('')}
                    ${nonDoctorStaff.map((st) => {
                        const initials = (st.fullName || 'NV').trim().split(/\s+/).slice(-2).map((w) => w[0]?.toUpperCase() || '').join('');
                        return /* HTML */ `
                            <tr>
                                <td>
                                    <div style="display:flex; align-items:center; gap:8px;">
                                        <span style="width:30px; height:30px; border-radius:50%; background:#f1f5f9; color:#475569; display:inline-flex; align-items:center; justify-content:center; font-size:11px; font-weight:700; flex-shrink:0;">${escape(initials)}</span>
                                        <div>
                                            <strong>${escape(st.fullName)}</strong>
                                            <small style="display:block; color:#64748b;">${escape(st.roleName || 'Nhân sự khoa')}</small>
                                        </div>
                                    </div>
                                </td>
                                ${weekDays.map((day) => {
                                    const nightDuty = dutyShifts.find((ds) => ds.accountId === st.id && String(ds.startTime).slice(0, 10) === day.iso);
                                    const nightHtml = nightDuty
                                        ? `<div style="background:#0f172a; color:#fff; border-radius:6px; padding:6px 8px; font-size:11px;">
                                                <div style="display:flex; justify-content:space-between;"><strong>🌙 Trực đêm</strong><span>18:00-07:00</span></div>
                                                <small style="color:#cbd5e1; display:block; margin-top:2px;">${escape(nightDuty.note || 'Trực đêm')}</small>
                                           </div>`
                                        : `<div style="color:#cbd5e1; text-align:center; padding:6px;">—</div>`;
                                    return `<td style="vertical-align:top; ${day.isToday ? 'background:#f0fdfa;' : ''}">${nightHtml}</td>`;
                                }).join('')}
                            </tr>
                        `;
                    }).join('')}
                </tbody>
            </table>
        `;
    }

    async function openDecisionMatrix(workDate, shift) {
        const deptId = Number(find('#scheduling-dept')?.value || 0);
        if (!deptId) return notify('Vui lòng chọn khoa chuyên môn.', true);

        const matrix = await request(`DecisionMatrix?departmentId=${deptId}&workDate=${encodeURIComponent(workDate)}&shift=${shift}`);
        const body = find('#decision-matrix-body');
        const shiftText = shift === 0 ? 'Ca Sáng (07:30 - 11:30)' : 'Ca Chiều (13:00 - 16:30)';
        find('#decision-matrix-title').textContent = `Duyệt ca ${date(workDate)} · ${shiftText}`;

        const roomOptions = (matrix.availableRooms || [])
            .map((r) => /* HTML */ `<option value="${r.id}">${escape(r.roomNumber)} - ${escape(r.roomName)}</option>`)
            .join('');
        const allDeptRooms = (schedulingState?.rooms || [])
            .map((r) => /* HTML */ `<option value="${r.id}">${escape(r.roomNumber)} - ${escape(r.roomName)}</option>`)
            .join('');

        if (!matrix.candidates || matrix.candidates.length === 0) {
            body.innerHTML = '<p class="empty">Chưa có bác sĩ đăng ký hoặc được xếp trong ca này.</p>';
        } else {
            body.innerHTML = /* HTML */ `
                <p style="margin:0 0 10px; color:#64748b; font-size:13px;">
                    Tổng phòng hoạt động: <strong>${matrix.totalActiveRooms}</strong> · Phòng còn trống: <strong>${(matrix.availableRooms || []).length}</strong>
                    ${matrix.isHotShift ? ' · <span class="badge reject">Ca Hot (đông đăng ký)</span>' : ''}
                </p>
                <div class="table-scroll">
                    <table>
                        <thead>
                            <tr>
                                <th>Bác sĩ</th>
                                <th>Ưu tiên / Cảnh báo</th>
                                <th>Phòng &amp; Duyệt</th>
                                <th>Điều chuyển (nếu đang chờ)</th>
                            </tr>
                        </thead>
                        <tbody>
                            ${matrix.candidates.map((c) => {
                                const isApproved = c.status === 1 || c.status === 'Approved';
                                const warnings = [];
                                if (c.hasNightDutyConflict) warnings.push('<span class="badge reject">Trực đêm hôm trước</span>');
                                if (c.rotationBadgeText) warnings.push(/* HTML */ `<span class="badge">${escape(c.rotationBadgeText)}</span>`);
                                warnings.push(`<span>Điểm: ${c.rotationPriorityScore} · Ca tuần: ${c.approvedShiftsThisPeriod} · Tái khám: ${c.continuityOfCareFollowUpCount}</span>`);

                                const approveCell = isApproved
                                    ? /* HTML */ `<span class="badge ok">Đã duyệt (${escape(c.roomNumber || 'Đã gán')})</span>`
                                    : (roomOptions
                                        ? /* HTML */ `<div style="display:flex; gap:6px; align-items:center;">
                                                <select id="matrix-room-${c.scheduleId}">${roomOptions}</select>
                                                <button type="button" class="btn button primary sm" data-approve-shift="${c.scheduleId}">Duyệt</button>
                                            </div>`
                                        : '<span style="color:#b91c1c;">Hết phòng trống</span>');

                                const reallocCell = isApproved
                                    ? '—'
                                    : /* HTML */ `<div style="display:flex; gap:4px; flex-wrap:wrap; align-items:center;">
                                            <input type="date" id="realloc-date-${c.scheduleId}" value="${escape(workDate)}" style="width:130px;" />
                                            <select id="realloc-shift-${c.scheduleId}">
                                                <option value="0" ${shift === 0 ? 'selected' : ''}>Sáng</option>
                                                <option value="1" ${shift === 1 ? 'selected' : ''}>Chiều</option>
                                            </select>
                                            <select id="realloc-room-${c.scheduleId}">${allDeptRooms}</select>
                                            <button type="button" class="btn button secondary sm" data-realloc-shift="${c.scheduleId}">Chuyển</button>
                                        </div>`;

                                return /* HTML */ `
                                    <tr>
                                        <td><strong>${escape(c.doctorName)}</strong>${c.doctorNote ? `<small style="display:block; color:#64748b;">${escape(c.doctorNote)}</small>` : ''}</td>
                                        <td>${warnings.join('<br/>')}</td>
                                        <td>${approveCell}</td>
                                        <td>${reallocCell}</td>
                                    </tr>
                                `;
                            }).join('')}
                        </tbody>
                    </table>
                </div>
            `;
        }
        find('#decision-matrix-dialog').showModal();
    }

    async function loadInlineDecisionMatrix(workDate, shift) {
        const deptId = Number(find('#scheduling-dept')?.value || 0);
        if (!deptId || !workDate) return;

        selectedMatrixDate = workDate;
        selectedMatrixShift = shift;
        const dmDate = find('#dm-date');
        const dmShift = find('#dm-shift');
        if (dmDate) dmDate.value = workDate;
        if (dmShift) dmShift.value = String(shift);

        const shiftText = shift === 0 ? 'Ca Sáng (07:30 - 11:30)' : 'Ca Chiều (13:00 - 16:30)';
        const label = find('#dm-selected-label');
        if (label) label.textContent = `Đang chọn ca: ${workDate} — ${shiftText}`;

        const matrix = await request(`DecisionMatrix?departmentId=${deptId}&workDate=${encodeURIComponent(workDate)}&shift=${shift}`);
        const roomsCountEl = find('#dm-rooms-count');
        if (roomsCountEl) {
            roomsCountEl.textContent = `Số buồng khám khả dụng của khoa: ${(matrix.availableRooms || []).length} / ${matrix.totalActiveRooms}`;
        }

        const roomOptions = (matrix.availableRooms || [])
            .map((r) => /* HTML */ `<option value="${r.id}">${escape(r.roomNumber)} - ${escape(r.roomName)}</option>`)
            .join('');

        table(
            '#dm-table',
            ['#', 'Bác sĩ', '1. Trực đêm (Fatigue)', '2. Luân phiên ca Hot', '3. Định mức tuần', '4. BN tái khám', 'Trạng thái', 'Duyệt / Điều phối'],
            (matrix.candidates || []).map((c, idx) => {
                const isApproved = c.status === 1 || c.status === 'Approved';
                const fatigueHtml = c.hasNightDutyConflict
                    ? '<span style="color:#b91c1c; font-weight:600;">⚠ Trực đêm hôm trước</span>'
                    : '<span style="color:#15803d; font-weight:600;">✓ Đảm bảo</span>';
                const rotationHtml = c.rotationBadgeText
                    ? /* HTML */ `<span class="badge" style="background:#e0f2fe; color:#0369a1;">${escape(c.rotationBadgeText)}</span>`
                    : '<span style="color:#64748b;">Bình thường</span>';
                const statusHtml = isApproved
                    ? /* HTML */ `<span class="badge" style="background:#dcfce7; color:#166534;">Đã duyệt (${escape(c.roomNumber || '')})</span>`
                    : '<span class="badge" style="background:#fef3c7; color:#92400e;">Chờ duyệt</span>';
                const actionHtml = isApproved
                    ? /* HTML */ `<span style="color:#15803d; font-weight:600;">Phòng ${escape(c.roomNumber || '')}</span>`
                    : /* HTML */ `<div style="display:flex; gap:6px; align-items:center; flex-wrap:wrap;">
                            ${roomOptions
                                ? /* HTML */ `<select id="inline-room-${c.scheduleId}"><option value="">-- Chọn phòng --</option>${roomOptions}</select>
                                    <button type="button" class="btn button primary sm" data-inline-approve="${c.scheduleId}">Duyệt</button>`
                                : '<span style="color:#b91c1c;">Hết phòng trống</span>'}
                            <button type="button" class="btn button secondary sm" data-open-matrix="${escape(workDate)}" data-shift="${shift}">Điều chuyển</button>
                        </div>`;

                return [
                    String(idx + 1),
                    /* HTML */ `<strong>${escape(c.doctorName)}</strong><br /><small style="color:#64748b;">${escape([c.academicTitle, c.specialty, c.employmentType === 0 ? 'Full-time' : 'Part-time'].filter(Boolean).join(' · '))}</small>`,
                    fatigueHtml,
                    rotationHtml,
                    `${c.approvedShiftsThisPeriod} / ${c.maxShiftsPerWeek} ca`,
                    String(c.continuityOfCareFollowUpCount),
                    statusHtml,
                    actionHtml,
                ];
            }),
        );
    }

    find('#scheduling-dept')?.addEventListener('change', (e) => {
        e.currentTarget.dataset.userPicked = '1';
        selectedMatrixDate = null;
        loadScheduling(Number(e.target.value)).catch((err) => notify(err.message, true));
    });

    find('#dm-date')?.addEventListener('change', (e) => {
        const d = e.currentTarget.value;
        const s = Number(find('#dm-shift')?.value || 0);
        if (d) loadInlineDecisionMatrix(d, s).catch((err) => notify(err.message, true));
    });

    find('#dm-shift')?.addEventListener('change', (e) => {
        const d = find('#dm-date')?.value;
        const s = Number(e.currentTarget.value || 0);
        if (d) loadInlineDecisionMatrix(d, s).catch((err) => notify(err.message, true));
    });

    find('#open-create-period')?.addEventListener('click', () => {
        const dialog = find('#create-period-dialog');
        const form = find('#create-period-form');
        form.reset();
        form.querySelector('.form-error').textContent = '';
        dialog.showModal();
    });

    find('#create-period-form')?.addEventListener('submit', (e) => {
        e.preventDefault();
        const data = Object.fromEntries(new FormData(e.currentTarget));
        data.departmentId = data.departmentId ? Number(data.departmentId) : null;
        mutate('SchedulingPeriods', data, e.currentTarget);
    });

    find('#publish-period-btn')?.addEventListener('click', (e) => {
        const periodId = Number(e.currentTarget.dataset.periodId || 0);
        if (!periodId) return;
        if (confirm('Xác nhận xuất bản kỳ lịch và sinh slot khám trực tuyến cho tất cả ca đã duyệt?')) {
            mutate('PublishPeriod', { periodId });
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
        if (preset?.maxPatients) form.elements.maxPatients.value = String(preset.maxPatients);
        if (preset?.onlineSlots !== undefined) form.elements.onlineSlots.value = String(preset.onlineSlots);
        if (preset?.doctorNote) form.elements.doctorNote.value = preset.doctorNote;
        find('#upsert-schedule-title').textContent = preset?.id ? 'Chỉnh sửa Quota / Phòng khám' : 'Xếp lịch trực tiếp';
        dialog.showModal();
    }

    find('#open-upsert-schedule')?.addEventListener('click', () => openUpsertScheduleModal());

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
        data.maxPatients = Number(data.maxPatients || 40);
        data.onlineSlots = Number(data.onlineSlots || 28);
        mutate('UpsertSchedule', data, e.currentTarget);
    });

    find('#decision-matrix-body')?.addEventListener('click', (e) => {
        const approveBtn = e.target.closest('[data-approve-shift]');
        if (approveBtn) {
            const scheduleId = Number(approveBtn.dataset.approveShift);
            const roomId = Number(find(`#matrix-room-${scheduleId}`)?.value || 0);
            if (!roomId) return notify('Vui lòng chọn phòng khám.', true);
            find('#decision-matrix-dialog').close();
            mutate('ApproveShift', { scheduleId, roomId });
            return;
        }
        const reallocBtn = e.target.closest('[data-realloc-shift]');
        if (reallocBtn) {
            const oldScheduleId = Number(reallocBtn.dataset.reallocShift);
            const targetDate = find(`#realloc-date-${oldScheduleId}`)?.value;
            const targetShift = Number(find(`#realloc-shift-${oldScheduleId}`)?.value || 0);
            const targetRoomId = Number(find(`#realloc-room-${oldScheduleId}`)?.value || 0);
            if (!targetDate || !targetRoomId) return notify('Vui lòng chọn đủ ngày và phòng điều chuyển.', true);
            find('#decision-matrix-dialog').close();
            mutate('ReallocateShift', { oldScheduleId, targetDate, targetShift, targetRoomId });
        }
    });

    document.addEventListener('click', (e) => {
        const delSchedBtn = e.target.closest('[data-delete-schedule]');
        if (delSchedBtn) {
            e.stopPropagation();
            if (confirm('Xác nhận hủy ca khám này?')) {
                const departmentId = Number(find('#scheduling-dept')?.value || 0);
                mutate('DeleteSchedule', { scheduleId: Number(delSchedBtn.dataset.deleteSchedule), departmentId });
            }
            return;
        }
        const addSchedBtn = e.target.closest('[data-add-schedule]');
        if (addSchedBtn) {
            openUpsertScheduleModal({
                doctorId: Number(addSchedBtn.dataset.addSchedule),
                workDate: addSchedBtn.dataset.date,
                shift: Number(addSchedBtn.dataset.shift),
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
                    maxPatients: sched.maxPatients,
                    onlineSlots: sched.onlineSlots,
                    doctorNote: sched.doctorNote || '',
                });
            }
            return;
        }
        const selectCell = e.target.closest('[data-select-matrix]');
        if (selectCell) {
            const d = selectCell.dataset.selectMatrix;
            const s = Number(selectCell.dataset.shift);
            loadInlineDecisionMatrix(d, s)
                .then(() => find('#inline-decision-matrix')?.scrollIntoView({ behavior: 'smooth', block: 'nearest' }))
                .catch((err) => notify(err.message, true));
            return;
        }
        const inlineApproveBtn = e.target.closest('[data-inline-approve]');
        if (inlineApproveBtn) {
            const scheduleId = Number(inlineApproveBtn.dataset.inlineApprove);
            const roomId = Number(find(`#inline-room-${scheduleId}`)?.value || 0);
            if (!roomId) return notify('Vui lòng chọn phòng khám trước khi duyệt.', true);
            mutate('ApproveShift', { scheduleId, roomId });
            return;
        }
        const matrixBtn = e.target.closest('[data-open-matrix]');
        if (matrixBtn) {
            openDecisionMatrix(matrixBtn.dataset.openMatrix, Number(matrixBtn.dataset.shift)).catch((err) => notify(err.message, true));
            return;
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

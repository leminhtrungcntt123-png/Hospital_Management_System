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
    let busy = false;
    let reportVersion = 0;

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
        table(
            '#report-table',
            ['Ngày', 'Tạm ứng', 'Thanh toán', 'Hoàn tiền', 'Thực thu', 'Lượt tiếp nhận'],
            report.days.map((day) => [
                escape(date(day.date)),
                ...[day.deposits, day.payments, day.refunds, day.revenue, day.visits].map(
                    (value) => /* HTML */ `<span class="number">${escape(money(value))}</span>`,
                ),
            ]),
        );

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
            // bỏ qua lỗi phụ nếu chưa có dữ liệu khoa
        }
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

    async function loadAudit() {
        const rows = await request('AuditLogs');
        table(
            '#audit-table',
            ['Thời gian', 'Tài khoản / Nhân sự', 'Hành động', 'Bệnh nhân', 'Bệnh án', 'Địa chỉ IP'],
            rows.map((row) => [
                /* HTML */ `<small>${escape(new Date(row.occurredAt).toLocaleString('vi-VN'))}</small>`,
                /* HTML */ `<strong>${escape(row.fullName)}</strong><br /><small style="color:#64748b;">${escape(row.username)}</small>`,
                /* HTML */ `<span class="status">${escape(row.action)}</span>`,
                escape(row.patientName || '—'),
                escape(row.medicalRecordId ? '#' + row.medicalRecordId : '—'),
                /* HTML */ `<code>${escape(row.ipAddress || '—')}</code>`,
            ]),
        );
    }

    async function loadRequests() {
        const rows = await request('Requests');
        const statuses = { Pending: 'Chờ duyệt', Approved: 'Đã duyệt', Rejected: 'Đã từ chối' };
        table(
            '#requests-table',
            ['Bác sĩ / phòng ban', 'Ngày / ca', 'Đề nghị', 'Trạng thái', 'Xử lý'],
            rows.map((row) => [
                /* HTML */ `<strong>${escape(row.doctor)}</strong><br /><small
                        >${escape(row.department)}</small
                    >`,
                /* HTML */ `${escape(date(row.date))}<br />${escape(row.shift)}`,
                /* HTML */ `${escape(row.type)}<br /><small
                        >${escape(row.reason || 'Không có ghi chú')}</small
                    >`,
                /* HTML */ `<span class="status ${row.status === 'Approved' ? '' : 'inactive'}"
                    >${escape(statuses[row.status] || row.status)}</span
                >`,
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
                    : escape(row.replacement || 'Đã xử lý'),
            ]),
        );
    }

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
                await Promise.all([loadDirectory(), loadRequests(), loadCatalog(), loadAudit()]);
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
    window.addEventListener('hashchange', selectSection);

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

    async function refresh() {
        await Promise.all([
            loadDirectory(),
            loadRequests(),
            loadReport(!!find('#report-filter').elements.from.value),
            loadCatalog(),
            loadAudit(),
        ]);
    }

    selectSection();
    refresh().catch((error) => notify(error.message, true));
})();

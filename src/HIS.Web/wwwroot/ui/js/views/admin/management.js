/* views / admin / management */
function accountsPage() {
    const list = db.accounts.filter((a) =>
        matched(a.name + ' ' + a.username + ' ' + (a.email || ''))
    );
    return /* HTML */ `${heading('Tài khoản & phân quyền', 'Quản lý 5 vai trò và trạng thái tài khoản trong hệ thống.', button('Tạo tài khoản', 'new-account', 'userplus', 'btn primary'))}
        <section class="card">
            <div class="toolbar">
                ${searchInput('Tìm tên, tên đăng nhập hoặc email…')}<span
                    >${list.length} tài khoản</span
                >
            </div>
            <div class="table-wrap">
                <table>
                    <thead>
                        <tr>
                            <th>Nhân sự / Tài khoản</th>
                            <th>Vai trò</th>
                            <th>Khoa</th>
                            <th>Liên hệ</th>
                            <th>Hoạt động</th>
                            <th>Thao tác</th>
                        </tr>
                    </thead>
                    <tbody>
                        ${list
                            .map(
                                (a) =>
                                    /* HTML */ `<tr>
                                        <td>
                                            <div class="person-cell">
                                                ${avatar(a.name)}
                                                <div>
                                                    <strong>${a.name}</strong
                                                    ><small>${a.username}</small>
                                                </div>
                                            </div>
                                        </td>
                                        <td><span class="pill">${roleInfo[a.role].name}</span></td>
                                        <td>${dept(a.deptId)?.name || '—'}</td>
                                        <td>${esc(a.email || 'Chưa cập nhật')}</td>
                                        <td>
                                            <button
                                                class="switch ${a.active ? 'on' : ''}"
                                                role="switch"
                                                aria-checked="${a.active}"
                                                aria-label="Đổi trạng thái tài khoản ${esc(a.name)}"
                                                data-act="toggle-account"
                                                data-id="${a.id}"
                                                ${a.id === session.accountId ? 'disabled' : ''}
                                            ></button>
                                        </td>
                                        <td>
                                            <div class="row-actions">
                                                ${button('Sửa', 'edit-account', 'edit', 'btn ghost sm', `data-id="${a.id}"`)}${button('Đặt lại mật khẩu', 'reset-account-password', 'shield', 'text-link', `data-id="${a.id}"`)}
                                            </div>
                                        </td>
                                    </tr>`
                            )
                            .join('')}
                    </tbody>
                </table>
            </div>
        </section>
        ${notice('Quản trị viên quản lý hệ thống và xem dữ liệu thu phí; nội dung phiếu khám chỉ dành cho các vai trò chuyên môn được cấp quyền.')}`;
}

const catalogOptions = {
    departments: 'Khoa',
    rooms: 'Phòng khám',
    services: 'Dịch vụ',
    medications: 'Thuốc',
    insurancePlans: 'Gói BHYT',
};

function catalogPage() {
    const key = ui.catalog,
        list = db[key].filter((x) => matched(x.name || x.number));
    return /* HTML */ `${heading('Danh mục hệ thống', 'Dữ liệu dùng chung cho lịch khám, chỉ định, kê đơn và thu phí.', button('Thêm ' + catalogOptions[key].toLowerCase(), 'new-catalog', 'plus', 'btn primary'))}
        <section class="card">
            ${tabs(Object.entries(catalogOptions), key, 'catalog')}
            <div class="toolbar">
                ${searchInput('Tìm trong danh mục…')}<span>${list.length} mục</span>
            </div>
            <div class="table-wrap">
                <table>
                    <thead>
                        <tr>
                            <th>Tên danh mục</th>
                            <th>Thông tin</th>
                            <th>
                                ${key === 'insurancePlans' ? 'Mức hưởng' : key === 'rooms' ? 'Khoa' : key === 'departments' ? 'Mô tả' : 'Đơn giá'}
                            </th>
                            <th>Trạng thái</th>
                            <th>Thao tác</th>
                        </tr>
                    </thead>
                    <tbody>
                        ${list
                            .map(
                                (x) =>
                                    /* HTML */ `<tr>
                                        <td>
                                            <strong>${esc(x.name)}</strong><small>${x.id}</small>
                                        </td>
                                        <td>
                                            ${key === 'services' ? typeNames[x.type] : key === 'medications' ? esc(x.ingredient || 'Chưa cập nhật') + '<small>' + esc(x.strength || '') + ' · ' + esc(x.unit) + '</small>' : key === 'insurancePlans' ? (x.referral === 'IN_NETWORK' ? 'Đúng tuyến' : 'Trái tuyến') : key === 'rooms' ? 'Phòng ' + esc(x.number) : 'Khoa chuyên môn'}
                                        </td>
                                        <td>
                                            ${key === 'insurancePlans' ? x.pct + '%' : key === 'rooms' ? dept(x.deptId)?.name : key === 'departments' ? esc(x.description || '—') : money(x.price)}
                                        </td>
                                        <td>
                                            ${x.active === false ? '<span class="pill">Tạm ngưng</span>' : '<span class="pill success">Sẵn sàng</span>'}
                                        </td>
                                        <td>
                                            ${button('Chỉnh sửa', 'edit-catalog', 'edit', 'text-link', `data-id="${x.id}"`)}
                                        </td>
                                    </tr>`
                            )
                            .join('')}
                    </tbody>
                </table>
            </div>
        </section>`;
}

function logsPage() {
    const list = db.logs.filter((l) =>
        matched(
            l.action +
                ' ' +
                l.patientId +
                ' ' +
                (db.accounts.find((a) => a.id === l.actor)?.name || '')
        )
    );
    return /* HTML */ `${heading('Nhật ký truy cập', 'Theo dõi ai đã đọc, sửa bệnh án hoặc xác minh liên kết. Không hiển thị nội dung bệnh án.')}
        <section class="card">
            <div class="toolbar">
                ${searchInput('Tìm thao tác, nhân sự hoặc mã người khám…')}<span
                    >${list.length} hoạt động</span
                >
            </div>
            ${
                list.length
                    ? /* HTML */ `<div class="table-wrap">
                          <table>
                              <thead>
                                  <tr>
                                      <th>Thời điểm</th>
                                      <th>Tài khoản</th>
                                      <th>Thao tác</th>
                                      <th>Người khám</th>
                                      <th>Phân hệ</th>
                                      <th>Thông tin ghi vết</th>
                                  </tr>
                              </thead>
                              <tbody>
                                  ${list
                                      .slice(0, 50)
                                      .map(
                                          (l) =>
                                              /* HTML */ `<tr>
                                                  <td>
                                                      ${fmtDate(l.date)}<small
                                                          >${new Date(l.date).toLocaleTimeString('vi-VN', { timeZone: 'Asia/Ho_Chi_Minh', hour: '2-digit', minute: '2-digit' })}</small
                                                      >
                                                  </td>
                                                  <td>
                                                      ${esc(db.accounts.find((a) => a.id === l.actor)?.name || 'Hệ thống mẫu')}
                                                  </td>
                                                  <td>
                                                      <span class="code-tag">${esc(l.action)}</span>
                                                  </td>
                                                  <td>${l.patientId || '—'}</td>
                                                  <td>${l.module}</td>
                                                  <td>${esc(l.detail || '')}</td>
                                              </tr>`
                                      )
                                      .join('')}
                              </tbody>
                          </table>
                      </div>`
                    : empty('Chưa có hoạt động phù hợp')
            }
        </section>`;
}

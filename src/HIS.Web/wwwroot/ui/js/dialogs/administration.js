/* dialogs / administration */
function accountModal(id = '') {
    requireRole('ADMIN');
    const a = db.accounts.find((a) => a.id === id);
    openModal(
        a ? 'Chỉnh tài khoản' : 'Tạo tài khoản',
        'Tài khoản theo 5 vai trò của bệnh viện.',
        /* HTML */ `<form id="account-form" data-id="${id}">
            <div class="form-grid">
                <div class="field full">
                    <label for="ac-name">Họ và tên *</label
                    ><input
                        id="ac-name"
                        name="name"
                        value="${esc(a?.name || '')}"
                        required
                        minlength="3"
                    />
                </div>
                <div class="field">
                    <label for="ac-user">Tên đăng nhập *</label
                    ><input
                        id="ac-user"
                        name="username"
                        value="${esc(a?.username || '')}"
                        required
                        pattern="[a-zA-Z0-9_.]{3,30}"
                    />
                </div>
                <div class="field">
                    <label for="ac-role">Vai trò *</label
                    ><select
                        id="ac-role"
                        name="role"
                        ${a?.id === session.accountId ? 'disabled' : ''}
                    >
                        ${Object.entries(roleInfo)
                            .map(
                                ([r, info]) =>
                                    /* HTML */ `<option
                                        value="${r}"
                                        ${a?.role === r ? 'selected' : ''}
                                    >
                                        ${info.name}
                                    </option>`
                            )
                            .join('')}
                    </select>
                </div>
                <div class="field full">
                    <label for="ac-dept">Khoa phụ trách</label
                    ><select id="ac-dept" name="deptId">
                        <option value="">Không thuộc khoa</option>
                        ${db.departments.map((d) => /* HTML */ `<option value="${d.id}" ${a?.deptId === d.id ? 'selected' : ''}>${d.name}</option>`).join('')}
                    </select>
                </div>
                <div class="field full">
                    <label for="ac-email">Email liên hệ</label
                    ><input
                        id="ac-email"
                        name="email"
                        type="email"
                        value="${esc(a?.email || '')}"
                    />
                </div>
                <div class="field full">
                    <label for="ac-active">Trạng thái</label
                    ><select
                        id="ac-active"
                        name="active"
                        ${a?.id === session.accountId ? 'disabled' : ''}
                    >
                        <option value="true" ${a?.active !== false ? 'selected' : ''}>
                            Hoạt động
                        </option>
                        <option value="false" ${a?.active === false ? 'selected' : ''}>
                            Khóa tài khoản
                        </option>
                    </select>
                </div>
            </div>
        </form>`,
        button('Đóng', 'close') +
            '<button class="btn primary" form="account-form" type="submit">' +
            (a ? 'Lưu thay đổi' : 'Tạo tài khoản mẫu') +
            '</button>'
    );
}

function catalogModal(id = '') {
    requireRole('ADMIN');
    const key = ui.catalog,
        item = db[key].find((x) => x.id === id) || {},
        field = (name, label, value = '', type = 'text', extra = '') =>
            /* HTML */ `<div class="field">
                <label for="cat-${name}">${label}</label
                ><input
                    id="cat-${name}"
                    name="${name}"
                    type="${type}"
                    value="${esc(value)}"
                    ${extra}
                />
            </div>`;
    let fields = field(
        'name',
        'Tên ' + catalogOptions[key].toLowerCase() + ' *',
        item.name || '',
        'text',
        'required maxlength="150"'
    );
    if (key === 'departments') fields += field('description', 'Mô tả', item.description || '');
    if (key === 'rooms')
        fields +=
            /* HTML */ `<div class="field"><label for="cat-dept">Khoa *</label><select id="cat-dept" name="deptId">${db.departments.map((d) => /* HTML */ `<option value="${d.id}" ${item.deptId === d.id ? 'selected' : ''}>${d.name}</option>`).join('')}</select></div>` +
            field('number', 'Số phòng *', item.number || '', 'text', 'required');
    if (key === 'services')
        fields +=
            /* HTML */ `<div class="field"><label for="cat-type">Loại dịch vụ</label><select id="cat-type" name="type">${Object.entries(
                typeNames
            )
                .map(
                    ([t, n]) =>
                        /* HTML */ `<option value="${t}" ${item.type === t ? 'selected' : ''}>
                            ${n}
                        </option>`
                )
                .join('')}</select></div>` +
            field('price', 'Đơn giá (VNĐ) *', item.price || 0, 'number', 'required min="0"') +
            field('description', 'Mô tả', item.description || '');
    if (key === 'medications')
        fields +=
            field('ingredient', 'Hoạt chất', item.ingredient || '') +
            field('strength', 'Hàm lượng', item.strength || '') +
            field('unit', 'Đơn vị *', item.unit || '', 'text', 'required') +
            field('price', 'Đơn giá (VNĐ) *', item.price || 0, 'number', 'required min="0"');
    if (key === 'insurancePlans')
        fields +=
            field(
                'pct',
                'Mức hưởng (%) *',
                item.pct ?? 80,
                'number',
                'required min="0" max="100" step="0.01"'
            ) +
            /* HTML */ `<div class="field">
                <label for="cat-referral">Loại tuyến</label
                ><select id="cat-referral" name="referral">
                    <option
                        value="IN_NETWORK"
                        ${item.referral !== 'OUT_OF_NETWORK' ? 'selected' : ''}
                    >
                        Đúng tuyến
                    </option>
                    <option
                        value="OUT_OF_NETWORK"
                        ${item.referral === 'OUT_OF_NETWORK' ? 'selected' : ''}
                    >
                        Trái tuyến
                    </option>
                </select>
            </div>`;
    if (['services', 'medications'].includes(key))
        fields += /* HTML */ `<div class="field">
            <label for="cat-active">Trạng thái</label
            ><select id="cat-active" name="active">
                <option value="true" ${item.active !== false ? 'selected' : ''}>
                    Đang sử dụng
                </option>
                <option value="false" ${item.active === false ? 'selected' : ''}>Tạm ngưng</option>
            </select>
        </div>`;
    openModal(
        (id ? 'Chỉnh sửa ' : 'Thêm ') + catalogOptions[key].toLowerCase(),
        'Danh mục dùng chung trong hệ thống.',
        /* HTML */ `<form id="catalog-form" data-id="${id}" data-key="${key}">
            <div class="form-grid">${fields}</div>
        </form>`,
        button('Đóng', 'close') +
            '<button class="btn primary" type="submit" form="catalog-form">Lưu danh mục</button>'
    );
}

function passwordModal(id = '', admin = false) {
    if (admin) requireRole('ADMIN');
    openModal(
        admin ? 'Đặt lại mật khẩu' : 'Đổi mật khẩu',
        'Mật khẩu mới được mã hóa trước khi lưu.',
        /* HTML */ `<form
            id="password-form"
            data-id="${id || session.accountId}"
            data-admin="${admin}"
        >
            ${admin ? '' : /* HTML */ `<div class="field"><label for="old-pass">Mật khẩu hiện tại *</label><input id="old-pass" name="oldPassword" type="password" required /></div>`}
            <div class="field space-top">
                <label for="new-pass">Mật khẩu mới *</label
                ><input id="new-pass" name="password" type="password" minlength="8" required />
            </div>
            <div class="field space-top">
                <label for="new-pass-confirm">Nhập lại mật khẩu *</label
                ><input
                    id="new-pass-confirm"
                    name="confirm"
                    type="password"
                    minlength="8"
                    required
                />
            </div>
        </form>`,
        button('Đóng', 'close') +
            '<button class="btn primary" form="password-form" type="submit">Lưu mật khẩu</button>'
    );
}

async function hashPassword(text) {
    const data = new TextEncoder().encode(text),
        hash = await crypto.subtle.digest('SHA-256', data);
    return [...new Uint8Array(hash)].map((x) => x.toString(16).padStart(2, '0')).join('');
}

async function checkPassword(account, password) {
    return account.passwordHash
        ? account.passwordHash === (await hashPassword(password))
        : password === 'Medicare@2026';
}

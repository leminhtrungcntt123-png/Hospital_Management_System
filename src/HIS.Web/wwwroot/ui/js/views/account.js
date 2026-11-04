/* views / account */
function loginPage() {
    return /* HTML */ `<div class="auth-card">
        <span class="eyebrow">CHÀO MỪNG TRỞ LẠI</span>
        <h1>Đăng nhập MediCare</h1>
        <p>Tiếp tục hành trình chăm sóc sức khỏe của bạn.</p>
        <form id="login-form">
            <div class="field">
                <label for="login-user">Tên đăng nhập *</label
                ><input
                    id="login-user"
                    name="username"
                    autocomplete="username"
                    placeholder="Ví dụ: thanhha"
                    required
                />
            </div>
            <div class="field">
                <label for="login-pass">Mật khẩu mẫu *</label
                ><input
                    id="login-pass"
                    name="password"
                    type="password"
                    autocomplete="current-password"
                    required
                />
            </div>
            <button class="btn primary full" type="submit">
                Đăng nhập bản mẫu ${icon('arrow')}
            </button>
        </form>
        <div class="auth-demo">Tài khoản: <b>thanhha</b> · Mật khẩu mẫu: <b>Medicare@2026</b></div>
        <p class="auth-switch">Chưa có tài khoản? ${link('Đăng ký', 'account/register')}</p>
        ${button('Trải nghiệm theo vai trò', 'roles', 'users', 'btn outline full')}
        <p class="fine-print">
            Email là kênh liên hệ. Bản mẫu dùng tên đăng nhập để vào tài khoản trải nghiệm.
        </p>
    </div>`;
}

function registerPage() {
    return /* HTML */ `<div class="auth-card wide">
        <span class="eyebrow">KẾT NỐI CHĂM SÓC</span>
        <h1>Tạo tài khoản bệnh nhân</h1>
        <p>Tài khoản mới được tạo cùng hồ sơ bản thân.</p>
        <form id="register-form">
            <div class="form-grid">
                <div class="field full">
                    <label for="register-name">Họ và tên *</label
                    ><input id="register-name" name="name" required minlength="3" />
                </div>
                <div class="field">
                    <label for="register-user">Tên đăng nhập *</label
                    ><input
                        id="register-user"
                        name="username"
                        required
                        pattern="[a-zA-Z0-9_.]{3,30}"
                    />
                </div>
                <div class="field">
                    <label for="register-phone">Số điện thoại *</label
                    ><input
                        id="register-phone"
                        name="phone"
                        type="tel"
                        pattern="[0-9+ ()-]{9,16}"
                        required
                    />
                </div>
                <div class="field">
                    <label for="register-dob">Ngày sinh *</label
                    ><input id="register-dob" name="dob" type="date" max="${TODAY}" required />
                </div>
                <div class="field">
                    <label for="register-gender">Giới tính</label
                    ><select id="register-gender" name="gender">
                        <option>Nữ</option>
                        <option>Nam</option>
                        <option>Khác</option>
                    </select>
                </div>
                <div class="field full">
                    <label for="register-email">Email liên hệ</label
                    ><input id="register-email" name="email" type="email" />
                </div>
                <div class="field full">
                    <label for="register-password">Mật khẩu *</label
                    ><input
                        id="register-password"
                        name="password"
                        type="password"
                        minlength="8"
                        required
                    /><small>Dùng thông tin giả lập khi trải nghiệm.</small>
                </div>
            </div>
            <label class="check-line"
                ><input name="consent" type="checkbox" required />Tôi đã đọc thông tin về quyền
                riêng tư của bản mẫu.</label
            ><button class="btn primary full" type="submit">
                Tạo tài khoản mẫu ${icon('arrow')}
            </button>
        </form>
        <p class="auth-switch">Đã có tài khoản? ${link('Đăng nhập', 'account/login')}</p>
    </div>`;
}

function accountPage() {
    return /* HTML */ `${heading('Tài khoản của bạn', 'Thông tin liên hệ và không gian trải nghiệm.')}
        <section class="card account-settings">
            <div class="patient-strip">
                ${avatar(acc()?.name || 'Khách', 'cyan', true)}
                <div>
                    <h2>${esc(acc()?.name || 'Khách')}</h2>
                    <p>${activeRole().name} · ${esc(acc()?.username || '')}</p>
                </div>
            </div>
            <div class="info-grid">
                <div>
                    <small>Email liên hệ</small
                    ><strong>${esc(acc()?.email || 'Chưa cập nhật')}</strong>
                </div>
                <div><small>Trạng thái</small><strong>Tài khoản trải nghiệm</strong></div>
            </div>
            <div class="inline-actions">
                ${button('Cập nhật liên hệ', 'edit-self', 'edit', 'btn outline', `data-id="${selfPatient() || ''}"`)}${button('Đổi mật khẩu mẫu', 'change-password', 'shield', 'btn outline')}${button('Chuyển vai trò', 'roles', 'users', 'btn outline')}${button('Đăng xuất', 'logout', 'logout', 'btn ghost')}
            </div>
        </section>`;
}

function accessDenied() {
    return /* HTML */ `<div class="card access-card">
        ${icon('shield')}
        <h1>Không gian này cần quyền phù hợp</h1>
        <p>Vui lòng chọn đúng vai trò để trải nghiệm. Admin không có quyền xem nội dung bệnh án.</p>
        ${button('Chọn vai trò trải nghiệm', 'roles', 'users', 'btn primary')}${link('Về trang chủ', 'public/home', 'arrow', 'btn outline')}
    </div>`;
}

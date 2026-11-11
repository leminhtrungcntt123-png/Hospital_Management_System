let patientProfilesState = {
    loaded: false,
    loading: false,
    error: '',
    selfProfile: null,
    relatives: [],
    accountEmail: '',
    username: '',
    avatarPreview: null,
    savingPersonal: false,
    savePersonalMessage: '',
    relativeError: '',
};

async function ensurePatientProfiles() {
    if (patientProfilesState.loaded || patientProfilesState.loading) return;
    patientProfilesState.loading = true;
    try {
        const response = await fetch(config.profilesUrl || '/Patient/FamilyProfiles/Profiles', {
            credentials: 'same-origin',
            cache: 'no-store',
            headers: { Accept: 'application/json' },
        });
        if (!response.ok) throw new Error('Chưa tải được thông tin hồ sơ.');
        const data = await response.json();
        patientProfilesState.selfProfile = data.selfProfile || {
            fullName: config.displayName || '',
            phoneNumber: '',
            dateOfBirth: '',
            gender: 0,
            address: '',
            email: data.accountEmail || config.userEmail || '',
        };
        patientProfilesState.relatives = Array.isArray(data.relatives) ? data.relatives : [];
        patientProfilesState.accountEmail = data.accountEmail || config.userEmail || '';
        patientProfilesState.username = data.username || '';
        patientProfilesState.loaded = true;
        render();
    } catch (err) {
        patientProfilesState.error = err.message;
    } finally {
        patientProfilesState.loading = false;
    }
}

function patientAccountShell(activeTab, contentHtml) {
    ensurePatientProfiles();
    return `<div class="hospital-portal patient-account-page">
        <nav class="account-breadcrumb" aria-label="Đường dẫn">
            <a href="#/public/home">${icon('building')} Trang chủ</a>
            <span class="sep">/</span>
            <span>Tài khoản</span>
            <span class="sep">/</span>
            <strong>${activeTab === 'personal' ? 'Thông tin cá nhân' : 'Thông tin người thân'}</strong>
        </nav>
        <div class="account-page-header">
            <h1>Tài khoản</h1>
            <p>Cập nhật thông tin cá nhân và quản lý các thành viên gia đình.</p>
        </div>
        <div class="account-page-layout">
            <aside class="account-sidebar">
                <nav class="account-sidebar-nav">
                    <a href="#/patient/profile" class="account-nav-btn ${activeTab === 'personal' ? 'active' : ''}" data-route="patient/profile">
                        ${icon('user')}<span>Thông tin cá nhân</span>
                    </a>
                    <a href="#/patient/family" class="account-nav-btn ${activeTab === 'family' ? 'active' : ''}" data-route="patient/family">
                        ${icon('users')}<span>Thông tin người thân</span>
                    </a>
                </nav>
            </aside>
            <section class="account-content card">
                ${contentHtml}
            </section>
        </div>
    </div>`;
}

function personalProfilePage() {
    const s = patientProfilesState;
    const p = s.selfProfile || {};
    const avatarSrc = s.avatarPreview;

    const formHtml = `
        <div class="account-content-header">
            <h2>Thông tin cá nhân</h2>
        </div>
        ${s.savePersonalMessage ? `<div class="account-alert success">${esc(s.savePersonalMessage)}</div>` : ''}
        ${s.error ? `<div class="account-alert error">${esc(s.error)}</div>` : ''}
        <form class="personal-profile-form" onsubmit="event.preventDefault(); submitSavePersonal();">
            <div class="avatar-uploader">
                <div class="avatar-circle">
                    ${avatarSrc ? `<img src="${avatarSrc}" alt="Avatar" />` : avatar(p.fullName || config.displayName || 'BN')}
                </div>
                <label class="btn outline btn-sm btn-upload-avatar">
                    ✎ Thay ảnh
                    <input type="file" accept="image/jpeg,image/png" style="display:none;" onchange="onAvatarFileSelected(this)" />
                </label>
                <small class="text-muted">Dung lượng file tối đa 1 MB<br />Định dạng: JPEG, PNG</small>
            </div>
            <div class="personal-form-fields">
                <div class="field">
                    <label for="personal-name">Họ và tên *</label>
                    <input id="personal-name" name="fullName" value="${esc(p.fullName || '')}" required minlength="2" maxlength="100" />
                </div>
                <div class="field">
                    <label for="personal-dob">Ngày sinh *</label>
                    <input id="personal-dob" name="dateOfBirth" type="date" value="${esc(p.dateOfBirth || '')}" required />
                </div>
                <div class="field">
                    <label>Giới tính *</label>
                    <div class="gender-radio-group">
                        <label><input type="radio" name="personalGender" value="0" ${Number(p.gender) === 0 ? 'checked' : ''} /> Nam</label>
                        <label><input type="radio" name="personalGender" value="1" ${Number(p.gender) === 1 ? 'checked' : ''} /> Nữ</label>
                        <label><input type="radio" name="personalGender" value="2" ${Number(p.gender) === 2 ? 'checked' : ''} /> Khác</label>
                    </div>
                </div>
                <div class="field">
                    <label for="personal-phone">Số điện thoại *</label>
                    <input id="personal-phone" name="phoneNumber" type="tel" value="${esc(p.phoneNumber || '')}" pattern="(0[0-9]{9}|[+]84[0-9]{9})" required />
                </div>
                <div class="field">
                    <label for="personal-email">Email</label>
                    <input id="personal-email" name="email" type="email" value="${esc(p.email || s.accountEmail || '')}" placeholder="example@email.com" />
                </div>
                <div class="field">
                    <label for="personal-address">Địa chỉ</label>
                    <input id="personal-address" name="address" value="${esc(p.address || '')}" placeholder="Số nhà, đường, quận/huyện..." />
                </div>
                <div class="form-actions">
                    <button class="btn primary" type="submit" ${s.savingPersonal ? 'disabled' : ''}>
                        ${s.savingPersonal ? 'Đang lưu…' : 'Lưu thay đổi'}
                    </button>
                </div>
            </div>
        </form>
    `;
    return patientAccountShell('personal', formHtml);
}

function renderRelativesContent() {
    const s = patientProfilesState;
    const relatives = s.relatives || [];

    return `
        <div class="account-content-header has-action">
            <h2>Thông tin người thân</h2>
            <button class="btn primary btn-sm" type="button" onclick="openAddRelativeModal()">
                + Thêm
            </button>
        </div>
        ${s.error ? `<div class="account-alert error">${esc(s.error)}</div>` : ''}
        ${
            relatives.length === 0
                ? `<div class="empty-relatives">
                    <p class="text-muted">Chưa có thông tin người thân được liên kết.</p>
                    <button class="btn outline btn-sm" type="button" onclick="openAddRelativeModal()">
                        ${icon('users')} Thêm người thân đầu tiên
                    </button>
                </div>`
                : `<div class="relatives-grid">
                    ${relatives
                        .map(
                            (r) => `
                        <div class="relative-card">
                            <div class="relative-card-badge">${esc(r.relationshipLabel || 'Người thân')}</div>
                            <strong class="relative-card-name">${esc(r.fullName)}</strong>
                            <div class="relative-card-meta">
                                <span><strong>Ngày sinh:</strong> ${esc(r.dateOfBirth || '—')}</span>
                                <span><strong>Giới tính:</strong> ${Number(r.gender) === 1 ? 'Nữ' : Number(r.gender) === 2 ? 'Khác' : 'Nam'}</span>
                                <span><strong>Số điện thoại:</strong> ${esc(r.phoneNumber || '—')}</span>
                                ${r.medicalCode ? `<span><strong>Mã y tế:</strong> ${esc(r.medicalCode)}</span>` : ''}
                            </div>
                        </div>
                    `,
                        )
                        .join('')}
                </div>`
        }
    `;
}

function familyPage() {
    if (config.role !== 'PATIENT') {
        return `${heading('Hồ sơ người khám', 'Quản lý thông tin người khám và các liên kết được hệ thống xác nhận.', unavailableButton('Thêm người khám'))}
            ${unavailableNotice()}${pendingTable('Danh sách hồ sơ', ['Người khám', 'Ngày sinh', 'Quan hệ', 'Trạng thái liên kết'])}`;
    }
    return patientAccountShell('family', renderRelativesContent());
}

function onAvatarFileSelected(input) {
    if (!input.files || !input.files[0]) return;
    const file = input.files[0];
    if (file.size > 1024 * 1024) {
        alert('Dung lượng ảnh vượt quá 1 MB.');
        return;
    }
    const reader = new FileReader();
    reader.onload = (e) => {
        patientProfilesState.avatarPreview = e.target.result;
        render();
    };
    reader.readAsDataURL(file);
}

async function submitSavePersonal() {
    const s = patientProfilesState;
    const nameInput = document.getElementById('personal-name');
    const dobInput = document.getElementById('personal-dob');
    const phoneInput = document.getElementById('personal-phone');
    const emailInput = document.getElementById('personal-email');
    const addrInput = document.getElementById('personal-address');
    const genderChecked = document.querySelector('input[name="personalGender"]:checked');

    if (!nameInput || !dobInput || !phoneInput) return;

    s.savingPersonal = true;
    s.savePersonalMessage = '';
    s.error = '';
    render();

    try {
        const payload = {
            fullName: nameInput.value.trim(),
            dateOfBirth: dobInput.value,
            phoneNumber: phoneInput.value.trim(),
            gender: genderChecked ? Number(genderChecked.value) : 0,
            email: emailInput ? emailInput.value.trim() : null,
            address: addrInput ? addrInput.value.trim() : null,
        };

        const csrf = document.querySelector('[name="__RequestVerificationToken"]')?.value || '';
        const response = await fetch(config.savePersonalUrl || '/Patient/FamilyProfiles/SavePersonal', {
            method: 'POST',
            credentials: 'same-origin',
            headers: {
                'Content-Type': 'application/json',
                RequestVerificationToken: csrf,
            },
            body: JSON.stringify(payload),
        });

        const body = await response.json();
        if (!response.ok) throw new Error(body.message || 'Không thể lưu thông tin.');

        s.selfProfile = {
            ...(s.selfProfile || {}),
            ...payload,
        };
        s.savePersonalMessage = 'Cập nhật thông tin cá nhân thành công.';
    } catch (err) {
        s.error = err.message;
    } finally {
        s.savingPersonal = false;
        render();
    }
}

function openAddRelativeModal() {
    openModal(
        'Thông tin người thân',
        `
        <form id="addRelativeForm" onsubmit="event.preventDefault(); submitAddRelative();">
            <div class="form-grid">
                <div class="field full">
                    <label for="rel-name">Họ và tên *</label>
                    <input id="rel-name" placeholder="Nhập họ và tên" required minlength="2" maxlength="100" />
                </div>
                <div class="field">
                    <label for="rel-gender">Giới tính *</label>
                    <select id="rel-gender" required>
                        <option value="0">Nam</option>
                        <option value="1">Nữ</option>
                        <option value="2">Khác</option>
                    </select>
                </div>
                <div class="field">
                    <label for="rel-relation">Mối quan hệ *</label>
                    <select id="rel-relation" required>
                        <option value="2">Con</option>
                        <option value="1">Vợ / Chồng</option>
                        <option value="3">Bố / Mẹ</option>
                        <option value="4">Anh / Chị / Em</option>
                        <option value="5">Khác</option>
                    </select>
                </div>
                <div class="field">
                    <label for="rel-dob">Ngày sinh *</label>
                    <input id="rel-dob" type="date" required />
                </div>
                <div class="field">
                    <label for="rel-phone">Số điện thoại *</label>
                    <input id="rel-phone" type="tel" placeholder="Nhập số điện thoại" pattern="(0[0-9]{9}|[+]84[0-9]{9})" required />
                </div>
                <div class="field full">
                    <label for="rel-email">Địa chỉ email</label>
                    <input id="rel-email" type="email" placeholder="Nhập địa chỉ email" />
                </div>
                <div class="field full">
                    <label for="rel-code">Mã y tế (nếu có)</label>
                    <input id="rel-code" placeholder="Nhập mã y tế" />
                </div>
            </div>
            <p class="text-muted small mt-2">
                Bằng việc nhấn vào nút <strong>+ Thêm</strong>, bạn đã đồng ý với <a href="#/public/guide" target="_blank">Chính sách bảo vệ dữ liệu cá nhân</a> của MediCare.
            </p>
            <div id="relativeModalError" class="account-alert error" style="display:none; margin-top:10px;"></div>
        </form>
        `,
        `
        <button class="btn outline" type="button" onclick="closeModal()">Huỷ</button>
        <button class="btn primary" type="button" onclick="submitAddRelative()">Thêm</button>
        `,
    );
}

let isSubmittingRelative = false;
async function submitAddRelative() {
    if (isSubmittingRelative) return;
    const nameEl = document.getElementById('rel-name');
    const genderEl = document.getElementById('rel-gender');
    const relationEl = document.getElementById('rel-relation');
    const dobEl = document.getElementById('rel-dob');
    const phoneEl = document.getElementById('rel-phone');
    const emailEl = document.getElementById('rel-email');
    const codeEl = document.getElementById('rel-code');
    const errEl = document.getElementById('relativeModalError');
    const submitBtn = document.querySelector('#modal [data-action="save"]') || document.querySelector('#modal .btn.primary');

    if (!nameEl || !dobEl || !phoneEl) return;
    if (!nameEl.value.trim() || !dobEl.value || !phoneEl.value.trim()) {
        if (errEl) {
            errEl.textContent = 'Vui lòng điền đủ các thông tin bắt buộc (*).';
            errEl.style.display = 'block';
        }
        return;
    }

    const payload = {
        fullName: nameEl.value.trim(),
        gender: Number(genderEl.value),
        relationship: Number(relationEl.value),
        dateOfBirth: dobEl.value,
        phoneNumber: phoneEl.value.trim(),
        email: emailEl?.value.trim() || null,
        medicalCode: codeEl?.value.trim() || null,
    };

    isSubmittingRelative = true;
    if (submitBtn) submitBtn.disabled = true;

    try {
        const csrf = document.querySelector('[name="__RequestVerificationToken"]')?.value || '';
        const response = await fetch(config.addRelativeUrl || '/Patient/FamilyProfiles/AddRelative', {
            method: 'POST',
            credentials: 'same-origin',
            headers: {
                'Content-Type': 'application/json',
                RequestVerificationToken: csrf,
            },
            body: JSON.stringify(payload),
        });

        const body = await response.json();
        if (!response.ok) throw new Error(body.message || 'Không thể thêm người thân.');

        closeModal();
        patientProfilesState.loaded = false;
        ensurePatientProfiles();
    } catch (err) {
        if (errEl) {
            errEl.textContent = err.message;
            errEl.style.display = 'block';
        }
    } finally {
        isSubmittingRelative = false;
        if (submitBtn) submitBtn.disabled = false;
    }
}

function appointmentsPage() {
    return `${heading('Lịch hẹn của tôi', 'Theo dõi trạng thái lịch khám và thông tin tiếp nhận.', link('Đặt lịch khám', 'public/booking', 'plus', 'btn primary'))}
        ${unavailableNotice()}${pendingTable('Danh sách lịch hẹn', ['Mã lịch', 'Người khám', 'Bác sĩ / phòng', 'Ngày giờ', 'Trạng thái'])}`;
}

function recordsPage() {
    return `${heading('Hồ sơ sức khỏe', 'Bệnh án, kết quả và đơn thuốc được cung cấp theo quyền truy cập của tài khoản.')}
        ${unavailableNotice()}${pendingTable('Hồ sơ khám bệnh', ['Ngày khám', 'Người khám', 'Chuyên khoa', 'Bác sĩ', 'Kết quả'])}`;
}

function notificationsPage() {
    return `${heading('Thông báo', 'Theo dõi cập nhật về lịch hẹn và hồ sơ của bạn.', unavailableButton('Đánh dấu đã đọc', 'check'))}
        ${unavailableNotice()}${pendingTable('Thông báo của bạn', ['Thời gian', 'Nội dung', 'Trạng thái'])}`;
}

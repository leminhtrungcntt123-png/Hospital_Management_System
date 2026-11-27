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

const patientRecordsState = {
    status: 'idle', // idle | loading | ready | error
    profiles: [],
    items: [],
    selectedPatientId: '',
    error: '',
};

function ensurePatientRecords() {
    if (!config.authenticated || patientRecordsState.status !== 'idle') return;
    loadPatientRecords();
}

async function loadPatientRecords(patientId = patientRecordsState.selectedPatientId) {
    patientRecordsState.status = 'loading';
    patientRecordsState.error = '';
    patientRecordsState.selectedPatientId = patientId ? String(patientId) : '';
    render();
    try {
        const baseUrl = config.medicalRecordsUrl || '/Patient/MedicalRecords/List';
        const url = patientRecordsState.selectedPatientId
            ? `${baseUrl}?patientId=${encodeURIComponent(patientRecordsState.selectedPatientId)}`
            : baseUrl;
        const res = await fetch(url, {
            credentials: 'same-origin',
            cache: 'no-store',
            headers: { Accept: 'application/json' },
        });
        const body = await res.json();
        if (!res.ok) throw new Error(body.message || 'Không tải được hồ sơ sức khỏe.');
        patientRecordsState.profiles = Array.isArray(body.profiles) ? body.profiles : [];
        patientRecordsState.items = Array.isArray(body.items) ? body.items : [];
        patientRecordsState.status = 'ready';
    } catch (err) {
        patientRecordsState.status = 'error';
        patientRecordsState.error = err.message;
    }
    render();
}

function openMedicalRecordDetailModal(recordId) {
    const rec = patientRecordsState.items.find((r) => Number(r.id) === Number(recordId));
    if (!rec) return;

    const formatDateOnly = (val) => (typeof formatPortalDate === 'function' ? formatPortalDate(val) : String(val || '—'));
    const formatDateTime = (val) => (typeof formatPortalDateTime === 'function' ? formatPortalDateTime(val) : String(val || '—'));

    const diagnosisTitle = [rec.primaryDiagnosisCode, rec.primaryDiagnosisDesc].filter(Boolean).join(' – ') || 'Chưa cập nhật';

    let vitalsHtml = '';
    if (rec.vitalSigns) {
        const v = rec.vitalSigns;
        const vItems = [
            v.pulse != null ? `<span><strong>Mạch:</strong> ${esc(v.pulse)} lần/phút</span>` : '',
            v.systolicBp != null && v.diastolicBp != null ? `<span><strong>Huyết áp:</strong> ${esc(v.systolicBp)}/${esc(v.diastolicBp)} mmHg</span>` : '',
            v.temperature != null ? `<span><strong>Nhiệt độ:</strong> ${esc(v.temperature)} °C</span>` : '',
            v.respiratoryRate != null ? `<span><strong>Nhịp thở:</strong> ${esc(v.respiratoryRate)} lần/phút</span>` : '',
            v.spo2 != null ? `<span><strong>SpO2:</strong> ${esc(v.spo2)}%</span>` : '',
            v.weight != null ? `<span><strong>Cân nặng:</strong> ${esc(v.weight)} kg</span>` : '',
            v.height != null ? `<span><strong>Chiều cao:</strong> ${esc(v.height)} cm</span>` : '',
        ].filter(Boolean);
        if (vItems.length) {
            vitalsHtml = `
                <div style="margin-bottom:14px;">
                    <h4 style="margin:0 0 8px 0; color:#007d9f; font-size:14px;">Chỉ số sinh hiệu</h4>
                    <div style="display:flex; flex-wrap:wrap; gap:8px 16px; background:#f7fbfd; border:1px solid #d9ecf4; border-radius:8px; padding:10px 12px; font-size:13px;">
                        ${vItems.join('')}
                    </div>
                </div>`;
        }
    }

    const prescriptions = Array.isArray(rec.prescriptions) ? rec.prescriptions : [];
    const prescriptionsHtml = prescriptions.length
        ? prescriptions
              .map(
                  (p) => `
            <div style="margin-bottom:12px;">
                <div style="font-size:13px; color:#4b677a; margin-bottom:6px;">
                    <strong>Ngày kê đơn:</strong> ${esc(formatDateOnly(p.prescribedDate))}
                    ${p.note ? ` · <strong>Lời dặn:</strong> ${esc(p.note)}` : ''}
                </div>
                <div class="table-wrap">
                    <table style="font-size:13px;">
                        <thead>
                            <tr>
                                <th>Tên thuốc</th>
                                <th>Số lượng</th>
                                <th>Liều dùng</th>
                                <th>Số ngày</th>
                                <th>Cách dùng</th>
                            </tr>
                        </thead>
                        <tbody>
                            ${(p.items || [])
                                .map(
                                    (m) => `<tr>
                                <td>
                                    <strong>${esc(m.medicationName)}</strong>
                                    ${m.activeIngredient || m.strength ? `<br /><small class="text-muted">${esc([m.activeIngredient, m.strength].filter(Boolean).join(' · '))}</small>` : ''}
                                </td>
                                <td>${esc(m.quantity)} ${esc(m.unit || '')}</td>
                                <td>${esc(m.dosage || '—')}</td>
                                <td>${m.durationDays != null ? esc(m.durationDays) + ' ngày' : '—'}</td>
                                <td>${esc(m.usageInstructions || '—')}</td>
                            </tr>`,
                                )
                                .join('')}
                        </tbody>
                    </table>
                </div>
            </div>`,
              )
              .join('')
        : '<p class="text-muted" style="font-size:13px; margin:0;">Không có đơn thuốc trong lượt khám này.</p>';

    const labOrders = Array.isArray(rec.labOrders) ? rec.labOrders : [];
    const labsHtml = labOrders.length
        ? `<div style="margin-top:14px;">
            <h4 style="margin:0 0 8px 0; color:#007d9f; font-size:14px;">Chỉ định & Kết quả cận lâm sàng</h4>
            <div class="table-wrap">
                <table style="font-size:13px;">
                    <thead>
                        <tr>
                            <th>Dịch vụ</th>
                            <th>Trạng thái</th>
                            <th>Kết quả</th>
                        </tr>
                    </thead>
                    <tbody>
                        ${labOrders
                            .map(
                                (o) => `<tr>
                            <td><strong>${esc(o.serviceName)}</strong>${o.note ? `<br /><small class="text-muted">${esc(o.note)}</small>` : ''}</td>
                            <td><span class="badge">${esc(o.status)}</span></td>
                            <td>${o.result ? esc(o.result) : '<span class="text-muted">Chưa có kết quả</span>'}</td>
                        </tr>`,
                            )
                            .join('')}
                    </tbody>
                </table>
            </div>
        </div>`
        : '';

    const bodyHtml = `
        <div style="background:#f0f8fb; border:1px solid #d5eaf3; border-radius:10px; padding:12px 14px; margin-bottom:14px; font-size:13.5px;">
            <div style="display:grid; grid-template-columns:repeat(auto-fit, minmax(200px, 1fr)); gap:6px 14px;">
                <div><strong>Mã lịch hẹn:</strong> <span style="color:#007d9f;">${esc(rec.appointmentCode)}</span></div>
                <div><strong>Ngày khám:</strong> ${esc(formatDateTime(rec.examDate))}</div>
                <div><strong>Người khám:</strong> ${esc(rec.patientName)}</div>
                <div><strong>Bác sĩ khám:</strong> ${esc(rec.doctorName)}</div>
                <div><strong>Chuyên khoa:</strong> ${esc(rec.departmentName)}</div>
                <div><strong>Phòng khám:</strong> ${esc(rec.roomNumber)}</div>
            </div>
        </div>
        ${vitalsHtml}
        <div style="margin-bottom:14px;">
            <h4 style="margin:0 0 8px 0; color:#007d9f; font-size:14px;">Kết quả khám của bác sĩ</h4>
            <dl style="display:grid; grid-template-columns:150px 1fr; gap:6px 12px; margin:0; font-size:13.5px;">
                <dt style="color:#4b677a; font-weight:600; margin:0;">Triệu chứng</dt>
                <dd style="margin:0;">${esc(rec.symptoms || 'Không ghi nhận')}</dd>
                <dt style="color:#4b677a; font-weight:600; margin:0;">Khám lâm sàng</dt>
                <dd style="margin:0;">${esc(rec.clinicalExamination || 'Không ghi nhận')}</dd>
                <dt style="color:#4b677a; font-weight:600; margin:0;">Chẩn đoán chính</dt>
                <dd style="margin:0;"><strong>${esc(diagnosisTitle)}</strong></dd>
                ${
                    rec.secondaryDiagnosis
                        ? `<dt style="color:#4b677a; font-weight:600; margin:0;">Chẩn đoán phụ</dt><dd style="margin:0;">${esc(rec.secondaryDiagnosis)}</dd>`
                        : ''
                }
                <dt style="color:#4b677a; font-weight:600; margin:0;">Kết luận & dặn dò</dt>
                <dd style="margin:0;">${esc(rec.conclusion || 'Không có')}</dd>
                ${
                    rec.followUpDate
                        ? `<dt style="color:#4b677a; font-weight:600; margin:0;">Ngày tái khám</dt><dd style="margin:0;"><strong style="color:#007d9f;">${esc(formatDateOnly(rec.followUpDate))}</strong></dd>`
                        : ''
                }
            </dl>
        </div>
        <div>
            <h4 style="margin:0 0 8px 0; color:#007d9f; font-size:14px;">Đơn thuốc</h4>
            ${prescriptionsHtml}
        </div>
        ${labsHtml}
    `;

    openModal(
        `Hồ sơ khám bệnh · ${rec.appointmentCode}`,
        bodyHtml,
        `<button type="button" class="btn primary" onclick="closeModal()">Đóng</button>`,
    );
}

function recordsPage() {
    ensurePatientRecords();
    const s = patientRecordsState;
    const formatDateTime = (val) => (typeof formatPortalDateTime === 'function' ? formatPortalDateTime(val) : String(val || '—'));

    const profileFilter =
        s.profiles.length > 1
            ? `<select aria-label="Lọc theo người khám" onchange="loadPatientRecords(this.value)" style="min-width:210px;">
                <option value="" ${!s.selectedPatientId ? 'selected' : ''}>Tất cả người khám (${s.profiles.length})</option>
                ${s.profiles
                    .map(
                        (p) =>
                            `<option value="${esc(p.patientId)}" ${s.selectedPatientId === String(p.patientId) ? 'selected' : ''}>${esc(p.fullName)} (${esc(p.relationshipLabel)})</option>`,
                    )
                    .join('')}
            </select>`
            : '';

    const headerActions = `<div style="display:flex; gap:8px; align-items:center; flex-wrap:wrap;">
        ${profileFilter}
        <button type="button" class="btn outline" onclick="loadPatientRecords()">${icon('refresh')}Làm mới</button>
    </div>`;

    let content = '';
    if (s.status === 'loading') {
        content = `<section class="card"><p style="padding:20px;">Đang tải hồ sơ sức khỏe…</p></section>`;
    } else if (s.status === 'error') {
        content = `<div class="notice error" role="alert">${esc(s.error)} <button type="button" class="btn outline" onclick="loadPatientRecords()">Thử lại</button></div>`;
    } else if (!s.items.length) {
        content = `<section class="card">
            <header class="card-header"><h2>Hồ sơ khám bệnh</h2>${headerActions}</header>
            ${empty('Chưa có hồ sơ khám bệnh nào', 'Kết quả khám, chẩn đoán và đơn thuốc sẽ tự động hiển thị tại đây sau khi bác sĩ hoàn tất khám bệnh.', link('Đặt lịch khám', 'public/booking', 'calendar', 'btn primary'))}
        </section>`;
    } else {
        content = `<section class="card">
            <header class="card-header">
                <h2>Hồ sơ khám bệnh (${s.items.length})</h2>
                ${headerActions}
            </header>
            <div class="table-wrap"><table><thead><tr>
                <th scope="col">Ngày khám</th>
                <th scope="col">Người khám</th>
                <th scope="col">Chuyên khoa</th>
                <th scope="col">Bác sĩ</th>
                <th scope="col">Kết quả / Chẩn đoán</th>
                <th scope="col">Thao tác</th>
            </tr></thead><tbody>
                ${s.items
                    .map((r) => {
                        const diag = [r.primaryDiagnosisCode, r.primaryDiagnosisDesc].filter(Boolean).join(' – ') || r.conclusion || 'Đã hoàn tất khám';
                        const rxCount = (r.prescriptions || []).reduce((acc, p) => acc + (p.items ? p.items.length : 0), 0);
                        const labCount = (r.labOrders || []).length;
                        const subTags = [
                            rxCount > 0 ? `<span class="badge ok">${rxCount} thuốc</span>` : '',
                            labCount > 0 ? `<span class="badge">${labCount} cận lâm sàng</span>` : '',
                        ]
                            .filter(Boolean)
                            .join(' ');
                        return `<tr>
                            <td>
                                <strong>${esc(formatDateTime(r.examDate))}</strong><br />
                                <small class="text-muted">${esc(r.appointmentCode)}</small>
                            </td>
                            <td>${esc(r.patientName)}</td>
                            <td>${esc(r.departmentName)}<br /><small class="text-muted">Phòng ${esc(r.roomNumber)}</small></td>
                            <td>${esc(r.doctorName)}</td>
                            <td>
                                <div>${esc(diag)}</div>
                                ${subTags ? `<div style="margin-top:4px; display:flex; gap:4px; flex-wrap:wrap;">${subTags}</div>` : ''}
                            </td>
                            <td>
                                <button type="button" class="btn outline" onclick="openMedicalRecordDetailModal(${r.id})">${icon('eye')}Xem kết quả</button>
                            </td>
                        </tr>`;
                    })
                    .join('')}
            </tbody></table></div>
        </section>`;
    }

    return `${heading('Hồ sơ sức khỏe', 'Bệnh án, kết quả khám và đơn thuốc trực tiếp từ bác sĩ sau khi hoàn tất khám.')}${content}`;
}

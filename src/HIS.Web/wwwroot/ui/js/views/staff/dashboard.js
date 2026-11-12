/* views / staff / dashboard */

let nurseVitalsState = {
    loaded: false,
    loading: false,
    error: '',
    successMessage: '',
    queue: [],
};

let nurseDashboardState = {
    loaded: false,
    loading: false,
    error: '',
    summary: null,
};

let nursePatientsState = {
    loaded: false,
    loading: false,
    error: '',
    patients: [],
    keyword: '',
};

function getCsrfToken() {
    return (
        document.querySelector('#his-server-logout input[name="__RequestVerificationToken"]')?.value ||
        document.querySelector('input[name="__RequestVerificationToken"]')?.value ||
        ''
    );
}

// --- API FETCHERS ---

async function loadVitalsQueue() {
    nurseVitalsState.loading = true;
    try {
        const res = await fetch('/api/staff/workspace/VitalsQueue?includeRecorded=true', {
            credentials: 'same-origin',
            headers: { Accept: 'application/json' },
        });
        if (!res.ok) throw new Error('Không thể tải danh sách chờ sinh hiệu.');
        nurseVitalsState.queue = await res.json();
        nurseVitalsState.loaded = true;
        nurseVitalsState.error = '';
    } catch (e) {
        nurseVitalsState.error = e.message;
        nurseVitalsState.loaded = true;
    } finally {
        nurseVitalsState.loading = false;
        render();
    }
}

async function loadDashboardSummary() {
    nurseDashboardState.loading = true;
    try {
        const res = await fetch('/api/staff/workspace/Dashboard', {
            credentials: 'same-origin',
            headers: { Accept: 'application/json' },
        });
        if (!res.ok) throw new Error('Không thể tải tổng quan bàn làm việc.');
        nurseDashboardState.summary = await res.json();
        nurseDashboardState.loaded = true;
        nurseDashboardState.error = '';
    } catch (e) {
        nurseDashboardState.error = e.message;
        nurseDashboardState.loaded = true;
    } finally {
        nurseDashboardState.loading = false;
        render();
    }
}

async function loadNursePatients(keyword = '') {
    nursePatientsState.loading = true;
    nursePatientsState.keyword = keyword;
    try {
        const query = keyword ? `?keyword=${encodeURIComponent(keyword)}` : '';
        const res = await fetch(`/api/staff/workspace/Patients${query}`, {
            credentials: 'same-origin',
            headers: { Accept: 'application/json' },
        });
        if (!res.ok) throw new Error('Không thể tải danh sách hồ sơ người khám.');
        nursePatientsState.patients = await res.json();
        nursePatientsState.loaded = true;
        nursePatientsState.error = '';
    } catch (e) {
        nursePatientsState.error = e.message;
        nursePatientsState.loaded = true;
    } finally {
        nursePatientsState.loading = false;
        render();
    }
}

// --- 1. MÀN HÌNH BÀN LÀM VIỆC (DASHBOARD) ---

function staffDashboard() {
    if (!nurseDashboardState.loaded && !nurseDashboardState.loading) {
        loadDashboardSummary();
    }

    const reception =
        config.role === 'NURSE'
            ? link('Mở quầy tiếp nhận', 'staff/reception', 'users', 'btn primary')
            : '';

    const headingHtml = heading(
        config.role === 'DEPARTMENT_HEAD' ? 'Tổng quan khoa' : 'Bàn làm việc',
        'Theo dõi công việc và các lượt khám trong khoa.',
        reception,
    );

    if (nurseDashboardState.loading && !nurseDashboardState.loaded) {
        return `${headingHtml}<div class="loading-state" style="padding:40px; text-align:center;">Đang tải dữ liệu bàn làm việc...</div>`;
    }

    if (nurseDashboardState.error) {
        return `${headingHtml}<div class="alert error">${esc(nurseDashboardState.error)}</div>`;
    }

    const s = nurseDashboardState.summary || {
        departmentName: '',
        totalToday: 0,
        waitingProcess: 0,
        waitingVitalsCount: 0,
        readyForDoctorCount: 0,
        inProgressCount: 0,
        completedCount: 0,
        todayTasks: [],
    };

    const statsHtml = `<div class="kpi-grid" style="display:grid; grid-template-columns:repeat(auto-fit, minmax(200px, 1fr)); gap:16px; margin:20px 0;">
        <div class="card stat-card" style="padding:16px; background:#fff; border:1px solid #e2e8f0; border-radius:8px;">
            <div style="font-size:12px; color:#64748b; font-weight:600;">LỊCH KHÁM HÔM NAY</div>
            <div style="font-size:28px; font-weight:700; color:#0f172a; margin:6px 0;">${s.totalToday}</div>
            <div style="font-size:11px; color:#64748b;">Tổng lượt khám tại khoa</div>
        </div>
        <div class="card stat-card" style="padding:16px; background:#fff; border:1px solid #e2e8f0; border-radius:8px;">
            <div style="font-size:12px; color:#d97706; font-weight:600;">CHỜ XỬ LÝ</div>
            <div style="font-size:28px; font-weight:700; color:#d97706; margin:6px 0;">${s.waitingProcess}</div>
            <div style="font-size:11px; color:#92400e;">Chờ đo: <strong>${s.waitingVitalsCount}</strong> · Chờ khám: <strong>${s.readyForDoctorCount}</strong></div>
        </div>
        <div class="card stat-card" style="padding:16px; background:#fff; border:1px solid #e2e8f0; border-radius:8px;">
            <div style="font-size:12px; color:#2563eb; font-weight:600;">ĐANG KHÁM</div>
            <div style="font-size:28px; font-weight:700; color:#2563eb; margin:6px 0;">${s.inProgressCount}</div>
            <div style="font-size:11px; color:#64748b;">Bác sĩ đang khám</div>
        </div>
        <div class="card stat-card" style="padding:16px; background:#fff; border:1px solid #e2e8f0; border-radius:8px;">
            <div style="font-size:12px; color:#16a34a; font-weight:600;">HOÀN THÀNH</div>
            <div style="font-size:28px; font-weight:700; color:#16a34a; margin:6px 0;">${s.completedCount}</div>
            <div style="font-size:11px; color:#64748b;">Đã có hồ sơ bệnh án</div>
        </div>
    </div>`;

    const tasks = s.todayTasks || [];
    let rowsHtml = '';
    if (tasks.length === 0) {
        rowsHtml = `<tr><td colspan="5" style="text-align:center; padding:30px; color:#64748b;">Hiện không có ca khám nào cần xử lý trong khoa.</td></tr>`;
    } else {
        rowsHtml = tasks
            .map((t) => {
                let statusBadge = '';
                let actionBtn = '';
                if (t.statusId === 3) {
                    statusBadge = `<span class="badge" style="background:#fef3c7; color:#92400e; padding:3px 8px; border-radius:4px; font-size:11px;">Cần đo sinh hiệu</span>`;
                    actionBtn = `<button type="button" class="btn primary sm" data-act="open-vitals-modal" data-app-id="${t.appointmentId}">Đo sinh hiệu</button>`;
                } else if (t.statusId === 2) {
                    statusBadge = `<span class="badge" style="background:#f1f5f9; color:#475569; padding:3px 8px; border-radius:4px; font-size:11px;">Chờ quầy tiếp nhận</span>`;
                    actionBtn = `<a href="${esc(routeUrl('staff/reception'))}" class="btn outline sm">Tiếp nhận</a>`;
                } else if (t.statusId === 4) {
                    statusBadge = `<span class="badge" style="background:#e0f2fe; color:#0369a1; padding:3px 8px; border-radius:4px; font-size:11px;">Sẵn sàng khám</span>`;
                    actionBtn = `<button type="button" class="btn text-link sm" data-act="open-vitals-modal" data-app-id="${t.appointmentId}">Sửa sinh hiệu</button>`;
                } else if (t.statusId === 5) {
                    statusBadge = `<span class="badge" style="background:#dcfce7; color:#166534; padding:3px 8px; border-radius:4px; font-size:11px;">Đang khám</span>`;
                    actionBtn = `<span style="font-size:12px; color:#64748b;">Trong phòng khám</span>`;
                }

                return `<tr>
                <td><strong>${esc(t.queueNumber || '—')}</strong><br/><small style="color:#64748b;">${esc(t.appointmentCode)}</small></td>
                <td><strong>${esc(t.patientName)}</strong><br/><small style="color:#64748b;">${esc(t.gender)} · ${esc(t.phoneNumber)}</small></td>
                <td>${esc(t.doctorName)}<br/><small style="color:#64748b;">Phòng ${esc(t.roomName || '—')}</small></td>
                <td>${statusBadge}</td>
                <td>${actionBtn}</td>
            </tr>`;
            })
            .join('');
    }

    const tableHtml = `<div class="card" style="background:#fff; border:1px solid #e2e8f0; border-radius:8px; margin-top:20px; overflow:hidden;">
        <div style="padding:16px 20px; border-bottom:1px solid #e2e8f0; display:flex; justify-content:space-between; align-items:center;">
            <h3 style="margin:0; font-size:16px; font-weight:700;">Công việc hôm nay</h3>
            <button type="button" class="btn outline sm" data-act="refresh-dashboard">${icon('trend')} Làm mới</button>
        </div>
        <div class="table-responsive">
            <table class="table" style="width:100%; border-collapse:collapse;">
                <thead>
                    <tr style="background:#f8fafc; border-bottom:1px solid #e2e8f0; text-align:left; font-size:12px; color:#475569;">
                        <th style="padding:10px 16px;">STT / Mã</th>
                        <th style="padding:10px 16px;">Người khám</th>
                        <th style="padding:10px 16px;">Bác sĩ / Phòng</th>
                        <th style="padding:10px 16px;">Trạng thái</th>
                        <th style="padding:10px 16px;">Thao tác</th>
                    </tr>
                </thead>
                <tbody>${rowsHtml}</tbody>
            </table>
        </div>
    </div>`;

    return `${headingHtml}${statsHtml}${tableHtml}`;
}

// --- 2. MÀN HÌNH SINH HIỆU (VITALS) ---

function vitalsPage() {
    if (!nurseVitalsState.loaded && !nurseVitalsState.loading) {
        loadVitalsQueue();
    }

    const refreshBtn = `<button type="button" class="btn outline" data-act="refresh-vitals">${icon('pulse')} Tải lại hàng đợi</button>`;
    const headingHtml = heading(
        'Sinh hiệu',
        'Ghi nhận và cập nhật sinh hiệu cho người khám đã được tiếp nhận trong khoa.',
        refreshBtn,
    );

    let alertHtml = '';
    if (nurseVitalsState.successMessage) {
        alertHtml = `<div class="alert success" style="padding:12px 16px; background:#dcfce7; color:#166534; border-radius:6px; margin:12px 0;">${esc(nurseVitalsState.successMessage)}</div>`;
    }
    if (nurseVitalsState.error) {
        alertHtml = `<div class="alert error" style="padding:12px 16px; background:#fee2e2; color:#991b1b; border-radius:6px; margin:12px 0;">${esc(nurseVitalsState.error)}</div>`;
    }

    if (nurseVitalsState.loading && !nurseVitalsState.loaded) {
        return `${headingHtml}${alertHtml}<div class="loading-state" style="padding:40px; text-align:center;">Đang tải danh sách chờ sinh hiệu...</div>`;
    }

    const queue = nurseVitalsState.queue || [];
    let rowsHtml = '';
    if (queue.length === 0) {
        rowsHtml = `<tr><td colspan="7" style="text-align:center; padding:36px; color:#64748b;">Hiện không có ca khám nào cần đo sinh hiệu trong khoa hôm nay.</td></tr>`;
    } else {
        rowsHtml = queue
            .map((item) => {
                const isMeasured = item.hasVitalSigns && item.statusId === 4;
                const badge = isMeasured
                    ? `<span class="badge" style="background:#e0f2fe; color:#0369a1; padding:3px 8px; border-radius:4px; font-size:11px;">Đã đo (Chờ BS)</span>`
                    : `<span class="badge" style="background:#fef3c7; color:#92400e; padding:3px 8px; border-radius:4px; font-size:11px;">Chờ đo sinh hiệu</span>`;

                let vitalsSummary = '—';
                if (item.vitalSigns) {
                    const v = item.vitalSigns;
                    vitalsSummary = `<small style="font-size:11px; color:#334155; line-height:1.4; display:block;">
                        Mạch: <strong>${esc(v.pulse)}</strong> l/p · HA: <strong>${esc(v.systolicBp)}/${esc(v.diastolicBp)}</strong><br/>
                        Nhiệt độ: <strong>${esc(v.temperature)}</strong>°C · Nặng: <strong>${esc(v.weight)}</strong>kg
                        ${v.spo2 ? ` · SpO₂: <strong>${esc(v.spo2)}%</strong>` : ''}
                    </small>`;
                }

                const action = isMeasured
                    ? `<button type="button" class="btn outline sm" data-act="open-vitals-modal" data-app-id="${item.appointmentId}">Sửa chỉ số</button>`
                    : `<button type="button" class="btn primary sm" data-act="open-vitals-modal" data-app-id="${item.appointmentId}">${icon('pulse')} Ghi sinh hiệu</button>`;

                return `<tr>
                <td><strong style="font-size:14px; color:#079bc9;">${esc(item.queueNumber || '—')}</strong></td>
                <td><strong>${esc(item.appointmentCode)}</strong></td>
                <td><strong>${esc(item.patientName)}</strong><br/><small style="color:#64748b;">${esc(item.gender)} · ${esc(item.phoneNumber)}</small></td>
                <td>${esc(item.doctorName)}<br/><small style="color:#64748b;">Phòng ${esc(item.roomName || '—')}</small></td>
                <td>${badge}</td>
                <td>${vitalsSummary}</td>
                <td>${action}</td>
            </tr>`;
            })
            .join('');
    }

    const tableHtml = `<div class="card" style="background:#fff; border:1px solid #e2e8f0; border-radius:8px; margin-top:16px; overflow:hidden;">
        <div style="padding:14px 20px; border-bottom:1px solid #e2e8f0; background:#f8fafc;">
            <h3 style="margin:0; font-size:15px; font-weight:700;">Danh sách chờ đo sinh hiệu (${queue.length})</h3>
        </div>
        <div class="table-responsive">
            <table class="table" style="width:100%; border-collapse:collapse;">
                <thead>
                    <tr style="background:#f8fafc; border-bottom:1px solid #e2e8f0; text-align:left; font-size:12px; color:#475569;">
                        <th style="padding:10px 14px;">Số thứ tự</th>
                        <th style="padding:10px 14px;">Mã lịch</th>
                        <th style="padding:10px 14px;">Người khám</th>
                        <th style="padding:10px 14px;">Bác sĩ / Phòng</th>
                        <th style="padding:10px 14px;">Trạng thái</th>
                        <th style="padding:10px 14px;">Chỉ số đã đo</th>
                        <th style="padding:10px 14px;">Thao tác</th>
                    </tr>
                </thead>
                <tbody>${rowsHtml}</tbody>
            </table>
        </div>
    </div>`;

    return `${headingHtml}${alertHtml}${tableHtml}`;
}

// --- MODAL GHI / SỬA SINH HIỆU ---

async function openVitalsModal(appointmentId) {
    appointmentId = parseInt(appointmentId);
    let item = nurseVitalsState.queue.find((q) => q.appointmentId === appointmentId);
    if (!item) {
        // Mở từ Bàn làm việc: hàng đợi sinh hiệu chưa được tải nên phải tải trước
        await loadVitalsQueue();
        item = nurseVitalsState.queue.find((q) => q.appointmentId === appointmentId);
    }
    if (!item) {
        alert('Không tìm thấy thông tin lượt khám trong hàng đợi.');
        return;
    }

    const v = item.vitalSigns || {};
    const isEdit = item.hasVitalSigns && item.statusId === 4;

    const modalBody = `<form id="vitals-modal-form" style="display:flex; flex-direction:column; gap:14px;">
        <div style="padding:10px 14px; background:#f1f5f9; border-radius:6px; font-size:13px; line-height:1.5;">
            Bệnh nhân: <strong>${esc(item.patientName)}</strong> · STT: <strong style="color:#079bc9;">${esc(item.queueNumber || '—')}</strong><br/>
            Mã lịch: ${esc(item.appointmentCode)} · Giới tính: ${esc(item.gender)}
        </div>

        <div style="display:grid; grid-template-columns:1fr 1fr; gap:12px;">
            <div class="field">
                <label for="v-pulse" style="font-weight:600; font-size:12px;">Mạch (lần/phút) <span style="color:red;">*</span></label>
                <input id="v-pulse" name="Pulse" type="number" min="30" max="220" class="form-control" placeholder="60 - 100" value="${esc(v.pulse || '')}" required />
            </div>
            <div class="field">
                <label for="v-temp" style="font-weight:600; font-size:12px;">Thân nhiệt (°C) <span style="color:red;">*</span></label>
                <input id="v-temp" name="Temperature" type="number" step="0.1" min="34.0" max="43.0" class="form-control" placeholder="36.5" value="${esc(v.temperature || '')}" required />
            </div>
        </div>

        <div style="display:grid; grid-template-columns:1fr 1fr; gap:12px;">
            <div class="field">
                <label for="v-sys" style="font-weight:600; font-size:12px;">Huyết áp tâm thu (mmHg) <span style="color:red;">*</span></label>
                <input id="v-sys" name="SystolicBp" type="number" min="50" max="260" class="form-control" placeholder="120" value="${esc(v.systolicBp || '')}" required />
            </div>
            <div class="field">
                <label for="v-dia" style="font-weight:600; font-size:12px;">Huyết áp tâm trương (mmHg) <span style="color:red;">*</span></label>
                <input id="v-dia" name="DiastolicBp" type="number" min="30" max="160" class="form-control" placeholder="80" value="${esc(v.diastolicBp || '')}" required />
            </div>
        </div>

        <div style="display:grid; grid-template-columns:1fr 1fr; gap:12px;">
            <div class="field">
                <label for="v-weight" style="font-weight:600; font-size:12px;">Cân nặng (kg) <span style="color:red;">*</span></label>
                <input id="v-weight" name="Weight" type="number" step="0.1" min="1.0" max="300.0" class="form-control" placeholder="60.0" value="${esc(v.weight || '')}" required oninput="calcBmiPreview()" />
            </div>
            <div class="field">
                <label for="v-height" style="font-weight:600; font-size:12px;">Chiều cao (cm)</label>
                <input id="v-height" name="Height" type="number" step="0.1" min="30.0" max="250.0" class="form-control" placeholder="165.0" value="${esc(v.height || '')}" oninput="calcBmiPreview()" />
            </div>
        </div>

        <div style="display:grid; grid-template-columns:1fr 1fr; gap:12px;">
            <div class="field">
                <label for="v-resp" style="font-weight:600; font-size:12px;">Nhịp thở (lần/phút)</label>
                <input id="v-resp" name="RespiratoryRate" type="number" min="8" max="60" class="form-control" placeholder="16 - 20" value="${esc(v.respiratoryRate || '')}" />
            </div>
            <div class="field">
                <label for="v-spo2" style="font-weight:600; font-size:12px;">SpO₂ (%)</label>
                <input id="v-spo2" name="Spo2" type="number" min="50" max="100" class="form-control" placeholder="95 - 100" value="${esc(v.spo2 || '')}" />
            </div>
        </div>

        <div id="bmi-box" style="padding:8px 12px; background:#f8fafc; border:1px solid #e2e8f0; border-radius:6px; font-size:12px; display:flex; justify-content:space-between; align-items:center;">
            <span>Chỉ số BMI tính nhẩm: <strong id="bmi-num">—</strong></span>
            <span id="bmi-status" style="font-weight:600;"></span>
        </div>

        <div id="modal-vitals-error" style="display:none; padding:8px 12px; background:#fee2e2; color:#991b1b; border-radius:6px; font-size:12px;"></div>
    </form>`;

    const modalFooter = `
        <button type="button" class="btn outline" data-act="close">Đóng</button>
        <button type="button" class="btn primary" id="btn-save-vitals" data-act="save-vitals" data-app-id="${appointmentId}">
            ${isEdit ? 'Cập nhật sinh hiệu' : 'Lưu &amp; Chuyển Bác sĩ'}
        </button>
    `;

    openModal(isEdit ? 'Cập nhật sinh hiệu' : 'Ghi nhận sinh hiệu', modalBody, modalFooter);
    calcBmiPreview();
}

function calcBmiPreview() {
    const w = parseFloat(document.getElementById('v-weight')?.value);
    const h = parseFloat(document.getElementById('v-height')?.value);
    const numEl = document.getElementById('bmi-num');
    const statusEl = document.getElementById('bmi-status');
    if (!numEl || !statusEl) return;

    if (w > 0 && h > 0) {
        const heightM = h / 100;
        const bmi = (w / (heightM * heightM)).toFixed(1);
        numEl.textContent = bmi;
        if (bmi < 18.5) {
            statusEl.textContent = 'Thiếu cân';
            statusEl.style.color = '#d97706';
        } else if (bmi <= 22.9) {
            statusEl.textContent = 'Bình thường';
            statusEl.style.color = '#16a34a';
        } else if (bmi <= 24.9) {
            statusEl.textContent = 'Tiền béo phì';
            statusEl.style.color = '#ea580c';
        } else {
            statusEl.textContent = 'Béo phì';
            statusEl.style.color = '#dc2626';
        }
    } else {
        numEl.textContent = '—';
        statusEl.textContent = '';
    }
}

async function saveVitalsForm(appointmentId) {
    appointmentId = parseInt(appointmentId);
    const currentItem = nurseVitalsState.queue.find((q) => q.appointmentId === appointmentId);
    const isEdit = !!currentItem && currentItem.hasVitalSigns && currentItem.statusId === 4;
    const pulse = parseInt(document.getElementById('v-pulse')?.value);
    const temp = parseFloat(document.getElementById('v-temp')?.value);
    const sys = parseInt(document.getElementById('v-sys')?.value);
    const dia = parseInt(document.getElementById('v-dia')?.value);
    const weight = parseFloat(document.getElementById('v-weight')?.value);
    const height = parseFloat(document.getElementById('v-height')?.value) || null;
    const resp = parseInt(document.getElementById('v-resp')?.value) || null;
    const spo2 = parseInt(document.getElementById('v-spo2')?.value) || null;

    const errBox = document.getElementById('modal-vitals-error');
    const showError = (msg) => {
        if (errBox) {
            errBox.textContent = msg;
            errBox.style.display = 'block';
        } else {
            alert(msg);
        }
    };

    if (isNaN(pulse) || isNaN(temp) || isNaN(sys) || isNaN(dia) || isNaN(weight)) {
        showError('Vui lòng điền đủ 5 chỉ số bắt buộc: Mạch, Thân nhiệt, Huyết áp tâm thu/tâm trương, Cân nặng.');
        return;
    }

    if (sys <= dia) {
        showError('Huyết áp tâm thu phải lớn hơn huyết áp tâm trương.');
        return;
    }

    if (sys - dia < 10) {
        showError('Hiệu áp (Tâm thu - Tâm trương) tối thiểu phải đạt 10 mmHg.');
        return;
    }

    const payload = {
        appointmentId,
        pulse,
        systolicBp: sys,
        diastolicBp: dia,
        temperature: temp,
        weight,
        height,
        respiratoryRate: resp,
        spo2,
    };

    const saveBtn = document.getElementById('btn-save-vitals');
    if (saveBtn) {
        saveBtn.disabled = true;
        saveBtn.textContent = 'Đang lưu...';
    }

    try {
        const token = getCsrfToken();
        const res = await fetch('/api/staff/workspace/SaveVitals', {
            method: 'POST',
            credentials: 'same-origin',
            headers: {
                'Content-Type': 'application/json',
                RequestVerificationToken: token,
            },
            body: JSON.stringify(payload),
        });

        const data = await res.json();
        if (!res.ok) {
            let errorMsg = data.message;
            if (data.errors && typeof data.errors === 'object') {
                const fieldErrors = Object.entries(data.errors)
                    .map(([field, errs]) => `${field}: ${Array.isArray(errs) ? errs.join(', ') : errs}`)
                    .join(' | ');
                errorMsg = fieldErrors || errorMsg;
            }
            throw new Error(errorMsg || 'Lỗi khi lưu sinh hiệu.');
        }

        closeModal();
        nurseVitalsState.successMessage = isEdit
            ? 'Đã cập nhật sinh hiệu thành công.'
            : 'Đã lưu sinh hiệu thành công. Bệnh nhân đã chuyển sang trạng thái Sẵn sàng khám.';
        await loadVitalsQueue();
        if (nurseDashboardState.loaded) loadDashboardSummary();
    } catch (err) {
        showError(err.message);
        if (saveBtn) {
            saveBtn.disabled = false;
            saveBtn.textContent = isEdit ? 'Cập nhật sinh hiệu' : 'Lưu & Chuyển Bác sĩ';
        }
    }
}

// --- 3. MÀN HÌNH HỒ SƠ NGƯỜI KHÁM (PATIENTS) ---

function patientsPage() {
    if (!nursePatientsState.loaded && !nursePatientsState.loading) {
        loadNursePatients();
    }

    const headingHtml = heading(
        'Hồ sơ người khám',
        'Danh sách bệnh nhân đã đăng ký khám tại khoa.',
    );

    const searchBar = `<div class="card" style="background:#fff; border:1px solid #e2e8f0; border-radius:8px; padding:16px 20px; margin:16px 0;">
        <form onsubmit="event.preventDefault(); loadNursePatients(document.getElementById('nurse-pat-search').value);" style="display:flex; gap:10px;">
            <input id="nurse-pat-search" type="text" class="form-control" placeholder="Tìm theo họ tên, số điện thoại hoặc CCCD..." value="${esc(nursePatientsState.keyword)}" style="flex:1;" />
            <button type="submit" class="btn primary">${icon('search')} Tìm kiếm</button>
        </form>
    </div>`;

    if (nursePatientsState.loading && !nursePatientsState.loaded) {
        return `${headingHtml}${searchBar}<div class="loading-state" style="padding:40px; text-align:center;">Đang tải danh sách người khám...</div>`;
    }

    const patients = nursePatientsState.patients || [];
    let rowsHtml = '';
    if (patients.length === 0) {
        rowsHtml = `<tr><td colspan="6" style="text-align:center; padding:30px; color:#64748b;">Không tìm thấy hồ sơ người khám nào phù hợp.</td></tr>`;
    } else {
        rowsHtml = patients
            .map(
                (p) => `<tr>
            <td><strong>#${esc(p.patientId)}</strong></td>
            <td><strong>${esc(p.fullName)}</strong></td>
            <td>${esc(p.dateOfBirth)} · ${esc(p.gender)}</td>
            <td>${esc(p.phoneNumber)}<br/><small style="color:#64748b;">CCCD: ${esc(p.nationalId || '—')}</small></td>
            <td><strong style="color:#079bc9;">${esc(p.totalVisits)}</strong> lượt</td>
            <td>${esc(p.lastVisitDate ? new Date(p.lastVisitDate).toLocaleDateString('vi-VN') : '—')}</td>
        </tr>`,
            )
            .join('');
    }

    const tableHtml = `<div class="card" style="background:#fff; border:1px solid #e2e8f0; border-radius:8px; overflow:hidden;">
        <div class="table-responsive">
            <table class="table" style="width:100%; border-collapse:collapse;">
                <thead>
                    <tr style="background:#f8fafc; border-bottom:1px solid #e2e8f0; text-align:left; font-size:12px; color:#475569;">
                        <th style="padding:10px 14px;">Mã BN</th>
                        <th style="padding:10px 14px;">Họ và tên</th>
                        <th style="padding:10px 14px;">Ngày sinh / Giới tính</th>
                        <th style="padding:10px 14px;">Số điện thoại / CCCD</th>
                        <th style="padding:10px 14px;">Lượt khám tại khoa</th>
                        <th style="padding:10px 14px;">Lần khám gần nhất</th>
                    </tr>
                </thead>
                <tbody>${rowsHtml}</tbody>
            </table>
        </div>
    </div>`;

    return `${headingHtml}${searchBar}${tableHtml}`;
}

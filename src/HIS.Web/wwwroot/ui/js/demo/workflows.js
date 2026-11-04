/* demo / workflows */
function holdBooking() {
    const b = ui.booking,
        p = patient(b.patientId),
        d = doctor(b.doctorId);
    if (!linkFor(p.id) || !['ACTIVE', 'PENDING'].includes(linkFor(p.id).status))
        throw new Error('Vui lòng chọn hồ sơ người khám đã liên kết.');
    if (activeApps().length >= CONFIG.maxActiveAppointments)
        throw new Error('Mỗi tài khoản có tối đa 5 lịch hẹn hiệu lực.');
    if (
        db.appointments.some(
            (a) =>
                a.patientId === p.id &&
                a.deptId === d.deptId &&
                a.date === b.date &&
                liveStates.includes(a.status)
        )
    )
        throw new Error('Người khám đã có lịch hiệu lực cùng ngày tại khoa này.');
    if (!b.time || !b.reason.trim()) throw new Error('Vui lòng chọn giờ khám và nhập lý do khám.');
    if (!availableTimes(d.id, b.date, b.shift).includes(b.time))
        throw new Error('Khung giờ không còn khả dụng. Vui lòng chọn giờ khác.');
    const random = Array.from(crypto.getRandomValues(new Uint8Array(6)))
        .map((n) => 'ABCDEFGHJKLMNPQRSTUVWXYZ23456789'[n % 32])
        .join('');
    const a = {
        id: uid('a'),
        code: 'HIS-' + b.date.replaceAll('-', '') + '-' + random,
        patientId: p.id,
        bookedBy: session.accountId,
        doctorId: d.id,
        deptId: d.deptId,
        roomId: d.roomId,
        date: b.date,
        time: b.time,
        shift: b.shift,
        status: 'PENDING',
        channel: 'WEB',
        visitType: 'SCHEDULED',
        priority: 0,
        queue: null,
        reason: b.reason.trim(),
        holdUntil: Date.now() + CONFIG.holdMinutes * 60000,
        parentId: b.parentId,
        createdAt: stamp(),
        history: [{ to: 'PENDING', at: stamp(), by: acc().name }],
    };
    db.appointments.push(a);
    b.holdId = a.id;
    b.step = 4;
    save();
    render();
}

function availableTimes(did, date, shift) {
    if (
        !date ||
        date < TODAY ||
        date > dateAdd(TODAY, 14) ||
        [0, 6].includes(new Date(date + 'T12:00:00').getDay())
    )
        return [];
    const sc = db.schedules.find((s) => s.doctorId === did && s.active);
    if (!sc || !sc.days.includes(new Date(date + 'T12:00:00').getDay())) return [];
    if (
        db.exceptions.some(
            (e) =>
                e.doctorId === did &&
                e.date === date &&
                e.status === 'APPROVED' &&
                (e.shift === shift || e.shift === 'ALL')
        )
    )
        return [];
    const start = shift === 'MORNING' ? 450 : 780,
        end = shift === 'MORNING' ? 690 : 990,
        result = [];
    for (let n = start; n < end; n += sc.slotMinutes) {
        const t =
            String(Math.floor(n / 60)).padStart(2, '0') + ':' + String(n % 60).padStart(2, '0');
        const busy = db.appointments.some(
            (a) =>
                a.doctorId === did &&
                a.date === date &&
                a.time === t &&
                liveStates.includes(a.status)
        );
        if (!busy && !(date === TODAY && n < 540)) result.push(t);
    }
    return result.slice(
        0,
        Math.max(
            0,
            sc.onlineSlots -
                db.appointments.filter(
                    (a) =>
                        a.doctorId === did &&
                        a.date === date &&
                        a.channel === 'WEB' &&
                        (a.shift || 'MORNING') === shift &&
                        liveStates.includes(a.status)
                ).length
        )
    );
}

function expireHolds() {
    let changed = false;
    for (const a of db.appointments) {
        if (a.status === 'PENDING' && a.holdUntil <= Date.now()) {
            a.status = 'EXPIRED';
            a.history.push({ from: 'PENDING', to: 'EXPIRED', at: stamp(), by: 'Hệ thống mẫu' });
            changed = true;
        }
    }
    if (changed) save();
}

function confirmBooking() {
    const a = appointment(ui.booking.holdId);
    if (!a || a.status !== 'PENDING' || a.holdUntil <= Date.now())
        throw new Error('Thời gian giữ chỗ đã hết. Vui lòng chọn lại khung giờ.');
    transition(a, 'CONFIRMED');
    a.holdUntil = null;
    if (a.parentId) {
        const parent = appointment(a.parentId);
        if (parent?.status === 'RESCHEDULE_REQUIRED') transition(parent, 'CANCELLED');
    }
    notify(
        a,
        'Đã xác nhận lịch khám',
        `${doctor(a.doctorId).specialty} · ${a.time} ngày ${fmtDate(a.date)} · Phòng ${room(a.roomId).number}.`
    );
    save();
    ui.booking = {
        step: 1,
        patientId: selfPatient(),
        doctorId: 'd1',
        method: 'specialty',
        date: workingDays()[1],
        time: '',
        shift: 'MORNING',
        reason: '',
        holdId: null,
        parentId: null,
    };
    go('patient/appointments');
    toast('Lịch khám đã được xác nhận. Bạn có thể xem mã hẹn trong danh sách.');
}

function nextQueue(a) {
    const shift = minuteOf(a.time) < 720 ? 'MORNING' : 'AFTERNOON',
        key = [a.date, a.roomId, shift, a.channel].join('/'),
        existing = db.appointments
            .filter(
                (x) =>
                    x.date === a.date && x.roomId === a.roomId && x.channel === a.channel && x.queue
            )
            .map((x) => Number(x.queue.slice(1)) || 0);
    db.queueCounters[key] = Math.max(db.queueCounters[key] || 0, ...existing, 0) + 1;
    return (a.channel === 'WEB' ? 'O' : 'W') + String(db.queueCounters[key]).padStart(2, '0');
}

function checkinModal(id) {
    const a = appointment(id);
    if (a.deptId !== acc().deptId) throw new Error('Lượt khám thuộc khoa khác.');
    if (a.status !== 'CONFIRMED') throw new Error('Chỉ tiếp nhận lịch đã xác nhận.');
    if (a.date !== TODAY || appointmentTime(a) - demoNow() > CONFIG.checkinEarly * 60000)
        throw new Error('Check-in mở từ 60 phút trước giờ hẹn trong ngày khám.');
    const p = patient(a.patientId),
        link = db.links.find((l) => l.accountId === a.bookedBy && l.patientId === p.id);
    openModal(
        'Xác nhận tiếp nhận',
        a.code,
        /* HTML */ `<div class="patient-strip">
                ${avatar(p.name)}
                <div>
                    <h3>${esc(p.name)}</h3>
                    <p>${p.id} · ${age(p.dob)} tuổi · ${a.time} — Phòng ${room(a.roomId).number}</p>
                </div>
            </div>
            <form id="checkin-form" data-id="${a.id}">
                <div class="notice info">
                    ${icon('info')}<span>Đối chiếu giấy tờ và người khám trước khi cấp số.</span>
                </div>
                ${
                    link && link.relationship !== 'SELF'
                        ? /* HTML */ `<label class="check-line"
                                  ><input
                                      name="verifyLink"
                                      type="checkbox"
                                      ${link.status === 'PENDING' ? 'required' : ''}
                                  />Xác nhận liên kết người đặt
                                  hộ${link.status === 'PENDING' ? ' *' : ''}</label
                              >
                              <p class="field-help">
                                  Đã kiểm tra sự có mặt của người bệnh hoặc người giám hộ.
                              </p>
                              ${age(p.dob) >= 18 ? '<label class="check-line"><input name="consent" type="checkbox">Người bệnh đồng ý cho người đặt hộ xem bệnh án</label><p class="field-help">Chỉ bật quyền xem sau khi nhận được sự đồng ý trực tiếp.</p>' : ''}`
                        : ''
                }
                <div class="field">
                    <label for="priority">Mức ưu tiên</label
                    ><select id="priority" name="priority">
                        <option value="0">Thông thường</option>
                        <option value="1">Ưu tiên 1</option>
                        <option value="2">Ưu tiên 2</option>
                        <option value="3">Ưu tiên 3</option>
                    </select>
                </div>
            </form>`,
        button('Quay lại', 'close') +
            '<button class="btn primary" form="checkin-form" type="submit">' +
            icon('check') +
            'Tiếp nhận & cấp số</button>'
    );
}

function vitalsModal(id) {
    const a = appointment(id),
        p = patient(a.patientId),
        v = db.vitals.find((v) => v.appointmentId === id) || {};
    if (a.deptId !== acc().deptId) throw new Error('Không thuộc khoa của bạn.');
    openModal(
        'Ghi nhận sinh hiệu',
        p.name + ' · ' + a.code,
        /* HTML */ `<form id="vitals-form" data-id="${id}">
            <div class="form-grid">
                ${[
                    ['pulse', 'Mạch (lần/phút)', 20, 250, 1],
                    ['bpSys', 'Huyết áp tâm thu (mmHg)', 40, 300, 1],
                    ['bpDia', 'Huyết áp tâm trương (mmHg)', 20, 200, 1],
                    ['temp', 'Nhiệt độ (°C)', 25, 45, 0.1],
                    ['resp', 'Nhịp thở (lần/phút)', 5, 80, 1],
                    ['spo2', 'SpO₂ (%)', 40, 100, 1],
                    ['weight', 'Cân nặng (kg)', 1, 400, 0.1],
                    ['height', 'Chiều cao (cm)', 20, 250, 0.1],
                ]
                    .map(
                        ([name, label, min, max, step]) =>
                            /* HTML */ `<div class="field">
                                <label for="v-${name}">${label} *</label
                                ><input
                                    id="v-${name}"
                                    type="number"
                                    name="${name}"
                                    value="${v[name] ?? ''}"
                                    min="${min}"
                                    max="${max}"
                                    step="${step}"
                                    required
                                />
                            </div>`
                    )
                    .join('')}
            </div>
        </form>`,
        button('Đóng', 'close') +
            '<button class="btn primary" form="vitals-form" type="submit">' +
            icon('check') +
            'Lưu & chuyển chờ bác sĩ</button>',
        true
    );
}

function recordAllowed(r) {
    if (session.role === 'ADMIN' || session.role === 'NURSE') return false;
    if (session.role === 'PATIENT') return canView(r.patientId);
    return session.role === 'DOCTOR'
        ? doctor(r.doctorId)?.deptId === acc()?.deptId
        : session.role === 'DEPARTMENT_HEAD' && doctor(r.doctorId)?.deptId === acc()?.deptId;
}

function recordModal(id) {
    const r = db.records.find((r) => r.id === id);
    if (!r || !recordAllowed(r)) throw new Error('Bạn chưa có quyền xem nội dung bệnh án này.');
    audit('VIEW_RECORD', r.patientId, 'Mở phiếu ' + r.id, 'Bệnh án');
    openModal(
        'Phiếu khám & kết quả',
        patient(r.patientId).name + ' · ' + fmtDate(r.date),
        recordDetail(r),
        button('Đóng', 'close'),
        true
    );
}

function getOrCreateDraft(a) {
    let r = db.records.find((r) => r.appointmentId === a.id);
    if (!r) {
        r = {
            id: uid('mr'),
            appointmentId: a.id,
            patientId: a.patientId,
            doctorId: a.doctorId,
            date: TODAY,
            symptoms: '',
            examination: '',
            icd: '',
            diagnosis: '',
            secondary: '',
            conclusion: '',
            meds: [],
            final: false,
        };
        db.records.push(r);
    }
    return r;
}

function readConsultForm(form) {
    const f = new FormData(form);
    return {
        symptoms: String(f.get('symptoms') || '').trim(),
        examination: String(f.get('examination') || '').trim(),
        icd: String(f.get('icd') || '').trim(),
        diagnosis: String(f.get('diagnosis') || '').trim(),
        secondary: String(f.get('secondary') || '').trim(),
        conclusion: String(f.get('conclusion') || '').trim(),
        meds: [...form.querySelectorAll('.rx-row')].map((row) => ({
            medicationId: row.querySelector('[name="medicationId"]').value,
            dosage: row.querySelector('[name="dosage"]').value.trim(),
            quantity: Number(row.querySelector('[name="quantity"]').value),
            days: Number(row.querySelector('[name="days"]').value) || null,
            instructions: row.querySelector('[name="instructions"]').value.trim(),
        })),
    };
}

function persistDraft() {
    const f = $('#consult-form');
    if (f && session.role === 'DOCTOR') {
        const a = appointment(f.dataset.id);
        if (a?.doctorId === acc()?.doctorId && a.status === 'IN_PROGRESS') {
            Object.assign(getOrCreateDraft(a), readConsultForm(f));
            save();
        }
    }
}

function finishConsultation(form) {
    const a = appointment(form.dataset.id);
    if (a.doctorId !== acc().doctorId || a.status !== 'IN_PROGRESS')
        throw new Error('Bạn chỉ được hoàn tất lượt khám đang thực hiện của mình.');
    const data = readConsultForm(form);
    if (!data.symptoms || !data.diagnosis)
        throw new Error('Vui lòng nhập triệu chứng và chẩn đoán.');
    if (data.meds.some((m) => !m.medicationId || !m.dosage || m.quantity < 1))
        throw new Error('Kiểm tra đơn thuốc: tên thuốc, liều dùng và số lượng.');
    const r = getOrCreateDraft(a);
    Object.assign(r, data, { final: true });
    audit('EDIT_RECORD', a.patientId, 'Hoàn tất phiếu khám ' + r.id, 'Bệnh án');
    transition(a, 'COMPLETED');
    if (!db.invoices.some((i) => i.appointmentId === a.id)) {
        const items = [
            {
                category: 'EXAMINATION',
                description: 'Khám ' + doctor(a.doctorId).specialty,
                amount: doctor(a.doctorId).fee,
            },
            ...db.labOrders
                .filter((l) => l.recordId === r.id)
                .map((l) => {
                    const s = db.services.find((s) => s.id === l.serviceId);
                    return { category: 'SERVICE', description: s.name, amount: s.price };
                }),
            ...r.meds.map((m) => {
                const med = db.medications.find((x) => x.id === m.medicationId);
                return {
                    category: 'MEDICATION',
                    description: med.name + ' × ' + m.quantity + ' ' + med.unit,
                    amount: m.quantity * med.price,
                };
            }),
        ];
        db.invoices.unshift({
            id: 'HD-' + Date.now().toString().slice(-8),
            appointmentId: a.id,
            patientId: a.patientId,
            date: TODAY,
            items,
            insuranceCardId: null,
            coveragePct: 0,
        });
    }
    notify(
        a,
        'Đã có kết quả khám',
        'Phiếu khám đã hoàn tất. Hồ sơ được hiển thị theo quyền xem của tài khoản.'
    );
    save();
    ui.selectedAppointment = null;
    render();
    toast('Đã hoàn tất phiếu khám và chuyển hóa đơn sang quầy thu phí.');
}

function paymentModal(id) {
    const i = db.invoices.find((i) => i.id === id),
        t = invoiceTotal(i);
    if (session.role !== 'NURSE') throw new Error('Chỉ nhân viên thu phí được ghi nhận giao dịch.');
    openModal(
        'Ghi nhận thu / hoàn tiền',
        i.id + ' · ' + patient(i.patientId).name,
        /* HTML */ `<div class="amount-summary">
                <span>Còn phải thu</span><strong>${money(t.balance)}</strong>
            </div>
            <form id="payment-form" data-id="${id}">
                <div class="form-grid">
                    <div class="field">
                        <label for="pay-type">Loại giao dịch</label
                        ><select id="pay-type" name="type">
                            <option value="PAYMENT">Thanh toán</option>
                            <option value="DEPOSIT">Tạm ứng</option>
                            <option value="REFUND">Hoàn tiền</option>
                        </select>
                    </div>
                    <div class="field">
                        <label for="pay-amount">Số tiền (VNĐ) *</label
                        ><input
                            id="pay-amount"
                            name="amount"
                            type="number"
                            min="1"
                            step="1"
                            value="${t.balance || t.overpaid || t.paid}"
                            required
                        />
                    </div>
                    <div class="field full">
                        <label for="pay-method">Phương thức</label
                        ><select id="pay-method" name="method">
                            <option value="CASH">Tiền mặt</option>
                            <option value="BANK_TRANSFER">Chuyển khoản</option>
                        </select>
                    </div>
                    <div class="field full">
                        <label for="receipt-email">Email nhận biên lai</label
                        ><input
                            id="receipt-email"
                            name="receiptEmail"
                            type="email"
                            placeholder="Email do người thanh toán cung cấp"
                        /><small>Biên lai chỉ gồm mã hẹn, ngày và tổng tiền.</small>
                    </div>
                    <div class="field full">
                        <label for="payment-note">Ghi chú</label
                        ><input id="payment-note" name="note" maxlength="255" />
                    </div>
                </div>
            </form>`,
        button('Quay lại', 'close') +
            '<button class="btn primary" form="payment-form" type="submit">' +
            icon('check') +
            'Xác nhận đã nhận / hoàn tiền</button>'
    );
}

function exceptionDecision(id, approve) {
    const e = db.exceptions.find((x) => x.id === id);
    if (session.role !== 'DEPARTMENT_HEAD' || doctor(e.doctorId).deptId !== acc().deptId)
        throw new Error('Chỉ trưởng khoa phụ trách được duyệt.');
    if (e.status !== 'PENDING') throw new Error('Yêu cầu đã được xử lý.');
    if (e.date < TODAY) throw new Error('Chỉ duyệt thay đổi cho ca khám trong tương lai.');
    e.status = approve ? 'APPROVED' : 'REJECTED';
    e.approvedBy = session.accountId;
    e.approvedAt = stamp();
    if (approve) {
        const affected = db.appointments.filter(
            (a) =>
                a.doctorId === e.doctorId &&
                a.date === e.date &&
                (e.shift === 'ALL' ||
                    (minuteOf(a.time) < 720 ? 'MORNING' : 'AFTERNOON') === e.shift) &&
                ['PENDING', 'CONFIRMED'].includes(a.status)
        );
        for (const a of affected) {
            if (a.status === 'PENDING') {
                transition(a, 'CANCELLED');
                notify(a, 'Lịch giữ chỗ đã hủy', 'Ca khám thay đổi. Vui lòng chọn một lịch mới.');
                continue;
            }
            const old = doctor(a.doctorId);
            let replacement = e.replacementId
                ? doctor(e.replacementId)
                : db.doctors.find(
                      (d) =>
                          d.id !== e.doctorId &&
                          d.deptId === old.deptId &&
                          d.active &&
                          !db.appointments.some(
                              (x) =>
                                  x.doctorId === d.id &&
                                  x.date === a.date &&
                                  x.time === a.time &&
                                  liveStates.includes(x.status)
                          )
                  );
            if (
                replacement &&
                db.appointments.some(
                    (x) =>
                        x.id !== a.id &&
                        x.doctorId === replacement.id &&
                        x.date === a.date &&
                        x.time === a.time &&
                        liveStates.includes(x.status)
                )
            )
                replacement = null;
            if (replacement) {
                a.doctorId = replacement.id;
                a.roomId = replacement.roomId;
                a.history.push({
                    from: 'CONFIRMED',
                    to: 'CONFIRMED',
                    at: stamp(),
                    by: acc().name,
                    note: 'Đổi bác sĩ ' + old.name + ' → ' + replacement.name,
                });
                notify(
                    a,
                    'Điều chỉnh bác sĩ khám',
                    `BS. ${old.name} → BS. ${replacement.name}; ${a.time} ngày ${fmtDate(a.date)}.`
                );
            } else {
                transition(a, 'RESCHEDULE_REQUIRED');
                notify(
                    a,
                    'Cần chọn lại lịch khám',
                    'Không còn khung giờ thay thế phù hợp. Vui lòng đặt lại lịch từ lịch hẹn này.'
                );
            }
        }
    }
    audit(
        approve ? 'APPROVE_EXCEPTION' : 'REJECT_EXCEPTION',
        '',
        'Xử lý yêu cầu ' + e.id,
        'Lịch khám'
    );
    save();
    render();
    toast(
        approve ? 'Đã duyệt và cập nhật các lịch hẹn liên quan.' : 'Đã từ chối yêu cầu thay đổi.'
    );
}

function exportCsv() {
    const rows = [
        [
            'Hóa đơn',
            'Mã lịch hẹn',
            'Ngày khám',
            'Tổng tiền',
            'BHYT',
            'Người bệnh trả',
            'Đã thu',
            'Còn lại',
            'Trạng thái',
        ],
        ...db.invoices.map((i) => {
            const t = invoiceTotal(i);
            return [
                i.id,
                appointment(i.appointmentId)?.code,
                i.date,
                t.gross,
                t.covered,
                t.due,
                t.paid,
                t.balance,
                t.status,
            ];
        }),
    ];
    const csv =
        '\ufeff' +
        rows
            .map((r) => r.map((v) => '"' + String(v ?? '').replaceAll('"', '""') + '"').join(','))
            .join('\r\n');
    const url = URL.createObjectURL(new Blob([csv], { type: 'text/csv;charset=utf-8' }));
    const a = document.createElement('a');
    a.href = url;
    a.download = 'MediCare-so-thu-phi.csv';
    a.click();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
    toast('Đã xuất sổ thu phí CSV.');
}

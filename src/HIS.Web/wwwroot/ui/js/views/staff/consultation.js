/* views / staff / consultation */
function consultationPage() {
    const apps = todayApps().filter((a) => ['READY_FOR_DOCTOR', 'IN_PROGRESS'].includes(a.status));
    if (!apps.some((a) => a.id === ui.selectedAppointment)) ui.selectedAppointment = apps[0]?.id;
    const a = appointment(ui.selectedAppointment);
    return /* HTML */ `${heading('Khám bệnh & kê đơn', doctor(acc().doctorId)?.specialty + ' · BS. ' + acc().name + ' · Chỉ chỉnh sửa phiếu do bạn phụ trách.', link('Lịch cá nhân', 'staff/schedule', 'calendar', 'btn outline'))}
        <div class="clinical-layout">
            <aside class="card consultation-list">
                ${cardHeader('Danh sách chờ', apps.length + ' lượt khám')}
                <div>
                    ${
                        apps
                            .map(
                                (a) =>
                                    /* HTML */ `<button
                                        class="consultation-person ${ui.selectedAppointment === a.id ? 'active' : ''}"
                                        data-act="select-consult"
                                        data-id="${a.id}"
                                    >
                                        ${avatar(patient(a.patientId).name)}<span
                                            ><strong>${patient(a.patientId).name}</strong
                                            ><small>${a.queue} · ${a.time}</small
                                            >${badge(a.status)}</span
                                        >
                                    </button>`
                            )
                            .join('') || empty('Đã hoàn tất khám')
                    }
                </div>
            </aside>
            <section class="card consultation-workspace">
                ${a ? consultationForm(a) : empty('Không có bệnh nhân đang chờ', 'Lượt khám sẽ xuất hiện sau khi nhân viên hoàn tất sinh hiệu.')}
            </section>
        </div>`;
}

function consultationForm(a) {
    const p = patient(a.patientId),
        r = db.records.find((r) => r.appointmentId === a.id) || {},
        v = db.vitals.find((v) => v.appointmentId === a.id),
        editable = a.status === 'IN_PROGRESS',
        orders = db.labOrders.filter((l) => l.recordId === r.id);
    return /* HTML */ `${cardHeader(/* HTML */ `<span>${p.name}</span>`, p.id + ' · ' + p.gender + ' · ' + age(p.dob) + ' tuổi · ' + a.queue, button('Lịch sử khám', 'patient-history', 'clock', 'text-link', `data-id="${p.id}"`), 'stethoscope')}
        <div class="clinical-inner">
            ${notice('<strong>Dị ứng / Lưu ý:</strong> ' + esc(p.allergy) + '<br>Lý do khám: ' + esc(a.reason), 'amber')}
            <div class="vitals-strip">
                ${[
                    ['Mạch', v?.pulse, 'l/p'],
                    ['Huyết áp', v ? v.bpSys + '/' + v.bpDia : '—', 'mmHg'],
                    ['Nhiệt độ', v?.temp, '°C'],
                    ['SpO₂', v?.spo2, '%'],
                    ['Cân nặng', v?.weight, 'kg'],
                ]
                    .map(
                        ([n, x, u]) =>
                            /* HTML */ `<div>
                                <small>${n}</small><strong>${x ?? '—'} <span>${u}</span></strong>
                            </div>`
                    )
                    .join('')}
            </div>
            ${
                !editable
                    ? /* HTML */ `<div class="begin-exam">
                          <div>
                              <h3>Bệnh nhân đã sẵn sàng</h3>
                              <p>Bắt đầu khám để nhập phiếu và tạo chỉ định.</p>
                          </div>
                          ${button('Bắt đầu khám', 'begin-consult', 'stethoscope', 'btn primary', `data-id="${a.id}"`)}
                      </div>`
                    : ''
            }
            <form id="consult-form" data-id="${a.id}">
                <fieldset ${editable ? '' : 'disabled'}>
                    <div class="form-section-title">
                        <span>01</span>
                        <h3>Thăm khám & chẩn đoán</h3>
                    </div>
                    <div class="form-grid">
                        <div class="field full">
                            <label for="symptoms">Triệu chứng / Lý do khám *</label
                            ><textarea id="symptoms" name="symptoms" required>
${esc(r.symptoms || '')}</textarea>
                        </div>
                        <div class="field full">
                            <label for="examination">Khám lâm sàng</label
                            ><textarea id="examination" name="examination">
${esc(r.examination || '')}</textarea>
                        </div>
                        <div class="field">
                            <label for="icd">Mã chẩn đoán (ICD-10)</label
                            ><input
                                id="icd"
                                name="icd"
                                maxlength="20"
                                value="${esc(r.icd || '')}"
                                placeholder="Nhập mã khi có"
                            />
                        </div>
                        <div class="field">
                            <label for="diagnosis">Chẩn đoán chính *</label
                            ><input
                                id="diagnosis"
                                name="diagnosis"
                                maxlength="255"
                                required
                                value="${esc(r.diagnosis || '')}"
                            />
                        </div>
                        <div class="field full">
                            <label for="secondary">Chẩn đoán kèm theo</label
                            ><input
                                id="secondary"
                                name="secondary"
                                value="${esc(r.secondary || '')}"
                            />
                        </div>
                    </div>
                    <div class="form-section-title">
                        <span>02</span>
                        <h3>Chỉ định cận lâm sàng</h3>
                        ${button('Thêm chỉ định', 'new-lab', 'plus', 'text-link', `data-id="${a.id}"`)}
                    </div>
                    ${orders.length ? /* HTML */ `<div class="inline-labs">${orders.map((l) => /* HTML */ `<div><span>${db.services.find((s) => s.id === l.serviceId)?.name}</span><span class="pill ${l.status === 'COMPLETED' ? 'success' : 'amber'}">${l.status === 'COMPLETED' ? 'Đã có kết quả' : 'Chờ kết quả'}</span>${l.status === 'COMPLETED' ? button('Xem', 'lab-view', 'eye', 'text-link', `data-id="${l.id}"`) : ''}</div>`).join('')}</div>` : '<p class="empty-line">Chưa có chỉ định. Thêm dịch vụ khi cần thiết theo đánh giá của bác sĩ.</p>'}
                    <div class="form-section-title">
                        <span>03</span>
                        <h3>Đơn thuốc</h3>
                        ${button('Thêm thuốc', 'add-rx', 'plus', 'text-link')}
                    </div>
                    <div id="rx-rows">${(r.meds || []).map(rxRow).join('')}</div>
                    <p class="field-help">
                        Danh mục thuốc minh họa. Liều dùng và hướng dẫn do bác sĩ nhập.
                    </p>
                    <div class="form-section-title">
                        <span>04</span>
                        <h3>Kết luận & lời dặn</h3>
                    </div>
                    <div class="field">
                        <label for="conclusion">Kết luận / Hướng dẫn theo dõi</label
                        ><textarea id="conclusion" name="conclusion">
${esc(r.conclusion || '')}</textarea>
                    </div>
                </fieldset>
                <div class="clinical-actions">
                    <span>Hoàn tất sẽ tạo hóa đơn thu phí.</span
                    >${button('Lưu nháp', 'save-draft', 'file', 'btn outline', editable ? '' : 'disabled')}<button
                        class="btn primary"
                        type="submit"
                        ${editable ? '' : 'disabled'}
                    >
                        ${icon('check')}Hoàn tất khám
                    </button>
                </div>
            </form>
        </div>`;
}

function rxRow(m = {}) {
    return /* HTML */ `<div class="rx-row">
        <div class="field">
            <label>Thuốc *</label
            ><select name="medicationId" aria-label="Tên thuốc" required>
                ${db.medications
                    .filter((x) => x.active)
                    .map(
                        (x) =>
                            /* HTML */ `<option
                                value="${x.id}"
                                ${m.medicationId === x.id ? 'selected' : ''}
                            >
                                ${x.name} · ${x.unit}
                            </option>`
                    )
                    .join('')}
            </select>
        </div>
        <div class="field">
            <label>Liều dùng *</label
            ><input
                name="dosage"
                aria-label="Liều dùng"
                value="${esc(m.dosage || '')}"
                required
                placeholder="Theo chỉ định"
            />
        </div>
        <div class="field small-field">
            <label>Số lượng *</label
            ><input
                name="quantity"
                aria-label="Số lượng thuốc"
                type="number"
                min="1"
                value="${m.quantity || 1}"
                required
            />
        </div>
        <div class="field small-field">
            <label>Số ngày</label
            ><input
                name="days"
                aria-label="Số ngày dùng"
                type="number"
                min="1"
                value="${m.days || ''}"
            />
        </div>
        <div class="field full instructions">
            <label>Hướng dẫn sử dụng</label
            ><input
                name="instructions"
                aria-label="Hướng dẫn sử dụng thuốc"
                value="${esc(m.instructions || '')}"
            />
        </div>
        ${button('', 'remove-rx', 'trash', 'icon-btn danger-text', 'aria-label="Xóa thuốc"')}
    </div>`;
}

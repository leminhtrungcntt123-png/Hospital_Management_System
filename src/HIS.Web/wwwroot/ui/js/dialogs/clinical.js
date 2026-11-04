/* dialogs / clinical */
function labModal(appointmentId) {
    requireRole('DOCTOR');
    persistDraft();
    const a = appointment(appointmentId);
    if (a.doctorId !== acc().doctorId || a.status !== 'IN_PROGRESS')
        throw new Error('Bắt đầu khám trước khi tạo chỉ định.');
    openModal(
        'Thêm chỉ định',
        'Chọn dịch vụ xét nghiệm hoặc chẩn đoán hình ảnh.',
        /* HTML */ `<form id="lab-order-form" data-id="${a.id}">
            <div class="field">
                <label for="lab-service">Dịch vụ *</label
                ><select id="lab-service" name="serviceId">
                    ${db.services
                        .filter((s) => s.active && s.type !== 'EXAMINATION')
                        .map(
                            (s) =>
                                /* HTML */ `<option value="${s.id}">
                                    ${s.name} · ${money(s.price)}
                                </option>`
                        )
                        .join('')}
                </select>
            </div>
            <div class="field space-top">
                <label for="lab-note">Ghi chú chỉ định</label
                ><textarea id="lab-note" name="note" maxlength="255"></textarea>
            </div>
        </form>`,
        button('Quay lại', 'close') +
            '<button class="btn primary" form="lab-order-form" type="submit">Tạo chỉ định</button>'
    );
}

function labResultModal(id, edit = false) {
    requireRole('DOCTOR', 'DEPARTMENT_HEAD');
    const l = db.labOrders.find((l) => l.id === id),
        r = db.records.find((r) => r.id === l?.recordId);
    if (!l || !recordAllowed(r)) throw new Error('Bạn không có quyền xem chỉ định này.');
    if (edit && l.doctorId !== acc().doctorId)
        throw new Error('Bạn chỉ cập nhật chỉ định do mình phụ trách.');
    audit('VIEW_RECORD', l.patientId, 'Xem kết quả ' + l.id, 'Cận lâm sàng');
    openModal(
        edit ? 'Nhập kết quả' : 'Kết quả chỉ định',
        patient(l.patientId).name + ' · ' + db.services.find((s) => s.id === l.serviceId).name,
        edit
            ? /* HTML */ `<form id="lab-result-form" data-id="${id}">
                  <div class="field">
                      <label for="lab-result">Nội dung kết quả *</label
                      ><textarea id="lab-result" name="result" required rows="6">
${esc(l.result || '')}</textarea>
                  </div>
              </form>`
            : /* HTML */ `<div class="result-box">
                  <span class="pill"
                      >${l.status === 'COMPLETED' ? 'Đã có kết quả' : 'Chờ kết quả'}</span
                  >
                  <p>${esc(l.result || 'Chưa có kết quả.')}</p>
              </div>`,
        button('Đóng', 'close') +
            (edit
                ? '<button class="btn primary" form="lab-result-form" type="submit">Lưu kết quả</button>'
                : ''),
        true
    );
}

/* dialogs / schedule */
function exceptionModal() {
    requireRole('DOCTOR');
    const d = doctor(acc().doctorId);
    openModal(
        'Đề nghị nghỉ / thay đổi ca',
        'Yêu cầu được gửi đến trưởng khoa phụ trách.',
        /* HTML */ `<form id="exception-form">
            <div class="form-grid">
                <div class="field">
                    <label for="ex-date">Ngày thay đổi *</label
                    ><input
                        id="ex-date"
                        name="date"
                        type="date"
                        min="${dateAdd(TODAY, 1)}"
                        value="${dateAdd(TODAY, 1)}"
                        required
                    />
                </div>
                <div class="field">
                    <label for="ex-shift">Ca khám</label
                    ><select id="ex-shift" name="shift">
                        <option value="MORNING">Ca sáng</option>
                        <option value="AFTERNOON">Ca chiều</option>
                        <option value="ALL">Cả ngày</option>
                    </select>
                </div>
                <div class="field full">
                    <label for="ex-type">Loại yêu cầu</label
                    ><select id="ex-type" name="type">
                        <option value="LEAVE">Nghỉ phép</option>
                        <option value="TRAINING">Đào tạo</option>
                        <option value="SURGERY">Phẫu thuật</option>
                        <option value="SHIFT_SWAP">Đổi ca</option>
                    </select>
                </div>
                <div class="field full">
                    <label for="ex-replacement">Đề xuất bác sĩ thay thế</label
                    ><select id="ex-replacement" name="replacementId">
                        <option value="">Để trưởng khoa điều phối</option>
                        ${db.doctors
                            .filter((x) => x.id !== d.id && x.deptId === d.deptId)
                            .map((x) => /* HTML */ `<option value="${x.id}">BS. ${x.name}</option>`)
                            .join('')}
                    </select>
                </div>
                <div class="field full">
                    <label for="ex-reason">Lý do *</label
                    ><textarea id="ex-reason" name="reason" required maxlength="255"></textarea>
                </div>
            </div>
        </form>`,
        button('Quay lại', 'close') +
            '<button class="btn primary" form="exception-form" type="submit">Gửi trưởng khoa</button>'
    );
}

function scheduleModal(id = '') {
    requireRole('DEPARTMENT_HEAD');
    const s = db.schedules.find((s) => s.id === id),
        d = s ? doctor(s.doctorId) : db.doctors.find((d) => d.deptId === acc().deptId);
    openModal(
        s ? 'Chỉnh lịch khám & hạn mức' : 'Thiết lập lịch khám',
        'Ca khám sáng / chiều; lịch trực đêm được phân riêng.',
        /* HTML */ `<form id="schedule-form" data-id="${s?.id || ''}">
            <div class="form-grid">
                <div class="field full">
                    <label for="sc-doctor">Bác sĩ *</label
                    ><select id="sc-doctor" name="doctorId">
                        ${db.doctors
                            .filter((x) => x.deptId === acc().deptId)
                            .map(
                                (x) =>
                                    /* HTML */ `<option
                                        value="${x.id}"
                                        ${x.id === d.id ? 'selected' : ''}
                                    >
                                        BS. ${x.name}
                                    </option>`
                            )
                            .join('')}
                    </select>
                </div>
                <div class="field full">
                    <label for="sc-room">Phòng khám *</label
                    ><select id="sc-room" name="roomId">
                        ${db.rooms
                            .filter((x) => x.deptId === acc().deptId)
                            .map(
                                (x) =>
                                    /* HTML */ `<option
                                        value="${x.id}"
                                        ${x.id === (s?.roomId || d.roomId) ? 'selected' : ''}
                                    >
                                        ${x.name} · ${x.number}
                                    </option>`
                            )
                            .join('')}
                    </select>
                </div>
                <div class="field">
                    <label for="sc-total">Tổng người khám / ca *</label
                    ><input
                        id="sc-total"
                        name="maxPatients"
                        type="number"
                        min="1"
                        max="100"
                        value="${s?.maxPatients || 40}"
                        required
                    />
                </div>
                <div class="field">
                    <label for="sc-online">Hạn mức trực tuyến *</label
                    ><input
                        id="sc-online"
                        name="onlineSlots"
                        type="number"
                        min="0"
                        max="100"
                        value="${s?.onlineSlots ?? 28}"
                        required
                    />
                </div>
                <div class="field">
                    <label for="sc-minutes">Thời lượng slot (phút)</label
                    ><select id="sc-minutes" name="slotMinutes">
                        ${[15, 20, 30].map((n) => /* HTML */ `<option value="${n}" ${s?.slotMinutes === n ? 'selected' : ''}>${n} phút</option>`).join('')}
                    </select>
                </div>
                <div class="field">
                    <label for="sc-active">Trạng thái</label
                    ><select id="sc-active" name="active">
                        <option value="true" ${s?.active !== false ? 'selected' : ''}>
                            Đang áp dụng
                        </option>
                        <option value="false" ${s?.active === false ? 'selected' : ''}>
                            Tạm ngưng sinh lịch
                        </option>
                    </select>
                </div>
            </div>
            ${notice('Hạn mức tại quầy = tổng người khám − hạn mức trực tuyến. Lịch mẫu áp dụng Thứ Hai đến Thứ Sáu.')}
        </form>`,
        button('Đóng', 'close') +
            '<button class="btn primary" form="schedule-form" type="submit">Lưu lịch khám</button>'
    );
}

function dutyModal() {
    requireRole('DEPARTMENT_HEAD');
    openModal(
        'Phân ca trực nhân sự',
        'Có thể phân ca trực qua nửa đêm; không sinh slot đặt khám.',
        /* HTML */ `<form id="duty-form">
            <div class="form-grid">
                <div class="field full">
                    <label for="duty-staff">Nhân sự trong khoa *</label
                    ><select id="duty-staff" name="accountId">
                        ${db.accounts
                            .filter(
                                (a) =>
                                    a.deptId === acc().deptId &&
                                    ['DOCTOR', 'NURSE'].includes(a.role)
                            )
                            .map(
                                (a) =>
                                    /* HTML */ `<option value="${a.id}">
                                        ${a.name} · ${roleInfo[a.role].name}
                                    </option>`
                            )
                            .join('')}
                    </select>
                </div>
                <div class="field">
                    <label for="duty-start">Bắt đầu *</label
                    ><input
                        id="duty-start"
                        name="start"
                        type="datetime-local"
                        value="${TODAY}T17:00"
                        required
                    />
                </div>
                <div class="field">
                    <label for="duty-end">Kết thúc *</label
                    ><input
                        id="duty-end"
                        name="end"
                        type="datetime-local"
                        value="${dateAdd(TODAY, 1)}T07:00"
                        required
                    />
                </div>
                <div class="field full">
                    <label for="duty-note">Ghi chú</label
                    ><input id="duty-note" name="note" maxlength="255" />
                </div>
            </div>
        </form>`,
        button('Đóng', 'close') +
            '<button class="btn primary" form="duty-form" type="submit">Phân công ca trực</button>'
    );
}

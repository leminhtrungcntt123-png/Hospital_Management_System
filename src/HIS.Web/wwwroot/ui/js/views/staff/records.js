/* views / staff / records */
function recordDetail(r) {
    const a = appointment(r.appointmentId),
        p = patient(r.patientId),
        v = db.vitals.find((v) => v.appointmentId === a?.id),
        orders = db.labOrders.filter((l) => l.recordId === r.id);
    return /* HTML */ `<div class="patient-strip">
            ${avatar(p.name)}
            <div>
                <h3>${p.name}</h3>
                <p>${p.id} · BS. ${doctor(r.doctorId).name} · ${fmtDate(r.date)}</p>
            </div>
        </div>
        ${
            v
                ? /* HTML */ `<div class="vitals-strip">
                      <div><small>Huyết áp</small><strong>${v.bpSys}/${v.bpDia}</strong></div>
                      <div><small>Mạch</small><strong>${v.pulse}</strong></div>
                      <div><small>Nhiệt độ</small><strong>${v.temp} °C</strong></div>
                      <div><small>SpO₂</small><strong>${v.spo2}%</strong></div>
                  </div>`
                : ''
        }
        <div class="record-detail-section">
            <h3>Thăm khám</h3>
            <dl>
                <dt>Triệu chứng</dt>
                <dd>${esc(r.symptoms || 'Chưa ghi nhận')}</dd>
                <dt>Khám lâm sàng</dt>
                <dd>${esc(r.examination || 'Chưa ghi nhận')}</dd>
                <dt>Chẩn đoán</dt>
                <dd>${esc(r.icd || '')} ${esc(r.diagnosis)}</dd>
                <dt>Chẩn đoán kèm theo</dt>
                <dd>${esc(r.secondary || 'Không ghi nhận')}</dd>
            </dl>
        </div>
        <div class="record-detail-section">
            <h3>Chỉ định & kết quả</h3>
            ${
                orders
                    .map(
                        (l) =>
                            /* HTML */ `<div class="result-box">
                                <strong
                                    >${db.services.find((s) => s.id === l.serviceId)?.name}</strong
                                ><span class="pill"
                                    >${l.status === 'COMPLETED' ? 'Đã trả kết quả' : 'Đang chờ'}</span
                                >
                                <p>${esc(l.result || 'Chưa có kết quả.')}</p>
                            </div>`
                    )
                    .join('') || '<p>Không có chỉ định trong phiếu khám này.</p>'
            }
        </div>
        <div class="record-detail-section">
            <h3>Đơn thuốc</h3>
            ${
                r.meds.length
                    ? /* HTML */ `<div class="table-wrap">
                          <table>
                              <thead>
                                  <tr>
                                      <th>Thuốc</th>
                                      <th>Liều dùng</th>
                                      <th>Số lượng / Ngày</th>
                                      <th>Hướng dẫn</th>
                                  </tr>
                              </thead>
                              <tbody>
                                  ${r.meds
                                      .map(
                                          (m) =>
                                              /* HTML */ `<tr>
                                                  <td>
                                                      ${db.medications.find((x) => x.id === m.medicationId)?.name}
                                                  </td>
                                                  <td>${esc(m.dosage)}</td>
                                                  <td>${m.quantity} / ${m.days || '—'}</td>
                                                  <td>${esc(m.instructions)}</td>
                                              </tr>`
                                      )
                                      .join('')}
                              </tbody>
                          </table>
                      </div>`
                    : '<p>Không kê thuốc trong phiếu khám này.</p>'
            }
        </div>
        <div class="record-detail-section">
            <h3>Kết luận & lời dặn</h3>
            <p>${esc(r.conclusion || 'Chưa ghi nhận')}</p>
        </div>`;
}

function labsPage() {
    const list = db.labOrders.filter(
        (l) =>
            l.doctorId === acc().doctorId &&
            (ui.filter === 'ALL' || l.status === ui.filter) &&
            matched(patient(l.patientId).name)
    );
    return /* HTML */ `${heading('Chỉ định & kết quả', 'Theo dõi các chỉ định thuộc phiếu khám do bạn phụ trách.')}
        <section class="card">
            ${tabs(
                [
                    ['ALL', 'Tất cả'],
                    ['PENDING', 'Chờ kết quả'],
                    ['COMPLETED', 'Đã có kết quả'],
                ],
                ui.filter
            )}
            <div class="toolbar">${searchInput('Tìm theo người khám…')}</div>
            ${
                list.length
                    ? /* HTML */ `<div class="table-wrap">
                          <table>
                              <thead>
                                  <tr>
                                      <th>Người khám</th>
                                      <th>Dịch vụ</th>
                                      <th>Ngày chỉ định</th>
                                      <th>Trạng thái</th>
                                      <th>Thao tác</th>
                                  </tr>
                              </thead>
                              <tbody>
                                  ${list
                                      .map(
                                          (l) =>
                                              /* HTML */ `<tr>
                                                  <td>${personCell(patient(l.patientId))}</td>
                                                  <td>
                                                      ${db.services.find((s) => s.id === l.serviceId)?.name}<small
                                                          >${esc(l.note || '')}</small
                                                      >
                                                  </td>
                                                  <td>${fmtDate(l.date)}</td>
                                                  <td>
                                                      <span
                                                          class="pill ${l.status === 'COMPLETED' ? 'success' : 'amber'}"
                                                          >${l.status === 'COMPLETED' ? 'Đã có kết quả' : 'Đang chờ'}</span
                                                      >
                                                  </td>
                                                  <td>
                                                      ${button(l.status === 'COMPLETED' ? 'Xem kết quả' : 'Nhập kết quả', l.status === 'COMPLETED' ? 'lab-view' : 'lab-result', l.status === 'COMPLETED' ? 'eye' : 'edit', 'btn outline sm', `data-id="${l.id}"`)}
                                                  </td>
                                              </tr>`
                                      )
                                      .join('')}
                              </tbody>
                          </table>
                      </div>`
                    : empty('Chưa có chỉ định phù hợp')
            }
        </section>`;
}

function staffRecordsPage() {
    const list = db.records.filter(
        (r) => recordAllowed(r) && r.final && matched(patient(r.patientId).name + ' ' + r.diagnosis)
    );
    return /* HTML */ `${heading('Bệnh án trong khoa', 'Xem hồ sơ trong phạm vi chuyên môn. Chỉ bác sĩ phụ trách được chỉnh sửa phiếu của mình.')}
        <section class="card">
            <div class="toolbar">
                ${searchInput('Tìm người khám hoặc kết luận…')}<span
                    >${list.length} phiếu khám</span
                >
            </div>
            ${
                list.length
                    ? /* HTML */ `<div class="table-wrap">
                          <table>
                              <thead>
                                  <tr>
                                      <th>Người khám</th>
                                      <th>Ngày khám</th>
                                      <th>Bác sĩ</th>
                                      <th>Kết luận</th>
                                      <th>Thao tác</th>
                                  </tr>
                              </thead>
                              <tbody>
                                  ${list
                                      .map(
                                          (r) =>
                                              /* HTML */ `<tr>
                                                  <td>${personCell(patient(r.patientId))}</td>
                                                  <td>${fmtDate(r.date)}</td>
                                                  <td>BS. ${doctor(r.doctorId).name}</td>
                                                  <td>${esc(r.diagnosis)}</td>
                                                  <td>
                                                      ${button('Xem phiếu', 'record-detail', 'eye', 'btn outline sm', `data-id="${r.id}"`)}
                                                  </td>
                                              </tr>`
                                      )
                                      .join('')}
                              </tbody>
                          </table>
                      </div>`
                    : empty('Chưa có phiếu khám phù hợp')
            }
        </section>`;
}

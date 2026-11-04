/* dialogs / billing */
function invoiceDetail(id) {
    const i = db.invoices.find((x) => x.id === id);
    if (!i) throw new Error('Không tìm thấy hóa đơn.');
    const a = appointment(i.appointmentId);
    if (session.role === 'NURSE' && a.deptId !== acc().deptId)
        throw new Error('Hóa đơn ngoài khoa của bạn.');
    requireRole('NURSE', 'ADMIN');
    const t = invoiceTotal(i),
        p = patient(i.patientId),
        cards = db.insuranceCards.filter((c) => c.patientId === p.id && c.active);
    openModal(
        'Chi tiết thu phí',
        i.id + ' · ' + a.code,
        /* HTML */ `<div class="patient-strip">
                ${avatar(p.name)}
                <div>
                    <h3>${p.name}</h3>
                    <p>${p.id} · Ngày khám ${fmtDate(i.date)}</p>
                </div>
                ${badge(t.status)}
            </div>
            ${
                session.role === 'NURSE'
                    ? /* HTML */ `<form id="insurance-form" data-id="${i.id}" class="insurance-box">
                          <div class="field">
                              <label for="insurance-card">Thẻ BHYT của người khám</label
                              ><select name="cardId" id="insurance-card">
                                  <option value="">Không áp dụng</option>
                                  ${cards.map((c) => /* HTML */ `<option value="${c.id}" ${i.insuranceCardId === c.id ? 'selected' : ''}>${c.number} · ${db.insurancePlans.find((p) => p.id === c.planId).name} · Hạn ${fmtDate(c.to)}</option>`).join('')}
                              </select>
                          </div>
                          <button type="submit" class="btn outline">
                              ${icon('shield')}Áp dụng BHYT</button
                          ><small
                              >Kiểm tra hiệu lực theo ngày khám ${fmtDate(i.date)}. Mức hưởng đang
                              áp dụng: ${i.coveragePct || 0}%.</small
                          >
                      </form>`
                    : notice(
                          'Màn hình quản trị chỉ hiển thị phân loại chi phí và số tiền, không có nội dung bệnh án.'
                      )
            }
            <div class="table-wrap">
                <table class="invoice-items">
                    <thead>
                        <tr>
                            <th>Hạng mục</th>
                            <th>Phân loại</th>
                            <th class="right">Thành tiền</th>
                        </tr>
                    </thead>
                    <tbody>
                        ${i.items
                            .map(
                                (x) =>
                                    /* HTML */ `<tr>
                                        <td>
                                            ${session.role === 'ADMIN' ? { EXAMINATION: 'Chi phí khám', MEDICATION: 'Chi phí thuốc', SERVICE: 'Chi phí dịch vụ' }[x.category] : esc(x.description)}
                                        </td>
                                        <td>
                                            ${{ EXAMINATION: 'Khám bệnh', MEDICATION: 'Thuốc', SERVICE: 'Dịch vụ' }[x.category]}
                                        </td>
                                        <td class="right">${money(x.amount)}</td>
                                    </tr>`
                            )
                            .join('')}
                    </tbody>
                </table>
            </div>
            <div class="billing-totals">
                ${[
                    ['Tổng chi phí', t.gross],
                    ['BHYT chi trả (' + (i.coveragePct || 0) + '%)', t.covered],
                    ['Người bệnh phải trả', t.due],
                    ['Đã tạm ứng / thanh toán', t.paid],
                ]
                    .map(
                        ([l, v]) =>
                            /* HTML */ `<div><span>${l}</span><strong>${money(v)}</strong></div>`
                    )
                    .join('')}
                <div class="grand-total">
                    <span>${t.overpaid ? 'Cần hoàn lại' : 'Còn phải thu'}</span
                    ><strong>${money(t.overpaid || t.balance)}</strong>
                </div>
            </div>
            <h3 class="modal-section-title">Giao dịch của hóa đơn</h3>
            ${paymentsTable(db.payments.filter((p) => p.invoiceId === i.id))}`,
        button('Đóng', 'close') +
            (session.role === 'NURSE'
                ? button(
                      'Ghi nhận thu / hoàn tiền',
                      'payment',
                      'wallet',
                      'btn primary',
                      `data-id="${i.id}"`
                  )
                : ''),
        true
    );
}

function receiptModal(id) {
    const p = db.payments.find((p) => p.id === id),
        i = db.invoices.find((i) => i.id === p?.invoiceId),
        a = appointment(i?.appointmentId);
    if (!p || !i) throw new Error('Không tìm thấy biên lai.');
    openModal(
        'Biên lai giao dịch',
        p.id,
        /* HTML */ `<div class="receipt-paper">
                <div class="receipt-brand">${brand()}</div>
                <h2>BIÊN LAI THU PHÍ</h2>
                <div><span>Mã lịch hẹn</span><strong>${a.code}</strong></div>
                <div><span>Ngày giao dịch</span><strong>${fmtDate(p.date)}</strong></div>
                <div class="receipt-total">
                    <span>${p.type === 'REFUND' ? 'Số tiền hoàn' : 'Số tiền đã thu'}</span
                    ><strong>${money(p.amount)}</strong>
                </div>
                <p>Biên lai minh họa · MediCare HIS</p>
            </div>
            <p class="fine-print no-print">
                Email nhận biên lai: ${esc(p.receiptEmail || 'Chưa cung cấp')}. Bản mẫu không gửi
                email ra bên ngoài.
            </p>`,
        button('Đóng', 'close') + button('In biên lai', 'print', 'printer', 'btn primary')
    );
}

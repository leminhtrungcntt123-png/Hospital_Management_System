/* views / staff / billing */
function cashierPage() {
    const list = db.invoices.filter(
            (i) =>
                appointment(i.appointmentId)?.deptId === acc().deptId &&
                (ui.filter === 'ALL' || invoiceTotal(i).status === ui.filter) &&
                matched(i.id + ' ' + patient(i.patientId).name)
        ),
        all = db.invoices.filter((i) => appointment(i.appointmentId)?.deptId === acc().deptId);
    return /* HTML */ `${heading('Thu phí & BHYT', 'Đối chiếu thẻ theo ngày khám, theo dõi tạm ứng và các khoản còn phải thu.', button('Xuất sổ thu phí', 'export', 'download', 'btn outline'))}
        <div class="stats-grid three">
            ${stat('Tổng người bệnh trả', money(all.reduce((n, i) => n + invoiceTotal(i).due, 0)), 'wallet', 'Sau khi áp dụng BHYT', 'cyan')}${stat('Đã ghi nhận thu', money(all.reduce((n, i) => n + invoiceTotal(i).paid, 0)), 'check', 'Tạm ứng + thanh toán − hoàn tiền', 'blue')}${stat('Còn phải thu', money(all.reduce((n, i) => n + invoiceTotal(i).balance, 0)), 'clock', 'Theo dõi từng hóa đơn', 'amber')}
        </div>
        <section class="card">
            ${tabs(
                [
                    ['ALL', 'Tất cả'],
                    ['UNPAID', 'Chưa thu'],
                    ['PARTIALLY_PAID', 'Thu một phần'],
                    ['PAID', 'Đã thu đủ'],
                ],
                ui.filter
            )}
            <div class="toolbar">${searchInput('Tìm mã hóa đơn hoặc người khám…')}</div>
            ${invoicesTable(list)}
        </section>`;
}

function invoicesTable(list, admin = false) {
    return list.length
        ? /* HTML */ `<div class="table-wrap">
              <table>
                  <thead>
                      <tr>
                          <th>Hóa đơn / Ngày khám</th>
                          <th>Người khám</th>
                          <th>Tổng chi phí</th>
                          <th>BHYT</th>
                          <th>Đã thu</th>
                          <th>Còn lại</th>
                          <th>Trạng thái</th>
                          <th></th>
                      </tr>
                  </thead>
                  <tbody>
                      ${list
                          .map((i) => {
                              const t = invoiceTotal(i);
                              return /* HTML */ `<tr>
                                  <td><strong>${i.id}</strong><small>${fmtDate(i.date)}</small></td>
                                  <td>${personCell(patient(i.patientId))}</td>
                                  <td>${money(t.gross)}</td>
                                  <td>${money(t.covered)}</td>
                                  <td>${money(t.paid)}</td>
                                  <td>
                                      <strong class="${t.balance ? 'amber-text' : ''}"
                                          >${money(t.balance)}</strong
                                      >
                                  </td>
                                  <td>${badge(t.status)}</td>
                                  <td>
                                      ${button(admin ? 'Chi tiết tài chính' : 'Mở hóa đơn', 'invoice-detail', 'arrow', 'btn outline sm', `data-id="${i.id}"`)}
                                  </td>
                              </tr>`;
                          })
                          .join('')}
                  </tbody>
              </table>
          </div>`
        : empty('Chưa có hóa đơn phù hợp');
}

function revenuePage() {
    const totals = db.invoices.map(invoiceTotal);
    return /* HTML */ `${heading('Doanh thu & thu phí', 'Tổng hợp từ giao dịch tạm ứng, thanh toán, hoàn tiền và phần BHYT đã áp dụng.', button('Xuất CSV', 'export', 'download', 'btn primary'))}
        <section class="stats-grid">
            ${stat('Tổng chi phí', money(totals.reduce((n, t) => n + t.gross, 0)), 'file', 'Toàn bộ hóa đơn mẫu', 'neutral')}${stat('BHYT đã áp dụng', money(totals.reduce((n, t) => n + t.covered, 0)), 'shield', 'Tỷ lệ chụp tại lúc tính tiền', 'blue')}${stat('Đã thu thực tế', money(totals.reduce((n, t) => n + t.paid, 0)), 'wallet', 'Đã trừ các khoản hoàn tiền', 'cyan')}${stat('Chưa thu đủ', money(totals.reduce((n, t) => n + t.balance, 0)), 'clock', 'Số dư người bệnh còn phải trả', 'amber')}
        </section>
        <section class="card">
            ${cardHeader('Sổ hóa đơn', 'Chỉ hiển thị dữ liệu tài chính, không có nội dung bệnh án', '', 'wallet')}${invoicesTable(db.invoices, true)}
        </section>
        <section class="card space-top">
            ${cardHeader('Lịch sử giao dịch', '', '', 'clock')}${paymentsTable(db.payments, true)}
        </section>`;
}

function paymentsTable(payments, admin = false) {
    return payments.length
        ? /* HTML */ `<div class="table-wrap">
              <table>
                  <thead>
                      <tr>
                          <th>Mã giao dịch</th>
                          <th>Hóa đơn</th>
                          <th>Loại</th>
                          <th>Số tiền</th>
                          <th>Phương thức</th>
                          <th>Ngày</th>
                          <th></th>
                      </tr>
                  </thead>
                  <tbody>
                      ${payments
                          .map(
                              (p) =>
                                  /* HTML */ `<tr>
                                      <td>${p.id}</td>
                                      <td>${p.invoiceId}</td>
                                      <td>
                                          <span
                                              class="pill ${p.type === 'REFUND' ? 'amber' : 'cyan'}"
                                              >${{ DEPOSIT: 'Tạm ứng', PAYMENT: 'Thanh toán', REFUND: 'Hoàn tiền' }[p.type]}</span
                                          >
                                      </td>
                                      <td>
                                          <strong
                                              >${p.type === 'REFUND' ? '−' : ''}${money(p.amount)}</strong
                                          >
                                      </td>
                                      <td>${p.method === 'CASH' ? 'Tiền mặt' : 'Chuyển khoản'}</td>
                                      <td>${fmtDate(p.date)}</td>
                                      <td>
                                          ${button('Biên lai', 'receipt', 'printer', 'text-link', `data-id="${p.id}"`)}
                                      </td>
                                  </tr>`
                          )
                          .join('')}
                  </tbody>
              </table>
          </div>`
        : empty('Chưa có giao dịch');
}

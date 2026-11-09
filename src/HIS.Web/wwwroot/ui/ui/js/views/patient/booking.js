function bookingPage() {
    return `${heading('Đặt lịch khám', 'Chọn người khám, bác sĩ và thời gian cho cuộc hẹn.')}
        ${unavailableNotice()}
        <ol class="booking-steps">${['Khám cho ai', 'Chọn bác sĩ', 'Ngày & giờ', 'Xác nhận'].map((title, index) => `<li><span>${index + 1}</span><strong>${title}</strong></li>`).join('')}</ol>
        <div class="booking-layout"><section class="card booking-panel"><h2>Thông tin lịch khám</h2>
            <fieldset disabled><div class="form-grid">
                <div class="field full"><label for="booking-patient">Người khám</label><select id="booking-patient"><option>Chưa tải được hồ sơ người khám</option></select></div>
                <div class="field"><label for="booking-doctor">Bác sĩ</label><select id="booking-doctor"><option>Chưa có lịch khám để lựa chọn</option></select></div>
                <div class="field"><label for="booking-date">Ngày khám</label><input id="booking-date" type="date" /></div>
                <div class="field full"><label for="booking-slot">Khung giờ</label><select id="booking-slot"><option>Chưa có khung giờ từ bệnh viện</option></select></div>
                <div class="field full"><label for="booking-reason">Lý do khám</label><textarea id="booking-reason" rows="3"></textarea></div>
            </div><div class="booking-actions"><button class="btn primary" type="button" disabled>Xác nhận đặt lịch</button></div></fieldset>
        </section><aside class="card booking-summary"><span class="eyebrow">CUỘC HẸN CỦA BẠN</span><h2>Chưa có lịch khám được chọn</h2>
            <p>Lịch hẹn và mã xác nhận sẽ hiển thị khi hệ thống tiếp nhận yêu cầu thành công.</p>
        </aside></div>`;
}

function appointmentsPage() {
    return `${heading('Lịch hẹn của tôi', 'Theo dõi trạng thái lịch khám và thông tin tiếp nhận.', link('Đặt lịch khám', 'patient/booking', 'plus', 'btn primary'))}
        ${unavailableNotice()}${pendingTable('Danh sách lịch hẹn', ['Mã lịch', 'Người khám', 'Bác sĩ / phòng', 'Ngày giờ', 'Trạng thái'])}`;
}

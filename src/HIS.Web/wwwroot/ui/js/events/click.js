/* UI click actions. Registered by bindEvents in interactions.js. */
const handleUiClick = async (e) => {
    const el = e.target.closest('[data-act],[data-route]');
    if (!el) return;
    if (el.dataset.route) {
        e.preventDefault();
        go(el.dataset.route);
        return;
    }
    const a = el.dataset.act,
        id = el.dataset.id,
        value = el.dataset.value;
    try {
        switch (a) {
            case 'close':
                closeModal();
                break;
            case 'menu':
                ui.mobileMenu = !ui.mobileMenu;
                render();
                break;
            case 'roles':
                rolesModal();
                break;
            case 'role':
                switchRole(el.dataset.role);
                break;
            case 'patient-portal':
                switchRole('PATIENT');
                break;
            case 'account-menu':
                openModal(
                    'Không gian của bạn',
                    acc()?.name || 'Khách',
                    /* HTML */ `<div class="menu-links">
                        ${link('Lịch hẹn của tôi', 'patient/appointments', 'calendar')}${link('Hồ sơ người khám', 'patient/family', 'users')}${link('Hồ sơ & kết quả', 'patient/records', 'clipboard')}${link('Thông báo', 'patient/notifications', 'bell')}${link('Tài khoản', 'account/settings', 'shield')}
                    </div>`,
                    button('Chuyển vai trò', 'roles', 'users', 'btn outline') +
                        button('Đóng', 'close')
                );
                break;
            case 'search':
                openModal(
                    'Tìm bác sĩ hoặc dịch vụ',
                    'Chọn nội dung bạn muốn khám phá.',
                    /* HTML */ `<form id="global-search-form">
                        <label class="search-input large"
                            >${icon('search')}<input
                                name="query"
                                placeholder="Nhập tên bác sĩ, chuyên khoa hoặc dịch vụ…"
                                required
                        /></label>
                        <div class="inline-actions">
                            <button class="btn primary" type="submit" name="target" value="doctors">
                                Tìm bác sĩ</button
                            ><button
                                class="btn outline"
                                type="submit"
                                name="target"
                                value="services"
                            >
                                Tìm dịch vụ
                            </button>
                        </div>
                    </form>`
                );
                break;
            case 'staff-search':
                openModal(
                    session.role === 'ADMIN' ? 'Tìm tài khoản' : 'Tìm người khám',
                    'Tra cứu theo không gian đang sử dụng.',
                    /* HTML */ `<form id="staff-search-form">
                        <div class="field">
                            <label for="staff-query">Tên, mã hoặc điện thoại</label
                            ><input id="staff-query" name="query" required />
                        </div>
                    </form>`,
                    button('Đóng', 'close') +
                        '<button class="btn primary" form="staff-search-form" type="submit">Tìm người khám</button>'
                );
                break;
            case 'staff-notices':
                openModal(
                    'Công việc cần chú ý',
                    'Tổng hợp cập nhật trong phiên trải nghiệm.',
                    /* HTML */ `<div class="notification-summary">
                        <p>
                            ${todayApps().filter((a) => a.status === 'CHECKED_IN').length} lượt đang
                            chờ ghi sinh hiệu.
                        </p>
                        <p>
                            ${db.exceptions.filter((x) => x.status === 'PENDING').length} đề nghị
                            lịch đang chờ duyệt.
                        </p>
                        <p>
                            ${db.invoices.filter((i) => invoiceTotal(i).balance > 0).length} hóa đơn
                            chưa thu đủ.
                        </p>
                    </div>`,
                    button('Đóng', 'close')
                );
                break;
            case 'book-doctor':
            case 'specialty':
                beginBooking(id);
                break;
            case 'book-service': {
                const s = db.services.find((s) => s.id === id),
                    d = db.doctors.find((d) => d.specialty === s.specialty) || db.doctors[0];
                beginBooking(d.id);
                break;
            }
            case 'book-profile':
                beginBooking('', id);
                break;
            case 'booking-profile':
                ui.booking.patientId = id;
                render();
                break;
            case 'booking-method':
                ui.booking.method = value;
                ui.query = '';
                render();
                break;
            case 'booking-doctor':
                ui.booking.doctorId = id;
                ui.booking.time = '';
                render();
                break;
            case 'booking-date':
                ui.booking.date = el.dataset.date;
                ui.booking.time = '';
                render();
                break;
            case 'booking-shift':
                ui.booking.shift = value;
                ui.booking.time = '';
                render();
                break;
            case 'booking-time':
                ui.booking.time = value;
                render();
                break;
            case 'booking-next':
                if (ui.booking.step === 1 && !linkFor(ui.booking.patientId))
                    throw new Error('Hãy chọn người khám.');
                ui.booking.step = Math.min(3, ui.booking.step + 1);
                ui.query = '';
                render();
                window.scrollTo(0, 0);
                break;
            case 'booking-prev':
                ui.booking.step = Math.max(1, ui.booking.step - 1);
                render();
                break;
            case 'hold-booking':
                holdBooking();
                window.scrollTo(0, 0);
                break;
            case 'confirm-booking':
                confirmBooking();
                break;
            case 'release-hold': {
                const old = appointment(ui.booking.holdId);
                if (old?.status === 'PENDING') transition(old, 'CANCELLED');
                ui.booking.holdId = null;
                ui.booking.step = 3;
                ui.booking.time = '';
                save();
                render();
                break;
            }
            case 'new-family':
                familyModal();
                break;
            case 'link-family':
                linkFamilyModal();
                break;
            case 'open-profile-records':
                ui.profileId = id;
                go('patient/records');
                break;
            case 'record-profile':
                ui.profileId = id;
                render();
                break;
            case 'access-help':
                openModal(
                    'Quyền đặt lịch & xem hồ sơ',
                    'Quyền của từng người khám được xác minh riêng.',
                    /* HTML */ `<div class="content-list">
                        <p>
                            <strong>Đặt lịch:</strong> hồ sơ đã xác minh hoặc chờ xác minh đều đặt
                            được.
                        </p>
                        <p>
                            <strong>Xem bệnh án:</strong> cần liên kết đã xác minh và được cấp quyền
                            xem.
                        </p>
                        <p>
                            Với người trưởng thành, quyền xem cần sự đồng ý của người bệnh và được
                            nhân viên ghi nhận. Hồ sơ Con khi đủ 18 tuổi cần xác minh lại.
                        </p>
                    </div>`,
                    button('Đóng', 'close')
                );
                break;
            case 'revoke-link': {
                const l = db.links.find((l) => l.id === id && l.accountId === session.accountId);
                if (!l || l.relationship === 'SELF')
                    throw new Error('Không thể thu hồi hồ sơ bản thân tại đây.');
                openModal(
                    'Thu hồi liên kết?',
                    patient(l.patientId).name,
                    /* HTML */ `<p>
                        Bạn sẽ không đặt thêm lịch hoặc xem hồ sơ qua liên kết này. Các lịch hẹn đã
                        tạo vẫn được lưu trong tài khoản đặt lịch.
                    </p>`,
                    button('Giữ liên kết', 'close') +
                        button(
                            'Xác nhận thu hồi',
                            'confirm-revoke',
                            'close',
                            'btn danger',
                            `data-id="${id}"`
                        )
                );
                break;
            }
            case 'confirm-revoke': {
                const l = db.links.find((l) => l.id === id && l.accountId === session.accountId);
                if (!l || l.relationship === 'SELF') throw new Error('Liên kết không hợp lệ.');
                l.status = 'REVOKED';
                l.canView = false;
                audit(
                    'REVOKE_LINK',
                    l.patientId,
                    'Chủ tài khoản thu hồi liên kết',
                    'Hồ sơ người khám'
                );
                save();
                closeModal();
                render();
                toast('Đã thu hồi liên kết.');
                break;
            }
            case 'edit-self': {
                const p = patient(id || selfPatient());
                if (!p || session.role !== 'PATIENT' || p.id !== selfPatient())
                    throw new Error('Chỉ cập nhật thông tin liên hệ của bản thân.');
                openModal(
                    'Cập nhật liên hệ',
                    p.name,
                    /* HTML */ `<form id="contact-form" data-id="${p.id}">
                        <div class="field">
                            <label for="contact-phone">Điện thoại *</label
                            ><input
                                id="contact-phone"
                                name="phone"
                                type="tel"
                                pattern="[0-9+ ()-]{9,16}"
                                value="${p.phone}"
                                required
                            />
                        </div>
                        <div class="field space-top">
                            <label for="contact-address">Địa chỉ</label
                            ><input id="contact-address" name="address" value="${esc(p.address)}" />
                        </div>
                        <div class="field space-top">
                            <label for="contact-email">Email liên hệ</label
                            ><input
                                id="contact-email"
                                name="email"
                                type="email"
                                value="${esc(acc().email || '')}"
                            />
                        </div>
                    </form>`,
                    button('Đóng', 'close') +
                        '<button class="btn primary" form="contact-form" type="submit">Lưu liên hệ</button>'
                );
                break;
            }
            case 'appointment-detail':
                appointmentDetail(id);
                break;
            case 'cancel-appointment':
                openModal(
                    'Hủy lịch hẹn?',
                    appointment(id).code,
                    /* HTML */ `<p>
                        Khung giờ sẽ được mở lại sau khi hủy. Thao tác được lưu trong lịch sử lịch
                        hẹn.
                    </p>`,
                    button('Giữ lịch', 'close') +
                        button(
                            'Xác nhận hủy',
                            'confirm-cancel',
                            'close',
                            'btn danger',
                            `data-id="${id}"`
                        )
                );
                break;
            case 'confirm-cancel': {
                const a = appointment(id);
                if (a.bookedBy !== session.accountId)
                    throw new Error('Bạn không phải tài khoản đặt lịch này.');
                if (
                    a.status === 'CONFIRMED' &&
                    appointmentTime(a) - demoNow() < CONFIG.cancelCutoff * 60000
                )
                    throw new Error('Không hủy trực tuyến trong vòng 120 phút trước hẹn.');
                if (!['PENDING', 'CONFIRMED', 'RESCHEDULE_REQUIRED'].includes(a.status))
                    throw new Error('Trạng thái lịch không thể hủy.');
                transition(a, 'CANCELLED');
                notify(a, 'Lịch hẹn đã hủy', a.time + ' ngày ' + fmtDate(a.date));
                save();
                closeModal();
                render();
                toast('Đã hủy lịch và cập nhật trạng thái.');
                break;
            }
            case 'reschedule': {
                const a = appointment(id);
                if (a.bookedBy !== session.accountId || a.status !== 'RESCHEDULE_REQUIRED')
                    throw new Error('Lịch này chưa thuộc diện cần đặt lại.');
                closeModal();
                beginBooking(a.doctorId, a.patientId, a.id);
                ui.booking.step = 3;
                render();
                break;
            }
            case 'walkin':
                walkinModal();
                break;
            case 'checkin':
                requireRole('NURSE');
                checkinModal(id);
                break;
            case 'vitals':
                requireRole('NURSE');
                vitalsModal(id);
                break;
            case 'patient-detail':
                patientDetail(id);
                break;
            case 'patient-history':
                patientDetail(id);
                break;
            case 'record-detail':
                recordModal(id);
                break;
            case 'open-work':
                if (session.role === 'DEPARTMENT_HEAD') {
                    go('staff/records');
                } else {
                    go(value === 'CHECKED_IN' ? 'staff/vitals' : 'staff/reception', {
                        filter: value,
                    });
                }
                break;
            case 'open-cashier':
                go('staff/cashier');
                break;
            case 'open-revenue':
                go('admin/revenue');
                break;
            case 'open-accounts':
                go('admin/accounts');
                break;
            case 'call-next': {
                requireRole('NURSE');
                const candidates = todayApps()
                    .filter(
                        (a) =>
                            a.status === 'READY_FOR_DOCTOR' &&
                            !a.calledAt &&
                            (ui.roomId === 'ALL' || a.roomId === ui.roomId) &&
                            !db.appointments.some(
                                (other) =>
                                    other.id !== a.id &&
                                    other.date === TODAY &&
                                    other.roomId === a.roomId &&
                                    (other.status === 'IN_PROGRESS' ||
                                        (other.status === 'READY_FOR_DOCTOR' && other.calledAt))
                            )
                    )
                    .sort((a, b) => b.priority - a.priority || a.time.localeCompare(b.time));
                if (!candidates.length)
                    throw new Error(
                        'Chưa có phòng sẵn sàng để gọi thêm. Chọn phòng hoặc chờ lượt đang khám hoàn tất.'
                    );
                const a = candidates[0];
                a.calledAt = stamp();
                save();
                render();
                toast('Mời số ' + a.queue + ' đến phòng ' + room(a.roomId).number + '.');
                break;
            }
            case 'recall':
                toast(
                    'Mời số ' +
                        appointment(id).queue +
                        ' đến phòng ' +
                        room(appointment(id).roomId).number +
                        '.'
                );
                break;
            case 'select-consult':
                persistDraft();
                ui.selectedAppointment = id;
                render();
                break;
            case 'begin-consult': {
                requireRole('DOCTOR');
                const a = appointment(id);
                if (a.doctorId !== acc().doctorId)
                    throw new Error('Không phải bệnh nhân do bạn phụ trách.');
                transition(a, 'IN_PROGRESS');
                getOrCreateDraft(a);
                audit('EDIT_RECORD', a.patientId, 'Bắt đầu phiếu khám', 'Bệnh án');
                save();
                render();
                toast('Đã bắt đầu khám.');
                break;
            }
            case 'save-draft':
                persistDraft();
                audit(
                    'EDIT_RECORD',
                    appointment($('#consult-form').dataset.id).patientId,
                    'Lưu nháp phiếu khám',
                    'Bệnh án'
                );
                toast('Đã lưu nháp phiếu khám.');
                break;
            case 'add-rx':
                $('#rx-rows').insertAdjacentHTML('beforeend', rxRow());
                $('#rx-rows').lastElementChild.querySelector('select').focus();
                break;
            case 'remove-rx':
                el.closest('.rx-row').remove();
                break;
            case 'new-lab':
                labModal(id);
                break;
            case 'lab-result':
                labResultModal(id, true);
                break;
            case 'lab-view':
                labResultModal(id);
                break;
            case 'invoice-detail':
                invoiceDetail(id);
                break;
            case 'payment':
                paymentModal(id);
                break;
            case 'receipt':
                receiptModal(id);
                break;
            case 'print':
                window.print();
                break;
            case 'export':
                requireRole('NURSE', 'ADMIN');
                exportCsv();
                break;
            case 'prev-week':
                ui.scheduleOffset--;
                render();
                break;
            case 'next-week':
                ui.scheduleOffset++;
                render();
                break;
            case 'this-week':
                ui.scheduleOffset = 0;
                render();
                break;
            case 'new-schedule':
                scheduleModal();
                break;
            case 'edit-schedule':
                scheduleModal(id);
                break;
            case 'new-duty':
                dutyModal();
                break;
            case 'new-exception':
                exceptionModal();
                break;
            case 'approve-exception': {
                requireRole('DEPARTMENT_HEAD');
                const ex = db.exceptions.find((e) => e.id === id),
                    affected = db.appointments.filter(
                        (a) =>
                            a.doctorId === ex.doctorId &&
                            a.date === ex.date &&
                            ['PENDING', 'CONFIRMED'].includes(a.status)
                    );
                openModal(
                    'Duyệt thay đổi ca khám?',
                    doctor(ex.doctorId).name + ' · ' + fmtDate(ex.date),
                    /* HTML */ `<div class="review-grid">
                            <div>
                                <small>Lượt cần xử lý</small><strong>${affected.length}</strong>
                            </div>
                            <div>
                                <small>Đề xuất thay thế</small
                                ><strong
                                    >${ex.replacementId ? 'BS. ' + doctor(ex.replacementId).name : 'Điều phối trong khoa'}</strong
                                >
                            </div>
                        </div>
                        <p class="fine-print">
                            Lịch hẹn được cập nhật cùng thông báo gửi về tài khoản đặt. Lượt hết chỗ
                            thay thế cần chọn lại lịch.
                        </p>`,
                    button('Quay lại', 'close') +
                        button(
                            'Duyệt & cập nhật lịch',
                            'confirm-exception',
                            'check',
                            'btn primary',
                            `data-id="${id}"`
                        )
                );
                break;
            }
            case 'confirm-exception':
                closeModal();
                exceptionDecision(id, true);
                break;
            case 'reject-exception':
                exceptionDecision(id, false);
                break;
            case 'new-account':
                accountModal();
                break;
            case 'edit-account':
                accountModal(id);
                break;
            case 'toggle-account': {
                requireRole('ADMIN');
                const a = db.accounts.find((a) => a.id === id);
                if (a.id === session.accountId)
                    throw new Error('Không thể khóa tài khoản quản trị đang sử dụng.');
                a.active = !a.active;
                audit('UPDATE_ACCOUNT', '', 'Đổi trạng thái tài khoản ' + a.username, 'Tài khoản');
                save();
                render();
                toast(a.active ? 'Đã mở tài khoản mẫu.' : 'Đã khóa tài khoản mẫu.');
                break;
            }
            case 'reset-account-password':
                passwordModal(id, true);
                break;
            case 'change-password':
                passwordModal();
                break;
            case 'new-catalog':
                catalogModal();
                break;
            case 'edit-catalog':
                catalogModal(id);
                break;
            case 'tab':
                ui[el.dataset.kind] = value;
                ui.query = '';
                render();
                break;
            case 'anchor':
                document.getElementById(id)?.scrollIntoView({ behavior: 'smooth', block: 'start' });
                break;
            case 'read-all':
                for (const n of db.notifications)
                    if (n.accountId === session.accountId) n.read = true;
                save();
                render();
                toast('Đã đánh dấu tất cả thông báo là đã đọc.');
                break;
            case 'read-notice': {
                const n = db.notifications.find(
                    (n) => n.id === id && n.accountId === session.accountId
                );
                if (!n) throw new Error('Không tìm thấy thông báo.');
                n.read = true;
                save();
                go('patient/appointments');
                if (n.appointmentId) appointmentDetail(n.appointmentId);
                break;
            }
            case 'logout':
                session.authenticated = false;
                session.role = 'PATIENT';
                session.accountId = 'ac-p';
                go('account/login');
                toast('Đã thoát phiên tài khoản mẫu.');
                break;
            case 'privacy':
                openModal(
                    'Quyền riêng tư trong bản mẫu',
                    'MediCare HIS',
                    /* HTML */ `<div class="content-list">
                        <p>
                            Dữ liệu được lưu trong trình duyệt của bạn. Website này là giao diện
                            trải nghiệm phục vụ đồ án.
                        </p>
                        <p>
                            Quyền xem bệnh án được minh họa theo vai trò và liên kết người khám.
                            Admin không có màn xem nội dung bệnh án.
                        </p>
                        <p>
                            Hãy dùng dữ liệu giả lập. Bản mẫu không gửi email, không thanh toán thật
                            và không thay thế hệ thống bệnh viện đang vận hành.
                        </p>
                    </div>`,
                    button('Đóng', 'close')
                );
                break;
            case 'guide-modal':
                openModal(
                    'Trải nghiệm hệ thống HIS',
                    'Chọn vai trò ở đầu trang để kiểm tra các phần liên thông.',
                    /* HTML */ `<div class="process-list">
                        ${[
                            ['Bệnh nhân', 'Chọn người khám → bác sĩ → giữ chỗ → xác nhận.'],
                            [
                                'Y tá / Lễ tân',
                                'Tiếp nhận → xác minh liên kết → đo sinh hiệu → gọi số.',
                            ],
                            ['Bác sĩ', 'Bắt đầu khám → chỉ định / đơn thuốc → hoàn tất.'],
                            [
                                'Thu phí',
                                'Áp dụng BHYT → ghi nhận tạm ứng / thanh toán → xem biên lai.',
                            ],
                            [
                                'Trưởng khoa & Admin',
                                'Duyệt đổi ca, phân trực; quản lý tài khoản, danh mục và nhật ký.',
                            ],
                        ]
                            .map(
                                ([t, d], i) =>
                                    /* HTML */ `<article>
                                        <span>${i + 1}</span>
                                        <div>
                                            <h3>${t}</h3>
                                            <p>${d}</p>
                                        </div>
                                    </article>`
                            )
                            .join('')}
                    </div>`,
                    button('Chọn vai trò', 'roles', 'users', 'btn primary') +
                        button('Đóng', 'close')
                );
                break;
            case 'reset':
                openModal(
                    'Khôi phục dữ liệu mẫu?',
                    'Tất cả thao tác thử trong bản HIS v3 sẽ được đặt lại.',
                    /* HTML */ `<p>
                        Lịch hẹn, hồ sơ liên kết, phiếu khám, hóa đơn và danh mục sẽ trở về trạng
                        thái minh họa ban đầu.
                    </p>`,
                    button('Giữ dữ liệu', 'close') +
                        button('Khôi phục', 'confirm-reset', 'refresh', 'btn danger')
                );
                break;
            case 'confirm-reset':
                db = seed();
                save();
                closeModal();
                ui.selectedAppointment = null;
                ui.filter = 'ALL';
                ui.query = '';
                render();
                toast('Đã khôi phục dữ liệu mẫu HIS.');
                break;
        }
    } catch (err) {
        toast(err.message, true);
    }
};

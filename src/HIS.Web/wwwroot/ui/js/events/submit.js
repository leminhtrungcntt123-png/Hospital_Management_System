/* UI submit actions. Registered by bindEvents in interactions.js. */
const handleUiSubmit = async (e) => {
    const form = e.target;
    if (!form.id) return;
    e.preventDefault();
    const f = new FormData(form),
        id = form.dataset.id;
    try {
        switch (form.id) {
            case 'global-search-form': {
                const target = e.submitter?.value || 'doctors',
                    q = String(f.get('query')).trim();
                closeModal();
                go('public/' + target);
                ui.query = q;
                render();
                break;
            }
            case 'staff-search-form': {
                const q = String(f.get('query')).trim();
                closeModal();
                go(session.role === 'ADMIN' ? 'admin/accounts' : 'staff/patients');
                ui.query = q;
                render();
                break;
            }
            case 'family-form': {
                requireRole('PATIENT');
                if (myLinks().length >= 5) throw new Error('Bạn đã sử dụng đủ 5 hồ sơ.');
                const p = newPatient(f),
                    duplicate = db.patients.some(
                        (x) => x.name === p.name && x.dob === p.dob && x.phone === p.phone
                    ),
                    relation = f.get('relationship');
                db.patients.push(p);
                db.links.push({
                    id: uid('l'),
                    accountId: session.accountId,
                    patientId: p.id,
                    relationship: relation,
                    status: 'ACTIVE',
                    canView: relation === 'CHILD' && age(p.dob) < 18,
                });
                audit('LINK_PATIENT', p.id, 'Tạo và liên kết hồ sơ mới', 'Hồ sơ người khám');
                save();
                closeModal();
                if (ui.route === 'patient/booking') {
                    ui.booking.patientId = p.id;
                    render();
                } else go('patient/family');
                toast(
                    duplicate
                        ? 'Đã tạo hồ sơ; có thông tin trùng, vui lòng đối chiếu tại quầy.'
                        : 'Đã thêm hồ sơ người khám.'
                );
                break;
            }
            case 'link-family-form': {
                requireRole('PATIENT');
                if (myLinks().length >= 5) throw new Error('Bạn đã sử dụng đủ 5 hồ sơ.');
                if (
                    db.linkRequests.filter(
                        (r) => r.accountId === session.accountId && r.date === TODAY
                    ).length >= 5
                )
                    throw new Error('Đã đạt giới hạn 5 yêu cầu liên kết trong ngày.');
                db.linkRequests.push({ accountId: session.accountId, date: TODAY });
                save();
                const norm = (s) =>
                        String(s || '')
                            .normalize('NFD')
                            .replace(/\p{M}/gu, '')
                            .replace(/[đĐ]/g, 'd')
                            .toLowerCase()
                            .trim()
                            .replace(/\s+/g, ' '),
                    p = db.patients.find((p) => {
                        const identity =
                                norm(p.name) === norm(f.get('name')) && p.dob === f.get('dob'),
                            national =
                                f.get('nationalId') &&
                                norm(p.nationalId) === norm(f.get('nationalId')),
                            insurance =
                                f.get('cardNumber') &&
                                db.insuranceCards.some(
                                    (c) =>
                                        c.patientId === p.id &&
                                        norm(c.number) === norm(f.get('cardNumber'))
                                ),
                            phone = f.get('phone') && p.phone === String(f.get('phone')).trim();
                        return (
                            identity &&
                            (age(p.dob) < 18
                                ? national || insurance || phone
                                : national || insurance)
                        );
                    });
                if (!p)
                    throw new Error(
                        'Thông tin không khớp. Vui lòng kiểm tra hoặc liên hệ quầy tiếp nhận.'
                    );
                if (myLinks().some((l) => l.patientId === p.id))
                    throw new Error('Hồ sơ đã có trong danh sách liên kết của bạn.');
                db.links.push({
                    id: uid('l'),
                    accountId: session.accountId,
                    patientId: p.id,
                    relationship: f.get('relationship'),
                    status: 'PENDING',
                    canView: false,
                    createdAt: stamp(),
                });
                audit('LINK_PATIENT', p.id, 'Gửi yêu cầu chờ xác minh', 'Hồ sơ người khám');
                save();
                closeModal();
                go('patient/family');
                toast('Đã gửi yêu cầu. Quyền xem bệnh án chờ xác minh tại quầy.');
                break;
            }
            case 'contact-form': {
                const p = patient(id);
                if (p.id !== selfPatient()) throw new Error('Bạn chỉ cập nhật hồ sơ bản thân.');
                p.phone = String(f.get('phone')).trim();
                p.address = String(f.get('address') || '').trim();
                acc().email = String(f.get('email') || '')
                    .trim()
                    .toLowerCase();
                save();
                closeModal();
                render();
                toast('Đã lưu thông tin liên hệ.');
                break;
            }
            case 'walkin-form': {
                requireRole('NURSE');
                const d = doctor(f.get('doctorId')),
                    time = f.get('time'),
                    shift = minuteOf(time) < 720 ? 'MORNING' : 'AFTERNOON',
                    sc = db.schedules.find((s) => s.doctorId === d.id),
                    count = db.appointments.filter(
                        (a) =>
                            a.doctorId === d.id &&
                            a.date === TODAY &&
                            a.channel === 'ON_SITE' &&
                            (a.shift || 'MORNING') === shift &&
                            !['CANCELLED', 'EXPIRED'].includes(a.status)
                    ).length;
                if (count >= sc.maxPatients - sc.onlineSlots)
                    throw new Error('Ca này đã đạt hạn mức tiếp nhận tại quầy.');
                const p = f.get('existing') === 'NEW' ? newPatient(f) : patient(f.get('existing'));
                if (
                    db.appointments.some(
                        (a) =>
                            a.patientId === p.id &&
                            a.deptId === d.deptId &&
                            a.date === TODAY &&
                            liveStates.includes(a.status)
                    )
                )
                    throw new Error('Người khám đã có lượt hiệu lực trong ngày tại khoa này.');
                if (f.get('existing') === 'NEW') db.patients.push(p);
                const a = {
                    id: uid('a'),
                    code:
                        'HIS-' +
                        TODAY.replaceAll('-', '') +
                        '-' +
                        Math.random().toString(36).slice(2, 8).toUpperCase(),
                    patientId: p.id,
                    bookedBy: session.accountId,
                    doctorId: d.id,
                    deptId: d.deptId,
                    roomId: d.roomId,
                    date: TODAY,
                    time,
                    shift,
                    status: 'CHECKED_IN',
                    channel: 'ON_SITE',
                    visitType: 'WALK_IN',
                    priority: Number(f.get('priority')),
                    reason: String(f.get('reason')).trim(),
                    checkedInAt: stamp(),
                    history: [{ to: 'CHECKED_IN', at: stamp(), by: acc().name }],
                };
                a.queue = nextQueue(a);
                db.appointments.push(a);
                audit('CHECK_IN', p.id, 'Khách vãng lai · ' + a.queue, 'Tiếp nhận');
                save();
                closeModal();
                go('staff/vitals');
                toast('Đã cấp số ' + a.queue + '. Tiếp tục ghi nhận sinh hiệu.');
                break;
            }
            case 'checkin-form': {
                requireRole('NURSE');
                const a = appointment(id);
                if (a.status !== 'CONFIRMED' || a.deptId !== acc().deptId)
                    throw new Error('Lượt khám không đủ điều kiện tiếp nhận.');
                if (a.date !== TODAY || appointmentTime(a) - demoNow() > 60 * 60000)
                    throw new Error('Chưa đến thời gian check-in.');
                const l = db.links.find(
                    (l) => l.accountId === a.bookedBy && l.patientId === a.patientId
                );
                if (l?.status === 'PENDING' && !f.has('verifyLink'))
                    throw new Error('Cần xác nhận liên kết người đặt hộ.');
                if (l && f.has('verifyLink')) {
                    l.status = 'ACTIVE';
                    l.verifiedBy = session.accountId;
                    l.verifiedAt = stamp();
                    audit(
                        'LINK_PATIENT',
                        l.patientId,
                        'Xác minh người đặt hộ tại quầy',
                        'Liên kết'
                    );
                }
                if (l && f.has('consent') && l.status === 'ACTIVE') {
                    l.canView = true;
                    l.verifiedBy = session.accountId;
                    l.verifiedAt = stamp();
                    l.verifiedAdult = true;
                    audit(
                        'GRANT_RECORD_ACCESS',
                        l.patientId,
                        'Người bệnh đồng ý cho xem bệnh án',
                        'Liên kết'
                    );
                }
                a.priority = Number(f.get('priority'));
                a.queue = nextQueue(a);
                a.checkedInAt = stamp();
                transition(a, 'CHECKED_IN');
                save();
                closeModal();
                render();
                toast('Đã tiếp nhận và cấp số ' + a.queue + '.');
                break;
            }
            case 'vitals-form': {
                requireRole('NURSE');
                const a = appointment(id);
                if (
                    a.deptId !== acc().deptId ||
                    !['CHECKED_IN', 'READY_FOR_DOCTOR'].includes(a.status)
                )
                    throw new Error('Không thể cập nhật sinh hiệu ở trạng thái hiện tại.');
                const data = Object.fromEntries(
                    ['pulse', 'bpSys', 'bpDia', 'temp', 'resp', 'spo2', 'weight', 'height'].map(
                        (k) => [k, Number(f.get(k))]
                    )
                );
                let v = db.vitals.find((v) => v.appointmentId === id);
                if (v) Object.assign(v, data, { by: session.accountId });
                else
                    db.vitals.push({
                        id: uid('v'),
                        appointmentId: id,
                        ...data,
                        by: session.accountId,
                    });
                if (a.status === 'CHECKED_IN') transition(a, 'READY_FOR_DOCTOR');
                audit('RECORD_VITALS', a.patientId, 'Ghi sinh hiệu trước khám', 'Sinh hiệu');
                save();
                closeModal();
                render();
                toast('Đã lưu sinh hiệu và chuyển vào hàng chờ bác sĩ.');
                break;
            }
            case 'consult-form':
                finishConsultation(form);
                break;
            case 'lab-order-form': {
                requireRole('DOCTOR');
                const a = appointment(id);
                if (a.doctorId !== acc().doctorId || a.status !== 'IN_PROGRESS')
                    throw new Error('Lượt khám không còn đủ điều kiện tạo chỉ định.');
                const r = getOrCreateDraft(a),
                    serviceId = f.get('serviceId');
                if (db.labOrders.some((l) => l.recordId === r.id && l.serviceId === serviceId))
                    throw new Error('Dịch vụ này đã được chỉ định trong phiếu khám.');
                db.labOrders.push({
                    id: uid('lab'),
                    recordId: r.id,
                    patientId: a.patientId,
                    doctorId: a.doctorId,
                    serviceId,
                    note: String(f.get('note') || '').trim(),
                    status: 'PENDING',
                    date: TODAY,
                });
                audit('EDIT_RECORD', a.patientId, 'Tạo chỉ định cận lâm sàng', 'Bệnh án');
                save();
                closeModal();
                render();
                toast('Đã tạo chỉ định.');
                break;
            }
            case 'lab-result-form': {
                requireRole('DOCTOR');
                const l = db.labOrders.find((l) => l.id === id);
                if (l.doctorId !== acc().doctorId)
                    throw new Error('Chỉ cập nhật chỉ định do mình phụ trách.');
                l.result = String(f.get('result')).trim();
                l.status = 'COMPLETED';
                l.by = session.accountId;
                audit('EDIT_RECORD', l.patientId, 'Cập nhật kết quả chỉ định', 'Cận lâm sàng');
                save();
                closeModal();
                render();
                toast('Đã lưu kết quả.');
                break;
            }
            case 'insurance-form': {
                requireRole('NURSE');
                const i = db.invoices.find((i) => i.id === id),
                    cardId = f.get('cardId'),
                    c = db.insuranceCards.find((c) => c.id === cardId);
                if (
                    cardId &&
                    (!c ||
                        c.patientId !== i.patientId ||
                        !c.active ||
                        c.from > i.date ||
                        c.to < i.date)
                )
                    throw new Error(
                        'Thẻ không thuộc người khám hoặc không còn hiệu lực tại ngày khám.'
                    );
                i.insuranceCardId = cardId || null;
                i.coveragePct = c ? db.insurancePlans.find((p) => p.id === c.planId).pct : 0;
                audit(
                    'APPLY_INSURANCE',
                    i.patientId,
                    'Áp dụng mức hưởng ' + i.coveragePct + '%',
                    'Thu phí'
                );
                save();
                render();
                invoiceDetail(id);
                toast('Đã cập nhật phần BHYT và số tiền còn lại.');
                break;
            }
            case 'payment-form': {
                requireRole('NURSE');
                const i = db.invoices.find((i) => i.id === id),
                    t = invoiceTotal(i),
                    amount = Number(f.get('amount')),
                    type = f.get('type');
                if (amount <= 0 || !Number.isFinite(amount))
                    throw new Error('Số tiền phải lớn hơn 0.');
                if (type === 'REFUND' && amount > t.paid)
                    throw new Error('Không hoàn quá tổng số tiền đã thu.');
                if (type !== 'REFUND' && amount > t.balance)
                    throw new Error('Khoản thu vượt số tiền còn phải trả.');
                const a = appointment(i.appointmentId),
                    l = db.links.find(
                        (l) =>
                            l.accountId === a.bookedBy &&
                            l.patientId === i.patientId &&
                            l.status === 'ACTIVE'
                    ),
                    fallback = l ? db.accounts.find((x) => x.id === a.bookedBy)?.email : '';
                const pay = {
                    id: uid('PT'),
                    invoiceId: id,
                    amount,
                    type,
                    method: f.get('method'),
                    cashierId: session.accountId,
                    date: TODAY,
                    note: String(f.get('note') || '').trim(),
                    receiptEmail: String(f.get('receiptEmail') || fallback || '')
                        .trim()
                        .toLowerCase(),
                };
                db.payments.unshift(pay);
                audit('RECORD_PAYMENT', i.patientId, type + ' · ' + money(amount), 'Thu phí');
                save();
                render();
                invoiceDetail(id);
                toast('Đã ghi nhận giao dịch và cập nhật số dư.');
                break;
            }
            case 'exception-form': {
                requireRole('DOCTOR');
                const data = {
                    id: uid('ex'),
                    doctorId: acc().doctorId,
                    date: f.get('date'),
                    shift: f.get('shift'),
                    type: f.get('type'),
                    replacementId: f.get('replacementId') || null,
                    reason: String(f.get('reason')).trim(),
                    status: 'PENDING',
                };
                if (data.date <= TODAY)
                    throw new Error('Chỉ đề nghị thay đổi cho ngày trong tương lai.');
                if (
                    db.exceptions.some(
                        (e) =>
                            e.doctorId === data.doctorId &&
                            e.date === data.date &&
                            e.shift === data.shift &&
                            e.status !== 'REJECTED'
                    )
                )
                    throw new Error('Bạn đã có đề nghị cho ngày và ca này.');
                db.exceptions.unshift(data);
                save();
                closeModal();
                go('staff/exceptions');
                toast('Đã gửi đề nghị đến trưởng khoa.');
                break;
            }
            case 'schedule-form': {
                requireRole('DEPARTMENT_HEAD');
                const max = Number(f.get('maxPatients')),
                    online = Number(f.get('onlineSlots')),
                    d = doctor(f.get('doctorId'));
                if (online > max)
                    throw new Error('Hạn mức trực tuyến không được vượt tổng số người khám.');
                let s =
                    db.schedules.find((s) => s.id === id) ||
                    db.schedules.find((s) => s.doctorId === d.id);
                const data = {
                    doctorId: d.id,
                    roomId: f.get('roomId'),
                    days: [1, 2, 3, 4, 5],
                    start: '07:30',
                    end: '16:30',
                    maxPatients: max,
                    onlineSlots: online,
                    slotMinutes: Number(f.get('slotMinutes')),
                    active: f.get('active') === 'true',
                };
                if (s) Object.assign(s, data);
                else db.schedules.push({ id: uid('sc'), ...data });
                d.roomId = data.roomId;
                save();
                closeModal();
                render();
                toast('Đã cập nhật lịch khám và hạn mức.');
                break;
            }
            case 'duty-form': {
                requireRole('DEPARTMENT_HEAD');
                if (new Date(f.get('end')) <= new Date(f.get('start')))
                    throw new Error('Giờ kết thúc phải sau giờ bắt đầu.');
                const aid = f.get('accountId');
                if (
                    db.dutyShifts.some(
                        (s) =>
                            s.accountId === aid && s.start < f.get('end') && s.end > f.get('start')
                    )
                )
                    throw new Error('Nhân sự đã có ca trực trùng thời gian.');
                db.dutyShifts.push({
                    id: uid('ds'),
                    accountId: aid,
                    deptId: acc().deptId,
                    start: f.get('start'),
                    end: f.get('end'),
                    note: String(f.get('note') || '').trim(),
                    assignedBy: session.accountId,
                });
                save();
                closeModal();
                render();
                toast('Đã phân công ca trực.');
                break;
            }
            case 'account-form': {
                requireRole('ADMIN');
                const username = plainName(f.get('username')),
                    existing = db.accounts.find((a) => a.id === id),
                    role = existing?.id === session.accountId ? existing.role : f.get('role'),
                    deptId = f.get('deptId') || null,
                    email = String(f.get('email') || '')
                        .trim()
                        .toLowerCase();
                if (
                    db.accounts.some(
                        (a) => a.id !== id && a.username.toLowerCase() === username.toLowerCase()
                    )
                )
                    throw new Error('Tên đăng nhập đã tồn tại.');
                if (email && db.accounts.some((a) => a.id !== id && a.email === email))
                    throw new Error('Email liên hệ đã được sử dụng.');
                if (['DOCTOR', 'NURSE', 'DEPARTMENT_HEAD'].includes(role) && !deptId)
                    throw new Error('Vui lòng chọn khoa cho nhân viên.');
                if (
                    role === 'DEPARTMENT_HEAD' &&
                    db.accounts.some(
                        (a) => a.id !== id && a.role === 'DEPARTMENT_HEAD' && a.deptId === deptId
                    )
                )
                    throw new Error('Khoa này đã có trưởng khoa. Hãy chỉnh tài khoản hiện có.');
                const data = {
                    name: plainName(f.get('name')),
                    username,
                    email,
                    role,
                    deptId,
                    active:
                        existing?.id === session.accountId
                            ? existing.active
                            : f.get('active') === 'true',
                };
                if (existing) {
                    Object.assign(existing, data);
                    if (existing.doctorId) {
                        const d = doctor(existing.doctorId);
                        if (d) {
                            d.name = existing.name;
                            d.active = existing.active;
                            d.deptId = existing.deptId || d.deptId;
                        }
                    }
                } else {
                    const a = { id: uid('ac'), ...data };
                    if (role === 'DOCTOR') {
                        const firstRoom = db.rooms.find((r) => r.deptId === deptId);
                        if (!firstRoom)
                            throw new Error('Khoa cần có phòng khám trước khi tạo bác sĩ.');
                        const d = {
                            id: uid('d'),
                            accountId: a.id,
                            name: a.name,
                            deptId,
                            roomId: firstRoom.id,
                            specialty: dept(deptId).name,
                            minutes: 15,
                            fee: 250000,
                            color: 'cyan',
                            focus: 'Thông tin bác sĩ mới trong bản mẫu.',
                            active: true,
                        };
                        db.doctors.push(d);
                        db.schedules.push({
                            id: uid('sc'),
                            doctorId: d.id,
                            roomId: d.roomId,
                            days: [1, 2, 3, 4, 5],
                            slotMinutes: 15,
                            maxPatients: 40,
                            onlineSlots: 28,
                            active: true,
                        });
                        a.doctorId = d.id;
                    }
                    db.accounts.push(a);
                }
                audit('UPDATE_ACCOUNT', '', 'Cập nhật tài khoản ' + username, 'Tài khoản');
                save();
                closeModal();
                render();
                toast('Đã lưu tài khoản mẫu.');
                break;
            }
            case 'catalog-form': {
                requireRole('ADMIN');
                const key = form.dataset.key,
                    item = db[key].find((x) => x.id === id),
                    data = { name: plainName(f.get('name')) };
                for (const [k, v] of f.entries())
                    if (k !== 'name')
                        data[k] = ['price', 'pct'].includes(k)
                            ? Number(v)
                            : k === 'active'
                              ? v === 'true'
                              : String(v).trim();
                if (key === 'insurancePlans' && (data.pct < 0 || data.pct > 100))
                    throw new Error('Mức hưởng phải từ 0% đến 100%.');
                if (item) Object.assign(item, data);
                else {
                    if (key === 'services')
                        Object.assign(data, {
                            icon: data.type === 'EXAMINATION' ? 'stethoscope' : 'pulse',
                            deptId: 'dep1',
                            items: [
                                'Tiếp nhận và đối chiếu thông tin',
                                'Thực hiện theo chỉ định',
                                'Cập nhật kết quả',
                            ],
                            description: data.description || 'Dịch vụ mới trong danh mục.',
                        });
                    db[key].push({ id: uid(key.slice(0, 2)), active: true, ...data });
                }
                audit(
                    'UPDATE_CATALOG',
                    '',
                    'Cập nhật ' + catalogOptions[key] + ': ' + data.name,
                    'Danh mục'
                );
                save();
                closeModal();
                render();
                toast('Đã lưu danh mục.');
                break;
            }
            case 'login-form': {
                const username = plainName(f.get('username')),
                    account = db.accounts.find(
                        (a) => a.username.toLowerCase() === username.toLowerCase()
                    );
                if (!account || !account.active)
                    throw new Error('Tên đăng nhập hoặc mật khẩu không đúng.');
                if (account.lockedUntil > Date.now())
                    throw new Error('Tài khoản mẫu tạm khóa do nhiều lần đăng nhập sai.');
                if (!(await checkPassword(account, String(f.get('password'))))) {
                    account.failedCount = (account.failedCount || 0) + 1;
                    if (account.failedCount >= 5) account.lockedUntil = Date.now() + 15 * 60000;
                    save();
                    throw new Error('Tên đăng nhập hoặc mật khẩu không đúng.');
                }
                account.failedCount = 0;
                session = { role: account.role, accountId: account.id, authenticated: true };
                ui.profileId = selfPatient() || 'BN0001';
                save();
                go(
                    account.role === 'PATIENT'
                        ? 'public/home'
                        : account.role === 'ADMIN'
                          ? 'admin/dashboard'
                          : account.role === 'DOCTOR'
                            ? 'staff/consultation'
                            : account.role === 'DEPARTMENT_HEAD'
                              ? 'staff/schedule'
                              : 'staff/dashboard'
                );
                toast('Đã vào tài khoản trải nghiệm.');
                break;
            }
            case 'register-form': {
                const username = plainName(f.get('username')),
                    email = String(f.get('email') || '')
                        .trim()
                        .toLowerCase();
                if (db.accounts.some((a) => a.username.toLowerCase() === username.toLowerCase()))
                    throw new Error('Tên đăng nhập đã được sử dụng.');
                if (email && db.accounts.some((a) => a.email === email))
                    throw new Error('Email liên hệ đã được sử dụng.');
                const account = {
                        id: uid('ac'),
                        username,
                        name: plainName(f.get('name')),
                        email,
                        role: 'PATIENT',
                        active: true,
                        passwordHash: await hashPassword(String(f.get('password'))),
                    },
                    p = newPatient(f);
                db.accounts.push(account);
                db.patients.push(p);
                db.links.push({
                    id: uid('l'),
                    accountId: account.id,
                    patientId: p.id,
                    relationship: 'SELF',
                    status: 'ACTIVE',
                    canView: true,
                });
                session = { role: 'PATIENT', accountId: account.id, authenticated: true };
                ui.profileId = p.id;
                save();
                go('public/home');
                toast('Đã tạo tài khoản mẫu cùng hồ sơ bản thân.');
                break;
            }
            case 'password-form': {
                const a = db.accounts.find((a) => a.id === id);
                if (form.dataset.admin === 'true') {
                    requireRole('ADMIN');
                } else if (!(await checkPassword(a, String(f.get('oldPassword')))))
                    throw new Error('Mật khẩu hiện tại không đúng.');
                if (f.get('password') !== f.get('confirm'))
                    throw new Error('Hai mật khẩu mới chưa trùng nhau.');
                a.passwordHash = await hashPassword(String(f.get('password')));
                a.failedCount = 0;
                a.lockedUntil = null;
                audit(
                    'RESET_PASSWORD',
                    '',
                    'Cập nhật mật khẩu tài khoản mẫu ' + a.username,
                    'Tài khoản'
                );
                save();
                closeModal();
                toast('Đã cập nhật mật khẩu cho tài khoản mẫu trong trình duyệt.');
                break;
            }
        }
    } catch (err) {
        toast(err.message, true);
    }
};

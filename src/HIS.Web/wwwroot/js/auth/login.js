const container = document.getElementById('hisSliderContainer');
const btnGoRegister = document.getElementById('btnGoRegister');
const btnGoLogin = document.getElementById('btnGoLogin');

function switchToRegister() {
    container.classList.add('active');
}

function switchToLogin() {
    container.classList.remove('active');
}

if (btnGoRegister) {
    btnGoRegister.addEventListener('click', switchToRegister);
}

if (btnGoLogin) {
    btnGoLogin.addEventListener('click', switchToLogin);
}

// Tự động mở chế độ đăng ký nếu có param hoặc hash
if (
    window.location.hash === '#register' ||
    new URLSearchParams(window.location.search).get('mode') === 'register'
) {
    switchToRegister();
}

// Ẩn / hiện mật khẩu
function togglePasswordVisibility(inputId, iconId) {
    var p = document.getElementById(inputId);
    var icon = document.getElementById(iconId);
    if (!p || !icon) return;

    if (p.type === 'password') {
        p.type = 'text';
        icon.innerHTML =
            '<path d="M17.94 17.94A10.07 10.07 0 0 1 12 20c-7 0-11-8-11-8a18.45 18.45 0 0 1 5.06-5.94M9.9 4.24A9.12 9.12 0 0 1 12 4c7 0 11 8 11 8a18.5 18.5 0 0 1-2.16 3.19m-6.72-1.07a3 3 0 1 1-4.24-4.24"></path><line x1="1" y1="1" x2="23" y2="23"></line>';
    } else {
        p.type = 'password';
        icon.innerHTML =
            '<path d="M1 12s4-8 11-8 11 8 11 8-4 8-11 8-11-8-11-8z"></path><circle cx="12" cy="12" r="3"></circle>';
    }
}

function getCsrfToken() {
    var input = document.querySelector('input[name="__RequestVerificationToken"]');
    return input ? input.value : '';
}

// Xử lý gửi đăng ký thực tế qua API lưu vào Database
async function handleRegisterSubmit(e) {
    e.preventDefault();
    var fullname = document.getElementById('reg-fullname').value.trim();
    var phone = document.getElementById('reg-phone').value.trim();
    var email = document.getElementById('reg-email').value.trim();
    var username = document.getElementById('reg-username').value.trim();
    var password = document.getElementById('reg-password').value;

    var alertEl = document.getElementById('regAlert');
    var btn = e.target.querySelector('button[type="submit"]');
    var originalBtnText = btn.innerText;

    alertEl.style.display = 'none';

    if (!fullname || !phone || !username || !password) {
        alertEl.innerText = '⚠️ Vui lòng điền đầy đủ các thông tin bắt buộc.';
        alertEl.style.display = 'block';
        return;
    }

    if (password.length < 6) {
        alertEl.innerText = '⚠️ Mật khẩu phải có tối thiểu 6 ký tự.';
        alertEl.style.display = 'block';
        return;
    }

    btn.innerText = '⏳ Đang đăng ký tài khoản...';
    btn.disabled = true;

    try {
        var response = await fetch(container.dataset.registerUrl, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                RequestVerificationToken: getCsrfToken(),
            },
            body: JSON.stringify({
                dateOfBirth: document.getElementById('regDateOfBirth').value,
                fullName: fullname,
                phoneNumber: phone,
                email: email || null,
                username: username,
                password: password,
            }),
        });

        var data = await response.json();
        if (!data.success) {
            alertEl.innerText = '⚠️ ' + data.message;
            alertEl.style.display = 'block';
            btn.innerText = originalBtnText;
            btn.disabled = false;
            return;
        }

        // Đăng ký thành công -> Thông báo, chuyển tab Login và điền sẵn thông tin
        alert('Đăng ký tài khoản thành công! Bạn có thể đăng nhập ngay bây giờ.');
        switchToLogin();

        var u = document.getElementById('login-username');
        var p = document.getElementById('login-password');
        if (u) u.value = username;
        if (p) {
            p.value = password;
            p.focus();
        }

        // Reset form đăng ký
        document.getElementById('registerForm').reset();
    } catch (err) {
        alertEl.innerText = '⚠️ Không thể kết nối tới máy chủ. Vui lòng thử lại sau.';
        alertEl.style.display = 'block';
    } finally {
        btn.innerText = originalBtnText;
        btn.disabled = false;
    }
}

// ================= Quản lý Modal Quên Mật Khẩu =================
const forgotModal = document.getElementById('hisForgotModal');

function openForgotModal() {
    var inputVal = document.getElementById('login-username').value;
    if (inputVal) {
        document.getElementById('forgot-account').value = inputVal;
    }
    backToStep1();
    forgotModal.classList.add('active');
    setTimeout(function () {
        var el = document.getElementById('forgot-account');
        if (el) el.focus();
    }, 120);
}

function closeForgotModal() {
    forgotModal.classList.remove('active');
}

function backToStep1() {
    document.getElementById('forgotStep1').style.display = 'block';
    document.getElementById('forgotStep2').style.display = 'none';
    document.getElementById('forgotStep3').style.display = 'none';
    document.getElementById('step1Alert').style.display = 'none';
}

async function handleForgotSubmit(e) {
    e.preventDefault();
    var account = document.getElementById('forgot-account').value.trim();
    if (!account) return;

    var alertEl = document.getElementById('step1Alert');
    var btnText = document.getElementById('btnSendOtpText');
    alertEl.style.display = 'none';
    btnText.innerText = '⏳ Đang gửi mã OTP...';

    try {
        var response = await fetch(container.dataset.requestResetUrl, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                RequestVerificationToken: getCsrfToken(),
            },
            body: JSON.stringify({ emailOrUsername: account }),
        });

        var data = await response.json();
        if (!data.success) {
            alertEl.innerText = '⚠️ ' + data.message;
            alertEl.style.display = 'block';
            btnText.innerText = 'Gửi Mã Xác Thực OTP';
            return;
        }

        // Chuyển sang Bước 2
        document.getElementById('step2AccountHidden').value = account;
        document.getElementById('step2MaskedEmail').innerText = data.maskedEmail;

        document.getElementById('forgotStep1').style.display = 'none';
        document.getElementById('forgotStep2').style.display = 'block';
        document.getElementById('step2Alert').style.display = 'none';
        document.getElementById('step2Otp').value = '';
        document.getElementById('step2NewPass').value = '';
        document.getElementById('step2ConfirmPass').value = '';
        btnText.innerText = 'Gửi Mã Xác Thực OTP';
        document.getElementById('step2Otp').focus();
    } catch (err) {
        alertEl.innerText = '⚠️ Lỗi kết nối tới máy chủ. Vui lòng thử lại sau.';
        alertEl.style.display = 'block';
        btnText.innerText = 'Gửi Mã Xác Thực OTP';
    }
}

async function resendOtp() {
    var account = document.getElementById('step2AccountHidden').value;
    if (!account) return;
    var alertEl = document.getElementById('step2Alert');
    alertEl.style.display = 'none';

    try {
        var response = await fetch(container.dataset.requestResetUrl, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                RequestVerificationToken: getCsrfToken(),
            },
            body: JSON.stringify({ emailOrUsername: account }),
        });

        var data = await response.json();
        if (data.success) {
            alert('Mã OTP mới đã được gửi lại vào email của bạn!');
        } else {
            alertEl.innerText = '⚠️ ' + data.message;
            alertEl.style.display = 'block';
        }
    } catch {
        alert('Không thể gửi lại mã OTP. Vui lòng kiểm tra kết nối mạng.');
    }
}

async function handleResetSubmit(e) {
    e.preventDefault();
    var account = document.getElementById('step2AccountHidden').value.trim();
    var otp = document.getElementById('step2Otp').value.trim();
    var newPass = document.getElementById('step2NewPass').value;
    var confirmPass = document.getElementById('step2ConfirmPass').value;
    var alertEl = document.getElementById('step2Alert');
    var btnText = document.getElementById('btnConfirmResetText');

    alertEl.style.display = 'none';

    if (newPass !== confirmPass) {
        alertEl.innerText = '⚠️ Mật khẩu xác nhận không khớp.';
        alertEl.style.display = 'block';
        return;
    }

    btnText.innerText = '⏳ Đang xác nhận...';

    try {
        var response = await fetch(container.dataset.confirmResetUrl, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                RequestVerificationToken: getCsrfToken(),
            },
            body: JSON.stringify({
                emailOrUsername: account,
                otp: otp,
                newPassword: newPass,
            }),
        });

        var data = await response.json();
        if (!data.success) {
            alertEl.innerText = '⚠️ ' + data.message;
            alertEl.style.display = 'block';
            btnText.innerText = 'Xác Nhận Đổi Mật Khẩu';
            return;
        }

        // Thành công: chuyển sang Step 3
        document.getElementById('forgotStep2').style.display = 'none';
        document.getElementById('forgotStep3').style.display = 'block';
        btnText.innerText = 'Xác Nhận Đổi Mật Khẩu';
    } catch {
        alertEl.innerText = '⚠️ Lỗi kết nối tới máy chủ. Vui lòng thử lại sau.';
        alertEl.style.display = 'block';
        btnText.innerText = 'Xác Nhận Đổi Mật Khẩu';
    }
}

function finishAndLogin() {
    var account = document.getElementById('step2AccountHidden').value;
    closeForgotModal();
    switchToLogin();
    var u = document.getElementById('login-username');
    var p = document.getElementById('login-password');
    if (u) u.value = account;
    if (p) {
        p.value = '';
        p.focus();
    }
}

// Tự động mở modal quên mật khẩu nếu có param #forgot hoặc mode=forgot
if (
    window.location.hash === '#forgot' ||
    new URLSearchParams(window.location.search).get('mode') === 'forgot'
) {
    openForgotModal();
}

const authActions = {
    switchToRegister,
    switchToLogin,
    openForgotModal,
    closeForgotModal,
    backToStep1,
    resendOtp,
    finishAndLogin,
};
document.addEventListener('click', (event) => {
    const trigger = event.target.closest('[data-auth-action]');
    if (!trigger) return;
    event.preventDefault();
    if (trigger.dataset.authAction === 'password-toggle') {
        togglePasswordVisibility(trigger.dataset.input, trigger.dataset.icon);
    } else {
        authActions[trigger.dataset.authAction]?.();
    }
});
document.getElementById('registerForm').addEventListener('submit', handleRegisterSubmit);
document.getElementById('step1Form').addEventListener('submit', handleForgotSubmit);
document.getElementById('step2Form').addEventListener('submit', handleResetSubmit);

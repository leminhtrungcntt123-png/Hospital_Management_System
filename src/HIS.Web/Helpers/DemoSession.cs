using HIS.Web.ViewModels.Account;

namespace HIS.Web.Helpers;

/// <summary>
/// Phiên đăng nhập demo của HIS.Web. Trang đăng nhập thật do một thành viên khác phụ trách
/// nên web tạm giữ Id tài khoản trong Session (cùng cơ chế với các bước đặt lịch) thay vì
/// dùng cookie xác thực; khi phần đăng nhập thật nối vào, chỉ cần thay ba hàm này bằng Claims.
/// </summary>
public static class DemoSession
{
    private const string AccountKey = "DemoAccount";

    public static LoggedInAccount? Account(this HttpContext context)
        => context.Session.GetObject<LoggedInAccount>(AccountKey);

    public static void SignIn(this HttpContext context, LoggedInAccount account)
        => context.Session.SetObject(AccountKey, account);

    public static void SignOut(this HttpContext context)
        => context.Session.Remove(AccountKey);
}

namespace HIS.BLL.Options;

public class BillingOptions
{
    public const string SectionName = "Billing";

    /// <summary>ID dịch vụ khám bệnh mặc định nếu không có dịch vụ chỉ định</summary>
    public int ExamFeeServiceId { get; set; } = 1;

    /// <summary>Giá khám bệnh mặc định khi chưa tìm thấy dịch vụ tương ứng</summary>
    public decimal DefaultExamPrice { get; set; } = 150000m;

    /// <summary>Tỷ lệ thanh toán BHYT trái tuyến ngoại trú (mặc định 0%)</summary>
    public decimal OutOfRoutePct { get; set; } = 0m;

    /// <summary>Mã ngân hàng nhận thanh toán VietQR (VD: ICB, VCB, CTG, TCB...)</summary>
    public string BankCode { get; set; } = "ICB";

    /// <summary>Tên hiển thị ngân hàng</summary>
    public string BankName { get; set; } = "VietinBank";

    /// <summary>Số tài khoản ngân hàng bệnh viện</summary>
    public string AccountNumber { get; set; } = "119002953584";

    /// <summary>Tên chủ tài khoản thụ hưởng</summary>
    public string AccountName { get; set; } = "BENH VIEN MEDICARE";

    public string BankAccountNumber => AccountNumber;
    public string BankAccountHolder => AccountName;
}

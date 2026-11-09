using HIS.BLL.DTOs;
using HIS.BLL.Interfaces;
using System.Drawing;
using System.Windows.Forms;

namespace HIS.WinForms.Dialogs;

/// <summary>
/// Danh sách liên kết hồ sơ chờ duyệt (PENDING) do bệnh nhân tự gửi từ Web.
/// Quầy đối chiếu giấy tờ rồi xác nhận: PENDING -> ACTIVE, ghi verified_by/verified_at.
/// </summary>
public class LinkApprovalDialog : Form
{
    private readonly IPatientProfileService? _profileService;
    private readonly int? _verifierAccountId;
    private readonly TextBox _txtSearch;
    private readonly DataGridView _grid;
    private readonly Label _lblHint;
    private readonly Button _btnVerify;
    private readonly Button _btnClose;

    private List<PatientProfileLinkDto> _links = new();
    private bool _loading;

    /// <summary>Có ít nhất một liên kết vừa được duyệt trong phiên đối thoại này.</summary>
    public bool VerifiedAny { get; private set; }

    public LinkApprovalDialog() : this(null, null)
    {
    }

    public LinkApprovalDialog(IPatientProfileService? profileService, int? verifierAccountId)
    {
        _profileService = profileService;
        _verifierAccountId = verifierAccountId;

        Text = "Xác nhận liên kết hồ sơ";
        Size = new Size(900, 560);
        MinimumSize = new Size(760, 460);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Color.White;
        Font = new Font("Segoe UI", 9.5F);

        _lblHint = new Label
        {
            Location = new Point(24, 18),
            Size = new Size(830, 40),
            ForeColor = Color.FromArgb(77, 91, 110),
            Text = "Bệnh nhân tự thêm người thân trên Web thì liên kết ở trạng thái CHỜ DUYỆT. " +
                   "Đối chiếu CCCD/giấy tờ của người đến khám rồi bấm Xác nhận để hồ sơ được đặt lịch và xem lịch sử."
        };

        var lblSearch = new Label
        {
            Text = "Tìm theo tên bệnh nhân, mã BN, tên tài khoản",
            Location = new Point(24, 64),
            AutoSize = true,
            ForeColor = Color.FromArgb(23, 38, 59),
            Font = new Font("Segoe UI", 9F, FontStyle.Bold)
        };

        _txtSearch = new TextBox
        {
            Location = new Point(24, 86),
            Size = new Size(560, 28)
        };
        _txtSearch.KeyDown += async (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                await LoadAsync();
            }
        };

        var btnSearch = new Button
        {
            Text = "Tìm",
            Location = new Point(594, 85),
            Size = new Size(96, 30),
            FlatStyle = FlatStyle.Flat,
            BackColor = Theme.Blue,
            ForeColor = Color.White
        };
        btnSearch.FlatAppearance.BorderSize = 0;
        btnSearch.Click += async (_, _) => await LoadAsync();

        _grid = new DataGridView
        {
            Location = new Point(24, 126),
            Size = new Size(830, 320),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            ReadOnly = true,
            MultiSelect = false,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            BackgroundColor = Color.White,
            BorderStyle = BorderStyle.None,
            CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
            GridColor = Color.FromArgb(230, 234, 239),
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
            ColumnHeadersHeight = 36,
            EnableHeadersVisualStyles = false,
            RowTemplate = { Height = 38 }
        };
        _grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Color.FromArgb(243, 246, 249),
            ForeColor = Color.FromArgb(98, 112, 132),
            Font = new Font("Segoe UI", 7.5F, FontStyle.Bold),
            SelectionBackColor = Color.FromArgb(243, 246, 249),
            SelectionForeColor = Color.FromArgb(98, 112, 132),
            WrapMode = DataGridViewTriState.False
        };
        _grid.DefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Color.White,
            ForeColor = Color.FromArgb(67, 81, 102),
            Font = new Font("Segoe UI", 8.5F),
            SelectionBackColor = Color.FromArgb(246, 250, 255),
            SelectionForeColor = Color.FromArgb(38, 54, 74),
            WrapMode = DataGridViewTriState.False
        };
        _grid.Columns.AddRange(
            new DataGridViewTextBoxColumn { HeaderText = "TÀI KHOẢN", FillWeight = 120, Name = "colAccount" },
            new DataGridViewTextBoxColumn { HeaderText = "HỒ SƠ", FillWeight = 170, Name = "colPatient" },
            new DataGridViewTextBoxColumn { HeaderText = "MÃ BN", FillWeight = 80, Name = "colPatientCode" },
            new DataGridViewTextBoxColumn { HeaderText = "NGÀY SINH", FillWeight = 84, Name = "colDob" },
            new DataGridViewTextBoxColumn { HeaderText = "QUAN HỆ", FillWeight = 90, Name = "colRelationship" },
            new DataGridViewTextBoxColumn { HeaderText = "GỬI TỪ", FillWeight = 110, Name = "colCreated" },
            new DataGridViewTextBoxColumn { HeaderText = "LƯỢT HÔM NAY", FillWeight = 96, Name = "colToday" });

        _btnVerify = new Button
        {
            Text = "Xác nhận liên kết",
            Size = new Size(180, 38),
            Location = new Point(674, 458),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
            FlatStyle = FlatStyle.Flat,
            BackColor = Theme.Blue,
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 9F, FontStyle.Bold)
        };
        _btnVerify.FlatAppearance.BorderSize = 0;
        _btnVerify.Click += async (_, _) => await VerifySelectedAsync();

        _btnClose = new Button
        {
            Text = "Đóng",
            Size = new Size(96, 38),
            Location = new Point(568, 458),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.White,
            ForeColor = Color.FromArgb(77, 91, 110)
        };
        _btnClose.FlatAppearance.BorderColor = Color.FromArgb(186, 196, 208);
        _btnClose.Click += (_, _) => Close();

        Controls.Add(_lblHint);
        Controls.Add(lblSearch);
        Controls.Add(_txtSearch);
        Controls.Add(btnSearch);
        Controls.Add(_grid);
        Controls.Add(_btnVerify);
        Controls.Add(_btnClose);

        Load += async (_, _) => await LoadAsync();
    }

    private PatientProfileLinkDto? SelectedLink()
    {
        if (_grid.CurrentRow?.Tag is PatientProfileLinkDto link)
        {
            return link;
        }

        return null;
    }

    private async Task LoadAsync()
    {
        if (_profileService == null || _loading)
        {
            return;
        }

        _loading = true;
        UseWaitCursor = true;

        try
        {
            IReadOnlyList<PatientProfileLinkDto> links = await _profileService.GetPendingLinksAsync(_txtSearch.Text.Trim());

            if (IsDisposed || Disposing)
            {
                return;
            }

            _links = links.ToList();
            _grid.Rows.Clear();

            foreach (PatientProfileLinkDto link in _links)
            {
                int rowIndex = _grid.Rows.Add(
                    link.AccountUsername,
                    link.PatientFullName,
                    link.PatientCode,
                    link.DateOfBirth.ToString("dd/MM/yyyy"),
                    link.RelationshipLabel,
                    link.CreatedAt.ToString("dd/MM HH:mm"),
                    link.HasAppointmentToday ? "có" : "—");
                _grid.Rows[rowIndex].Tag = link;
            }

            Text = _links.Count == 0
                ? "Xác nhận liên kết hồ sơ"
                : $"Xác nhận liên kết hồ sơ ({_links.Count})";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Không tải được danh sách chờ duyệt", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _loading = false;
            UseWaitCursor = false;
        }
    }

    private async Task VerifySelectedAsync()
    {
        if (_profileService == null)
        {
            return;
        }

        if (_verifierAccountId is not int verifierAccountId)
        {
            MessageBox.Show(this, "Phiên đăng nhập không có tài khoản nhân viên nên không ghi được người duyệt.",
                "Chưa đăng nhập", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        PatientProfileLinkDto? link = SelectedLink();
        if (link == null)
        {
            MessageBox.Show(this, "Chọn một liên kết trong danh sách trước.", "Chưa chọn liên kết",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        DialogResult confirm = MessageBox.Show(
            this,
            $"Xác nhận đã đối chiếu giấy tờ của {link.PatientFullName} ({link.PatientCode}) " +
            $"cho tài khoản {link.AccountUsername}?\n\nQuan hệ khai báo: {link.RelationshipLabel}",
            "Xác nhận liên kết hồ sơ",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);

        if (confirm != DialogResult.Yes)
        {
            return;
        }

        _btnVerify.Enabled = false;
        UseWaitCursor = true;

        try
        {
            ProfileLinkResult result = await _profileService.VerifyLinkAsync(link.AccountId, link.PatientId, verifierAccountId);

            if (IsDisposed || Disposing)
            {
                return;
            }

            if (!result.Success)
            {
                MessageBox.Show(this, result.Message, "Không duyệt được", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            else
            {
                VerifiedAny = true;
            }

            await LoadAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Lỗi xác nhận liên kết", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            if (!IsDisposed && !Disposing)
            {
                _btnVerify.Enabled = true;
                UseWaitCursor = false;
            }
        }
    }
}

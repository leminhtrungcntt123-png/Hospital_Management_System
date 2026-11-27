using System.Text.Json;
using HIS.BLL.Common;
using HIS.BLL.DTOs;
using HIS.DAL;
using HIS.Domain;
using Microsoft.EntityFrameworkCore;

namespace HIS.BLL.Services;

public class CashierShiftService : ICashierShiftService
{
    public const string ShiftEntityName = "cashier_shifts";
    public const string ActionOpen = "SHIFT_OPEN";
    public const string ActionClosed = "SHIFT_CLOSED";

    private sealed class ShiftPayload
    {
        public decimal OpeningCash { get; set; }
        public decimal? HandoverCash { get; set; }
        public decimal TopUpCash { get; set; }
        public decimal MidShiftTopUpCash { get; set; }
        public decimal? PreviousExpectedHandover { get; set; }
        public decimal? HandoverDifference { get; set; }
        public DateTime? ClosedAt { get; set; }
        public decimal? ExpectedCash { get; set; }
        public decimal? CountedCash { get; set; }
        public decimal? Difference { get; set; }
        public decimal? VaultDropCash { get; set; }
        public decimal? CarryOverCash { get; set; }
        public string? Note { get; set; }
    }

    private readonly AppDbContext _context;
    private readonly TimeProvider _timeProvider;

    public CashierShiftService(AppDbContext context, TimeProvider timeProvider)
    {
        _context = context;
        _timeProvider = timeProvider;
    }

    private DateTime Now => DateTime.SpecifyKind(_timeProvider.GetLocalNow().DateTime, DateTimeKind.Unspecified);

    public async Task<CashierShiftDto?> GetCurrentShiftAsync(int cashierAccountId, CancellationToken ct = default)
    {
        var log = await _context.AccessLogs
            .AsNoTracking()
            .Where(l => l.EntityName == ShiftEntityName && l.AccountId == cashierAccountId && l.Action == ActionOpen)
            .OrderByDescending(l => l.OccurredAt)
            .FirstOrDefaultAsync(ct);

        if (log == null) return null;

        var username = await GetCashierUsernameAsync(cashierAccountId, ct);
        return MapToDto(log, username);
    }

    public async Task<CashierShiftDto?> GetLastClosedShiftAsync(CancellationToken ct = default)
    {
        var logs = await _context.AccessLogs
            .AsNoTracking()
            .Where(l => l.EntityName == ShiftEntityName && l.Action == ActionClosed)
            .OrderByDescending(l => l.Id)
            .Take(10)
            .ToListAsync(ct);

        if (logs.Count == 0)
        {
            return null;
        }

        var latest = logs
            .Select(l => (Log: l, Payload: DeserializePayload(l.Details)))
            .OrderByDescending(x => x.Payload.ClosedAt ?? x.Log.OccurredAt)
            .ThenByDescending(x => x.Log.Id)
            .First().Log;

        int cid = latest.AccountId ?? 0;
        var username = cid > 0 ? await GetCashierUsernameAsync(cid, ct) : "Thu ngân";
        return MapToDto(latest, username);
    }

    public static (decimal EffectiveOpeningCash, decimal HandoverCash, decimal TopUpCash, decimal? HandoverDifference) ComputeOpeningComponents(OpenShiftRequest request)
    {
        bool hasBreakdown = request.HandoverCash.HasValue || request.TopUpCash.HasValue;
        decimal handover = hasBreakdown ? (request.HandoverCash ?? 0m) : request.OpeningCash;
        decimal topUp = hasBreakdown ? (request.TopUpCash ?? 0m) : 0m;
        decimal effectiveOpening = hasBreakdown ? (handover + topUp) : request.OpeningCash;
        decimal? handoverDiff = request.PreviousExpectedHandover.HasValue
            ? handover - request.PreviousExpectedHandover.Value
            : null;
        return (effectiveOpening, handover, topUp, handoverDiff);
    }

    public static decimal ComputeExpectedClosingCash(decimal openingCash, decimal midShiftTopUpCash, decimal cashIn, decimal cashOut)
        => openingCash + midShiftTopUpCash + cashIn - cashOut;

    public async Task<Result<CashierShiftDto>> OpenShiftAsync(OpenShiftRequest request, CancellationToken ct = default)
    {
        var (effectiveOpening, handover, topUp, handoverDiff) = ComputeOpeningComponents(request);

        if (effectiveOpening < 0 || handover < 0 || topUp < 0)
        {
            return Result.Failure<CashierShiftDto>("INVALID_OPENING_CASH", "Số tiền nhận bàn giao và tiền tiếp quỹ không được âm.");
        }

        if (handoverDiff.HasValue && handoverDiff.Value != 0m && string.IsNullOrWhiteSpace(request.Note))
        {
            return Result.Failure<CashierShiftDto>(
                "HANDOVER_DIFF_REASON_REQUIRED",
                "Số tiền thực nhận từ ca trước đang chênh lệch so với bàn giao. Vui lòng nhập lý do vào ô Ghi chú để đối chứng.");
        }

        var strategy = _context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = _context.Database.CurrentTransaction == null
                ? await _context.Database.BeginTransactionAsync(ct)
                : null;

            if (_context.Database.IsNpgsql())
            {
                await _context.Accounts
                    .FromSqlInterpolated($"SELECT * FROM accounts WHERE id = {request.CashierAccountId} FOR UPDATE")
                    .Select(a => a.Id)
                    .FirstOrDefaultAsync(ct);
            }

            var hasOpen = await _context.AccessLogs
                .AnyAsync(l => l.EntityName == ShiftEntityName && l.AccountId == request.CashierAccountId && l.Action == ActionOpen, ct);

            if (hasOpen)
            {
                return Result.Failure<CashierShiftDto>("SHIFT_ALREADY_OPEN", "Bạn đang có một ca làm việc chưa kết thúc.");
            }

            var payload = new ShiftPayload
            {
                OpeningCash = effectiveOpening,
                HandoverCash = handover,
                TopUpCash = topUp,
                MidShiftTopUpCash = 0m,
                PreviousExpectedHandover = request.PreviousExpectedHandover,
                HandoverDifference = handoverDiff,
                Note = request.Note?.Trim()
            };

            var log = new AccessLog
            {
                AccountId = request.CashierAccountId,
                Action = ActionOpen,
                EntityName = ShiftEntityName,
                IsSuccess = true,
                Severity = "INFO",
                OccurredAt = Now,
                Details = JsonSerializer.Serialize(payload)
            };

            _context.AccessLogs.Add(log);
            await _context.SaveChangesAsync(ct);

            log.EntityId = checked((int)log.Id);
            await _context.SaveChangesAsync(ct);

            if (transaction != null)
            {
                await transaction.CommitAsync(ct);
            }

            var username = await GetCashierUsernameAsync(request.CashierAccountId, ct);
            return Result.Success(MapToDto(log, username));
        });
    }

    public async Task<Result<CashierShiftDto>> TopUpShiftAsync(TopUpShiftRequest request, CancellationToken ct = default)
    {
        if (request.Amount <= 0)
        {
            return Result.Failure<CashierShiftDto>("INVALID_TOPUP_AMOUNT", "Số tiền tiếp quỹ thêm phải lớn hơn 0 đ.");
        }

        var strategy = _context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = _context.Database.CurrentTransaction == null
                ? await _context.Database.BeginTransactionAsync(ct)
                : null;

            AccessLog? log;
            long targetId = request.ShiftId;
            if (_context.Database.IsNpgsql())
            {
                log = await _context.AccessLogs
                    .FromSqlInterpolated($"SELECT * FROM access_logs WHERE id = {targetId} AND entity_name = {ShiftEntityName} FOR UPDATE")
                    .FirstOrDefaultAsync(ct);
            }
            else
            {
                log = await _context.AccessLogs
                    .FirstOrDefaultAsync(l => l.Id == targetId && l.EntityName == ShiftEntityName, ct);
            }

            if (log == null)
            {
                return Result.Failure<CashierShiftDto>("NOT_FOUND", "Không tìm thấy thông tin ca làm việc.");
            }

            if (log.Action == ActionClosed)
            {
                return Result.Failure<CashierShiftDto>("ALREADY_CLOSED", "Ca làm việc này đã kết thúc, không thể tiếp quỹ.");
            }

            if (log.AccountId != request.CashierAccountId)
            {
                return Result.Failure<CashierShiftDto>("FORBIDDEN", "Bạn không có quyền tiếp quỹ cho ca của người khác.");
            }

            var payload = DeserializePayload(log.Details);
            payload.MidShiftTopUpCash += request.Amount;
            payload.TopUpCash += request.Amount;
            var stamp = $"[Tiếp quỹ +{request.Amount:N0}đ lúc {Now:HH:mm}{(string.IsNullOrWhiteSpace(request.Note) ? "" : $": {request.Note.Trim()}")}]";
            payload.Note = string.IsNullOrWhiteSpace(payload.Note)
                ? stamp
                : $"{payload.Note} | {stamp}";

            log.Details = JsonSerializer.Serialize(payload);
            await _context.SaveChangesAsync(ct);

            if (transaction != null)
            {
                await transaction.CommitAsync(ct);
            }

            var username = await GetCashierUsernameAsync(request.CashierAccountId, ct);
            return Result.Success(MapToDto(log, username));
        });
    }

    public async Task<Result<CashierShiftDto>> CloseShiftAsync(CloseShiftRequest request, CancellationToken ct = default)
    {
        if (request.CountedCash < 0)
        {
            return Result.Failure<CashierShiftDto>("INVALID_COUNTED_CASH", "Số tiền kiểm đếm thực tế không được âm.");
        }

        if (request.VaultDropCash < 0 || request.VaultDropCash > request.CountedCash)
        {
            return Result.Failure<CashierShiftDto>("INVALID_VAULT_DROP", "Số tiền nộp về Thủ quỹ không được âm hoặc lớn hơn tiền mặt thực đếm tại quầy.");
        }

        var strategy = _context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = _context.Database.CurrentTransaction == null
                ? await _context.Database.BeginTransactionAsync(ct)
                : null;

            AccessLog? log;
            long targetId = request.ShiftId;
            if (_context.Database.IsNpgsql())
            {
                log = await _context.AccessLogs
                    .FromSqlInterpolated($"SELECT * FROM access_logs WHERE id = {targetId} AND entity_name = {ShiftEntityName} FOR UPDATE")
                    .FirstOrDefaultAsync(ct);
            }
            else
            {
                log = await _context.AccessLogs
                    .FirstOrDefaultAsync(l => l.Id == targetId && l.EntityName == ShiftEntityName, ct);
            }

            if (log == null)
            {
                return Result.Failure<CashierShiftDto>("NOT_FOUND", "Không tìm thấy thông tin ca làm việc.");
            }

            if (log.Action == ActionClosed)
            {
                return Result.Failure<CashierShiftDto>("ALREADY_CLOSED", "Ca làm việc này đã được đóng trước đó.");
            }

            if (log.AccountId != request.CashierAccountId)
            {
                return Result.Failure<CashierShiftDto>("FORBIDDEN", "Bạn không có quyền đóng ca làm việc của người khác.");
            }

            var payload = DeserializePayload(log.Details);
            var closedAt = Now;

            var payments = await _context.Payments
                .Where(p => p.CashierId == log.AccountId
                    && p.PaymentMethod == PaymentMethod.Cash
                    && p.PaidAt >= log.OccurredAt
                    && p.PaidAt <= closedAt)
                .ToListAsync(ct);

            decimal cashIn = payments
                .Where(p => p.PaymentType == PaymentType.Payment || p.PaymentType == PaymentType.Deposit)
                .Sum(p => p.Amount);
            decimal cashOut = payments
                .Where(p => p.PaymentType == PaymentType.Refund)
                .Sum(p => p.Amount);

            decimal expectedCash = ComputeExpectedClosingCash(payload.OpeningCash, payload.MidShiftTopUpCash, cashIn, cashOut);

            payload.ExpectedCash = expectedCash;
            payload.CountedCash = request.CountedCash;
            payload.Difference = request.CountedCash - expectedCash;
            payload.VaultDropCash = request.VaultDropCash;
            payload.CarryOverCash = request.CountedCash - request.VaultDropCash;
            payload.ClosedAt = closedAt;
            if (!string.IsNullOrWhiteSpace(request.Note))
            {
                payload.Note = string.IsNullOrWhiteSpace(payload.Note)
                    ? request.Note.Trim()
                    : $"{payload.Note} | {request.Note.Trim()}";
            }

            log.Action = ActionClosed;
            log.Details = JsonSerializer.Serialize(payload);

            await _context.SaveChangesAsync(ct);
            if (transaction != null)
            {
                await transaction.CommitAsync(ct);
            }

            var username = await GetCashierUsernameAsync(request.CashierAccountId, ct);
            return Result.Success(MapToDto(log, username));
        });
    }

    public async Task<IReadOnlyList<CashierShiftDto>> GetShiftsAsync(DateOnly? date = null, int? cashierAccountId = null, CancellationToken ct = default)
    {
        var query = _context.AccessLogs
            .AsNoTracking()
            .Where(l => l.EntityName == ShiftEntityName)
            .AsQueryable();

        if (cashierAccountId.HasValue)
        {
            query = query.Where(l => l.AccountId == cashierAccountId.Value);
        }

        if (date.HasValue)
        {
            var start = date.Value.ToDateTime(TimeOnly.MinValue);
            var end = date.Value.AddDays(1).ToDateTime(TimeOnly.MinValue);
            query = query.Where(l => l.OccurredAt >= start && l.OccurredAt < end);
        }

        var list = await query
            .OrderByDescending(l => l.OccurredAt)
            .Take(50)
            .ToListAsync(ct);

        var accountIds = list.Where(l => l.AccountId.HasValue).Select(l => l.AccountId!.Value).Distinct().ToList();
        var usernames = await _context.Accounts
            .AsNoTracking()
            .Where(a => accountIds.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, a => a.Username, ct);

        return list.Select(l =>
        {
            int cid = l.AccountId ?? 0;
            string name = usernames.TryGetValue(cid, out var u) ? u : $"User #{cid}";
            return MapToDto(l, name);
        }).ToList();
    }

    private async Task<string> GetCashierUsernameAsync(int cashierAccountId, CancellationToken ct)
    {
        return await _context.Accounts
            .AsNoTracking()
            .Where(a => a.Id == cashierAccountId)
            .Select(a => a.Username)
            .FirstOrDefaultAsync(ct) ?? $"User #{cashierAccountId}";
    }

    private static ShiftPayload DeserializePayload(string? details)
    {
        if (string.IsNullOrWhiteSpace(details))
        {
            return new ShiftPayload();
        }

        try
        {
            return JsonSerializer.Deserialize<ShiftPayload>(details) ?? new ShiftPayload();
        }
        catch
        {
            return new ShiftPayload();
        }
    }

    private static CashierShiftDto MapToDto(AccessLog log, string cashierName)
    {
        var payload = DeserializePayload(log.Details);
        int cashierId = log.AccountId ?? 0;
        var status = log.Action == ActionClosed ? ShiftStatus.Closed : ShiftStatus.Open;
        decimal handoverCash = payload.HandoverCash ?? payload.OpeningCash;
        decimal? carryOver = status == ShiftStatus.Closed
            ? (payload.CarryOverCash ?? payload.CountedCash)
            : payload.CarryOverCash;

        return new CashierShiftDto(
            checked((int)log.Id),
            cashierId,
            cashierName,
            log.OccurredAt,
            payload.ClosedAt,
            payload.OpeningCash,
            payload.ExpectedCash,
            payload.CountedCash,
            payload.Difference,
            status,
            payload.Note,
            handoverCash,
            payload.TopUpCash,
            payload.MidShiftTopUpCash,
            payload.PreviousExpectedHandover,
            payload.HandoverDifference,
            payload.VaultDropCash,
            carryOver);
    }
}

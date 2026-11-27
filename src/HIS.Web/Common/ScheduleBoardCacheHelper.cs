using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;

namespace HIS.Web.Common;

internal static class ScheduleBoardCacheHelper
{
    private const string DeptTokenPrefix = "schedule-board-dept-token:";

    public static IChangeToken GetDepartmentToken(IMemoryCache cache, int departmentId)
    {
        var tokenKey = $"{DeptTokenPrefix}{departmentId}";
        var cts = cache.GetOrCreate(tokenKey, _ => new CancellationTokenSource())!;
        return new CancellationChangeToken(cts.Token);
    }

    public static void InvalidateDepartment(IMemoryCache? cache, int? departmentId)
    {
        if (cache == null || departmentId is not > 0) return;
        var tokenKey = $"{DeptTokenPrefix}{departmentId.Value}";
        if (cache.TryGetValue(tokenKey, out var cached) && cached is CancellationTokenSource cts)
        {
            cts.Cancel();
        }
        cache.Remove(tokenKey);
    }
}

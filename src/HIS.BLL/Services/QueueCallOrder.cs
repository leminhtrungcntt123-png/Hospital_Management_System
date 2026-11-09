using HIS.Domain;

namespace HIS.BLL.Services;

/// <summary>
/// Dựng thứ tự gọi số của một hàng đợi trong phòng.
/// Gốc là dãy số trong ca (khung giờ trước rồi tới vị trí trong khung). Trong cùng một khung giờ, mức 1
/// (trẻ nhỏ / người cao tuổi, hệ thống tự tính) được gọi trước các mức còn lại. Mức 2 do nhân viên gán
/// không đổi thứ tự trong khung mà chen lên trước tối đa maxOvertakeAhead người đang chờ, và dừng lại khi
/// gặp một lượt ưu tiên khác hoặc người đã bị chen đủ maxTimesOvertaken lần.
/// Hàm thuần, không ghi gì xuống CSDL.
/// </summary>
public static class QueueCallOrder
{
    public static List<Appointment> Order(IReadOnlyList<Appointment> waiting, int maxOvertakeAhead, int maxTimesOvertaken)
    {
        var result = waiting
            .OrderBy(a => a.Slot != null ? a.Slot.StartTime : DateTime.MaxValue)
            .ThenBy(a => a.PriorityLevel == PriorityLevels.AgePriority ? 0 : 1)
            .ThenBy(a => a.SlotPosition ?? int.MaxValue)
            .ThenBy(a => a.CreatedAt)
            .ToList();

        // Số lần bị chen của mỗi người trong lượt dựng này: khởi đầu từ overtaken_count đã lưu rồi cộng dần
        // trong bộ nhớ, để hai lượt ưu tiên liên tiếp không cùng chen qua một người quá trần.
        var passedTimes = new Dictionary<int, int>();

        for (int i = 0; i < result.Count; i++)
        {
            if (result[i].PriorityLevel != PriorityLevels.StaffPriority)
            {
                continue;
            }

            var moving = result[i];
            int budget = maxOvertakeAhead;
            int target = i;

            while (budget > 0 && target > 0)
            {
                var ahead = result[target - 1];
                int timesPassed = passedTimes.TryGetValue(ahead.Id, out int counted) ? counted : ahead.OvertakenCount;

                if (ahead.PriorityLevel >= PriorityLevels.StaffPriority || timesPassed >= maxTimesOvertaken)
                {
                    break;
                }

                passedTimes[ahead.Id] = timesPassed + 1;
                target--;
                budget--;
            }

            if (target != i)
            {
                result.RemoveAt(i);
                result.Insert(target, moving);
            }
        }

        return result;
    }
}

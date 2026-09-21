using System;
using System.Collections.Generic;
using System.Linq;

namespace PayslipApi.Reconcile;

public sealed class ReconcileResult
{
    public List<ReconcileRow> Rows { get; } = new();
    public List<Finding> Findings { get; } = new();

    /// <summary>兩邊都找得到的人數。</summary>
    public int MatchedCount { get; set; }

    /// <summary>對得上的人裡面，紙本收費金額與系統已繳不符的人數。</summary>
    public int AmountMismatchCount { get; set; }

    public decimal TotalPaid => Rows.Sum(r => r.Paid);
    public decimal TotalPendingRefund => Rows.Sum(r => r.PendingRefund);
    public decimal TotalUnpaid => Rows.Sum(r => r.Unpaid);
    public int PersonCount => Rows.Count;
}

/// <summary>
/// 把「應收項目明細表」與「訂餐表／活動名單」對起來，算出應退金額並列出所有不一致。
///
/// 四條主規則
///   R1 名單比對：兩邊互相找不到人
///   R2 日期比對：繳費日期不一致
///   R3 金額比對：紙本收費金額 ≠ 系統已繳
///   R4 退費計算：(已收份數 − 實際份數) × 單價 − 已退費
///
/// 例外處理見各區塊註解，全部來自 115/08 實際對帳踩到的狀況。
/// </summary>
public sealed class ReconcileEngine
{
    private readonly ReconcileOptions _opt;

    public ReconcileEngine(ReconcileOptions options) => _opt = options;

    public ReconcileResult Run(IEnumerable<BillingRecord> billing, IEnumerable<RosterRecord> roster)
    {
        var result = new ReconcileResult();

        // === 例外 1：同一人可能有多筆收費（18餐 + 加訂1餐、單餐分4次收） ===
        // 一律先依「人」彙總，否則第二筆會被當成獨立的人而算出負數退費。
        var billGroups = billing
            .GroupBy(b => KeyOf(b.StudentId, b.StudentName))
            .ToDictionary(g => g.Key, g => g.ToList());

        // === 例外 2：名單重複登記同一人（博愛大型活動的陳宥穎） ===
        var rosterGroups = roster
            .GroupBy(r => KeyOf(r.StudentId, r.StudentName))
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var (key, list) in rosterGroups)
        {
            if (list.Count <= 1) continue;
            var dup = list[0];
            result.Findings.Add(new Finding
            {
                Type = FindingType.DuplicateInRoster,
                Severity = Severity.Warning,
                StudentName = dup.StudentName,
                StudentId = dup.StudentId,
                ClassName = dup.ClassName,
                Message = $"名單重複登記 {list.Count} 次，請刪除多餘的列",
            });
        }

        // ---------- 逐人比對 ----------
        foreach (var (key, bills) in billGroups)
        {
            var head = bills[0];
            rosterGroups.TryGetValue(key, out var rows);
            // 重複列取份數最大的那筆當代表（重複通常有一列是空的）
            var paper = rows?.OrderByDescending(r => r.ActualUnits ?? 0).FirstOrDefault();

            decimal paid = bills.Sum(b => b.Paid);
            decimal refunded = bills.Sum(b => b.Refunded);
            decimal unpaid = bills.Sum(b => b.Unpaid);
            decimal writeOff = bills.Sum(b => b.WriteOff);
            int? billedUnits = SumUnits(bills);

            // === 例外 3：沖銷／折扣 ===
            // 應繳 ≠ 已繳時，一律以「已繳」反推實際收了幾份，否則會多退。
            int? paidUnits = paid > 0 ? (int)Math.Round(paid / _opt.UnitPrice, MidpointRounding.AwayFromZero) : 0;
            if (_opt.MonthlyCap is { } cap && paid >= cap)
                paidUnits = billedUnits; // 月費上限項目：收滿上限即視為訂足月

            foreach (var b in bills.Where(b => b.WriteOff > 0))
            {
                result.Findings.Add(new Finding
                {
                    Type = FindingType.AmountMismatch,
                    Severity = Severity.Info,
                    StudentName = head.StudentName, StudentId = head.StudentId, ClassName = head.ClassName,
                    SystemValue = $"應繳 {b.Due:N0} / 已繳 {b.Paid:N0} / 沖銷 {b.WriteOff:N0}",
                    Message = "有沖銷金額，退費已依實收計算",
                });
            }

            // === 例外 4：真實欠費（未付款）——不計入應退 ===
            if (paid == 0 && unpaid > 0)
            {
                var paperBlank = paper is null || (paper.ChargedAmount is null or 0 && paper.ChargedDate is null);
                result.Findings.Add(new Finding
                {
                    Type = FindingType.Unpaid,
                    Severity = Severity.Critical,
                    StudentName = head.StudentName, StudentId = head.StudentId, ClassName = head.ClassName,
                    Amount = unpaid,
                    Message = paperBlank
                        ? "系統未付款、名單亦空白，記錄一致，屬真實欠費"
                        : "系統顯示未付款，但名單上有收費紀錄，請查核",
                });
                result.Rows.Add(new ReconcileRow
                {
                    StudentName = head.StudentName, StudentId = head.StudentId,
                    ClassName = paper?.ClassName ?? head.ClassName,
                    BilledUnits = billedUnits, PaidUnits = 0, ActualUnits = paper?.ActualUnits,
                    Paid = 0, AlreadyRefunded = refunded, PendingRefund = 0, Unpaid = unpaid,
                    Status = "未繳",
                });
                continue;
            }

            // === 例外 5：帳單狀態「待結單」——已收款但流程未結 ===
            foreach (var b in bills.Where(b => b.BillStatus.Contains("待結單")))
            {
                result.Findings.Add(new Finding
                {
                    Type = FindingType.PendingSettlement,
                    Severity = Severity.Warning,
                    StudentName = head.StudentName, StudentId = head.StudentId, ClassName = head.ClassName,
                    Amount = b.Paid,
                    SystemValue = $"{b.PaidDate:M/d} 收款 {b.Paid:N0}，狀態待結單",
                    Message = "已收款但帳單尚未結單",
                });
            }

            // === R1：系統有繳費，名單找不到人 ===
            if (paper is null)
            {
                decimal pending = Math.Max(0, paid - refunded);
                result.Findings.Add(new Finding
                {
                    Type = FindingType.MissingInRoster,
                    Severity = Severity.Critical,
                    StudentName = head.StudentName, StudentId = head.StudentId, ClassName = head.ClassName,
                    Amount = pending,
                    SystemValue = $"{head.PaidDate:M/d} 已繳 {paid:N0}，已退 {refunded:N0}",
                    Message = "系統有繳費，訂餐表／名單上查無此人 — 確認是漏登記還是應退費",
                });
                result.Rows.Add(new ReconcileRow
                {
                    StudentName = head.StudentName, StudentId = head.StudentId, ClassName = head.ClassName,
                    BilledUnits = billedUnits, PaidUnits = paidUnits, ActualUnits = null,
                    Paid = paid, AlreadyRefunded = refunded, PendingRefund = pending, Unpaid = unpaid,
                    Status = "名單無此人",
                });
                continue;
            }

            result.MatchedCount++;

            // === 例外 6：系統已退費，名單卻還列著 ===
            if (refunded > 0 && !paper.IsCancelled && (paper.ActualUnits ?? 0) > 0)
            {
                result.Findings.Add(new Finding
                {
                    Type = FindingType.RefundedButStillListed,
                    Severity = Severity.Warning,
                    StudentName = head.StudentName, StudentId = head.StudentId, ClassName = paper.ClassName,
                    SystemValue = bills.FirstOrDefault(b => b.Refunded > 0)?.RefundReason ?? $"已退 {refunded:N0}",
                    Message = "系統已退費，名單上應刪除",
                });
            }

            // === 例外 7：名單劃掉註明退費，系統卻沒退 ===
            if (paper.IsCancelled && refunded == 0 && paid > 0)
            {
                result.Findings.Add(new Finding
                {
                    Type = FindingType.CancelledButNotRefunded,
                    Severity = Severity.Critical,
                    StudentName = head.StudentName, StudentId = head.StudentId, ClassName = paper.ClassName,
                    Amount = paid,
                    PaperValue = "名單已劃掉／註明退費",
                    SystemValue = "已退費 = 0",
                    Message = "紙本已註明退費，系統尚未退款",
                });
            }

            // === R2：繳費日期 ===
            var sysDate = bills.OrderBy(b => b.PaidDate).FirstOrDefault(b => b.PaidDate.HasValue)?.PaidDate;
            if (paper.ChargedDate is { } pd && sysDate is { } sd)
            {
                var diff = Math.Abs(pd.DayNumber - sd.DayNumber);
                if (diff > _opt.DateToleranceDays)
                {
                    result.Findings.Add(new Finding
                    {
                        Type = FindingType.DateMismatch,
                        Severity = Severity.Info,
                        StudentName = head.StudentName, StudentId = head.StudentId, ClassName = paper.ClassName,
                        PaperValue = pd.ToString("M/d"), SystemValue = sd.ToString("M/d"),
                        Message = "繳費日期不一致",
                    });
                }
            }

            // === R3：收費金額 ===
            if (paper.ChargedAmount is { } amt && Math.Abs(amt - paid) > 0.001m)
            {
                result.AmountMismatchCount++;
                result.Findings.Add(new Finding
                {
                    Type = FindingType.AmountMismatch,
                    Severity = Severity.Warning,
                    StudentName = head.StudentName, StudentId = head.StudentId, ClassName = paper.ClassName,
                    Amount = Math.Abs(amt - paid),
                    PaperValue = amt.ToString("N0"), SystemValue = paid.ToString("N0"),
                    Message = "紙本收費金額與系統已繳不符",
                });
            }

            // === R4：退費計算 ===
            decimal pendingRefund = 0;
            if (_opt.FixedPrice)
            {
                // 活動類：沒參加就整筆退
                bool attended = !paper.IsCancelled && (paper.ActualUnits ?? 1) > 0;
                if (!attended) pendingRefund = Math.Max(0, paid - refunded);
            }
            else if (_opt.TrustRosterUnits && paper.ActualUnits is { } actual)
            {
                // 以「金額」計，不是以「份數」計：已收金額 − 實際份數×單價 − 已退費。
                // 這樣一次涵蓋三種狀況，不必特別處理：
                //   月費上限：點心「每週五天」收 650，650 − 18×33 = 56（正確），
                //             若用份數算會先推成 20 份而少退。
                //   沖銷／折扣：陳玄滋收 1,235、實吃 13 餐 → 1,235 − 13×95 = 0（正確不用退）。
                //   一律重算，不採信紙本的退費欄與合計列（本月 11 班有 6 班合計是錯的）。
                pendingRefund = Math.Max(0, paid - actual * _opt.UnitPrice - refunded);
            }

            if (pendingRefund > 0)
            {
                result.Findings.Add(new Finding
                {
                    Type = FindingType.PendingRefund,
                    Severity = pendingRefund >= _opt.UnitPrice * 10 ? Severity.Warning : Severity.Info,
                    StudentName = head.StudentName, StudentId = head.StudentId, ClassName = paper.ClassName,
                    Amount = pendingRefund,
                    PaperValue = paper.RefundAmount?.ToString("N0"),
                    SystemValue = $"已退 {refunded:N0}",
                    Message = $"訂 {paidUnits} 份、實際 {paper.ActualUnits} 份，應退 {pendingRefund:N0}",
                });
            }

            // 紙本退費欄與重算結果不符時單獨提醒（本月 11 班有 6 班合計是錯的）
            if (paper.RefundAmount is { } pr && Math.Abs(pr - (pendingRefund + refunded)) > 0.001m)
            {
                result.Findings.Add(new Finding
                {
                    Type = FindingType.AmountMismatch,
                    Severity = Severity.Info,
                    StudentName = head.StudentName, StudentId = head.StudentId, ClassName = paper.ClassName,
                    PaperValue = pr.ToString("N0"), SystemValue = (pendingRefund + refunded).ToString("N0"),
                    Message = "紙本退費金額與重算結果不符，以重算為準",
                });
            }

            result.Rows.Add(new ReconcileRow
            {
                StudentName = head.StudentName, StudentId = head.StudentId, ClassName = paper.ClassName,
                BilledUnits = billedUnits, PaidUnits = paidUnits, ActualUnits = paper.ActualUnits,
                Paid = paid, AlreadyRefunded = refunded, PendingRefund = pendingRefund, Unpaid = unpaid,
                Status = pendingRefund > 0 ? "應退" : "正常",
            });
        }

        // === R1 反向：名單上有人，系統查無繳費 ===
        foreach (var (key, rows) in rosterGroups)
        {
            if (billGroups.ContainsKey(key)) continue;
            var p = rows[0];
            if (p.IsCancelled) continue;                       // 已劃掉的不算
            if ((p.ActualUnits ?? 0) == 0 && p.ChargedAmount is null or 0) continue; // 根本沒訂

            result.Findings.Add(new Finding
            {
                Type = FindingType.MissingInBilling,
                Severity = Severity.Critical,
                StudentName = p.StudentName, StudentId = p.StudentId, ClassName = p.ClassName,
                PaperValue = p.ChargedAmount?.ToString("N0"),
                Message = "名單上有此人，系統查無繳費紀錄",
            });
        }

        result.Rows.Sort((a, b) => string.CompareOrdinal(a.ClassName + a.StudentName, b.ClassName + b.StudentName));
        return result;
    }

    /// <summary>
    /// 例外 8：同名不同人。有學號就用學號，沒有就用正規化後的姓名。
    /// 博愛 8 月電影有兩個「呂宥樂」，只靠姓名會合併成一人 — 上線前務必請系統加開學號欄位。
    /// </summary>
    private static string KeyOf(string? id, string name)
        => !string.IsNullOrWhiteSpace(id) ? "#" + id.Trim() : ItemParser.NormalizeName(name);

    private static int? SumUnits(List<BillingRecord> bills)
    {
        var known = bills.Select(b => b.BilledUnits).Where(u => u.HasValue).Select(u => u!.Value).ToList();
        return known.Count == 0 ? null : known.Sum();
    }
}

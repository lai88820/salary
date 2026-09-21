using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using PayslipApi.Reconcile;
using Microsoft.Extensions.Caching.Memory;

namespace PayslipApi.Controllers;

[ApiController]
[Route("api/reconcile")]
public sealed class ReconcileController : ControllerBase
{
    private readonly IMemoryCache _cache;
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(30);

    public ReconcileController(IMemoryCache cache) => _cache = cache;

    /// <summary>可選的收費項目（前端下拉用）。</summary>
    [HttpGet("profiles")]
    public IActionResult Profiles() =>
        Ok(FeeProfile.All.Select(p => new
        {
            p.Name,
            unitPrice = p.Options.UnitPrice,
            fixedPrice = p.Options.FixedPrice,
            monthlyCap = p.Options.MonthlyCap,
        }));

    /// <summary>上傳訂餐表後列出可選分頁。</summary>
    [HttpPost("sheets")]
    public async Task<IActionResult> Sheets(IFormFile roster)
    {
        if (roster is null || roster.Length == 0) return BadRequest("請選擇訂餐表檔案");
        await using var ms = new MemoryStream();
        await roster.CopyToAsync(ms); ms.Position = 0;
        return Ok(ExcelReader.ListSheets(ms));
    }

    /// <summary>執行比對。</summary>
    /// <param name="billing">應收項目明細表 (.xlsx)</param>
    /// <param name="roster">訂餐表／活動名單 (.xlsx)</param>
    /// <param name="profile">收費項目：午餐／點心／電影／大型活動</param>
    /// <param name="rosterSheet">訂餐表要用哪個分頁</param>
    /// <param name="month">月份字串，例：2026年08月</param>
    [HttpPost]
    [RequestSizeLimit(30 * 1024 * 1024)]
    public async Task<IActionResult> Run(
        IFormFile billing, IFormFile roster,
        [FromForm] string profile, [FromForm] string rosterSheet, [FromForm] string month)
    {
        if (billing is null || billing.Length == 0) return BadRequest("請選擇應收項目明細表");
        if (roster is null || roster.Length == 0) return BadRequest("請選擇訂餐表／名單");

        FeeProfile fp;
        try { fp = FeeProfile.Get(profile); }
        catch (ArgumentException ex) { return BadRequest(ex.Message); }

        List<BillingRecord> bills;
        List<RosterRecord> rows;
        try
        {
            await using var b = new MemoryStream(); await billing.CopyToAsync(b); b.Position = 0;
            bills = ExcelReader.ReadBilling(b, month, fp.ItemKeyword);

            await using var r = new MemoryStream(); await roster.CopyToAsync(r); r.Position = 0;
            rows = ExcelReader.ReadRoster(r, rosterSheet);
        }
        catch (Exception ex)
        {
            return BadRequest($"讀取檔案失敗：{ex.Message}");
        }

        if (bills.Count == 0)
            return BadRequest($"「{month}」找不到任何「{fp.ItemKeyword}」的收費紀錄，請確認月份是否正確");

        var result = new ReconcileEngine(fp.Options).Run(bills, rows);

        // ---- 配錯檔的防呆 ----
        // 兩種錯法要分開抓：
        //  (1) 不同分校：名字幾乎都對不上。
        //  (2) 分頁／收費項目選錯（例：系統抓「電影」、名單選「午餐」）：
        //      因為是同一批學生，名字照樣對得上，但金額幾乎全錯 —— 只看名字抓不到。
        int unmatchedBilling = result.Findings.Count(f => f.Type == FindingType.MissingInRoster);
        int unmatchedRoster = result.Findings.Count(f => f.Type == FindingType.MissingInBilling);
        double matchRate = result.PersonCount == 0
            ? 0
            : (double)(result.PersonCount - unmatchedBilling) / result.PersonCount;
        double amountMismatchRate = result.MatchedCount == 0
            ? 0
            : (double)result.AmountMismatchCount / result.MatchedCount;

        string? warning = null;
        if (matchRate < 0.5)
        {
            warning = $"只有 {matchRate:P0} 的人對得上（系統 {result.PersonCount} 人中有 {unmatchedBilling} 人在名單上找不到，"
                    + $"名單另有 {unmatchedRoster} 人查無繳費）。這兩份檔很可能不是同一個分校，請確認後再採用下面的金額。";
        }
        else if (amountMismatchRate > 0.5)
        {
            warning = $"對得上的 {result.MatchedCount} 人裡，有 {result.AmountMismatchCount} 人（{amountMismatchRate:P0}）"
                    + $"的收費金額與系統不符。你選的是「{profile}」，訂餐表分頁是「{rosterSheet}」—— "
                    + "分頁或收費項目很可能選錯了，請確認後再採用下面的金額。";
        }
        else
        {
            // 分頁名稱若明確指向別的收費項目，也提醒一下（例：分頁叫「信義-午餐」卻選了電影）
            var other = FeeProfile.All.FirstOrDefault(
                p => p.Name != profile && rosterSheet.Contains(p.Name));
            if (other is not null && !rosterSheet.Contains(profile))
                warning = $"訂餐表分頁叫「{rosterSheet}」，看起來是「{other.Name}」的名單，"
                        + $"但收費項目選的是「{profile}」。請確認是否選錯。";
        }

        var id = Guid.NewGuid().ToString("N");
        _cache.Set(id, (result, profile, month, rosterSheet), Ttl);

        return Ok(new
        {
            id,
            warning,
            summary = new
            {
                profile,
                month,
                sheet = rosterSheet,
                billingCount = bills.Count,
                rosterCount = rows.Count,
                personCount = result.PersonCount,
                matchedCount = result.MatchedCount,
                matchRate,
                amountMismatchRate,
                totalPaid = result.TotalPaid,
                totalPendingRefund = result.TotalPendingRefund,
                totalUnpaid = result.TotalUnpaid,
                findingCount = result.Findings.Count,
                byType = result.Findings.GroupBy(f => f.Type)
                                        .ToDictionary(g => g.Key.ToString(), g => g.Count()),
            },
            rows = result.Rows,
            findings = result.Findings.OrderByDescending(f => f.Severity)
                                      .ThenByDescending(f => f.Amount),
        });
    }

    /// <summary>把上一次比對結果匯出成 xlsx。</summary>
    [HttpGet("{id}/export")]
    public IActionResult Export(string id)
    {
        if (!_cache.TryGetValue(id, out (ReconcileResult Result, string Profile, string Month, string Sheet) v))
            return NotFound("比對結果已過期，請重新執行");

        var bytes = ReportExporter.Build(v.Result, v.Profile, v.Month, v.Sheet);
        var name = $"{v.Month}_{v.Profile}_{v.Sheet}_比對結果.xlsx";
        return File(bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", name);
    }
}

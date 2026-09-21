using System;
using System.Collections.Generic;

namespace PayslipApi.Reconcile;

/// <summary>來源 A：系統匯出的「應收項目明細表」一列。</summary>
public sealed class BillingRecord
{
    public string? StudentId { get; init; }          // 學號（有的話優先用它比對）
    public string StudentName { get; init; } = "";   // 學生
    public string ClassName { get; init; } = "";     // 班級
    public string Category { get; init; } = "";      // 收費類別（伙食類/學費…）
    public string ItemName { get; init; } = "";      // 收費項目（2026年08月 / 代訂-午餐19餐）
    public DateOnly? PaidDate { get; init; }         // 繳費日期（空 = 未收款）
    public decimal Due { get; init; }                // 應繳
    public decimal Paid { get; init; }               // 已繳
    public decimal Unpaid { get; init; }             // 未繳
    public decimal Refunded { get; init; }           // 已退費
    public decimal Discount { get; init; }           // 折扣
    public decimal WriteOff { get; init; }           // 沖銷金額
    public string BillStatus { get; init; } = "";    // 帳單狀態（交易成功/待結單/未付款）
    public string? RefundReason { get; init; }       // 退費事由
    public string? Note { get; init; }               // 備註

    /// <summary>從「代訂-午餐19餐」這類項目名稱解析出訂購份數；解析不到回傳 null。</summary>
    public int? BilledUnits => ItemParser.ParseUnits(ItemName);
}

/// <summary>來源 B：各班紙本／Excel 的「訂餐表」或「活動名單」一列。</summary>
public sealed class RosterRecord
{
    public string? StudentId { get; init; }
    public string StudentName { get; init; } = "";
    public string ClassName { get; init; } = "";     // 班級（大升一、小二A…）
    public int? ActualUnits { get; init; }           // 實際份數；活動名單填 0 或 1
    public decimal? ChargedAmount { get; init; }     // 紙本「收費金額」欄
    public DateOnly? ChargedDate { get; init; }      // 紙本「日期」欄
    public decimal? RefundAmount { get; init; }      // 紙本「退費金額」欄（僅供對照，不採信）
    public string? ReceiptNo { get; init; }          // 收據編號
    public bool IsCancelled { get; init; }           // 整列被劃掉（退出／不參加）
    public string? Note { get; init; }               // 備註（請假、取消…）
}

// 預設 enum 會序列化成數字（0/1/2），前端就拿不到 "Critical"、"MissingInRoster" 這些名稱，
// 畫面上的「型態」「嚴重度」欄就會變成數字。加上轉換器讓 JSON 直接輸出名稱。
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
public enum Severity { Info, Warning, Critical }

[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
public enum FindingType
{
    /// <summary>系統有繳費，名單上找不到人。</summary>
    MissingInRoster,
    /// <summary>名單上有人，系統查無繳費紀錄。</summary>
    MissingInBilling,
    /// <summary>繳費日期不一致。</summary>
    DateMismatch,
    /// <summary>收費金額不一致。</summary>
    AmountMismatch,
    /// <summary>應退未退。</summary>
    PendingRefund,
    /// <summary>真實欠費（系統未付款，名單也空白）。</summary>
    Unpaid,
    /// <summary>名單重複登記同一人。</summary>
    DuplicateInRoster,
    /// <summary>已收款但帳單仍為「待結單」。</summary>
    PendingSettlement,
    /// <summary>系統已退費，名單卻還列著。</summary>
    RefundedButStillListed,
    /// <summary>名單劃掉註明退費，但系統未退。</summary>
    CancelledButNotRefunded,
}

public sealed class Finding
{
    public FindingType Type { get; init; }
    public Severity Severity { get; init; }
    public string StudentName { get; init; } = "";
    public string? StudentId { get; init; }
    public string ClassName { get; init; } = "";
    /// <summary>涉及金額（待退、欠費、差額）。不涉及金額時為 0。</summary>
    public decimal Amount { get; init; }
    public string? PaperValue { get; init; }
    public string? SystemValue { get; init; }
    public string Message { get; init; } = "";
}

/// <summary>逐人的對帳結果（即使沒有任何異常也會產出一列）。</summary>
public sealed class ReconcileRow
{
    public string StudentName { get; init; } = "";
    public string? StudentId { get; init; }
    public string ClassName { get; init; } = "";
    public int? BilledUnits { get; init; }       // 系統訂購份數
    public int? PaidUnits { get; init; }         // 依「實收金額」反推的已收份數
    public int? ActualUnits { get; init; }       // 名單實際份數
    public decimal Paid { get; init; }
    public decimal AlreadyRefunded { get; init; }
    public decimal PendingRefund { get; init; }  // 本次算出的應退
    public decimal Unpaid { get; init; }
    public string Status { get; init; } = "";
}

public sealed class ReconcileOptions
{
    /// <summary>單價（午餐 95、點心 33、電影 490、大型活動 1580）。</summary>
    public decimal UnitPrice { get; set; } = 95m;

    /// <summary>月費上限。例：點心「每週五天」收 650，而非 20×33=660。null = 無上限。</summary>
    public decimal? MonthlyCap { get; set; }

    /// <summary>固定金額項目（電影、大型活動）：不按份數計價，只比對「有沒有參加」。</summary>
    public bool FixedPrice { get; set; }

    /// <summary>繳費日期容許誤差天數，0 = 必須完全相同。</summary>
    public int DateToleranceDays { get; set; }

    /// <summary>名單上的份數是否可信。false 時只比名單與金額，不算退費。</summary>
    public bool TrustRosterUnits { get; set; } = true;
}

/// <summary>姓名正規化與項目份數解析。</summary>
public static class ItemParser
{
    private static readonly System.Text.RegularExpressions.Regex UnitRx =
        new(@"(\d+)\s*餐", System.Text.RegularExpressions.RegexOptions.Compiled);

    private static readonly System.Text.RegularExpressions.Regex WeeklyRx =
        new(@"每週五天", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>「代訂-午餐19餐」→ 19；「代訂-點心每週五天」→ null（走月費上限）。</summary>
    public static int? ParseUnits(string itemName)
    {
        if (string.IsNullOrWhiteSpace(itemName)) return null;
        if (WeeklyRx.IsMatch(itemName)) return null;
        var m = UnitRx.Match(itemName);
        return m.Success ? int.Parse(m.Groups[1].Value) : null;
    }

    /// <summary>
    /// 姓名正規化：去掉英文名、括號註記與空白。
    /// 「黃宥榛/Moana」「呂宥樂Ryan」「阮荺茜(Molly)」→ 黃宥榛／呂宥樂／阮荺茜
    /// </summary>
    public static string NormalizeName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";
        var s = name.Trim();
        var cut = s.IndexOf('/');
        if (cut > 0) s = s[..cut];
        s = System.Text.RegularExpressions.Regex.Replace(s, @"[（(][^）)]*[）)]", "");
        s = System.Text.RegularExpressions.Regex.Replace(s, @"[A-Za-z0-9\s._-]", "");
        return s.Trim();
    }
}

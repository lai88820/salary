using Microsoft.AspNetCore.StaticFiles;
using PayslipApi.Data;
using QuestPDF.Drawing;
using QuestPDF.Infrastructure;

// QuestPDF 社群授權：年營收 < 100 萬美元的公司可免費使用
QuestPDF.Settings.License = LicenseType.Community;

// 若 Fonts 資料夾有字型檔（例如 NotoSansTC-Regular.ttf）就註冊，否則使用系統字型（微軟正黑體）
var fontDir = Path.Combine(AppContext.BaseDirectory, "Fonts");
if (Directory.Exists(fontDir))
{
    foreach (var f in Directory.EnumerateFiles(fontDir).Where(p => p.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase)
                                                                 || p.EndsWith(".otf", StringComparison.OrdinalIgnoreCase)))
    {
        using var stream = File.OpenRead(f);
        FontManager.RegisterFont(stream);
    }
}

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddSingleton<PayrollRepository>();
builder.Services.AddMemoryCache(); // 收費對帳：暫存比對結果供匯出用

var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? ["http://localhost:4200"];
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod()
                                                      .WithExposedHeaders("Content-Disposition")));

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();

// 前端（Angular build 後放在 wwwroot）由後端一起提供，只要開一個程式
var mime = new FileExtensionContentTypeProvider();
mime.Mappings[".mjs"] = "text/javascript"; // pdf.js worker
app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions { ContentTypeProvider = mime });

app.MapControllers();
app.MapFallbackToFile("index.html"); // /preview 等前端網址

app.Run();

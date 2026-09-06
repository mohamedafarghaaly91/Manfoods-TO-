using MvcApp.Data;
using MvcApp.Models;
using MvcApp.Models.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace MvcApp.Services;

public class TableTotalColumnSettingsService : ITableTotalColumnSettingsService
{
    private const string TurnoverKey = "table_total_turnover_visible";
    private const string NinetyDayKey = "table_total_ninety_day_visible";

    private readonly AppDbContext _db;

    public TableTotalColumnSettingsService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<TableTotalColumnSettings> GetAsync()
    {
        var values = await _db.AppSettings
            .Where(s => s.Key == TurnoverKey || s.Key == NinetyDayKey)
            .ToDictionaryAsync(s => s.Key, s => s.Value);

        return new TableTotalColumnSettings
        {
            TurnoverTotalVisible = ReadBool(values, TurnoverKey),
            NinetyDayTotalVisible = ReadBool(values, NinetyDayKey),
        };
    }

    public async Task SaveAsync(TableTotalColumnSettings settings)
    {
        await SaveValueAsync(TurnoverKey, settings.TurnoverTotalVisible);
        await SaveValueAsync(NinetyDayKey, settings.NinetyDayTotalVisible);
        await _db.SaveChangesAsync();
    }

    private async Task SaveValueAsync(string key, bool value)
    {
        var setting = await _db.AppSettings.FindAsync(key);
        if (setting == null)
        {
            _db.AppSettings.Add(new AppSetting { Key = key, Value = value.ToString() });
        }
        else
        {
            setting.Value = value.ToString();
        }
    }

    private static bool ReadBool(IReadOnlyDictionary<string, string> values, string key)
    {
        return !values.TryGetValue(key, out var value) ||
               !bool.TryParse(value, out var parsed) ||
               parsed;
    }
}
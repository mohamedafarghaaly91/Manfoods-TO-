namespace MvcApp.Models.ViewModels;

/// <summary>
/// Controls whether the calculated Total column is shown in each trend matrix.
/// </summary>
public class TableTotalColumnSettings
{
    public bool TurnoverTotalVisible { get; set; } = true;
    public bool NinetyDayTotalVisible { get; set; } = true;
}
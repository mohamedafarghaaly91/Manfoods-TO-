using System.ComponentModel.DataAnnotations.Schema;

namespace MvcApp.Models;

/// <summary>
/// One row per successful portal login (Home and Admin alike) — written by
/// AuthService.ValidateAsync the moment credentials check out, which is the
/// single choke point both AccountControllers' Login actions share. Failed
/// attempts are never logged here (see AuthService's own lockout counter for
/// that); this is purely "who actually got into the portal, and when."
/// </summary>
[Table("login_history")]
public class LoginHistory
{
    [Column("id")]
    public int Id { get; set; }

    [Column("user_id")]
    public int UserId { get; set; }

    // Denormalized so the log still reads correctly even if the account is
    // later renamed/deleted — the same reasoning UploadLog.UploadedBy uses.
    [Column("email")]
    public string Email { get; set; } = "";

    [Column("logged_in_at")]
    public DateTime LoggedInAt { get; set; } = DateTime.UtcNow;

    [Column("ip_address")]
    public string? IpAddress { get; set; }

    [Column("user_agent")]
    public string? UserAgent { get; set; }
}

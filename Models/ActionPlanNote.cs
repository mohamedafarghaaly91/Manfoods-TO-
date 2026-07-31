using System.ComponentModel.DataAnnotations.Schema;

namespace MvcApp.Models;

/// <summary>
/// A manager's dated note on a StoreActionPlan describing what was actually
/// done. Append-only — no edit/delete in V1. AuthorName/AuthorRole are
/// snapshotted at write time so historical notes keep their original author
/// even if that user's role or assigned name changes later.
/// </summary>
[Table("action_plan_notes")]
public class ActionPlanNote
{
    [Column("id")]
    public int Id { get; set; }

    [Column("store_action_plan_id")]
    public int StoreActionPlanId { get; set; }

    [Column("author_user_id")]
    public int AuthorUserId { get; set; }

    [Column("author_name")]
    public string AuthorName { get; set; } = "";

    [Column("author_role")]
    public string AuthorRole { get; set; } = "";

    [Column("note_text")]
    public string NoteText { get; set; } = "";

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

using Google.Cloud.Firestore;

namespace taskup_backend.Models
{
    public class TaskModel
    {
        [FirestoreProperty("id")]
        public string? Id { get; set; }

        [FirestoreProperty("title")]
        public string Title { get; set; } = "";

        [FirestoreProperty("priority")]
        public string? Priority { get; set; }

        [FirestoreProperty("xpReward")]
        public int XpReward { get; set; }

        [FirestoreProperty("status")]
        public string? Status { get; set; }

        [FirestoreProperty("orgId")]
        public string? OrgId { get; set; }

        [FirestoreProperty("assignedTo")]
        public List<string> AssignedTo { get; set; } = new List<string>();

        [FirestoreProperty("type")]
        public string? Type { get; set; }

        [FirestoreProperty("description")]
        public string? Description { get; set; }

        [FirestoreProperty("timeLimitSeconds")]
        public int? TimeLimitSeconds { get; set; }

        [FirestoreProperty("startingCode")]
        public string? StartingCode { get; set; }

        [FirestoreProperty("attachmentUrl")]
        public string? AttachmentUrl { get; set; }

        [FirestoreProperty("readContent")]
        public string? ReadContent { get; set; }

        // 🚨 This will now serialize beautifully!
        [FirestoreProperty("questions")]
        public List<QuizQuestion> Questions { get; set; } = new List<QuizQuestion>();
        [FirestoreProperty("xpPerQuestion")]
        public int? XpPerQuestion { get; set; } // 🚨 NEW: For dynamic calculation

        [FirestoreProperty("createdAt")]
        public DateTime? CreatedAt { get; set; }
    }
}

[FirestoreData]
public class QuizQuestion
{
    [FirestoreProperty("questionText")]
    public string QuestionText { get; set; }
    [FirestoreProperty("options")]
    public List<string> Options { get; set; }
    [FirestoreProperty("correctAnswer")]
    public string CorrectAnswer { get; set; } // 🚨 NEW: Stores the correct choice securely

}
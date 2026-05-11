namespace TaskUp.API.Models
{
    public class TaskAttempt
    {
        public string Id { get; set; }
        public string TaskId { get; set; }     // Links to the master task
        public string UserId { get; set; }     // The student/employee
        public string OrgId { get; set; }
        public string Status { get; set; }     // "todo", "in-progress", "review", "completed"
        public DateTime? StartedAt { get; set; } // When they clicked "Start Test"
        public DateTime? SubmittedAt { get; set; } // When the timer ran out / they submitted
        public string? Description { get; set; }
        public int? TimeLimitSeconds { get; set; }
        public string? StartingCode { get; set; }
    }
}
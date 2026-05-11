using Microsoft.AspNetCore.Mvc;
using Google.Cloud.Firestore;
using taskup_backend.Models;
using taskup_backend.Services;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;

namespace taskup_backend.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TasksController : ControllerBase
{
    private readonly FirestoreService _firestore;
    private readonly IConfiguration _config;
    public TasksController(FirestoreService firestore, IConfiguration config)
    {
        _firestore = firestore;
        _config = config;
    }

    [HttpGet("{orgId}")]
    public async Task<IActionResult> GetTasks(string orgId)
    {
        var query = _firestore.Collection("tasks").WhereEqualTo("orgId", orgId);
        var snapshot = await query.GetSnapshotAsync();

        var tasks = snapshot.Documents.Select(doc =>
        {
            var task = doc.ConvertTo<TaskItem>();
            task.Id = doc.Id;
            return task;
        }).ToList();

        return Ok(tasks);
    }

    [HttpPost]
    public async Task<IActionResult> CreateTask([FromBody] TaskModel request)
    {
        var taskRef = _firestore.Collection("tasks").Document();

        var taskData = new Dictionary<string, object>
        {
            { "id", taskRef.Id },
            { "title", request.Title },
            { "priority", request.Priority ?? "medium" },
            { "xpReward", request.XpReward },
            { "status", "todo" },
            { "orgId", request.OrgId },
            { "assignedTo", request.AssignedTo },
            { "type", request.Type ?? "coding" },
            { "description", request.Description ?? "" },
            { "timeLimitSeconds", request.TimeLimitSeconds ?? 1800 },
            // 🚨 THE CRITICAL FIX: Add the creation timestamp!
            // Using DateTime.UtcNow ensures it is saved as a native Firestore Timestamp
            { "createdAt", DateTime.UtcNow },
            // Coding spec
            { "startingCode", request.StartingCode ?? "" },
            { "attachmentUrl", request.AttachmentUrl ?? "" },

            // Course spec
            { "readContent", request.ReadContent ?? "" },

            // Quiz spec (Firestore natively handles nested objects/lists)
            { "questions", request.Questions ?? new List<QuizQuestion>() }
        };

        await taskRef.SetAsync(taskData);
        return Ok(taskData);
    }

    [HttpPatch("{id}/status")]
    public async Task<IActionResult> UpdateStatus(string id, [FromBody] string status)
    {
        // 1. Get the User ID from the Firebase Token (set by our Middleware)
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

        var taskRef = _firestore.Collection("tasks").Document(id);
        var taskSnap = await taskRef.GetSnapshotAsync();

        if (!taskSnap.Exists) return NotFound("Task not found");

        var currentStatus = taskSnap.ContainsField("status") ? taskSnap.GetValue<string>("status") : "";

        // 2. Update the Task Status
        await taskRef.UpdateAsync("status", status);

        // 3. GAMIFICATION LOGIC: Award XP if moving to "completed"
        if (status == "completed" && currentStatus != "completed" && !string.IsNullOrEmpty(userId))
        {
            int xpReward = taskSnap.ContainsField("xpReward") ? taskSnap.GetValue<int>("xpReward") : 50; // Default 50 XP

            var userRef = _firestore.Collection("users").Document(userId);
            var userSnap = await userRef.GetSnapshotAsync();

            if (userSnap.Exists)
            {
                int currentXp = userSnap.ContainsField("xp") ? userSnap.GetValue<int>("xp") : 0;
                int newXp = currentXp + xpReward;
                int newLevel = (newXp / 500) + 1; // Level up every 500 XP

                // Update the user's profile with new stats
                await userRef.UpdateAsync(new Dictionary<string, object>
                {
                    { "xp", newXp },
                    { "level", newLevel }
                });
            }
        }

        return Ok();
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateTaskDetails(string id, [FromBody] TaskItem updatedTask)
    {
        var taskRef = _firestore.Collection("tasks").Document(id);
        var snapshot = await taskRef.GetSnapshotAsync();

        if (!snapshot.Exists) return NotFound("Task not found");

        var updates = new Dictionary<string, object>
        {
            { "title", updatedTask.Title },
            { "description", updatedTask.Description },
            { "priority", updatedTask.Priority }
        };

        await taskRef.UpdateAsync(updates);

        // 🚨 THE FIX: Return an actual JSON object so Angular doesn't panic
        return Ok(new { message = "Task updated successfully" });
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteTask(string id)
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var taskRef = _firestore.Collection("tasks").Document(id);
        await taskRef.DeleteAsync();

        // 🚨 THE FIX: Return an actual JSON object
        return Ok(new { message = "Task deleted successfully" });
    }

    [HttpPost("{taskId}/start")]
    public async Task<IActionResult> StartTaskAttempt(string taskId, [FromBody] string userId)
    {
        // 1. Check if an attempt already exists for THIS user and THIS task
        var attemptsQuery = await _firestore.Collection("taskAttempts")
            .WhereEqualTo("taskId", taskId)
            .WhereEqualTo("userId", userId)
            .GetSnapshotAsync();

        if (attemptsQuery.Documents.Count > 0)
        {
            // They already started it, return the existing attempt ID
            return Ok(new { attemptId = attemptsQuery.Documents[0].Id, status = attemptsQuery.Documents[0].GetValue<string>("status") });
        }

        // 2. 🚨 THE FIX: Create a personal copy (Attempt) for this specific user
        var newAttempt = new Dictionary<string, object>
        {
            { "taskId", taskId },
            { "userId", userId },
            { "status", "in-progress" }, // Only their attempt is in-progress!
            { "startedAt", Timestamp.GetCurrentTimestamp() }
        };

        var docRef = await _firestore.Collection("taskAttempts").AddAsync(newAttempt);

        return Ok(new { attemptId = docRef.Id, status = "in-progress" });
    }

    [HttpPost("attempts/{attemptId}/submit")]
    public async Task<IActionResult> SubmitTaskAttempt(string attemptId, [FromBody] SubmitRequest request)
    {
        // 1. Point to both potential collections
        var taskRef = _firestore.Collection("tasks").Document(attemptId);
        var attemptRef = _firestore.Collection("taskAttempts").Document(attemptId);
        DocumentReference targetRef = null;

        // 2. Find out where this document actually lives
        var taskSnap = await taskRef.GetSnapshotAsync();
        if (taskSnap.Exists)
        {
            targetRef = taskRef; // It's in the tasks collection!
        }
        else
        {
            var attemptSnap = await attemptRef.GetSnapshotAsync();
            if (attemptSnap.Exists)
            {
                targetRef = attemptRef; // It's in the taskAttempts collection!
            }
        }

        // 3. If we STILL can't find it, return a clean 404 instead of crashing
        if (targetRef == null)
        {
            return NotFound($"Could not find a task or attempt matching ID: {attemptId}");
        }

        // 4. We found it! Save the code and move it to the Review column
        var updates = new Dictionary<string, object>
        {
            { "status", "review" },
            { "submittedAt", DateTime.UtcNow },
            { "codePayload", request.CodePayload ?? "" },
            { "language", request.Language ?? "javascript" }
        };

        await targetRef.UpdateAsync(updates);
        return Ok(new { message = "Task successfully submitted for Admin review." });
    }
    [HttpGet("languages")]
    public IActionResult GetSupportedLanguages()
    {
        // 🚨 THE FIX: These are the exact Compiler IDs required by OnlineCompiler.io
        var languages = new[]
        {
            new { id = "typescript-deno", name = "JavaScript / TypeScript" }, // They use Deno for JS/TS
            new { id = "python-3.14", name = "Python 3.14" },
            new { id = "dotnet-csharp-9", name = "C# (.NET 9)" },
            new { id = "openjdk-25", name = "Java (OpenJDK 25)" },
            new { id = "g++-15", name = "C++ (G++ 15)" },
            new { id = "go-1.26", name = "Go 1.26" }
        };

        return Ok(languages);
    }

    // 3. ADMIN CLICKS "APPROVE & GRANT XP"
    [HttpPost("attempts/{attemptId}/approve")]
    public async Task<IActionResult> ApproveAttemptAndGrantXp(string attemptId, [FromBody] ApproveRequest request)
    {
        // 1. Find the attempt or task
        var attemptRef = _firestore.Collection("taskAttempts").Document(attemptId);
        var snapshot = await attemptRef.GetSnapshotAsync();

        if (!snapshot.Exists)
        {
            // Fallback to global tasks
            attemptRef = _firestore.Collection("tasks").Document(attemptId);
            snapshot = await attemptRef.GetSnapshotAsync();
            if (!snapshot.Exists) return NotFound("Task not found");
        }

        // 2. 🚨 THE FIX: Safely extract the UserId handling both camelCase and Arrays!
        string userId = null;
        string fieldName = snapshot.ContainsField("assignedTo") ? "assignedTo" :
                           snapshot.ContainsField("AssignedTo") ? "AssignedTo" : null;

        if (fieldName == null)
        {
            return BadRequest("Task does not have an assigned user.");
        }

        try
        {
            // Since we updated Angular to send arrays (e.g., ["user123"]), read it as a List
            var userIds = snapshot.GetValue<List<string>>(fieldName);
            if (userIds != null && userIds.Count > 0)
            {
                userId = userIds[0]; // Grab the first assigned user
            }
        }
        catch
        {
            // Fallback: If it's an old task saved as a single string
            userId = snapshot.GetValue<string>(fieldName);
        }

        if (string.IsNullOrEmpty(userId))
        {
            return BadRequest("Could not parse assigned user ID.");
        }

        // 3. Lock the task
        await attemptRef.UpdateAsync("status", "completed"); // Save back as camelCase for Angular!

        // 4. Auto-Deposit the XP
        var userRef = _firestore.Collection("users").Document(userId);
        var userSnap = await userRef.GetSnapshotAsync();

        if (userSnap.Exists)
        {
            // Handle case sensitivity for the XP field as well
            string xpField = userSnap.ContainsField("xp") ? "xp" :
                             userSnap.ContainsField("Xp") ? "Xp" : "xp";

            int currentXp = userSnap.ContainsField(xpField) ? userSnap.GetValue<int>(xpField) : 0;
            await userRef.UpdateAsync(xpField, currentXp + request.XpReward);
        }

        return Ok(new { message = $"Granted {request.XpReward} XP to {userId}." });
    }

    [HttpPost("attempts/{attemptId}/run")]
    public async Task<IActionResult> RunCode(string attemptId, [FromBody] RunCodeRequest request)
    {
        try
        {
            using var client = new HttpClient();

            var payload = new
            {
                compiler = request.CompilerId,
                code = request.Code,
                input = ""
            };

            var jsonPayload = new StringContent(System.Text.Json.JsonSerializer.Serialize(payload), System.Text.Encoding.UTF8, "application/json");

            // 🚨 1. Bulletproof API Key Check
            var apiKey = _config["CompilerApi:ApiKey"];
            if (string.IsNullOrEmpty(apiKey))
            {
                return Ok(new { status = "failed", output = "Server Error: API Key is missing from appsettings.json." });
            }

            client.DefaultRequestHeaders.Add("Authorization", apiKey);

            // 🚨 THE FIX: Use the synchronous endpoint!
            var response = await client.PostAsync("https://api.onlinecompiler.io/api/run-code-sync/", jsonPayload);
            var responseString = await response.Content.ReadAsStringAsync();

            // 🚨 2. Catch API Rejections (like wrong API key)
            if (!response.IsSuccessStatusCode)
            {
                return Ok(new { status = "failed", output = $"API Rejected the request: {responseString}" });
            }

            // 🚨 3. Safe JSON Parsing
            using var document = System.Text.Json.JsonDocument.Parse(responseString);
            var root = document.RootElement;

            // TryGetProperty prevents crashes if the API omits a field
            string output = root.TryGetProperty("output", out var outProp) ? outProp.GetString() : "";
            string error = root.TryGetProperty("error", out var errProp) ? errProp.GetString() : "";
            int exitCode = root.TryGetProperty("exit_code", out var codeProp) ? codeProp.GetInt32() : 1;

            var finalStatus = exitCode == 0 ? "passed" : "failed";
            var finalOutput = exitCode == 0 ? output : $"{error}\n{output}";

            return Ok(new
            {
                status = finalStatus,
                output = finalOutput,
                memory = root.TryGetProperty("memory", out var memProp) ? memProp.GetString() : "N/A",
                time = root.TryGetProperty("time", out var timeProp) ? timeProp.GetString() : "N/A"
            });
        }
        catch (Exception ex)
        {
            // 🚨 4. Catch any other C# disasters and print them to the Angular terminal
            return Ok(new { status = "failed", output = $"Internal Server Error: {ex.Message}" });
        }
    }
    // 🚨 THE FIX: Added "detail/" to make the route unique!
    // 🚨 THE FIX: Added "detail/" to make the route unique!
    [HttpGet("detail/{id}")]
    public async Task<IActionResult> GetTaskById(string id)
    {
        var taskRef = _firestore.Collection("tasks").Document(id);
        var snapshot = await taskRef.GetSnapshotAsync();

        if (!snapshot.Exists)
        {
            var attemptRef = _firestore.Collection("taskAttempts").Document(id);
            snapshot = await attemptRef.GetSnapshotAsync();
            if (!snapshot.Exists) return NotFound();
        }

        return Ok(snapshot.ToDictionary());
    }

    [HttpPost("attempts/{attemptId}/evaluate-quiz")]
    public async Task<IActionResult> EvaluateQuiz(string attemptId)
    {
        DocumentSnapshot attemptSnap = null;
        DocumentReference attemptRef = null;

        // 1. 🚨 MATCH FIRESTORE COLLECTION: Look inside "taskAttempts" (camelCase)
        attemptRef = _firestore.Collection("taskAttempts").Document(attemptId);
        attemptSnap = await attemptRef.GetSnapshotAsync();

        // 2. 🚨 FALLBACK QUERY: If not found directly, check if the ID passed was a TaskId
        if (!attemptSnap.Exists)
        {
            // Query taskAttempts where PascalCase "TaskId" matches, and "Status" is "review"
            var query = _firestore.Collection("taskAttempts")
                .WhereEqualTo("TaskId", attemptId)
                .WhereEqualTo("Status", "review");

            var querySnap = await query.GetSnapshotAsync();

            if (querySnap.Documents.Count > 0)
            {
                attemptSnap = querySnap.Documents[0];
                attemptRef = attemptSnap.Reference;
            }
        }

        // 3. Descriptive 404 if the document still cannot be found anywhere
        if (attemptSnap == null || !attemptSnap.Exists)
        {
            return NotFound($"No evaluation attempt found in 'taskAttempts' collection directly or via TaskId for: '{attemptId}'");
        }

        var attemptData = attemptSnap.ToDictionary();

        // 🚨 MATCH FIRESTORE FIELDS: Read PascalCase "TaskId" and "UserId"
        string taskId = attemptData.ContainsKey("TaskId") ? attemptData["TaskId"].ToString() : "";
        string userId = attemptData.ContainsKey("UserId") ? attemptData["UserId"].ToString() : "";

        if (string.IsNullOrEmpty(taskId) || string.IsNullOrEmpty(userId))
        {
            return BadRequest("Target attempt record is missing TaskId or UserId fields.");
        }

        // 4. Fetch the original Task Document to get the Correct Answers
        var taskRef = _firestore.Collection("tasks").Document(taskId);
        var taskSnap = await taskRef.GetSnapshotAsync();

        if (!taskSnap.Exists)
        {
            return NotFound($"Original Task record '{taskId}' was not found in the 'tasks' collection.");
        }

        // Deserialize the task questions securely matching your C# model
        var questions = taskSnap.GetValue<List<QuizQuestion>>("questions");
        int xpPerQuestion = taskSnap.ContainsField("xpPerQuestion") ? taskSnap.GetValue<int>("xpPerQuestion") : 0;

        // 5. Parse Student Answers (stored in camelCase codePayload from your Angular submit)
        string payloadField = attemptData.ContainsKey("codePayload") ? "codePayload" :
                             attemptData.ContainsKey("CodePayload") ? "CodePayload" : null;

        if (payloadField == null || attemptData[payloadField] == null)
        {
            return BadRequest("No student answers found inside this submission payload.");
        }

        string studentAnswersJson = attemptData[payloadField].ToString();
        var studentAnswers = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(studentAnswersJson);

        int correctCount = 0;

        // 6. Execute the Grading Engine calculations
        for (int i = 0; i < questions.Count; i++)
        {
            if (studentAnswers.ContainsKey(i.ToString()) &&
                studentAnswers[i.ToString()] == questions[i].CorrectAnswer)
            {
                correctCount++;
            }
        }

        int totalEarnedXp = correctCount * xpPerQuestion;

        // 7. Update Attempt Status to Graded (preserving camelCase/PascalCase for your UI model compatibility)
        var updates = new Dictionary<string, object>
        {
            { "status", "graded" },
            { "Status", "graded" },
            { "earnedXp", totalEarnedXp },
            { "score", $"{correctCount}/{questions.Count}" }
        };
        await attemptRef.UpdateAsync(updates);

        // 8. Add XP to the User's Total Profile (Using Firestore's thread-safe atomic Incrementer)
        var userRef = _firestore.Collection("users").Document(userId);
        var userSnap = await userRef.GetSnapshotAsync();

        if (userSnap.Exists)
        {
            // Auto-detect if user profile is tracking "xp", "Xp", or "totalXp"
            string userXpField = userSnap.ContainsField("totalXp") ? "totalXp" :
                                 userSnap.ContainsField("xp") ? "xp" : "Xp";

            await userRef.UpdateAsync(userXpField, Google.Cloud.Firestore.FieldValue.Increment(totalEarnedXp));
        }

        return Ok(new
        {
            message = "Quiz graded successfully",
            score = $"{correctCount}/{questions.Count}",
            xpAwarded = totalEarnedXp
        });
    }

    [HttpPatch("{taskId}/assign")]
    public async Task<IActionResult> AssignTask(string taskId, [FromBody] List<string> assignedTo)
    {
        try
        {
            var taskRef = _firestore.Collection("tasks").Document(taskId);

            // Just update the assignedTo array
            await taskRef.UpdateAsync("assignedTo", assignedTo);

            return Ok(new { message = "Task assignment updated successfully!" });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = $"Internal Error: {ex.Message}" });
        }
    }
}
public class ApproveRequest
{
    public int XpReward { get; set; }
}
public class SubmitRequest
{
    public string CodePayload { get; set; }
    public string Language { get; set; }
}
public class RunCodeRequest
{
    public string Code { get; set; }
    public string CompilerId { get; set; } // e.g., "python-3.14"
}
// 🚨 THE FIX: Paste this at the bottom of TasksController.cs

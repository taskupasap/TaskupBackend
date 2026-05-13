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
    public async Task<IActionResult> GetTasks(string orgId, [FromQuery] string userId = null)
    {
        var currentUserId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        var targetUserId = userId ?? currentUserId;

        var query = _firestore.Collection("tasks").WhereEqualTo("orgId", orgId);
        var snapshot = await query.GetSnapshotAsync();

        var tasksList = new List<Dictionary<string, object>>();

        foreach (var doc in snapshot.Documents)
        {
            var taskDict = doc.ToDictionary();
            taskDict["id"] = doc.Id;
            if (!taskDict.ContainsKey("status")) taskDict["status"] = taskDict.ContainsKey("Status") ? taskDict["Status"] : "todo";

            tasksList.Add(taskDict);
        }

        // =========================================================================
        // 🚨 THE NUCLEAR FIX: IN-MEMORY COUNTER
        // Bypasses Firestore query rules to guarantee every attempt is counted!
        // =========================================================================
        var allAttemptsSnap = await _firestore.Collection("taskAttempts").GetSnapshotAsync();
        var reviewCounts = new Dictionary<string, int>();

        foreach (var doc in allAttemptsSnap.Documents)
        {
            // Extract Task ID safely
            string tId = doc.ContainsField("TaskId") ? doc.GetValue<string>("TaskId") :
                         doc.ContainsField("taskId") ? doc.GetValue<string>("taskId") : "";

            // Extract Status safely and clean it
            string rawStatus = doc.ContainsField("Status") ? doc.GetValue<string>("Status") :
                               doc.ContainsField("status") ? doc.GetValue<string>("status") : "";

            string cleanStatus = rawStatus?.Trim().ToLower();

            // Explicitly count any attempt sitting in "review"
            if (!string.IsNullOrEmpty(tId) && cleanStatus == "review")
            {
                if (!reviewCounts.ContainsKey(tId)) reviewCounts[tId] = 0;
                reviewCounts[tId]++;
            }
        }

        // Inject the TRUE count explicitly as an integer
        foreach (var task in tasksList)
        {
            string tId = task["id"].ToString();
            task["pendingReviewCount"] = reviewCounts.ContainsKey(tId) ? reviewCounts[tId] : 0;
        }

        // =========================================================================
        // OVERLAY STUDENT PROGRESS (Existing Logic)
        // =========================================================================
        if (!string.IsNullOrEmpty(targetUserId))
        {
            var attemptsQuery = await _firestore.Collection("taskAttempts")
                .WhereEqualTo("UserId", targetUserId)
                .GetSnapshotAsync();

            var userAttempts = new Dictionary<string, DocumentSnapshot>();
            foreach (var doc in attemptsQuery.Documents)
            {
                string tId = doc.ContainsField("TaskId") ? doc.GetValue<string>("TaskId") :
                             doc.ContainsField("taskId") ? doc.GetValue<string>("taskId") : "";

                if (!string.IsNullOrEmpty(tId)) userAttempts[tId] = doc;
            }

            foreach (var task in tasksList)
            {
                string tId = task["id"].ToString();

                if (userAttempts.TryGetValue(tId, out var attemptDoc))
                {
                    string attemptStatus = attemptDoc.ContainsField("Status") ? attemptDoc.GetValue<string>("Status") :
                                           attemptDoc.ContainsField("status") ? attemptDoc.GetValue<string>("status") : "todo";

                    task["status"] = attemptStatus;
                    task["attemptId"] = attemptDoc.Id;
                }
            }
        }

        return Ok(tasksList);
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
            // Quiz Specific
            { "questions", request.Questions ?? new List<QuizQuestion>() },
            { "xpPerQuestion", request.XpPerQuestion > 0 ? request.XpPerQuestion : 10 }, // 🚨 Never allow 0 XP per question!
            { "pendingReviewCount", 0 }, // 🚨 FIX 1: Always start the counter at 0!
        };

        await taskRef.SetAsync(taskData);
        return Ok(taskData);
    }

    [HttpPatch("{taskId}/status")]
    public async Task<IActionResult> UpdateStatus(string taskId, [FromBody] string status)
    {
        // 1. Get the current User ID from the token
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId)) return Unauthorized("User not authenticated.");

        // Clean the status string just in case it has extra JSON quotes
        status = status?.Trim('"');

        // 2. Query the student's personal attempt using the Task ID + User ID!
        var attemptsQuery = await _firestore.Collection("taskAttempts")
            .WhereEqualTo("TaskId", taskId)
            .WhereEqualTo("UserId", userId)
            .GetSnapshotAsync();

        DocumentReference attemptRef;
        string currentStatus = "";

        if (attemptsQuery.Documents.Count > 0)
        {
            var attemptDoc = attemptsQuery.Documents[0];
            attemptRef = attemptDoc.Reference;
            currentStatus = attemptDoc.ContainsField("Status") ? attemptDoc.GetValue<string>("Status") : "";
        }
        else
        {
            // Create their personal attempt document if it doesn't exist yet!
            var newAttempt = new Dictionary<string, object>
            {
                { "TaskId", taskId },
                { "UserId", userId },
                { "Status", status },
                { "status", status }, // camelCase for UI binding
                { "StartedAt", Timestamp.GetCurrentTimestamp() }
            };
            attemptRef = await _firestore.Collection("taskAttempts").AddAsync(newAttempt);
        }

        // 3. Update the student's Attempt Status
        await attemptRef.UpdateAsync(new Dictionary<string, object> {
            { "Status", status },
            { "status", status }
        });

        // 🚨 FIX 2: Dynamic Counter for Drag-and-Drop!
        var taskRefMaster = _firestore.Collection("tasks").Document(taskId);

        if (status.ToLower() == "review" && currentStatus.ToLower() != "review")
        {
            await taskRefMaster.UpdateAsync("pendingReviewCount", Google.Cloud.Firestore.FieldValue.Increment(1));
        }
        else if (status.ToLower() != "review" && currentStatus.ToLower() == "review")
        {
            await taskRefMaster.UpdateAsync("pendingReviewCount", Google.Cloud.Firestore.FieldValue.Increment(-1));
        }

        // 4. GAMIFICATION LOGIC: Award XP if dragged to "completed" manually
        if (status?.ToLower() == "completed" && currentStatus?.ToLower() != "completed")
        {
            var taskSnap = await taskRefMaster.GetSnapshotAsync();
            int xpReward = taskSnap.Exists && taskSnap.ContainsField("xpReward") ? taskSnap.GetValue<int>("xpReward") : 50;

            var userRef = _firestore.Collection("users").Document(userId);
            var userSnap = await userRef.GetSnapshotAsync();

            if (userSnap.Exists)
            {
                string userXpField = userSnap.ContainsField("totalXp") ? "totalXp" :
                                     userSnap.ContainsField("xp") ? "xp" : "Xp";
                await userRef.UpdateAsync(userXpField, Google.Cloud.Firestore.FieldValue.Increment(xpReward));
            }
        }

        return Ok(new { message = "Progress updated successfully." });
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
        if (string.IsNullOrEmpty(userId)) return BadRequest("UserId is required to start a task.");

        // 1. 🚨 MATCH FIRESTORE CASING: Search using PascalCase fields
        var attemptsQuery = await _firestore.Collection("taskAttempts")
            .WhereEqualTo("TaskId", taskId)
            .WhereEqualTo("UserId", userId)
            .GetSnapshotAsync();

        // Fallback for old camelCase data (just in case)
        if (attemptsQuery.Documents.Count == 0)
        {
            attemptsQuery = await _firestore.Collection("taskAttempts")
                .WhereEqualTo("taskId", taskId)
                .WhereEqualTo("userId", userId)
                .GetSnapshotAsync();
        }

        if (attemptsQuery.Documents.Count > 0)
        {
            // They already started it, return the existing attempt ID safely!
            var existingDoc = attemptsQuery.Documents[0];
            string currentStatus = existingDoc.ContainsField("Status") ? existingDoc.GetValue<string>("Status") :
                                   existingDoc.ContainsField("status") ? existingDoc.GetValue<string>("status") : "in-progress";

            return Ok(new { attemptId = existingDoc.Id, status = currentStatus });
        }

        // 2. 🚨 CREATE SAFELY: Use PascalCase to match your Firestore TaskAttempt model perfectly
        var newAttempt = new Dictionary<string, object>
        {
            { "TaskId", taskId },
            { "UserId", userId },
            { "Status", "in-progress" },
            { "status", "in-progress" }, // Keep camelCase for UI bindings
            { "StartedAt", Timestamp.GetCurrentTimestamp() }
        };

        var docRef = await _firestore.Collection("taskAttempts").AddAsync(newAttempt);

        return Ok(new { attemptId = docRef.Id, status = "in-progress" });
    }

    [HttpPost("attempts/{attemptIdOrTaskId}/submit")]
    public async Task<IActionResult> SubmitTaskAttempt(string attemptIdOrTaskId, [FromBody] SubmitRequest request)
    {
        // 1. Get the logged-in student's ID
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId)) return Unauthorized("User not logged in.");

        DocumentSnapshot attemptSnap = null;
        DocumentReference attemptRef = null;

        // 2. Try to find the document assuming they passed a valid Attempt ID
        attemptRef = _firestore.Collection("taskAttempts").Document(attemptIdOrTaskId);
        attemptSnap = await attemptRef.GetSnapshotAsync();

        // 3. SMART FALLBACK: If not found, they passed the Task ID! Find this user's attempt for this task.
        if (!attemptSnap.Exists)
        {
            var query = _firestore.Collection("taskAttempts")
                .WhereEqualTo("TaskId", attemptIdOrTaskId)
                .WhereEqualTo("UserId", userId);

            var querySnap = await query.GetSnapshotAsync();
            if (querySnap.Documents.Count > 0)
            {
                attemptSnap = querySnap.Documents[0];
                attemptRef = attemptSnap.Reference;
            }
        }

        if (attemptSnap == null || !attemptSnap.Exists)
        {
            return NotFound($"No active student attempt found for Task/Attempt ID: {attemptIdOrTaskId}");
        }

        var attemptData = attemptSnap.ToDictionary();
        string taskId = attemptData.ContainsKey("TaskId") ? attemptData["TaskId"].ToString() :
                        attemptData.ContainsKey("taskId") ? attemptData["taskId"].ToString() : attemptIdOrTaskId;

        var taskRef = _firestore.Collection("tasks").Document(taskId);
        var taskSnap = await taskRef.GetSnapshotAsync();

        if (!taskSnap.Exists) return NotFound("The master task template has been deleted.");

        // 4. Save the student's answers (Course verification string, Quiz JSON, or Code)
        var updates = new Dictionary<string, object>
        {
            { "SubmittedAt", DateTime.UtcNow },
            { "CodePayload", request.CodePayload ?? "" },
            { "codePayload", request.CodePayload ?? "" }, // UI compat
            { "Language", request.Language ?? "javascript" },
            { "Status", "review" },
            { "status", "review" }
        };

        await attemptRef.UpdateAsync(updates);

        // 5. Increment the Notification Counter on the Master Task for the Admin!
        await taskRef.UpdateAsync("pendingReviewCount", Google.Cloud.Firestore.FieldValue.Increment(1));

        return Ok(new { message = "Successfully submitted for Admin review.", status = "review" });
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
        if (string.IsNullOrEmpty(request.StudentId))
            return BadRequest("StudentId is required to approve the correct submission.");

        DocumentSnapshot attemptSnap = null;
        DocumentReference attemptRef = null;

        attemptRef = _firestore.Collection("taskAttempts").Document(attemptId);
        attemptSnap = await attemptRef.GetSnapshotAsync();

        // 🚨 FALLBACK: Query by TaskId AND StudentId to find the exact submission!
        if (!attemptSnap.Exists)
        {
            var query = _firestore.Collection("taskAttempts")
                .WhereEqualTo("TaskId", attemptId)
                .WhereEqualTo("UserId", request.StudentId)
                .WhereEqualTo("Status", "review");

            var querySnap = await query.GetSnapshotAsync();
            if (querySnap.Documents.Count > 0)
            {
                attemptSnap = querySnap.Documents[0];
                attemptRef = attemptSnap.Reference;
            }
        }

        if (attemptSnap == null || !attemptSnap.Exists)
            return NotFound("Student submission record not found.");

        // Lock attempt status to completed
        await attemptRef.UpdateAsync(new Dictionary<string, object> {
            { "Status", "completed" },
            { "status", "completed" },
            { "earnedXp", request.XpReward }
        });

        // 🚨 ADD THIS: Decrement the notification counter on the Master Task!
        var attemptData = attemptSnap.ToDictionary();
        string taskId = attemptData.ContainsKey("TaskId") ? attemptData["TaskId"].ToString() :
                        attemptData.ContainsKey("taskId") ? attemptData["taskId"].ToString() : "";

        if (!string.IsNullOrEmpty(taskId))
        {
            var taskRefToUpdate = _firestore.Collection("tasks").Document(taskId);
            await taskRefToUpdate.UpdateAsync("pendingReviewCount", Google.Cloud.Firestore.FieldValue.Increment(-1));
        }

        // Grant the XP directly to the student
        var userRef = _firestore.Collection("users").Document(request.StudentId);
        var userSnap = await userRef.GetSnapshotAsync();

        if (userSnap.Exists)
        {
            string userXpField = userSnap.ContainsField("totalXp") ? "totalXp" :
                                 userSnap.ContainsField("xp") ? "xp" : "Xp";
            await userRef.UpdateAsync(userXpField, Google.Cloud.Firestore.FieldValue.Increment(request.XpReward));
        }

        return Ok(new { message = $"Successfully granted {request.XpReward} XP." });
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
    [HttpGet("detail/{id}")]
    public async Task<IActionResult> GetTaskById(string id)
    {
        // 1. Try to fetch it directly as a Master Task
        var taskRef = _firestore.Collection("tasks").Document(id);
        var snapshot = await taskRef.GetSnapshotAsync();

        if (snapshot.Exists)
        {
            var taskData = snapshot.ToDictionary();
            taskData["id"] = snapshot.Id;
            return Ok(taskData);
        }

        // 2. If it's not a Master Task, it must be an Attempt ID!
        var attemptRef = _firestore.Collection("taskAttempts").Document(id);
        var attemptSnap = await attemptRef.GetSnapshotAsync();

        if (!attemptSnap.Exists) return NotFound("Task or Attempt not found.");

        var attemptData = attemptSnap.ToDictionary();
        string masterTaskId = attemptData.ContainsKey("TaskId") ? attemptData["TaskId"].ToString() :
                              attemptData.ContainsKey("taskId") ? attemptData["taskId"].ToString() : "";

        // 3. Fetch the underlying Master Task to get the Starting Code & Description
        var masterTaskRef = _firestore.Collection("tasks").Document(masterTaskId);
        var masterSnap = await masterTaskRef.GetSnapshotAsync();

        if (!masterSnap.Exists) return NotFound("The master task template has been deleted.");

        var resultData = masterSnap.ToDictionary();
        resultData["id"] = masterSnap.Id; // Keep master ID
        resultData["attemptId"] = attemptSnap.Id; // Expose the specific student attempt ID

        // 4. OVERLAY PRIORITY: Apply the student's saved work over the starting template
        string savedCode = attemptData.ContainsKey("codePayload") ? attemptData["codePayload"].ToString() :
                           attemptData.ContainsKey("CodePayload") ? attemptData["CodePayload"].ToString() : null;

        if (!string.IsNullOrEmpty(savedCode))
        {
            resultData["codePayload"] = savedCode;
        }

        // 5. Apply the student's specific status & language
        resultData["status"] = attemptData.ContainsKey("Status") ? attemptData["Status"].ToString() :
                               attemptData.ContainsKey("status") ? attemptData["status"].ToString() : "todo";

        resultData["language"] = attemptData.ContainsKey("Language") ? attemptData["Language"].ToString() :
                                 attemptData.ContainsKey("language") ? attemptData["language"].ToString() : "Unknown";

        // 🚨 THE FIX: Safely calculate exactly how long the task took!
        DateTime? startedAt = null;
        if (attemptData.TryGetValue("StartedAt", out var s1) || attemptData.TryGetValue("startedAt", out s1))
        {
            if (s1 is Google.Cloud.Firestore.Timestamp ts1) startedAt = ts1.ToDateTime();
            else if (s1 is DateTime dt1) startedAt = dt1;
        }

        DateTime? submittedAt = null;
        if (attemptData.TryGetValue("SubmittedAt", out var s2) || attemptData.TryGetValue("submittedAt", out s2))
        {
            if (s2 is Google.Cloud.Firestore.Timestamp ts2) submittedAt = ts2.ToDateTime();
            else if (s2 is DateTime dt2) submittedAt = dt2;
        }

        if (startedAt.HasValue && submittedAt.HasValue)
        {
            TimeSpan duration = submittedAt.Value - startedAt.Value;
            // Format to "XXm YYs"
            resultData["timeTaken"] = $"{(int)duration.TotalMinutes}m {duration.Seconds}s";
        }
        else
        {
            resultData["timeTaken"] = "Unknown";
        }

        return Ok(resultData);
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
        // 🚨 CRITICAL FIX: Safely read XP Per Question and ensure it never defaults to 0!
        int xpPerQuestion = taskSnap.ContainsField("xpPerQuestion") ? taskSnap.GetValue<int>("xpPerQuestion") :
                            taskSnap.ContainsField("XpPerQuestion") ? taskSnap.GetValue<int>("XpPerQuestion") : 10;

        if (xpPerQuestion <= 0) xpPerQuestion = 10; // Failsafe!

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

        // 🚨 ADD THIS: Decrement the notification counter on the Master Task!
        await taskRef.UpdateAsync("pendingReviewCount", Google.Cloud.Firestore.FieldValue.Increment(-1));

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
    public string StudentId { get; set; } // 🚨 ADDED: Identify the correct student
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
public class StatusUpdateRequest
{
    public string Status { get; set; }
}
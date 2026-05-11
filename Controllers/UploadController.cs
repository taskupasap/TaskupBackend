using Microsoft.AspNetCore.Mvc;
using CloudinaryDotNet;
using CloudinaryDotNet.Actions;

namespace taskup_backend.Controllers
{
    [ApiController]
    [Route("api/upload")] // 🚨 Note the 'api/' prefix
    public class UploadController : ControllerBase
    {
        private readonly Cloudinary _cloudinary;

        public UploadController(IConfiguration config)
        {
            var account = new Account(
                config["Cloudinary:CloudName"],
                config["Cloudinary:ApiKey"],
                config["Cloudinary:ApiSecret"]
            );
            _cloudinary = new Cloudinary(account);
        }
        [HttpPost]
        public async Task<IActionResult> UploadAsset([FromForm] IFormFile file) // 🚨 ADDED [FromForm]
        {
            if (file == null || file.Length == 0) return BadRequest("No file attached.");

            using var stream = file.OpenReadStream();

            // 🚨 CHANGED to AutoUploadParams so it accepts PDFs, Images, and Text files
            var uploadParams = new AutoUploadParams()
            {
                File = new FileDescription(file.FileName, stream),
                Folder = "taskup_attachments"
            };

            var result = await _cloudinary.UploadAsync(uploadParams);

            if (result.Error != null) return BadRequest(result.Error.Message);

            return Ok(new { url = result.SecureUrl.ToString() });
        }
    }
}
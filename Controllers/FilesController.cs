using Microsoft.AspNetCore.Mvc;
using taskup_backend.Services;

namespace taskup_backend.Controllers;

[ApiController]
[Route("api/[controller]")]
public class FilesController : ControllerBase
{
    private readonly CloudinaryService _cloudinary;

    public FilesController(CloudinaryService cloudinary)
    {
        _cloudinary = cloudinary;
    }

    [HttpPost("upload")]
    public async Task<IActionResult> UploadFile(IFormFile file)
    {
        var result = await _cloudinary.UploadFileAsync(file);

        if (result.Error != null) return BadRequest(result.Error.Message);

        return Ok(new
        {
            Url = result.SecureUrl.AbsoluteUri,
            PublicId = result.PublicId,
            Name = file.FileName
        });
    }
}
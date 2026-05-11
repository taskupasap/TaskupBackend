using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Microsoft.Extensions.Configuration;

namespace taskup_backend.Services;

public class CloudinaryService
{
    private readonly Cloudinary _cloudinary;

    public CloudinaryService(IConfiguration config)
    {
        var acc = new Account(
            config["Cloudinary:CloudName"],
            config["Cloudinary:ApiKey"],
            config["Cloudinary:ApiSecret"]
        );
        _cloudinary = new Cloudinary(acc);
    }

    public async Task<RawUploadResult> UploadFileAsync(IFormFile file)
    {
        // Initialize as RawUploadResult to support any file type (PDF, Word, Images, etc.)
        var uploadResult = new RawUploadResult();

        if (file != null && file.Length > 0)
        {
            using var stream = file.OpenReadStream();

            var uploadParams = new RawUploadParams()
            {
                File = new FileDescription(file.FileName, stream),
                Folder = "taskup_attachments"
            };

            // Execute the upload using the generic UploadAsync method
            uploadResult = await _cloudinary.UploadAsync(uploadParams);
        }

        return uploadResult;
    }
}
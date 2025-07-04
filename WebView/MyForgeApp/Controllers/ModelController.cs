using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/[controller]")]
public class ModelsController : ControllerBase
{
    public record BucketObject(string name, string urn);

    private readonly APS _aps;

    public ModelsController(APS aps)
    {
        _aps = aps;
    }

    // ✅ GET list of uploaded files
    [HttpGet()]
    public async Task<IEnumerable<BucketObject>> GetModels()
    {
        var objects = await _aps.GetObjects();
        return from o in objects
               select new BucketObject(o.ObjectKey, APS.Base64Encode(o.ObjectId));
    }

    // ✅ GET translation status
    [HttpGet("{urn}/status")]
    public async Task<TranslationStatus> GetModelStatus(string urn)
    {
        var status = await _aps.GetTranslationStatus(urn);
        return status;
    }

    // ✅ Form class for file upload
    public class UploadModelForm
    {
        [FromForm(Name = "model-zip-entrypoint")]
        public string? Entrypoint { get; set; } // <-- Make nullable

        [FromForm(Name = "model-file")]
        public IFormFile File { get; set; }
    }


    // ✅ POST upload & translate
    [HttpPost(), DisableRequestSizeLimit]
    public async Task<IActionResult> UploadAndTranslateModel([FromForm] UploadModelForm form)
    {
        if (form.File == null)
        {
            return BadRequest(new { error = "model-file is required." });
        }

        var isZip = Path.GetExtension(form.File.FileName).Equals(".zip", StringComparison.OrdinalIgnoreCase);

        if (isZip && string.IsNullOrWhiteSpace(form.Entrypoint))
        {
            return BadRequest(new { error = "model-zip-entrypoint is required for ZIP files." });
        }

        // Upload to OSS  
        using var stream = form.File.OpenReadStream();
        var obj = await _aps.UploadModel(form.File.FileName, stream);

        // For ZIP: use entrypoint; for others: pass null or empty
        var job = await _aps.TranslateModel(obj.ObjectId, isZip ? form.Entrypoint : null);

        return Ok(new BucketObject(obj.ObjectKey, job.Urn));
    }

}

//    [HttpDelete("{objectName}")]
//    public async Task<IActionResult> DeleteModel(string objectName)
//    {
//        try
//        {
//            await _aps.DeleteModel(objectName);
//            return NoContent(); // 204 success with no content
//        }
//        catch (Exception ex)
//        {
//            return StatusCode(500, new { error = ex.Message });
//        }
//    }

//}
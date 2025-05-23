using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using MediaFeeder.Data;
using MediaFeeder.Data.Identity;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders.Physical;

namespace MediaFeeder.Controllers;

[ApiController]
[Route("api/[controller]")]
public class VideoController(IDbContextFactory<MediaFeederDataContext> contextFactory, UserManager userManager, ILogger<VideoController> logger) : ControllerBase
{
    [HttpGet("{id:int}/play")]
    [Authorize(Roles = "Download", AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<IActionResult> Play(int id)
    {
        var user = await userManager.GetUserAsync(HttpContext.User);
        ArgumentNullException.ThrowIfNull(user);

        var scopeClaim = HttpContext.User.Claims.SingleOrDefault(static c => c.Type == "Video");
        if (scopeClaim == null || scopeClaim.Value != id.ToString())
        {
            var serialized = JsonSerializer.Serialize(HttpContext.User, new JsonSerializerOptions()
            {
                ReferenceHandler = ReferenceHandler.IgnoreCycles
            });

            logger.LogInformation("The video claim does not contain '{}' or was not found", scopeClaim);
            return StatusCode((int)HttpStatusCode.Forbidden, $"The video claim does not contain '{scopeClaim}' or was not found. Full User: {serialized}");
        }

        await using var context = await contextFactory.CreateDbContextAsync(HttpContext.RequestAborted);
        var video = await context.Videos.SingleOrDefaultAsync(v => v.Id == id && v.Subscription!.UserId == user.Id, HttpContext.RequestAborted);

        if (video == null)
            return NotFound();

        if (video.DownloadedPath == null)
            return StatusCode((int)HttpStatusCode.PreconditionFailed, "Not Downloaded");

        if (video.DownloadedPath.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            return Redirect(video.DownloadedPath);

        new FileExtensionContentTypeProvider().TryGetContentType(video.DownloadedPath, out var mimeType);
        return PhysicalFile(video.DownloadedPath, mimeType ?? "application/octet-stream", true);
    }

    [HttpGet("{id:int}/thumbnail")]
    [OutputCache(Duration = 60 * 60 * 24 * 7)]
    [ResponseCache(Duration = 60 * 60 * 24 * 7)]
    [Authorize(Policy = "Thumbnails")]
    public async Task<IActionResult> Thumbnail(int id)
    {
        var user = await userManager.GetUserAsync(HttpContext.User);
        ArgumentNullException.ThrowIfNull(user);

        await using var context = await contextFactory.CreateDbContextAsync(HttpContext.RequestAborted);
        var video = await context.Videos.SingleOrDefaultAsync(v => v.Id == id && v.Subscription!.UserId == user.Id, HttpContext.RequestAborted);

        if (video == null)
            return NotFound();

        if (video.Thumb == null)
            return StatusCode((int)HttpStatusCode.PreconditionFailed, "Not Downloaded - no path saved");

        if (video.Thumb.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            return Redirect(video.Thumb);

        try
        {
            var stream = (new PhysicalFileInfo(new FileInfo(video.Thumb))).CreateReadStream();
            new FileExtensionContentTypeProvider().TryGetContentType(video.Thumb, out var mimeType);
            return File(stream, mimeType ?? "application/octet-stream");
        }
        catch (IOException e)
        {
            if (System.IO.File.Exists(video.Thumb))
                System.IO.File.Delete(video.Thumb);

            video.Thumb = null;
            await context.SaveChangesAsync(HttpContext.RequestAborted);

            return StatusCode((int)HttpStatusCode.PreconditionFailed, $"Not Downloaded - {e.Message}");
        }
    }
}

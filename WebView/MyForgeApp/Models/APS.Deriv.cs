using System.Collections.Generic;
using System.Threading.Tasks;
using Autodesk.ModelDerivative;
using Autodesk.ModelDerivative.Model;

public record TranslationStatus(string Status, string Progress, IEnumerable<string> Messages);

public partial class APS
{
    public static string Base64Encode(string plainText)
    {
        var plainTextBytes = System.Text.Encoding.UTF8.GetBytes(plainText);
        return System.Convert.ToBase64String(plainTextBytes).TrimEnd('=');
    }

    public async Task<Job> TranslateModel(string objectId, string rootFilename, bool isZip = false)
    {
        var auth = await GetInternalToken();
        var modelDerivativeClient = new ModelDerivativeClient();
        var payload = new JobPayload
        {
            Input = new JobPayloadInput
            {
                Urn = Base64Encode(objectId)
            },
            Output = new JobPayloadOutput
            {
                Formats =
                [
                    new JobPayloadFormatSVF2
                    {
                        Views = [View._2d, View._3d]
                    }
                ]
            }
        };
        if (isZip && !string.IsNullOrEmpty(rootFilename))
        {
            payload.Input.RootFilename = rootFilename;
            payload.Input.CompressedUrn = true;
        }
        var job = await modelDerivativeClient.StartJobAsync(jobPayload: payload, region: Region.US, accessToken: auth.AccessToken);
        return job;
    }

    public async Task<TranslationStatus> GetTranslationStatus(string urn)
    {
        var auth = await GetInternalToken();
        var modelDerivativeClient = new ModelDerivativeClient();
        try
        {
            var manifest = await modelDerivativeClient.GetManifestAsync(urn, accessToken: auth.AccessToken);

            // Extract messages from the manifest (e.g., errors, warnings, etc.)
            var messages = new List<string>();

            if (manifest.Derivatives != null)
            {
                foreach (var derivative in manifest.Derivatives)
                {
                    if (derivative.Messages != null)
                    {
                        foreach (var msg in derivative.Messages)
                        {
                            messages.Add($"{msg.Type}: {msg.Message}");
                        }
                    }
                }
            }

            return new TranslationStatus(manifest.Status, manifest.Progress, messages);
        }
        catch (ModelDerivativeApiException ex)
        {
            if (ex.HttpResponseMessage.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return new TranslationStatus("n/a", "", null);
            }
            else
            {
                throw;
            }
        }
    }
}
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace ReleaseNotesMaker
{
    public static class GetVersion
    {
        [FunctionName(nameof(GetVersion))]
        public static async Task<IActionResult> Run(
            [HttpTrigger(
                AuthorizationLevel.Anonymous, 
                "get", 
                Route = "version")] 
            HttpRequest req,
            ILogger log)
        {
            return new OkObjectResult("1.1");
        }
    }
}


using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Net.Http;
using ReleaseNotesMaker.Model;
using System.IO;
using Newtonsoft.Json;
using System.Linq;
using System.Collections.Generic;

namespace ReleaseNotesMaker
{
    public static class ReleaseNotesMaker
    {
        [FunctionName(nameof(UpdateReleaseNotes))]
        public static async Task<IActionResult> UpdateReleaseNotes(
            [HttpTrigger(
                AuthorizationLevel.Function, 
                "post",
                Route = "release-notes/milestones/{milestones}")]
            HttpRequest req,
            string milestones,
            ILogger log)
        {
            log.LogInformation("-> UpdateReleaseNotes");

            var client = new HttpClient();
            client.DefaultRequestHeaders.Add("User-Agent", nameof(ReleaseNotesMaker));

            NotificationService.SetHttpClient(client);

            var token = req.Headers[Constants.GitHubTokenHaderKey];
            log?.LogDebug($"token {token}");

            string json = await new StreamReader(req.Body).ReadToEndAsync();
            var info = JsonConvert.DeserializeObject<GitHubInfo>(json);

            log?.LogDebug($"AccountName {info.AccountName}");
            log?.LogDebug($"RepoName {info.RepoName}");
            log?.LogDebug($"BranchName {info.BranchName}");

            var forMilestones = (milestones == "all")
                ? null
                : milestones.Split(new char[]
                {
                    ','
                }).ToList();

            var helper = new GitHubHelper.GitHubHelper(client);

            var releaseNotesResult = await helper.CreateReleaseNotesMarkdown(
                info.AccountName,
                info.RepoName,
                info.Projects,
                forMilestones,
                token);

            if (!string.IsNullOrEmpty(releaseNotesResult.ErrorMessage))
            {
                return new BadRequestObjectResult(releaseNotesResult.ErrorMessage);
            }

            var filesToSave = new List<GitHubTextFile>();

            foreach (var page in releaseNotesResult.CreatedPages)
            {
                var existingContent = await helper.GetTextFile(
                    info.AccountName,
                    info.RepoName,
                    info.BranchName,
                    page.FilePath,
                    token);

                if (!string.IsNullOrEmpty(existingContent.ErrorMessage)
                    || existingContent.TextContent != page.Markdown)
                {
                    filesToSave.Add(new GitHubTextFile
                    {
                        Content = page.Markdown,
                        Path = page.FilePath
                    });
                }
            }

            var commitContent = filesToSave
                .Select(f => (f.Path, f.Content))
                .ToList();

            var result = await helper.CommitFiles(
                info.AccountName,
                info.RepoName,
                info.BranchName,
                token,
                info.CommitMessage,
                commitContent);

            if (!string.IsNullOrEmpty(result.ErrorMessage))
            {
                log.LogError($"Cannot save file: {result.ErrorMessage}");

                await NotificationService.Notify(
                    "Release notes NOT saved to GitHub",
                    $"There was an error: {result.ErrorMessage}",
                    log);

                log.LogInformation("UpdateReleaseNotes ->");
                return new UnprocessableEntityObjectResult($"There was an issue: {result.ErrorMessage}");
            }

            await NotificationService.Notify(
                "Release notes saved to GitHub",
                $"The release notes were saved to {info.AccountName}/{info.RepoName}",
                log);

            log.LogInformation("UpdateReleaseNotes ->");
            return new OkObjectResult(result);
        }
    }
}

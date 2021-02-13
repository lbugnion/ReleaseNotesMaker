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
using System;

namespace ReleaseNotesMaker
{
    public static class ReleaseNotesMaker
    {
        private const string PageUrlMask = "https://github.com/{0}/{1}/blob/{2}/{3}.md";

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

            string json = string.Empty;
            GitHubInfo info;

            try
            {
                json = await new StreamReader(req.Body).ReadToEndAsync();
                info = JsonConvert.DeserializeObject<GitHubInfo>(json);
            }
            catch (Exception ex)
            {
                log.LogError(ex, $"Error deserializing the JSON: {json}");
                return new BadRequestObjectResult("Invalid request");
            }

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
                info.BranchName,
                info.Projects,
                forMilestones,
                token);

            if (!string.IsNullOrEmpty(releaseNotesResult.ErrorMessage))
            {
                return new UnprocessableEntityObjectResult(releaseNotesResult.ErrorMessage);
            }

            var filesToSave = new List<GitHubTextFile>();
            var functionResult = new List<string>();

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

                    functionResult.Add(string.Format(
                        PageUrlMask,
                        info.AccountName,
                        info.RepoName,
                        info.BranchName,
                        page.FilePath));
                }
            }

            var commitContent = filesToSave
                .Select(f => (f.Path, f.Content))
                .ToList();

            if (commitContent.Count > 0)
            {
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
            }

            if (functionResult.Count == 0)
            {
                var emptyMessage = $"The release notes were NOT saved to {info.AccountName}/{info.RepoName} (no changes found)";

                await NotificationService.Notify(
                    "Release notes unchanged",
                    emptyMessage,                    
                    log);

                log.LogInformation("UpdateReleaseNotes ->");
                return new OkObjectResult(emptyMessage);
            }

            var message = $"The release notes were saved to {info.AccountName}/{info.RepoName}";

            await NotificationService.Notify(
                "Release notes saved",
                message,
                log);

            log.LogInformation("UpdateReleaseNotes ->");
            return new OkObjectResult(message);
        }
    }
}

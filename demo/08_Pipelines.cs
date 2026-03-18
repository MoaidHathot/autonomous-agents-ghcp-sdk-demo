#!/usr/bin/env dotnet
#:package GitHub.Copilot.SDK@*
#:package Dumpify@*

using System.Text.Json;
using GitHub.Copilot.SDK;
using Dumpify;

#pragma warning disable IL2026, IL3050

await using var client = new CopilotClient();

// ── Agent definitions ────────────────────────────────────────────────

// 1. Ev2 Investigator — queries Ev2 rollout/release status and errors
var ev2Investigator = new CustomAgentConfig
{
    Name = "ev2-investigator",
    DisplayName = "Ev2 Investigator",
    Description = "Understands Ev2 deployment/rollout failures. Given an Ev2 release or rollout URL, it uses the Ev2 MCP to fetch rollout details, status, and error information.",
    Prompt = """
    You are an Ev2 deployment failure investigator.
    Given an Ev2 release or rollout URL (typically starting with https://ev2portal.azure.net),
    use the Ev2 MCP tools such as `get_rollout_by_url` and `get_failed_ev2_rollout_error_by_url`
    to retrieve rollout details, determine if it succeeded or failed, and extract the full error
    information including any failed steps, error messages, and affected resources.
    If a resource is a Deployment Script, extract the subscriptionId, resource groups, ARM ID
    and any relevant info, then use the `extension_cli_generate` tool to generate the `az CLI`
    command that can retrieve the input, output and logs of the deployment script.
    Always return the complete error context so downstream agents can diagnose the root cause.
    """,
    Infer = true,
    McpServers = new Dictionary<string, object>
    {
        ["ev2"] = new McpLocalServerConfig
        {
            Type = "local",
            Command = "dnx",
            Args = ["Ev2Mcp", "--source", "https://pkgs.dev.azure.com/msazure/One/_packaging/ZTS/nuget/v3/index.json"],
            Tools = ["*"],
        },
    },
};

// 2. AzDO Navigator — fetches releases, builds, PRs, WIs from Azure DevOps
var azdoNavigator = new CustomAgentConfig
{
    Name = "azdo-navigator",
    DisplayName = "AzDO Navigator",
    Description = "Fetches Azure DevOps releases and builds, extracts Ev2 rollout URLs from build logs, and navigates linked PRs (including drafts) and Work Items to gather context.",
    Prompt = """
    You are an Azure DevOps navigator and release analyst.
	org is "msazure", project is "One", repo is "ZTS".
    Your job is to:
    1. Use the AzDO MCP tools such as `pipelines_get_build_definitions` to find release definitions
       for the CURRENT user (not any user).
    2. Locate builds for the requested release/environment. Query and read ALL build logs to find
       links starting with `https://ev2portal.azure.net` — these are Ev2 Portal or Rollout URLs.
       Some builds contain multiple Ev2 rollouts; grab all distinct URLs.
    3. Follow linked PRs (even draft PRs) via the build's associated changes, and follow linked
       Work Items to extract PR links and context.
    4. Return all discovered Ev2 rollout URLs, PR links, WI details, and any relevant build metadata.
""",
    Infer = true,
    McpServers = new Dictionary<string, object>
    {
        ["azdo"] = new McpLocalServerConfig
        {
            Type = "local",
            Command = "npx",
            Args = ["-y", "@azure-devops/mcp", "msazure"],
            Tools = ["*"],
        },
    },
};

// 3. Code Analyst — reads ZTS repo code to locate the source of an error
var codeAnalyst = new CustomAgentConfig
{
    Name = "code-analyst",
    DisplayName = "Code Analyst",
    Description = "Has access to the ZTS repository on disk (main branch). Given a PR or draft link and an error description, reads the relevant source code to identify the code that caused the failure.",
    Prompt = """
    You are a code analyst with access to the ZTS (Zero Trust Segmentation) repository.
    Given a PR link (or draft PR) and an error description from an Ev2 rollout failure,
    use the filesystem tools to navigate the repo, read relevant source files, ARM templates,
    deployment scripts, and configuration files to pinpoint the exact code that caused the error.
    Focus on recent changes in the PR, deployment scripts, Bicep/ARM templates, and any
    configuration that could relate to the reported failure.
    Return the specific files, line numbers, and code snippets that are most likely responsible.
    """,
    Infer = true,
    McpServers = new Dictionary<string, object>
    {
        ["filesystem"] = new McpLocalServerConfig
        {
            Type = "local",
            Command = "npx",
            Args = ["-y", "@modelcontextprotocol/server-filesystem", @"P:\Work\Networking\Repo\Zero-Trust-Segmentation\ZTS\"],
            Tools = ["*"],
        },
    },
};

// 4. Fix Advisor — synthesizes all findings and proposes fixes
var fixAdvisor = new CustomAgentConfig
{
    Name = "fix-advisor",
    DisplayName = "Fix Advisor",
    Description = "Receives all investigation results — Ev2 errors, AzDO context, and code analysis — and synthesizes a clear diagnosis with actionable fix suggestions.",
    Prompt = """
    You are a senior deployment engineer and fix advisor.
    You will receive a summary of:
    - Ev2 rollout/deployment errors (from the Ev2 Investigator)
    - Azure DevOps release and PR context (from the AzDO Navigator)
    - Source code analysis pointing to the likely offending code (from the Code Analyst)
    Synthesize all of this into:
    1. A clear root-cause diagnosis
    2. Step-by-step fix suggestions with specific code changes where applicable
    3. Any preventive measures or deployment best practices to avoid recurrence
    Format your response in markdown.
    """,
    Infer = true,
};

// ── Session creation ─────────────────────────────────────────────────

await using var session = await client.CreateSessionAsync(new()
{
    OnPermissionRequest = PermissionHandler.ApproveAll,
    Streaming = true,
    CustomAgents = [ev2Investigator, azdoNavigator, codeAnalyst, fixAdvisor],
});

// ── Event subscriptions ──────────────────────────────────────────────

var toolCallNames = new Dictionary<string, string>();
// Internal SDK tools that fire every turn — suppress to reduce noise
var internalTools = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    { "report_intent", "task", "thinking" };
// Deduplicate consecutive agent-started events
var lastAgentStarted = "";

session.On(evt =>
{
    switch (evt)
    {
        // Sub-agent lifecycle (deduplicated)
        case SubagentStartedEvent started:
            if (started.Data.AgentDisplayName == lastAgentStarted) break;
            lastAgentStarted = started.Data.AgentDisplayName;
            Console.ForegroundColor = ConsoleColor.Magenta;
            Console.WriteLine($"  [Agent Started]   {started.Data.AgentDisplayName}");
            Console.ResetColor();
            break;
        case SubagentCompletedEvent completed:
            lastAgentStarted = ""; // reset so a re-entry shows again
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"  [Agent Completed] {completed.Data.AgentDisplayName}");
            Console.ResetColor();
            break;
        case SubagentFailedEvent failed:
            lastAgentStarted = "";
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"  [Agent Failed]    {failed.Data.AgentDisplayName}: {failed.Data.Error}");
            Console.ResetColor();
            break;

        // Tool execution — single-line truncated JSON (skip internal SDK tools)
        case ToolExecutionStartEvent toolStart:
            toolCallNames[toolStart.Data.ToolCallId] = toolStart.Data.ToolName;
            if (internalTools.Contains(toolStart.Data.ToolName)) break;
            var argsJson = toolStart.Data.Arguments is JsonElement args
                ? JsonSerializer.Serialize(args, new JsonSerializerOptions { WriteIndented = false })
                : "";
            if (argsJson.Length > 120) argsJson = argsJson[..120] + "...";
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"  [Tool Start]  {toolStart.Data.ToolName}  {argsJson}");
            Console.ResetColor();
            break;

        case ToolExecutionCompleteEvent toolComplete:
            var name = toolCallNames.GetValueOrDefault(toolComplete.Data.ToolCallId, "unknown");
            if (internalTools.Contains(name)) break;
            var status = toolComplete.Data.Success ? "Done" : "FAILED";
            Console.ForegroundColor = toolComplete.Data.Success ? ConsoleColor.DarkGreen : ConsoleColor.Red;
            Console.Write($"  [Tool {status}]  {name}");
            // Print truncated single-line result or error
            if (toolComplete.Data.Result is { } result)
            {
                var resultJson = JsonSerializer.Serialize(result.Content, new JsonSerializerOptions { WriteIndented = false });
                if (resultJson.Length > 120) resultJson = resultJson[..120] + "...";
                Console.Write($"  {resultJson}");
            }
            if (toolComplete.Data.Error is { } error)
            {
                var errMsg = error.Message ?? "";
                if (errMsg.Length > 120) errMsg = errMsg[..120] + "...";
                Console.Write($"  ERR: {errMsg}");
            }
            Console.WriteLine();
            Console.ResetColor();
            break;

        // Reasoning
        case AssistantReasoningDeltaEvent reasoning:
            Console.Write(reasoning.Data.DeltaContent);
            break;
    }
});

// ── Prompt ────────────────────────────────────────────────────────────

Console.WriteLine("[Main] Investigating pipeline failure...");
Console.WriteLine();

var response = await session.SendAndWaitAsync(
    new MessageOptions
    {
        Prompt = """
        I have a failing Azure DevOps release pipeline that triggers Ev2 rollouts.
        I need you to orchestrate the following investigation:

        1. Use the azdo-navigator agent to look at the realse (https://msazure.visualstudio.com/One/_build/results?buildId=156822366&view=results)
           extract the Ev2 rollout URL(s) from the build logs,
           and identify any linked PRs (including drafts) and Work Items.

        2. Use the ev2-investigator agent to take those Ev2 rollout URL(s) and determine
           what went wrong — get the full error details from the rollout.

        3. Use the code-analyst agent to take the PR/draft link and the error details,
           then look through the ZTS repo to find the code that likely caused the failure.

        4. Finally, use the fix-advisor agent to synthesize everything — the Ev2 error,
           the AzDO context, and the code analysis — into a clear root-cause diagnosis
           and actionable fix suggestions.

        Present the complete investigation report in markdown.
        """,
    },
    timeout: TimeSpan.FromMinutes(15)
);

Console.WriteLine();
Console.WriteLine("=== Investigation Report ===");
response?.Data.Content.Dump();

#!/usr/bin/env dotnet
#:package GitHub.Copilot.SDK@*
#:package Dumpify@*

using GitHub.Copilot.SDK;
using Dumpify;

#pragma warning disable IL2026, IL3050

await using var client = new CopilotClient();

// Session with hooks -- OnPreToolUse denies reading this file (07_Hooks.cs)
await using var session = await client.CreateSessionAsync(new()
{
    OnPermissionRequest = PermissionHandler.ApproveAll,
    Hooks = new SessionHooks
    {
        OnPreToolUse = (input, invocation) =>
        {
            // Check if the tool arguments reference the protected file
            var argsJson = System.Text.Json.JsonSerializer.Serialize(input.ToolArgs);
            var isBlocked = argsJson.Contains("07_Hooks.cs", StringComparison.OrdinalIgnoreCase);

            if (isBlocked)
            {
                Console.WriteLine($"  [Hook:PreToolUse]  DENIED  tool={input.ToolName}  reason=file 07_Hooks.cs is protected");
                return Task.FromResult<PreToolUseHookOutput?>(new PreToolUseHookOutput
                {
                    PermissionDecision = "deny",
                    PermissionDecisionReason = "Access to 07_Hooks.cs is denied by a session hook. Try a different file.",
                });
            }

            Console.WriteLine($"  [Hook:PreToolUse]  ALLOWED tool={input.ToolName}");
            return Task.FromResult<PreToolUseHookOutput?>(new PreToolUseHookOutput
            {
                PermissionDecision = "allow",
            });
        },
        OnPostToolUse = (input, invocation) =>
        {
            Console.WriteLine($"  [Hook:PostToolUse] tool={input.ToolName} completed");
            return Task.FromResult<PostToolUseHookOutput?>(null);
        },
    },
    CustomAgents =
    [
        new()
        {
            Name = "file-analyst",
            DisplayName = "File Analyst",
            Description = "Reads and analyses local files on disk using the filesystem MCP server",
            Prompt = "You are a file analyst. Use the filesystem tools to read files and describe their contents.",
            Infer = true,
            McpServers = new Dictionary<string, object>
            {
                ["filesystem"] = new McpLocalServerConfig
                {
                    Type = "local",
                    Command = "npx",
                    Args = ["-y", "@modelcontextprotocol/server-filesystem", "."],
                    Tools = ["*"],
                },
            },
        },
    ],
});

// Subscribe to sub-agent lifecycle + reasoning events
session.On(evt =>
{
    switch (evt)
    {
        case AssistantReasoningDeltaEvent reasoning:
            Console.Write(reasoning.Data.DeltaContent);
            break;
        case SubagentStartedEvent started:
            Console.WriteLine($"[Sub-agent Started]   {started.Data.AgentDisplayName}");
            break;
        case SubagentCompletedEvent completed:
            Console.WriteLine($"[Sub-agent Completed] {completed.Data.AgentDisplayName}");
            break;
        case SubagentFailedEvent failed:
            Console.WriteLine($"[Sub-agent Failed]    {failed.Data.AgentDisplayName}: {failed.Data.Error}");
            break;
    }
});

// Ask the agent to read 07_Hooks.cs (will be denied) and then fall back to another file
Console.WriteLine("[Main] Sending prompt -- the hook will block access to 07_Hooks.cs...");
Console.WriteLine();

var response = await session.SendAndWaitAsync(
    new MessageOptions
    {
        Prompt = """
        I need you to do two things:

        1. Use the file-analyst agent to read the file "demo/07_Hooks.cs" and summarise its contents.
           (Note: this may be denied by a hook -- if so, report what happened.)

        2. Then, use the file-analyst agent to read "demo/00_Helloworld.cs" instead and summarise that file.

        Present both results clearly, noting which file was accessible and which was denied.
        Format the output in markdown.
        """,
    },
    timeout: TimeSpan.FromMinutes(5)
);

Console.WriteLine();
Console.WriteLine("=== Final Output ===");
response?.Data.Content.Dump();

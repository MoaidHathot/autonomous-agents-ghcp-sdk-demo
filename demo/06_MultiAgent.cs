#!/usr/bin/env dotnet
#:package GitHub.Copilot.SDK@*
#:package Dumpify@*

using GitHub.Copilot.SDK;
using Dumpify;

await using var client = new CopilotClient();

// Single session with custom agents -- the runtime delegates to sub-agents automatically
await using var session = await client.CreateSessionAsync(new()
{
    OnPermissionRequest = PermissionHandler.ApproveAll,
    CustomAgents =
    [
        new()
        {
            Name = "web-researcher",
            DisplayName = "Web Researcher",
            Description = "Fetches and summarises content from the web using the fetch MCP server",
            Prompt = "You are a web researcher. Use the fetch tool to retrieve web pages and produce concise bullet-point summaries.",
            Infer = true,
            McpServers = new Dictionary<string, object>
            {
                ["fetch"] = new McpLocalServerConfig
                {
                    Type = "local",
                    Command = "npx",
                    Args = ["-y", "@modelcontextprotocol/server-fetch"],
                    Tools = ["*"],
                },
            },
        },
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

// Subscribe to sub-agent lifecycle events
session.On(evt =>
{
    switch (evt)
    {
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

// One prompt -- the parent agent delegates to sub-agents as needed
Console.WriteLine("[Main] Sending prompt -- the parent agent will orchestrate sub-agents...");
Console.WriteLine();

var response = await session.SendAndWaitAsync(
    new MessageOptions
    {
        Prompt = """
        I need you to do two things and then combine the results:

        1. Use the web-researcher agent to fetch https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-10/overview
           and produce a concise bullet-point summary of the key .NET 10 highlights.

        2. Use the file-analyst agent to list all .cs files in the current directory (recursively)
           and give a one-line description of each file based on its contents.

        After both tasks are done, write a short README-style paragraph (under 200 words)
        that describes this demo project and how it showcases .NET 10 features.
        Format the result in markdown.
        """,
    },
    timeout: TimeSpan.FromMinutes(5)
);

Console.WriteLine();
Console.WriteLine("=== Final Output ===");
response?.Data.Content.Dump();

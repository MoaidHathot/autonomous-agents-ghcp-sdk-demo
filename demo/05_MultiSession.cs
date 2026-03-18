#!/usr/bin/env dotnet
#:package GitHub.Copilot.SDK@*

using GitHub.Copilot.SDK;

await using var client = new CopilotClient();

await using var session1 = await client.CreateSessionAsync(new()
{
    OnPermissionRequest = PermissionHandler.ApproveAll,
	Model = "claude-opus-4.6",
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
});

await using var session2 = await client.CreateSessionAsync(new()
{
    OnPermissionRequest = PermissionHandler.ApproveAll,
	Model = "claude-sonnet-4.6",
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
});


Console.WriteLine("[Session 1] Starting: fetching .NET 10 highlights from the web...");
var task1 = session1.SendAndWaitAsync(new MessageOptions
{
    Prompt = "Fetch https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-10/overview and give me a concise bullet-point summary of the key highlights in .NET 10.",
});

Console.WriteLine("[Session 2] Starting: reading local demo files...");
var task2 = session2.SendAndWaitAsync(new MessageOptions
{
    Prompt = "List all .cs files in the current directory (recursively). For each file, give me its name and a one-line description of what it does based on its contents.",
});

var results = await Task.WhenAll(task1, task2);

var webSummary = results[0]?.Data.Content ?? "No response";
var fileSummary = results[1]?.Data.Content ?? "No response";

Console.WriteLine("[Session 1] Done.");
Console.WriteLine("[Session 2] Done.");

await using var session3 = await client.CreateSessionAsync(new()
{
    OnPermissionRequest = PermissionHandler.ApproveAll,
});

Console.WriteLine("[Session 3] Starting: combining results...");
var response = await session3.SendAndWaitAsync(new MessageOptions
{
    Prompt = $"""
    I have two pieces of information gathered by separate agents:

    ## Web Research: .NET 10 Highlights
    {webSummary}

    ## Local Project: Demo File Inventory
    {fileSummary}

    Based on these two inputs, write a short README-style paragraph that describes
    this demo project and how it showcases .NET 10 features. Keep it under 200 words.
    The result should be structured in markdown format
""",
});
Console.WriteLine("[Session 3] Done.");

Console.WriteLine();
Console.WriteLine("=== Final Output ===");
Console.WriteLine(response?.Data.Content);

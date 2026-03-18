#!/usr/bin/env dotnet
#:package GitHub.Copilot.SDK@*
#:package Dumpify@*

using System.Text.Json;
using GitHub.Copilot.SDK;
using Dumpify;

#pragma warning disable IL2026 // Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access otherwise can break functionality when trimming application code
#pragma warning disable CA1869 // Cache and reuse 'JsonSerializerOptions' instances
#pragma warning disable IL3050 // Calling members annotated with 'RequiresDynamicCodeAttribute' may break functionality when AOT compiling.

await using var client = new CopilotClient();
await using var session = await client.CreateSessionAsync(new()
{
    OnPermissionRequest = PermissionHandler.ApproveAll,
    Streaming = true,
    McpServers = new Dictionary<string, object>
    {
        // Fetch MCP server -- lets the agent fetch and read web pages
        ["fetch"] = new McpLocalServerConfig
        {
            Type = "local",
            Command = "npx",
            Args = ["-y", "@modelcontextprotocol/server-fetch"],
            Tools = ["*"],
        },
        // Filesystem MCP server -- lets the agent read/write/search local files
        ["filesystem"] = new McpLocalServerConfig
        {
            Type = "local",
            Command = "npx",
            Args = ["-y", "@modelcontextprotocol/server-filesystem", ".", @"C:\Users\moaid\OneDrive\Documents\Obsidian Vault\Cooking\"],
            Tools = ["*"],
        },
    },
});

var model = "";
var toolCallNames = new Dictionary<string, string>();

session.On(evt =>
{
    switch (evt)
    {
        case ToolExecutionStartEvent toolStart:
            toolCallNames[toolStart.Data.ToolCallId] = toolStart.Data.ToolName;
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"[Tool Start] {toolStart.Data.ToolName}");
            Console.ResetColor();
            if (toolStart.Data.Arguments is JsonElement args)
            {
                var json = JsonSerializer.Serialize(args, new JsonSerializerOptions { WriteIndented = true });
                json.Dump($"Args: {toolStart.Data.ToolName}");
            }
            break;

        case ToolExecutionCompleteEvent toolComplete:
            var toolName = toolCallNames.GetValueOrDefault(toolComplete.Data.ToolCallId, "unknown");
            var status = toolComplete.Data.Success ? "Done" : "Failed";
            Console.ForegroundColor = toolComplete.Data.Success ? ConsoleColor.Green : ConsoleColor.Red;
            Console.WriteLine($"[Tool {status}]  {toolName}");
            Console.ResetColor();
            if (toolComplete.Data.Result is { } result)
                result.Content.Dump($"Result: {toolName}");
            if (toolComplete.Data.Error is { } error)
                error.Message.Dump($"Error: {toolName}");
            break;

        case AssistantMessageDeltaEvent delta:
            // Console.Write(delta.Data.DeltaContent);
            break;

        case AssistantReasoningDeltaEvent delta:
            Console.Write(delta.Data.DeltaContent);
            break;

        case AssistantUsageEvent usage:
            model = usage.Data.Model;
            break;

        case AssistantMessageEvent msg:
            // Console.WriteLine(msg.Data.Content);
            break;
    }
});

var recipePath = @"C:\Users\moaid\OneDrive\Documents\Obsidian Vault\Cooking\Potatoes\Crispy Oven-Crushed Potatoes.md";
var response = await session.SendAndWaitAsync(new MessageOptions
{
    Prompt = $"""
	First Look at the ingredients for my crispy potatoes recipes at {recipePath}
	Then search using duckduckgo (use server-fetch and baseurl: `https://html.duckduckgo.com/html/?q=`, where is the closest grocery store to me
	If there are multiple in radius of 5 miles, pick the one with the cheapest prices
	I live near University Village, Seattle, WA
	""",
});
response?.Data.Content.Dump();
Console.WriteLine($"Model used: {model}");

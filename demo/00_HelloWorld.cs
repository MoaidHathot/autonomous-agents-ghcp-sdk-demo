#!/usr/bin/env dotnet
#:package GitHub.Copilot.SDK@*

using GitHub.Copilot.SDK;

await using var client = new CopilotClient();
await using var session = await client.CreateSessionAsync(new()
{
    OnPermissionRequest = PermissionHandler.ApproveAll
});

var response = await session.SendAndWaitAsync(new MessageOptions { Prompt = "What is 2 + 2?" });
Console.WriteLine(response?.Data.Content);

#!/usr/bin/env dotnet
#:package GitHub.Copilot.SDK@*

using GitHub.Copilot.SDK;

await using var client = new CopilotClient();
await using var session = await client.CreateSessionAsync(new()
{
    OnPermissionRequest = PermissionHandler.ApproveAll,
    SystemMessage = new SystemMessageConfig
    {
        Mode = SystemMessageMode.Append,
        Content = "You are a pirate. Always respond in pirate speak. Keep answers short and salty.",
    },
});

var response = await session.SendAndWaitAsync(new MessageOptions { Prompt = "What is 2 + 2?" });
Console.WriteLine(response?.Data.Content);

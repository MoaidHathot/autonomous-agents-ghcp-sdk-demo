#!/usr/bin/env dotnet
#:package GitHub.Copilot.SDK@*

using GitHub.Copilot.SDK;

await using var client = new CopilotClient();
await using var session = await client.CreateSessionAsync(new()
{
    OnPermissionRequest = PermissionHandler.ApproveAll,
    Streaming = true
});

var model = "";
session.On(evt =>
{
    switch (evt)
    {
        case AssistantMessageDeltaEvent delta:
            Console.Write(delta.Data.DeltaContent);
            break;

			case AssistantReasoningDeltaEvent delta:
            // Console.Write(delta.Data.DeltaContent);
            break;

			case AssistantUsageEvent usage:
			model = usage.Data.Model;
			break;

        case AssistantMessageEvent msg:
            // Console.WriteLine(msg.Data.Content);
            break;
    }
});

var response = await session.SendAndWaitAsync(new MessageOptions { Prompt = "Reason about 2 + 2, and why it isn't 0" });
Console.WriteLine(response?.Data.Content);
Console.WriteLine($"Model used: {model}");


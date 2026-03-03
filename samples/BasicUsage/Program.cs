using JD.SemanticKernel.Connectors.OpenAICodex;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

// Build a kernel with Codex authentication.
// This automatically resolves credentials from:
//   1. OPENAI_API_KEY environment variable
//   2. ~/.codex/auth.json (Codex CLI credentials)
var kernel = Kernel.CreateBuilder()
    .UseCodexChatCompletion(CodexModels.O4Mini)
    .Build();

var chat = kernel.GetRequiredService<IChatCompletionService>();
var history = new ChatHistory("You are a helpful assistant.");

Console.WriteLine("Chat with OpenAI via Codex credentials. Type 'exit' to quit.\n");

while (true)
{
    Console.Write("You: ");
    var input = Console.ReadLine();
    if (string.IsNullOrWhiteSpace(input) || input.Equals("exit", StringComparison.OrdinalIgnoreCase))
        break;

    history.AddUserMessage(input);

    Console.Write("AI: ");
    var response = await chat.GetChatMessageContentAsync(history);
    Console.WriteLine(response.Content);
    Console.WriteLine();

    history.AddAssistantMessage(response.Content ?? string.Empty);
}

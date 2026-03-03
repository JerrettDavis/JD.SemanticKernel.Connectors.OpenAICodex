# JD.SemanticKernel.Connectors.OpenAICodex

[![NuGet](https://img.shields.io/nuget/v/JD.SemanticKernel.Connectors.OpenAICodex)](https://www.nuget.org/packages/JD.SemanticKernel.Connectors.OpenAICodex)
[![CI](https://github.com/JerrettDavis/JD.SemanticKernel.Connectors.OpenAICodex/actions/workflows/ci.yml/badge.svg)](https://github.com/JerrettDavis/JD.SemanticKernel.Connectors.OpenAICodex/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

OpenAI Codex session connector for [Semantic Kernel](https://github.com/microsoft/semantic-kernel).
Provides `UseCodexChatCompletion()` to wire local Codex CLI OAuth credentials (or an explicit API key) into any Semantic Kernel application without managing tokens manually.

## Features

- **Multi-source credential resolution** — Options → `OPENAI_API_KEY` → `CODEX_TOKEN` → `~/.codex/auth.json` → interactive device code login
- **Token exchange** — Automatically exchanges Codex OAuth tokens for OpenAI API keys via the standard token exchange endpoint
- **Token refresh** — Automatically refreshes expired tokens using stored refresh tokens
- **Device code login** — Full interactive `codex login` equivalent for .NET applications
- **Semantic Kernel integration** — One-call `UseCodexChatCompletion()` extension method
- **DI support** — `AddCodexAuthentication()` for dependency injection
- **Model discovery** — `IModelDiscoveryProvider` with known Codex models (o3, o4-mini, codex-mini, GPT-4.1, etc.)
- **Multi-TFM** — Targets `netstandard2.0`, `net8.0`, and `net10.0`

## Installation

```bash
dotnet add package JD.SemanticKernel.Connectors.OpenAICodex
```

## Quick Start

### Minimal (auto-discovers credentials)

```csharp
using JD.SemanticKernel.Connectors.OpenAICodex;
using Microsoft.SemanticKernel;

var kernel = Kernel.CreateBuilder()
    .UseCodexChatCompletion()
    .Build();
```

### With explicit API key

```csharp
var kernel = Kernel.CreateBuilder()
    .UseCodexChatCompletion(apiKey: "sk-your-openai-api-key")
    .Build();
```

### With specific model

```csharp
var kernel = Kernel.CreateBuilder()
    .UseCodexChatCompletion(CodexModels.O3)
    .Build();
```

### Using Dependency Injection

```csharp
services.AddCodexAuthentication(o => o.ApiKey = "sk-...");

// Or from configuration:
services.AddCodexAuthentication(configuration);
// Reads from "CodexSession" section in appsettings.json
```

### Direct HttpClient usage

```csharp
using var client = CodexHttpClientFactory.Create();
// Use with any OpenAI-compatible API
```

## Credential Resolution Order

The provider checks these sources in order and uses the first valid credential found:

| Priority | Source | Description |
|----------|--------|-------------|
| 1 | `CodexSessionOptions.ApiKey` | Explicit API key from options/config |
| 2 | `CodexSessionOptions.AccessToken` | Explicit OAuth token from options/config |
| 3 | `OPENAI_API_KEY` env var | Standard OpenAI environment variable |
| 4 | `CODEX_TOKEN` env var | Codex-specific environment variable |
| 5 | `~/.codex/auth.json` | Codex CLI local credentials file |
| 6 | Device code login | Interactive OAuth (when `EnableInteractiveLogin = true`) |

## Configuration

### appsettings.json

```json
{
  "CodexSession": {
    "ApiKey": null,
    "Issuer": "https://auth.openai.com",
    "ApiBaseUrl": "https://api.openai.com/v1",
    "AutoRefreshTokens": true,
    "EnableInteractiveLogin": false
  }
}
```

### Available Options

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `ApiKey` | `string?` | `null` | Explicit OpenAI API key |
| `AccessToken` | `string?` | `null` | Explicit OAuth access token |
| `CredentialsPath` | `string?` | `null` | Custom path to `auth.json` |
| `Issuer` | `string` | `https://auth.openai.com` | OAuth issuer URL |
| `ApiBaseUrl` | `string` | `https://api.openai.com/v1` | OpenAI API base URL |
| `ClientId` | `string?` | `null` | OAuth client ID for device code flow |
| `EnableInteractiveLogin` | `bool` | `false` | Enable interactive device code login |
| `AutoRefreshTokens` | `bool` | `true` | Auto-refresh expired tokens |

## Available Models

| Constant | Model ID | Description |
|----------|----------|-------------|
| `CodexModels.O3` | `o3` | Most capable reasoning model |
| `CodexModels.O4Mini` | `o4-mini` | Fast, cost-effective reasoning |
| `CodexModels.CodexMini` | `codex-mini-latest` | Optimised for code generation |
| `CodexModels.Gpt4Point1` | `gpt-4.1` | High-capability general model |
| `CodexModels.Gpt4Point1Mini` | `gpt-4.1-mini` | Fast general model |
| `CodexModels.Gpt4Point1Nano` | `gpt-4.1-nano` | Fastest, most cost-effective |

## Related Packages

- [JD.SemanticKernel.Connectors.ClaudeCode](https://github.com/JerrettDavis/JD.SemanticKernel.Connectors.ClaudeCode) — Claude Code connector
- [JD.SemanticKernel.Connectors.GitHubCopilot](https://github.com/JerrettDavis/JD.SemanticKernel.Connectors.GitHubCopilot) — GitHub Copilot connector
- [JD.SemanticKernel.Connectors.Abstractions](https://www.nuget.org/packages/JD.SemanticKernel.Connectors.Abstractions) — Shared abstractions

## License

[MIT](LICENSE)

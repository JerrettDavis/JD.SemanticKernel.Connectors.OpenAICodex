# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- Initial release of `JD.SemanticKernel.Connectors.OpenAICodex`
- `CodexSessionProvider` — multi-source credential resolution (options → env vars → `~/.codex/auth.json` → device code login)
- `CodexDeviceCodeAuth` — interactive OAuth device code flow (`codex login` equivalent)
- `CodexTokenRefresher` — automatic token refresh using refresh tokens
- `CodexModelDiscovery` — `IModelDiscoveryProvider` for OpenAI Codex models
- `KernelBuilderExtensions.UseCodexChatCompletion()` — one-call Semantic Kernel integration
- `ServiceCollectionExtensions.AddCodexAuthentication()` — DI registration
- Support for `OPENAI_API_KEY` and `CODEX_TOKEN` environment variables
- Token exchange flow (OAuth token → API key via OpenAI token exchange endpoint)
- Comprehensive test suite with mocked HTTP handlers

using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Antiphon.Server.Application.Services;

public class LlmProviderService
{
    private readonly AppDbContext _db;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<LlmProviderService> _logger;

    public LlmProviderService(
        AppDbContext db,
        IHttpClientFactory httpClientFactory,
        ILogger<LlmProviderService> logger)
    {
        _db = db;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<List<LlmProviderDto>> GetAllAsync(CancellationToken cancellationToken)
    {
        var providers = await _db.LlmProviders
            .OrderBy(p => p.Name)
            .Select(p => ToDto(p))
            .ToListAsync(cancellationToken);

        return providers;
    }

    public async Task<LlmProviderDto> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var provider = await _db.LlmProviders
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
            ?? throw new NotFoundException(nameof(LlmProvider), id);

        return ToDto(provider);
    }

    public async Task<LlmProviderDto> CreateAsync(
        CreateLlmProviderRequest request, CancellationToken cancellationToken)
    {
        ValidateRequest(request.Name, request.ProviderType, request.BaseUrl);

        var provider = new LlmProvider
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            ProviderType = request.ProviderType,
            ApiKey = request.ApiKey,
            BaseUrl = request.BaseUrl,
            IsEnabled = request.IsEnabled,
            DefaultModel = request.DefaultModel,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _db.LlmProviders.Add(provider);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Created LLM provider {ProviderName} ({ProviderType})", provider.Name, provider.ProviderType);

        return ToDto(provider);
    }

    public async Task<LlmProviderDto> UpdateAsync(
        Guid id, UpdateLlmProviderRequest request, CancellationToken cancellationToken)
    {
        var provider = await _db.LlmProviders
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
            ?? throw new NotFoundException(nameof(LlmProvider), id);

        ValidateRequest(request.Name, request.ProviderType, request.BaseUrl);

        provider.Name = request.Name;
        provider.ProviderType = request.ProviderType;
        provider.BaseUrl = request.BaseUrl;
        provider.IsEnabled = request.IsEnabled;
        provider.DefaultModel = request.DefaultModel;
        provider.UpdatedAt = DateTime.UtcNow;

        // Only update API key if a new one is provided (non-null, non-empty)
        if (!string.IsNullOrEmpty(request.ApiKey))
        {
            provider.ApiKey = request.ApiKey;
        }

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Updated LLM provider {ProviderName} ({ProviderId})", provider.Name, provider.Id);

        return ToDto(provider);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var provider = await _db.LlmProviders
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
            ?? throw new NotFoundException(nameof(LlmProvider), id);

        _db.LlmProviders.Remove(provider);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Deleted LLM provider {ProviderName} ({ProviderId})", provider.Name, provider.Id);
    }

    public async Task<TestProviderResult> TestConnectivityAsync(Guid id, CancellationToken cancellationToken)
    {
        var provider = await _db.LlmProviders
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
            ?? throw new NotFoundException(nameof(LlmProvider), id);

        try
        {
            return provider.ProviderType switch
            {
                ProviderType.Anthropic => await TestAnthropicAsync(provider, cancellationToken),
                ProviderType.OpenAI => await TestOpenAiAsync(provider, cancellationToken),
                ProviderType.Ollama => await TestOllamaAsync(provider, cancellationToken),
                _ => new TestProviderResult(false, $"Unknown provider type: {provider.ProviderType}")
            };
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Connectivity test failed for provider {ProviderName}", provider.Name);
            return new TestProviderResult(false, $"Connection failed: {ex.Message}");
        }
        catch (TaskCanceledException)
        {
            return new TestProviderResult(false, "Connection timed out.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unexpected error testing provider {ProviderName}", provider.Name);
            return new TestProviderResult(false, $"Unexpected error: {ex.Message}");
        }
    }

    // --- Model Routing ---

    public async Task<List<ModelRoutingDto>> GetAllRoutingsAsync(CancellationToken cancellationToken)
    {
        var routings = await _db.ModelRoutings
            .OrderBy(r => r.StageName)
            .Select(r => new ModelRoutingDto(r.Id, r.StageName, r.ModelName, r.ProviderId, r.WorkflowTemplateId, r.CreatedAt))
            .ToListAsync(cancellationToken);

        return routings;
    }

    public async Task<List<ModelRoutingDto>> GetRoutingsByTemplateAsync(
        Guid templateId, CancellationToken cancellationToken)
    {
        var routings = await _db.ModelRoutings
            .Where(r => r.WorkflowTemplateId == templateId)
            .OrderBy(r => r.StageName)
            .Select(r => new ModelRoutingDto(r.Id, r.StageName, r.ModelName, r.ProviderId, r.WorkflowTemplateId, r.CreatedAt))
            .ToListAsync(cancellationToken);

        return routings;
    }

    public async Task<ModelRoutingDto> CreateRoutingAsync(
        Guid templateId, CreateModelRoutingRequest request, CancellationToken cancellationToken)
    {
        ValidateRoutingRequest(request.StageName, request.ModelName);

        // Verify provider exists
        var providerExists = await _db.LlmProviders.AnyAsync(p => p.Id == request.ProviderId, cancellationToken);
        if (!providerExists)
        {
            throw new ValidationException("providerId", "The specified provider does not exist.");
        }

        // Verify template exists
        var templateExists = await _db.WorkflowTemplates.AnyAsync(t => t.Id == templateId, cancellationToken);
        if (!templateExists)
        {
            throw new NotFoundException(nameof(Domain.Entities.WorkflowTemplate), templateId);
        }

        var routing = new ModelRouting
        {
            Id = Guid.NewGuid(),
            StageName = request.StageName,
            ModelName = request.ModelName,
            ProviderId = request.ProviderId,
            WorkflowTemplateId = templateId,
            CreatedAt = DateTime.UtcNow
        };

        _db.ModelRoutings.Add(routing);
        await _db.SaveChangesAsync(cancellationToken);

        return new ModelRoutingDto(routing.Id, routing.StageName, routing.ModelName, routing.ProviderId, routing.WorkflowTemplateId, routing.CreatedAt);
    }

    public async Task<ModelRoutingDto> UpdateRoutingAsync(
        Guid id, UpdateModelRoutingRequest request, CancellationToken cancellationToken)
    {
        var routing = await _db.ModelRoutings
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken)
            ?? throw new NotFoundException(nameof(ModelRouting), id);

        ValidateRoutingRequest(request.StageName, request.ModelName);

        var providerExists = await _db.LlmProviders.AnyAsync(p => p.Id == request.ProviderId, cancellationToken);
        if (!providerExists)
        {
            throw new ValidationException("providerId", "The specified provider does not exist.");
        }

        routing.StageName = request.StageName;
        routing.ModelName = request.ModelName;
        routing.ProviderId = request.ProviderId;

        await _db.SaveChangesAsync(cancellationToken);

        return new ModelRoutingDto(routing.Id, routing.StageName, routing.ModelName, routing.ProviderId, routing.WorkflowTemplateId, routing.CreatedAt);
    }

    public async Task DeleteRoutingAsync(Guid id, CancellationToken cancellationToken)
    {
        var routing = await _db.ModelRoutings
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken)
            ?? throw new NotFoundException(nameof(ModelRouting), id);

        _db.ModelRoutings.Remove(routing);
        await _db.SaveChangesAsync(cancellationToken);
    }

    // --- Private helpers ---

    private async Task<TestProviderResult> TestAnthropicAsync(LlmProvider provider, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient();
        var baseUrl = string.IsNullOrEmpty(provider.BaseUrl) ? "https://api.anthropic.com" : provider.BaseUrl;

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl.TrimEnd('/')}/v1/messages");
        request.Headers.Add("x-api-key", provider.ApiKey);
        request.Headers.Add("anthropic-version", "2023-06-01");
        request.Content = new StringContent(
            """{"model":"claude-sonnet-4-20250514","max_tokens":1,"messages":[{"role":"user","content":"Hi"}]}""",
            System.Text.Encoding.UTF8,
            "application/json");

        using var response = await client.SendAsync(request, cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            return new TestProviderResult(true, "Connection successful. Anthropic API is reachable.");
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        return new TestProviderResult(false, $"Anthropic API returned {(int)response.StatusCode}: {body}");
    }

    private async Task<TestProviderResult> TestOpenAiAsync(LlmProvider provider, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient();
        var baseUrl = string.IsNullOrEmpty(provider.BaseUrl) ? "https://api.openai.com" : provider.BaseUrl;

        using var request = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl.TrimEnd('/')}/v1/models");
        request.Headers.Add("Authorization", $"Bearer {provider.ApiKey}");

        using var response = await client.SendAsync(request, cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            return new TestProviderResult(true, "Connection successful. OpenAI API is reachable.");
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        return new TestProviderResult(false, $"OpenAI API returned {(int)response.StatusCode}: {body}");
    }

    private async Task<TestProviderResult> TestOllamaAsync(LlmProvider provider, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient();
        var baseUrl = string.IsNullOrEmpty(provider.BaseUrl) ? "http://localhost:11434" : provider.BaseUrl;

        using var request = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl.TrimEnd('/')}/api/tags");

        using var response = await client.SendAsync(request, cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            return new TestProviderResult(true, "Connection successful. Ollama is reachable.");
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        return new TestProviderResult(false, $"Ollama returned {(int)response.StatusCode}: {body}");
    }

    private static void ValidateRequest(string name, ProviderType providerType, string baseUrl)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(name))
        {
            errors["name"] = ["Name is required."];
        }

        if (!Enum.IsDefined(providerType))
        {
            errors["providerType"] = ["Invalid provider type."];
        }

        if (!string.IsNullOrEmpty(baseUrl) && !Uri.TryCreate(baseUrl, UriKind.Absolute, out _))
        {
            errors["baseUrl"] = ["Base URL must be a valid absolute URL."];
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }
    }

    private static void ValidateRoutingRequest(string stageName, string modelName)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(stageName))
        {
            errors["stageName"] = ["Stage name is required."];
        }

        if (string.IsNullOrWhiteSpace(modelName))
        {
            errors["modelName"] = ["Model name is required."];
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }
    }

    /// <summary>
    /// Maps entity to DTO with masked API key. API keys are NEVER returned to the frontend (NFR7).
    /// </summary>
    private static LlmProviderDto ToDto(LlmProvider entity) =>
        new(
            entity.Id,
            entity.Name,
            entity.ProviderType,
            MaskApiKey(entity.ApiKey),
            entity.BaseUrl,
            entity.IsEnabled,
            entity.DefaultModel,
            entity.CreatedAt,
            entity.UpdatedAt);

    private static string MaskApiKey(string apiKey)
    {
        if (string.IsNullOrEmpty(apiKey))
            return "";

        if (apiKey.Length <= 9)
            return apiKey[0] + new string('*', apiKey.Length - 2) + apiKey[^1];

        return apiKey[..3] + new string('*', apiKey.Length - 6) + apiKey[^3..];
    }
}

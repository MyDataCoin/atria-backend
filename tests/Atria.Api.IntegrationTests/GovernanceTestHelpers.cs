using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;

namespace Atria.Api.IntegrationTests;

/// <summary>
/// Helpers for the actions that now take two people. Publishing an issue and blocking an investor are
/// no longer single calls: one account raises the request, a different account approves it. Tests that
/// only need an open property to work with use <see cref="PublishAsync"/> and stay readable.
/// </summary>
internal static class GovernanceTestHelpers
{
    private const string PropertiesRoute = "/api/v1/properties";
    private const string GovernanceRoute = "/api/v1/governance/critical-actions";
    private const string AdminLoginRoute = "/api/v1/auth/admin/login";

    /// <summary>A super-admin client, used as the second pair of eyes.</summary>
    public static async Task<HttpClient> ApproverClientAsync(AtriaApiFactory factory)
    {
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync(AdminLoginRoute, new
        {
            username = AtriaApiFactory.SuperAdminUsername,
            password = AtriaApiFactory.SuperAdminPassword,
        });
        login.IsSuccessStatusCode.Should().BeTrue("the super admin account is seeded for tests");

        using var doc = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", doc.RootElement.GetProperty("accessToken").GetString());
        return client;
    }

    /// <summary>Raises a publication request as <paramref name="requester"/>; returns the request id.</summary>
    public static async Task<Guid> RequestPublishAsync(HttpClient requester, string propertyId)
    {
        var response = await requester.PostAsync($"{PropertiesRoute}/{propertyId}/publish", null);
        response.StatusCode.Should().Be(HttpStatusCode.Accepted, "publication is only ever requested, never done in one call");

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetGuid();
    }

    /// <summary>Approves a pending request as someone other than the requester.</summary>
    public static async Task ApproveAsync(HttpClient approver, Guid actionId)
        => (await approver.PostAsync($"{GovernanceRoute}/{actionId}/approve", null))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

    /// <summary>Publishes a property the way it now works end to end: requested by one, approved by another.</summary>
    public static async Task PublishAsync(AtriaApiFactory factory, HttpClient requester, string propertyId)
    {
        var actionId = await RequestPublishAsync(requester, propertyId);
        var approver = await ApproverClientAsync(factory);
        await ApproveAsync(approver, actionId);
    }
}

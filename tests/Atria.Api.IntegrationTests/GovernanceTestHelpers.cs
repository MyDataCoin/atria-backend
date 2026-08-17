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
/// Helpers for the actions that take two people — blocking an investor is raised by one account and
/// approved by another. Publishing an issue is NOT one of them: an administrator opens an offering
/// on their own, and <see cref="PublishAsync"/> is a single call kept here so the tests that just
/// need an open property stay readable.
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



    /// <summary>Approves a pending request as someone other than the requester.</summary>
    public static async Task ApproveAsync(HttpClient approver, Guid actionId)
        => (await approver.PostAsync($"{GovernanceRoute}/{actionId}/approve", null))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

    /// <summary>Publishes a property: one admin call, applied immediately.</summary>
    public static async Task PublishAsync(AtriaApiFactory factory, HttpClient admin, string propertyId)
    {
        var response = await admin.PostAsync($"{PropertiesRoute}/{propertyId}/publish", null);
        response.StatusCode.Should().Be(HttpStatusCode.NoContent, "publishing takes effect on the call");
    }
}

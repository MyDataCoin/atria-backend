using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Atria.Api.Swagger;

/// <summary>
/// Adds process-level descriptions to the Swagger tag groups (controller names), so each
/// section of the UI explains the end-to-end flow, not just individual endpoints.
/// </summary>
public sealed class TagDescriptionsDocumentFilter : IDocumentFilter
{
    public void Apply(OpenApiDocument doc, DocumentFilterContext context)
    {
        doc.Tags =
        [
            new OpenApiTag
            {
                Name = "Auth",
                Description =
                    "Phone-only authentication (Kyrgyzstan +996). Flow: request-otp → verify-otp → JWT " +
                    "(access + refresh); refresh rotates the pair with reuse detection. No email/password."
            },
            new OpenApiTag
            {
                Name = "Kyc",
                Description =
                    "KYC verification (hosted provider, e.g. Didit). Flow: (1) POST /kyc/submit returns a " +
                    "verificationUrl and moves the profile to UnderReview; (2) redirect the user to that URL " +
                    "to complete checks; (3) the provider webhook (POST /webhooks/kyc/{provider}) moves the " +
                    "profile to Approved/Rejected; (4) poll GET /kyc/me. Compliance can also review manually. " +
                    "Statuses: Pending → UnderReview → Approved | Rejected."
            },
            new OpenApiTag
            {
                Name = "Consent",
                Description =
                    "Records an investor's acceptance of a consent document version (who/what/when) as " +
                    "regulator evidence. Accepting the personal-data notice (Pdn) of the current version is a " +
                    "precondition for POST /kyc/submit."
            },
            new OpenApiTag
            {
                Name = "Webhooks",
                Description =
                    "Inbound provider callbacks (KYC + payments). Anonymous transport, but the raw body is " +
                    "HMAC-signature + timestamp/replay verified and processed idempotently. The body only " +
                    "moves aggregate State — it is never trusted as a command."
            },
            new OpenApiTag
            {
                Name = "Investments",
                Description =
                    "Investor creates a (PendingPayment) investment in a property, then opens a payment session " +
                    "(provider sent by name, e.g. Stripe/BankTransfer). The payment webhook reconciles the amount, " +
                    "activates the investment, and allocates tokens."
            },
            new OpenApiTag
            {
                Name = "Properties",
                Description = "Tokenization objects (real estate). Public listing/detail; Admin creates."
            },
            new OpenApiTag
            {
                Name = "Documents",
                Description = "Upload/download investor documents (object storage). Owner, or Admin/Compliance, access."
            },
            new OpenApiTag
            {
                Name = "Notifications",
                Description = "The caller's own notifications: list and mark-as-read."
            },
            new OpenApiTag
            {
                Name = "AdminAudit",
                Description = "Immutable audit-log query, filtered by entity type/id (Admin/Compliance only)."
            },
        ];
    }
}

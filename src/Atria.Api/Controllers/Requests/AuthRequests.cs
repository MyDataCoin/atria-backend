using Atria.Application.Properties.Dtos;
using Atria.Domain.Users;
using Atria.Domain.Consents;
using Atria.Domain.Documents;
using Atria.Domain.Governance;
using Atria.Domain.Holders;
using Atria.Domain.Kyc;
using Atria.Domain.Regulatory;

namespace Atria.Api.Controllers.Requests;

// HTTP request bodies. Kept separate from Application commands so the wire shape can
// evolve independently and so multipart/route-bound inputs (IFormFile, phone OTP, IP) map cleanly.

/// <summary>POST /auth/refresh body.</summary>
/// <remarks>
/// The token is NULLABLE on purpose. The browser clients hold no refresh token — it lives in the
/// HttpOnly cookie the API sets — so they post an empty <c>{}</c> body and let the cookie do the
/// work. With a non-nullable <c>string</c> under &lt;Nullable&gt;enable&lt;/Nullable&gt; MVC infers an
/// implicit [Required] and rejects that body with 400 during model validation, i.e. BEFORE the
/// controller ever reads the cookie — which silently breaks every cookie-based refresh.
/// </remarks>
/// <param name="RefreshToken">A valid, unexpired refresh token, or null to authenticate by cookie.</param>
public sealed record RefreshTokenRequest(string? RefreshToken);

/// <summary>POST /auth/admin/login body. Static admin credentials from server configuration.</summary>
/// <param name="Username">The configured admin username.</param>
/// <param name="Password">The configured static admin password.</param>
public sealed record AdminLoginRequest(string Username, string Password);

/// <summary>POST /auth/realtor/login body. Static realtor credentials from server configuration.</summary>
/// <param name="Username">The configured realtor username.</param>
/// <param name="Password">The configured static realtor password.</param>
public sealed record RealtorLoginRequest(string Username, string Password);

/// <summary>POST /auth/register/phone/request-otp body. The IP is captured server-side.</summary>
/// <param name="Phone">Kyrgyz phone number in <c>+996XXXXXXXXX</c> format, e.g. <c>+996700123456</c>.</param>
public sealed record RequestOtpRequest(string Phone);

/// <summary>POST /auth/register/phone/verify-otp body.</summary>
/// <param name="Phone">The same Kyrgyz phone number used to request the code, e.g. <c>+996700123456</c>.</param>
/// <param name="Code">The one-time code received via SMS (a fixed dev code in development).</param>
/// <param name="Intent">
/// Which button the person pressed: <c>login</c> refuses a number that has no account (and creates
/// nothing), <c>register</c> refuses a number that already has one. Omitted or unrecognised means the
/// original behaviour — sign in, creating the account on first use.
/// </param>
public sealed record VerifyOtpRequest(string Phone, string Code, string? Intent = null);

/// <summary>POST /kyc/submit body.</summary>
/// <param name="Provider">The KYC verification provider to open a session with.</param>
/// <param name="WalletAddress">Optional 0x-prefixed 40-hex-character wallet address for token allocation.</param>
/// <param name="FullName">Optional full legal name (max 256 chars).</param>
/// <param name="DocumentNumber">Optional identity document number (max 128 chars).</param>
/// <param name="Nationality">Optional nationality (max 128 chars).</param>
public sealed record SubmitKycRequest(
    KycProviderType Provider,
    string? WalletAddress,
    string? FullName,
    string? DocumentNumber,
    string? Nationality);

/// <summary>PATCH /kyc/wallet body. Links the caller's wallet to their KYC profile after verification.</summary>
/// <param name="WalletAddress">0x-prefixed 40-hex-character wallet address for token allocation.</param>
public sealed record LinkWalletRequest(string WalletAddress);

/// <summary>POST /kyc/{id}/review body. <c>Approve=false</c> requires a <c>Reason</c>.</summary>
/// <param name="Approve"><c>true</c> to approve the profile; <c>false</c> to reject it.</param>
/// <param name="Reason">Required when rejecting; the human-readable rejection reason.</param>
public sealed record ReviewKycRequest(bool Approve, string? Reason);

/// <summary>POST /investments body.</summary>
/// <param name="PropertyId">Identifier of the property to invest in.</param>
/// <param name="Amount">Amount the investor wishes to commit; must be greater than 0.</param>
/// <param name="ReferralToken">Optional realtor referral token the investor arrived with; an invalid or expired token is ignored.</param>
public sealed record CreateInvestmentRequest(Guid PropertyId, decimal Amount, string? ReferralToken = null);

/// <summary>POST /deals body. Creates a realtor referral deal for a property.</summary>
/// <param name="PropertyId">Identifier of the (open) property the referral link points to.</param>
/// <param name="CommissionPercent">The realtor's commission as a percent of the investor's purchase (0–100).</param>
public sealed record CreateDealRequest(Guid PropertyId, decimal CommissionPercent);

/// <summary>PATCH /properties/{id} body. Only the supplied fields are changed.</summary>
/// <param name="Name">New display name; <c>null</c> to leave unchanged.</param>
/// <param name="Description">New description; <c>null</c> to leave unchanged.</param>
/// <param name="Address">New address; <c>null</c> to leave unchanged.</param>
/// <param name="PropertyType">New kind (e.g. residential); <c>null</c> to leave unchanged.</param>
/// <param name="City">New city; <c>null</c> to leave unchanged.</param>
/// <param name="YearBuilt">New build year; <c>null</c> to leave unchanged.</param>
/// <param name="Developer">New developer; <c>null</c> to leave unchanged.</param>
/// <param name="Floors">New floor count; <c>null</c> to leave unchanged.</param>
/// <param name="BuildingId">Move the unit into this building; <c>null</c> to leave unchanged, all-zero Guid to detach it.</param>
/// <param name="UnitType">New unit kind (<c>apartment</c>, <c>garage</c>, …); <c>null</c> to leave unchanged.</param>
/// <param name="UnitNumber">New flat / garage box number; <c>null</c> to leave unchanged.</param>
/// <param name="FloorNumber">New floor the unit is on; <c>null</c> to leave unchanged.</param>
/// <param name="RoomCount">New room count; <c>null</c> to leave unchanged.</param>
/// <param name="Section">Car-park section; <c>null</c> CLEARS it, unlike the fields above.</param>
/// <param name="Row">Row within the section; <c>null</c> CLEARS it.</param>
/// <param name="Spot">The parking space's own number; <c>null</c> CLEARS it.</param>
/// <param name="TotalAreaSqM">New total area in m²; <c>null</c> to leave unchanged.</param>
/// <param name="Rooms">Replaces the whole room breakdown; <c>null</c> leaves it unchanged, <c>[]</c> clears it.</param>
/// <param name="LandAreaHectares">New land plot area in hectares; <c>null</c> to leave unchanged.</param>
/// <param name="LandPlotCode">New cadastre identification code for the plot; <c>null</c> to leave unchanged.</param>
/// <param name="CadastralNumber">New cadastral number of the built object; <c>null</c> to leave unchanged.</param>
/// <param name="ConstructionStage">New construction stage (<c>land_only</c>, <c>design</c>, …); <c>null</c> to leave unchanged.</param>
/// <param name="PlannedCompletionDate">New expected commissioning date; <c>null</c> to leave unchanged.</param>
/// <param name="ReadinessPercent">New reported readiness, 0–100; <c>null</c> to leave unchanged.</param>
/// <param name="IsFreeOfEncumbrances">Cadastre check result; applied only together with <paramref name="EncumbranceCheckedAtUtc"/>.</param>
/// <param name="EncumbranceCheckedAtUtc">When that cadastre check was made.</param>
/// <param name="PayoutFrequency">New distribution frequency (<c>none</c>, <c>monthly</c>, …); <c>null</c> to leave unchanged.</param>
/// <param name="UsableAreaSqM">Usable floor area in m²; must not exceed the total area.</param>
/// <param name="DocumentedUse">Permitted use as written in the title documents; <c>null</c> to leave unchanged.</param>
/// <param name="BuildingClass">Class of the object; <c>null</c> to leave unchanged.</param>
/// <param name="WallMaterial">Construction material; <c>null</c> to leave unchanged.</param>
/// <param name="Heating">Heating arrangement; <c>null</c> to leave unchanged.</param>
/// <param name="Elevator">Lifts, as described; <c>null</c> to leave unchanged.</param>
/// <param name="Security">Security arrangement; <c>null</c> to leave unchanged.</param>
/// <param name="Parking">Parking available with the object; <c>null</c> to leave unchanged.</param>
public sealed record UpdatePropertyRequest(
    string? Name,
    string? Description,
    string? Address,
    string? PropertyType,
    string? City,
    int? YearBuilt,
    string? Developer,
    int? Floors,
    Guid? BuildingId = null,
    string? UnitType = null,
    string? UnitNumber = null,
    int? FloorNumber = null,
    int? RoomCount = null,
    string? Section = null,
    string? Row = null,
    string? Spot = null,
    decimal? TotalAreaSqM = null,
    IReadOnlyList<PropertyRoomInput>? Rooms = null,
    decimal? LandAreaHectares = null,
    string? LandPlotCode = null,
    string? CadastralNumber = null,
    string? ConstructionStage = null,
    DateTime? PlannedCompletionDate = null,
    int? ReadinessPercent = null,
    bool? IsFreeOfEncumbrances = null,
    DateTime? EncumbranceCheckedAtUtc = null,
    string? PayoutFrequency = null,
    decimal? UsableAreaSqM = null,
    string? DocumentedUse = null,
    string? BuildingClass = null,
    string? WallMaterial = null,
    string? Heating = null,
    string? Elevator = null,
    string? Security = null,
    string? Parking = null,
    string? LocationDescription = null);

/// <summary>POST /publications body. Creates and publishes a news-feed item.</summary>
/// <param name="Type">Kind: <c>financial_report</c> | <c>news_release</c> | <c>valuation_audit</c> | <c>general_news</c>.</param>
/// <param name="Title">Headline (max 200 chars).</param>
/// <param name="Body">Plain-text body (max 10 000 chars); newlines are preserved.</param>
/// <param name="PropertyId">Property the item is about; omit or send <c>null</c> for general platform news.</param>
public sealed record CreatePublicationRequest(string Type, string Title, string Body, Guid? PropertyId);

/// <summary>PATCH /publications/{id} body. Only the supplied fields are changed.</summary>
/// <param name="Type">New kind; <c>null</c> to leave unchanged.</param>
/// <param name="Title">New headline; <c>null</c> to leave unchanged.</param>
/// <param name="Body">New body; <c>null</c> to leave unchanged.</param>
public sealed record UpdatePublicationRequest(string? Type, string? Title, string? Body);

/// <summary>POST /consent body. Records the caller's acceptance of a consent document version.</summary>
/// <param name="Type">The consent type, sent by name (e.g. <c>Pdn</c> for the personal-data notice).</param>
/// <param name="Version">Version of the consent text the user accepted (e.g. <c>1.0</c>).</param>
/// <param name="Accepted">Must be <c>true</c>; the endpoint only records acceptance.</param>
public sealed record RecordConsentRequest(ConsentType Type, string Version, bool Accepted);

/// <summary>POST /properties body.</summary>
/// <param name="Name">Display name of the property; required, max 256 characters.</param>
/// <param name="Description">Optional longer description; max 4000 characters.</param>
/// <param name="Address">Optional physical address; max 512 characters.</param>
/// <param name="TotalValue">Total monetary value of the property; must be greater than 0.</param>
/// <param name="TokenPrice">Price of a single token; must be greater than 0.</param>
/// <param name="TotalTokens">Total number of tokens to issue; a whole number greater than 0.</param>
/// <param name="Currency">Currency of the issue; must be <c>KGS</c> — the platform issues in Kyrgyzstani som only.</param>
/// <param name="MinPurchaseTokens">Fewest tokens one application may be for; a whole number, at least 1 and at most <paramref name="TotalTokens"/>. Defaults to 1.</param>
/// <param name="PropertyType">Kind of property (e.g. residential, commercial); optional.</param>
/// <param name="City">City the property is in; optional.</param>
/// <param name="YearBuilt">Year the property was built; optional.</param>
/// <param name="Developer">Developer / builder name; optional.</param>
/// <param name="Floors">Number of storeys; optional.</param>
/// <param name="BuildingId">Building to register this unit in; omit for a standalone issue.</param>
/// <param name="UnitType">What the unit is: <c>apartment</c> | <c>garage</c> | <c>parking_space</c> | <c>commercial</c> | <c>storage</c> | <c>land_plot</c> | <c>other</c>.</param>
/// <param name="UnitNumber">Flat / garage box number inside the building; max 32 characters.</param>
/// <param name="FloorNumber">Floor the unit is on.</param>
/// <param name="RoomCount">How many rooms the unit is sold as (2-, 3-, 4-комнатная). <c>null</c> is normal for a garage.</param>
/// <param name="Section">Car-park section a garage / parking space sits in (e.g. <c>B</c>); optional, max 32 characters.</param>
/// <param name="Row">Row within the section (e.g. <c>12А</c>); optional, max 32 characters.</param>
/// <param name="Spot">The parking space's own number (e.g. <c>125</c>); optional, max 32 characters.</param>
/// <param name="TotalAreaSqM">Total floor area of the unit in m²; must be greater than 0 when sent.</param>
/// <param name="Rooms">Room breakdown, e.g. <c>[{ "name": "Кухня+Столовая", "areaSqM": 28.68 }]</c>.</param>
/// <param name="LandAreaHectares">Area of the land plot in hectares; must be greater than 0 when sent. Not floor area.</param>
/// <param name="LandPlotCode">The plot's identification code in the state cadastre (e.g. <c>1-04-13-0033-0135</c>); optional.</param>
/// <param name="CadastralNumber">Cadastral number of the built object, when there is one; optional.</param>
/// <param name="ConstructionStage">How far along the object is: <c>land_only</c> | <c>design</c> | <c>under_construction</c> | <c>commissioned</c>; optional.</param>
/// <param name="PlannedCompletionDate">Expected commissioning date; optional.</param>
/// <param name="ReadinessPercent">Reported construction readiness, 0–100; optional.</param>
/// <param name="PayoutFrequency">How often the issue distributes: <c>none</c> | <c>monthly</c> | <c>quarterly</c> | <c>annually</c>; optional.</param>
/// <param name="UsableAreaSqM">Usable floor area in m²; must not exceed the total area.</param>
/// <param name="DocumentedUse">Permitted use as written in the title documents; optional.</param>
/// <param name="BuildingClass">Class of the object; optional.</param>
/// <param name="WallMaterial">Construction material; optional.</param>
/// <param name="Heating">Heating arrangement; optional.</param>
/// <param name="Elevator">Lifts, as described; optional.</param>
/// <param name="Security">Security arrangement; optional.</param>
/// <param name="Parking">Parking available with the object; optional.</param>
/// <param name="LocationDescription">The neighbourhood: infrastructure, transport, what is around the object; optional.</param>
public sealed record CreatePropertyRequest(
    string Name,
    string? Description,
    string? Address,
    decimal TotalValue,
    decimal TokenPrice,
    long TotalTokens,
    string Currency,
    long MinPurchaseTokens = 1,
    string? PropertyType = null,
    string? City = null,
    int? YearBuilt = null,
    string? Developer = null,
    int? Floors = null,
    Guid? BuildingId = null,
    string? UnitType = null,
    string? UnitNumber = null,
    int? FloorNumber = null,
    int? RoomCount = null,
    string? Section = null,
    string? Row = null,
    string? Spot = null,
    decimal? TotalAreaSqM = null,
    IReadOnlyList<PropertyRoomInput>? Rooms = null,
    decimal? LandAreaHectares = null,
    string? LandPlotCode = null,
    string? CadastralNumber = null,
    string? ConstructionStage = null,
    DateTime? PlannedCompletionDate = null,
    int? ReadinessPercent = null,
    string? PayoutFrequency = null,
    decimal? UsableAreaSqM = null,
    string? DocumentedUse = null,
    string? BuildingClass = null,
    string? WallMaterial = null,
    string? Heating = null,
    string? Elevator = null,
    string? Security = null,
    string? Parking = null,
    string? LocationDescription = null);

/// <summary>POST /buildings body. Registers the building an admin then fills with units.</summary>
/// <param name="Name">Display name (e.g. "ЖК Ала-Тоо, блок B").</param>
/// <param name="Description">Optional longer description.</param>
/// <param name="Address">Physical address.</param>
/// <param name="City">City the building is in.</param>
/// <param name="Developer">Developer / builder name.</param>
/// <param name="YearBuilt">Year the building was built.</param>
/// <param name="Floors">Number of storeys.</param>
/// <param name="BuildingType">Kind of building (residential, commercial, mixed).</param>
public sealed record CreateBuildingRequest(
    string Name,
    string? Description = null,
    string? Address = null,
    string? City = null,
    string? Developer = null,
    int? YearBuilt = null,
    int? Floors = null,
    string? BuildingType = null);

/// <summary>PATCH /buildings/{id} body. Only the supplied fields are changed.</summary>
/// <param name="Name">New display name; <c>null</c> to leave unchanged.</param>
/// <param name="Description">New description; <c>null</c> to leave unchanged.</param>
/// <param name="Address">New address; <c>null</c> to leave unchanged.</param>
/// <param name="City">New city; <c>null</c> to leave unchanged.</param>
/// <param name="Developer">New developer; <c>null</c> to leave unchanged.</param>
/// <param name="YearBuilt">New build year; <c>null</c> to leave unchanged.</param>
/// <param name="Floors">New storey count; <c>null</c> to leave unchanged.</param>
/// <param name="BuildingType">New building kind; <c>null</c> to leave unchanged.</param>
public sealed record UpdateBuildingRequest(
    string? Name = null,
    string? Description = null,
    string? Address = null,
    string? City = null,
    string? Developer = null,
    int? YearBuilt = null,
    int? Floors = null,
    string? BuildingType = null);

/// <summary>POST /investments/{id}/reject body. Rejects a reserved offering application.</summary>
/// <param name="Reason">Required human-readable rejection reason shown to the investor and journalled.</param>
public sealed record RejectInvestmentRequest(string Reason);

/// <summary>POST /documents multipart form. The file is bound from the request part.</summary>
/// <param name="File">The document file uploaded as a multipart/form-data part.</param>
/// <param name="Type">Kind of document being uploaded, sent by name.</param>
public sealed record UploadDocumentRequest(IFormFile File, DocumentType Type);

/// <summary>POST /support/tickets body. Opens a new ticket with a first message.</summary>
/// <param name="Subject">Short subject line; required, max 120 characters.</param>
/// <param name="Category">Category label chosen on the client (e.g. <c>KYC</c>, <c>Платежи</c>).</param>
/// <param name="Body">The opening message text; required.</param>
public sealed record CreateTicketRequest(
    string Subject, string Category, string Body, Guid? PropertyId = null);

/// <summary>POST /support/tickets/{id}/messages body. The author is derived from the caller's role.</summary>
/// <param name="Body">The reply text; required.</param>
public sealed record AddTicketMessageRequest(string Body);

/// <summary>POST /users/{id}/ban body. Optional; the reason is shown to the banned user on the blocked screen.</summary>
/// <param name="Reason">Human-readable ban reason; null/empty to ban without a stated reason.</param>
public sealed record BanUserRequest(string? Reason = null);

/// <summary>POST /users/{id}/password/reset body. Optional; omit to have the server generate a temporary password.</summary>
/// <param name="NewPassword">An explicit new password to set; when null/empty a temporary one is generated.</param>
public sealed record ResetPasswordRequest(string? NewPassword = null);

/// <summary>POST /admins body. Creates a staff account (super admin only).</summary>
/// <param name="Username">Login name; must be unique.</param>
/// <param name="FullName">Full name shown in the panel header.</param>
/// <param name="Password">The one-time password handed over; must be changed on first sign-in.</param>
/// <param name="Role">
/// Which staff account to create: <c>Admin</c> (the default when omitted), <c>Finance</c> for an
/// accountant, or <c>Auditor</c> for a lawyer. Anything else is rejected with 400.
/// </param>
public sealed record RegisterAdminRequest(
    string Username, string FullName, string Password, Role Role = Role.Admin);

/// <summary>POST /auth/password/change body. Changes the signed-in account's own password.</summary>
/// <param name="CurrentPassword">The password just used to sign in.</param>
/// <param name="NewPassword">The replacement; six or more characters with upper/lower case, a digit and a symbol.</param>
public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

/// <summary>POST /realtors body. Registers a realtor account (super admin only).</summary>
/// <param name="Username">Login name; must be unique.</param>
/// <param name="Password">Cleartext password set by the super admin; stored hashed.</param>
/// <param name="FullName">Realtor full name.</param>
/// <param name="CompanyName">Company name (optional).</param>
/// <param name="PhoneNumber">Contact phone (optional).</param>
public sealed record RegisterRealtorRequest(
    string Username, string Password, string FullName, string? CompanyName = null, string? PhoneNumber = null);

/// <summary>POST /feedback body. Anonymous question from the public site's feedback form.</summary>
/// <param name="FullName">The sender's name.</param>
/// <param name="Email">Email to answer on.</param>
/// <param name="Phone">Phone to answer on.</param>
/// <param name="Message">The question itself.</param>
public sealed record SubmitFeedbackRequest(string FullName, string Email, string Phone, string Message);

/// <summary>POST /appeals body. Anonymous ban appeal from the blocked screen.</summary>
/// <param name="Username">The login the sender tried to use (optional; helps match an account).</param>
/// <param name="Message">The appeal text; required.</param>
public sealed record SubmitAppealRequest(string? Username, string Message);

/// <summary>POST /holders/snapshots body. Freezes an issuance's holder register at a cut.</summary>
/// <param name="PropertyId">The issuance to snapshot.</param>
/// <param name="Purpose">Why the snapshot is taken (payout run or regulatory statement), sent by name.</param>
/// <param name="SnapshotAtUtc">The cut to freeze; omit to cut at the current instant. Must not be in the future.</param>
public sealed record CreateHolderSnapshotRequest(
    Guid PropertyId, SnapshotPurpose Purpose, DateTime? SnapshotAtUtc = null);

/// <summary>POST /governance/critical-actions body. Raises a request for a second approval.</summary>
/// <param name="Kind">What is being asked for, sent by name.</param>
/// <param name="TargetId">The entity the action applies to.</param>
/// <param name="Reason">Why. Carried into the executed action where it has a meaning (e.g. a ban reason).</param>
public sealed record RequestCriticalActionRequest(CriticalActionKind Kind, Guid TargetId, string? Reason = null);

/// <summary>POST /governance/critical-actions/{id}/reject body.</summary>
/// <param name="Note">Why the request is being declined; required.</param>
public sealed record RejectCriticalActionRequest(string Note);

/// <summary>PATCH /properties/{id}/collateral body. Records the collateral file of an issue.</summary>
/// <param name="CollateralValue">Appraised value of the collateral, in the issue's currency.</param>
/// <param name="CollateralValuedAtUtc">Date of that appraisal.</param>
/// <param name="CollateralAppraiser">Who certified it.</param>
/// <param name="EncumbranceRegistrationNumber">Registration number of the encumbrance in the state register.</param>
/// <param name="EncumbranceRegisteredAtUtc">When the encumbrance was registered; defaults to now.</param>
/// <param name="IssueRegistrationNumber">State registration number of the issue itself.</param>
/// <param name="CollateralManagerUserId">The user acting as collateral manager for this issue.</param>
public sealed record SetPropertyCollateralRequest(
    decimal? CollateralValue = null,
    DateTime? CollateralValuedAtUtc = null,
    string? CollateralAppraiser = null,
    string? EncumbranceRegistrationNumber = null,
    DateTime? EncumbranceRegisteredAtUtc = null,
    string? IssueRegistrationNumber = null,
    Guid? CollateralManagerUserId = null);

/// <summary>PUT /properties/{id}/token-contract body. Binds a deployed token contract to an issue.</summary>
/// <param name="TokenContractAddress">Address of the deployed permissioned token contract.</param>
/// <param name="TokenChain">Tag of the network it is deployed on, e.g. <c>bsc-testnet</c>.</param>
/// <param name="IssuerWalletAddress">Wallet the issuer holds the issue's own shares in.</param>
/// <param name="DeploymentBlock">Block the contract was deployed in; lets the holder registry replay
/// this issue's transfers from the contract's own start instead of the chain's.</param>
public sealed record SetPropertyTokenContractRequest(
    string TokenContractAddress,
    string TokenChain,
    string IssuerWalletAddress,
    long? DeploymentBlock = null);

/// <summary>POST /regulatory-reports body. Records a filing obligation and its deadline.</summary>
/// <param name="Kind">Which notification, sent by name.</param>
/// <param name="PeriodStartUtc">Start of the period covered.</param>
/// <param name="PeriodEndUtc">End of the period covered — the deadline counts from here.</param>
/// <param name="PropertyId">The issue it concerns; omit for platform-wide notifications.</param>
public sealed record RaiseRegulatoryReportRequest(
    RegulatoryReportKind Kind, DateTime PeriodStartUtc, DateTime PeriodEndUtc, Guid? PropertyId = null);

/// <summary>POST /regulatory-reports/{id}/file body.</summary>
/// <param name="FilingReference">The regulator's acknowledgement reference; required.</param>
public sealed record MarkReportFiledRequest(string FilingReference);

/// <summary>POST /properties/{id}/annul-tokens body. Annuls unplaced capacity of an issue.</summary>
/// <param name="TokenCount">How many unplaced shares to annul.</param>
/// <param name="Reason">Why; required and journalled.</param>
public sealed record AnnulTokensRequest(long TokenCount, string Reason);

/// <summary>POST /properties/{id}/invalidate body. Declares an issue invalid (§73).</summary>
/// <param name="Reason">The ground on which the issue is declared invalid; required and journalled.</param>
public sealed record InvalidateIssueRequest(string Reason);

/// <summary>PUT /properties/{id}/images/order body. Sets the gallery order; the first id is the cover.</summary>
/// <param name="ImageIds">Every image of the property, exactly once, in display order.</param>
public sealed record ReorderPropertyImagesRequest(IReadOnlyList<Guid> ImageIds);

/// <summary>POST /properties/{id}/placement body. Sets the placement window and the sum to raise.</summary>
/// <param name="OpensAtUtc">When the placement should open; <c>null</c> to leave unchanged.</param>
/// <param name="ClosesAtUtc">When it should close; <c>null</c> to leave unchanged. Must be after the opening.</param>
/// <param name="TargetAmount">The sum to raise by the closing date; <c>null</c> to leave unchanged.</param>
/// <param name="OfferedAreaSqM">Area being placed in m² when only part of the object is issued; <c>null</c> to leave unchanged. Refused once shares have been placed.</param>
public sealed record SchedulePlacementRequest(
    DateTime? OpensAtUtc,
    DateTime? ClosesAtUtc,
    decimal? TargetAmount,
    decimal? OfferedAreaSqM = null);

/// <summary>POST /properties/{id}/placement/extend body.</summary>
/// <param name="NewClosesAtUtc">The new closing date; must be later than the current one.</param>
/// <param name="Reason">Why the placement is being extended. Required and journalled.</param>
public sealed record ExtendPlacementRequest(DateTime NewClosesAtUtc, string Reason);

/// <summary>POST /properties/{id}/placement/unsubscribed body.</summary>
/// <param name="Reason">Why the placement is declared unsubscribed. Required and journalled.</param>
public sealed record ClosePlacementUnsubscribedRequest(string Reason);

/// <summary>POST /whitelist/mint-lists body. Assembles a batch of whitelisted requests for the exchange.</summary>
/// <param name="PropertyId">The issuance to build the batch for.</param>
/// <param name="EntryIds">
/// Whitelist requests to include. Omit or leave empty to take every mintable request the issuance has.
/// </param>
/// <param name="Note">What this batch is, in the operator's words; journalled.</param>
public sealed record CreateMintListRequest(
    Guid PropertyId, IReadOnlyList<Guid>? EntryIds = null, string? Note = null);

/// <summary>POST /whitelist/mint-lists/{id}/cancel body.</summary>
/// <param name="Reason">Why the batch is being called off; required and journalled.</param>
public sealed record CancelMintListRequest(string Reason);

/// <summary>POST /investments/{id}/annul body. Voids an application that should not stand.</summary>
/// <param name="Reason">Why it is being voided; required and journalled.</param>
/// <param name="RecordRefund">
/// Whether money was received for it and is owed back. Defaults to true: a recorded debt that turns
/// out not to exist is a line someone deletes, a missing one is money an investor never sees again.
/// </param>
public sealed record AnnulInvestmentRequest(string Reason, bool RecordRefund = true);

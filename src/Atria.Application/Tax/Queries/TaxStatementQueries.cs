using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Domain.Tax;

namespace Atria.Application.Tax.Queries;

/// <summary>An issued statement as the investor's list shows it.</summary>
/// <param name="Id">Statement identifier.</param>
/// <param name="Year">Calendar year covered.</param>
/// <param name="Number">Document number printed on the statement.</param>
/// <param name="VerificationCode">Code the holder of the document verifies it by.</param>
/// <param name="TotalInvested">Total invested across holdings at issue time.</param>
/// <param name="TotalIncome">Income paid out over the year.</param>
/// <param name="Currency">Currency of both totals.</param>
/// <param name="IssuedAtUtc">When it was issued.</param>
public sealed record TaxStatementDto(
    Guid Id, int Year, string Number, string VerificationCode,
    decimal TotalInvested, decimal TotalIncome, string Currency, DateTime IssuedAtUtc);

/// <summary>What a verification returns — enough to confirm a document, nothing more.</summary>
/// <param name="Number">Document number.</param>
/// <param name="Year">Year covered.</param>
/// <param name="InvestorFullName">Name the statement was issued to.</param>
/// <param name="TotalInvested">Total invested as stated.</param>
/// <param name="TotalIncome">Income as stated.</param>
/// <param name="Currency">Currency of the amounts.</param>
/// <param name="IssuedAtUtc">When it was issued.</param>
public sealed record TaxStatementVerificationDto(
    string Number, int Year, string InvestorFullName,
    decimal TotalInvested, decimal TotalIncome, string Currency, DateTime IssuedAtUtc);

/// <summary>A rendered statement, ready to be handed over or printed.</summary>
/// <param name="FileName">Suggested file name.</param>
/// <param name="ContentType">MIME type of <paramref name="Content"/>.</param>
/// <param name="Content">File bytes.</param>
public sealed record TaxStatementFileDto(string FileName, string ContentType, byte[] Content);

/// <summary>The caller's own issued statements, newest year first.</summary>
public sealed record GetMyTaxStatementsQuery : IRequest<Result<IReadOnlyList<TaxStatementDto>>>;

/// <summary>Renders one of the caller's statements as a printable document.</summary>
/// <param name="Id">Statement identifier.</param>
public sealed record RenderTaxStatementQuery(Guid Id) : IRequest<Result<TaxStatementFileDto>>;

/// <summary>
/// Renders a draft of the caller's statement for a year — including the year still running — without
/// issuing or storing anything. Marked as a draft and carrying no number or verification code, so it
/// cannot pass for the issued document.
/// </summary>
/// <param name="Year">The year the draft covers.</param>
public sealed record RenderTaxStatementDraftQuery(int Year) : IRequest<Result<TaxStatementFileDto>>;

/// <summary>Verifies a statement by the code printed on it. Anonymous.</summary>
/// <param name="VerificationCode">The code from the document.</param>
public sealed record VerifyTaxStatementQuery(string VerificationCode)
    : IRequest<Result<TaxStatementVerificationDto>>;

public sealed class GetMyTaxStatementsQueryHandler
    : IRequestHandler<GetMyTaxStatementsQuery, Result<IReadOnlyList<TaxStatementDto>>>
{
    private readonly ITaxStatementRepository _statements;
    private readonly ICurrentUserService _currentUser;

    public GetMyTaxStatementsQueryHandler(ITaxStatementRepository statements, ICurrentUserService currentUser)
    {
        _statements = statements;
        _currentUser = currentUser;
    }

    public async Task<Result<IReadOnlyList<TaxStatementDto>>> Handle(
        GetMyTaxStatementsQuery request, CancellationToken ct)
    {
        var userId = _currentUser.UserId;
        if (userId is null)
            return Result.Failure<IReadOnlyList<TaxStatementDto>>(
                Error.Unauthorized("taxStatement.unauthorized", "Authentication is required."));

        IReadOnlyList<TaxStatementDto> dtos = (await _statements.ListByInvestorAsync(userId.Value, ct))
            .Select(s => new TaxStatementDto(
                s.Id, s.Year, s.Number, s.VerificationCode, s.TotalInvested, s.TotalIncome,
                s.Currency, s.IssuedAtUtc))
            .ToList();

        return Result.Success(dtos);
    }
}

public sealed class VerifyTaxStatementQueryHandler
    : IRequestHandler<VerifyTaxStatementQuery, Result<TaxStatementVerificationDto>>
{
    private readonly ITaxStatementRepository _statements;

    public VerifyTaxStatementQueryHandler(ITaxStatementRepository statements) => _statements = statements;

    public async Task<Result<TaxStatementVerificationDto>> Handle(
        VerifyTaxStatementQuery request, CancellationToken ct)
    {
        var code = request.VerificationCode?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(code))
            return Result.Failure<TaxStatementVerificationDto>(
                Error.Validation("taxStatement.codeRequired", "A verification code is required."));

        var statement = await _statements.FindByVerificationCodeAsync(code, ct);
        if (statement is null)
            return Result.Failure<TaxStatementVerificationDto>(
                Error.NotFound("taxStatement.notFound", "No statement matches this code."));

        return Result.Success(new TaxStatementVerificationDto(
            statement.Number, statement.Year, statement.InvestorFullName,
            statement.TotalInvested, statement.TotalIncome, statement.Currency, statement.IssuedAtUtc));
    }
}

/// <summary>
/// Renders the stored statement as a self-contained printable document. The bytes come from the
/// server, so what the investor hands over is what the platform issued — the browser only prints it.
/// </summary>
public sealed class RenderTaxStatementQueryHandler
    : IRequestHandler<RenderTaxStatementQuery, Result<TaxStatementFileDto>>
{
    private readonly ITaxStatementRepository _statements;
    private readonly ICurrentUserService _currentUser;

    public RenderTaxStatementQueryHandler(ITaxStatementRepository statements, ICurrentUserService currentUser)
    {
        _statements = statements;
        _currentUser = currentUser;
    }

    public async Task<Result<TaxStatementFileDto>> Handle(RenderTaxStatementQuery request, CancellationToken ct)
    {
        var userId = _currentUser.UserId;
        if (userId is null)
            return Result.Failure<TaxStatementFileDto>(
                Error.Unauthorized("taxStatement.unauthorized", "Authentication is required."));

        var statement = await _statements.GetByIdAsync(request.Id, ct);

        // Not-found rather than forbidden for someone else's statement: whether a given id exists is
        // itself none of the caller's business.
        if (statement is null || statement.InvestorId != userId.Value)
            return Result.Failure<TaxStatementFileDto>(
                Error.NotFound("taxStatement.notFound", "Statement not found."));

        var html = TaxStatementRenderer.Render(
            statement.Number, statement.Year, statement.InvestorFullName, statement.TotalInvested,
            statement.TotalIncome, statement.Currency, statement.IssuedAtUtc, statement.Content,
            statement.VerificationCode);
        return Result.Success(new TaxStatementFileDto(
            $"{statement.Number}.html", "text/html; charset=utf-8", Encoding.UTF8.GetBytes(html)));
    }
}

public sealed class RenderTaxStatementDraftQueryHandler
    : IRequestHandler<RenderTaxStatementDraftQuery, Result<TaxStatementFileDto>>
{
    private readonly TaxStatementComposer _composer;
    private readonly ICurrentUserService _currentUser;
    private readonly IDateTimeProvider _clock;

    public RenderTaxStatementDraftQueryHandler(
        IInvestmentRepository investments,
        IPayoutRunRepository payouts,
        IPropertyRepository properties,
        IKycRepository kyc,
        ICurrentUserService currentUser,
        IDateTimeProvider clock)
    {
        _composer = new TaxStatementComposer(investments, payouts, properties, kyc);
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<Result<TaxStatementFileDto>> Handle(RenderTaxStatementDraftQuery request, CancellationToken ct)
    {
        var userId = _currentUser.UserId;
        if (userId is null)
            return Result.Failure<TaxStatementFileDto>(
                Error.Unauthorized("taxStatement.unauthorized", "Authentication is required."));

        var now = _clock.UtcNow;
        if (request.Year > now.Year || request.Year < 2000)
            return Result.Failure<TaxStatementFileDto>(Error.Validation(
                "taxStatement.yearOutOfRange", "A draft can only be shown for a year that has started."));

        var composed = await _composer.ComposeAsync(userId.Value, request.Year, now, ct);
        if (composed.IsFailure)
            return Result.Failure<TaxStatementFileDto>(composed.Error);

        var c = composed.Value;
        var html = TaxStatementRenderer.Render(
            null, request.Year, c.FullName, c.TotalInvested, c.TotalIncome, c.Currency, now,
            c.Content, null);
        return Result.Success(new TaxStatementFileDto(
            $"draft-{request.Year}.html", "text/html; charset=utf-8", Encoding.UTF8.GetBytes(html)));
    }
}

/// <summary>
/// The statement as a self-contained printable page. One template for the issued document and the
/// draft, so the two cannot drift apart; a draft (no number) is stamped as such and carries no
/// verification code.
/// </summary>
internal static class TaxStatementRenderer
{

    public static string Render(
        string? number, int year, string fullName, decimal totalInvested, decimal totalIncome,
        string currency, DateTime issuedAtUtc, string contentJson, string? verificationCode)
    {
        var isDraft = number is null;
        var content = JsonDocument.Parse(contentJson).RootElement;
        var rows = new StringBuilder();
        var payoutRows = new StringBuilder();

        if (content.TryGetProperty("payouts", out var payouts))
        {
            foreach (var p in payouts.EnumerateArray())
            {
                var paidAt = p.TryGetProperty("paidAtUtc", out var at) && at.ValueKind == JsonValueKind.String
                    ? at.GetDateTime().ToString("dd.MM.yyyy", Culture)
                    : "—";
                var name = p.TryGetProperty("propertyName", out var pn) && pn.ValueKind == JsonValueKind.String
                    ? pn.GetString()!
                    : "—";
                payoutRows.Append("<tr><td>").Append(paidAt)
                    .Append("</td><td>").Append(Escape(name))
                    .Append("</td><td class=\"num\">").Append(p.GetProperty("tokens").GetInt64().ToString("N0", Culture))
                    .Append("</td><td class=\"num\">").Append(p.GetProperty("amount").GetDecimal().ToString("N2", Culture))
                    .Append("</td></tr>");
            }
        }

        var asOf = content.TryGetProperty("holdingsAsOfUtc", out var asOfValue) && asOfValue.ValueKind == JsonValueKind.String
            ? asOfValue.GetDateTime()
            : issuedAtUtc;

        if (content.TryGetProperty("holdings", out var holdings))
        {
            foreach (var h in holdings.EnumerateArray())
            {
                rows.Append("<tr><td>")
                    .Append(Escape(h.GetProperty("propertyName").GetString() ?? "—"))
                    .Append("</td><td class=\"num\">")
                    .Append(h.GetProperty("tokens").GetInt64().ToString("N0", Culture))
                    .Append("</td><td class=\"num\">")
                    .Append(h.GetProperty("invested").GetDecimal().ToString("N2", Culture))
                    .Append("</td><td class=\"num\">")
                    .Append((h.GetProperty("shareOfIssue").GetDecimal() * 100m).ToString("N4", Culture))
                    .Append(" %</td></tr>");
            }
        }

        var incomeNote = content.TryGetProperty("incomeNote", out var note) ? note.GetString() : null;

        return $$"""
        <!doctype html>
        <html lang="ru">
        <head>
        <meta charset="utf-8">
        <title>{{(isDraft ? $"Черновик справки за {year} год" : Escape(number!))}}</title>
        <style>
          @page { size: A4; margin: 18mm; }
          body { font-family: Georgia, 'Times New Roman', serif; color: #111; line-height: 1.5; }
          h1 { font-size: 20px; margin: 0 0 4px; }
          .muted { color: #666; font-size: 12px; }
          .mono { font-family: 'Courier New', monospace; }
          table { width: 100%; border-collapse: collapse; margin: 18px 0; font-size: 13px; }
          th { text-align: left; border-bottom: 2px solid #111; padding: 6px 4px; font-size: 11px;
               text-transform: uppercase; letter-spacing: .05em; }
          td { border-bottom: 1px solid #ddd; padding: 6px 4px; }
          td.num, th.num { text-align: right; }
          .totals { margin-top: 12px; font-size: 14px; }
          .totals strong { font-size: 18px; }
          footer { margin-top: 28px; border-top: 1px solid #ddd; padding-top: 10px; font-size: 11px; color: #555; }
          .draft { border: 2px dashed #b45309; color: #92400e; background: #fffbeb; padding: 10px 12px;
                   font-size: 13px; margin-bottom: 16px; }
          h2 { font-size: 14px; margin: 22px 0 0; text-transform: uppercase; letter-spacing: .05em; }
        </style>
        </head>
        <body>
          {{(isDraft ? $"<div class=\"draft\"><strong>ЧЕРНОВИК — не является налоговым документом.</strong> Цифры на {issuedAtUtc.ToString("dd.MM.yyyy", Culture)}; справку за {year} год можно будет выпустить после окончания года.</div>" : "")}}
          <h1>Справка о доходе от инвестиций</h1>
          <div class="muted">
            {{(isDraft ? "Черновик" : $"Документ № <span class=\"mono\">{Escape(number!)}</span>")}} ·
            отчётный период: {{year}} год ·
            {{(isDraft ? "сформирован" : "выдан")}} {{issuedAtUtc.ToString("dd.MM.yyyy", Culture)}}
          </div>

          <p style="margin-top:18px">
            Настоящая справка подтверждает, что <strong>{{Escape(fullName)}}</strong>
            по состоянию на {{asOf.ToString("dd.MM.yyyy", Culture)}} владел(а) долями в объектах, перечисленных ниже.
          </p>

          <table>
            <thead>
              <tr>
                <th>Объект</th>
                <th class="num">Долей</th>
                <th class="num">Вложено, {{Escape(currency)}}</th>
                <th class="num">Доля выпуска</th>
              </tr>
            </thead>
            <tbody>{{(rows.Length == 0 ? "<tr><td colspan=\"4\" class=\"muted\">На эту дату долей нет.</td></tr>" : rows.ToString())}}</tbody>
          </table>

          {{(payoutRows.Length == 0 ? "" : $"<h2>Выплаты дохода за {year} год</h2><table><thead><tr><th>Дата</th><th>Объект</th><th class=\"num\">Долей</th><th class=\"num\">Сумма, {Escape(currency)}</th></tr></thead><tbody>{payoutRows}</tbody></table>")}}

          <div class="totals">
            Итого вложено: <strong>{{totalInvested.ToString("N2", Culture)}} {{Escape(currency)}}</strong><br>
            Доход за период: <strong>{{totalIncome.ToString("N2", Culture)}} {{Escape(currency)}}</strong>
            {{(string.IsNullOrWhiteSpace(incomeNote) ? "" : $"<div class=\"muted\">{Escape(incomeNote!)}</div>")}}
          </div>

          <footer>
            {{(isDraft ? "Черновик не имеет номера и кода проверки и не подтверждает доход перед третьими лицами." : $"Проверка подлинности: код <span class=\"mono\">{Escape(verificationCode ?? "")}</span>.")}}
            Справка сформирована на сервере ATRIA; сведения в ней соответствуют записям платформы на
            дату формирования.
          </footer>
        </body>
        </html>
        """;
    }

    private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("ru-RU");

    private static string Escape(string value) => WebUtility.HtmlEncode(value);
}

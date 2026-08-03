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

        var html = Render(statement);
        return Result.Success(new TaxStatementFileDto(
            $"{statement.Number}.html", "text/html; charset=utf-8", Encoding.UTF8.GetBytes(html)));
    }

    private static string Render(TaxStatement statement)
    {
        var content = JsonDocument.Parse(statement.Content).RootElement;
        var rows = new StringBuilder();

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
        <title>{{Escape(statement.Number)}}</title>
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
        </style>
        </head>
        <body>
          <h1>Справка о доходе от инвестиций</h1>
          <div class="muted">
            Документ № <span class="mono">{{Escape(statement.Number)}}</span> ·
            отчётный период: {{statement.Year}} год ·
            выдан {{statement.IssuedAtUtc.ToString("dd.MM.yyyy", Culture)}}
          </div>

          <p style="margin-top:18px">
            Настоящая справка подтверждает, что <strong>{{Escape(statement.InvestorFullName)}}</strong>
            по состоянию на дату выдачи владеет долями в объектах, перечисленных ниже.
          </p>

          <table>
            <thead>
              <tr>
                <th>Объект</th>
                <th class="num">Долей</th>
                <th class="num">Вложено, {{Escape(statement.Currency)}}</th>
                <th class="num">Доля выпуска</th>
              </tr>
            </thead>
            <tbody>{{rows}}</tbody>
          </table>

          <div class="totals">
            Итого вложено: <strong>{{statement.TotalInvested.ToString("N2", Culture)}} {{Escape(statement.Currency)}}</strong><br>
            Доход за период: <strong>{{statement.TotalIncome.ToString("N2", Culture)}} {{Escape(statement.Currency)}}</strong>
            {{(string.IsNullOrWhiteSpace(incomeNote) ? "" : $"<div class=\"muted\">{Escape(incomeNote!)}</div>")}}
          </div>

          <footer>
            Проверка подлинности: код <span class="mono">{{Escape(statement.VerificationCode)}}</span>.
            Справка сформирована на сервере ATRIA; сведения в ней соответствуют записям платформы на
            дату выдачи.
          </footer>
        </body>
        </html>
        """;
    }

    private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("ru-RU");

    private static string Escape(string value) => WebUtility.HtmlEncode(value);
}

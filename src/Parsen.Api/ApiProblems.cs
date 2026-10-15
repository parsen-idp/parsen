namespace Parsen.Api;

/// <summary>Respostas de erro em Problem Details (RFC 9457) com o campo "code" da Parsen.</summary>
internal static class ApiProblems
{
    public const string CodeKey = "code";

    public static IResult Create(int status, string code, string title) =>
        Results.Problem(statusCode: status, title: title, extensions: new Dictionary<string, object?> { [CodeKey] = code });

    public static IResult InvalidRequest(string title) =>
        Create(StatusCodes.Status400BadRequest, "REQUISICAO_INVALIDA", title);

    public static IResult DossierNotFound() =>
        Create(StatusCodes.Status404NotFound, "DOSSIE_NAO_ENCONTRADO", "Dossiê não encontrado.");

    /// <summary>Código padrão quando o erro não definiu um (ex.: 401 gerado pela autenticação).</summary>
    public static string DefaultCode(int? status) => status switch
    {
        StatusCodes.Status400BadRequest => "REQUISICAO_INVALIDA",
        StatusCodes.Status401Unauthorized => "NAO_AUTENTICADO",
        StatusCodes.Status403Forbidden => "ACESSO_NEGADO",
        StatusCodes.Status404NotFound => "NAO_ENCONTRADO",
        StatusCodes.Status409Conflict => "CONFLITO",
        _ => "ERRO_INTERNO",
    };
}

namespace Parsen.Schemas;

/// <summary>Schema e prompt de extração de um tipo de documento, numa versão.</summary>
/// <param name="DocumentType">Tipo do documento (ex.: holerite).</param>
/// <param name="Version">Versão (ex.: v1). Registrada no resultado, para saber qual extração gerou cada dado.</param>
public sealed record ExtractionDefinition(string DocumentType, string Version, string Schema, string SystemPrompt)
{
    /// <summary>Identificador gravado junto do resultado (ex.: holerite.v1).</summary>
    public string Id => $"{DocumentType}.{Version}";

    /// <summary>Ferramenta que o modelo é obrigado a chamar (tool use forçado); o schema é o inputSchema dela.</summary>
    public string ToolName => $"registrar_{DocumentType}";

    /// <summary>
    /// Só as primeiras páginas do documento vão ao modelo (null = todas). Regra do tipo: o comprovante tem os dados
    /// no começo; mandar o resto custaria tokens e exporia dados pessoais desnecessários (minimização, LGPD).
    /// </summary>
    public int? MaxPages { get; init; }

    /// <summary>
    /// Teto de tokens da resposta para o tipo (null = o padrão da configuração). O extrato precisa de mais: cada
    /// crédito custa ~40 tokens de saída, e uma resposta cortada é recusada, nunca entregue pela metade.
    /// </summary>
    public int? MaxTokens { get; init; }
}

/// <summary>Lê os schemas e prompts embutidos no assembly (pasta {tipo}/{tipo}.{versão}.schema.json e .prompt.md).</summary>
public static class ExtractionSchemas
{
    /// <summary>Versão em uso de cada tipo de documento. Trocar a versão exige rodar o Parsen.Eval (regra 8).</summary>
    public static ExtractionDefinition Holerite => Load("holerite", "v2");

    /// <summary>Split e classificação de um arquivo (não é um tipo de documento: decide os tipos).</summary>
    public static ExtractionDefinition Classificacao => Load("classificacao", "v3");

    /// <summary>Contas de consumo (spec, seção 14): os dados ficam na primeira página; a segunda é margem de segurança.</summary>
    public static ExtractionDefinition ComprovanteResidencia => Load("comprovante_residencia", "v2") with { MaxPages = 2 };

    /// <summary>Extrato inteiro (os créditos estão em todas as páginas), com teto de saída maior (spec, seção 14).</summary>
    public static ExtractionDefinition ExtratoBancario => Load("extrato_bancario", "v1") with { MaxTokens = 16_000 };

    /// <summary>
    /// Certidão inteira (o histórico de atos continua por várias fichas). Saída média: atos e partes de uma matrícula
    /// comum cabem em ~4.000 tokens; 8.000 dão folga para históricos longos.
    /// </summary>
    public static ExtractionDefinition MatriculaImovel => Load("matricula_imovel", "v1") with { MaxTokens = 8_000 };

    /// <summary>Schema de extração em uso para o código do tipo (ex.: holerite); null se o tipo ainda não tem schema.</summary>
    public static ExtractionDefinition? ForDocumentType(string documentType) => documentType switch
    {
        "holerite" => Holerite,
        "comprovante_residencia" => ComprovanteResidencia,
        "extrato_bancario" => ExtratoBancario,
        "matricula_imovel" => MatriculaImovel,
        _ => null,
    };

    public static ExtractionDefinition Load(string documentType, string version) => new(
        documentType,
        version,
        ReadResource($"{documentType}.{version}.schema.json"),
        ReadResource($"{documentType}.{version}.prompt.md"));

    private static string ReadResource(string fileName)
    {
        var assembly = typeof(ExtractionSchemas).Assembly;
        var name = assembly.GetManifestResourceNames().SingleOrDefault(n => n.EndsWith($".{fileName}", StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"Recurso de extração não encontrado: {fileName}.");

        using var stream = assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}

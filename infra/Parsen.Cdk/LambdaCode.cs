using Amazon.CDK;
using Amazon.CDK.AWS.Lambda;

namespace Parsen.Cdk;

/// <summary>
/// Empacota projetos .NET como código de Lambda. O CDK roda o build num container Amazon Linux 2023
/// (Build/LambdaAot), copiando só os arquivos necessários para fora das pastas do Windows.
/// </summary>
internal static class LambdaCode
{
    /// <summary>Arquivos da raiz que todo build precisa (versões centrais de pacotes, SDK, fontes NuGet).</summary>
    private static readonly string[] RootFiles = ["Directory.Build.props", "Directory.Packages.props", "global.json", "nuget.config"];

    /// <summary>
    /// Native AOT para o runtime provided.al2023: o executável "bootstrap" e as bibliotecas nativas que ele
    /// carrega ao lado (ex.: libpdfium.so e libSkiaSharp.so no Ingest). Os símbolos de depuração (.dbg, .pdb) ficam de fora.
    /// </summary>
    /// <param name="projectDir">Pasta do projeto da Lambda, relativa à raiz do repositório (ex.: src/Parsen.Auth.PreTokenGeneration).</param>
    /// <param name="dependencyDirs">Pastas dos projetos referenciados por ela, no mesmo formato.</param>
    public static Code NativeAot(string projectDir, params string[] dependencyDirs) =>
        Build(projectDir, dependencyDirs,
            publish: $"dotnet publish /tmp/src/{projectDir} -c Release -r linux-x64 -o /tmp/publish",
            copyOutput: "find /tmp/publish -maxdepth 1 -type f ! -name '*.dbg' ! -name '*.pdb' -exec cp {} /asset-output/ \\;");

    /// <summary>
    /// Runtime gerenciado (dotnet10): o pacote são as DLLs do publish, executadas pelo .NET da própria Lambda.
    /// </summary>
    public static Code Managed(string projectDir, params string[] dependencyDirs) =>
        Build(projectDir, dependencyDirs,
            publish: $"dotnet publish /tmp/src/{projectDir} -c Release -r linux-x64 --self-contained false -o /tmp/publish",
            copyOutput: "cp -r /tmp/publish/. /asset-output/");

    private static AssetCode Build(string projectDir, string[] dependencyDirs, string publish, string copyOutput)
    {
        string[] dirs = [projectDir, .. dependencyDirs];
        var repoRoot = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", ".."));
        var buildImageDir = Path.Combine(Directory.GetCurrentDirectory(), "Build", "LambdaAot");

        // O container copia só o necessário para /tmp/src e compila lá: assim o publish de Linux
        // não escreve obj/ dentro das pastas do Windows (montadas em /asset-input)
        var copyDirs = string.Join(" && ", dirs.Select(d => $"mkdir -p /tmp/src/{d} && cp -r /asset-input/{d}/. /tmp/src/{d}/"));
        var script = string.Join(" && ",
            "mkdir -p /tmp/src",
            $"cp {string.Join(' ', RootFiles.Select(f => $"/asset-input/{f}"))} /tmp/src/",
            copyDirs,
            "find /tmp/src -type d \\( -name bin -o -name obj \\) -prune -exec rm -rf {} +",
            publish,
            copyOutput);

        return Code.FromAsset(repoRoot, new Amazon.CDK.AWS.S3.Assets.AssetOptions
        {
            // Hash calculado por nós: o código das pastas do projeto, os arquivos da raiz e o comando de build.
            // Mudou qualquer um deles → hash novo → build e deploy da Lambda. (Os padrões de exclusão estilo
            // .gitignore deixavam as pastas de código fora do hash: uma mudança só no C# não era implantada.)
            AssetHashType = AssetHashType.CUSTOM,
            AssetHash = SourceHash(repoRoot, dirs, script),
            Bundling = new BundlingOptions
            {
                Image = DockerImage.FromBuild(buildImageDir),
                Command = ["bash", "-c", script],

                // Por padrão o CDK usa o usuário 1000, que não existe na imagem: o .NET SDK falha ao criar
                // seus arquivos de primeiro uso. O container é descartável e só enxerga o código montado
                User = "root",
            },
        });
    }

    /// <summary>
    /// SHA-256 dos arquivos que entram no build (sem bin/ e obj/), em ordem estável, mais o comando.
    /// Inclui o caminho de cada arquivo: renomear ou mover também muda o hash.
    /// </summary>
    internal static string SourceHash(string repoRoot, IEnumerable<string> dirs, string script)
    {
        var files = RootFiles
            .Select(f => Path.Combine(repoRoot, f))
            .Concat(dirs.SelectMany(d => Directory.EnumerateFiles(Path.Combine(repoRoot, d), "*", SearchOption.AllDirectories)))
            .Select(path => (Full: path, Relative: Path.GetRelativePath(repoRoot, path).Replace('\\', '/')))
            .Where(f => !f.Relative.Split('/').Any(part => part is "bin" or "obj"))
            .OrderBy(f => f.Relative, StringComparer.Ordinal);

        using var sha = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
        foreach (var (full, relative) in files)
        {
            sha.AppendData(System.Text.Encoding.UTF8.GetBytes(relative + "\n"));
            sha.AppendData(File.ReadAllBytes(full));
        }

        sha.AppendData(System.Text.Encoding.UTF8.GetBytes(script));
        return Convert.ToHexStringLower(sha.GetHashAndReset());
    }
}

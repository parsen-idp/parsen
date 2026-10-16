using Amazon.CDK;
using Amazon.CDK.AWS.Lambda;

namespace Parsen.Cdk;

/// <summary>
/// Empacota um projeto .NET como Lambda Native AOT: o CDK roda o build num container
/// Amazon Linux 2023 (Build/LambdaAot) e usa o executável "bootstrap" como código da função.
/// </summary>
internal static class LambdaAotCode
{
    /// <summary>Arquivos da raiz que todo build precisa (versões centrais de pacotes, SDK, fontes NuGet).</summary>
    private static readonly string[] RootFiles = ["Directory.Build.props", "Directory.Packages.props", "global.json", "nuget.config"];

    /// <param name="projectDir">Pasta do projeto da Lambda, relativa à raiz do repositório (ex.: src/Parsen.Auth.PreTokenGeneration).</param>
    /// <param name="dependencyDirs">Pastas dos projetos referenciados por ela, no mesmo formato.</param>
    public static Code FromProject(string projectDir, params string[] dependencyDirs)
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
            $"dotnet publish /tmp/src/{projectDir} -c Release -r linux-x64 -o /tmp/publish",
            "cp /tmp/publish/bootstrap /asset-output/");

        return Code.FromAsset(repoRoot, new Amazon.CDK.AWS.S3.Assets.AssetOptions
        {
            // Só esses arquivos entram no hash do asset: o build só roda de novo quando eles mudam
            IgnoreMode = IgnoreMode.GIT,
            Exclude =
            [
                "*",
                .. RootFiles.Select(f => $"!/{f}"),
                .. IncludeDirs(dirs),
                "**/bin/",
                "**/obj/",
            ],
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
    /// Padrões estilo .gitignore que reincluem pastas aninhadas (ex.: src/Projeto). A ordem importa:
    /// primeiro as pastas-pai ("!/src/" e "/src/*"), depois as pastas pedidas, para que o "/src/*"
    /// de uma não volte a excluir a outra.
    /// </summary>
    private static IEnumerable<string> IncludeDirs(IEnumerable<string> dirs)
    {
        var parents = dirs
            .SelectMany(d =>
            {
                var parts = d.Split('/');
                return Enumerable.Range(1, parts.Length - 1).Select(i => string.Join('/', parts[..i]));
            })
            .Distinct()
            .OrderBy(p => p.Count(c => c == '/'));

        foreach (var parent in parents)
        {
            yield return $"!/{parent}/";
            yield return $"/{parent}/*";
        }

        foreach (var dir in dirs)
        {
            yield return $"!/{dir}/";
        }
    }
}

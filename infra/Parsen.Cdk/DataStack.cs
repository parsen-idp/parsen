using Amazon.CDK;
using Constructs;

namespace Parsen.Cdk;

/// <summary>Stack de dados: tabela DynamoDB, bucket S3 de documentos e chave KMS (passos 4b e 4c).</summary>
internal sealed class DataStack : Stack
{
    public DataStack(Construct scope, string id, IStackProps props, StageSettings settings)
        : base(scope, id, props)
    {
        _ = settings;
    }
}

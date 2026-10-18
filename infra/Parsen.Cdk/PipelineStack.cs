using Amazon.CDK;
using Amazon.CDK.AWS.Events;
using Amazon.CDK.AWS.Logs;
using Constructs;

namespace Parsen.Cdk;

/// <summary>
/// Stack do pipeline (spec, seções 4 e 5). Por enquanto só o barramento de eventos, onde a API
/// publica DossierSubmitted; o Step Functions e os workers entram no passo 6.
/// </summary>
internal sealed class PipelineStack : Stack
{
    public PipelineStack(Construct scope, string id, IStackProps props, StageSettings settings)
        : base(scope, id, props)
    {
        EventBus = new EventBus(this, "EventBus", new EventBusProps
        {
            EventBusName = $"parsen-{settings.Name}",
            Description = "Eventos de integração da Parsen (ex.: DossierSubmitted)",
        });

        // Observação: copia todo evento do barramento para o CloudWatch Logs. Os eventos só levam IDs
        // (regra 3), então o log não tem dados pessoais. Útil para ver o fluxo antes de existir o pipeline
        var eventLog = new LogGroup(this, "EventLog", new LogGroupProps
        {
            LogGroupName = $"/aws/events/parsen-{settings.Name}",
            Retention = RetentionDays.ONE_MONTH,
            RemovalPolicy = RemovalPolicy.DESTROY,
        });

        _ = new Rule(this, "LogAllEvents", new RuleProps
        {
            EventBus = EventBus,
            Description = "Registra todos os eventos do barramento no CloudWatch Logs",

            // Padrão que casa com qualquer evento da conta: o filtro mínimo exigido pelo EventBridge
            EventPattern = new EventPattern { Account = [Account] },
            Targets = [new Amazon.CDK.AWS.Events.Targets.CloudWatchLogGroup(eventLog)],
        });

        _ = new CfnOutput(this, "EventBusName", new CfnOutputProps { Value = EventBus.EventBusName });
    }

    public EventBus EventBus { get; }
}

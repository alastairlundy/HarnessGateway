using System.Reflection;
using HarnessGateway.Launcher.Core.Providers;

namespace HarnessGateway.Launcher.Tests.Providers;

public class ProtocolEmissionPlannerTests
{
    private static readonly Uri BaseUri = new("https://gw.example/v1/");

    // The table pins the consolidated mapping: each protocol kind to
    // its base-url variable and its API-key variable name, with the trailing
    // slash trimmed the way every adapter normalizes today.
    [Test]
    [Arguments(ProtocolKind.OpenAiChat, "OPENAI_BASE_URL", "OPENAI_API_KEY")]
    [Arguments(ProtocolKind.OpenAiResponses, "OPENAI_BASE_URL", "OPENAI_API_KEY")]
    [Arguments(ProtocolKind.Anthropic, "ANTHROPIC_BASE_URL", "ANTHROPIC_API_KEY")]
    public async Task Plan_maps_each_protocol_to_its_env_var_set(
        ProtocolKind protocol,
        string expectedBaseUrlVariable,
        string expectedApiKeyVariable)
    {
        IProtocolEmissionPlanner planner = new ProtocolEmissionPlanner();

        ProtocolEmission emission = planner.Plan(protocol, BaseUri);

        await Assert.That(emission.Protocol).IsEqualTo(protocol);
        await Assert.That(emission.BaseUrlVariable).IsEqualTo(expectedBaseUrlVariable);
        await Assert.That(emission.BaseUrlValue).IsEqualTo("https://gw.example/v1");

        // The API-key variable name rides in ExtraVariables: an empty
        // value is the key slot - the planner receives no key, so the
        // adapter that has the secret fills the value.
        await Assert.That(emission.ExtraVariables.ContainsKey(expectedApiKeyVariable)).IsTrue();
        await Assert.That(emission.ExtraVariables[expectedApiKeyVariable]).IsEmpty();

        // Variables of an unrelated protocol are never emitted.
        bool anthropic = protocol == ProtocolKind.Anthropic;
        string unrelatedBaseUrl = anthropic ? "OPENAI_BASE_URL" : "ANTHROPIC_BASE_URL";
        string unrelatedApiKey = anthropic ? "OPENAI_API_KEY" : "ANTHROPIC_API_KEY";
        await Assert.That(emission.BaseUrlVariable).IsNotEqualTo(unrelatedBaseUrl);
        await Assert.That(emission.ExtraVariables.ContainsKey(unrelatedApiKey)).IsFalse();
    }

    [Test]
    public async Task Plan_signature_is_two_plain_inputs_one_output_and_no_async()
    {
        MethodInfo? plan = typeof(IProtocolEmissionPlanner).GetMethod("Plan");

        await Assert.That(plan).IsNotNull();

        // The whole-result and async signatures were declined: two
        // plain values in, one emission out, synchronous.
        ParameterInfo[] parameters = plan.GetParameters();
        await Assert.That(parameters).Count().IsEqualTo(2);
        await Assert.That(parameters[0].ParameterType).IsEqualTo(typeof(ProtocolKind));
        await Assert.That(parameters[1].ParameterType).IsEqualTo(typeof(Uri));
        await Assert.That(plan.ReturnType).IsEqualTo(typeof(ProtocolEmission));
    }

    [Test]
    public async Task Plan_rejects_a_protocol_kind_outside_the_known_three()
    {
        IProtocolEmissionPlanner planner = new ProtocolEmissionPlanner();

        Exception? caught = null;
        try
        {
            planner.Plan((ProtocolKind)999, BaseUri);
        }
        catch (Exception ex)
        {
            caught = ex;
        }

        await Assert.That(caught).IsTypeOf<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task Planner_types_live_in_providers_and_never_reference_adapters()
    {
        Type[] plannerTypes =
        [
            typeof(IProtocolEmissionPlanner),
            typeof(ProtocolEmission),
            typeof(ProtocolEmissionPlanner),
        ];

        foreach (Type type in plannerTypes)
        {
            // One inward arrow into Core/Providers.
            await Assert.That(type.Namespace).IsEqualTo("HarnessGateway.Launcher.Core.Providers");

            foreach (Type referenced in ReferencedTypes(type))
            {
                await Assert.That(
                    (referenced.Namespace ?? string.Empty).StartsWith(
                        "HarnessGateway.Launcher.Core.Adapters",
                        StringComparison.Ordinal)).IsFalse();
            }
        }
    }

    private static IEnumerable<Type> ReferencedTypes(Type type)
    {
        if (type.BaseType is not null)
        {
            yield return type.BaseType;
        }

        foreach (Type implemented in type.GetInterfaces())
        {
            yield return implemented;
        }

        const BindingFlags flags =
            BindingFlags.Public
            | BindingFlags.NonPublic
            | BindingFlags.Instance
            | BindingFlags.Static
            | BindingFlags.DeclaredOnly;

        foreach (MemberInfo member in type.GetMembers(flags))
        {
            switch (member)
            {
                case MethodInfo method:
                    yield return method.ReturnType;
                    foreach (ParameterInfo parameter in method.GetParameters())
                    {
                        yield return parameter.ParameterType;
                    }

                    break;

                case PropertyInfo property:
                    yield return property.PropertyType;
                    break;

                case FieldInfo field:
                    yield return field.FieldType;
                    break;

                case EventInfo eventInfo when eventInfo.EventHandlerType is not null:
                    yield return eventInfo.EventHandlerType;
                    break;
            }
        }
    }
}

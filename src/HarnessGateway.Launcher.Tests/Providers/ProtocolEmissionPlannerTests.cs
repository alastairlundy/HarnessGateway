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

        // The API-key variable name is in ExtraVariables: an empty
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
    public async Task Plan_keeps_a_query_string_intact()
    {
        // A gated proxy endpoint carries its credential in the query; the
        // planner must hand the whole endpoint to the harness untouched.
        IProtocolEmissionPlanner planner = new ProtocolEmissionPlanner();

        ProtocolEmission emission = planner.Plan(ProtocolKind.OpenAiChat, new Uri("https://host/v1?key=abc"));

        await Assert.That(emission.BaseUrlValue).IsEqualTo("https://host/v1?key=abc");
    }

    [Test]
    public async Task Plan_does_not_mangle_a_query_ending_in_a_slash()
    {
        // Trailing-slash trimming is a path concern only: trimming the whole
        // string used to chop a query's final "/" off (https://host/v1?next=/
        // became https://host/v1?next=), corrupting it.
        IProtocolEmissionPlanner planner = new ProtocolEmissionPlanner();

        ProtocolEmission withoutPathSlash = planner.Plan(
            ProtocolKind.OpenAiChat, new Uri("https://host/v1?next=/"));
        ProtocolEmission withPathSlash = planner.Plan(
            ProtocolKind.OpenAiChat, new Uri("https://host/v1/?next=/"));

        await Assert.That(withoutPathSlash.BaseUrlValue).IsEqualTo("https://host/v1?next=/");
        await Assert.That(withPathSlash.BaseUrlValue).IsEqualTo("https://host/v1?next=/");
    }

    [Test]
    public async Task Plan_trims_the_root_slash_of_a_bare_origin()
    {
        // The bare-origin contract: https://host/ reaches the harness as
        // https://host, exactly as before.
        IProtocolEmissionPlanner planner = new ProtocolEmissionPlanner();

        ProtocolEmission emission = planner.Plan(ProtocolKind.Anthropic, new Uri("https://host/"));

        await Assert.That(emission.BaseUrlValue).IsEqualTo("https://host");
    }

    [Test]
    public async Task Plan_keeps_a_responses_suffixed_endpoint_untouched()
    {
        // The planner is a pure protocol mapping: the /responses segment
        // drop is adapter-owned (EmissionMerge.DropResponsesSegment), so a
        // base URL that already ends in the segment reaches the adapter
        // exactly as configured - the harnesses that append it themselves
        // trim it there.
        IProtocolEmissionPlanner planner = new ProtocolEmissionPlanner();

        ProtocolEmission responses = planner.Plan(
            ProtocolKind.OpenAiResponses, new Uri("https://host/v1/responses"));

        await Assert.That(responses.BaseUrlValue).IsEqualTo("https://host/v1/responses");
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
